#!/usr/bin/env python3
"""Run as root from an uploaded source directory; token is read only from /run.

Retains the verified deployed Rust binary; adds an authentication preflight.
Does not restart or modify the game service. Retires the China bot permanently.
"""
import grp
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path("/opt/bnl-discordbot")
RELEASE = ROOT / "releases/single-server-20261023"
TOKEN = Path("/run/bnl-discord-replacement-token")
BINARY_SHA = "3c3e37d96b43ea2123ea8c0ca2800e3f5bd32880e2b8b3d0b21b2a33252b2e09"


def run(*args):
    subprocess.run(args, check=True)


def main():
    if os.geteuid() != 0:
        raise SystemExit("Run with sudo")
    source = Path(__file__).resolve().parent
    spec = importlib.util.spec_from_file_location("preflight", source / "run-bot.py")
    preflight = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(preflight)
    token = TOKEN.read_text().strip()
    code, message = preflight.validate(token)
    print(message, flush=True)
    if code:
        return code
    old = (ROOT / "current").resolve(strict=True)
    binary = old / "bot"
    if hashlib.sha256(binary.read_bytes()).hexdigest() != BINARY_SHA:
        raise SystemExit("Unexpected deployed binary; refusing replacement")
    if RELEASE.exists():
        raise SystemExit("Release already exists; inspect before retrying")
    run("systemd-analyze", "verify", str(source / "bnl-discordbot.service"))
    gid = grp.getgrnam("bnldiscord").gr_gid
    backup = Path("/root/config-backups") / ("discord-single-server-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    shutil.copy2("/etc/systemd/system/bnl-discordbot.service", backup / "bnl-discordbot.service")
    shutil.copy2("/etc/bnl/discordbot/main.env", backup / "main.env")
    (backup / "previous-release.txt").write_text(str(old) + "\n")
    RELEASE.mkdir(mode=0o750)
    os.chown(RELEASE, 0, gid)
    for src, name in ((binary, "bot"), (source / "run-bot.py", "run-bot.py")):
        dest = RELEASE / name
        shutil.copyfile(src, dest)
        os.chmod(dest, 0o750)
        os.chown(dest, 0, gid)
    run("systemctl", "stop", "bnl-discordbot.service")
    env = Path("/etc/bnl/discordbot/main.env")
    temporary = env.with_suffix(".new")
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w") as stream:
        stream.write("BOT_TOKEN=" + token + "\nSTATUS_URL=" + preflight.STATUS_URL + "\nPOLL_INTERVAL=10\nMAX_POLLS=0\n")
    os.chown(temporary, 0, gid)
    os.chmod(temporary, 0o640)
    os.replace(temporary, env)
    shutil.copyfile(source / "bnl-discordbot.service", "/etc/systemd/system/bnl-discordbot.service")
    os.chmod("/etc/systemd/system/bnl-discordbot.service", 0o644)
    link = ROOT / ".current-single-server"
    link.symlink_to(RELEASE)
    os.replace(link, ROOT / "current")
    # No secondary endpoint, token, enabled unit, or reconnection-test target remains.
    run("systemctl", "disable", "--now", "bnl-discordbot-cn.service")
    for retired in ("/etc/systemd/system/bnl-discordbot-cn.service", "/etc/bnl/discordbot/china.env"):
        Path(retired).unlink(missing_ok=True)
    run("systemctl", "daemon-reload")
    run("systemctl", "reset-failed", "bnl-discordbot.service")
    run("systemctl", "enable", "--now", "bnl-discordbot.service")
    TOKEN.unlink()
    print("Deployed single Reborn bot; previous main config retained at " + str(backup), flush=True)
    print("Verify READY, presence refresh and visible Discord activity; queued presence is not an ACK.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
