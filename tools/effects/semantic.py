"""Installed-build-specific offline emitter audit; no game code execution.

Field prefixes verified against Cecil serialization metadata for the pinned build.
Private AutoParent/AutoChildren fields lack SerializeField and are not serialized.
"""
import collections
import gc
import hashlib
import json
import pathlib
import struct
from catalog import Assets, slug, gallery

PIN = '51f2921c61e237d7fc6a38f31de2c8bea8dd5826cf381d2fb8033b3c66afd743'


def audit(data, output, bundle):
    assembly = data / 'Managed/Assembly-CSharp.dll'
    if hashlib.sha256(assembly.read_bytes()).hexdigest() != PIN:
        raise ValueError('Emitter prefix schema requires reviewed installed assembly')
    index = json.loads((output / 'bundle-index.json').read_text(encoding='utf8'))
    names = [r['name'] for r in index['entries'] if r['name'].endswith('.assets') or
             __import__('re').fullmatch(r'level\d+', r['name'])]
    counts, errors, contexts = collections.Counter(), [], []
    total = 0
    def pointer(raw, offset):
        fid, pid = struct.unpack_from('<iq', raw, offset)
        return {'m_FileID':fid, 'm_PathID':pid}
    for number, name in enumerate(names):
        assets = Assets(output, bundle)
        f = assets.load(name)
        scripts, transforms = {}, {}
        def identity(ob):
            ob.reset()
            header = ob.reader.read_bytes(32)
            script = pointer(header, 16)
            address = (ob.assets_file.name, script['m_FileID'], script['m_PathID'])
            if address not in scripts:
                sr = assets.ref(ob, script)
                scripts[address] = sr.read().m_ClassName if sr else 'MissingScript'
            return scripts[address]
        def hierarchy(go):
            parents = []
            gd = go.read()
            tr = None
            for c in gd.m_Component:
                ob = assets.ref(go, {'m_FileID':c.component.file_id,'m_PathID':c.component.path_id})
                if ob and ob.type.name == 'Transform':
                    tr = ob.read()
                    break
            seen = set()
            while tr and tr.m_Father.path_id:
                address = (tr.m_Father.file_id, tr.m_Father.path_id)
                if address in seen:
                    raise ValueError('Transform cycle')
                seen.add(address)
                ob = assets.ref(go, {'m_FileID':address[0],'m_PathID':address[1]})
                tr = ob.read()
                pg = assets.ref(ob, {'m_FileID':tr.m_GameObject.file_id,'m_PathID':tr.m_GameObject.path_id})
                parents.append(pg.peek_name())
            return parents
        def describe(ob):
            if not ob:
                return None
            result = {'asset':ob.assets_file.name,'pathId':ob.path_id,'type':ob.type.name}
            if ob.type.name == 'MonoBehaviour':
                result['class'] = identity(ob)
                ob.reset()
                go = assets.ref(ob, pointer(ob.reader.read_bytes(12), 0))
                result['gameObject'] = {'asset':go.assets_file.name,'pathId':go.path_id,'name':go.peek_name()} if go else None
            else:
                result['name'] = ob.peek_name()
            return result
        for ob in list(f.objects.values()):
            if ob.type.name != 'MonoBehaviour':
                continue
            total += 1
            try:
                kind = identity(ob)
                counts[kind] += 1
                if kind not in ('FxPlayer', 'WeaknessEffectManager', 'WeaknessActivator', 'SoundPlayer'):
                    continue
                ob.reset()
                raw = ob.reader.read_bytes(min(ob.byte_size, 80))
                go = assets.ref(ob, pointer(raw,0))
                row = {'asset':name,'pathId':ob.path_id,'class':kind,'name':go.peek_name() if go else ''}
                if kind == 'FxPlayer':
                    pool = assets.ref(ob,pointer(raw,32))
                    if not pool:
                        continue
                    desc = describe(pool)
                    pname = (desc.get('gameObject') or {}).get('name','')
                    if 'taidanger' not in pname.lower() and 'weakness' not in pname.lower():
                        continue
                    row['pool'] = desc
                    row['sound'] = describe(assets.ref(ob,pointer(raw,44)))
                elif kind == 'SoundPlayer':
                    length = struct.unpack_from('<i', raw,32)[0]
                    if not 0 <= length <= 1024:
                        raise ValueError('Invalid serialized SoundPlayer string')
                    ob.reset()
                    full = ob.reader.read_bytes(36 + length)
                    row['event'] = full[36:36+length].decode('utf8')
                    if not any(k in row['event'].lower() for k in ('danger','weakness','taichi','unbound')):
                        continue
                elif kind == 'WeaknessEffectManager':
                    row['nativeOwners'] = [describe(assets.ref(ob,pointer(raw,off))) for off in (32,44,56)]
                else:
                    row['role'] = 'Native Tai-Chi activation; not an independent VFX prefab'
                row['parents'] = hierarchy(go) if go else []
                contexts.append(row)
            except Exception as exc:
                errors.append({'asset':name,'pathId':ob.path_id,'error':str(exc)})
                if len(errors) <= 3:
                    print('Schema diagnostic', errors[-1], flush=True)
        print('Semantic',number+1,'/',len(names), name, 'MonoBehaviours',total,'contexts',len(contexts),'errors',len(errors),flush=True)
        del assets, f
        gc.collect()
    report = {'assemblySha256':PIN,'objects':total,'classes':dict(counts),'contexts':contexts,'errors':errors,
              'schema':'MonoBehaviour32-byte head; FxPlayer public PoolObject@32/SoundPlayer@44; SoundPlayer string@32; WeaknessEffectManager three public PPtrs@32/44/56'}
    (output / 'semantic-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    catalog = json.loads((output / 'catalog.json').read_text(encoding='utf8'))
    catalog['semanticAudit'] = {'objects':total,'contexts':len(contexts),'errors':len(errors),'classes':dict(counts)}
    catalog['rows'] = [r for r in catalog['rows'] if r.get('category') != 'Native cue']
    unique = {}
    for row in contexts:
        if row['class'] == 'FxPlayer':
            pool = row['pool']
            addr = (pool['asset'],pool['pathId'])
            item = unique.setdefault(addr, {'key':'cue-'+slug(pool['gameObject']['name'])+'-'+hashlib.sha256(str(addr).encode()).hexdigest()[:8],
                'name':pool['gameObject']['name'],'category':'Native cue','status':'Verified serialized native emitter ownership',
                'note':'Native pooled warning. Sprite-only samples cannot reproduce its particle/material timing. Import readiness requires a canvas lease test.',
                'previews':[],'pool':pool,'emitters':[]})
            item['emitters'].append(row)
        elif row['class'] == 'WeaknessEffectManager':
            owners = row['nativeOwners']
            # The first field is a per-enemy SpriteFlasher, not a different visual template.
            addr = json.dumps(owners[1:],sort_keys=True)
            item = unique.setdefault(('weakness',addr), {'key':'cue-taichi-outline-'+hashlib.sha256(addr.encode()).hexdigest()[:8],
                'name':'Tai-Chi native outline / afterimage owner','category':'Native cue','status':'Verified native weakness presentation owners',
                'note':'Native outline is shader/actor dependent, not a standalone sprite animation. Preserve actor renderer/material and pooled afterimage contracts.',
                'previews':[],'owners':owners,'emitters':[]})
            item['emitters'].append(row)
    catalog['rows'].extend(unique.values())
    catalog['limits'][0] = 'All serialized MonoBehaviours audited for native warning pools, weakness presentation owners and kick activation. Enemy-specific unnamed artwork remains a separate discovery problem.'
    catalog['coverage']['nativeCueGraphs'] = len(unique)
    (output / 'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
    gallery(output,catalog)
