from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parent.parent
data = json.loads((root / 'src/references/aio-b1.json').read_text(encoding='utf-8-sig'))
assert data['length'] == 180 and len(data['fields']) == 292
assert hashlib.sha256((root / 'maps/upstream/B1.yml').read_bytes()).hexdigest() == data['sourceSha256']
cross = json.loads((root / 'src/references/aio-nac-cross.json').read_text(encoding='utf-8-sig'))
assert cross['readOnly'] and cross['counts'] == {'exact': 258, 'position': 29, 'none': 5}
assert cross['legacySourceSha256'] == hashlib.sha256((root / 'src/references/nac.json').read_bytes()).hexdigest()
for file in root.rglob('*.json'):
    if '.git' not in file.parts:
        json.loads(file.read_text(encoding='utf-8-sig'))
manifest = root / 'SHA256SUMS.txt'
if manifest.exists():
    for row in manifest.read_text(encoding='utf-8').splitlines():
        digest, name = row.split('  ', 1)
        assert hashlib.sha256((root / name).read_bytes()).hexdigest() == digest, name
print('OK: 292 campos, B1 de 180 bytes, fontes, cruzamento, JSON e hashes.')
