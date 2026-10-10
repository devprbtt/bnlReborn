import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('history', Path(__file__).parents[1] / 'tools/preserve_cdb_history.py')
h = importlib.util.module_from_spec(spec); spec.loader.exec_module(h)


class FakeCouch:
    def __init__(self):
        self.docs = {
            '1-a': {'_id': 'gear_test', '_rev': '1-a', 'value': 1},
            '2-b': {'_id': 'gear_test', '_rev': '2-b', 'value': 2},
            '3-c': {'_id': 'gear_test', '_rev': '3-c', '_deleted': True},
        }
        self.fail = False
        self.since = []

    def request(self, path):
        if path == '': return {'uuid': 'test-server'}
        self.since.append(path)
        return {'results': [{'id': 'gear_test', 'changes': [{'rev': '3-c'}]}], 'last_seq': '10-token', 'pending': 0}

    def document(self, key, query):
        if self.fail and query['rev'] == '2-b': raise ConnectionError('transient failure')
        doc = copy.deepcopy(self.docs[query['rev']])
        if 'revs_info' in query:
            doc['_revs_info'] = [{'rev': r, 'status': 'deleted' if r == '3-c' else 'available'} for r in self.docs]
        return doc


class HistoryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.db = h.open_archive(Path(self.tmp.name) / 'history.sqlite3')
        self.couch = FakeCouch()

    def tearDown(self):
        self.db.close(); self.tmp.cleanup()

    def test_burst_edits_and_deletion_are_all_archived(self):
        self.assertEqual(h.snapshot(self.couch, self.db)['new_archived_revisions'], 3)
        self.assertEqual(h.export_revision(self.db, 'gear_test', '1-a')['value'], 1)
        self.assertTrue(h.export_revision(self.db, 'gear_test', '3-c')['_deleted'])
        self.assertEqual(h.snapshot(self.couch, self.db)['new_archived_revisions'], 0)
        self.assertIn('since=10-token', self.couch.since[-1])

    def test_failed_page_does_not_advance_checkpoint(self):
        self.couch.fail = True
        with self.assertRaises(ConnectionError): h.snapshot(self.couch, self.db)
        self.assertIsNone(self.db.execute("SELECT value FROM state WHERE key='since'").fetchone())
        self.couch.fail = False
        self.assertEqual(h.snapshot(self.couch, self.db)['total_archived_revisions'], 3)

    def test_archive_survives_source_history_removal(self):
        h.snapshot(self.couch, self.db)
        self.couch.docs.clear()
        self.assertEqual(h.export_revision(self.db, 'gear_test', '2-b')['value'], 2)

    def test_archive_detects_tampering(self):
        h.snapshot(self.couch, self.db)
        self.db.execute("UPDATE revisions SET body='{}' WHERE rev='1-a'")
        with self.assertRaisesRegex(ValueError, 'checksum'): h.export_revision(self.db, 'gear_test', '1-a')

    def test_same_revision_cannot_be_replaced(self):
        h.snapshot(self.couch, self.db)
        altered = dict(self.couch.docs['1-a'], value=99)
        with self.assertRaisesRegex(ValueError, 'mismatch'): h.save_revision(self.db, altered)

    def test_shard_scope_excludes_other_databases(self):
        root = Path(self.tmp.name)
        shard = root / 'shards/00000000-ffffffff'; shard.mkdir(parents=True)
        (shard / 'bnl.123.couch').touch(); (shard / '_users.123.couch').touch()
        self.assertEqual(h.shard_names(root), ['shards/00000000-ffffffff/bnl.123'])


if __name__ == '__main__': unittest.main()
