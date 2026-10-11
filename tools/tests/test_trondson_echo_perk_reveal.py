import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('echo', ROOT / 'add_trondson_echo_perk_reveal.py')
echo = importlib.util.module_from_spec(spec)
spec.loader.exec_module(echo)

class EchoPerkTests(unittest.TestCase):
    def setUp(self):
        self.before = {c['_id']: c for c in json.loads((ROOT / 'tests/fixtures/trondson_echo_before.json').read_text(encoding='utf-8'))}
        self.after = {k: echo.plan(v) for k, v in self.before.items()}

    def test_dependencies_and_scope(self):
        echo.validate_dependencies(self.before)
        self.assertEqual({k for k in self.before if self.before[k] != self.after[k]}, {echo.GEAR, echo.PERK})
        self.assertEqual(self.after[echo.BASE], self.before[echo.BASE])
        self.assertEqual(self.after[echo.MARKER], self.before[echo.MARKER])

    def test_preserves_existing_weapon_behavior(self):
        tools = copy.deepcopy(self.after[echo.GEAR]['tools'])
        reveal = tools[1]['hit_effect']['instant'].pop()
        self.assertEqual(tools, self.before[echo.GEAR]['tools'])
        self.assertEqual(reveal['targeting']['affected_team'], 'opponent')
        self.assertEqual(reveal['targeting']['affected_units'], ['player'])
        self.assertTrue(reveal['targeting']['ignore_caster'])
        self.assertEqual(reveal['constant'], [echo.MARKER])
        self.assertEqual(self.after[echo.MARKER]['duration'], 7)

    def test_repeat_is_noop(self):
        self.assertEqual({k: echo.plan(v) for k,v in self.after.items()}, self.after)

if __name__ == '__main__':
    unittest.main()
