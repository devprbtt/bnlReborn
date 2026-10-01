import unittest
from unittest.mock import patch
import test_lan
from loadout import key
import map_voting


class MapVotingTests(unittest.TestCase):
    setUp=test_lan.LanTests.setUp
    tearDown=test_lan.LanTests.tearDown
    room=test_lan.LanTests.room

    def start(self):
        for room in (self.a,self.b):
            room.maps={key(m):{'id':m} for m in ('alpha','bravo','charlie')}
            room.packets['scene']=b'home'
            room.instance=[];room.send_instance=room.instance.append
        for room in (self.a,self.b):self.mm.handle(room,b'\x0b\x00'+key('beta_menu_friendly'))
        for room in (self.a,self.b):self.mm.handle(room,b'\x0b\x03\x01')
        return self.a.group['map_vote']

    def test_native_two_choices_shared_deadline_and_early_ready_blocked(self):
        ballot=self.start()
        self.assertEqual(len(set(ballot['candidates'])),2)
        self.assertEqual(self.a.selection_end,self.b.selection_end)
        self.a.handle_instance(b'\x09\x0a',self.a.instance.append)
        self.assertFalse(self.a.ready);self.assertNotIn('world',self.a.group)
        update=self.a.update()
        self.assertEqual(update[7],2) # native LobbyMapData list count
        self.assertIn(key(ballot['candidates'][1]),update)

    def test_one_vote_live_update_replay_and_other_candidate_rejected(self):
        ballot=self.start();first,second=ballot['candidates']
        self.a.handle_instance(b'\x09\x09'+key(first),self.a.instance.append)
        self.assertEqual(ballot['votes'],{self.a.player_id:first})
        self.assertTrue(self.b.instance)
        for packet in (b'\x09\x09'+key(first),b'\x09\x09'+key(second),b'\x09\x09'+key('bad'),b'\x09\x09'):
            self.a.handle_instance(packet,self.a.instance.append)
        self.assertEqual(ballot['votes'],{self.a.player_id:first})
        self.b.handle_instance(b'\x09\x09'+key(first),self.b.instance.append)
        self.assertEqual(ballot['winner'],first)
        self.assertEqual(self.a.map_id,self.b.map_id)
        self.assertGreater(self.a.selection_end,ballot['end'])
        self.assertEqual(self.a.update()[7],1)

    def test_deadline_selects_plurality_and_late_votes_do_not_change_winner(self):
        ballot=self.start();selected=ballot['candidates'][1]
        map_voting.vote(self.a,key(selected))
        with patch('map_voting.millis',return_value=ballot['end']):
            self.mm.tick()
        self.assertEqual(ballot['winner'],selected)
        self.assertFalse(map_voting.vote(self.b,key(ballot['candidates'][0])))
        self.assertEqual(ballot['winner'],selected)

    def test_tie_random_choice_only_from_tied_options(self):
        ballot=self.start();first,second=ballot['candidates']
        map_voting.vote(self.a,key(first))
        with patch('map_voting.secrets.choice',return_value=second) as choice:
            map_voting.vote(self.b,key(second))
        self.assertEqual(set(choice.call_args.args[0]),{first,second})
        self.assertEqual(ballot['winner'],second)

    def test_abstention_timeout_selects_offered_map(self):
        ballot=self.start()
        with patch('map_voting.millis',return_value=ballot['end']):self.mm.tick()
        self.assertIn(ballot['winner'],ballot['candidates'])
        self.assertFalse(self.a.ready)

    def test_departure_removes_vote_and_cancels_empty_team_before_game(self):
        ballot=self.start();map_voting.vote(self.a,key(ballot['candidates'][0]))
        group=self.a.group;self.mm.leave(self.a)
        self.assertNotIn(self.a.player_id,ballot['votes'])
        self.assertNotIn(group['id'],self.mm.groups)
        self.assertIsNone(self.b.group);self.assertEqual(self.b.state,'menu')

    def test_custom_vote_cannot_change_host_selection(self):
        self.mm.create_group([self.a,self.b],'Custom')
        for room in (self.a,self.b):room.open_lobby()
        self.a.handle_instance(b'\x09\x09'+key('two-team'),lambda p:None)
        self.assertEqual(self.a.map_id,'beta_practice_map')
        self.assertNotIn('map_vote',self.a.group)

    def test_loadout_remains_editable_during_ballot(self):
        self.start()
        self.a.handle_instance(b'\x09\x04'+__import__('struct').pack('<i',1),lambda p:None)
        self.assertNotIn(1,self.a.devices)
        self.a.handle_instance(b'\x09\x06',lambda p:None)
        self.assertEqual(len(self.a.devices),6)
        self.assertTrue(map_voting.active(self.a.group))

    def test_common_pool_only_and_single_map_skips_ballot(self):
        self.mm.create_group([self.a,self.b],'Friendly',friendly=True)
        self.a.maps[key('exclusive')]={'id':'exclusive'}
        map_voting.begin(self.a.group)
        self.assertEqual(self.a.group['map_vote']['winner'],'two-team')


if __name__=='__main__':unittest.main()
