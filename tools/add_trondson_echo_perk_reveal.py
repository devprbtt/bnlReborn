"""Add enemy reveal to the Echo-Location perk staff only.

Read-only by default; --apply backs up and revision-checks two CDB cards.
Preserves base staff, primary fire, ally healing, 11m radius and 26 ammo cost.
No server binary update or restart is needed; re-equip/rejoin after applying.
"""
import base64
import copy
import json
from pathlib import Path
import sys
import time
import urllib.parse
import urllib.request

BASE = 'gear_trondson_sonic_staff'
GEAR = BASE + '_echo_location'
PERK = 'perk_hero_trondson_echo_location'
MARKER = 'effect_hero_trondson_sonic_staff_marker'
READ_IDS = (BASE, GEAR, PERK, MARKER)
REVEAL = {
    'type': 'bunch', 'interrupt': None,
    'targeting': {'affected_labels': None, 'affected_units': ['player'],
                  'affected_team': 'opponent', 'caster_owned_only': False,
                  'ignore_caster': True},
    'impact': None, 'break_on_effect_fail': False,
    'instant': [], 'constant': [MARKER],
}


def validate_dependencies(cards):
    marker = cards[MARKER]
    assert marker['duration'] == 7
    assert marker['effect']['type'] == 'buff'
    assert marker['effect']['buffs']['vision_mark'] == 1
    assert marker['effect']['targeting']['affected_team'] == 'opponent'
    assert {'type': 'gear', 'replace_from': BASE, 'replace_to': GEAR} in cards[PERK]['perk_mods']
    pulse = cards[GEAR]['tools'][1]
    assert pulse['hit_effect']['type'] == 'all_units_bunch'
    assert pulse['hit_effect']['range'] == 11 and pulse['ammo']['rate'] == 26


def plan(card):
    result = copy.deepcopy(card)
    if card['_id'] == GEAR:
        pulse = result['tools'][1]['hit_effect']
        if REVEAL not in pulse['instant']:
            pulse['instant'].append(copy.deepcopy(REVEAL))
        result['description'] = {'text': "Trondson's trusty Sonic Staff acts as a multi-purpose tool and weapon. <BINDING_CAST1> fires pulses of destructive sonic energy while <BINDING_CAST2> heals nearby allies and reveals enemy players within 11 meters for 7 seconds.", 'data': {}}
    elif card['_id'] == PERK:
        result['description'] = {'text': 'Increases Sonic Staff Echo Pulse range by 6 meters and ammo consumption by 6. The right-click pulse also reveals enemy players in its radius for 7 seconds.', 'data': {}}
    return result


def payload(card):
    return {k: v for k, v in card.items() if k != '_rev'}


def main():
    config = json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
    config = {k.replace('_', '').lower(): v for k, v in config.items()}
    base = config['couchdbendpoint'].rstrip('/') + '/' + config['couchdbdatabasename']
    auth = 'Basic ' + base64.b64encode(
        (config['couchdbusername'] + ':' + config['couchdbpassword']).encode()).decode()

    def request(card_id, data=None):
        req = urllib.request.Request(
            base + '/' + urllib.parse.quote(card_id, safe=''),
            data=None if data is None else json.dumps(data).encode(),
            headers={'Authorization': auth, 'Content-Type': 'application/json'},
            method='GET' if data is None else 'PUT')
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)

    before = {key: request(key) for key in READ_IDS}
    validate_dependencies(before)
    after = {key: plan(card) for key, card in before.items()}
    changes = {key: card for key, card in after.items() if card != before[key]}
    assert all(plan(card) == card for card in after.values())
    print(json.dumps({'changes': list(changes), 'reveal_seconds': 7, 'radius': 11, 'perk_only': True}), flush=True)
    if '--apply' not in sys.argv or not changes:
        return

    backup = Path('/root/config-backups') / (
        'trondson-echo-perk-reveal-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / 'before.json').write_text(json.dumps(before, indent=2))
    print(json.dumps({'backup': str(backup)}), flush=True)
    saved = []
    try:
        for key, card in changes.items():
            result = request(key, card)
            saved.append((key, result['rev']))
            (backup / 'written-revisions.json').write_text(json.dumps(saved, indent=2))
            assert payload(request(key)) == payload(card), 'Readback differs: ' + key
        assert all(payload(request(key)) == payload(card) for key, card in after.items())
    except Exception:
        # Roll back only revisions written by this run, preserving concurrent edits.
        for key, revision in reversed(saved):
            original = dict(before[key], _rev=revision)
            try:
                request(key, original)
            except Exception:
                print('Revision-guarded rollback failed for ' + key, file=sys.stderr)
        raise
    print(json.dumps({'verified': list(changes), 'backup': str(backup)}), flush=True)


if __name__ == '__main__':
    main()
