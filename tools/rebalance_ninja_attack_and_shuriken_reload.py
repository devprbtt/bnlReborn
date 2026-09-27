"""Slow Ninja's katana attack by 15% and add a three-star shuriken magazine.

Run on the production host. The default mode is read-only and prints the plan.
``--apply`` writes revision-checked documents, stores a private rollback copy,
and reads every changed card back. The catalogue watcher applies the change
without a server restart.

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

KATANA_IDS = (
    "gear_ninja_katana_and_teleport",
    "gear_ninja_katana_and_teleport_bloody_bleeding",
    "gear_ninja_katana_and_teleport_mystical_teleport",
)
SHURIKEN_IDS = (
    "gear_ninja_shuriken",
    "gear_ninja_shuriken_bloody_bleeding",
)
CARD_IDS = KATANA_IDS + SHURIKEN_IDS
OLD_KATANA_ATTACK_TIME = 0.65
NEW_KATANA_ATTACK_TIME = 0.7475
SHURIKEN_MAG_SIZE = 3
SHURIKEN_RELOAD_TIME = 1.0

assert math.isclose(NEW_KATANA_ATTACK_TIME, OLD_KATANA_ATTACK_TIME * 1.15)


def katana_attack(card):
    assert card["category"] == "gear" and card["prefab"] == "katana"
    tool = card["tools"][0]
    assert tool["type"] == "melee", f"unexpected katana primary tool on {card['_id']}"
    return tool


def validate_shuriken_shape(card):
    assert card["category"] == "gear" and card["prefab"] == "shuriken"
    assert len(card["ammo"]) == 1 and card["ammo"][0]["pool"]["pool_size"] == 20
    assert len(card["tools"]) == 2
    assert [tool["type"] for tool in card["tools"]] == ["shot", "shot"]
    assert [tool["ammo"]["rate"] for tool in card["tools"]] == [1, 3]


def migrate_katana(card):
    attack = katana_attack(card)
    current = attack["timing"]["attack_time"]
    assert math.isclose(current, OLD_KATANA_ATTACK_TIME) or math.isclose(current, NEW_KATANA_ATTACK_TIME), \
        f"unexpected katana attack time on {card['_id']}: {current}"
    if math.isclose(current, NEW_KATANA_ATTACK_TIME):
        return card
    changed = copy.deepcopy(card)
    katana_attack(changed)["timing"]["attack_time"] = NEW_KATANA_ATTACK_TIME
    return changed


def migrate_shuriken(card):
    validate_shuriken_shape(card)
    desired_reload = {"type": "full_clip", "reload_time": SHURIKEN_RELOAD_TIME}
    mag_size = card["ammo"][0].get("mag_size")
    reload_config = card.get("reload")
    flags = [
        (tool["ammo"]["auto_reload_on_empty_gun_fire"], tool["ammo"]["auto_reload_after_last_shot"])
        for tool in card["tools"]
    ]
    already_changed = mag_size == SHURIKEN_MAG_SIZE and reload_config == desired_reload and all(
        empty_fire and after_last for empty_fire, after_last in flags
    )
    if already_changed:
        return card
    assert mag_size is None, f"unexpected shuriken magazine on {card['_id']}: {mag_size}"
    assert reload_config is None, f"unexpected shuriken reload on {card['_id']}: {reload_config}"
    assert flags == [(False, False), (False, False)], f"unexpected auto-reload flags on {card['_id']}: {flags}"
    changed = copy.deepcopy(card)
    changed["ammo"][0]["mag_size"] = SHURIKEN_MAG_SIZE
    changed["reload"] = desired_reload
    for tool in changed["tools"]:
        tool["ammo"]["auto_reload_on_empty_gun_fire"] = True
        tool["ammo"]["auto_reload_after_last_shot"] = True
    return changed


def migrate(card):
    if card["_id"] in KATANA_IDS:
        return migrate_katana(card)
    if card["_id"] in SHURIKEN_IDS:
        return migrate_shuriken(card)
    raise AssertionError("unexpected card: " + card["_id"])


def audit(cards):
    result = {"katana": {}, "shuriken": {}}
    for card_id in KATANA_IDS:
        attack_time = katana_attack(cards[card_id])["timing"]["attack_time"]
        assert math.isclose(attack_time, NEW_KATANA_ATTACK_TIME)
        result["katana"][card_id] = {"attack_time": attack_time}
    for card_id in SHURIKEN_IDS:
        card = cards[card_id]
        validate_shuriken_shape(card)
        assert card["ammo"][0]["mag_size"] == SHURIKEN_MAG_SIZE
        assert card["reload"] == {"type": "full_clip", "reload_time": SHURIKEN_RELOAD_TIME}
        assert all(tool["ammo"]["auto_reload_on_empty_gun_fire"] for tool in card["tools"])
        assert all(tool["ammo"]["auto_reload_after_last_shot"] for tool in card["tools"])
        result["shuriken"][card_id] = {
            "mag_size": card["ammo"][0]["mag_size"],
            "reload": card["reload"],
            "primary_ammo_cost": card["tools"][0]["ammo"]["rate"],
            "alt_ammo_cost": card["tools"][1]["ammo"]["rate"],
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
    "ninja-attack-shuriken-reload-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
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
