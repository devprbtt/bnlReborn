"""Reduce Astarella's saucer magazine to three and raise ammo regen by 30%.

Run on the production host. The default mode is read-only and prints the plan.
``--apply`` writes revision-checked documents, stores a private rollback copy,
and reads both cards back. The catalogue watcher applies the change without a
server restart.

For an offline audit, pass ``--from-file CATALOGUE.json``. This accepts the
normal catalogue array and never connects to CouchDB or writes anything.
"""

import base64
import copy
import json
import math
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

CARD_IDS = (
    "gear_astro_saucer_launcher",
    "gear_astro_saucer_launcher_fly_away_bad_guy",
)
OLD_MAG_SIZE = 4
NEW_MAG_SIZE = 3
OLD_BASE_REGEN = 0.9
NEW_BASE_REGEN = 1.17
REGEN_INCREASE = 0.30

assert math.isclose(NEW_BASE_REGEN, OLD_BASE_REGEN * (1 + REGEN_INCREASE))


def ammo_config(card):
    assert card["category"] == "gear" and card["prefab"] == "saucer_launcher"
    assert len(card["ammo"]) == 1
    ammo = card["ammo"][0]
    assert ammo["pool"]["pool_size"] == 18
    return ammo


def migrate(card):
    ammo = ammo_config(card)
    mag_size = ammo["mag_size"]
    regen = ammo["pool"]["base_regen"]
    assert mag_size in (OLD_MAG_SIZE, NEW_MAG_SIZE), \
        f"unexpected magazine size on {card['_id']}: {mag_size}"
    assert math.isclose(regen, OLD_BASE_REGEN) or math.isclose(regen, NEW_BASE_REGEN), \
        f"unexpected ammo regen on {card['_id']}: {regen}"
    if mag_size == NEW_MAG_SIZE and math.isclose(regen, NEW_BASE_REGEN):
        return card
    changed = copy.deepcopy(card)
    changed_ammo = ammo_config(changed)
    changed_ammo["mag_size"] = NEW_MAG_SIZE
    changed_ammo["pool"]["base_regen"] = NEW_BASE_REGEN
    return changed


def audit(cards):
    result = {}
    for card_id in CARD_IDS:
        ammo = ammo_config(cards[card_id])
        assert ammo["mag_size"] == NEW_MAG_SIZE
        assert math.isclose(ammo["pool"]["base_regen"], NEW_BASE_REGEN)
        result[card_id] = {
            "mag_size": ammo["mag_size"],
            "pool_size": ammo["pool"]["pool_size"],
            "base_regen": ammo["pool"]["base_regen"],
        }
    return result


if "--from-file" in sys.argv:
    source = Path(sys.argv[sys.argv.index("--from-file") + 1])
    all_docs = {doc["_id"]: doc for doc in json.loads(source.read_text(encoding="utf-8"))}
    before = {card_id: all_docs[card_id] for card_id in CARD_IDS}
    after = {card_id: migrate(card) for card_id, card in before.items()}
    print(json.dumps({"write": [card_id for card_id in CARD_IDS if before[card_id] != after[card_id]],
                      "after": audit(after)}, indent=2))
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


before = {card_id: request(card_id) for card_id in CARD_IDS}
after = {card_id: migrate(card) for card_id, card in before.items()}
changes = {card_id: card for card_id, card in after.items() if card != before[card_id]}
print(json.dumps({"write": list(changes), "revisions": {card_id: before[card_id]["_rev"] for card_id in CARD_IDS},
                  "after": audit(after)}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / (
    "astarella-saucer-ammo-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for card_id in CARD_IDS:
    if card_id in changes:
        request(card_id, changes[card_id])  # Carries _rev; concurrent edits fail.
saved = {card_id: request(card_id) for card_id in CARD_IDS}
for card_id, planned in after.items():
    assert {key: value for key, value in saved[card_id].items() if key != "_rev"} == {
        key: value for key, value in planned.items() if key != "_rev"
    }, "readback differs for " + card_id
print(json.dumps({"backup": str(backup),
                  "revisions": {card_id: saved[card_id]["_rev"] for card_id in CARD_IDS},
                  "verified": audit(saved)}, indent=2), flush=True)
