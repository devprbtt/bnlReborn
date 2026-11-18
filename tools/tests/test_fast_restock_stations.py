import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('migration', Path(__file__).parents[1] / 'fix_fast_restock_stations.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class FastRestockMigration(unittest.TestCase):
    def test_known_scopes_preserve_tuning_and_penalty(self):
        for scope in m.OLD_SCOPES:
            for name in m.IDS:
                before = {'_id': name, '_rev': '1-fixture', 'description': {
                    'text': '15% increased '+scope+' / Brick income reduced by 15%',
                    'data': {'pt': {'translation': 'stale'}}}, 'level': 3}
                original = copy.deepcopy(before)
                after = m.plan(before)
                self.assertEqual(before, original)
                self.assertEqual(after['description']['text'], '15% increased '+m.NEW_SCOPE+' / Brick income reduced by 15%')
                self.assertEqual(after['description']['data'], {})
                self.assertEqual(m.plan(after), after)
                self.assertEqual({k:v for k,v in after.items() if k!='description'}, {k:v for k,v in before.items() if k!='description'})

    def test_custom_text_and_translations_remain_editable(self):
        card = {'description': {'text': 'My custom description', 'data': {'pt': {'translation': 'custom'}}}}
        self.assertEqual(m.plan(card), card)

    def test_missing_description(self):
        self.assertEqual(m.plan({'_id': m.IDS[0]}), {'_id': m.IDS[0]})

if __name__ == '__main__':
    unittest.main()
