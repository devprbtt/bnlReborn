"""Rebuild the isolated baseline from an offline decoded original CDB.

Usage: python tools/generate_time_assault_traps.py <original-catalogue.json>
Run from the server repository root. Never edits the input catalogue or maps.
"""
import json
import sys
from pathlib import Path

source = {c['_id']: c for c in json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))}
roots = sorted({u['unit_key'] for c in source.values()
                if c['category'] == 'map' and c.get('data', {}).get('match') == 'time_trial'
                for u in c['data']['units']
                if source[u['unit_key']].get('data', {}).get('type') == 'landmine'})
blocks = ['block_glue', 'block_caltrops', 'block_caltrops_astro',
          'block_bear_trap', 'block_bear_trap_astro', 'block_false']

def strings(value):
    if isinstance(value, dict):
        for item in value.values():
            yield from strings(item)
    elif isinstance(value, list):
        for item in value:
            yield from strings(item)
    elif isinstance(value, str):
        yield value

closure = set()
# Reborn no longer contains this original material. Preserve its native entry
# sound/footstep presentation instead of leaving an unresolved visual key.
pending = roots + blocks + ['material_caltrops']
while pending:
    key = pending.pop()
    if key in closure:
        continue
    closure.add(key)
    pending.extend(s for s in strings(source[key]) if s in source
                   and source[s]['category'] in ('unit', 'effect', 'block') and s not in closure)
names = {k: k.split('_', 1)[0] + '_time_assault_original_' + k.split('_', 1)[1]
         for k in sorted(closure)}

def rewrite(value):
    if isinstance(value, dict):
        return {k: rewrite(v) for k, v in value.items()}
    if isinstance(value, list):
        return [rewrite(v) for v in value]
    return names.get(value, value) if isinstance(value, str) else value

cards = [rewrite(source[k]) for k in sorted(closure)]
for i, card in enumerate(c for c in cards if c['category'] == 'block'):
    card['block_id'] = 64000 + i
if len(roots) != 7 or len(cards) != 30:
    raise ValueError('Historical input changed; review the baseline before regenerating')
if any(s.startswith('UNRESOLVED_HASH_') for c in cards for s in strings(c)):
    raise ValueError('Historical baseline has unresolved references')
payload = {'map_unit_overrides': {k: names[k] for k in roots},
           'map_block_overrides': {**{k: names[k] for k in blocks},
                                   'block_fire_from_fire_trap': names['block_fire']},
           'cards': cards}
Path('BNLReloadedServer/Database/TimeAssaultOriginalTraps.json').write_text(
    json.dumps(payload, indent=2, ensure_ascii=True) + '\n', encoding='utf-8')
