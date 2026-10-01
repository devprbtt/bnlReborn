import unittest
import test_practice as fixtures
import test_shared_world as shared_fixtures
from practice import pack


def pad(block_id, aligned=True):
    return {'block_id':block_id,'solid':False,'grounded':not aligned,
            'can_stay_in_air':False,'visual':{'face_align':aligned}}


class BlockSupportTests(unittest.TestCase):
    def setUp(self):fixtures.PracticeTests.setUp(self)

    def test_bounce_and_speed_including_cosmetics_break_with_crate(self):
        for block_id,aligned in ((16,True),(48,True),(18,False),(49,False)):
            with self.subTest(block_id=block_id):
                self.s.block_cards[block_id]=pad(block_id,aligned)
                self.s.set_block((2,1,2),1)
                self.s.set_block((2,2,2),block_id,vdata=1 if aligned else 0)
                self.s.block_damage[(2,2,2)]=10;self.s.block_teams[(2,2,2)]=1
                self.s.set_block((2,1,2),0,vdata=1)
                self.assertEqual(self.s.blocks[self.s.cell_index((2,2,2))],0)
                self.assertNotIn((2,2,2),self.s.block_damage)
                self.assertNotIn((2,2,2),self.s.block_teams)

    def test_all_mount_faces_and_unrelated_neighbor_removal(self):
        self.s.block_cards[16]=pad(16)
        for face,n in enumerate(((0,-1,0),(0,1,0),(-1,0,0),(1,0,0),(0,0,-1),(0,0,1))):
            with self.subTest(face=face):
                support=(3,1,3);c=tuple(a+b for a,b in zip(support,n))
                self.s.set_block(support,1);self.s.set_block(c,16,vdata=face)
                other=(c[0]+1,c[1],c[2]+1)
                self.s.set_block(other,1);self.s.set_block(other,0,vdata=1)
                self.assertEqual(self.s.blocks[self.s.cell_index(c)],16)
                self.s.set_block(support,0,vdata=1)
                self.assertEqual(self.s.blocks[self.s.cell_index(c)],0)

    def test_partial_damage_and_replacement_preserve_pad(self):
        self.s.block_cards[16]=pad(16)
        self.s.set_block((2,1,2),1);self.s.set_block((2,2,2),16,vdata=1)
        self.s.set_block((2,1,2),1,damage=200);self.s.set_block((2,1,2),7)
        self.assertEqual(self.s.blocks[self.s.cell_index((2,2,2))],16)

    def test_collapsed_support_removes_pad_chain(self):
        self.s.block_cards[16]=pad(16);self.s.block_cards[18]=pad(18,False)
        self.s.set_block((2,0,2),1);self.s.set_block((2,1,2),1)
        self.s.set_block((2,2,2),16,vdata=1);self.s.set_block((2,3,2),18)
        self.s.collapse_removals.append((2,1,2));self.s.tick_collapse()
        for y in (1,2,3):self.assertEqual(self.s.blocks[self.s.cell_index((2,y,2))],0)


class SharedBlockSupportTests(unittest.TestCase):
    def setUp(self):shared_fixtures.SharedWorldTests.setUp(self)
    def tearDown(self):self.store.db.close()

    def test_pad_destruction_broadcasts_to_every_player(self):
        self.a.block_cards[18]=pad(18,False)
        self.a.set_block((2,1,2),1);self.a.set_block((2,2,2),18)
        self.a.set_block((2,1,2),0,vdata=1)
        removal=b'\x06\x06\x01'+pack('hhh',2,2,2)+bytes([0xf0,0,0,1,0])
        for packets in self.sent.values():self.assertIn(removal,packets)
        self.assertEqual(self.enemy.blocks[self.enemy.cell_index((2,2,2))],0)


if __name__=='__main__':unittest.main()
