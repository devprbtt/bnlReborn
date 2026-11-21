"""Initialize global_logic.entity_attacks after deploying the matching server.

Existing CDB tuning is preserved. Online mode defaults to a read-only preview;
--apply backs up the original and writes using CouchDB's revision guard.
"""
import argparse
import base64
import copy
import json
import time
import urllib.request
from pathlib import Path

DEFAULTS = {'beam_player_damage_min': 20.833334,
            'beam_player_damage_max': 100, 'fireball_player_damage': 40}


def plan(card):
    after = copy.deepcopy(card)
    if after.get('entity_attacks') is None:
        after['entity_attacks'] = {}
    for name, value in DEFAULTS.items():
        after['entity_attacks'].setdefault(name, value)
    return after


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--from-file', type=Path, help='Offline global_logic document')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if args.from_file:
        if args.apply:
            parser.error('--apply is only available online')
        card = json.loads(args.from_file.read_text(encoding='utf-8-sig'))
    else:
        if args.output:
            parser.error('--output requires --from-file')
        config = json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
        config = {k.replace('_', '').lower(): v for k, v in config.items()}
        url = config['couchdbendpoint'].rstrip('/') + '/' + config['couchdbdatabasename'] + '/global_logic'
        auth = 'Basic ' + base64.b64encode((config['couchdbusername'] + ':' + config['couchdbpassword']).encode()).decode()

        def request(data=None):
            req = urllib.request.Request(url, data=None if data is None else json.dumps(data).encode(),
                                         method='GET' if data is None else 'PUT',
                                         headers={'Authorization': auth, 'Content-Type': 'application/json'})
            with urllib.request.urlopen(req, timeout=30) as response:
                return json.load(response)

        card = request()
    if card.get('_id') != 'global_logic':
        raise ValueError('Expected the global_logic CDB document')
    after = plan(card)
    print(json.dumps({'changed': after != card, 'entity_attacks': after['entity_attacks']}))
    if args.from_file:
        if args.output:
            args.output.write_text(json.dumps(after, indent=2) + '\n', encoding='utf-8')
        return
    if not args.apply or after == card:
        return
    if request() != card:
        raise RuntimeError('CDB changed during review; rerun the preview')
    backup = Path('/root/config-backups') / ('entity-attacks-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / 'before.json').write_text(json.dumps(card, indent=2))
    request(after)
    saved = request()
    if {k: v for k, v in saved.items() if k != '_rev'} != {k: v for k, v in after.items() if k != '_rev'}:
        raise RuntimeError('CDB readback differs from prepared settings')
    print(json.dumps({'updated': 'global_logic', 'backup': str(backup)}))


if __name__ == '__main__':
    main()
