"""Attribute completed Hall samples using retained, scoped evidence only.

Usage: python tools/Analyze-HallGarbage.py
Results stay in ignored .local. Heap deltas are process-wide growth, not
allocation stacks. Nested stage brackets must not be summed together.
"""
from pathlib import Path
import collections
import json
import re
import statistics

root = Path(__file__).resolve().parent.parent
rows = json.loads((root / '.local/hall-endurance/completed-global-summary.json').read_text())
derived = []
for fixture in dict.fromkeys(r['fixture'] for r in rows):
    folder = root / '.local/pre-v01-native' / fixture
    identity = (folder / 'HollowKnight/native-menu.fixture').read_text().splitlines()[0]
    if identity != fixture:
        raise ValueError('Evidence fixture identity mismatch')
    native = (folder / 'HollowKnight/native-player.log').read_text(errors='replace')
    paths = list((folder / 'app/HollowSols/paired/HollowSols/runtime/runs/HollowKnight').glob('*/plugin.log'))
    if len(paths) != 1:
        raise ValueError('Expected one authenticated HK runtime')
    lines = set()
    for suffix in ['.startup', '.2', '.1', '']:
        path = Path(str(paths[0]) + suffix)
        if path.exists():
            with path.open(errors='replace') as stream:
                for line in stream:
                    if any(tag in line for tag in ['HK_FRAME_STAGE ', 'HK_PIXEL_POOL ']):
                        lines.add(line)
    for row in (r for r in rows if r['fixture'] == fixture):
        name = row['boss']
        metrics = row['performance']['results']['HollowKnight']
        heap = metrics['managedMB']
        increases = [(b-a)/2 for a, b in zip(heap, heap[1:]) if b >= a]
        # Two-second summaries approximate growth. Collection-containing deltas
        # are excluded; drops cannot reconstruct bytes allocated during a GC.
        calls = re.findall(r'ROSTER_PERF_WINDOW_END workload=' + re.escape(name) + r'\s[^\n]*', native)
        if len(calls) != 1:
            raise ValueError('Missing/ambiguous exact collection receipt: ' + name)
        exact = int(re.search(r'nativeCollections=(\d+)', calls[0]).group(1))
        begin, end = (row['performance']['frameMarkers'][k] for k in ['BEGIN', 'END'])
        stages = collections.defaultdict(lambda: [0, 0])
        for line in lines:
            if 'HK_FRAME_STAGE ' not in line: continue
            values = dict(re.findall(r'(\w+)=([^ ]+)', line))
            if begin <= int(values['frame']) <= end and int(values.get('heapSamples', 0)):
                tally = stages[values['stage']]
                tally[0] += int(values['sampledProcessHeapDeltaBytes'])
                tally[1] += int(values['heapSamples'])
        result = dict(boss=name, fixture=fixture, exactCombatCollections=exact,
            firstContainedHeapMiB=heap[0], lastContainedHeapMiB=heap[-1],
            medianPositiveGrowthMiBPerSecond=statistics.median(increases),
            p90PositiveGrowthMiBPerSecond=sorted(increases)[int(len(increases)*.9)],
            measuredStageGrowthBytesPerSample={k: v[0]/v[1] for k, v in stages.items()},
            stageSamples={k: v[1] for k, v in stages.items()})
        actor = []
        for line in lines:
            if 'HK_PIXEL_POOL ' not in line: continue
            values = dict(re.findall(r'(\w+)=([^ ]+)', line))
            if values['channel'] == 'actor' and int(values['frame']) <= end:
                actor.append((int(values['frame']), int(values['created'])))
        actor.sort()
        before = [v for v in actor if v[0] <= begin]
        during = [v for v in actor if begin <= v[0] <= end]
        if during:
            baselineComplete = bool(before) and begin-before[-1][0] <= 432
            baseline = before[-1] if baselineComplete else during[0]
            result['actorPool'] = dict(baselineFrame=baseline[0], baselineCreated=baseline[1],
                lastFrame=during[-1][0], lastCreated=during[-1][1],
                newBuffers=during[-1][1]-baseline[1], completeBaseline=baselineComplete)
        else: result['actorPool'] = None
        # The compact file preserves timing after verbose logs rotate away.
        timing = set()
        for suffix in ['.startup', '.2', '.1', '']:
            path = Path(str(paths[0])+'.perf'+suffix)
            if path.exists():
                with path.open(errors='replace') as stream:
                    for line in stream:
                        if 'FRAME_PERF ' in line: timing.add(line)
        windows = []
        for line in sorted(timing):
            values = dict(re.findall(r'(\w+)=([^ ]+)', line))
            if begin <= int(values['frame']) <= end: windows.append(values)
        bursts = []
        for previous, current in zip(windows, windows[1:]):
            change = float(current['managedMB'])-float(previous['managedMB'])
            if change < 20: continue
            a, b = int(previous['frame']), int(current['frame'])
            growth = [(f,c) for f,c in actor if a < f <= b]
            prior = [c for f,c in actor if f <= a]
            new = growth[-1][1]-prior[-1] if growth and prior else None
            bursts.append(dict(frame=b, heapGrowthMiB=change, actorNewBuffers=new))
        result['bursts20MiB'] = bursts
        derived.append(result)
        print(f"{name:23} gc={exact} firstMiB={heap[0]:.1f} medianMiB/s={result['medianPositiveGrowthMiBPerSecond']:.2f}")
for failed in [False, True]:
    selected = [r for r in derived if bool(r['exactCombatCollections']) == failed]
    print('combatGC=' + str(failed), 'cases=', len(selected),
        'median firstMiB=', round(statistics.median(r['firstContainedHeapMiB'] for r in selected), 2),
        'median growthMiB/s=', round(statistics.median(r['medianPositiveGrowthMiBPerSecond'] for r in selected), 2))
for result in derived:
    if result['actorPool']:
        print('ACTOR', result['boss'], result['actorPool'], 'bursts=', len(result['bursts20MiB']), 'withNewBuffers=', sum(bool(v['actorNewBuffers']) for v in result['bursts20MiB']))
(root / '.local/hall-endurance/gc-attribution.json').write_text(json.dumps(derived, indent=2))
