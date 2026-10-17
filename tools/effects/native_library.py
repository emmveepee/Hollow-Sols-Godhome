"""Read-only installed-build effect inventory; all derived assets stay in .local.

Never loads a game process, save, or game managed code. UnityPy only parses data.
"""
import argparse
import bisect
import collections
import gc
import hashlib
import io
import json
import pathlib
import re
import struct
import sys
import time


class Bundle:
    def __init__(self, path):
        import lz4.block
        self.lz4 = lz4.block
        self.path = pathlib.Path(path)
        self.file = self.path.open('rb')
        self.cache = collections.OrderedDict()
        self.cache_bytes = 0
        f = self.file
        def z(stream):
            raw = bytearray()
            while (c := stream.read(1)) != b'\0':
                if not c or len(raw) > 4096:
                    raise ValueError('Invalid bundle string')
                raw.extend(c)
            return raw.decode('utf8')
        def n(stream, fmt):
            return struct.unpack('>' + fmt, stream.read(struct.calcsize('>' + fmt)))
        if z(f) != 'UnityFS':
            raise ValueError('Expected UnityFS')
        self.version = n(f, 'I')[0]
        self.player, self.engine = z(f), z(f)
        size, cs, us, flags = n(f, 'qIII')
        if size != self.path.stat().st_size:
            raise ValueError('Bundle length mismatch')
        if self.version >= 7:
            f.seek((f.tell() + 15) // 16 * 16)
        start = f.tell()
        if flags & 128:
            f.seek(size - cs)
        info = io.BytesIO(self.decode(f.read(cs), us, flags))
        info.read(16)
        self.blocks = [n(info, 'IIH') for _ in range(n(info, 'I')[0])]
        self.entries = {}
        for _ in range(n(info, 'I')[0]):
            offset, length, attr = n(info, 'qqI')
            name = z(info)
            native_path = pathlib.PurePosixPath(name.replace('\\', '/'))
            if native_path.is_absolute() or '..' in native_path.parts or ':' in name:
                raise ValueError('Unsafe bundle entry path')
            self.entries[name] = (offset, length, attr)
        pos = start if flags & 128 else start + cs
        if flags & 512:
            pos = (pos + 15) // 16 * 16
        self.compressed, self.uncompressed = [], []
        total = 0
        for u, c, _ in self.blocks:
            self.compressed.append(pos)
            self.uncompressed.append(total)
            pos += c
            total += u
        self.total = total

    def decode(self, raw, size, flags):
        mode = flags & 63
        if mode == 0:
            result = raw
        elif mode in (2, 3):
            result = self.lz4.decompress(raw, uncompressed_size=size)
        else:
            raise ValueError('Unsupported compression: ' + str(mode))
        if len(result) != size:
            raise ValueError('Decompressed size mismatch')
        return result

    def read(self, offset, length):
        if offset < 0 or length < 0 or offset + length > self.total:
            raise ValueError('Bundle range outside data')
        result = bytearray()
        while length:
            index = bisect.bisect_right(self.uncompressed, offset) - 1
            if index in self.cache:
                raw = self.cache.pop(index)
                self.cache[index] = raw
            else:
                u, c, flags = self.blocks[index]
                self.file.seek(self.compressed[index])
                raw = self.decode(self.file.read(c), u, flags)
                self.cache[index] = raw
                self.cache_bytes += len(raw)
                while self.cache_bytes > 32 * 1024 * 1024 and len(self.cache) > 1:
                    _, evicted = self.cache.popitem(last=False)
                    self.cache_bytes -= len(evicted)
            within = offset - self.uncompressed[index]
            take = min(length, len(raw) - within)
            result.extend(raw[within:within + take])
            offset += take
            length -= take
        return bytes(result)

    def entry(self, name):
        offset, size, _ = self.entries[name]
        return self.read(offset, size)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf8')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['index', 'scan', 'catalog', 'semantic'])
    p.add_argument('--game-data', required=True)
    p.add_argument('--output', required=True)
    p.add_argument('--dependencies', required=True)
    p.add_argument('--decoder', help='Pinned portable vgmstream-cli path for catalog audio previews')
    args = p.parse_args()
    sys.stdout.reconfigure(encoding='utf8')
    sys.path.insert(0, str(pathlib.Path(args.dependencies).resolve()))
    import UnityPy
    data = pathlib.Path(args.game_data).resolve()
    output = pathlib.Path(args.output).resolve()
    local = pathlib.Path(__file__).resolve().parents[2] / '.local'
    if not output.is_relative_to(local) or output == local:
        raise ValueError('Derived assets must be inside repository .local subdirectory')
    output.mkdir(parents=True, exist_ok=True)
    metadata = output / 'metadata'
    metadata.mkdir(exist_ok=True)
    bundle = Bundle(data / 'data.unity3d')
    if args.command in ('catalog', 'semantic'):
        recorded = json.loads((output / 'bundle-index.json').read_text(encoding='utf8'))['build']
        current_hash = hashlib.sha256((data / 'Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
        if (recorded['assemblySha256'] != current_hash or recorded['bundleBytes'] != bundle.path.stat().st_size
                or recorded['bundleModifiedNs'] != bundle.path.stat().st_mtime_ns):
            raise ValueError('Installed build changed: regenerate metadata index before previews')
    if args.command == 'semantic':
        from semantic import audit
        audit(data, output, bundle)
        return
    if args.command == 'catalog':
        if not args.decoder:
            p.error('catalog requires --decoder')
        from catalog import catalog
        catalog(data, output, bundle, pathlib.Path(args.decoder).resolve())
        return
    assembly = data / 'Managed' / 'Assembly-CSharp.dll'
    pin = {'assemblySha256': hashlib.sha256(assembly.read_bytes()).hexdigest(),
           'bundleBytes': bundle.path.stat().st_size,
           'bundleModifiedNs': bundle.path.stat().st_mtime_ns,
           'engine': bundle.engine, 'entries': len(bundle.entries),
           'resourcePolicy': 'metadata first; selected resources only; no saves/processes'}
    entries = [{'name': name, 'offset': e[0], 'bytes': e[1]} for name, e in bundle.entries.items()]
    write_json(output / 'bundle-index.json', {'build': pin, 'entries': entries})
    names = [name for name in bundle.entries if name.endswith('.assets') or re.fullmatch(r'level\d+|globalgamemanagers', name)]
    print('Metadata entries:', len(names), 'bytes:', sum(bundle.entries[n][1] for n in names), flush=True)
    if args.command == 'index':
        return
    # Parse one file at a time; this does not load texture/audio resource payloads.
    rows, errors, counts = [], [], collections.Counter()
    started = time.monotonic()
    for index, name in enumerate(names):
        path = metadata / name
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists() or path.stat().st_size != bundle.entries[name][1]:
            path.write_bytes(bundle.entry(name))
        try:
            env = UnityPy.load(str(path))
            for obj in env.objects:
                kind = obj.type.name
                counts[kind] += 1
                if kind not in ('GameObject', 'MonoScript', 'AnimationClip', 'Sprite', 'Texture2D', 'Material', 'AudioClip', 'AnimatorController', 'AnimatorOverrideController'):
                    continue
                try:
                    label = obj.peek_name()
                    row = {'asset': name, 'pathId': obj.path_id, 'type': kind, 'name': label}
                    if kind == 'MonoScript':
                        script = obj.read()
                        row.update(className=script.m_ClassName, namespace=script.m_Namespace, assembly=script.m_AssemblyName)
                    rows.append(row)
                except Exception as exc:
                    errors.append({'asset': name, 'pathId': obj.path_id, 'stage': 'name', 'error': str(exc)})
            del env
            gc.collect()
        except Exception as exc:
            errors.append({'asset': name, 'stage': 'parse', 'error': str(exc)})
        if index % 10 == 0:
            print('Indexed', index + 1, '/', len(names), name, 'objects', sum(counts.values()), flush=True)
    write_json(output / 'object-index.json', {'build': pin, 'files': names, 'objects': rows,
                                              'typeCounts': dict(counts), 'errors': errors,
                                              'elapsedSeconds': time.monotonic() - started})
    print('Finished', len(rows), 'named objects; errors', len(errors), flush=True)


if __name__ == '__main__':
    main()
