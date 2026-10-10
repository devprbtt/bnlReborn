"""Deploy the reviewed two-card Ninja hold migration. Dry-run unless --apply.

Requires a stopped game service for writes; keeps revision-guarded rollback cards.
Credentials are read on the VPS and never printed or copied into the release.
"""
import base64
import json
from pathlib import Path
import subprocess
import sys
import time
import urllib.parse
import urllib.request

from create_ninja_shuriken_hold_catalogue import build, CARDS


def main():
    config = json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
    config = {k.replace('_', '').lower(): v for k, v in config.items()}
    base = config['couchdbendpoint'].rstrip('/') + '/' + config['couchdbdatabasename']
    auth = 'Basic ' + base64.b64encode((config['couchdbusername'] + ':' + config['couchdbpassword']).encode()).decode()

    def request(card_id, data=None):
        req = urllib.request.Request(base + '/' + urllib.parse.quote(card_id, safe=''),
            data=None if data is None else json.dumps(data).encode(),
            headers={'Authorization': auth, 'Content-Type': 'application/json'},
            method='GET' if data is None else 'PUT')
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)

    before = [request(card_id) for card_id in CARDS]
    after = build(before)
    changes = [(old, new) for old, new in zip(before, after) if old != new]
    assert build(after) == after
    print(json.dumps({'changes': [new['_id'] for _, new in changes],
        'tools': {c['_id']: c['tools'][1]['type'] for c in after}}), flush=True)
    if '--apply' not in sys.argv or not changes:
        return
    state = subprocess.check_output(['systemctl', 'show', 'bnlreloaded.service', '-p', 'ActiveState', '--value'], text=True).strip()
    assert state == 'inactive', 'Stop the game service before catalogue activation'
    backup = Path('/root/config-backups') / ('ninja-shuriken-hold-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / 'before.json').write_text(json.dumps(before, indent=2))
    saved = []
    try:
        for old, new in changes:
            result = request(new['_id'], new)
            saved.append((old, result['rev']))
            current = request(new['_id'])
            assert {k: v for k, v in current.items() if k != '_rev'} == {k: v for k, v in new.items() if k != '_rev'}
    except Exception:
        for old, revision in reversed(saved):
            old = dict(old, _rev=revision)
            request(old['_id'], old)
        raise
    print(json.dumps({'backup': str(backup), 'verified': len(saved)}), flush=True)


if __name__ == '__main__':
    main()
