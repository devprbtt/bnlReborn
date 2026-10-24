#!/usr/bin/env python3
"""Drop only established HTTPS sockets owned by the Reborn presence bot.

Run with sudo after deploying the fix. This intentionally interrupts Discord
connectivity briefly; it never changes firewall rules or game-server sockets.
"""
import ipaddress
import subprocess

units = ("bnl-discordbot.service",)
for unit in units:
    pid = int(subprocess.check_output(
        ["systemctl", "show", unit, "-p", "MainPID", "--value"], text=True).strip())
    if pid <= 1:
        raise RuntimeError(f"{unit} has no running process")
    sockets = subprocess.check_output(["ss", "-Htnp"], text=True)
    targets = []
    for line in sockets.splitlines():
        fields = line.split()
        if len(fields) < 6 or fields[0] != "ESTAB" or f"pid={pid}," not in line:
            continue
        local_ip, _, local_port = fields[3].rpartition(":")
        peer_ip, _, peer_port = fields[4].rpartition(":")
        if peer_port != "443":
            continue
        ipaddress.ip_address(local_ip)
        if not ipaddress.ip_address(peer_ip).is_global:
            raise RuntimeError("Unexpected non-public HTTPS peer")
        if not local_port.isdecimal():
            raise RuntimeError("Invalid local port")
        targets.append((local_ip, local_port, peer_ip))
    if not targets:
        raise RuntimeError(f"No established Discord HTTPS connection found for {unit}")
    for local_ip, local_port, peer_ip in targets:
        subprocess.run(["ss", "-K", "src", local_ip, "sport", "=", f":{local_port}",
                        "dst", peer_ip, "dport", "=", ":443"], check=True)
    print(f"Interrupted {len(targets)} bot-only HTTPS sockets: {unit} PID={pid}", flush=True)
