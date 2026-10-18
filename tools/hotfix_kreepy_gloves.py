"""Kreepy Extendo Gloves nerf: weaker knockback (especially midair) and one less round per clip.

gear_kreepy_extendo_gloves (Kreepy and the generic test-dummy player):
- primary shot knockback force 4 -> 3, midair_force 6 -> 4
- magazine (ammo[0].mag_size) 6 -> 5; pool 66 and regen 3.3 unchanged
Damage, falloff, range and the melee secondary are unchanged.

Run on the production host. Default is a read-only plan; --apply saves a private rollback copy,
writes a revision-guarded card, then verifies readback. --offline <cards.json> tests the plan.
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

GLOVES = "gear_kreepy_extendo_gloves"
BEFORE = {"force": 4, "midair_force": 6, "mag_size": 6}
AFTER = {"force": 3, "midair_force": 4, "mag_size": 5}


def knockback(card):
    hits = [e for e in card["tools"][0]["hit_effect"]["instant"] if e["type"] == "knockback"]
    assert len(hits) == 1, "expected one primary knockback"
    return hits[0]


def values(card):
    kb = knockback(card)
    assert len(card["ammo"]) == 1, "expected one ammo pool"
    return {"force": kb["force"], "midair_force": kb["midair_force"], "mag_size": card["ammo"][0]["mag_size"]}


def plan(card):
    assert card["_id"] == GLOVES and card["tools"][0]["type"] == "shot" and card["tools"][1]["type"] == "melee"
    assert values(card) in (BEFORE, AFTER), values(card)
    kb = knockback(card)
    assert kb["effect_range"] == 2 and kb["linear_falloff"] is False, kb
    assert card["ammo"][0]["pool"] == {"pool_size": 66, "base_regen": 3.3}, card["ammo"][0]["pool"]
    changed = copy.deepcopy(card)
    kb = knockback(changed)
    kb["force"], kb["midair_force"] = AFTER["force"], AFTER["midair_force"]
    changed["ammo"][0]["mag_size"] = AFTER["mag_size"]
    return changed


def differences(a, b, path=""):
    if isinstance(a, dict) and isinstance(b, dict):
        out = []
        for k in set(a) | set(b):
            if k != "_rev":
                out += differences(a.get(k), b.get(k), f"{path}.{k}")
        return out
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        return [d for i, (x, y) in enumerate(zip(a, b)) for d in differences(x, y, f"{path}[{i}]")]
    return [] if a == b else [path]


EXPECTED = sorted([".tools[0].hit_effect.instant[1].force", ".tools[0].hit_effect.instant[1].midair_force",
                   ".ammo[0].mag_size"])

if __name__ == "__main__":
    if "--offline" in sys.argv:
        cards = {c["_id"]: c for c in json.loads(Path(sys.argv[sys.argv.index("--offline") + 1]).read_text())}
        card = cards[GLOVES]
        after = plan(card)
        assert sorted(differences(card, after)) == EXPECTED, differences(card, after)
        assert values(after) == AFTER and plan(after) == after, "not idempotent"
        bad = copy.deepcopy(card); bad["ammo"][0]["mag_size"] = 8
        try:
            plan(bad); raise SystemExit("unexpected input accepted")
        except AssertionError:
            pass
        print("offline plan OK: exactly force, midair_force and mag_size change; idempotent; rejects unexpected input")
        sys.exit()

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

    before = request(GLOVES)
    after = plan(before)
    print(json.dumps({"write": after != before, "revision": before["_rev"],
                      "before": values(before), "after": values(after)}, indent=2), flush=True)
    if "--apply" not in sys.argv or after == before:
        sys.exit()

    backup = Path("/root/config-backups") / ("kreepy-gloves-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps({GLOVES: before}, indent=2))
    request(GLOVES, after)  # Original _rev rejects concurrent edits.
    saved = request(GLOVES)
    assert sorted(differences(before, saved)) == EXPECTED and values(saved) == AFTER, "readback differs"
    print(json.dumps({"backup": str(backup), "revision": saved["_rev"], "verified": True}, indent=2), flush=True)
