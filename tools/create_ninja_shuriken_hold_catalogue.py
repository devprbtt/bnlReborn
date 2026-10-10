"""Build an offline catalogue candidate; never writes to a running server.

Usage: python tools/create_ninja_shuriken_hold_catalogue.py INPUT.json OUTPUT_DIRECTORY
Turns the three-star alt fire on both shuriken cards into a hold-and-release
throw. Damage, bleed, spread, ammo, timing and speed are carried over unchanged;
no max_speed or hold_interval, so holding never changes the throw speed.
"""
import copy
import hashlib
import json
from pathlib import Path
import sys

CARDS = ('gear_ninja_shuriken', 'gear_ninja_shuriken_bloody_bleeding')
# Shot-only fields; a throw has no hitscan range and no recoil.
SHOT_ONLY = ('auto_fire', 'range', 'hit_on_out_of_range', 'recoil')


def build(cards):
    result = copy.deepcopy(cards)
    by_id = {c['_id']: c for c in result}
    for name in CARDS:
        alt = by_id[name]['tools'][1]
        if alt['type'] == 'throw':
            continue
        assert alt['type'] == 'shot', name
        assert alt['bullet']['type'] == 'projectile', name
        assert alt['bullet'].get('max_speed') is None and alt['bullet'].get('hold_interval') is None, name
        assert alt['bullets']['count'] == 3, name
        alt['type'] = 'throw'
        for field in SHOT_ONLY:
            alt.pop(field, None)
    return result


def main():
    source, output = Path(sys.argv[1]), Path(sys.argv[2])
    raw = source.read_bytes()
    original = json.loads(raw)
    candidate = build(original)
    before = {c['_id']: c for c in original}
    changed = [c for c in candidate if c != before.get(c['_id'])]
    assert sorted(c['_id'] for c in changed) in ([], sorted(CARDS)), [c['_id'] for c in changed]
    output.mkdir(parents=True, exist_ok=True)
    for name, data in [('catalogue.json', candidate), ('changed-cards.json', changed),
                       ('rollback-cards.json', [before[c['_id']] for c in changed]),
                       ('provenance.json', {'source': str(source.resolve()), 'sha256': hashlib.sha256(raw).hexdigest(),
                        'changed': [c['_id'] for c in changed]})]:
        (output / name).write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'Prepared {len(changed)} changed cards in {output}; no server writes.')


if __name__ == '__main__':
    main()
