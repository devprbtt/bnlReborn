"""Loopback-only public aggregates and signed Discord interactions."""
import concurrent.futures
import json
import os
import sqlite3
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
import player_graph as g


def verify(body, timestamp, signature, key, now=None):
    try:
        if abs((now or time.time()) - int(timestamp)) > 300:
            return False
        Ed25519PublicKey.from_public_bytes(bytes.fromhex(key)).verify(bytes.fromhex(signature), timestamp.encode() + body)
        return True
    except (ValueError, TypeError, InvalidSignature):
        return False


def selection(data):
    if (data.get("type") != 3 or str(data.get("application_id")) != g.BOT or
        str(data.get("guild_id")) != g.GUILD or str(data.get("channel_id")) != g.CHANNEL or
        data.get("message", {}).get("author", {}).get("id") != g.BOT):
        return None
    return {"bnl_graph:1": 1, "bnl_graph:7": 7, "bnl_graph:30": 30}.get(data.get("data", {}).get("custom_id"))


def read_rows(path, now, days):
    db = sqlite3.connect("file:" + str(path) + "?mode=ro", uri=True, timeout=3)
    try:
        return g.window(db, now, days)
    finally:
        db.close()


def complete(api, db_path, interaction, days):
    now = time.time()
    rows = read_rows(db_path, now, days)
    title = "24 hours" if days == 1 else str(days) + " days"
    payload = {"allowed_mentions": {"parse": []}, "attachments": [{"id": 0, "filename": "players.png"}],
               "content": "Your private view — last " + title + ". Times in GMT (UTC+0). Missing history is shown as gaps.",
               "embeds": [{"image": {"url": "attachment://players.png"}}]}
    api.request("PATCH", f"webhooks/{g.BOT}/{interaction['token']}/messages/@original", payload, g.render(rows, now, days))


def make_handler(db_path, api, public_key):
    pool = concurrent.futures.ThreadPoolExecutor(max_workers=4)
    capacity = threading.BoundedSemaphore(4)

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def reply(self, status, payload):
            body = json.dumps(payload, separators=(",", ":")).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            url = urlparse(self.path)
            if url.path != "/player-history/data":
                return self.reply(404, {"error": "Not found"})
            try:
                days = int(parse_qs(url.query).get("days", ["1"])[0])
                now = int(time.time())
                rows = read_rows(db_path, now, days)
                return self.reply(200, {"generated_at": now, "days": days, "samples": rows})
            except (ValueError, sqlite3.Error):
                return self.reply(400, {"error": "History unavailable or invalid period"})

        def do_POST(self):
            if self.path != "/discord/player-graph-interactions":
                return self.reply(404, {"error": "Not found"})
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                return self.reply(400, {"error": "Invalid length"})
            if length < 1 or length > 65536:
                return self.reply(413, {"error": "Invalid body size"})
            self.connection.settimeout(5)
            body = self.rfile.read(length)
            if not verify(body, self.headers.get("X-Signature-Timestamp", ""), self.headers.get("X-Signature-Ed25519", ""), public_key):
                return self.reply(401, {"error": "Invalid signature"})
            try:
                data = json.loads(body)
                if not isinstance(data, dict):
                    raise ValueError()
            except ValueError:
                return self.reply(400, {"error": "Invalid JSON"})
            if data.get("type") == 1:
                return self.reply(200, {"type": 1})
            days = selection(data)
            if days is None or not isinstance(data.get("token"), str):
                return self.reply(200, {"type": 4, "data": {"flags": 64, "content": "This graph control is unavailable."}})
            if not capacity.acquire(blocking=False):
                return self.reply(200, {"type": 4, "data": {"flags": 64, "content": "The graph is busy. Please try again shortly."}})
            def work():
                try:
                    complete(api, db_path, data, days)
                except Exception as exc:
                    print("private graph failed: " + type(exc).__name__, flush=True)
                    try:
                        api.request("PATCH", f"webhooks/{g.BOT}/{data['token']}/messages/@original", {"content": "History is temporarily unavailable. Please try again."})
                    except Exception:
                        pass
                finally:
                    capacity.release()
            try:
                self.reply(200, {"type": 5, "data": {"flags": 64}})
            except Exception:
                capacity.release()
                raise
            pool.submit(work)

    return Handler


def start_http(path, api):
    key = os.environ["DISCORD_VERIFY_KEY"]
    server = ThreadingHTTPServer(("127.0.0.1", 8097), make_handler(path, api, key))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server
