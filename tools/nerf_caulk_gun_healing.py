"""Nerf Tony's Caulk Gun ally healing by 30% and halve its ammo pool.

Ally heal 6 -> 4.2 HP per tick on both tools (20/40 -> 14/28 HP/s) and ammo
pool 150 -> 75 (regen unchanged). Enemy slow and block repair are untouched.

Same modes as double_brain_hero_healing.py: read-only by default, ``--apply``
writes the revision-checked card with a private backup and readback, and
``--from-file CATALOGUE.json`` audits offline.
"""

import base64
import copy
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

CAULK_ID = "gear_engineer_caulk_gun"
HEAL = (6, 4.2)
POOL = (150, 75)


def is_ally_heal(effect):
    targeting = effect.get("targeting") or {}
    return effect.get("type") == "heal" and targeting.get("affected_team") == "friendly" and \
        targeting.get("affected_units") == ["player"] and targeting.get("ignore_caster") is True


def migrate(card):
    changed = copy.deepcopy(card)
    for tool in changed["tools"]:
        heals = [e for e in tool["interval_effects"] if is_ally_heal(e)]
        assert len(heals) == 1, "Caulk Gun tool without a single ally heal"
        assert heals[0]["player_heal"] in HEAL, f"unexpected heal {heals[0]['player_heal']}"
        heals[0]["player_heal"] = HEAL[1]
    assert len(changed["ammo"]) == 1, "Caulk Gun without a single ammo pool"
    pool = changed["ammo"][0]["pool"]
    assert pool["pool_size"] in POOL, f"unexpected pool {pool['pool_size']}"
    pool["pool_size"] = POOL[1]
    return changed


def audit(card):
    tools = card["tools"]
    result = {
        "tony_hp_per_second": [round(next(e for e in t["interval_effects"] if is_ally_heal(e))["player_heal"]
                                     / t["interval"], 6) for t in tools],
        "ammo_pool": card["ammo"][0]["pool"],
    }
    assert result["tony_hp_per_second"] == [14, 28]
    assert result["ammo_pool"]["pool_size"] == POOL[1]
    return result


if "--from-file" in sys.argv:
    source = Path(sys.argv[sys.argv.index("--from-file") + 1])
    all_docs = {doc["_id"]: doc for doc in json.loads(source.read_text(encoding="utf-8"))}
    before = all_docs[CAULK_ID]
    after = migrate(before)
    print(json.dumps({"write": before != after, "after": audit(after)}, indent=2))
    sys.exit()

config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
config = {key.replace("_", "").lower(): value for key, value in config.items()}
base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
auth = "Basic " + base64.b64encode(
    (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
).decode()


def request(name, data=None):
    req = urllib.request.Request(
        base + "/" + urllib.parse.quote(name, safe=""),
        headers={"Authorization": auth, "Content-Type": "application/json"},
        data=None if data is None else json.dumps(data).encode(),
        method="GET" if data is None else "PUT",
    )
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)


before = request(CAULK_ID)
after = migrate(before)
changed = after != before
print(json.dumps({"write": changed, "revision": before["_rev"], "after": audit(after)}, indent=2), flush=True)
if "--apply" not in sys.argv or not changed:
    sys.exit()

backup = Path("/root/config-backups") / ("caulk-gun-nerf-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
request(CAULK_ID, after)  # Carries _rev; a concurrent edit fails.
saved = request(CAULK_ID)
assert {k: v for k, v in saved.items() if k != "_rev"} == {k: v for k, v in after.items() if k != "_rev"}, \
    "readback differs"
print(json.dumps({"backup": str(backup), "revision": saved["_rev"], "verified": audit(saved)}, indent=2), flush=True)
