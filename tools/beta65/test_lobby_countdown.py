import unittest
from unittest.mock import patch
import test_lan
from loadout import pack

class CountdownTests(unittest.TestCase):
    setUp=test_lan.LanTests.setUp
    tearDown=test_lan.LanTests.tearDown
    room=test_lan.LanTests.room

    def start(self,solo=False,friendly=False):
        self.packets.update({'scene':b'menu','terrain-scene':b'zone'})
        members=[self.a] if solo else [self.a,self.b]
        self.group=self.mm.create_group(members,'countdown',friendly=friendly)
        for room in members:
            room.instance=[];room.send_instance=room.instance.append
            with patch('lobby.millis',return_value=1000):room.open_lobby()
        return members

    def ready(self,room,now):
        with patch('lobby.millis',return_value=now):room.handle_instance(b'\x09\x0a',room.instance.append)

    def test_last_player_starts_shared_five_seconds_then_loads_once(self):
        members=self.start();self.ready(self.a,2000)
        self.assertFalse(self.a.start_countdown);self.assertNotIn('world',self.group)
        self.ready(self.b,3000)
        for room in members:
            self.assertEqual((room.selection_start,room.selection_end,room.start_countdown),(3000,8000,8000))
            self.assertIn(b'\xe0\x03'+pack('QQ',3000,8000),room.instance[-1]);self.assertNotIn(b'zone',room.sent)
        with patch('shared_world.World') as world:
            with patch('lobby.millis',return_value=7999):self.a.tick(self.a.instance.append)
            world.assert_not_called()
            with patch('lobby.millis',return_value=8000):
                self.b.tick(self.b.instance.append);self.a.tick(self.a.instance.append);self.b.tick(self.b.instance.append)
            world.assert_called_once()
        for room in members:self.assertEqual(room.state,'zone');self.assertEqual(room.sent.count(b'zone'),1)

    def test_duplicate_ready_cannot_restart_or_skip_countdown(self):
        self.start();self.ready(self.a,2000);self.ready(self.b,3000)
        self.ready(self.a,4500);self.ready(self.b,6000)
        self.assertEqual(self.a.start_countdown,8000);self.assertEqual(self.b.start_countdown,8000)
        self.assertEqual(self.a.state,'lobby');self.assertNotIn('world',self.group)

    def test_solo_also_waits_five_seconds(self):
        self.start(solo=True);self.ready(self.a,2000)
        with patch('lobby.millis',return_value=6999):self.a.tick(self.a.instance.append)
        self.assertEqual(self.a.state,'lobby')
        with patch('lobby.millis',return_value=7000):self.a.tick(self.a.instance.append)
        self.assertEqual(self.a.state,'zone');self.assertEqual(self.a.sent.count(b'zone'),1)

    def test_selection_expiry_still_gives_full_countdown(self):
        self.start();self.a.devices.pop(2)
        with patch('lobby.millis',return_value=121000):
            self.a.tick(self.a.instance.append);self.b.tick(self.b.instance.append)
        self.assertTrue(self.a.valid_loadout());self.assertEqual(self.a.start_countdown,126000)
        self.assertEqual(self.a.state,'lobby');self.assertEqual(self.b.state,'lobby')

    def test_custom_departure_cancels_and_requires_new_ready(self):
        self.start();self.ready(self.a,2000);self.ready(self.b,3000)
        with patch('lobby.millis',return_value=4000):self.mm.leave(self.b)
        self.assertFalse(self.a.start_countdown);self.assertFalse(self.a.ready)
        self.assertEqual(self.a.selection_end,124000)
        with patch('lobby.millis',return_value=8000):self.a.tick(self.a.instance.append)
        self.assertEqual(self.a.state,'lobby');self.assertNotIn(b'zone',self.a.sent)

    def test_friendly_departure_never_launches_empty_opposing_team(self):
        self.start(friendly=True);self.ready(self.a,2000);self.ready(self.b,3000);self.mm.leave(self.b)
        self.assertEqual(self.a.state,'menu');self.assertIsNone(self.a.group);self.assertFalse(self.a.start_countdown)
        self.assertNotIn(b'zone',self.a.sent)

    def test_invalidated_ready_loadout_cancels_at_deadline(self):
        self.start();self.ready(self.a,2000);self.ready(self.b,3000);self.b.devices.pop(2)
        with patch('lobby.millis',return_value=8000):self.a.tick(self.a.instance.append)
        self.assertFalse(self.a.ready);self.assertFalse(self.b.ready);self.assertNotIn('world',self.group)
