"""Shorten the Genie lamp shield pickup from 10 s to 7 s.

effect_status_shielded_10 (applied only by unit_pickup_shielded_djinn_lamp, the
Genie lamp's shield pickup): duration 10 -> 7 seconds. The 37.5% damage reduction
(shield 0.375, set on 2026-10-17) and the pickup itself are unchanged. The card id
keeps its historical _10 suffix because the pickup references it by id.

Run on the production host. Default is a read-only plan; --apply saves a
private rollback copy, writes a revision-guarded card, then verifies readback.
Rollback: PUT the cards from <backup>/before.json with their current _rev.
"""

import base64
import copy
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

SHIELD = "effect_status_shielded_10"
PICKUP = "unit_pickup_shielded_djinn_lamp"
BEFORE_SECONDS, AFTER_SECONDS = 10, 7


def plan(shield, pickup):
    assert pickup["data"]["take_effect"]["constant"] == [SHIELD], "shield pickup changed"
    assert shield["effect"]["type"] == "buff" and shield["effect"]["buffs"] == {"shield": 0.375}, shield["effect"]
    assert shield["duration"] in (BEFORE_SECONDS, AFTER_SECONDS), shield["duration"]
    changed = copy.deepcopy(shield)
    changed["duration"] = AFTER_SECONDS
    return changed


if __name__ == "__main__":
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

    before = request(SHIELD)
    pickup = request(PICKUP)
    after = plan(before, pickup)
    print(json.dumps({"write": after != before, "revision": before["_rev"],
                      "duration_seconds": {"before": before["duration"], "after": after["duration"]},
                      "shield": after["effect"]["buffs"]["shield"]}, indent=2), flush=True)
    if "--apply" not in sys.argv or after == before:
        sys.exit()

    backup = Path("/root/config-backups") / ("genie-shield-7s-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps({SHIELD: before}, indent=2))
    request(SHIELD, after)  # Original _rev rejects concurrent edits.
    saved = request(SHIELD)
    assert {k: v for k, v in saved.items() if k != "_rev"} == {k: v for k, v in after.items() if k != "_rev"}, "readback differs"
    print(json.dumps({"backup": str(backup), "revision": saved["_rev"], "verified": True}, indent=2), flush=True)
