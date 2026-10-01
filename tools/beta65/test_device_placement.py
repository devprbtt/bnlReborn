import struct
import unittest
import test_practice as fixtures
from practice import pack

class DevicePlacementTests(unittest.TestCase):
    def setUp(self):
        fixtures.PracticeTests.setUp(self)
        self.unit={'_id':'unit_mine','category':'unit','health':{'health':{'max_health':50}},'movement':{'type':'falling','gravity':10},'data':{'type':'common'},'beta_falling_bottom_offset':.5}
        self.device={'_id':'mine','device_key':'unit_mine','base_cost':5,'ground_only':False}
        self.s.device_definitions={'unit_mine':self.unit}
        self.s.packets['device-templates']={'mine':b'\x06\x08'+pack('I',100)+b'head'+pack('fffhhh',101.25,102.5,103.75,0,0,0)+b'tail'}
        self.s.block_cards[1]={'solid':True,'destructible':True,'passable':False}

    def test_wall_and_ceiling_rotation_survive_gravity_until_support_removed(self):
        cases=[(2,(1,0,0),(0,0,900)),(3,(-1,0,0),(0,0,-900)),(4,(0,0,1),(-900,0,0)),(5,(0,0,-1),(900,0,0)),(0,(0,1,0),(1800,0,0))]
        for face,delta,rotation in cases:
            with self.subTest(face=face):
                cell=(3,1,3);support=tuple(a+b for a,b in zip(cell,delta))
                self.s.blocks[self.s.cell_index(support)]=1;self.s.build_face=face
                self.s.complete_build(cell,self.device);unit=self.s.next_device-1;entry=self.s.placed[unit]
                create=next(p for p in reversed(self.sent) if p[:2]==b'\x06\x08')
                self.assertEqual(struct.unpack_from('<hhh',create,22),rotation)
                before=entry['position']
                for _ in range(20):self.s.fall_unit(unit,entry,.1)
                self.assertEqual(entry['position'],before)
                self.s.blocks[self.s.cell_index(support)]=0;self.s.fall_unit(unit,entry,.1)
                self.assertNotIn('support',entry);self.assertLess(entry['position'][1],before[1])

    def test_device_bottom_stays_above_grass_covered_ground(self):
        self.s.block_cards[6]={'passable_block_falling':True};self.s.passable.add(6)
        self.s.blocks[self.s.cell_index((3,0,3))]=1
        self.s.blocks[self.s.cell_index((3,1,3))]=6
        for offset in (.45,.5,.4998169541358948):
            entry={'definition':dict(self.unit,beta_falling_bottom_offset=offset),'position':(3.5,2.5,3.5)}
            for _ in range(40):self.s.fall_unit(100,entry,.1)
            self.assertGreaterEqual(entry['position'][1]-offset,1)
            self.assertLess(entry['position'][1]-offset,1.1)

    def test_landed_device_is_motionless_across_small_ticks(self):
        self.s.blocks[self.s.cell_index((3,0,3))]=1
        entry={'definition':self.unit,'position':(3.5,2.5,3.5)}
        for _ in range(30):self.s.fall_unit(100,entry,.1)
        settled=entry['position'];count=len(self.sent)
        for _ in range(200):self.s.fall_unit(100,entry,.016)
        self.assertEqual(entry['position'],settled);self.assertEqual(len(self.sent),count)
        self.assertEqual(entry['ground_support'],(3,0,3))

    def test_settled_device_falls_again_only_when_support_removed(self):
        self.s.blocks[self.s.cell_index((3,1,3))]=1
        entry={'definition':self.unit,'position':(3.5,3.5,3.5)}
        for _ in range(30):self.s.fall_unit(100,entry,.1)
        settled=entry['position'];self.s.blocks[self.s.cell_index((3,1,3))]=0
        self.s.fall_unit(100,entry,.1)
        self.assertNotIn('ground_support',entry);self.assertLess(entry['position'][1],settled[1])

    def test_centered_blockbuster_and_pickup_rest_on_top_of_voxel(self):
        self.s.blocks[self.s.cell_index((3,0,3))]=1
        for speed,gravity in ((60,0),(2,10)):
            definition=dict(self.unit,movement={'type':'falling','start_speed':speed,'gravity':gravity})
            entry={'definition':definition,'position':(3.5,3,3.5)}
            for _ in range(50):self.s.fall_unit(100,entry,.05)
            self.assertAlmostEqual(entry['position'][1],1.55)
            self.assertAlmostEqual(entry['position'][1]-.5,1.05)
            before=entry['position']
            for _ in range(100):self.s.fall_unit(100,entry,.016)
            self.assertEqual(entry['position'],before)

if __name__=='__main__':unittest.main()
