"""Guarded combined rollout. Preview by default; --apply needs the deployed revision.

Only public catalogue documents are handled. Host credentials never leave config.
"""
import argparse
import base64
import copy
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path


def content(card):
    return None if card is None else {k: v for k, v in card.items() if k != '_rev'}


class Couch:
    def __init__(self):
        cfg = {k.replace('_', '').lower(): v for k, v in json.loads(
            Path('/opt/bnlreloaded/current/Configs/configs.json').read_text()).items()}
        self.base = cfg['couchdbendpoint'].rstrip('/') + '/' + cfg['couchdbdatabasename']
        self.auth = 'Basic ' + base64.b64encode((cfg['couchdbusername'] + ':' + cfg['couchdbpassword']).encode()).decode()

    def request(self, cid, method='GET', body=None, revision=None):
        url = self.base + '/' + urllib.parse.quote(cid, safe='')
        if revision:
            url += '?rev=' + urllib.parse.quote(revision, safe='')
        req = urllib.request.Request(url, method=method, headers={
            'Authorization': self.auth, 'Content-Type': 'application/json'},
            data=None if body is None else json.dumps(body).encode())
        try:
            with urllib.request.urlopen(req, timeout=30) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if method == 'GET' and error.code == 404:
                return None
            raise


def preflight(db, plan):
    rows = plan['cards']
    assert plan['schema_version'] == 1 and len(rows) == 19
    assert len({r['id'] for r in rows}) == 19
    pending = []
    for row in rows:
        live = db.request(row['id'])
        if content(live) == content(row['after']):
            continue
        assert live == row['before'], 'Catalogue baseline changed: ' + row['id']
        assert (live or {}).get('_rev') == row['expected_revision'], row['id']
        assert row['after']['scope'] == 'public' and row['after']['_id'] == row['id']
        proposed = copy.deepcopy(row['after'])
        if live:
            proposed['_rev'] = live['_rev']
        else:
            proposed.pop('_rev', None)
        pending.append((row['id'], live, proposed))
    # New wrappers must exist before any abilities can reference them.
    return sorted(pending, key=lambda item: (item[1] is not None, item[0]))


def rollback(db, attempted):
    diverged = []
    for cid, old, proposed in reversed(attempted):
        current = db.request(cid)
        if content(current) == content(old):
            continue
        if content(current) != content(proposed):
            diverged.append(cid)
            continue
        if old is None:
            if diverged:
                continue  # Keep wrappers if a concurrent ability may still need them.
            db.request(cid, 'DELETE', revision=current['_rev'])
        else:
            db.request(cid, 'PUT', dict(old, _rev=current['_rev']))
        assert content(db.request(cid)) == content(old), 'Rollback readback failed: ' + cid
    if diverged:
        raise RuntimeError('Concurrent edits require manual rollback review: ' + ', '.join(diverged))


def apply(db, pending):
    attempted = []
    try:
        for cid, old, proposed in pending:
            assert db.request(cid) == old, 'Concurrent catalogue edit: ' + cid
            attempted.append((cid, old, proposed))  # A lost response may still commit.
            db.request(cid, 'PUT', proposed)
            assert content(db.request(cid)) == content(proposed), 'Readback failed: ' + cid
    except Exception:
        # Reverse order restores ability references before removing new wrappers.
        rollback(db, attempted)
        raise
    return len(attempted)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('patch', type=Path)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--required-revision')
    args = parser.parse_args()
    plan = json.loads(args.patch.read_text())
    db = Couch()
    pending = preflight(db, plan)
    print(json.dumps({'dry_run': not args.apply, 'pending_cards': len(pending),
                      'already_applied': len(plan['cards']) - len(pending)}), flush=True)
    if not args.apply or not pending:
        return
    assert args.required_revision and Path('/opt/bnlreloaded/current/REVISION').read_text().strip() == args.required_revision
    assert os.geteuid() == 0
    backup = Path('/root/config-backups') / ('gameplay-catalogue-' +
        time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()) + '-' + uuid.uuid4().hex[:8])
    backup.mkdir(mode=0o700)
    (backup / 'before.json').write_text(json.dumps({cid: old for cid, old, _ in pending}, indent=2))
    (backup / 'patch.json').write_text(json.dumps(plan, indent=2))
    print('CATALOGUE_BACKUP=' + str(backup), flush=True)
    applied = apply(db, pending)
    print(json.dumps({'applied_cards': applied, 'backup': str(backup)}), flush=True)


if __name__ == '__main__':
    main()
