"""Loopback-only protocol-65 menu and limited solo practice service. No production matches."""
import argparse
import hashlib
import io
import math
import json
import secrets
import socket
import select
from practice import Practice
import struct
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

MAX_FRAME = 65536


def varint(value):
    result = bytearray()
    while value >= 128:
        result.append((value & 127) | 128)
        value >>= 7
    return bytes(result) + bytes([value])


def read_exact(stream, count):
    data = bytearray()
    while len(data) < count:
        part = stream.recv(count - len(data))
        if not part:
            raise EOFError()
        data.extend(part)
    return bytes(data)


def receive(stream):
    size = 0
    for shift in range(0, 35, 7):
        value = read_exact(stream, 1)[0]
        size |= (value & 127) << shift
        if not value & 128:
            if not 2 <= size <= MAX_FRAME:
                raise ValueError("Invalid frame length")
            return read_exact(stream, size)
    raise ValueError("Invalid frame prefix")


def string(value):
    data = value.encode("utf-8")
    return varint(len(data)) + data


def read_string(reader):
    size = 0
    for shift in range(0, 35, 7):
        part = reader.read(1)
        if not part:
            raise ValueError("Truncated string")
        size |= (part[0] & 127) << shift
        if not part[0] & 128:
            if size > 4096:
                raise ValueError("String too long")
            data = reader.read(size)
            if len(data) != size:
                raise ValueError("Truncated string")
            return data.decode("utf-8")
    raise ValueError("Invalid string prefix")


class MenuServer:
    def __init__(self, packets, event_path, port=27065, terrain_test=False):
        self.packets = packets
        self.event_path = event_path
        self.port = port
        self.tokens = {}
        self.instance_tokens = {}
        self.terrain_test = terrain_test
        self.lock = threading.Lock()

    def event(self, kind, **fields):
        # Never log wire payloads, login credentials, or session tokens.
        with self.lock:
            with self.event_path.open("a", encoding="utf-8") as f:
                f.write(json.dumps({"time": time.time(), "kind": kind, **fields}) + "\n")

    @staticmethod
    def send(connection, packet):
        connection.sendall(varint(len(packet)) + packet)

    def session(self, connection):
        verified = False
        authenticated = False
        instance = False
        spawned = False
        practice = None
        last_movement_log = 0
        try:
            with connection:
                connection.settimeout(600)
                while True:
                    if practice:
                        practice.tick()
                        if not select.select([connection], [], [], 0.1)[0]:
                            continue
                    packet = receive(connection)
                    service, function = packet[:2]
                    if (service, function) != (6, 15):
                        self.event("received", service=service, function=function)
                    if (service, function) == (1, 0):
                        if len(packet) != 13 or packet[4] != 0xC0:
                            raise ValueError("Invalid version request")
                        version, hash_value = struct.unpack_from("<ii", packet, 5)
                        verified = (version, hash_value) == (65, 0)
                        self.send(connection, packet[:4] + b"\x00" + bytes([verified, 0xC0]) + struct.pack("<ii", 65, 0))
                        if not verified:
                            return
                    elif not verified:
                        raise ValueError("Version must be checked first")
                    elif (service, function) == (1, 1):
                        reader = io.BytesIO(packet[4:])
                        name, password = read_string(reader), read_string(reader)
                        if reader.read() or name != "BetaLocal" or password != "local-diagnostic-only":
                            self.send(connection, packet[:4] + b"\xff" + string("Use the local BetaLocal profile."))
                            return
                        token = secrets.token_hex(32)
                        with self.lock:
                            now = time.monotonic()
                            self.tokens = {k: v for k, v in self.tokens.items() if v > now}
                            self.tokens[token] = now + 30
                        self.send(connection, packet[:4] + b"\x00" + struct.pack("<I", 1))
                        self.send(connection, b"\x01\x08" + string("127.0.0.1") + struct.pack("<i", self.port) + string(token))
                        self.event("master_login_accepted")
                    elif (service, function) == (1, 9):
                        reader = io.BytesIO(packet[4:])
                        token = read_string(reader)
                        with self.lock:
                            expiry = self.tokens.pop(token, 0)
                        if reader.read() or expiry < time.monotonic():
                            self.send(connection, packet[:4] + b"\xff" + string("Local session expired; relogin."))
                            return
                        authenticated = True
                        self.send(connection, packet[:4] + b"\x00\x00")
                        for name in ("catalogue", "player", "server-update"):
                            self.send(connection, self.packets[name])
                        self.send(connection, b"\x01\x0b")
                        self.send(connection, self.packets["terrain-scene"] if self.terrain_test else self.packets["scene"])
                        if self.terrain_test:
                            token = secrets.token_hex(32)
                            with self.lock:
                                self.instance_tokens[token] = time.monotonic() + 120
                            self.send(connection, b"\x02\x02" + string("127.0.0.1") + struct.pack("<i",self.port) + string(token))
                        self.event("region_login_accepted")
                    elif (service, function) == (1, 13) and self.terrain_test:
                        reader = io.BytesIO(packet[4:])
                        token = read_string(reader)
                        with self.lock:
                            expiry = self.instance_tokens.pop(token, 0)
                        if reader.read() or expiry < time.monotonic():
                            self.send(connection, packet[:4] + b"\xff" + string("Terrain test session expired."))
                            return
                        authenticated = True
                        instance = True
                        self.send(connection, packet[:4] + b"\x00")
                        self.send(connection, self.packets["zone-init"])
                        self.event("terrain_instance_initialized")
                    elif not authenticated:
                        raise ValueError("Login required")
                    elif self.terrain_test and instance and (service, function) == (6, 1):
                        if spawned:
                            raise ValueError("Duplicate zone readiness")
                        spawned = True
                        self.event("terrain_ready")
                        self.send(connection, self.packets["zone-start"])
                        for name in ("hero-create", "hero-state"):
                            if name in self.packets:
                                self.send(connection, self.packets[name])
                        if "practice" in self.packets:
                            practice = Practice(self.packets, lambda p: self.send(connection,p), self.event)
                            practice.start()
                    elif practice and service == 6 and practice.handle(packet):
                        pass
                    elif instance and spawned and (service, function) == (6, 32):
                        key = packet[4:]
                        equipment = self.packets.get("equipment", {})
                        accepted = key in equipment and (practice is None or practice.switch(key))
                        self.send(connection, packet[:4] + bytes([0, accepted]))
                        if accepted:
                            self.send(connection, equipment[key])
                        self.event("gear_switch", accepted=accepted)
                    elif instance and spawned and (service, function) == (6, 15):
                        # Observe local prediction only; no authoritative physics implemented.
                        if len(packet) != 46 or struct.unpack_from("<I", packet, 2)[0] != 1 or packet[14:16] != b"\xff\x80":
                            raise ValueError("Invalid local unit movement")
                        position = struct.unpack_from("<fff", packet, 16)
                        if not all(math.isfinite(v) for v in position):
                            raise ValueError("Non-finite movement")
                        if practice:
                            practice.move(position)
                        if time.monotonic() - last_movement_log > 5:
                            self.event("local_movement_received", position=position)
                            last_movement_log = time.monotonic()
                    elif self.terrain_test and instance and (service, function) == (9, 11):
                        pass  # Loader Ready messages; no lobby gameplay implemented.
                    elif (service, function) == (2, 1):
                        self.event("terrain_scene_entered" if self.terrain_test else "main_menu_entered")
                    elif (service, function) == (5, 31):
                        self.send(connection, packet[:4] + b"\x00" + self.packets["profile"][2:])
                    elif service == 12 and function in (0, 1, 2, 3):
                        # Empty local leaderboard; optional 'me' is absent for top queries.
                        self.send(connection, packet[:4] + b"\x00\x00" + (b"\x00" if function in (0, 2) else b""))
                    elif (service, function) == (3, 1):
                        self.send(connection, packet[:4] + b"\x00" + struct.pack("<q", int(time.time()*1000)))
                    elif service == 5 and function in (6, 7, 8, 19, 27):
                        pass  # Client metadata; no personal data persisted.
                    else:
                        self.event("unsupported", service=service, function=function)
        except EOFError:
            self.event("disconnected")
        except (OSError, ValueError) as error:
            self.event("connection_error", error=type(error).__name__)

    def run(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", self.port))
            listener.listen(4)
            self.event("listening", address="127.0.0.1", port=self.port)
            while True:
                connection, _ = listener.accept()
                threading.Thread(target=self.session, args=(connection,), daemon=True).start()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packets", type=Path, required=True)
    parser.add_argument("--events", type=Path, required=True)
    parser.add_argument("--terrain-test", action="store_true", help="Experimental terrain/solo practice; NOT a production match")
    args = parser.parse_args()
    packets = {name: (args.packets / (name + ".bin")).read_bytes()
               for name in ("catalogue", "player", "scene", "server-update", "profile")}
    if args.terrain_test:
        packets.update({name: (args.packets / (name + ".bin")).read_bytes() for name in ("terrain-scene", "zone-init", "zone-start")})
    if args.terrain_test:
        for name in ("hero-create", "hero-state"):
            path = args.packets / (name + ".bin")
            if path.exists():
                packets[name] = path.read_bytes()
    if "hero-create" in packets:
        packets["equipment"] = {path.read_bytes(): (args.packets / ("equip-" + path.name[4:])).read_bytes()
                                for path in args.packets.glob("key-*.bin")}
    if "hero-create" in packets and (args.packets / "practice.json").exists():
        packets["practice"] = json.loads((args.packets / "practice.json").read_text())
        packets["keys"] = {path.stem[4:]:path.read_bytes() for path in args.packets.glob("key-*.bin")}
        for name in ("target-create", "target-state", "terrain", "brick-key"):
            packets[name] = (args.packets / (name + ".bin")).read_bytes()
    args.events.parent.mkdir(parents=True, exist_ok=True)
    class Feed(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/health":
                payload = json.dumps({"service":"bnl-beta65-menu","protocol":65,"matches":False,"mode":"terrain-test" if args.terrain_test else "menu","hero":"hero-create" in packets,"packet_revision":hashlib.sha256((args.packets / "provenance.json").read_bytes()).hexdigest() if (args.packets / "provenance.json").exists() else None}).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
                return
            if self.path.split("?")[0] != "/feed":
                self.send_error(404)
                return
            payload = json.dumps({"channel": [{"title": "Local beta recovery", "description": "Menu compatibility preview; matches are unavailable.", "link": "", "items": []}]}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def log_message(self, *_):
            pass

    with ThreadingHTTPServer(("127.0.0.1", 27066), Feed) as http:
        threading.Thread(target=http.serve_forever, daemon=True).start()
        MenuServer(packets, args.events, terrain_test=args.terrain_test).run()


if __name__ == "__main__":
    main()
