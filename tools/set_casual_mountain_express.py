"""Replace Mountain Express Mid Edit in Casual with regular Mountain Express.

Run on the server: python3 tools/set_casual_mountain_express.py [--apply]
Dry-run by default. Only map_list.friendly changes; credentials stay on the host.
"""
import argparse
import base64
import copy
import json
from pathlib import Path
import time
import urllib.request

OLD = 'map_sr2_mountain_express_mid_edit'
NEW = 'map_sr2_mountain_express'


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
    assert request(NEW)['category'] == 'map'
    assert Path('/opt/bnlreloaded/current/Maps', NEW + '.bnlbin').is_file()
    after = copy.deepcopy(before)
    pool = []
    for key in before['friendly']:
        if key == OLD or (key == NEW and NEW in pool):
            continue
        pool.append(key)
    if NEW not in pool:
        pool.append(NEW)
    after['friendly'] = pool
    assert [key for key in pool if key != NEW] == [key for key in before['friendly'] if key not in (OLD, NEW)]
    print(json.dumps({'apply': args.apply, 'changed': before != after,
        'old_mid_edit_entries': before['friendly'].count(OLD), 'mid_edit_entries': pool.count(OLD),
        'regular_entries': pool.count(NEW), 'casual_map_count': len(pool)}), flush=True)
    if not args.apply:
        return
    backup = None
    if before != after:
        backup = Path('/root/config-backups') / ('casual-mountain-express-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
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
        if runtime['pools']['friendly'] == pool:
            for name in ('ranked', 'custom'):
                assert runtime['pools'][name] == before[name]
            print(json.dumps({'verified': True, 'backup': str(backup) if backup else None,
                'casual_map_count': len(pool), 'mid_edit_entries': 0, 'regular_entries': 1}), flush=True)
            return
        time.sleep(1)
    raise RuntimeError('Saved to CDB, but the live rotation has not refreshed; inspect the catalogue watcher.')


if __name__ == '__main__':
    main()
