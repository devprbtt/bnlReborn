"""Regression coverage for protocol-65 optional fields and hero effects."""
import unittest
import test_practice as fixtures
from practice import pack
from loadout import key

class HeroTests(unittest.TestCase):
 def setUp(self):fixtures.PracticeTests.setUp(self)
 def test_self_ability_without_shot_fields_consumes_one_charge(self):
  self.s.ability={'_id':'self','application':{'type':'self'},'charges':{'max_charges':1,'charge_cooldown':5},'hit_effect':{'type':'heal','player_heal':10}}
  self.s.charges=1;self.s.player_health=40
  req=b'\x06\x2f\x01\x00\x80'+key('self');self.s.handle(req)
  self.assertEqual((self.s.charges,self.s.player_health),(0,50))
  self.now=1;self.s.handle(req);self.assertEqual(self.s.player_health,50)
  self.assertIn(req[:4]+b'\x00\x00',self.sent)
 def test_null_shot_id_allowed_for_hitscan_ability(self):
  self.s.ability={'_id':'scan','application':{'type':'hitscan','range':20},'charges':{'max_charges':1,'charge_cooldown':5},'hit_effect':{'type':'all_units_bunch','range':3,'targeting':{'affected_team':'opponent'},'constant':['mark']}}
  self.s.definitions['mark']={'effect':{'type':'buff','buffs':{'vision_mark':1}},'duration':2};self.s.charges=1
  self.s.handle(b'\x06\x2f\x01\x00\xe0'+key('scan')+pack('fff',1,1,2)+b'\x01'+pack('fff',5,1,2)+b'\x00')
  self.assertEqual(self.s.charges,0);self.assertEqual(self.s.buffs_for(2)['vision_mark'],1)
  self.assertFalse(self.s.buffs_for(1));self.now=3;self.s.tick();self.assertFalse(self.s.buffs_for(2))
 def test_melee_variable_hits_and_target_distance_validation(self):
  t=self.s.weapons[self.key]['tools'][0];t.update(type='melee',range=5,hit_effect={'type':'bunch','instant':[{'type':'damage','damage':{'player_damage':25}}]})
  fixtures.PracticeTests.shoot(self);fixtures.PracticeTests.hit(self);self.assertEqual(self.s.target_health,135)
  self.now=1;self.s.handle(b'\x06\x1e\xe0\x00'+pack('fff',1,1,2)+b'\x00');self.assertEqual(self.s.last_cast,1)
 def test_teleport_rejects_outside_world(self):
  pos=self.s.position;self.assertFalse(self.s.teleport((500,1,500)));self.assertEqual(self.s.position,pos)
  self.assertTrue(self.s.teleport((3,1,3)));self.assertEqual(self.s.position,(3.5,1.1,3.5))
 def test_status_owner_keeps_enemy_aura_targeting(self):
  self.s.definitions={'aura':{'effect':{'type':'aura','outer_radius':5,'constant_effects':['slow']}},'slow':{'effect':{'type':'buff','targeting':{'affected_team':'opponent'},'buffs':{'run_speed':-.7}}}}
  self.s.add_status(2,'aura',2,owner=2,check=False);self.s.tick()
  self.assertEqual(self.s.buffs_for(1)['run_speed'],-.7);self.assertFalse(self.s.buffs_for(2))
 def test_interval_effect_runs_at_interval_not_every_tick(self):
  self.s.definitions={'gas':{'effect':{'type':'interval','interval':.5,'interval_effects':[{'type':'damage','damage':{'player_damage':5}}]}}}
  self.s.add_status(2,'gas',2);self.s.tick();self.assertEqual(self.s.target_health,155)
  self.now=.2;self.s.tick();self.assertEqual(self.s.target_health,155)
  self.now=.6;self.s.tick();self.assertEqual(self.s.target_health,150)
 def test_channel_rejects_remote_target_and_dead_player(self):
  self.s.weapons[self.key]['tools'][0]={'type':'channel','range':6,'interval':.3,'interval_effects':[]}
  req=b'\x06\x24\x01\x00\xd0\x00'+pack('fffI',1,1,2,2)
  self.s.handle(req);self.assertIsNone(self.s.channel)
  req=b'\x06\x24\x01\x00\xd0\x00'+pack('fffI',5,1,2,2)
  self.s.player_respawn_at=5;self.s.handle(req);self.assertIsNone(self.s.channel)
 def test_block_metadata_survives_damage(self):
  cell=(2,1,2);self.s.set_block(cell,9,vdata=1,ldata=1);self.s.set_block(cell,9,100)
  i=self.s.cell_index(cell);self.assertEqual(self.s.blocks[i:i+4],bytes([9,100,1,1]))

if __name__=='__main__':unittest.main()
