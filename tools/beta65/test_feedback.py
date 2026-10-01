"""Regressions for reported beta feedback and control failures."""
import math
import struct
import unittest
import test_practice as fixtures
from practice import pack,Reader
from loadout import key

class FeedbackTests(unittest.TestCase):
 def setUp(self):fixtures.PracticeTests.setUp(self)
 def buffs(self,unit):
  prefix=b'\x06\x09'+pack('I',unit)+b'\x00\x02\x00'
  payload=next(p[len(prefix):] for p in reversed(self.sent) if p.startswith(prefix))
  r=Reader(payload);result={r.read('B'):r.read('f') for _ in range(r.size())};r.end();return result
 def test_expired_root_key_is_absent_in_client_snapshot(self):
  self.packets['buff-ids']={'root':39,'run_speed':28}
  self.s.definitions={'root':{'duration':1.7,'effect':{'type':'buff','buffs':{'root':1}}},'slow':{'duration':4,'effect':{'type':'buff','buffs':{'run_speed':-.7}}}}
  self.s.add_status(1,'root');self.s.add_status(1,'slow');self.s.tick()
  self.assertIn(39,self.buffs(1))
  self.now=1.8;self.s.tick();self.assertNotIn(39,self.buffs(1));self.assertIn(28,self.buffs(1))
  self.now=4.1;self.s.tick();self.assertEqual(self.buffs(1),{})
 def test_radar_preserves_other_buffs_and_removes_mark_key(self):
  self.packets['buff-ids']={'vision_mark':18,'root':39}
  self.s.definitions['root']={'duration':3,'effect':{'type':'buff','buffs':{'root':1}}}
  self.s.add_status(2,'root');self.s.radar_marked=True;self.s.publish_buffs(2)
  self.assertEqual(self.buffs(2),{18:1,39:1})
  self.s.radar_marked=False;self.s.publish_buffs(2);self.assertEqual(self.buffs(2),{39:1})
 def test_healthless_cloud_is_not_destroyed_by_damage(self):
  self.s.placed[100]={'definition':{'_id':'cloud','health':None},'health':1}
  self.s.damage_entity(100,100,self.key);self.assertIn(100,self.s.placed);self.assertFalse(self.sent)
 def test_cloud_sends_many_air_cells_without_crossing_wall(self):
  for y in range(4):
   for z in range(8):self.s.blocks[self.s.cell_index((3,y,z))]=1
  self.s.fill_cloud(100,(1.5,1.5,2.5),5)
  r=Reader(self.sent[-1][9:]);cells=[r.read('hhh') for _ in range(r.size())];r.end()
  self.assertGreater(len(cells),8);self.assertTrue(all(c[0]<3 for c in cells))
 def test_fire_pattern_uses_supported_air_and_expires(self):
  fire={'_id':'fire','block_id':32,'health':{'max_health':6}}
  self.s.definitions['fire']=fire;self.s.passable.add(32)
  for x in range(8):
   for z in range(8):self.s.blocks[self.s.cell_index((x,0,z))]=1
  self.s.apply_effect({'type':'blocks_spawn','pattern':{'type':'sphere','block_key':'fire','radius':1,'fill_rate':1}},(4.5,1.5,4.5),(4.5,1.5,4.5),self.key)
  self.assertEqual(len(self.s.fire_cells),5)
  for cell in self.s.fire_cells:self.assertEqual(self.s.blocks[self.s.cell_index(cell)+3],1)
  self.now=6.1;self.s.tick();self.assertFalse(self.s.fire_cells);self.assertNotIn(32,self.s.blocks[::4])
 def test_mining_sends_impact_before_break_and_break_flag(self):
  cell=(3,1,2);self.s.block_cards[1]={'block_id':1,'destructible':True,'health':{'max_health':20}}
  self.s.blocks[self.s.cell_index(cell)]=1
  self.s.damage_block((3.01,1.5,2.5),{'hit_effect':{'type':'damage','impact':'impact_melee_common','damage':{'world_damage':25}}},(1,1.5,2.5))
  self.assertEqual(self.sent[0][:2],b'\x06\x12')
  update=next(p for p in self.sent if p[:2]==b'\x06\x06');self.assertEqual(update[-4:],bytes([0,0,1,0]))
 def test_mortar_waits_then_rises_and_does_not_damage_early(self):
  self.s.placed[100]={'device':'device_cogwheel_mortar','position':(1.5,1,2.5),'team':1,'created':0,'definition':{'data':{'type':'mortar','angle':80,'projectile_key':'shell'}}}
  self.s.extra_effect({'type':'fire_mortars','base_fire_delay':.5,'hit_effect':{'type':'damage','damage':{'world_damage':0}}},(5.5,1,2.5),(1,1,2),self.key,None)
  self.assertFalse(self.sent);f=next(iter(self.s.mortar_flights.values()))
  self.now=.4;self.s.tick_heroes();self.assertFalse(self.sent)
  self.now=.5;self.s.tick_heroes();self.assertTrue(any(p[:2]==b'\x06\x34' for p in self.sent))
  self.now=.5+f['duration']/2;self.s.tick_heroes()
  move=next(p for p in reversed(self.sent) if p[:2]==b'\x06\x35');pos=struct.unpack_from('<fff',move,12)
  self.assertGreater(pos[1],f['start'][1]+2);self.assertTrue(self.s.mortar_flights)
  self.now=.6+f['duration'];self.s.tick_heroes();self.assertFalse(self.s.mortar_flights)
 def test_falling_bomb_reaches_ground_and_blast_damages_voxels(self):
  self.s.block_cards[1]={'block_id':1,'destructible':True,'health':{'max_health':100}}
  self.s.blocks[self.s.cell_index((3,0,3))]=1
  d={'_id':'bomb','movement':{'type':'falling','gravity':10},'data':{'type':'bomb','timeout':5,'trigger_effect':{'type':'splash_damage','radius':3,'damage':{'player_damage':0,'world_damage':50}}}}
  self.s.placed[100]={'device':'bomb','definition':d,'position':(3.5,3.5,3.5),'cell':(3,3,3),'health':100,'created':0,'team':1}
  for n in range(1,51):self.now=n/10;self.s.tick()
  self.assertNotIn(100,self.s.placed);self.assertEqual(self.s.block_damage[(3,0,3)],50)

if __name__=='__main__':unittest.main()
