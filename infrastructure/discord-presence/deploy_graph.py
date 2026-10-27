"""Install graph companion only; never restart the game or presence service.

Upload this directory and the pinned Linux CPython 3.13 Pillow wheel, then run
sudo python3 deploy_graph.py. Credentials remain in the existing protected env.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import pwd
import shutil
import subprocess
import sys
import time
import zipfile

WHEEL_SHA = "eedf4b74eda2b5a4b2b2fb4c006d6295df3bf29e459e198c90ea48e130dc75c3"


def main():
    if os.geteuid() != 0:
        raise SystemExit("Run as root")
    source = Path(__file__).resolve().parent
    wheel = next(source.glob("pillow-12.2.0-cp313-*.whl"))
    if hashlib.sha256(wheel.read_bytes()).hexdigest() != WHEEL_SHA:
        raise SystemExit("Dependency hash mismatch")
    root = Path("/opt/bnl-discord-graph")
    release = root / "releases" / time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    release.mkdir(parents=True)
    for name in ["player_graph.py", "test_graph.py", "run-bot.py", "test_preflight.py", "bnl-discord-graph.service"]:
        shutil.copyfile(source / name, release / name)
    with zipfile.ZipFile(wheel) as archive:
        archive.extractall(release / "vendor")
    env = dict(os.environ, PYTHONPATH=str(release / "vendor"), PYTHONDONTWRITEBYTECODE="1")
    subprocess.run([sys.executable, "-m", "unittest", "discover", "-s", str(release), "-p", "test_*.py"], env=env, check=True)
    sys.path[:0] = [str(release), str(release / "vendor")]
    import player_graph as graph
    config = {}
    for line in Path("/etc/bnl/discordbot/main.env").read_text().splitlines():
        k, sep, v = line.partition("=")
        if sep:
            config[k] = v
    api = graph.Discord(config["BOT_TOKEN"])
    user = api.request("GET", "users/@me")
    channel = api.request("GET", "channels/" + graph.CHANNEL)
    if user.get("id") != graph.BOT or channel.get("guild_id") != graph.GUILD:
        raise SystemExit("Unexpected bot or channel identity")
    api.request("GET", f"channels/{graph.CHANNEL}/messages?limit=1")
    owner = pwd.getpwnam("bnldiscord")
    state = Path("/var/lib/bnl-discord-graph")
    state.mkdir(mode=0o700, exist_ok=True)
    os.chown(state, owner.pw_uid, owner.pw_gid)
    if (root / "current").exists():
        subprocess.run(["systemctl", "stop", "bnl-discord-graph.service"], check=True)
        (release / "previous-release.txt").write_text(str((root / "current").resolve()))
    db = graph.database(state / "history.sqlite")
    journal = subprocess.check_output(["journalctl", "-u", "bnl-discordbot.service", "--since", "24 hours ago", "-o", "json", "--no-pager"], text=True)
    count = graph.import_journal(db, journal.splitlines())
    (state / "preview.png").write_bytes(graph.render(graph.window(db, time.time()), time.time()))
    db.close()
    for path in state.iterdir():
        os.chown(path, owner.pw_uid, owner.pw_gid)
        os.chmod(path, 0o600)
    temporary = root / ".current-new"
    temporary.symlink_to(release)
    os.replace(temporary, root / "current")
    shutil.copyfile(release / "bnl-discord-graph.service", "/etc/systemd/system/bnl-discord-graph.service")
    subprocess.run(["systemd-analyze", "verify", "/etc/systemd/system/bnl-discord-graph.service"], check=True)
    subprocess.run(["systemctl", "daemon-reload"], check=True)
    subprocess.run(["systemctl", "enable", "--now", "bnl-discord-graph.service"], check=True)
    print(json.dumps({"release": str(release), "imported_minutes": count, "channel": graph.CHANNEL}))


if __name__ == "__main__":
    main()
