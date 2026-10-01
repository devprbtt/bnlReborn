import unittest
import test_practice as fixtures
from loadout import key
from practice import pack

class LoadoutTests(unittest.TestCase):
    def setUp(self):
        fixtures.PracticeTests.setUp(self)
        self.s.ability={'_id':'ability_sarge_frag_grenade','charges':{'max_charges':3,'charge_cooldown':15},'application':{'type':'projectile','speed':15},'hit_effect':{'type':'splash_damage','radius':3,'damage':{'player_damage':30,'world_damage':30}}}
        self.s.charges=3
    def ability(self,shot):
        return self.s.handle(b'\x06\x2f\x01\x00\xe0'+key('ability_sarge_frag_grenade')+pack('fff',1,1,2)+b'\x01'+pack('fff',5,1,2)+b'\x01'+pack('Q',shot))
    def test_f_consumes_charges_and_recharges(self):
        for i in range(3):self.now=i;self.ability(i+1)
        self.assertEqual(self.s.charges,0)
        self.now=3;self.ability(4);self.assertIn(b'\x06\x2f\x01\x00\x00\x00',self.sent[-2:])
        self.now=15;self.s.tick();self.assertEqual(self.s.charges,1)
    def test_wrong_ability_and_duplicate_shot_rejected(self):
        self.ability(1);self.now=1;self.ability(1);self.assertEqual(self.s.charges,2)
    def test_ability_state_matches_original_fixture(self):
        self.s.ability_update()
        self.assertEqual(self.sent[-1].hex(),'06090100000000e000269a0a84030000000000000000000000')
    def test_grenade_hit_applies_splash_once(self):
        self.ability(1);self.now=3;fixtures.PracticeTests.hit(self,1)
        self.assertEqual(self.s.target_health,130)
        fixtures.PracticeTests.hit(self,1);self.assertEqual(self.s.target_health,130)
    def test_rocket_bunch_damages_unit_and_voxel(self):
        effect={'type':'bunch','instant':[{'type':'splash_damage','radius':3,'damage':{'player_damage':40,'world_damage':30}}]}
        self.s.weapons[self.key]['tools'][0]['hit_effect']=effect
        self.s.block_cards[1]={'block_id':1,'destructible':True,'health':{'max_health':20}}
        self.s.blocks[self.s.cell_index((5,1,3))]=1
        fixtures.PracticeTests.shoot(self);fixtures.PracticeTests.hit(self)
        self.assertEqual(self.s.target_health,120)
        self.assertEqual(self.s.blocks[self.s.cell_index((5,1,3))],0)
        self.assertEqual(self.s.ammo[self.key][0],1)
    def test_loadout_cost_scales_with_live_devices(self):
        d={'_id':'mine','base_cost':180,'cost_inc_per_unit':20}
        self.assertEqual(self.s.build_cost(d),180)
        self.s.placed[100]={'device':'mine'}
        self.assertEqual(self.s.build_cost(d),200)
    def test_bomb_fuse_and_mine_enemy_trigger(self):
        effect={'type':'splash_damage','radius':2,'damage':{'player_damage':10,'world_damage':0}}
        bomb={'device':'bomb','definition':{'_id':'unit_bomb','data':{'type':'bomb','timeout':8,'trigger_effect':effect}},'position':(5,1,2),'created':0,'health':10,'cell':(5,1,2)}
        self.s.placed[100]=bomb;self.now=7;self.s.tick();self.assertIn(100,self.s.placed)
        self.now=8;self.s.tick();self.assertNotIn(100,self.s.placed);self.assertEqual(self.s.target_health,150)
        mine=dict(bomb,device='mine',definition={'_id':'unit_mine','data':{'type':'landmine','trigger_radius':2,'trigger_effect':effect}})
        self.s.placed[101]=mine;self.s.tick();self.assertNotIn(101,self.s.placed);self.assertEqual(self.s.target_health,140)

    def test_radar_and_spawn_point_follow_live_devices(self):
        radar={'device':'device_generic_radar','definition':{'data':{'type':'common'}},'position':(5,1,2),'created':0,'health':10,'cell':(5,1,2)}
        self.s.placed[100]=radar
        self.s.placed[101]=dict(radar,device='spawn',definition={'spawn_point':{},'data':{'type':'common'}})
        self.s.tick();self.assertTrue(self.s.radar_marked);self.assertEqual(self.s.spawn_position,self.s.base_spawn)
        self.s.selected_spawn=101;self.s.update_spawns();self.assertEqual(self.s.spawn_position,(5,2,2))
        self.s.placed.clear();self.s.tick();self.assertFalse(self.s.radar_marked);self.assertEqual(self.s.spawn_position,(14.5,5,23.5))

    def test_radar_does_not_update_a_dropped_target(self):
        self.s.radar_marked=True;self.s.target_health=0
        before=len(self.sent);self.s.tick_systems()
        self.assertFalse(self.s.radar_marked)
        self.assertFalse(any(p[:6]==b'\x06\x09'+pack('I',2) for p in self.sent[before:]))

    def test_ability_recharge_defers_packet_while_player_dead(self):
        self.s.charges=2;self.s.charge_at=1;self.s.player_respawn_at=5
        self.now=1;before=len(self.sent);self.s.tick_systems()
        self.assertEqual(self.s.charges,3);self.assertEqual(len(self.sent),before)

    def test_selected_block_cost_and_id_are_used(self):
        definition={'_id':'crate','device_key':'block_crate','base_cost':2}
        self.s.device_definitions['block_crate']={'category':'block','block_id':12}
        self.s.complete_build((2,1,2),definition)
        self.assertEqual(self.s.blocks[self.s.cell_index((2,1,2))],12)
        self.assertEqual(self.s.resources,498)

    def test_grenade_rejects_remote_origin(self):
        self.s.position=(7,3,7);self.ability(1)
        self.assertEqual(self.s.charges,3)

if __name__=='__main__':unittest.main()
