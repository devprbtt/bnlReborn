"""Prepare the reviewed six-hero tuning patch offline; never writes to a server."""
import copy
import hashlib
import json
import sys
from pathlib import Path

source, output = map(Path, sys.argv[1:])
cards = json.loads(source.read_text(encoding='utf-8'))
original = {c['_id']: c for c in cards}
draft = copy.deepcopy(original)
rows = {}

def get(card, path):
    for key in path:
        card = card[key]
    return card

def change(cid, path, value):
    parent = get(draft[cid], path[:-1])
    exists = path[-1] in parent if isinstance(parent, dict) else True
    before = copy.deepcopy(parent[path[-1]]) if exists else None
    if exists and before == value:
        return
    row = rows.setdefault(cid, {'id': cid, 'expected_revision': original[cid]['_rev'], 'changes': []})
    row['changes'].append({'path': path, 'before_exists': exists, 'before': before, 'after': value})
    parent[path[-1]] = copy.deepcopy(value)

def walk(value, path):
    if isinstance(value, dict):
        yield path, value
        for key, item in value.items():
            yield from walk(item, path + [key])
    elif isinstance(value, list):
        for index, item in enumerate(value):
            yield from walk(item, path + [index])

# Omitted action-score properties retain the server's existing defaults.
settings = copy.deepcopy(draft['global_logic'].get('action_scores') or {})
assert settings.get('roly_goo', 10) == 10
settings['roly_goo'] = 8
change('global_logic', ['action_scores'], settings)

for cid, heal in [('gear_doc_eliza_bubble_gun', 8), ('gear_doc_eliza_bubble_gun_beautiful_bubbles', 4)]:
    found = [(p, v) for p, v in walk(draft[cid]['tools'], ['tools'])
             if v.get('type') == 'heal' and v.get('targeting', {}).get('affected_units') == ['player']]
    assert len(found) == 2
    for path, node in found:
        assert node['player_heal'] == heal / .8
        assert node['targeting']['affected_team'] == 'friendly' and node['targeting']['ignore_caster']
        change(cid, path + ['player_heal'], heal)
    old = '10' if heal == 8 else '5'
    text = draft[cid]['description']['text']
    assert text.count('Heals allies for ' + old + ' HP') == 2
    change(cid, ['description', 'text'], text.replace('Heals allies for ' + old + ' HP', 'Heals allies for ' + str(heal) + ' HP'))

cid = 'gear_magnus_static_gloves'
found = [(p, v) for p, v in walk(draft[cid]['tools'][1], ['tools', 1])
         if v.get('type') == 'heal' and v.get('targeting', {}).get('affected_units') == ['player']]
assert len(found) == 1 and found[0][1]['player_heal'] == 2
change(cid, found[0][0] + ['player_heal'], 2.25)
text = draft[cid]['description']['text']
assert text.count('Heals allies for 2 HP') == 1
change(cid, ['description', 'text'], text.replace('Heals allies for 2 HP', 'Heals allies for 2.25 HP'))

for prefix, start, minimum in [('gear_trondson_sonic_staff', 12, .5), ('gear_hunter_rifle', 70, .55)]:
    ids = sorted(k for k in draft if k == prefix or k.startswith(prefix + '_'))
    assert len(ids) == 2
    for cid in ids:
        found = [(p, v) for p, v in walk(draft[cid]['tools'][0], ['tools', 0]) if v.get('type') == 'damage']
        assert len(found) == 1
        path, node = found[0]
        old = node['falloff']['max_damage_range']
        assert node['falloff']['reduction_coeff'] == .7 and old == (15 if start == 12 else 80)
        change(cid, path + ['falloff', 'max_damage_range'], start)
        change(cid, path + ['falloff', 'reduction_coeff'], minimum)
        text = draft[cid]['description']['text']
        assert text.count('Max Damage Range - ' + str(old)) == 1
        change(cid, ['description', 'text'], text.replace('Max Damage Range - ' + str(old), 'Max Damage Range - ' + str(start)))
        for index, reticle in enumerate(draft[cid].get('reticles') or []):
            if reticle.get('preview_damage_range') == old:
                change(cid, ['reticles', index, 'preview_damage_range'], start)

cid = 'effect_status_shielded_kinetic_shield'
assert draft[cid]['effect']['buffs']['shield'] == .2
change(cid, ['effect', 'buffs', 'shield'], .375)

# Rolling Wild trades some shielding for mobility, replacing the cooldown penalty.
cid = 'effect_hero_roly_emplacement_mode_rolling_wild'
assert draft[cid]['effect']['buffs']['shield'] == .4
change(cid, ['effect', 'buffs', 'shield'], .375)
cid = 'ability_roly_emplacement_mode_rolling_wild'
assert draft[cid]['charges']['charge_cooldown'] == 52
assert draft['ability_roly_emplacement_mode']['charges']['charge_cooldown'] == 45
change(cid, ['charges', 'charge_cooldown'], 45)
cid = 'perk_hero_roly_rolling_wild'
assert 'Increases Active Ability cooldown by 7 seconds' in draft[cid]['description']['text']
change(cid, ['description', 'text'],
       'Allows movement at 60% of normal speed whilst Emplacement Mode is active / '
       'Reduces Emplacement Mode shielding from 40% to 37.5% damage reduction')
change(cid, ['description_2', 'text'],
       'Move at 60% speed during Emplacement Mode, with 37.5% shielding instead of 40%. '
       'Uses the normal 45-second cooldown.')

assert len(rows) == 12
output.mkdir(parents=True, exist_ok=True)
patch = {'schema_version': 1, 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
         'status': 'PREPARED; NOT APPLIED', 'cards': [rows[k] for k in sorted(rows)]}
(output / 'six-hero-balance.patch.json').write_text(json.dumps(patch, indent=2) + '\n', encoding='utf-8')
(output / 'candidate-catalogue.json').write_text(json.dumps([draft[c['_id']] for c in cards], indent=2) + '\n', encoding='utf-8')
print('Prepared', len(rows), 'cards;', sum(len(r['changes']) for r in rows.values()), 'field changes; no server writes')
