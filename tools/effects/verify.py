"""Verify generated local cue artifacts without accessing installed games or saves."""
import argparse
import hashlib
import json
import pathlib
import sys
import wave


def verify(root):
    from PIL import Image
    root = root.resolve()
    repository = pathlib.Path(__file__).resolve().parents[2]
    if not root.is_relative_to(repository / '.local'):
        raise ValueError('Derived cue artifacts must stay under repository .local')
    catalog = json.loads((root / 'catalog.json').read_text(encoding='utf8'))
    audit = json.loads((root / 'semantic-audit.json').read_text(encoding='utf8'))
    rows = catalog['rows']
    keys = [r['key'] for r in rows]
    if len(keys) != len(set(keys)):
        raise ValueError('Duplicate selectable ID')
    if catalog['visualErrors'] or catalog['audioErrors'] or audit.get('errors'):
        raise ValueError('Catalog contains unresolved extraction/audit errors')
    files = {}
    def local(relative):
        p = (root / relative).resolve()
        if not p.is_relative_to(root) or not p.is_file():
            raise ValueError('Missing or escaping preview: ' + relative)
        files[relative] = hashlib.sha256(p.read_bytes()).hexdigest()
        return p
    previews = 0
    for row in rows:
        if not row.get('name') or not row.get('status'):
            raise ValueError('Missing entry identity/evidence tier')
        for sample in row['previews']:
            p = local(sample['path'])
            if row['category'] == 'Visual':
                with Image.open(p) as image:
                    if image.format != 'GIF' or not 1 <= image.n_frames <= sample['frames']:
                        raise ValueError('Invalid sample loop: ' + str(p))
                    for n in range(image.n_frames):
                        image.seek(n)
                        image.load()
                with Image.open(local(sample['sheet'])) as sheet:
                    sheet.load()
            else:
                with wave.open(str(p), 'rb') as audio:
                    seconds = audio.getnframes() / audio.getframerate()
                    if not 0 < seconds <= 120 or abs(seconds - sample['seconds']) > .02:
                        raise ValueError('Invalid audio duration: ' + str(p))
                    if not 1 <= audio.getnchannels() <= 8:
                        raise ValueError('Unexpected channel count')
            previews += 1
    report = {'catalogSha256': hashlib.sha256((root/'catalog.json').read_bytes()).hexdigest(),
              'build': catalog['build'], 'entries': len(rows), 'previewReferences': previews,
              'verifiedFiles': len(files), 'fileSha256': files,
              'scope': 'Local artifact integrity only; does not certify native playback or exhaustive unnamed attack coverage.'}
    (root/'verification.json').write_text(json.dumps(report, indent=2), encoding='utf8')
    print('PASS:',len(rows),'unique entries;',len(files),'valid local preview files;',previews,'preview references')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=pathlib.Path, default=pathlib.Path('.local/effect-library'))
    parser.add_argument('--dependencies', type=pathlib.Path)
    args = parser.parse_args()
    if args.dependencies:
        sys.path.insert(0, str(args.dependencies.resolve()))
    verify(args.root)
