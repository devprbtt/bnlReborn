"""Prepare earlier Nigel rifle falloff offline. No live writes."""
import copy
import hashlib
import json
from pathlib import Path
import sys

source, output = map(Path, sys.argv[1:])
cards = json.loads(source.read_text(encoding='utf-8'))
original = {c['_id']: c for c in cards}
draft = copy.deepcopy(original)
rows = []


def walk(value, path):
    if isinstance(value, dict):
        yield path, value
        for key, child in value.items():
            yield from walk(child, path + [key])
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from walk(child, path + [index])


for cid in ['gear_hunter_rifle', 'gear_hunter_rifle_spot_shot']:
    card = draft[cid]
    changes = []
    def change(path, after):
        parent = card
        for key in path[:-1]:
            parent = parent[key]
        before = parent[path[-1]]
        changes.append({'path': path, 'before_exists': True, 'before': before, 'after': after})
        parent[path[-1]] = after
    found = [(p, v) for p, v in walk(card['tools'][0], ['tools', 0]) if v.get('type') == 'damage']
    assert len(found) == 1
    path, node = found[0]
    assert node['falloff'] == {'reduction_coeff': .55, 'max_damage_range': 50, 'min_damage_range': 100, 'max_range': 100}
    change(path + ['falloff', 'max_damage_range'], 40)
    text = card['description']['text']
    assert text.count('Max Damage Range - 50') == 1
    change(['description', 'text'], text.replace('Max Damage Range - 50', 'Max Damage Range - 40'))
    for index, reticle in enumerate(card['reticles']):
        if reticle['preview_damage_range'] == 50:
            change(['reticles', index, 'preview_damage_range'], 40)
    assert len(changes) == 3
    rows.append({'id': cid, 'expected_revision': original[cid]['_rev'], 'changes': changes})

output.mkdir(parents=True, exist_ok=True)
plan = {'schema_version': 1, 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
        'status': 'PREPARED; NOT APPLIED', 'cards': rows}
(output / 'nigel-earlier-falloff.patch.json').write_text(json.dumps(plan, indent=2) + '\n', encoding='utf-8')
(output / 'candidate-catalogue.json').write_text(json.dumps(list(draft.values()), indent=2) + '\n', encoding='utf-8')
print('Prepared two cards/six fields: falloff starts 50m to 40m; no live writes')
