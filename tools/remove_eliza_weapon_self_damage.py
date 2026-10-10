"""CDB-only Eliza self-damage removal; dry-run unless --apply.

Uses existing instant-effect targeting, including both Bubble Gun fire modes.
Chemical grenade clouds already target opponents only and remain unchanged.
No client/server binary update or service restart is required. Re-equip/rejoin
to replace any already-instantiated gear holding the old card.
"""
import base64
import copy
import json
from pathlib import Path
import sys
import time
import urllib.parse
import urllib.request


CARDS = {
    'gear_doc_eliza_acid_washer': 1,
    'gear_doc_eliza_bubble_gun': 4,
    'gear_doc_eliza_bubble_gun_beautiful_bubbles': 4,
}


def damage_effects(node):
    if isinstance(node, dict):
        if node.get('type') in ('damage', 'splash_damage'):
            yield node
        for value in node.values():
            yield from damage_effects(value)
    elif isinstance(node, list):
        for value in node:
            yield from damage_effects(value)


def plan(card):
    result = copy.deepcopy(card)
    effects = list(damage_effects(result['tools']))
    assert len(effects) == CARDS[card['_id']], 'Unexpected weapon structure'
    for effect in effects:
        targeting = effect.get('targeting')
        if targeting is None:
            targeting = {
                'affected_labels': None,
                'affected_units': None,
                'affected_team': 'both',
                'caster_owned_only': False,
                'ignore_caster': True,
            }
            effect['targeting'] = targeting
        else:
            targeting['ignore_caster'] = True
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

    before = {key: request(key) for key in CARDS}
    after = {key: plan(card) for key, card in before.items()}
    changes = {key: card for key, card in after.items() if card != before[key]}
    assert all(plan(card) == card for card in after.values())
    print(json.dumps({'changes': list(changes), 'damage_effects': CARDS}), flush=True)
    if '--apply' not in sys.argv or not changes:
        return

    backup = Path('/root/config-backups') / (
        'eliza-no-self-damage-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
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
