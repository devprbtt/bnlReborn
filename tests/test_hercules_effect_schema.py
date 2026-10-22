import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("migration", Path(__file__).parents[1] / "tools/prepare_hercules_effects.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class HerculesSchemaTests(unittest.TestCase):
    def schema(self):
        return {"_rev":"keep", "custom_types": {
            "BuffType":{"values":["player_damage","world_damage","other"]},
            "ConstEffectType":{"values":["buff","on_kill"]},
            "ConstEffect":{"children":{"buff":"ConstEffectBuff"}},
            "ConstEffectOnKill":{"kind":"record","parent":"ConstEffect","fields":{"effect":{"name":"InstEffect"}}},
            "CardEffect":{"fields":{m.BUFF:{"kind":"float"},"duration":{}}}}}
    def test_dropdown_and_variant(self):
        source=self.schema(); before=copy.deepcopy(source); out=m.update_schema(source); types=out["custom_types"]
        self.assertEqual(source,before)
        self.assertEqual(types["BuffType"]["values"],["player_damage","life_steal","world_damage",m.BUFF,"other"])
        self.assertEqual(types["ConstEffect"]["children"]["on_hit"],"ConstEffectOnHit")
        self.assertEqual(types["ConstEffectOnHit"]["fields"]["effect"]["name"],"InstEffect")
        self.assertNotIn(m.BUFF,types["CardEffect"]["fields"])
        self.assertEqual(m.update_schema(out),out)
        self.assertEqual(out["_rev"],"keep")
    def test_move_preserves_card(self):
        source={"_id":"effect_x","_rev":"keep","category":"effect",m.BUFF:.5,"effect":{"type":"buff","buffs":{"world_damage":.2},"targeting":{"ignore_caster":True}}}
        out=m.migrate_card(source)
        self.assertEqual(out["effect"]["buffs"],{"world_damage":.2,m.BUFF:.5})
        self.assertNotIn(m.BUFF,out)
        self.assertEqual(source[m.BUFF],.5)
        self.assertEqual(out["_rev"],"keep")
        self.assertEqual(out["effect"]["targeting"],source["effect"]["targeting"])
        self.assertEqual(m.migrate_card(out),out)
    def test_conflicts_and_unsupported_not_silently_changed(self):
        with self.assertRaises(ValueError):m.migrate_card({"category":"effect",m.BUFF:.5,"effect":{"type":"buff","buffs":{m.BUFF:.75}}})
        with self.assertRaises(ValueError):m.migrate_card({"category":"effect",m.BUFF:.5,"effect":{"type":"self"}})

if __name__ == "__main__":unittest.main()
