import copy
import importlib.util
import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("rework",ROOT/"tools/rework_astarella_saucers.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class ReworkTests(unittest.TestCase):
    def setUp(self):
        self.before={c["_id"]:c for c in json.loads((ROOT/"tests/fixtures/astarella-saucers.json").read_text())}
    def test_modes_and_idempotence(self):
        for mode,base,perk,world in [("confirmed",24,21,26)]:
            with self.subTest(mode=mode):
                original=copy.deepcopy(self.before);after=m.plan(self.before)
                self.assertEqual(self.before,original)
                self.assertEqual(m.plan(after),after)
                self.assertEqual(m.effect(after[m.BASE],"splash_damage")["damage"]["player_damage"],base)
                self.assertEqual(m.effect(after[m.PERK],"splash_damage")["damage"]["player_damage"],perk)
                self.assertEqual(m.effect(after[m.PERK],"splash_damage")["damage"]["world_damage"],world)
                for key in (m.BASE,m.PERK):
                    self.assertEqual(m.effect(after[key],"knockback")["force"],8.5)
                    self.assertEqual(m.effect(after[key],"knockback")["midair_force"],8.5)
                    for field in ("effect_range","affect_caster","linear_falloff"):
                        self.assertEqual(m.effect(after[key],"knockback")[field],m.effect(original[key],"knockback")[field])
                self.assertEqual(after[m.GEAR],original[m.GEAR])
                for key in (m.GEAR,m.PERK_GEAR):
                    self.assertEqual(after[key]["ammo"],original[key]["ammo"])
                    self.assertEqual(after[key]["tools"][1],original[key]["tools"][1])
                    self.assertEqual(after[key]["reload"]["reload_time"],2)
                for key in (m.CARD,m.SHOP):
                    self.assertIn("50% faster",after[key]["description"]["text"])
                    self.assertEqual(after[key]["description"]["data"],{})
    def test_fifty_percent_throughout_flight(self):
        after=m.plan(self.before)
        for t in (0,.016,.1,.5,.85):
            base=min(1+100*t,100);perk=min(after[m.PERK_GEAR]["tools"][0]["bullet"]["min_speed"]+after[m.PERK]["data"]["acceleration"]*t,after[m.PERK]["data"]["max_speed"])
            self.assertAlmostEqual(perk,base*1.5)
        self.assertEqual(after[m.PERK]["data"]["movement_timeout"],.85)
    def test_unexpected_values_fail_before_writing(self):
        for key,path in [(m.BASE,("data","acceleration")),(m.PERK,("data","max_speed")),(m.PERK_GEAR,("reload","reload_time"))]:
            data=copy.deepcopy(self.before);data[key][path[0]][path[1]]=999
            with self.assertRaises(AssertionError):m.plan(data)
if __name__=="__main__":unittest.main()
