import unittest
from types import SimpleNamespace
import test_practice
from loadout import pack

class NinjaClimbTests(unittest.TestCase):
 def setUp(self):
  test_practice.PracticeTests.setUp(self)
  self.s.packets['practice']['hero']='ninja'
  self.s.packets['buff-ids']={'wall_climb':19,'root':39}
  self.s.definitions.update({'ninja':{'data':{'passive':['climb']}},'climb':{'effect':{'type':'buff','buffs':{'wall_climb':6}}},'root':{'duration':1,'effect':{'type':'buff','buffs':{'root':1}}}})
 def test_innate_buff_published_without_timed_status(self):
  self.s.tick_systems()
  self.assertIn(b'\x06\x09'+pack('I',self.s.unit)+b'\x00\x02\x00\x01'+pack('Bf',19,6),self.sent)
  self.assertEqual(self.s.buffs_for(self.s.unit),{'wall_climb':6})
 def test_expiring_status_keeps_climb_and_long_match_does_not_expire_it(self):
  self.s.add_status(self.s.unit,'root');self.s.tick_systems();self.assertEqual(self.s.buffs_for(self.s.unit)['wall_climb'],6)
  self.now=7200;self.s.tick_systems();self.assertEqual(self.s.buffs_for(self.s.unit),{'wall_climb':6})
 def test_death_cache_clear_and_respawn_republish(self):
  self.s.tick_systems();self.s.player_respawn_at=1;self.s.statuses.clear();self.s.buff_cache.clear();self.sent.clear()
  self.s.tick_systems();self.assertFalse(any(p[:9]==b'\x06\x09'+pack('I',self.s.unit)+b'\x00\x02\x00' for p in self.sent))
  self.s.player_respawn_at=None;self.s.tick_systems();self.assertEqual(self.s.buff_cache[self.s.unit],{19:6})
 def test_other_heroes_and_practice_target_do_not_get_climb(self):
  self.assertNotIn('wall_climb',self.s.buffs_for(2));self.s.packets['practice']['hero']='sarge';self.assertNotIn('wall_climb',self.s.buffs_for(self.s.unit))
 def test_shared_world_uses_recipient_hero(self):
  other=SimpleNamespace(packets={'practice':{'hero':'sarge'}})
  self.s.world=SimpleNamespace(players={1:other,3:self.s})
  self.assertNotIn('wall_climb',self.s.buffs_for(1));self.assertEqual(self.s.buffs_for(3),{'wall_climb':6})
