"""Offline Tony snapshot buff / Second Wind catalogue patch. Never writes live."""
import copy
import hashlib
import json
import sys
from pathlib import Path

source, output = map(Path, sys.argv[1:])
original = {c['_id']: c for c in json.loads(source.read_text(encoding='utf-8'))}
cards = copy.deepcopy(original)
build_id = 'effect_hero_engineer_bfs_build_cast'
turret_id = 'effect_hero_engineer_bfs_turret_cast'
assert build_id not in cards and turret_id not in cards

def targeting(units, labels=None):
    return {'affected_labels': labels, 'affected_units': units, 'affected_team': 'friendly',
            'caster_owned_only': False, 'ignore_caster': False}

def wrapper(cid, effects, targets, icon):
    card = copy.deepcopy(cards['effect_hero_engineer_bfs_build_self'])
    card.pop('_rev', None)
    card.pop('hercules_metadata', None)
    card['_id'] = cid
    if not icon:
        card['gui_info'] = None
        card['prefab_unit'] = ''
    card['effect']['targeting'] = targets
    card['effect']['constant_effects'] = effects
    assert card['duration'] == 20
    return card

cards[build_id] = wrapper(build_id, ['effect_hero_engineer_bfs_build'], targeting(['player']), True)
cards[turret_id] = wrapper(turret_id, ['effect_hero_engineer_turret_buff'], targeting(['turret'], ['engineer_turret']), False)
for cid, radius in [('ability_engineer_bfs', 5), ('ability_engineer_bfs_wider', 11)]:
    hit = cards[cid]['behavior']['hit_effect']
    assert len(hit['constant']) == 2 and any('aura' in key for key in hit['constant'])
    hit['constant'] = []
    for key, targets in [(build_id, targeting(['player'])), (turret_id, targeting(['turret'], ['engineer_turret']))]:
        hit['instant'].append({'type': 'all_units_bunch', 'targeting': targets, 'range': radius,
                               'break_on_effect_fail': False, 'instant': [], 'constant': [key]})

cards['unit_hero_engineer']['data']['gui_info']['active_ability']['description']['text'] = (
    "On activation, Tony and allied players within 5m gain increased build speed for 20s, "
    "and nearby Lil' Buddy turrets gain bonus damage for 20s. Buffs remain when recipients leave range; "
    "entering range later does not grant them. Affected players receive 5 points once per cast.")
skill = cards['perk_class_skill_1']
skill['class_perk']['enemy_proximity_radius'] = 8
skill['description']['text'] = (
    'After {out_of_combat_seconds}s out of combat, regenerate {regen_max_health_percent_per_second}% '
    'of your maximum health per second. Attacking, taking enemy or fall damage, being revealed, '
    'or an enemy player within {enemy_proximity_radius}m restarts the timer.')
skill['description_2']['text'] = (
    'Regenerate {regen_max_health_percent_per_second}% max health per second after {out_of_combat_seconds}s '
    'without combat, enemy reveals or an enemy within {enemy_proximity_radius}m.')
changed = [cid for cid in cards if cards[cid] != original.get(cid)]
def clean(card):
    return None if card is None else {k: v for k, v in card.items() if k not in ['_rev', 'hercules_metadata']}
rows = [{'id': cid, 'expected_revision': original.get(cid, {}).get('_rev'),
         'before': clean(original.get(cid)), 'after': clean(cards[cid])} for cid in changed]
assert len(rows) == 6
output.mkdir(parents=True, exist_ok=True)
(output/'tony-second-wind.patch.json').write_text(json.dumps({'schema_version': 1,
    'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'status': 'PREPARED; NOT APPLIED', 'cards': rows}, indent=2)+'\n')
(output/'candidate-catalogue.json').write_text(json.dumps(list(cards.values()), indent=2)+'\n', encoding='utf-8')
print('Prepared six cards, including two new timed effects; no CDB writes')
