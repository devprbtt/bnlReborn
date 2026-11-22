"""Run on the game host: reviewed patch path [--apply]. Default is read-only.

Checks document revisions/field preconditions, preserves all other live fields,
backs up privately, applies guarded PUTs, reads back, and rolls back on failure.
"""
import argparse
import base64
import copy
import json
import time
import urllib.request
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('patch', type=Path)
parser.add_argument('--apply', action='store_true')
args = parser.parse_args()
patch = json.loads(args.patch.read_text())
assert patch['schema_version'] == 1 and len(patch['cards']) == 13
cfg = {k.replace('_', '').lower(): v for k, v in json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text()).items()}
base = cfg['couchdbendpoint'].rstrip('/') + '/' + cfg['couchdbdatabasename']
authorization = 'Basic ' + base64.b64encode((cfg['couchdbusername'] + ':' + cfg['couchdbpassword']).encode()).decode()

def request(cid, body=None):
    req = urllib.request.Request(base + '/' + cid, headers={'Authorization': authorization, 'Content-Type': 'application/json'},
                                 data=None if body is None else json.dumps(body).encode(), method='GET' if body is None else 'PUT')
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)

def parent(card, path):
    for key in path[:-1]:
        card = card[key]
    return card

def after_matches(card, changes):
    return all(parent(card, c['path']).get(c['path'][-1]) == c['after']
               if isinstance(parent(card, c['path']), dict) else parent(card, c['path'])[c['path'][-1]] == c['after'] for c in changes)

pending = []
for row in patch['cards']:
    live = request(row['id'])
    if after_matches(live, row['changes']):
        continue
    assert live['_rev'] == row['expected_revision'], 'Catalogue revision changed: ' + row['id']
    proposed = copy.deepcopy(live)
    for c in row['changes']:
        p, key = parent(proposed, c['path']), c['path'][-1]
        exists = key in p if isinstance(p, dict) else True
        assert exists == c['before_exists'] and (not exists or p[key] == c['before']), row['id'] + str(c['path'])
        p[key] = c['after']
    pending.append((row['id'], live, proposed))
print(json.dumps({'dry_run': not args.apply, 'pending_cards': len(pending), 'already_applied': len(patch['cards']) - len(pending)}), flush=True)
if not args.apply or not pending:
    raise SystemExit(0)
backup = Path('/root/config-backups') / ('six-hero-balance-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
backup.mkdir(mode=0o700)
(backup / 'before.json').write_text(json.dumps({cid: old for cid, old, _ in pending}, indent=2))
(backup / 'patch.json').write_text(json.dumps(patch, indent=2))
written = []
def content(card):
    return {k: v for k, v in card.items() if k != '_rev'}

try:
    for cid, old, proposed in pending:
        assert request(cid) == old, 'Concurrent catalogue edit: ' + cid
        # Include an attempted PUT: a lost response can still have committed it.
        written.append((cid, old, proposed))
        request(cid, proposed)
        saved = request(cid)
        assert content(saved) == content(proposed), cid
except Exception:
    diverged = []
    for cid, old, proposed in reversed(written):
        current = request(cid)
        if content(current) == content(old):
            continue
        if content(current) != content(proposed):
            diverged.append(cid)
            continue  # Never overwrite a concurrent author's later changes.
        request(cid, dict(old, _rev=current['_rev']))
        assert content(request(cid)) == content(old), 'Rollback readback failed: ' + cid
    if diverged:
        raise RuntimeError('Concurrent edits need manual rollback review: ' + ', '.join(diverged) + '; backup=' + str(backup))
    raise
print(json.dumps({'applied_cards': len(written), 'backup': str(backup)}), flush=True)
