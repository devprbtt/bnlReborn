"""Persistent, read-only game telemetry and one editable Discord chart message."""
import io
import json
import math
import os
from pathlib import Path
import re
import sqlite3
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

from PIL import Image, ImageDraw, ImageFont

CHANNEL = "1564744213390696528"
GUILD = "283986382660173824"
BOT = "1555383788865982544"
MARKER = "BNL player history • GMT"
STATUS = "http://127.0.0.1:8080/api/public/status"


def database(path):
    db = sqlite3.connect(path)
    db.execute("CREATE TABLE IF NOT EXISTS samples (minute INTEGER PRIMARY KEY, count INTEGER)")
    db.execute("CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
    return db


def record(db, timestamp, count):
    minute = int(timestamp) // 60 * 60
    if count is not None and (type(count) is not int or count < 0):
        count = None
    db.execute("INSERT OR REPLACE INTO samples VALUES (?,?)", (minute, count))
    db.execute("DELETE FROM samples WHERE minute < ?", (minute - 25 * 3600,))
    db.commit()


def import_journal(db, lines):
    samples = {}
    for line in lines:
        row = json.loads(line)
        match = re.search(r"\bplayer_count=(None|\d+)\b", row.get("MESSAGE", ""))
        if match:
            stamp = int(row["__REALTIME_TIMESTAMP"]) // 1000000
            minute = stamp // 60 * 60
            if minute not in samples or stamp >= samples[minute][0]:
                samples[minute] = (stamp, None if match[1] == "None" else int(match[1]))
    for minute, (_, count) in sorted(samples.items()):
        db.execute("INSERT OR IGNORE INTO samples VALUES (?,?)", (minute, count))
    db.commit()
    return len(samples)


def window(db, now):
    end = int(now) // 60 * 60
    return list(db.execute("SELECT minute,count FROM samples WHERE minute > ? AND minute <= ? ORDER BY minute", (end - 86400, end)))


def segments(rows):
    parts, part = [], []
    for stamp, count in rows:
        if count is None or (part and stamp - part[-1][0] > 90):
            if part:
                parts.append(part)
            part = []
        if count is not None:
            part.append((stamp, count))
    if part:
        parts.append(part)
    return parts


def render(rows, now):
    image = Image.new("RGB", (1200, 560), "#151b24")
    draw = ImageDraw.Draw(image)
    font = lambda size: ImageFont.load_default(size=size)
    draw.text((40, 25), "BLOCK N LOAD REBORN", fill="#f4c259", font=font(27))
    draw.text((40, 67), "Players online • Last 24 hours • GMT (UTC+0)", fill="#e2e8f0", font=font(20))
    end = int(now) // 60 * 60
    start = end - 86400
    values = [n for _, n in rows if n is not None]
    peak = max(values, default=0)
    top = max(5, math.ceil(peak / 5) * 5)
    left, right, upper, lower = 75, 1155, 150, 445
    xy = lambda stamp, n: (left + (stamp - start) / 86400 * (right - left), lower - n / top * (lower - upper))
    for i in range(6):
        n = top * i / 5
        y = xy(start, n)[1]
        draw.line((left, y, right, y), fill="#34404e")
        draw.text((20, y - 8), f"{n:g}", fill="#aebdce", font=font(16))
    for i in range(9):
        stamp = start + i * 10800
        x = xy(stamp, 0)[0]
        draw.line((x, upper, x, lower), fill="#222e3c")
        draw.text((x - 23, lower + 15), datetime.fromtimestamp(stamp, timezone.utc).strftime("%H:%M"), fill="#aebdce", font=font(15))
    for part in segments(rows):
        points = [xy(t, n) for t, n in part]
        if len(points) > 1:
            draw.line(points, fill="#51d9e0", width=3)
        else:
            x, y = points[0]
            draw.ellipse((x-2, y-2, x+2, y+2), fill="#51d9e0")
    current = rows[-1][1] if rows and end - rows[-1][0] < 90 else None
    average = f"{sum(values)/len(values):.1f}" if values else "—"
    draw.text((75, 112), f"NOW  {current if current is not None else 'Unavailable'}     PEAK  {peak if values else '—'}     AVERAGE  {average}", fill="#ffffff", font=font(20))
    coverage = len(values) / 1440 * 100
    draw.text((40, 506), f"Recorded coverage: {coverage:.0f}% • Gaps = unavailable data • Average uses recorded samples", fill="#aebdce", font=font(16))
    out = io.BytesIO()
    image.save(out, format="PNG")
    return out.getvalue()


class Discord:
    def __init__(self, token):
        self.token = token

    def request(self, method, path, payload=None, png=None):
        headers = {"Authorization": "Bot " + self.token, "User-Agent": "BNLPlayerGraph/1.0"}
        data = None
        if png is not None:
            boundary = "bnl-" + os.urandom(16).hex()
            headers["Content-Type"] = "multipart/form-data; boundary=" + boundary
            data = (f'--{boundary}\r\nContent-Disposition: form-data; name="payload_json"\r\nContent-Type: application/json\r\n\r\n'.encode() + json.dumps(payload).encode() +
                    f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="files[0]"; filename="players.png"\r\nContent-Type: image/png\r\n\r\n'.encode() + png + f'\r\n--{boundary}--\r\n'.encode())
        elif payload is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(payload).encode()
        for attempt in range(3):
            try:
                req = urllib.request.Request("https://discord.com/api/v10/" + path, data=data, headers=headers, method=method)
                with urllib.request.urlopen(req, timeout=25) as response:
                    return json.load(response)
            except urllib.error.HTTPError as exc:
                if exc.code != 429 or attempt == 2:
                    raise
                delay = float(json.load(exc).get("retry_after", 5))
                time.sleep(max(1, min(delay, 3600)))


def is_graph(message):
    return message.get("author", {}).get("id") == BOT and any(e.get("footer", {}).get("text") == MARKER for e in message.get("embeds", []))


def publish(db, api, png, now):
    key = "message:" + CHANNEL
    saved = db.execute("SELECT value FROM state WHERE key=?", (key,)).fetchone()
    message_id = saved[0] if saved else None
    if not message_id:
        # Recover a message created before a crash, rather than posting duplicates.
        messages = api.request("GET", f"channels/{CHANNEL}/messages?limit=100")
        message_id = next((m["id"] for m in messages if is_graph(m)), None)
    payload = {"allowed_mentions": {"parse": []}, "attachments": [{"id": 0, "filename": "players.png"}],
               "embeds": [{"title": "Player count — last 24 hours", "color": 0x51d9e0,
               "description": "Times in GMT (UTC+0). Samples every minute; graph refreshes every 5 minutes.",
               "image": {"url": "attachment://players.png"}, "footer": {"text": MARKER},
               "timestamp": datetime.fromtimestamp(now, timezone.utc).isoformat()}]}
    try:
        if message_id:
            result = api.request("PATCH", f"channels/{CHANNEL}/messages/{message_id}", payload, png)
        else:
            payload.update(nonce="bnlg" + CHANNEL, enforce_nonce=True)
            result = api.request("POST", f"channels/{CHANNEL}/messages", payload, png)
    except urllib.error.HTTPError as exc:
        if exc.code == 404 and message_id:
            db.execute("DELETE FROM state WHERE key=?", (key,))
            db.commit()
        raise
    if not is_graph(result):
        raise ValueError("Discord returned an unexpected message")
    db.execute("INSERT OR REPLACE INTO state VALUES (?,?)", (key, result["id"]))
    db.commit()
    print("graph_message=" + result["id"], flush=True)
    return result


def main():
    api = Discord(os.environ["BOT_TOKEN"])
    channel = api.request("GET", "channels/" + CHANNEL)
    if channel.get("guild_id") != GUILD:
        raise ValueError("Unexpected channel guild")
    db = database(Path(os.environ.get("STATE_DIRECTORY", "/var/lib/bnl-discord-graph")) / "history.sqlite")
    next_publish = 0
    while True:
        now = time.time()
        try:
            with urllib.request.urlopen(STATUS, timeout=10) as response:
                count = json.load(response).get("player_count")
        except (OSError, ValueError):
            count = None
        record(db, now, count)
        if now >= next_publish:
            try:
                publish(db, api, render(window(db, now), now), now)
                next_publish = now + 300
            except (OSError, ValueError) as exc:
                print("graph publish failed: " + type(exc).__name__ + " code=" + str(getattr(exc, "code", "")), flush=True)
                next_publish = now + 60
        time.sleep(max(1, 60 - time.time() % 60))


if __name__ == "__main__":
    main()
