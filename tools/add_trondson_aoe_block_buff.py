"""Grant Trondson nearby allies +50% splash block damage via the existing F aura.
Requires the matching server change before applying. Read-only unless --apply.
"""
import base64
import copy
import json
from pathlib import Path
import sys
import time
import urllib.parse
import urllib.request

ICON = 'effect_hero_trondson_heigh_ho_buff_icon'
AURA = 'effect_hero_trondson_heigh_ho_aura'
VARIANT = AURA + '_singin_in_the_mine'
SELF = 'effect_hero_trondson_heigh_ho_buff_self'
READ_IDS = (ICON, AURA, VARIANT, SELF)


def validate_dependencies(cards):
    for key, radius in ((AURA, 10), (VARIANT, 16)):
        card = cards[key]
        assert card['duration'] == 12
        aura = card['effect']
        assert aura['type'] == 'aura' and aura['outer_radius'] == radius
        assert aura['targeting']['affected_team'] == 'friendly'
        assert aura['targeting']['ignore_caster'] is True
        assert ICON in aura['constant_effects']
    assert ICON not in cards[SELF]['effect']['constant_effects']
    effect = cards[ICON]['effect']
    assert effect['type'] == 'buff' and effect['buffs'] == {}
    assert effect['targeting']['affected_units'] == ['player']
    assert effect['targeting']['affected_team'] == 'friendly'


def plan(card):
    result = copy.deepcopy(card)
    if card['_id'] == ICON:
        result['splash_block_damage_bonus'] = 0.5
        result['gui_info']['name'] = {'text': 'Heigh-Ho!', 'data': {}}
        result['gui_info']['description'] = {'text': 'While near Trondson, deal 50% more explosion damage to blocks.', 'data': {}}
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
    print(json.dumps({'changes': list(changes), 'splash_block_damage_bonus': 0.5, 'allies_only': True, 'requires_server_update': True}), flush=True)
    if '--apply' not in sys.argv or not changes:
        return

    backup = Path('/root/config-backups') / (
        'trondson-aoe-block-buff-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
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
