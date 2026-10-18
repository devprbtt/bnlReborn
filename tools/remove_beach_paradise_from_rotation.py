"""Remove original Beach Base Mid Edit and Paradise from Casual and Ranked.

Run on the server: python3 tools/remove_beach_paradise_from_rotation.py [--apply]
Dry-run by default. Only map_list.friendly and map_list.ranked change; credentials stay on the host.
"""
import argparse
import base64
import copy
import json
from pathlib import Path
import time
import urllib.request

REMOVED = {'map_sr2_beach_base_new', 'map_sr2_paradise'}
POOLS = ('friendly', 'ranked')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    config = json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
    config = {key.replace('_', '').lower(): value for key, value in config.items()}
    base = config['couchdbendpoint'].rstrip('/') + '/' + config['couchdbdatabasename'] + '/'
    auth = base64.b64encode((config['couchdbusername'] + ':' + config['couchdbpassword']).encode()).decode()

    def request(key, data=None):
        req = urllib.request.Request(base + key, method='GET' if data is None else 'PUT',
            data=None if data is None else json.dumps(data).encode(),
            headers={'Authorization': 'Basic ' + auth, 'Content-Type': 'application/json'})
        with urllib.request.urlopen(req, timeout=20) as response:
            return json.load(response)

    before = request('map_list')
    assert before['category'] == 'map_list'
    after = copy.deepcopy(before)
    for name in POOLS:
        after[name] = [key for key in before[name] if key not in REMOVED]
    assert {k:v for k,v in after.items() if k not in POOLS} == {k:v for k,v in before.items() if k not in POOLS}
    print(json.dumps({'apply': args.apply, 'changed': before != after,
        'removed': sorted(REMOVED), 'pools': {name: {'before': len(before[name]), 'after': len(after[name]),
        'removed_entries': [key for key in before[name] if key in REMOVED]} for name in POOLS}}), flush=True)
    if not args.apply:
        return
    backup = None
    if before != after:
        backup = Path('/root/config-backups') / ('beach-paradise-rotation-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
        backup.mkdir(mode=0o700)
        (backup / 'map_list.before.json').write_text(json.dumps(before, indent=2))
        (backup / 'map_list.after.json').write_text(json.dumps(after, indent=2))
        # CouchDB's revision check prevents overwriting a concurrent rotation change.
        request('map_list', after)
    actual = request('map_list')
    assert {k: v for k, v in actual.items() if k != '_rev'} == {k: v for k, v in after.items() if k != '_rev'}
    port = config.get('controlpanelport', 8080)
    for _ in range(20):
        with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/maps', timeout=10) as response:
            runtime = json.load(response)
        if all(runtime['pools'][name] == after[name] for name in POOLS):
            assert runtime['pools']['custom'] == before['custom']
            print(json.dumps({'verified': True, 'backup': str(backup) if backup else None,
                'pools': {name: len(after[name]) for name in POOLS},
                'custom_unchanged': True}), flush=True)
            return
        time.sleep(1)
    raise RuntimeError('Saved to CDB, but the live rotation has not refreshed; inspect the catalogue watcher.')


if __name__ == '__main__':
    main()
