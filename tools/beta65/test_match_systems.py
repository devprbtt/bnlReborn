import unittest
import test_practice as fixtures
from practice import pack
from loadout import key

class MatchTests(unittest.TestCase):
    def setUp(self):fixtures.PracticeTests.setUp(self)

    def test_fall_threshold_scaling_and_replayed_dead_hit(self):
        self.s.handle(b'\x06\x3d'+pack('If?',1,5,False));self.assertEqual(self.s.player_health,160)
        self.s.handle(b'\x06\x3d'+pack('If?',1,15,False));self.assertEqual(self.s.player_health,80)
        self.s.handle(b'\x06\x3d'+pack('If?',1,25,False));self.assertEqual(self.s.player_health,0)
        deadline=self.s.player_respawn_at;self.now=1
        self.s.handle(b'\x06\x3d'+pack('If?',1,25,False));self.assertEqual(self.s.player_respawn_at,deadline)

    def test_friendly_objective_and_build_phase_protected(self):
        d={'labels':['objective'],'health':{'health':{'max_health':100}}}
        self.s.placed[100]={'definition':d,'health':100,'team':1}
        self.s.damage_entity(100,50,self.key);self.assertEqual(self.s.placed[100]['health'],100)
        self.s.placed[100]['team']=2;self.s.phase_end=100
        self.s.damage_entity(100,50,self.key);self.assertEqual(self.s.placed[100]['health'],100)
        self.s.phase_end=None;self.s.damage_entity(100,50,self.key);self.assertEqual(self.s.placed[100]['health'],50)

    def test_drowning_grace_period_and_surface_cancels_damage(self):
        self.s.handle(b'\x06\x3c\x01');self.now=4.9;self.s.tick_match();self.assertEqual(self.s.player_health,160)
        self.now=5;self.s.tick_match();self.assertEqual(self.s.player_health,144)
        self.s.handle(b'\x06\x3c\x00');self.now=9;self.s.tick_match();self.assertEqual(self.s.player_health,144)

    def test_recall_cancels_on_movement_and_damage_then_completes(self):
        self.s.base_spawn=(2.5,1,2.5)
        self.s.handle(b'\x06\x4d');self.s.move((3,1,2));self.assertIsNone(self.s.recall_at)
        self.s.handle(b'\x06\x4d');self.s.damage_entity(1,1,self.key);self.assertIsNone(self.s.recall_at)
        self.s.handle(b'\x06\x4d');self.now=10;self.s.tick_match()
        self.assertEqual(self.s.position,(2.5,1.1,2.5));self.assertIn(b'\x06\x50'+pack('I',1),self.sent)

    def test_spawn_selection_falls_back_on_pad_destruction(self):
        self.s.base_spawn=(1,1,2)
        self.s.placed[100]={'definition':{'spawn_point':{}},'position':(4,1,2),'team':1}
        self.s.update_spawns();self.assertEqual(self.s.spawn_position,self.s.base_spawn)
        self.s.handle(b'\x06\x40\x01'+pack('I',100));self.assertEqual(self.s.spawn_position,(4,2,2))
        self.s.placed.clear();self.s.update_spawns();self.assertEqual(self.s.spawn_position,self.s.base_spawn)

    def test_build_phase_transitions_and_respawn_timer_is_published(self):
        self.s.packets['practice']['build_seconds']=2;self.s.start_match()
        self.assertTrue(any(p[:5]==b'\x06\x07\x80\xe0\x02' for p in self.sent))
        self.now=2;self.s.tick_match();self.assertIsNone(self.s.phase_end)
        self.assertIn(b'\x06\x4a\x00',self.sent)
        self.s.damage_entity(1,160,self.key)
        self.assertTrue(any(p[:4]==b'\x06\x07\x08\x01' for p in self.sent))

    def test_chat_all_team_and_invalid_channel(self):
        from server import string
        for team in (0,1):
            room=self.s.chat_room(team);self.s.chat(b'\x07\x07'+room+string('hello'))
            self.assertEqual(self.sent[-1][:13],b'\x07\x08'+room)
        count=len(self.sent);self.s.chat(b'\x07\x07'+self.s.chat_room(2)+string('enemy'));self.assertEqual(count,len(self.sent))

    def test_build_replaces_grass_but_rejects_existing_solid(self):
        self.s.weapons[self.key]['tools'].append({'type':'build'})
        self.s.packets['brick-key']=pack('I',55)
        self.s.block_cards[6]={'replaceable':True};self.s.passable.add(6)
        self.s.blocks[self.s.cell_index((2,0,2))]=1
        i=self.s.cell_index((2,1,2));self.s.blocks[i]=6
        request=b'\x06\x38\x01\x00\xf8\x01'+pack('I',55)+pack('ffffff?',2.5,.999,2.5,2.5,1.001,2.5,True)
        self.s.handle(request);self.assertIn(request[:4]+b'\x00\x01',self.sent)
        self.now=.3;self.s.tick();self.assertEqual(self.s.blocks[i],7)
        self.s.handle(request);self.assertEqual(self.sent[-1],request[:4]+b'\x00\x00')

    def test_half_block_hit_ignores_its_own_voxel_in_visibility(self):
        self.s.block_cards[1]={'block_id':1,'destructible':True,'health':{'max_health':20}}
        i=self.s.cell_index((3,1,2));self.s.blocks[i:i+4]=bytes([1,0,42,0])
        self.s.damage_block((3.6,1.3,2.5),{'hit_effect':{'type':'damage','damage':{'world_damage':25}}},(1,1.5,2.5))
        self.assertEqual(self.s.blocks[i],0)

    def test_cut_detaches_structure_but_keeps_grounded_neighbor(self):
        self.s.block_cards[1]={'block_id':1,'destructible':True,'passable':False}
        for c in [(2,0,2),(2,1,2),(2,2,2),(3,2,2),(1,0,2)]:self.s.blocks[self.s.cell_index(c)]=1
        self.s.set_block((2,1,2),0,vdata=1);self.s.tick_collapse()
        self.assertEqual(self.s.blocks[self.s.cell_index((3,2,2))],0)
        self.assertEqual(self.s.blocks[self.s.cell_index((2,0,2))],1)
        self.assertTrue(any(p[-2:]==b'\x02\x00' for p in self.sent))

    def test_falling_pad_stops_on_player_passable_platform(self):
        self.s.block_cards[1]={'passable':True,'passable_block_falling':False};self.s.passable.add(1)
        self.s.blocks[self.s.cell_index((3,0,2))]=1
        d={'definition':{'movement':{'gravity':10},'beta_falling_bottom_offset':.5},'position':(3.5,2,2.5)}
        for _ in range(20):self.s.fall_unit(100,d,.1)
        self.assertGreaterEqual(d['position'][1],1.5);self.assertLess(d['position'][1],1.7)

if __name__=='__main__':unittest.main()
