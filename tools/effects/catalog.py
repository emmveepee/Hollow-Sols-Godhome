"""Offline native cue catalog and asset previews. Never executes game assemblies.

Preview strips use native sprite samples, not reconstructed Unity animation timing.
Game-derived output is restricted to .local by native_library's entry point.
"""
import collections
import gc
import hashlib
import html
import io
import json
import pathlib
import re
import struct
import subprocess
import wave
import xml.etree.ElementTree as ET


def slug(value):
    text = re.sub(r'[^a-z0-9]+', '-', value.lower()).strip('-')
    return text or 'unnamed'


class Resource(io.RawIOBase):
    """Seekable view of one bundle entry; never materializes the resource archive."""
    def __init__(self, bundle, entry):
        self.bundle = bundle
        self.offset, self.length, _ = bundle.entries[entry]
        self.position = 0

    def seek(self, offset, whence=0):
        position = offset + (self.position if whence == 1 else self.length if whence == 2 else 0)
        if not 0 <= position <= self.length:
            raise ValueError('Resource seek outside entry')
        self.position = position
        return position

    def tell(self):
        return self.position

    def read(self, size=-1):
        size = self.length - self.position if size < 0 else min(size, self.length - self.position)
        data = self.bundle.read(self.offset + self.position, size)
        self.position += size
        return data


class Assets:
    def __init__(self, output, bundle):
        import UnityPy
        from UnityPy.streams import EndianBinaryReader
        self.env = UnityPy.Environment()
        self.output, self.bundle = output, bundle
        self.files = {}
        for name in bundle.entries:
            if name.endswith(('.resS', '.resource')):
                self.env.register_cab(pathlib.PurePosixPath(name).name,
                                      EndianBinaryReader(Resource(bundle, name)))

    def load(self, name):
        name = pathlib.PurePosixPath(name).name
        if name not in self.files:
            f = next((value for value in self.env.files.values() if getattr(value, 'name', None) == name), None)
            if f is None:
                path = self.output / 'metadata' / name
                if not path.exists():
                    entry = next((n for n in self.bundle.entries if pathlib.PurePosixPath(n).name == name), None)
                    if entry:
                        path.write_bytes(self.bundle.entry(entry))
                    elif name == 'unity default resources':
                        path.write_bytes((self.bundle.path.parent / 'Resources' / name).read_bytes())
                f = self.env.load_file(str(path))
            if f is None:
                raise ValueError('Dependency not loaded: ' + name)
            self.files[name] = f
        return self.files[name]

    def ref(self, owner, pointer):
        fid, pid = pointer.get('m_FileID', 0), pointer.get('m_PathID', 0)
        if not pid:
            return None
        f = owner.assets_file
        if fid:
            f = self.load(f.externals[fid - 1].path)
        return f.objects.get(pid)


def pointers(value):
    if isinstance(value, dict):
        if 'm_FileID' in value and 'm_PathID' in value:
            yield value
        else:
            for child in value.values():
                yield from pointers(child)
    elif isinstance(value, list):
        for child in value:
            yield from pointers(child)


def audio_catalog(data, output, decoder):
    root = data / 'StreamingAssets/Audio/GeneratedSoundBanks/Windows'
    source = root / 'SoundbanksInfo.xml'
    tree = ET.parse(source)
    events, media = {}, collections.defaultdict(list)
    for bank in tree.findall('.//SoundBank'):
        name = bank.findtext('Path')
        for file in bank.findall('./Media/File'):
            media[file.attrib['Id']].append({'bank': name, 'name': file.findtext('ShortName'),
                                           'streaming': file.attrib.get('Streaming')})
        for event in bank.findall('./Events/Event'):
            eid = event.attrib['Id']
            row = events.setdefault(eid, {'id': eid, 'name': event.attrib['Name'], 'banks': [], 'media': []})
            row['banks'].append(name)
            row['media'] = sorted(set(row['media'] + [r.attrib['Id'] for r in event.findall('.//MediaRef')]))
    (output / 'audio-index.json').write_text(json.dumps({'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
        'events': list(events.values()), 'media': dict(media)}, ensure_ascii=False, indent=2), encoding='utf8')
    selected = [r for r in events.values() if re.search(r'danger|taichi|weakness|unbound', r['name'], re.I)]
    bank_cache = {}
    audio_dir = output / 'audio'
    audio_dir.mkdir(exist_ok=True)
    errors = []
    for row in selected:
        row.update(key='sfx-' + slug(row['name']) + '-' + row['id'], category='Sound', previews=[],
                   status='Authored event; raw samples', note='Samples omit Wwise bus gain, random selection, layering and runtime spatialization.')
        for mid in row['media']:
            try:
                wem, wav = audio_dir / (mid + '.wem'), audio_dir / (mid + '.wav')
                if not wem.exists():
                    found = False
                    for location in media[mid]:
                        bank = location['bank']
                        if bank not in bank_cache:
                            raw = (root / bank).read_bytes()
                            chunks, pos = {}, 0
                            while pos + 8 <= len(raw):
                                kind, length = struct.unpack_from('<4sI', raw, pos)
                                chunks[kind] = raw[pos + 8:pos + 8 + length]
                                pos += 8 + length
                            entries = {str(i): (off, size) for i, off, size in struct.iter_unpack('<III', chunks.get(b'DIDX', b''))}
                            bank_cache[bank] = (entries, chunks.get(b'DATA', b''))
                        entries, payload = bank_cache[bank]
                        if mid in entries:
                            off, size = entries[mid]
                            if off + size > len(payload):
                                raise ValueError('Bank media outside DATA')
                            wem.write_bytes(payload[off:off + size])
                            found = True
                            break
                    if not found:
                        candidates = [root / 'Media' / (mid + '.wem'), root / (mid + '.wem')]
                        external = next((p for p in candidates if p.is_file()), None)
                        if external is None:
                            raise ValueError('Media not present in bank or external media')
                        wem.write_bytes(external.read_bytes())
                if not wav.exists():
                    result = subprocess.run([str(decoder), '-i', '-o', str(wav), str(wem)],
                                            capture_output=True, timeout=30)
                    if result.returncode:
                        raise ValueError(result.stderr.decode('utf8', errors='replace')[:500])
                with wave.open(str(wav), 'rb') as w:
                    duration = w.getnframes() / w.getframerate()
                    if not 0 < duration < 120:
                        raise ValueError('Unexpected sample duration')
                    row['previews'].append({'path': 'audio/' + wav.name, 'mediaId': mid,
                        'name': media[mid][0]['name'], 'seconds': duration, 'channels': w.getnchannels(),
                        'rate': w.getframerate(), 'sha256': hashlib.sha256(wav.read_bytes()).hexdigest()})
            except Exception as exc:
                errors.append({'event': row['id'], 'media': mid, 'error': str(exc)})
    return selected, errors


def catalog(data, output, bundle, decoder):
    from PIL import Image, ImageDraw
    inventory = json.loads((output / 'object-index.json').read_text(encoding='utf8'))
    candidates = [r for r in inventory['objects'] if r['type'] == 'AnimationClip' and
                  re.search(r'danger|weakness|taichi|crimson', r['name'], re.I)]
    # Keep name discovery separate from confirmed emitter ownership.
    emitters = [r for r in inventory['objects'] if r['type'] == 'GameObject' and
                re.search(r'dangerhint|weaknesshint|weaknessfx|effect_taidanger', r['name'], re.I)]
    del inventory['objects']
    assets = Assets(output, bundle)
    output.joinpath('visual').mkdir(exist_ok=True)
    rows, errors, contexts = [], [], []
    sprite_cache = {}
    for index, row in enumerate(candidates):
        if re.search(r'door|healthbar|stealth|confuse|dead|engag|idle|seat|standup|turnaround|posture|fooattached', row['name'], re.I):
            continue
        key = 'vfx-' + slug(row['name']) + '-' + hashlib.sha256((row['asset'] + ':' + str(row['pathId'])).encode()).hexdigest()[:8]
        entry = dict(row, key=key, category='Visual', status='Discovered attack clip', previews=[],
                     note='Asset samples only. Unity transforms, shader/particle animation and authored timing are not reconstructed.')
        try:
            obj = assets.load(row['asset']).objects[row['pathId']]
            tree = obj.read_typetree()
            entry['sampleRate'] = tree.get('m_SampleRate')
            entry['duration'] = tree.get('m_MuscleClip', {}).get('m_StopTime')
            entry['bindings'] = tree.get('m_ClipBindingConstant', {}).get('genericBindings', [])
            sprites = {}
            for pointer in pointers(tree):
                ref = assets.ref(obj, pointer)
                if ref and ref.type.name == 'Sprite':
                    sprites[(ref.assets_file.name, ref.path_id)] = ref
            entry['spriteReferences'] = [{'asset': s.assets_file.name, 'pathId': s.path_id, 'name': s.peek_name()} for s in sprites.values()]
            # A body frame is not an isolated VFX. Select only explicitly named effect artwork.
            art = [s for s in sprites.values() if re.search(r'effect|vfx|fx_|fooexplode', s.peek_name(), re.I)]
            groups = collections.defaultdict(list)
            for s in art:
                prefix = re.sub(r'\d+$', '', s.peek_name())
                groups[prefix].append(s)
            for group, samples in groups.items():
                def frame_key(s):
                    match = re.search(r'(\d+)$', s.peek_name())
                    return int(match.group(1)) if match else 0
                samples.sort(key=frame_key)
                frames = []
                for s in samples:
                    address = (s.assets_file.name, s.path_id)
                    if address not in sprite_cache:
                        image = s.read().image.convert('RGBA')
                        if image.width > 8192 or image.height > 8192:
                            raise ValueError('Unexpected sprite size')
                        sprite_cache[address] = image
                    frames.append(sprite_cache[address])
                if not frames:
                    continue
                width, height = max(f.width for f in frames), max(f.height for f in frames)
                thumb_frames = []
                for frame in frames:
                    canvas = Image.new('RGB', (width + 24, height + 24), '#242a32')
                    canvas.paste(frame, ((canvas.width - frame.width)//2, (canvas.height - frame.height)//2), frame)
                    canvas.thumbnail((420, 320))
                    thumb_frames.append(canvas)
                name = key + '-' + slug(group)
                path = output / 'visual' / (name + '.gif')
                thumb_frames[0].save(path, save_all=True, append_images=thumb_frames[1:], duration=100, loop=0)
                sheet = Image.new('RGB', (160 * min(6, len(frames)), 150 * ((len(frames) + 5)//6)), '#242a32')
                draw = ImageDraw.Draw(sheet)
                for n, frame in enumerate(frames):
                    small = frame.copy()
                    small.thumbnail((150, 120))
                    x, y = n % 6 * 160, n // 6 * 150
                    sheet.paste(small, (x + (160-small.width)//2, y), small)
                    draw.text((x+5,y+125), str(n), fill='white')
                sheet.save(output / 'visual' / (name + '.png'))
                entry['previews'].append({'path': 'visual/' + path.name, 'sheet': 'visual/' + name + '.png',
                                           'name': group, 'frames': len(frames), 'kind': 'Native sprite sample loop (10 samples/s; not game timing)'})
            if entry['previews']:
                entry['status'] = 'Native sprite samples isolated'
            rows.append(entry)
        except Exception as exc:
            errors.append({'asset': row['asset'], 'pathId': row['pathId'], 'error': str(exc)})
            entry['error'] = str(exc)
            rows.append(entry)
        if index % 10 == 0:
            print('Visual candidates', index+1, '/', len(candidates), 'previews', sum(len(r['previews']) for r in rows), flush=True)
    # Resolve actual hierarchy and component identities for every named warning emitter.
    for file_name, group in __import__('itertools').groupby(sorted(emitters, key=lambda r:r['asset']), key=lambda r:r['asset']):
        f = assets.load(file_name)
        for row in group:
            try:
                obj = f.objects[row['pathId']]
                d = obj.read()
                components, parents = [], []
                for c in d.m_Component:
                    ob = assets.ref(obj, {'m_FileID': c.component.file_id, 'm_PathID': c.component.path_id})
                    if not ob:
                        continue
                    cd = ob.read(check_read=False)
                    kind = ob.type.name
                    if kind == 'MonoBehaviour':
                        sr = assets.ref(ob, {'m_FileID': cd.m_Script.file_id, 'm_PathID': cd.m_Script.path_id})
                        kind = sr.read().m_ClassName if sr else 'Unresolved script'
                    if kind == 'Transform':
                        tr = cd
                        visited = set()
                        while tr.m_Father.path_id and tr.m_Father.path_id not in visited:
                            visited.add(tr.m_Father.path_id)
                            parent = assets.ref(ob, {'m_FileID':tr.m_Father.file_id, 'm_PathID':tr.m_Father.path_id})
                            tr = parent.read()
                            go = assets.ref(parent, {'m_FileID':tr.m_GameObject.file_id, 'm_PathID':tr.m_GameObject.path_id})
                            parents.append(go.peek_name())
                    components.append({'class': kind, 'pathId': ob.path_id})
                contexts.append(dict(row, parents=parents, components=components))
            except Exception as exc:
                errors.append({'asset':row['asset'], 'pathId':row['pathId'], 'stage':'hierarchy', 'error':str(exc)})
        print('Emitter hierarchy', file_name, 'contexts', len(contexts), flush=True)
    sounds, audio_errors = audio_catalog(data, output, decoder)
    result = {'build': inventory['build'], 'rows': rows + sounds, 'emitters': contexts,
              'visualErrors': errors, 'audioErrors': audio_errors,
              'coverage': {'metadataFiles':len(inventory['files']), 'namedClipCandidates':len(candidates),
                  'warningEmitterCandidates':len(emitters), 'resolvedEmitters':len(contexts),
                  'visualEntries':len(rows), 'visualPreviews':sum(len(r['previews']) for r in rows),
                  'soundEvents':len(sounds), 'soundSamples':len({p['mediaId'] for r in sounds for p in r['previews']})},
              'limits': ['Discovery is name-led, not an exhaustive semantic attack-policy audit.',
                  'Optimized Unity clips may include only body sprites; those are not presented as isolated effects.',
                  'A source preview does not prove canvas initialization or synchronization.',
                  'No native game was launched; raw audio is not a live mixed event capture.']}
    (output / 'catalog.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf8')
    gallery(output, result)
    print(json.dumps(result['coverage']), 'visual errors', len(errors), 'audio errors',len(audio_errors), flush=True)


def gallery(output, result):
    content = json.dumps(result, ensure_ascii=False).replace('<', '\\u003c')
    page = '''<!doctype html><meta charset="utf-8"><title>Nine Sols native cue library</title>
<style>body{margin:30px;background:#141920;color:#e0e9ed;font:16px system-ui}h1{color:#77dfd6}input,select,button{font:inherit;padding:10px;background:#27333d;color:white;border:1px solid #54747a;border-radius:6px}input{width:55%}#cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(390px,1fr));gap:18px;margin-top:25px}.card{background:#202832;border-radius:10px;padding:18px;overflow-wrap:anywhere}.card img{max-width:100%;display:block;margin:12px auto}.key{color:#77dfd6;font-family:monospace}audio{width:100%}small{color:#b6c7cd}summary{cursor:pointer}pre{white-space:pre-wrap;font-size:12px}.notice{max-width:1100px;line-height:1.6}a{color:#77dfd6}</style>
<h1>Nine Sols native cue library</h1><p class="notice">Copy an ID when requesting an effect. Visual loops show isolated native sprite samples at a browsing speed of 10 samples/second. They do <b>not</b> reproduce Unity attack timing, particles or shader animation. Audio is decoded native source media; Wwise's live mix can differ. Missing previews remain listed. None of these new entries is automatically admitted to the canvas.</p>
<input id="q" placeholder="Search JieChuan, GouMang, danger, weakness…"><select id="kind"><option>All</option><option>Visual</option><option>Sound</option><option>Native cue</option></select><p id="count"></p><details><summary>Build, coverage and limitations</summary><pre id="coverage"></pre></details><div id="cards"></div>
<script>const data=CATALOG;const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
coverage.textContent=JSON.stringify({build:data.build,coverage:data.coverage,limits:data.limits,visualErrors:data.visualErrors,audioErrors:data.audioErrors},null,2);
function render(){const search=q.value.toLowerCase();const rows=data.rows.filter(r=>(kind.value==='All'||r.category===kind.value)&&JSON.stringify(r).toLowerCase().includes(search));count.textContent=rows.length+' entries';cards.innerHTML=rows.map(r=>'<article class="card"><h2>'+escape(r.name)+'</h2><p class="key">'+escape(r.key)+'</p><button data-copy="'+escape(r.key)+'">Copy ID</button><p>'+escape(r.status)+'</p><small>'+escape(r.note)+'</small>'+r.previews.map(p=>r.category==='Visual'?'<h3>'+escape(p.name)+'</h3><img loading="lazy" src="'+escape(p.path)+'"><small>'+escape(p.kind)+' · '+p.frames+' samples</small><p><a href="'+escape(p.sheet)+'">All samples</a></p>':'<p>'+escape(p.name)+' · '+p.seconds.toFixed(2)+' seconds</p><audio controls preload="none" src="'+escape(p.path)+'"></audio>').join('')+'<details><summary>Native addresses and dependencies</summary><pre>'+escape(JSON.stringify(r,null,2))+'</pre></details></article>').join('');}
q.addEventListener('input',render);kind.addEventListener('change',render);cards.addEventListener('click',async e=>{if(e.target.dataset.copy){try{await navigator.clipboard.writeText(e.target.dataset.copy);e.target.textContent='Copied';}catch{prompt('Copy this ID',e.target.dataset.copy);}}});render();</script>'''.replace('CATALOG', content)
    (output / 'index.html').write_text(page, encoding='utf8')
