import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("defaults", Path(__file__).parents[1] / "set_class_default_loadouts.py")
defaults = importlib.util.module_from_spec(spec)
spec.loader.exec_module(defaults)


class DefaultLoadoutsTest(unittest.TestCase):
    def setUp(self):
        self.cards = {key: {"_id": key, "category": "device"}
                      for devices in defaults.DEFAULTS.values() for key in devices}
        for kind in ("brawn", "skills", "brains"):
            self.cards[kind] = {"_id": kind, "category": "hero_class", "type": kind}
            for number in range(5):
                key = "unit_hero_" + kind + str(number)
                self.cards[key] = {"_id": key, "_rev": "1-original", "category": "unit",
                    "data": {"type": "player", "class": kind, "default_devices": ["old"] * 5,
                             "special_devices": ["signature", "variant"], "skins": ["skin"]}}

    def test_every_hero_order_and_only_default_field_changes(self):
        before = copy.deepcopy(self.cards)
        writes, after, audit = defaults.plan(self.cards)
        self.assertEqual(15, len(writes))
        self.assertEqual(15, len(audit))
        expected = {
            "brawn": ["device_block_crate", "device_block_brick", "device_generic_landmine", "device_generic_respawn_point", "device_generic_radar"],
            "skills": ["device_block_crate", "device_generic_bomb", "device_generic_bear_trap", "device_block_bounce", "device_block_fast_movement"],
            "brains": ["device_block_crate", "device_block_brick", "device_generic_fire_trap", "device_generic_heal_station", "device_generic_ammo_resupply"],
        }
        for key, card in writes.items():
            self.assertEqual(expected[card["data"]["class"]], card["data"]["default_devices"])
            restored = copy.deepcopy(card)
            restored["data"]["default_devices"] = before[key]["data"]["default_devices"]
            self.assertEqual(before[key], restored)
        self.assertEqual(before, self.cards)
        self.assertEqual({}, defaults.plan(after)[0])

    def test_missing_or_wrong_device_rejected(self):
        del self.cards["device_generic_bomb"]
        with self.assertRaises(ValueError):
            defaults.plan(self.cards)
        self.cards["device_generic_bomb"] = {"category": "effect"}
        with self.assertRaises(ValueError):
            defaults.plan(self.cards)

    def test_unknown_class_rejected(self):
        self.cards["unit_hero_skills0"]["data"]["class"] = "missing"
        with self.assertRaises(ValueError):
            defaults.plan(self.cards)

    def test_non_player_and_saved_loadout_untouched(self):
        self.cards["npc"] = {"_id": "npc", "category": "unit", "data": {"type": "npc"}}
        self.cards["profile"] = {"_id": "profile", "hero_loadouts": {"devices": ["custom"]}}
        self.cards["unit_device_generic_test_dummy_player"] = {"_id": "unit_device_generic_test_dummy_player", "category": "unit", "data": {"type": "player", "class": "skills", "default_devices": ["old"]}}
        _, after, _ = defaults.plan(self.cards)
        self.assertEqual(self.cards["npc"], after["npc"])
        self.assertEqual(self.cards["profile"], after["profile"])
        self.assertEqual(self.cards["unit_device_generic_test_dummy_player"], after["unit_device_generic_test_dummy_player"])

    def test_independent_default_lists(self):
        _, after, _ = defaults.plan(self.cards)
        after["unit_hero_skills0"]["data"]["default_devices"].clear()
        self.assertEqual(5, len(after["unit_hero_skills1"]["data"]["default_devices"]))


if __name__ == "__main__":
    unittest.main()
