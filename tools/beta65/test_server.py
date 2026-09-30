import socket
import struct
import tempfile
import threading
import time
import unittest
from pathlib import Path
from server import MenuServer, receive, string, varint


class Protocol65LoginTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.server = MenuServer({n: bytes([i, 0]) for i, n in enumerate(
            ("catalogue", "player", "server-update", "scene"), 2)}, Path(self.temp.name) / "events.jsonl")
        self.clients = []

    def tearDown(self):
        for client, worker in self.clients:
            client.close()
            worker.join(2)
        self.temp.cleanup()

    def connect(self, version=65):
        client, peer = socket.socketpair()
        client.settimeout(2)
        worker = threading.Thread(target=self.server.session, args=(peer,), daemon=True)
        worker.start()
        self.clients.append((client, worker))
        self.send(client, b"\x01\x00\x00\x00\xc0" + struct.pack("<ii", version, 0))
        return client, receive(client)

    @staticmethod
    def send(client, packet):
        client.sendall(varint(len(packet)) + packet)

    def test_master_region_and_menu_sequence(self):
        client, version = self.connect()
        self.assertEqual(version[4:7], b"\x00\x01\xc0")
        self.send(client, b"\x01\x01\x01\x00" + string("BetaLocal") + string("local-diagnostic-only"))
        self.assertEqual(receive(client), b"\x01\x01\x01\x00\x00\x01\x00\x00\x00")
        redirect = receive(client)
        token = redirect[-64:].decode()
        region, _ = self.connect()
        self.send(region, b"\x01\x09\x01\x00" + string(token))
        self.assertEqual(receive(region), b"\x01\x09\x01\x00\x00\x00")
        packets = [receive(region) for _ in range(5)]
        self.assertEqual(packets[3], b"\x01\x0b")
        self.assertEqual(packets[4], self.server.packets["scene"])
        self.send(region, b"\x0c\x02\x02\x00" + struct.pack("<iii", 0, 0, 0))
        self.assertEqual(receive(region), b"\x0c\x02\x02\x00\x00\x00\x00")
        replay, _ = self.connect()
        self.send(replay, b"\x01\x09\x01\x00" + string(token))
        self.assertEqual(receive(replay)[4], 255)

    def test_modern_version_rejected(self):
        _, version = self.connect(310)
        self.assertEqual(version[5], 0)

    def test_credentials_not_logged(self):
        client, _ = self.connect()
        self.send(client, b"\x01\x01\x01\x00" + string("wrong") + string("private-password"))
        self.assertEqual(receive(client)[4], 255)
        self.assertNotIn("private-password", self.server.event_path.read_text())

    def test_terrain_instance_token_is_one_use(self):
        self.server.terrain_test = True
        self.server.packets["zone-init"] = b"\x06\x00terrain-fixture"
        self.server.packets["zone-start"] = b"\x06\x07phase-fixture"
        self.server.packets["hero-create"] = b"\x06\x08hero-fixture"
        self.server.packets["hero-state"] = b"\x06\x09state-fixture"
        self.server.packets["equipment"] = {b"test-key": b"\x06\x09equip-fixture"}
        self.server.instance_tokens["test-instance"] = time.monotonic() + 30
        client, _ = self.connect()
        request = b"\x01\x0d\x01\x00" + string("test-instance")
        self.send(client, request)
        self.assertEqual(receive(client), b"\x01\x0d\x01\x00\x00")
        self.assertEqual(receive(client), self.server.packets["zone-init"])
        self.send(client, b"\x06\x01")
        for name in ("zone-start", "hero-create", "hero-state"):
            self.assertEqual(receive(client), self.server.packets[name])
        self.send(client, b"\x06\x20\x02\x00test-key")
        self.assertEqual(receive(client), b"\x06\x20\x02\x00\x00\x01")
        self.assertEqual(receive(client), b"\x06\x09equip-fixture")
        self.send(client, b"\x06\x20\x03\x00unknown")
        self.assertEqual(receive(client), b"\x06\x20\x03\x00\x00\x00")
        movement = b"\x06\x0f" + struct.pack("<IQ", 1, 0) + b"\xff\x80" + struct.pack("<fff",14.5,4,23.5) + bytes(18)
        self.send(client, movement)
        # A round-trip on the same stream ensures the movement handler completed.
        self.send(client, b"\x06\x20\x04\x00unknown")
        self.assertEqual(receive(client)[4:], b"\x00\x00")
        self.assertIn("local_movement_received", self.server.event_path.read_text())
        self.send(client, b"\x06\x01")
        with self.assertRaises(EOFError):
            receive(client)
        replay, _ = self.connect()
        self.send(replay, request)
        self.assertEqual(receive(replay)[4], 255)


if __name__ == "__main__":
    unittest.main()
