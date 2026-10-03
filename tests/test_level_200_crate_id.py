import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('migration', Path(__file__).parents[1] / 'tools/fix_level_200_crate_id.py')
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)


class MigrationTests(unittest.TestCase):
    def test_plan_preserves_visual_and_source(self):
        doc = {'_id': migration.CARD, 'category': 'block', 'block_id': 58, 'visual': {'texture_indices': [[23] * 6]}}
        result = migration.plan({migration.CARD: doc})
        self.assertEqual(58, doc['block_id'])
        self.assertEqual(1051, result['block_id'])
        self.assertEqual(doc['visual'], result['visual'])
        self.assertEqual(result, migration.plan({migration.CARD: result}))

    def test_occupied_id_is_rejected(self):
        docs = {migration.CARD: {'block_id': 58}, 'other': {'_id': 'other', 'category': 'block', 'block_id': 1051}}
        with self.assertRaises(ValueError):
            migration.plan(docs)


if __name__ == '__main__':
    unittest.main()
