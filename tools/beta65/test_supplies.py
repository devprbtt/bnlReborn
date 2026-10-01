import unittest,copy
import test_shared_world
from loadout import key,pack
from supply_systems import supply_packet

class SupplyTests(unittest.TestCase):
 def setUp(self):
  self.fixture=test_shared_world.SharedWorldTests();self.fixture.setUp();self.w=self.fixture.w;self.p=self.fixture.a;self.now=self.fixture.clock
  self.p.definitions.update({
   'marker':{'labels':['drop_point_resource','drop_point_blockbuster']},
   'unit_supply_resource':{'_id':'unit_supply_resource','labels':['supply_resource'],'movement':{'type':'falling','start_speed':10,'gravity':0},'data':{'type':'pickup','timeout':299,'take_effect':{'type':'resource_all','affected_team':'friendly','include_dead_players':True,'resource':500,'supply':True}}},
   'unit_supply_blockbuster_classic':{'_id':'unit_supply_blockbuster_classic','labels':['supply_blockbuster'],'lifetime':150,'health':{'health':{'max_health':600}},'data':{'type':'common'},'loot':{'loot_item':{'item':{'loot_unit_key':'unit_pickup_blockbuster_classic'}}}},
   'unit_pickup_blockbuster_classic':{'_id':'unit_pickup_blockbuster_classic','labels':['supply_blockbuster'],'lifetime':29,'data':{'type':'pickup','take_effect':{'type':'all_units_bunch','constant':['effect_blockbuster_classic_buff'],'targeting':{'affected_units':['player'],'affected_team':'friendly'}}}},
   'effect_blockbuster_classic_buff':{'duration':90,'effect':{'type':'buff','buffs':{'world_damage':1,'objective_damage':1}}},
   'effect_status_shielded_bb':{'duration':90,'effect':{'type':'buff','buffs':{'shield':1}}},
  })
  for p in self.w.players.values():
   p.packets['unit-templates']={name+'-0':b'\x06\x08'+pack('I',100)+pack('fff',101.25,102.5,103.75) for name in self.p.definitions if name.startswith('unit_')}
   p.packets['practice']['drop_points']=[{'unit_key':'marker','position':dict(x=4,y=2,z=4)}]
   p.packets['practice']['supply_logic']={'sequence':[{'seconds':5,'supply_unit_key':'unit_supply_resource','drop_point_label':'drop_point_resource'}], 'repeat_sequence':[{'seconds':7,'supply_unit_key':'unit_supply_blockbuster_classic','drop_point_label':'drop_point_blockbuster'}],'spawn_height':10}
  self.fixture.assault()
 def tearDown(self):self.fixture.tearDown()
 def test_schedule_no_build_drop_then_repeat_once_for_shared_world(self):
  self.assertFalse(self.p.placed);self.now[0]=6.2;self.w.tick()
  self.assertEqual(len(self.p.placed),1);entry=next(iter(self.p.placed.values()));self.assertEqual(entry['position'],(4,12,4));self.assertEqual(entry['team'],0)
  self.assertEqual(self.p.supply_state['index'],1);self.now[0]=13.3;self.w.tick();self.assertEqual(self.p.supply_state['index'],2)
  self.assertEqual(sum(k=='supply_dropped' for k,v in self.fixture.events),2)
 def test_collect_enemy_team_rewards_including_dead_only_once(self):
  u=self.p.spawn_unit('unit_supply_resource',(4,1,4),0,owner=0);dead=self.w.players[4];dead.player_respawn_at=999
  before={i:p.resources for i,p in self.w.players.items()}
  entry=self.p.placed[u];self.p.collect_pickup(u,entry,3);self.p.collect_pickup(u,entry,3)
  self.assertNotIn(u,self.p.placed)
  for i,p in self.w.players.items():self.assertEqual(p.resources,before[i]+(500 if i in (3,4) else 0))
 def test_crate_destroyed_spawns_neutral_loot_then_team_buff(self):
  u=self.p.spawn_unit('unit_supply_blockbuster_classic',(4,1,4),0,owner=0)
  self.p.damage_entity(u,600,self.p.current,owner=3)
  loot=next(i for i,d in self.p.placed.items() if d['definition']['_id']=='unit_pickup_blockbuster_classic')
  self.assertEqual(self.p.team(loot),0);self.p.collect_pickup(loot,self.p.placed[loot],3)
  self.assertEqual(self.p.buffs_for(3)['world_damage'],1);self.assertEqual(self.p.buffs_for(4)['world_damage'],1);self.assertFalse(self.p.buffs_for(1))
  self.now[0]+=90.1;self.assertFalse(self.p.buffs_for(3))
 def test_closed_crate_timeout_opens_but_pickup_timeout_does_not(self):
  u=self.p.spawn_unit('unit_supply_blockbuster_classic',(4,2,4),0,owner=0)
  self.now[0]+=150.1;self.p.tick_heroes();self.assertNotIn(u,self.p.placed)
  self.assertEqual(len(self.p.placed),1);self.now[0]+=29.1;self.p.tick_heroes();self.assertFalse(self.p.placed)
 def test_blockbuster_replacement_not_stacking(self):
  for _ in range(2):
   u=self.p.spawn_unit('unit_pickup_blockbuster_classic',(4,1,4),0,owner=0);self.p.collect_pickup(u,self.p.placed[u],1)
  self.assertEqual(self.p.buffs_for(1)['world_damage'],1)
 def test_zone_buff_returns_after_respawn_without_extending_expiry(self):
  self.p.apply_effect({'type':'zone_effect','duration':90,'effects':['effect_blockbuster_classic_buff'],'targeting':{'affected_team':'friendly'}},self.p.position,self.p.position,self.p.current,3,3)
  self.p.statuses.pop(3);self.now[0]+=10;self.p.tick_supplies();self.assertEqual(self.p.statuses[3]['effect_blockbuster_classic_buff'],91.1)
  self.now[0]=91.2;self.p.tick_supplies();self.assertFalse(self.p.buffs_for(3));self.assertFalse(self.p.supply_state['zones'])
 def test_shield_reduces_damage_and_expires(self):
  self.p.add_status(3,'effect_status_shielded_bb',owner=3);self.p.damage_entity(3,80,self.p.current);self.assertEqual(self.w.players[3].player_health,120)
  self.now[0]+=91;self.p.damage_entity(3,80,self.p.current);self.assertEqual(self.w.players[3].player_health,40)
 def test_world_and_objective_damage_use_active_buff(self):
  self.p.add_status(1,'effect_blockbuster_classic_buff')
  self.assertEqual(self.p.damage_bonus(50,'world_damage'),100);self.assertEqual(self.p.damage_bonus(50,'objective_damage'),100);self.assertEqual(self.p.damage_bonus(50,'player_damage'),50)
  u=self.p.spawn_unit('unit_supply_blockbuster_classic',(4,1,4),0,owner=0)
  self.p.placed[u]['definition']=dict(self.p.placed[u]['definition'],labels=['objective']);self.p.placed[u]['team']=2;self.p.unit_teams[u]=2
  self.p.apply_effect({'type':'damage','damage':{'objective_damage':50}},(4,1,4),(4,1,4),self.p.current,u)
  self.assertEqual(self.p.placed[u]['health'],500)
 def test_falling_uses_configured_speed_zero_gravity_and_solid_floor(self):
  u=self.p.spawn_unit('unit_supply_resource',(4.5,3,4.5),0,owner=0);e=self.p.placed[u]
  self.p.fall_unit(u,e,.1);self.assertEqual(e['position'][1],2)
  self.p.block_cards[1]={'solid':True};self.p.blocks[self.p.cell_index((4,0,4))]=1
  self.p.fall_unit(u,e,.2);self.assertAlmostEqual(e['position'][1],1.05)
 def test_block_damage_buff_and_expiration_change_real_terrain_damage(self):
  self.p.block_cards[1]={'block_id':1,'destructible':True,'health':{'max_health':150}}
  self.p.blocks[self.p.cell_index((4,1,4))]=1
  self.p.add_status(1,'effect_blockbuster_classic_buff')
  self.p.blast_block((4,1,4),{'world_damage':40});self.assertEqual(self.p.block_damage[(4,1,4)],80)
  self.now[0]+=91;self.p.blast_block((4,1,4),{'world_damage':40});self.assertEqual(self.p.block_damage[(4,1,4)],120)
 def test_leader_change_preserves_schedule(self):
  old=self.p.supply_state['next']['at'];self.w.leader=self.w.players[2];self.now[0]=2;self.w.leader.tick_supplies();self.assertEqual(self.w.leader.supply_state['next']['at'],old)
 def test_end_match_stops_drops(self):
  self.w.finished=True;self.now[0]=99;self.p.tick_supplies();self.assertFalse(self.p.placed)
 def test_missing_marker_skips_event_without_stalling_future_drops(self):
  self.p.packets['practice']['drop_points']=[];self.p.schedule_supply(self.now[0]);self.now[0]+=5.1;self.p.tick_supplies()
  self.assertEqual(self.p.supply_state['index'],1);self.assertFalse(self.p.placed)
 def test_effect_icons_are_cleared_after_expiration(self):
  self.p.add_status(1,'effect_blockbuster_classic_buff');self.p.tick_supplies()
  prefix=b'\x06\x09'+pack('I',1)+b'\x00\x04\x00'
  self.assertTrue(any(p.startswith(prefix+b'\x01') for p in self.fixture.sent[1]))
  self.now[0]+=91;self.p.tick_supplies();self.assertIn(prefix+b'\x00',self.fixture.sent[1])
 def test_supply_wire_fixture_shape(self):
  self.assertEqual(supply_packet('unit_supply_resource',123456),b'\x06\x07\x02\xc0'+key('unit_supply_resource')+pack('Q',123456))

if __name__=='__main__':unittest.main()
