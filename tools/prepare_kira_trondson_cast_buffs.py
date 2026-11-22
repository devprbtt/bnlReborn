"""Prepare Kira/Trondson activation buffs offline, preserving native tuning."""
import copy
import hashlib
import json
from pathlib import Path
import sys

source, output = map(Path, sys.argv[1:])
original = {c['_id']: c for c in json.loads(source.read_text(encoding='utf-8'))}
cards = copy.deepcopy(original)


def target(units, ignore=False):
    return {'affected_labels': None, 'affected_units': units, 'affected_team': 'friendly',
            'caster_owned_only': False, 'ignore_caster': ignore}


def new_card(template, cid):
    assert cid not in cards
    card = copy.deepcopy(cards[template])
    for key in ('_rev', 'hercules_metadata'):
        card.pop(key, None)
    card['_id'] = cid
    cards[cid] = card
    return card


value = new_card('effect_hero_kira_jpop_star_reload_buff', 'effect_hero_kira_jpop_star_reload_buff_cast_value')
value['duration'] = None  # Wrapper owns expiry; no ten-second child can outlive Thrills' six seconds.


def wrapper(template, cid, duration, children, targets):
    card = new_card(template, cid)
    card['duration'] = duration
    card['effect'] = {'type': 'self', 'targeting': targets, 'interval_effects': [],
                      'interval': 0, 'constant_effects': children}
    return card


def snapshot(cid, aura_id, marker, duration, children, template, units, ignore=False):
    aura = cards[aura_id]['effect']
    assert aura['type'] == 'aura'
    wrapper(template, marker, duration, children, target(units))
    hit = cards[cid]['behavior']['hit_effect']
    assert aura_id in hit['constant']
    hit['constant'].remove(aura_id)
    hit['instant'].append({'type': 'all_units_bunch', 'targeting': target(units, ignore),
                           'range': aura['outer_radius'], 'break_on_effect_fail': False,
                           'instant': [], 'constant': [marker]})


for suffix, duration in [('', 10), ('_idoru', 10), ('_thrills_for_days', 6)]:
    cid = 'ability_kira_jpop_star' + suffix
    hit = cards[cid]['behavior']['hit_effect']
    hit['constant'] = [key for key in hit['constant'] if not key.startswith('effect_hero_kira_jpop_star_reload_buff')]
    children = [value['_id']]
    if suffix == '_idoru':
        children.append('effect_hero_kira_jpop_star_reload_buff_idoru')
    snapshot(cid, 'effect_hero_kira_jpop_star_aura' + suffix, 'effect_hero_kira_jpop_star_cast' + suffix,
             duration, children, 'effect_hero_kira_jpop_star_reload_buff_icon', ['player'])

on_kill = cards['effect_hero_kira_jpop_star_self_thrills_for_days_on_kill']['effect']['effect']
assert 'effect_hero_kira_jpop_star_aura_thrills_for_days' in on_kill['constant']
on_kill['constant'].remove('effect_hero_kira_jpop_star_aura_thrills_for_days')
on_kill['constant'].append('effect_hero_kira_jpop_star_cast_thrills_for_days')

drill_id = 'effect_hero_trondson_heigh_ho_drill_cast'
drill = wrapper('effect_hero_trondson_heigh_ho_buff_self', drill_id, 12,
                ['effect_hero_trondson_heigh_ho_torpedo_buff'], target(['drill']))
drill['gui_info'] = None
drill['labels'] = []
for suffix in ['', '_singin_in_the_mine']:
    cid = 'ability_trondson_heigh_ho' + suffix
    aura_id = 'effect_hero_trondson_heigh_ho_aura' + suffix
    aura = cards[aura_id]['effect']
    children = [key for key in aura['constant_effects'] if key != 'effect_hero_trondson_heigh_ho_torpedo_buff']
    snapshot(cid, aura_id, 'effect_hero_trondson_heigh_ho_cast' + suffix, 12, children,
             'effect_hero_trondson_heigh_ho_buff_self', ['player'], ignore=True)
    cards[cid]['behavior']['hit_effect']['instant'].append({'type': 'all_units_bunch',
        'targeting': target(['drill']), 'range': aura['outer_radius'], 'break_on_effect_fail': False,
        'instant': [], 'constant': [drill_id]})

cards['unit_hero_kira']['data']['gui_info']['active_ability']['description']['text'] = (
    'On activation, Kira gains unlimited ammo and she and allied players within 10m gain '
    'increased reload speed for 10s. Recipients keep the reload buff after leaving range; '
    'entering range later does not grant it.')
cards['unit_hero_trondson']['data']['gui_info']['active_ability']['description']['text'] = (
    'On activation, Trondson and allied players within 10m gain their digging bonuses for 12s. '
    'Nearby allied drills also gain their damage bonus for 12s. Recipients keep their buffs '
    'after leaving range; entering range later does not grant them.')
cards['perk_hero_kira_thrills_for_days']['description']['text'] = (
    'Kills during J-POP Star refresh Kira and the original living buff recipients, even outside '
    'the activation range / Duration reduced from 10s to 6s. New arrivals do not gain the buff.')
cards['perk_hero_kira_idoru']['description']['text'] = (
    'Increases J-POP Star activation range from 10m to 16m and grants an extra 50% reload speed '
    '/ Increases cooldown from 60s to 67s. Recipients are selected at activation.')
cards['perk_hero_trondson_singin_in_the_mine']['description']['text'] = (
    'Increases Heigh-Ho activation range from 10m to 16m and grants 20% extra bricks while digging '
    '/ Increases cooldown from 50s to 57s. Recipients are selected at activation.')

rows = [{'id': cid, 'expected_revision': original.get(cid, {}).get('_rev'),
         'before': original.get(cid), 'after': card} for cid, card in cards.items() if card != original.get(cid)]
assert len(rows) == 18 and sum(row['before'] is None for row in rows) == 7
rows.sort(key=lambda row: (row['before'] is not None, row['id']))
output.mkdir(parents=True, exist_ok=True)
(output / 'kira-trondson-cast-buffs.patch.json').write_text(json.dumps({'schema_version': 1,
    'status': 'PREPARED; NOT APPLIED', 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
    'cards': rows}, indent=2) + '\n', encoding='utf-8')
(output / 'candidate-catalogue.json').write_text(json.dumps(list(cards.values()), indent=2) + '\n', encoding='utf-8')
print('Prepared 18 cards, including 7 new native effects; no live writes')
