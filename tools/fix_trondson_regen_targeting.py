"""Stop Trondson's Echo Pulse regen from healing enemies.

The pulse applied ``effect_hero_trondson_sonic_staff_regen`` from the
all-units bunch's constant list. That card is a ``self`` wrapper, and the
server evaluates a self wrapper's targeting relative to the unit carrying it,
so its "friendly" filter passed for every unit in range, enemies included.
Doc's Miracle Cure applies the same kind of wrapper through a nested bunch whose
own targeting is friendly players; the bunch is filtered relative to the
caster before anything is added. This tool gives both staff cards that shape.

Same modes as the other tools: read-only by default, ``--apply`` writes
revision-checked documents with a private backup and readback, and
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

STAFF_IDS = ("gear_trondson_sonic_staff", "gear_trondson_sonic_staff_echo_location")
REGEN = "effect_hero_trondson_sonic_staff_regen"
FRIENDLY_REGEN = {
    "type": "bunch",
    "interrupt": None,
    "targeting": {
        "affected_labels": None,
        "affected_units": ["player"],
        "affected_team": "friendly",
        "caster_owned_only": False,
        "ignore_caster": True,
    },
    "impact": None,
    "break_on_effect_fail": False,
    "instant": [],
    "constant": [REGEN],
}


def migrate(card):
    changed = copy.deepcopy(card)
    pulse = changed["tools"][1]["hit_effect"]
    assert pulse["type"] == "all_units_bunch", "unexpected Echo Pulse on " + card["_id"]
    if REGEN in pulse["constant"]:
        pulse["constant"].remove(REGEN)
    if FRIENDLY_REGEN not in pulse["instant"]:
        pulse["instant"].append(copy.deepcopy(FRIENDLY_REGEN))
    return changed


def audit(cards):
    result = {}
    for card_id in STAFF_IDS:
        pulse = cards[card_id]["tools"][1]["hit_effect"]
        assert REGEN not in pulse["constant"], card_id + " still applies the regen to every unit"
        assert pulse["instant"].count(FRIENDLY_REGEN) == 1, card_id + " lacks the friendly regen bunch"
        # The reveal marker was removed afterwards by rebalance_brain_hero_healing.py.
        assert pulse["constant"] in (["effect_hero_trondson_sonic_staff_marker"], []), card_id + " constant changed"
        result[card_id] = {"constant": pulse["constant"],
                           "instant": [e["type"] + ":" + (e.get("targeting") or {}).get("affected_team", "-")
                                       for e in pulse["instant"]]}
    return result


if "--from-file" in sys.argv:
    source = Path(sys.argv[sys.argv.index("--from-file") + 1])
    all_docs = {doc["_id"]: doc for doc in json.loads(source.read_text(encoding="utf-8"))}
    before = {card_id: all_docs[card_id] for card_id in STAFF_IDS}
    after = {card_id: migrate(card) for card_id, card in before.items()}
    print(json.dumps({"write": [c for c in STAFF_IDS if before[c] != after[c]], "after": audit(after)}, indent=2))
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


before = {card_id: request(card_id) for card_id in STAFF_IDS}
after = {card_id: migrate(card) for card_id, card in before.items()}
changes = {card_id: card for card_id, card in after.items() if card != before[card_id]}
print(json.dumps({"write": list(changes), "revisions": {c: before[c]["_rev"] for c in STAFF_IDS},
                  "after": audit(after)}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("trondson-regen-targeting-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for card_id in STAFF_IDS:
    if card_id in changes:
        request(card_id, changes[card_id])  # Carries _rev; concurrent edits fail.
saved = {card_id: request(card_id) for card_id in STAFF_IDS}
for card_id, planned in after.items():
    assert {k: v for k, v in saved[card_id].items() if k != "_rev"} == \
        {k: v for k, v in planned.items() if k != "_rev"}, "readback differs for " + card_id
print(json.dumps({"backup": str(backup), "revisions": {c: saved[c]["_rev"] for c in STAFF_IDS},
                  "verified": audit(saved)}, indent=2), flush=True)
