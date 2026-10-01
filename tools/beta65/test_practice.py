import unittest
import zlib
from practice import Practice, pack, health


class PracticeTests(unittest.TestCase):
    def setUp(self):
        self.now=0; self.sent=[]; self.events=[]
        weapon={'ammo':[{'mag_size':2,'pool':{'pool_size':4}}], 'reload':{'reload_time':2},
                'tools':[{'type':'shot','ammo':{'rate':1},'range':20,'timing':{'attack_time':0.1},
                          'hit_effect':{'type':'damage','damage':{'player_damage':80}}}]}
        self.key=pack('I',123)
        self.packets={'practice':{'target_position':[5,1,2],'passable':[0],
                       'weapons':[{'id':'gear_sarge_stone_m60','data':weapon}]},
                      'keys':{'gear_sarge_stone_m60':self.key},
                      'terrain':zlib.compress(pack('HHH',8,4,8)+bytes(8*4*8*4)),
                      'target-create':b'create','target-state':b'state'}
        self.s=Practice(self.packets,self.sent.append,lambda kind,**kw:self.events.append((kind,kw)),lambda:self.now)
        self.s.position=(1,1,2)

    def test_respawn_refills_all_weapons_and_cancels_pending_reload(self):
        reserve=pack('I',124);melee=pack('I',125)
        self.s.weapons[reserve]={'ammo':[{'pool':{'pool_size':12}}]}
        self.s.weapons[melee]={'ammo':[]}
        self.s.ammo={self.key:[0,1],reserve:[None,0]}
        self.s.packets.update({'hero-create':b'hero','hero-state':b'hero-state'})
        self.s.player_respawn_at=10;self.s.reload_at=10;self.s.player_health=0
        self.now=9.9;self.s.tick();self.assertEqual(self.s.ammo[reserve],[None,0])
        self.now=10;self.s.tick()
        self.assertEqual(self.s.ammo,{self.key:[2,4],reserve:[None,12]})
        self.assertIsNone(self.s.reload_at);self.assertNotIn(melee,self.s.ammo)
        self.assertIn(self.s.ammo_packet(),self.sent)
        self.s.ammo[self.key][0]=1;self.now=11;self.s.tick()
        self.assertEqual(self.s.ammo[self.key],[1,4])

    def shoot(self, shot=1):
        self.s.handle(b'\x06\x1e\xe0\x00'+pack('fff',1,1,2)+b'\x01'+pack('fff',5,1,2)+b'\x01'+pack('Q',shot))

    def hit(self, shot=1):
        self.s.handle(b'\x06\x1f\x01'+pack('Q',shot)+b'\xe8'+pack('fffBhhhI',5,1,2,0,0,0,0,2))

    def test_hit_requires_shot_and_cannot_replay(self):
        self.hit(); self.assertEqual(self.s.target_health,160)
        self.shoot(); self.hit(); self.hit()
        self.assertEqual(self.s.target_health,80)
        self.assertEqual(self.s.ammo[self.key],[1,4])

    def test_kill_and_timed_respawn(self):
        self.shoot(); self.hit(); self.now=.2; self.shoot(2); self.hit(2)
        self.assertEqual(self.s.target_health,0); self.assertEqual(self.s.kills,1)
        self.now=3; self.s.tick(); self.assertEqual(self.s.target_health,0)
        self.now=3.3; self.s.tick(); self.assertEqual(self.s.target_health,160)
        self.assertEqual(self.sent[-2:],[b'create',b'state'])

    def test_reload_is_delayed_and_conserves_ammo(self):
        self.shoot(); self.s.handle(b'\x06\x21\x01\x00')
        self.now=1; self.s.tick(); self.assertEqual(self.s.ammo[self.key],[1,4])
        self.shoot(2); self.assertEqual(self.s.ammo[self.key],[1,4])
        self.now=2; self.s.tick(); self.assertEqual(self.s.ammo[self.key],[2,3])

    def test_rate_limit_and_empty_magazine(self):
        self.shoot(); self.shoot(2); self.assertEqual(len(self.s.shots),1)
        self.now=.2; self.shoot(3); self.now=.4; self.shoot(4)
        self.assertEqual(self.s.ammo[self.key][0],0); self.assertNotIn(4,self.s.shots)

    def test_world_obstruction_rejects_damage(self):
        cells=bytearray(self.s.blocks);cells[((3*4+1)*8+2)*4]=1;self.s.blocks=bytes(cells)
        self.shoot(); self.hit(); self.assertEqual(self.s.target_health,160)

    def test_expired_and_malformed_hits(self):
        self.shoot();self.now=6;self.s.tick();self.hit();self.assertEqual(self.s.target_health,160)
        with self.assertRaises(ValueError): self.s.handle(b'\x06\x1f\x01')

    def test_mining_changes_world_and_awards_resource_once(self):
        self.s.block_cards[1]={'block_id':1,'destructible':True,'health':{'max_health':20,'toughness':0},'reward':{'player_reward':5}}
        cell=(3,1,2); self.s.blocks[self.s.cell_index(cell)]=1
        tool={'hit_effect':{'type':'damage','damage':{'world_damage':25,'mining':True}}}
        self.s.damage_block((3.001,1.5,2.5),tool,(1,1.5,2.5))
        self.assertEqual(self.s.blocks[self.s.cell_index(cell)],0)
        self.assertEqual(self.s.resources,505)
        self.s.damage_block((3.001,1.5,2.5),tool,(1,1.5,2.5))
        self.assertEqual(self.s.resources,505)

    def test_build_timer_cost_and_overlap_rejection(self):
        self.s.weapons[self.key]['tools'].append({'type':'build'})
        self.s.packets['brick-key']=pack('I',55)
        self.s.blocks[self.s.cell_index((2,0,2))]=1
        request=b'\x06\x38\x01\x00\xf8\x01'+pack('I',55)+pack('ffffff?',2.5,.999,2.5,2.5,1.001,2.5,True)
        self.s.handle(request); self.assertIn(request[:4]+b'\x00\x01',self.sent)
        self.assertEqual(self.s.resources,500)
        self.now=.3;self.s.tick()
        self.assertEqual(self.s.resources,495)
        self.assertEqual(self.s.blocks[self.s.cell_index((2,1,2))],7)
        self.s.handle(request);self.assertEqual(self.sent[-1],request[:4]+b'\x00\x00')

    def test_void_death_respawns_player_without_duplicate_death(self):
        self.s.packets.update({'hero-create':b'hero','hero-state':b'hero-state'})
        self.s.move((1,-4,2));self.s.move((1,-5,2))
        self.assertEqual(sum(k=='player_void_death' for k,v in self.events),1)
        self.now=5.1;self.s.tick()
        self.assertIsNone(self.s.player_respawn_at)
        self.assertEqual(self.s.position,(14.5,5,23.5))
        self.assertIn(b'hero',self.sent)

    def test_partial_damage_preserves_voxel_shape(self):
        index=self.s.cell_index((2,1,2));self.s.blocks[index:index+4]=bytes([1,0,42,9])
        self.s.set_block((2,1,2),1,30)
        self.assertEqual(self.s.blocks[index:index+4],bytes([1,30,42,9]))

    def test_beta_health_fixture(self):
        self.assertEqual(health(2,160).hex(),'06090200000040000000002043')

    def test_pool_only_weapon_consumes_pool_and_rejects_reload(self):
        self.s.weapons[self.key]['ammo'][0]['mag_size']=None
        self.s.ammo[self.key]=[None,4]
        self.shoot()
        self.assertEqual(self.s.ammo[self.key],[None,3])
        self.assertIn(b'\x01\xa0'+pack('if',0,3),self.s.ammo_packet())
        self.s.handle(b'\x06\x21\x01\x00')
        self.assertIsNone(self.s.reload_at)

    def test_multi_pellet_cast_consumes_one_round_and_rejects_duplicate_ids(self):
        self.s.weapons[self.key]['tools'][0]['bullets']={'count':2}
        header=b'\x06\x1e\xe0\x00'+pack('fff',1,1,2)+b'\x02'
        def shot(i):return pack('fff',5,1,2)+b'\x01'+pack('Q',i)
        self.s.handle(header+shot(1)+shot(1))
        self.assertEqual(self.s.shots,{})
        self.s.handle(header+shot(1)+shot(2))
        self.assertEqual(set(self.s.shots),{1,2})
        self.assertEqual(self.s.ammo[self.key],[1,4])

    def test_selected_hero_health_and_skin_survive_respawn(self):
        self.packets['practice'].update(max_health=210,current_gear='gear_sarge_stone_m60')
        self.packets.update({'hero-create':b'gold-robot','hero-state':b'robot-state'})
        self.s=Practice(self.packets,self.sent.append,lambda *a,**k:None,lambda:self.now)
        self.assertEqual(self.s.player_health,210)
        self.s.move((1,-5,2));self.now=5.1;self.s.tick()
        self.assertEqual(self.s.player_health,210)
        self.assertIn(b'gold-robot',self.sent)

    def test_map_spawn_and_kill_plane_survive_respawn(self):
        spawn=(2.5,3.2,2.5)
        self.packets['practice'].update(spawn_position=spawn,kill_height=1.5)
        self.packets.update({'hero-create':b'hero'+pack('fff',*spawn),'hero-state':b'state'})
        self.s=Practice(self.packets,self.sent.append,lambda *a,**k:None,lambda:self.now)
        self.assertEqual(self.s.position,spawn)
        self.s.move((2.5,1.4,2.5));self.assertIsNotNone(self.s.player_respawn_at)
        self.now=5.1;self.s.tick()
        self.assertEqual(self.s.position,spawn)
        self.assertIn(b'hero'+pack('fff',*spawn),self.sent)

if __name__=='__main__': unittest.main()
