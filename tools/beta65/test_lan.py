import tempfile
import threading
import unittest
from pathlib import Path
from profiles import Profiles,Social
from matchmaking import Matchmaking
from lobby import Lobby
from loadout import key
from server import string
import struct


class LanTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.store=Profiles(Path(self.temp.name)/'profiles.sqlite');self.social=Social(self.store);self.mm=Matchmaking(self.social)
        self.devices=[{'_id':'device_'+str(i)} for i in range(6)]
        self.packets={'lobby':{'hero':'sarge','skin':'skin','devices':self.devices},'practice':{'loadout':self.devices},'lobby-scene':bytes.fromhex('020002c001f1d5a9a2'),'maps':[{'id':'two-team','build_seconds':120}]}
        self.a=self.room('Alice');self.b=self.room('Bobby')
    def tearDown(self):self.store.db.close();self.temp.cleanup()
    def room(self,name):
        profile=self.store.login(name,'test-password');room=Lobby(self.packets,lambda p:None,lambda *a:None,lambda *a,**k:None)
        room.sent=[];room.send_region=room.sent.append;room.player_id=profile['id'];room.nickname=profile['name'];self.social.connect(room);return room
    def test_accounts_persist_passwords_are_verified_and_case_insensitive(self):
        self.assertIsNone(self.store.login('Alice','wrong-password'))
        self.assertEqual(self.store.login('alice','test-password')['id'],self.a.player_id)
        self.assertIsNone(self.store.login('bad!','test-password'))
        other=Profiles(Path(self.temp.name)/'profiles.sqlite')
        self.assertEqual(other.login('Alice','test-password')['id'],self.a.player_id);other.db.close()
        self.assertNotIn(b'test-password',(Path(self.temp.name)/'profiles.sqlite').read_bytes())
    def test_profile_not_found_and_native_search(self):
        self.social.handle(self.a,b'\x05\x17\x00\x00'+string('Bob'))
        self.assertIn(string('Bobby'),self.a.sent[-1])
        self.social.handle(self.a,b'\x05\x1f\x00\x00'+struct.pack('<I',999))
        self.assertEqual(self.a.sent[-1][4],255)
    def test_friend_accept_requires_pending_request(self):
        a,b=self.a.player_id,self.b.player_id
        self.store.friend(a,b,'accept',True);self.assertFalse(self.store.social(a)[0])
        self.social.handle(self.a,b'\x05\x18'+struct.pack('<I',b))
        self.assertEqual(self.store.social(b)[1][0]['id'],a)
        self.social.handle(self.b,b'\x05\x19'+struct.pack('<I?',a,True))
        self.assertEqual(self.store.social(a)[0][0]['id'],b)
        self.social.handle(self.a,b'\x05\x1a'+struct.pack('<I',b));self.assertFalse(self.store.social(b)[0])
    def test_invite_accept_and_disconnect_cleanup(self):
        a,b=self.a.player_id,self.b.player_id
        self.social.handle(self.a,b'\x05\x0f'+struct.pack('<I',b))
        self.assertEqual(self.b.sent[-1][:2],b'\x05\x10')
        self.social.handle(self.b,b'\x05\x0a\x00\x00\x01\x80'+struct.pack('<Q',a))
        self.assertEqual(self.social.squads[a],[a,b])
        self.social.disconnect(self.a);self.assertEqual(self.social.squads[b],[b])
    def test_uninvited_join_cannot_enter_squad(self):
        self.social.handle(self.a,b'\x05\x09\x00\x00'+key('beta_menu_friendly'))
        self.social.handle(self.b,b'\x05\x0a\x00\x00\x01\x80'+struct.pack('<Q',self.a.player_id))
        self.assertEqual(self.b.sent[-1][4],255)
    def test_queue_confirmation_requires_both_and_cleans_up(self):
        for room in (self.a,self.b):self.mm.handle(room,b'\x0b\x00'+key('beta_menu_friendly'))
        self.assertEqual(self.a.state,'confirming');self.assertEqual(self.b.state,'confirming')
        self.mm.handle(self.a,b'\x0b\x03\x01');self.assertEqual(self.a.state,'confirming')
        self.mm.handle(self.b,b'\x0b\x03\x01');self.assertEqual(self.a.state,'lobby');self.assertIs(self.a.group,self.b.group)
        self.assertEqual({self.a.team,self.b.team},{1,2});self.assertFalse(self.mm.pending)
        # Never present separate solo worlds as a multiplayer match.
        self.a.handle_instance(b'\x09\x0a',lambda p:None);self.assertEqual(self.a.state,'lobby')
    def test_cancel_and_timeout_remove_all_pending_members(self):
        for room in (self.a,self.b):self.mm.handle(room,b'\x0b\x00'+key('beta_menu_friendly'))
        self.mm.pending[self.a.player_id]['deadline']=1;self.mm.tick()
        self.assertFalse(self.mm.pending);self.assertEqual(self.b.state,'menu')
    def test_custom_join_password_host_map_and_team_switch(self):
        self.mm.handle(self.a,b'\x0b\x09'+string('Test room')+string('secret'))
        self.mm.handle(self.b,b'\x0b\x07\x00\x00'+struct.pack('<Q',1)+string('wrong'))
        self.assertEqual(self.b.sent[-1][-1],2);self.assertEqual(self.b.state,'menu')
        self.mm.handle(self.b,b'\x0b\x07\x00\x00'+struct.pack('<Q',1)+string('secret'))
        self.assertIs(self.a.group,self.b.group)
        self.mm.handle(self.b,b'\x0b\x0c\x80'+key('two-team'));self.assertEqual(self.a.map_id,'beta_practice_map')
        self.mm.handle(self.a,b'\x0b\x0c\x80'+key('two-team'));self.assertEqual(self.b.map_id,'two-team')
        old=self.b.team;self.mm.handle(self.b,b'\x0b\x0d');self.assertEqual(self.b.team,3-old)
        self.mm.leave(self.a);self.assertIs(self.b.group['owner'],self.b)
    def test_profile_packet_identity_is_not_another_players_profile(self):
        packet=self.social.profile_packet(self.b.player_id)
        self.assertIn(string('Bobby'),packet);self.assertNotIn(string('Alice'),packet)
    def test_queue_repeated_request_does_not_duplicate_and_disconnect_cancels(self):
        for _ in range(2):self.mm.handle(self.a,b'\x0b\x00'+key('beta_menu_friendly'))
        self.assertEqual(len(self.mm.queue),1)
        self.mm.leave(self.a);self.assertFalse(self.mm.queue);self.assertEqual(self.a.state,'menu')
    def test_team_two_scene_and_fixture_identity(self):
        from wire_units import set_identity
        packet=bytes.fromhex('060801000000f6a66ec0d8ff80000068410000a0400000bc41000084030000000000000000000000000000010100000001c3c0517f')
        changed=set_identity(packet,2,17)
        self.assertEqual(struct.unpack_from('<I',changed,44)[0],17);self.assertEqual(changed[-5],2)
        self.assertEqual(packet[-5],1)
        self.a.team=2;self.assertEqual(self.a.lobby_scene()[4],2)
    def test_rewards_persist_once_and_never_for_custom_or_aborted(self):
        rows=[{'id':self.a.player_id,'team':1},{'id':self.b.player_id,'team':2}]
        for mode,done in [('custom',True),('practice',True),('friendly',False)]:self.assertEqual(self.store.award('test',rows,1,mode,done),[])
        self.assertEqual(len(self.store.award('test',rows,1,'friendly',True)),2)
        self.assertEqual(self.store.award('test',rows,1,'friendly',True),[])
        self.assertEqual(self.store.get(self.a.player_id)['xp'],750)
        self.store.award('second',rows,1,'friendly',True);self.assertEqual(self.store.get(self.a.player_id)['level'],2)

if __name__=='__main__':unittest.main()
