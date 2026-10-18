"""Give Sweet Science's gravity well a 1x1x1 size so area effects can reach it.

unit_device_boxer_gravity_well (and its _test_map copy) has "size": null. The server only puts
sized units into its unit octree, so every area effect skipped the well: Power Surge could not
disable it, and an enemy Jinx could not confuse it. The client already treats a missing size as
1x1x1, so nothing changes visually. Only the size field changes.

Run on the production host. Default is a read-only plan; --apply saves a private rollback copy,
writes revision-guarded cards, then verifies readback.
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

WELLS = ("unit_device_boxer_gravity_well", "unit_device_boxer_gravity_well_test_map")
SIZE = {"x": 1, "y": 1, "z": 1}


def plan(card):
    assert card["category"] == "unit" and card["data"]["type"] == "common", card["_id"]
    assert card["prefab"] == "Prefabs/Devices/GravityTrapActivated", card["prefab"]
    assert card.get("size") in (None, SIZE), card.get("size")
    assert card["enabled_effects"] == ["effect_device_gravity_well_aura"], card["enabled_effects"]
    changed = copy.deepcopy(card)
    changed["size"] = dict(SIZE)
    return changed


def only_size_changed(before, after):
    keys = (set(before) | set(after)) - {"_rev", "size"}
    return all(before.get(k) == after.get(k) for k in keys) and after["size"] == SIZE


if __name__ == "__main__":
    if "--offline" in sys.argv:
        cards = {c["_id"]: c for c in json.loads(Path(sys.argv[sys.argv.index("--offline") + 1]).read_text())}
        for well in WELLS:
            after = plan(cards[well])
            assert only_size_changed(cards[well], after)
            assert plan(after) == after, "not idempotent"
        try:
            plan(dict(cards[WELLS[0]], size={"x": 2, "y": 2, "z": 2}))
            raise SystemExit("unexpected size accepted")
        except AssertionError:
            pass
        print("offline plan OK: exactly one field (size) changes per well, idempotent, rejects unexpected input")
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

    before = {well: request(well) for well in WELLS}
    after = {well: plan(card) for well, card in before.items()}
    print(json.dumps({well: {"write": after[well] != before[well], "revision": before[well]["_rev"],
                             "size": {"before": before[well].get("size"), "after": after[well]["size"]}}
                      for well in WELLS}, indent=2), flush=True)
    if "--apply" not in sys.argv or all(after[w] == before[w] for w in WELLS):
        sys.exit()

    backup = Path("/root/config-backups") / ("gravity-well-size-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps(before, indent=2))
    for well in WELLS:
        if after[well] != before[well]:
            request(well, after[well])  # Original _rev rejects concurrent edits.
    for well in WELLS:
        saved = request(well)
        assert only_size_changed(before[well], saved), well + " readback differs"
    print(json.dumps({"backup": str(backup), "verified": True}, indent=2), flush=True)
