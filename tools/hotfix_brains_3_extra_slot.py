"""Brains III: replace Quick Builder (30% faster building) with Extra Block (a 7th device slot).

perk_class_brains_3:
- perk_mods: drops effect_class_brains_3_build_speed (the effect card itself is left in place)
- name, description, description_2 and label describe the extra block slot
The slot itself is granted by the server (ClassPerkCatalogue.DeviceSlots) and shown by clients that know it.

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

PERK = "perk_class_brains_3"
OLD_MODS = [{"type": "effect", "constant": ["effect_class_brains_3_build_speed"]}]
TEXT = {
    "name": "Extra Block",
    "description": "Unlocks a 7th block slot in your loadout.\nThe extra slot is available only while this class perk is selected.",
    "description_2": "Unlocks a 7th block slot in your loadout.",
}
LABEL = "+1 block slot"


def plan(card):
    assert card["_id"] == PERK and card["slot_type"] == "class" and card["class_perk"] == {}, card
    assert card["perk_mods"] in (OLD_MODS, []), card["perk_mods"]
    changed = copy.deepcopy(card)
    changed["perk_mods"] = []
    for field, text in TEXT.items():
        changed[field] = {"text": text, "data": {}}
    changed["label"] = LABEL
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


EXPECTED = sorted([".perk_mods", ".name.text", ".description.text", ".description_2.text", ".label"])

if __name__ == "__main__":
    if "--offline" in sys.argv:
        cards = {c["_id"]: c for c in json.loads(Path(sys.argv[sys.argv.index("--offline") + 1]).read_text(encoding="utf-8"))}
        card = cards[PERK]
        after = plan(card)
        assert sorted(differences(card, after)) == EXPECTED, differences(card, after)
        assert plan(after) == after, "not idempotent"
        bad = copy.deepcopy(card); bad["perk_mods"] = [{"type": "effect", "constant": ["effect_other"]}]
        try:
            plan(bad); raise SystemExit("unexpected input accepted")
        except AssertionError:
            pass
        print("offline plan OK: exactly perk_mods and the four texts change; idempotent; rejects unexpected input")
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

    before = request(PERK)
    after = plan(before)
    print(json.dumps({"write": after != before, "revision": before["_rev"],
                      "changes": sorted(differences(before, after))}, indent=2), flush=True)
    if "--apply" not in sys.argv or after == before:
        sys.exit()

    backup = Path("/root/config-backups") / ("brains-3-extra-slot-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps({PERK: before}, indent=2))
    request(PERK, after)  # Original _rev rejects concurrent edits.
    saved = request(PERK)
    assert sorted(differences(before, saved)) == EXPECTED and plan(saved) == saved, "readback differs"
    print(json.dumps({"backup": str(backup), "revision": saved["_rev"], "verified": True}, indent=2), flush=True)
