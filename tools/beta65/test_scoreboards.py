import struct
import unittest
import test_shared_world as fixtures


class ScoreboardTests(unittest.TestCase):
    def setUp(self):fixtures.SharedWorldTests.setUp(self);fixtures.SharedWorldTests.assault(self)
    def tearDown(self):self.store.db.close()

    def test_tab_assists_damage_and_final_rows_agree(self):
        helper=self.w.players[2]
        helper.damage_entity(3,40,helper.current)
        self.a.damage_entity(3,120,self.a.current)
        self.assertEqual((self.a.kills,helper.assists,self.enemy.deaths),(1,1,1))
        packet=[p for p in self.sent[1] if p[:4]==b'\x06\x07\x40\xe0'][-1]
        self.assertEqual(struct.unpack_from('<iiii',packet,len(packet)-33),(160,0,0,0))
        self.w.finish(1)
        result=[p for p in self.sent[1] if p[:2]==b'\x06\x04'][-1]
        self.assertEqual(struct.unpack_from('<iiiiiiii',result,16),(0,0,0,0,0,1,0,0))
        self.assertEqual(struct.unpack_from('<iiiiiiii',result,63)[-1],1)
        count=len(self.sent[1]);self.w.finish(1);self.assertEqual(len(self.sent[1]),count)

    def test_old_damage_no_assist_and_healing_counts_only_restored_hp(self):
        self.w.players[2].damage_entity(3,40,self.a.current)
        self.clock[0]+=11
        self.a.damage_entity(3,120,self.a.current)
        self.assertEqual(self.w.players[2].assists,0)
        friend=self.w.players[2];friend.player_health=150
        self.a.extra_effect({'type':'heal','player_heal':50},friend.position,self.a.position,self.a.current,2,1)
        self.assertEqual(self.a.score['healing'],10)
        self.a.extra_effect({'type':'heal','player_heal':50},friend.position,self.a.position,self.a.current,2,1)
        self.assertEqual(self.a.score['healing'],10)

    def test_device_objective_damage_credited_to_owner(self):
        self.a.placed[100]={'definition':{'labels':[]},'owner':2,'team':1}
        self.a.placed[101]={'definition':{'labels':['objective'],'health':{'health':{'max_health':100}}},'health':100,'team':2}
        self.a.damage_entity(101,40,self.a.current,100)
        self.assertEqual(self.w.players[2].objective_damage,40)
        self.assertEqual(self.a.objective_damage,0)


if __name__=='__main__':unittest.main()
