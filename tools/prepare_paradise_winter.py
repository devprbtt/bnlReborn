"""Derive a winter appearance variant, preserving every gameplay field."""
import copy, hashlib, json, shutil, zlib
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = root / 'Maps/map_sr2_paradise.bnlbin'
stage = root / 'extracted/winter-inputs'
stage.mkdir(parents=True, exist_ok=True)
saved = stage / source.name
raw = source.read_bytes()
if saved.exists():
    assert saved.read_bytes() == raw, 'Staged source differs'
else:
    shutil.copyfile(source, saved)
original = json.loads(zlib.decompress(saved.read_bytes()))
variant = copy.deepcopy(original)
variant['name'] = 'Paradise - Winter'
variant['description'] = 'Snow-covered Paradise. Snowfall can be toggled in Graphics settings. Two Cubes and a Base.'
variant['map_id'] = 'map_sr2_paradise_winter'
assert original['map']['properties']['render'] == 'DaytimeWarm'
variant['map']['properties']['render'] = 'DaytimeWarm_winter'
comparison = copy.deepcopy(variant['map'])
comparison['properties']['render'] = original['map']['properties']['render']
assert comparison == original['map'], 'Gameplay data changed'
encoded = zlib.compress(json.dumps(variant, separators=(',', ':'), ensure_ascii=False).encode(), 9)
assert json.loads(zlib.decompress(encoded)) == variant
destination = root / 'Maps/map_sr2_paradise_winter.bnlbin'
destination.write_bytes(encoded)
report = {'source': 'Maps/map_sr2_paradise.bnlbin', 'source_sha256': hashlib.sha256(raw).hexdigest(),
          'staged': 'extracted/winter-inputs/map_sr2_paradise.bnlbin',
          'output': 'Maps/map_sr2_paradise_winter.bnlbin', 'sha256': hashlib.sha256(encoded).hexdigest(),
          'map_changes': {'properties.render': ['DaytimeWarm', 'DaytimeWarm_winter']},
          'gameplay_unchanged': True, 'minimum_client': '0.2.0-beta.56', 'pool': 'custom'}
(root / 'docs/paradise-winter-provenance.json').write_text(json.dumps(report, indent=2)+'\n')
print(json.dumps(report, indent=2))
