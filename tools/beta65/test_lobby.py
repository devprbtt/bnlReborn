import unittest
from lobby import Lobby
from loadout import key, pack
from server import string


class LobbyTests(unittest.TestCase):
    def setUp(self):
        devices = [{'_id': 'device_' + str(i)} for i in range(7)]
        self.packets = {'lobby': {'hero': 'sarge', 'skin': 'sarge_skin', 'devices': devices},
                        'practice': {'loadout': devices[:6]}, 'scene': b'menu', 'terrain-scene': b'zone'}
        self.region, self.instance, self.entries = [], [], []
        self.room = Lobby(self.packets, self.region.append, lambda r,s: self.entries.append(s), lambda *a,**k: None)

    def send(self, function, body=b''):
        return self.room.handle_instance(bytes([9,function])+body, self.instance.append)

    def test_create_leave_and_names_not_logged(self):
        events=[]; self.room.event=lambda k,**v:events.append((k,v))
        self.room.handle_region(b'\x0b\x09'+string('My room')+string('secret'))
        self.assertEqual(self.room.state,'room')
        self.assertEqual(self.room.password,'secret')
        self.assertNotIn('secret',repr(events))
        self.room.handle_region(b'\x0b\x0b')
        self.assertEqual(self.room.password,'')
        self.assertEqual(self.region[-1],b'\x0b\x11')

    def test_custom_and_friendly_start_lobby(self):
        self.room.handle_region(b'\x0b\x00'+key('beta_menu_ranked'))
        self.assertEqual(self.entries,[])
        self.room.handle_region(b'\x0b\x00'+key('beta_menu_friendly'))
        self.assertEqual(self.entries,['lobby'])
        self.room.handle_region(b'\x0b\x00'+key('beta_menu_friendly'))
        self.assertEqual(self.entries,['lobby'])

    def test_ready_requires_six_unique_devices_and_keeps_instance(self):
        self.room.open_lobby()
        self.send(4,pack('i',2)); self.send(10)
        self.assertEqual(self.room.state,'lobby')
        self.send(3,key('device_6')+pack('i',2)); self.send(10)
        self.assertEqual(self.room.state,'zone')
        self.assertEqual(self.region[-1],b'zone')
        self.assertEqual(self.entries,['lobby'])
        self.send(10); self.assertEqual(self.region.count(b'zone'),1)

    def test_invalid_and_duplicate_devices(self):
        self.room.open_lobby(); before=self.room.devices.copy()
        self.send(3,key('unknown')+pack('i',1)); self.send(3,key('device_6')+pack('i',7))
        self.assertEqual(before,self.room.devices)
        self.send(3,key('device_0')+pack('i',2))
        self.assertNotIn(1,self.room.devices)
        self.assertEqual(len(set(self.room.devices.values())),5)

    def test_swap_and_recommendations_carry_into_game_without_mutating_defaults(self):
        self.room.open_lobby(); self.send(5,pack('ii',1,5))
        self.send(3,key('device_6')+pack('i',2))
        selected=self.room.practice_packets()['practice']['loadout']
        self.assertEqual([d['_id'] for d in selected],['device_4','device_6','device_2','device_3','device_0','device_5'])
        self.assertEqual(self.packets['practice']['loadout'][0]['_id'],'device_0')
        self.send(6); self.assertEqual(self.room.devices,self.room.defaults)

    def test_cannot_edit_during_match_and_can_return_then_reenter(self):
        self.room.open_lobby(); self.send(10)
        before=self.room.devices.copy(); self.send(4,pack('i',1))
        self.assertEqual(self.room.devices,before)
        self.room.zone_initialized=True
        self.room.handle_instance(b'\x06\x05',self.instance.append)
        self.assertEqual(self.room.state,'menu'); self.assertEqual(self.region[-1],b'menu')
        self.room.open_lobby(); self.assertFalse(self.room.zone_initialized)

    def test_fixed_room_settings_are_not_falsely_accepted(self):
        self.room.state='room'
        self.room.handle_region(b'\x0b\x0c\x10\x01')
        self.assertEqual(self.region[0],self.room.room_update())
        self.assertEqual(self.region[1][:2],b'\x02\x03')


if __name__ == '__main__': unittest.main()
