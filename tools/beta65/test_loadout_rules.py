import unittest
from unittest.mock import patch
from lobby import Lobby
from loadout import key, pack

class LoadoutRuleTests(unittest.TestCase):
    def setUp(self):
        ids=['crate','brick','glue','heal','ammo','turret','snowman','mine','mine_skin','camo']
        devices=[{'_id':d,'beta_base_device':{'snowman':'turret','mine_skin':'mine','camo':'crate'}.get(d,d)} for d in ids]
        heroes=[{'id':'tony','skins':['tony_skin'],'defaults':ids[:6],'available_devices':ids[:7]+['camo'],'special_devices':['turret','snowman']},
                {'id':'sarge','skins':['sarge_skin'],'defaults':['crate','brick','glue','heal','ammo','mine'],'available_devices':['crate','brick','glue','heal','ammo','mine','mine_skin'],'special_devices':['mine','mine_skin']}]
        packets={'lobby':{'hero':'tony','skin':'tony_skin','heroes':heroes,'devices':devices},'practice':{'loadout':devices[:6]},'terrain-scene':b'zone'}
        self.sent=[];self.room=Lobby(packets,self.sent.append,lambda *a:None,lambda *a,**kw:None);self.room.open_lobby()
    def send(self,fn,data=b''):self.room.handle_instance(bytes([9,fn])+data,self.sent.append)
    def test_advertise_and_reject_cross_class(self):
        self.assertNotIn(key('mine'),self.room.player_state());old=self.room.devices.copy()
        self.send(3,key('mine')+pack('i',2));self.assertEqual(old,self.room.devices)
    def test_special_variant_replaces_signature_only(self):
        self.send(3,key('snowman')+pack('i',6));self.assertEqual(self.room.devices[6],key('snowman'));self.assertTrue(self.room.valid_loadout())
        old=self.room.devices.copy()
        for fn,data in [(3,key('snowman')+pack('i',1)),(3,key('brick')+pack('i',6)),(4,pack('i',6)),(5,pack('ii',1,6))]:self.send(fn,data);self.assertEqual(self.room.devices,old)
    def test_cosmetic_family_cannot_duplicate(self):
        self.send(3,key('camo')+pack('i',2));self.assertNotIn(1,self.room.devices);self.assertEqual(self.room.devices[2],key('camo'));self.assertFalse(self.room.valid_loadout())
        self.send(6);self.assertTrue(self.room.valid_loadout())
    def test_switch_hero_updates_permissions_and_resets(self):
        self.send(2,key('sarge'));self.assertIn(key('mine_skin'),self.room.allowed_devices());self.assertNotIn(key('snowman'),self.room.allowed_devices());self.assertEqual(self.room.devices[6],key('mine'))
    def test_corrupted_full_loadout_cannot_ready(self):
        self.room.devices[1]=key('mine');self.send(10);self.assertEqual(self.room.state,'lobby')
    def test_timer_repairs_corrupted_loadout(self):
        self.room.devices[1]=key('mine');self.room.selection_end=1;self.room.tick(self.sent.append);self.assertEqual(self.room.devices,self.room.defaults);self.assertEqual(self.room.state,'lobby')
        with patch('lobby.millis',return_value=self.room.start_countdown):self.room.tick(self.sent.append)
        self.assertEqual(self.room.state,'zone')

if __name__=='__main__':unittest.main()
