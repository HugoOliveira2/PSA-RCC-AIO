"""Convert the downloaded PSA-RE data; never opens serial ports."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
import yaml

src = ROOT / 'maps/upstream'
raw = (src / 'B1.yml').read_bytes()
data = yaml.safe_load(raw)
ecu = yaml.safe_load((src / 'ecu.yml').read_text(encoding='utf-8'))
fields = []
for name, field in data['params'].items():
    if field.get('unused'):
        continue
    choices = {str(k): v.get('pt', v.get('en', '')) for k, v in field.get('values', {}).items()}
    fields.append(dict(name=name, bits=field['bits'], type=field.get('type', 'uint'),
                       description=field.get('comment', {}).get('pt', field.get('comment', {}).get('en', name)),
                       choices=choices, factor=field.get('factor', 1), offset=field.get('offset', 0)))
alternatives = []
for alternative in data.get('alternatives', []):
    differing = []
    for name, field in data['params'].items():
        other = alternative.get('params', {}).get(name)
        if not field.get('unused') and other and any(field.get(k) != other.get(k) for k in ('bits', 'type', 'values')):
            differing.append(name)
    alternatives.append(dict(note=alternative.get('note', {}).get('en', ''), differing=differing,
                             extra=[k for k in alternative.get('params', {}) if k not in data['params']]))
out = dict(schema='aio-b1-map-v1', source='https://github.com/prototux/PSA-RE',
           sourcePath='diag/AEE2010.eco/BTEL/AIO/lids/B1.yml', sourceSha256=hashlib.sha256(raw).hexdigest(),
           length=data['length'], fields=fields, variants=ecu['variants'], alternatives=alternatives)
target = ROOT / 'src/references/aio-b1.json'
target.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'{target}: {len(fields)} fields; alternatives differ in {sum(len(x["differing"]) for x in alternatives)} fields')
