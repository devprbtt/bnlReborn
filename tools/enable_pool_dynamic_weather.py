"""Enable dynamic weather on existing Casual and Ranked map cards without duplicating maps.

Run on the server: python3 tools/enable_pool_dynamic_weather.py [--apply]
Dry-run by default. Only the selected cards dynamic_weather flag changes; credentials stay on the host.
"""
import argparse
import base64
import copy
import json
from pathlib import Path
import time
import urllib.request




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

    pools = request('map_list')
    ids = sorted(set(pools['friendly'] + pools['ranked']))
    before = {key: request(key) for key in ids}
    assert all(card['category']=='map' for card in before.values())
    after = copy.deepcopy(before)
    for card in after.values(): card['dynamic_weather'] = True
    changed = [key for key in ids if before[key] != after[key]]
    print(json.dumps({'apply':args.apply, 'pool_maps':len(ids), 'changed':changed, 'maps':ids, 'pool_unchanged':True}),flush=True)
    if not args.apply: return
    backup = None
    if changed:
        backup = Path('/root/config-backups') / ('pool-dynamic-weather-' + time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
        backup.mkdir(mode=0o700)
        (backup/'map_list.before.json').write_text(json.dumps(pools,indent=2))
        (backup/'cards.before.json').write_text(json.dumps(before,indent=2))
        # Abort if the operator changed the pool after the snapshot.
        assert request('map_list') == pools
        for key in changed: request(key,after[key])
    for key in ids:
        actual=request(key)
        assert {k:v for k,v in actual.items() if k!='_rev'} == {k:v for k,v in after[key].items() if k!='_rev'}
    assert request('map_list') == pools
    print(json.dumps({'verified':True,'enabled':len(ids),'backup':str(backup) if backup else None,'pool_unchanged':True}),flush=True)


if __name__ == '__main__':
    main()
