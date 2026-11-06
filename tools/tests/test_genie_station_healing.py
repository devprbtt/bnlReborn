import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('migration', Path(__file__).parents[1]/'match_genie_orb_station_healing.py')
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)

class GenieStationMigrationTests(unittest.TestCase):
    def setUp(self):
        self.cards = {
            migration.AURA: {'_id':migration.AURA, '_rev':'8-current', 'metadata':{'preserve':True},
                'effect':{'type':'aura', 'interval':.2, 'outer_radius':3,
                    'interval_effects':[{'type':'heal','player_heal':1.4,'world_heal':0}]}},
            migration.REGEN: {'effect':{'type':'buff','buffs':{'health_regen':10}}},
            **{name:{'enabled_effects':[migration.AURA]} for name in migration.ORBS},
        }

    def test_only_player_heal_changes_and_input_is_unchanged(self):
        before = copy.deepcopy(self.cards)
        after = migration.plan(self.cards)
        self.assertEqual(self.cards, before)
        self.assertEqual(after['effect']['interval_effects'][0]['player_heal'], 2)
        after['effect']['interval_effects'][0]['player_heal'] = 1.4
        self.assertEqual(after, before[migration.AURA])

    def test_idempotent(self):
        self.cards[migration.AURA] = migration.plan(self.cards)
        self.assertEqual(migration.plan(self.cards), self.cards[migration.AURA])

    def test_rejects_unreviewed_station_rate(self):
        self.cards[migration.REGEN]['effect']['buffs']['health_regen'] = 15
        with self.assertRaises(AssertionError): migration.plan(self.cards)

    def test_rejects_unreviewed_orb_rate(self):
        self.cards[migration.AURA]['effect']['interval_effects'][0]['player_heal'] = 3
        with self.assertRaises(AssertionError): migration.plan(self.cards)

    def test_rejects_unreviewed_interval(self):
        self.cards[migration.AURA]['effect']['interval'] = .5
        with self.assertRaises(AssertionError): migration.plan(self.cards)

    def test_requires_both_orbs_to_use_aura(self):
        self.cards[migration.ORBS[1]]['enabled_effects'] = []
        with self.assertRaises(AssertionError): migration.plan(self.cards)

if __name__ == '__main__': unittest.main()
