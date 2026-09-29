"""Replace Fly Away Bad Guy's damage penalty with a 15% reload penalty.

Run on the production host. Default is a read-only plan; --apply saves a
private rollback copy, writes revision-guarded cards, then verifies readback.
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

BASE_GEAR = "gear_astro_saucer_launcher"
PERK_GEAR = "gear_astro_saucer_launcher_fly_away_bad_guy"
BASE_PROJECTILE = "unit_projectile_astro_saucer"
PERK_PROJECTILE = "unit_projectile_astro_saucer_fly_away_bad_guy"
PERK = "perk_hero_astro_fly_away_bad_guy"
IDS = (BASE_GEAR, PERK_GEAR, BASE_PROJECTILE, PERK_PROJECTILE, PERK)
DESCRIPTION = "Saucer Launcher knockback is 70% more powerful / Saucer Launcher reload takes 15% longer"


def player_damage(card):
    effects = card["data"]["death_effect"]["instant"]
    splash = [effect for effect in effects if effect["type"] == "splash_damage"]
    assert len(splash) == 1, card["_id"]
    return splash[0]["damage"]


def knockback(card):
    effects = card["data"]["death_effect"]["instant"]
    matching = [effect for effect in effects if effect["type"] == "knockback"]
    assert len(matching) == 1, card["_id"]
    return matching[0]


def plan(cards):
    base_time = cards[BASE_GEAR]["reload"]["reload_time"]
    assert cards[BASE_GEAR]["reload"]["type"] == "full_clip"
    assert base_time == 2
    assert cards[PERK_GEAR]["reload"]["type"] == "full_clip"
    old_time = cards[PERK_GEAR]["reload"]["reload_time"]
    assert math.isclose(old_time, base_time) or math.isclose(old_time, base_time * 1.15)
    assert cards[PERK_GEAR]["tools"][0]["bullet"]["unit_projectile_key"] == PERK_PROJECTILE
    assert cards[PERK]["perk_mods"][0]["replace_to"] == PERK_GEAR

    base_damage = player_damage(cards[BASE_PROJECTILE])["player_damage"]
    old_damage = player_damage(cards[PERK_PROJECTILE])["player_damage"]
    assert base_damage == 24
    assert math.isclose(old_damage, 15.6) or math.isclose(old_damage, base_damage)
    assert knockback(cards[PERK_PROJECTILE])["force"] == 8.5
    assert knockback(cards[PERK_PROJECTILE])["midair_force"] == 8.5

    changed = copy.deepcopy(cards)
    changed[PERK_GEAR]["reload"]["reload_time"] = base_time * 1.15
    player_damage(changed[PERK_PROJECTILE])["player_damage"] = base_damage
    changed[PERK]["description"]["text"] = DESCRIPTION
    # Existing translations describe an obsolete ammo penalty; omit them so
    # localized clients display the current English text instead.
    changed[PERK]["description"]["data"] = {}
    return changed


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


before = {card_id: request(card_id) for card_id in IDS}
after = plan(before)
changes = {card_id: after[card_id] for card_id in IDS if after[card_id] != before[card_id]}
print(json.dumps({
    "write": list(changes),
    "revisions": {card_id: before[card_id]["_rev"] for card_id in changes},
    "player_damage": {"base": player_damage(after[BASE_PROJECTILE])["player_damage"],
                      "perk": player_damage(after[PERK_PROJECTILE])["player_damage"]},
    "reload_seconds": {"base": after[BASE_GEAR]["reload"]["reload_time"],
                       "perk": after[PERK_GEAR]["reload"]["reload_time"]},
    "description": after[PERK]["description"]["text"],
}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / (
    "fly-away-bad-guy-reload-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for card_id, card in changes.items():
    request(card_id, card)  # Original _rev rejects concurrent edits.
saved = {card_id: request(card_id) for card_id in IDS}
for card_id in IDS:
    assert {key: value for key, value in saved[card_id].items() if key != "_rev"} == {
        key: value for key, value in after[card_id].items() if key != "_rev"
    }, "readback differs for " + card_id
print(json.dumps({"backup": str(backup),
                  "revisions": {card_id: saved[card_id]["_rev"] for card_id in changes},
                  "verified": True}, indent=2), flush=True)
