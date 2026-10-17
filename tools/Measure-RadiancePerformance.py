"""Summarize wholly contained native Radiance workload windows.

Run with a disposable .local/pre-v01-native/<fixture-id> directory. Output is
ignored derived evidence, never a save. Percentiles remain per-window values;
markers are aligned across processes using host frame/time interpolation.
"""
from pathlib import Path
import re,json,sys,datetime
root=Path(sys.argv[1]).resolve()
allowed=Path(__file__).resolve().parent.parent/'.local/pre-v01-native'
if root.parent!=allowed.resolve():raise ValueError('Expected a repository-owned disposable fixture directory')
fixture=(root/'HollowKnight/native-menu.fixture').read_text(encoding='utf-8').splitlines()
if fixture[0]!=root.name:raise ValueError('Fixture identity does not match the evidence directory')
native=(root/'HollowKnight/native-player.log').read_text(encoding='utf-8',errors='replace')
marks={}
for kind,name,frame in re.findall(r'(?:RADIANCE|ROSTER)_PERF_WINDOW_(BEGIN|END) workload=(\S+)[^\n]*?frame=(\d+)',native):marks.setdefault(name,{})[kind]=int(frame)
setup=re.search(r'RADIANCE_PERF_SETUP workload=combat-RadianceRainPerf[^\n]*?frame=(\d+)',native)
if setup and 'END' in marks.get('rain',{}):marks['rain-including-cold-entry']={'BEGIN':int(setup.group(1)),'END':marks['rain']['END']}
def read_logs(side):
 path=next((root/'app/HollowSols/paired/HollowSols/runtime/runs'/side).glob('*/plugin.log'))
 lines=set()
 for base in [path,Path(str(path)+'.perf')]:
  for suffix in ['.startup','.2','.1','']:
   file=Path(str(base)+suffix)
   if file.exists():
    # Compact files retain every timed frame window across a long session.
    # Read only the logger's known paths; preserve overlap exactly once.
    with file.open(encoding='utf-8',errors='replace') as stream:
     for line in stream:
      if any(k in line for k in ['FRAME_PERF ','NS_VISUAL_COST','NS_ACTOR_ASYNC_COST','HK_FRAME_STAGE stage=aura','HK_FRAME_STAGE stage=contacts','HK_FRAME_STAGE stage=actor_upload','HK_FRAME_SPIKE']):lines.add(line.rstrip('\n'))
 return sorted(lines)
logs={side:read_logs(side) for side in ['HollowKnight','NineSols']}
def row(l):
 d=dict(re.findall(r'(\w+)=([^ ]+)',l));d['utc']=datetime.datetime.fromisoformat(l.split(' ')[0].replace('Z','+00:00'));return d
hk=[row(l) for l in logs['HollowKnight'] if 'FRAME_PERF ' in l and 'frame=' in l]
result={}
for name,m in marks.items():
 if 'END' not in m:continue
 # Map marker frames using nearest known frame/time. Include only wholly
 # contained two-second windows; report window percentiles, never fake an
 # aggregate percentile from already summarized data.
 nearest=lambda f:min(hk,key=lambda d:abs(int(d['frame'])-f))
 a=nearest(m['BEGIN']);b=nearest(m['END'])
 begin=a['utc']+datetime.timedelta(seconds=(m['BEGIN']-int(a['frame']))*float(a['meanMs'])/1000)
 end=b['utc']+datetime.timedelta(seconds=(m['END']-int(b['frame']))*float(b['meanMs'])/1000)
 results={}
 for side,lines in logs.items():
  rows=[row(l) for l in lines if 'FRAME_PERF ' in l and 'frame=' in l]
  boundary=[d for d in rows if begin<d['utc']<=end and d['utc']-datetime.timedelta(seconds=float(d['meanMs'])*int(d['frames'])/1000)<begin]
  first_boundary={k:boundary[0][k] for k in ['maxMs','spikes25','gc2']} if boundary else None
  rows=[d for d in rows if d['utc']<=end and d['utc']-datetime.timedelta(seconds=float(d['meanMs'])*int(d['frames'])/1000)>=begin]
  n=sum(int(d['frames']) for d in rows)
  results[side]={'windows':len(rows),'frames':n,'meanMs':sum(float(d['meanMs'])*int(d['frames']) for d in rows)/n if n else None,'worstWindowP95':max([float(d['p95Ms']) for d in rows],default=0),'maxMs':max([float(d['maxMs']) for d in rows],default=0),'spikes25':sum(int(d['spikes25']) for d in rows),'fullCollections':sum(int(d['gc2']) for d in rows),'managedMB':[float(d['managedMB']) for d in rows]}
  costs=[l for l in lines if any(k in l for k in ['NS_VISUAL_COST','NS_ACTOR_ASYNC_COST','HK_FRAME_STAGE stage=aura','HK_FRAME_STAGE stage=contacts','HK_FRAME_STAGE stage=actor_upload','HK_FRAME_SPIKE']) and begin<=datetime.datetime.fromisoformat(l.split(' ')[0].replace('Z','+00:00'))<=end]
  results[side]['costs']=costs
  results[side]['timingCoverageComplete']=len(rows)>=85
  results[side]['firstBoundaryWindow']=first_boundary
 result[name]={'begin':begin.isoformat(),'end':end.isoformat(),'frameMarkers':m,'results':results}
(root/'radiance-performance.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
for name,work in result.items():
 print(name,work['frameMarkers'])
 for side,data in work['results'].items():print(side,{k:v for k,v in data.items() if k!='costs'})
