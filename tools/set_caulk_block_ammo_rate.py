"""Set the Caulk Gun's block/device ammo drain (block_ammo_rate) to half its player drain.

Each channel tool's ammo.rate stays the drain while channelling a player; the new
server-only block_ammo_rate is the drain on blocks and devices (see
ToolChannel.BlockAmmoRate). Base gun 1 / 3 -> 0.5 / 1.5, Sticky Caulk 2 / 6 -> 1 / 3.
Both values are plain card fields, editable in the catalogue per tool. A server
without BlockAmmoRate ignores the field.

Read-only by default; ``--apply`` writes revision-checked documents with a
private backup and readback. Re-running plans nothing.
"""

import base64
import copy
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

GEAR_IDS = ("gear_engineer_caulk_gun", "gear_engineer_caulk_gun_sticky_caulk")
FACTOR = 0.5


def plan(card):
    changed = copy.deepcopy(card)
    for tool in changed["tools"]:
        assert tool["type"] == "channel"
        tool.setdefault("block_ammo_rate", tool["ammo"]["rate"] * FACTOR)
    return changed


def audit(cards):
    return {gear_id: [{"player": t["ammo"]["rate"], "block_or_device": t["block_ammo_rate"]}
                      for t in cards[gear_id]["tools"]] for gear_id in GEAR_IDS}


config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
config = {key.replace("_", "").lower(): value for key, value in config.items()}
base_url = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
auth = "Basic " + base64.b64encode(
    (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
).decode()


def request(name, data=None):
    req = urllib.request.Request(
        base_url + "/" + urllib.parse.quote(name, safe=""),
        headers={"Authorization": auth, "Content-Type": "application/json"},
        data=None if data is None else json.dumps(data).encode(),
        method="GET" if data is None else "PUT",
    )
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)


before = {gear_id: request(gear_id) for gear_id in GEAR_IDS}
after = {gear_id: plan(card) for gear_id, card in before.items()}
changes = {k: v for k, v in after.items() if v != before[k]}
print(json.dumps({"write": list(changes), "revisions": {k: v["_rev"] for k, v in before.items()},
                  "after": audit(after)}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("caulk-block-ammo-rate-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for gear_id, card in changes.items():
    request(gear_id, card)
saved = {gear_id: request(gear_id) for gear_id in GEAR_IDS}
for gear_id, planned in after.items():
    assert {k: v for k, v in saved[gear_id].items() if k != "_rev"} == \
        {k: v for k, v in planned.items() if k != "_rev"}, "readback differs for " + gear_id
print(json.dumps({"backup": str(backup), "revisions": {k: v["_rev"] for k, v in saved.items()},
                  "verified": audit(saved)}, indent=2), flush=True)
