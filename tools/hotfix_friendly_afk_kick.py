"""Give idle players two more minutes before the Friendly AFK kick.

game_mode_friendly anti_afk.afk_punish_seconds 180 -> 300. Only Friendly kicks
(kick_from_match true); the 120 s warning, punish_once, the popup tag, and the
Ranked/Graveyard settings are unchanged.

Run on the production host. Default is a read-only plan; --apply saves a
private rollback copy, writes a revision-guarded card, then verifies readback.
Rollback: PUT the card from <backup>/before.json with its current _rev.
"""

import base64
import copy
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

CARD = "game_mode_friendly"
BEFORE_SECONDS, AFTER_SECONDS = 180, 300


def anti_afk(card):
    afk = card["anti_afk"]
    assert afk["kick_from_match"] is True and afk["afk_warning_seconds"] == 120, afk
    return afk


def plan(card):
    assert anti_afk(card)["afk_punish_seconds"] in (BEFORE_SECONDS, AFTER_SECONDS), card["anti_afk"]
    changed = copy.deepcopy(card)
    anti_afk(changed)["afk_punish_seconds"] = AFTER_SECONDS
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

    before = request(CARD)
    after = plan(before)
    print(json.dumps({"write": after != before, "revision": before["_rev"],
                      "afk_punish_seconds": {"before": anti_afk(before)["afk_punish_seconds"],
                                             "after": anti_afk(after)["afk_punish_seconds"]}}, indent=2), flush=True)
    if "--apply" not in sys.argv or after == before:
        sys.exit()

    backup = Path("/root/config-backups") / ("friendly-afk-kick-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps({CARD: before}, indent=2))
    request(CARD, after)  # Original _rev rejects concurrent edits.
    saved = request(CARD)
    assert {k: v for k, v in saved.items() if k != "_rev"} == {k: v for k, v in after.items() if k != "_rev"}, "readback differs"
    print(json.dumps({"backup": str(backup), "revision": saved["_rev"], "verified": True}, indent=2), flush=True)
