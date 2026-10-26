import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("mobile", ROOT / "tools/remove_mobile_weapon_spread.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class MobileWeaponSpreadTests(unittest.TestCase):
    def setUp(self):
        self.before = {c["_id"]: c for c in json.loads(
            (ROOT / "tests/fixtures/mobile-weapon-spread.json").read_text(encoding="utf-8"))}

    def test_exact_field_changes_and_idempotence(self):
        original = copy.deepcopy(self.before)
        after = m.plan(self.before)
        self.assertEqual(original, self.before)
        self.assertEqual(after, m.plan(after))
        delta = m.changes(self.before, after)
        self.assertEqual(len(delta), 37)
        self.assertEqual(sum(map(len, delta.values())), 43)
        for key in delta:
            restored = copy.deepcopy(after[key])
            for path, cone in m.cones(restored):
                self.assertEqual((cone["move_mod"], cone["jump_mod"]), (0, 0))
                old = dict(m.cones(self.before[key]))[path]
                for field in ("move_mod", "jump_mod"):
                    cone[field] = old[field]
            self.assertEqual(restored, self.before[key], key)

    def test_nigel_and_unrelated_cards_unchanged(self):
        self.before["gear_unrelated_test"] = dict(copy.deepcopy(self.before["gear_sarge_stone_m60"]), _id="gear_unrelated_test")
        after = m.plan(self.before)
        for key in self.before:
            if key.startswith("gear_hunter_") or key.startswith("unit_hero_") or key == "gear_unrelated_test":
                self.assertEqual(after[key], self.before[key], key)
        for key in ("gear_djinn_orbs", "gear_djinn_orbs_speed_ball", "gear_boxer_blitz_stance"):
            self.assertEqual(after[key], self.before[key])

    def test_sarge_hip_and_ads_and_perk_variants(self):
        after = m.plan(self.before)
        gun = after["gear_sarge_stone_m60"]
        self.assertEqual(gun["cone_of_fire"]["angle"], 1)
        aimed = [c for path, c in m.cones(gun) if path != "cone_of_fire"][0]
        self.assertEqual(aimed["angle"], .5)
        for key in ("gear_astro_saucer_launcher_fly_away_bad_guy", "gear_engineer_rivet_gun_super_rivet_gun",
                    "gear_ninja_shuriken_bloody_bleeding"):
            self.assertTrue(all(c["move_mod"] == c["jump_mod"] == 0 for _, c in m.cones(after[key])))

    def test_incomplete_or_invalid_catalogue_rejected(self):
        incomplete = copy.deepcopy(self.before)
        incomplete.pop(m.NIGEL)
        with self.assertRaises(ValueError):
            m.plan(incomplete)
        for bad in (None, "15", float("nan")):
            invalid = copy.deepcopy(self.before)
            invalid["gear_sarge_stone_m60"]["cone_of_fire"]["jump_mod"] = bad
            with self.assertRaises(ValueError):
                m.plan(invalid)

    def store(self, fail_at=None):
        live = copy.deepcopy(self.before)
        writes = []

        def request(key, doc=None):
            if key.startswith("_all_docs?"):
                return {"rows": [{"doc": copy.deepcopy(c)} for c in live.values()]}
            if doc is None:
                return copy.deepcopy(live[key])
            if key == fail_at:
                raise RuntimeError("simulated write failure")
            if doc["_rev"] != live[key]["_rev"]:
                raise RuntimeError("revision conflict")
            writes.append(key)
            live[key] = copy.deepcopy(doc)
            live[key]["_rev"] = str(len(writes)) + "-test"
            return {"rev": live[key]["_rev"]}

        return live, writes, request

    def test_apply_readback_backup_and_second_run_noop(self):
        live, writes, request = self.store()
        after = m.plan(self.before)
        with tempfile.TemporaryDirectory() as tmp:
            backup = m.apply(request, self.before, after, Path(tmp))
            self.assertTrue((Path(backup) / "before.json").is_file())
            self.assertEqual(len(writes), 37)
            self.assertEqual(m.changes(live, m.plan(live)), {})
            self.assertIsNone(m.apply(request, live, m.plan(live), Path(tmp)))

    def test_failed_write_rolls_back_and_concurrent_change_blocks(self):
        live, writes, request = self.store("gear_astro_raygun_auto")
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(RuntimeError):
                m.apply(request, self.before, m.plan(self.before), Path(tmp))
        for key in live:
            self.assertEqual({k: v for k, v in live[key].items() if k != "_rev"},
                             {k: v for k, v in self.before[key].items() if k != "_rev"})
        live, writes, request = self.store()
        live["gear_hunter_rifle"]["_rev"] = "changed"
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(RuntimeError):
                m.apply(request, self.before, m.plan(self.before), Path(tmp))
        self.assertEqual(writes, [])


if __name__ == "__main__":
    unittest.main()
