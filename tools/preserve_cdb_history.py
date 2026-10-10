"""Preserve Hercules card history without restarting CouchDB or the game server.

Run `protect --apply` once, then `snapshot` periodically. Credentials stay on the
server. The archive is independent of CouchDB compaction; `export` is read-only.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import time
import urllib.error
import urllib.parse
import urllib.request


class Couch:
    def __init__(self, config):
        c = {k.replace('_', '').lower(): v for k, v in json.loads(Path(config).read_text()).items()}
        self.endpoint = c['couchdbendpoint'].rstrip('/')
        self.database = c['couchdbdatabasename']
        if self.database != 'bnl':
            raise ValueError('This deployment tool is restricted to the bnl card database')
        self.auth = 'Basic ' + base64.b64encode(
            (c['couchdbusername'] + ':' + c['couchdbpassword']).encode()).decode()

    def request(self, path, value=None, method='GET'):
        req = urllib.request.Request(self.endpoint + '/' + path,
            data=None if value is None else json.dumps(value).encode(), method=method,
            headers={'Authorization': self.auth, 'Content-Type': 'application/json'})
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.load(response)

    def document(self, key, query):
        return self.request('bnl/' + urllib.parse.quote(key, safe='') + '?' + urllib.parse.urlencode(query))


def shard_names(root):
    names = sorted(str(p.relative_to(root).with_suffix('')).replace(os.sep, '/')
                   for p in Path(root).glob('shards/*/bnl.*.couch'))
    if not names or any(not re.fullmatch(r'shards/[0-9a-f]{8}-[0-9a-f]{8}/bnl\.[0-9]+', n) for n in names):
        raise ValueError('Cannot identify the local bnl shards safely')
    return names


def protect(couch, root, backup_root, apply):
    nodes = couch.request('_membership')['all_nodes']
    if len(nodes) != 1:
        raise ValueError('Inspect every node before using this tool on a cluster')
    section = '_node/' + urllib.parse.quote(nodes[0], safe='') + '/_config/smoosh.ignore'
    names = ['bnl'] + shard_names(root)
    previous = couch.request(section)
    result = {'database': 'bnl', 'protected_keys': names, 'apply': apply,
              'compaction_running': couch.request('bnl')['compact_running']}
    if apply:
        if result['compaction_running']:
            raise RuntimeError('Compaction is already running; preserve a physical backup before proceeding')
        backup = Path(backup_root) / ('cdb-history-' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
        backup.mkdir(mode=0o700, parents=True, exist_ok=False)
        (backup / 'smoosh-ignore.json').write_text(json.dumps(previous, indent=2))
        for name in names:
            couch.request(section + '/' + urllib.parse.quote(name, safe=''), 'true', 'PUT')
        current = couch.request(section)
        assert all(current.get(name) == 'true' for name in names)
        assert all(current.get(k) == v for k, v in previous.items() if k not in names)
        result['backup'] = str(backup)
    return result


def open_archive(path):
    path = Path(path)
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    db = sqlite3.connect(path, timeout=30)
    db.execute('PRAGMA synchronous=FULL')
    db.execute('CREATE TABLE IF NOT EXISTS revisions (id TEXT, rev TEXT, body TEXT NOT NULL, sha256 TEXT NOT NULL, captured TEXT NOT NULL, PRIMARY KEY(id,rev))')
    db.execute('CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT NOT NULL)')
    return db


def save_revision(db, doc):
    # Revision-discovery fields are not part of the revision body.
    doc = {k: v for k, v in doc.items() if k not in ('_revs_info', '_revisions', '_conflicts', '_deleted_conflicts')}
    body = json.dumps(doc, sort_keys=True, separators=(',', ':'), ensure_ascii=False)
    digest = hashlib.sha256(body.encode()).hexdigest()
    old = db.execute('SELECT sha256 FROM revisions WHERE id=? AND rev=?', (doc['_id'], doc['_rev'])).fetchone()
    if old and old[0] != digest:
        raise ValueError('Archived revision content mismatch: ' + doc['_id'])
    if old:
        return 0
    db.execute('INSERT INTO revisions VALUES (?,?,?,?,?)',
               (doc['_id'], doc['_rev'], body, digest, time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())))
    return 1


def snapshot(couch, db):
    identity = couch.request('')['uuid']
    stored = db.execute("SELECT value FROM state WHERE key='server_uuid'").fetchone()
    if stored and stored[0] != identity:
        raise ValueError('Archive belongs to another CouchDB instance')
    row = db.execute("SELECT value FROM state WHERE key='since'").fetchone()
    since = json.loads(row[0]) if row else 0
    saved = missing = changed = 0
    while True:
        query = urllib.parse.urlencode({'since': since, 'limit': 200, 'style': 'all_docs'})
        batch = couch.request('bnl/_changes?' + query)
        with db:
            for change in batch['results']:
                changed += 1
                key = change['id']
                if key.startswith('_design/'):
                    continue
                for leaf in change['changes']:
                    doc = couch.document(key, {'rev': leaf['rev'], 'revs_info': 'true', 'attachments': 'true'})
                    for revision in doc.get('_revs_info', []):
                        if revision['status'] == 'missing':
                            missing += 1
                            continue
                        rev = revision['rev']
                        if db.execute('SELECT 1 FROM revisions WHERE id=? AND rev=?', (key, rev)).fetchone():
                            continue
                        body = doc if rev == doc['_rev'] else couch.document(key, {'rev': rev, 'attachments': 'true'})
                        saved += save_revision(db, body)
                    saved += save_revision(db, doc)
            since = batch['last_seq']
            db.execute("INSERT OR REPLACE INTO state VALUES ('since',?)", (json.dumps(since),))
            db.execute("INSERT OR REPLACE INTO state VALUES ('server_uuid',?)", (identity,))
        if not batch.get('pending', 0):
            break
    return {'changed_documents': changed, 'new_archived_revisions': saved,
            'already_missing_observations': missing,
            'total_archived_revisions': db.execute('SELECT count(*) FROM revisions').fetchone()[0]}


def export_revision(db, key, rev):
    row = db.execute('SELECT body,sha256 FROM revisions WHERE id=? AND rev=?', (key, rev)).fetchone()
    if not row:
        raise ValueError('Revision is not in the archive')
    if hashlib.sha256(row[0].encode()).hexdigest() != row[1]:
        raise ValueError('Archive checksum failed')
    return json.loads(row[0])


def main():
    os.umask(0o077)
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--config', default='/opt/bnlreloaded/current/Configs/configs.json')
    p.add_argument('--archive', default='/var/lib/bnl-cdb-history/revisions.sqlite3')
    sub = p.add_subparsers(dest='action', required=True)
    protection = sub.add_parser('protect')
    protection.add_argument('--apply', action='store_true')
    protection.add_argument('--data-root', default='/var/lib/bnl-couchdb')
    protection.add_argument('--backup-root', default='/root/config-backups')
    sub.add_parser('snapshot')
    listing = sub.add_parser('list'); listing.add_argument('id')
    export = sub.add_parser('export'); export.add_argument('id'); export.add_argument('rev')
    args = p.parse_args()
    if args.action == 'protect':
        result = protect(Couch(args.config), args.data_root, args.backup_root, args.apply)
    else:
        with open_archive(args.archive) as db:
            if args.action == 'snapshot':
                result = snapshot(Couch(args.config), db)
            elif args.action == 'list':
                result = [{'rev': r[0], 'captured': r[1]} for r in db.execute(
                    'SELECT rev,captured FROM revisions WHERE id=? ORDER BY CAST(rev AS INTEGER) DESC', (args.id,))]
            else:
                result = export_revision(db, args.id, args.rev)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
