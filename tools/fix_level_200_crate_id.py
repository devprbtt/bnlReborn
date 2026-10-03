"""Read-only by default; --apply assigns the level-200 crate an unused block ID.

Run on the server. Preserves the default crate and takes a private rollback snapshot.
Existing placed blocks with the formerly shared ID cannot be distinguished retrospectively.
"""
import base64
import copy
import json
import sys
import time
import urllib.request
from pathlib import Path

CARD = 'block_crate_level_200_hero'
BLOCK_ID = 1051


def plan(docs):
    source = docs[CARD]
    conflicts = [d['_id'] for d in docs.values() if d.get('category') == 'block'
                 and d.get('block_id') == BLOCK_ID and d['_id'] != CARD]
    if conflicts:
        raise ValueError('Destination block ID is occupied: ' + ', '.join(conflicts))
    if source['block_id'] not in (58, BLOCK_ID):
        raise ValueError('Unexpected existing level-200 block ID')
    result = copy.deepcopy(source)
    result['block_id'] = BLOCK_ID
    return result


def main():
    c = {k.replace('_', '').lower(): v for k, v in json.loads(
        Path('/opt/bnlreloaded/current/Configs/configs.json').read_text()).items()}
    base = c['couchdbendpoint'].rstrip('/') + '/' + c['couchdbdatabasename']
    auth = 'Basic ' + base64.b64encode((c['couchdbusername'] + ':' + c['couchdbpassword']).encode()).decode()

    def request(path, data=None):
        req = urllib.request.Request(base + '/' + path,
            headers={'Authorization': auth, 'Content-Type': 'application/json'},
            data=None if data is None else json.dumps(data).encode(), method='GET' if data is None else 'PUT')
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)

    docs = {r['id']: r['doc'] for r in request('_all_docs?include_docs=true')['rows'] if 'doc' in r}
    after = plan(docs)
    print(json.dumps({'card': CARD, 'before': docs[CARD]['block_id'], 'after': BLOCK_ID,
                      'changed': after != docs[CARD]}))
    if '--apply' not in sys.argv or after == docs[CARD]:
        return
    backup = Path('/root/config-backups') / ('level-200-crate-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / 'before.json').write_text(json.dumps(docs[CARD], indent=2))
    request(CARD, after)  # Uses the fetched _rev; concurrent edits fail.
    actual = request(CARD)
    assert {k: v for k, v in actual.items() if k != '_rev'} == {k: v for k, v in after.items() if k != '_rev'}
    assert request('block_crate_player') == docs['block_crate_player']
    print(json.dumps({'backup': str(backup), 'revision': actual['_rev']}))


if __name__ == '__main__':
    main()
