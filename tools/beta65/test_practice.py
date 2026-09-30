import unittest
import zlib
from practice import Practice, pack, health


class PracticeTests(unittest.TestCase):
    def setUp(self):
        self.now=0; self.sent=[]; self.events=[]
        weapon={'ammo':[{'mag_size':2,'pool':{'pool_size':4}}], 'reload':{'reload_time':2},
                'tools':[{'type':'shot','range':20,'timing':{'attack_time':0.1},
                          'hit_effect':{'type':'damage','damage':{'player_damage':80}}}]}
        self.key=pack('I',123)
        self.packets={'practice':{'target_position':[5,1,2],'passable':[0],
                       'weapons':[{'id':'gear_sarge_stone_m60','data':weapon}]},
                      'keys':{'gear_sarge_stone_m60':self.key},
                      'terrain':zlib.compress(pack('HHH',8,4,8)+bytes(8*4*8*4)),
                      'target-create':b'create','target-state':b'state'}
        self.s=Practice(self.packets,self.sent.append,lambda kind,**kw:self.events.append((kind,kw)),lambda:self.now)
        self.s.position=(1,1,2)

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
        self.now=3.1;self.s.tick()
        self.assertIsNone(self.s.player_respawn_at)
        self.assertEqual(self.s.position,(14.5,5,23.5))
        self.assertIn(b'hero',self.sent)

    def test_partial_damage_preserves_voxel_shape(self):
        index=self.s.cell_index((2,1,2));self.s.blocks[index:index+4]=bytes([1,0,42,9])
        self.s.set_block((2,1,2),1,30)
        self.assertEqual(self.s.blocks[index:index+4],bytes([1,30,42,9]))

    def test_beta_health_fixture(self):
        self.assertEqual(health(2,160).hex(),'06090200000040000000002043')

if __name__=='__main__': unittest.main()
