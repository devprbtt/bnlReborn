"""Loopback-only protocol-65 menu compatibility service. No match simulation."""
import argparse
import io
import json
import secrets
import socket
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
    def __init__(self, packets, event_path, port=27065):
        self.packets = packets
        self.event_path = event_path
        self.port = port
        self.tokens = {}
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
        try:
            with connection:
                connection.settimeout(600)
                while True:
                    packet = receive(connection)
                    service, function = packet[:2]
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
                        self.send(connection, self.packets["scene"])
                        self.event("region_login_accepted")
                    elif not authenticated:
                        raise ValueError("Login required")
                    elif (service, function) == (2, 1):
                        self.event("main_menu_entered")
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
    args = parser.parse_args()
    packets = {name: (args.packets / (name + ".bin")).read_bytes()
               for name in ("catalogue", "player", "scene", "server-update", "profile")}
    args.events.parent.mkdir(parents=True, exist_ok=True)
    class Feed(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/health":
                payload = b'{"service":"bnl-beta65-menu","protocol":65,"matches":false}'
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
        MenuServer(packets, args.events).run()


if __name__ == "__main__":
    main()
