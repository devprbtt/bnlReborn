#!/usr/bin/env python3
"""Validate the configured bot before starting the existing Rust gateway client.

Exit 78 for configuration/authentication errors (systemd must not restart).
Transient network/Discord failures exit 1 and are retried by systemd.
Never log credentials or HTTP response bodies.
"""
import json
import os
from pathlib import Path
import sys
import urllib.error
import urllib.request

APPLICATION_ID = "1555383788865982544"
GUILD_ID = "283986382660173824"
STATUS_URL = "http://127.0.0.1:8080/api/public/status"


def discord_get(path, token):
    request = urllib.request.Request(
        "https://discord.com/api/v10/" + path,
        headers={"Authorization": "Bot " + token, "User-Agent": "BNLDiscordPresence/1.0"})
    with urllib.request.urlopen(request, timeout=15) as response:
        return json.load(response)


def validate(token, fetch=discord_get):
    if not token or any(c.isspace() for c in token):
        return 78, "Missing or malformed BOT_TOKEN; update protected main.env."
    try:
        user = fetch("users/@me", token)
        if user.get("id") != APPLICATION_ID or user.get("bot") is not True:
            return 78, "Token belongs to an unexpected Discord identity."
        guilds = fetch("users/@me/guilds", token)
        if not any(guild.get("id") == GUILD_ID for guild in guilds):
            return 78, "Invite this bot to Block N Load Community before starting."
    except urllib.error.HTTPError as error:
        if error.code in (401, 403):
            return 78, "Discord rejected the bot credential; replace token and restart service."
        return 1, "Discord preflight temporarily failed (HTTP %d)." % error.code
    except (OSError, ValueError, TypeError, AttributeError):
        return 1, "Discord preflight unavailable; retrying without exposing response data."
    return 0, "Discord bot identity and community membership verified."


def main():
    if os.environ.get("STATUS_URL") != STATUS_URL:
        print("STATUS_URL must name the local Reborn status endpoint.", flush=True)
        return 78
    code, message = validate(os.environ.get("BOT_TOKEN", ""))
    print(message, flush=True)
    if code:
        return code
    os.execv(str(Path(__file__).resolve().with_name("bot")), ["bot"])


if __name__ == "__main__":
    sys.exit(main())
