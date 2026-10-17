"""Genie balance hotfix: Hand of Nur right-click -20% damage, healing orb -30% healing,
lamp shield pickup -25% protection.

- gear_djinn_spikes tools[1] (right-click, 5-spike spread): player damage 10 -> 8 per
  spike. Block (world) damage, primary fire, crit modifier, spread and ammo are unchanged.
- effect_hero_djinn_heavy_orb_heal_aura (used by both the normal and the Speed Ball
  healing orb): player heal 2 -> 1.4 per 0.2 s tick (10 -> 7 HP/s). Radius 3 m,
  interval, targeting and the regen icon are unchanged.
- effect_status_shielded_10 (applied only by unit_pickup_shielded_djinn_lamp, the Genie
  lamp's shield pickup): shield 1 -> 0.375. The server multiplies damage by
  max(1 - shield, 0.5), so 1 was already at the 50% cap; 0.375 is 37.5% less damage,
  a quarter less protection. Duration 10 s is unchanged.

Run on the production host. Default is a read-only plan; --apply saves a
private rollback copy, writes revision-guarded cards, then verifies readback.
Rollback: PUT the cards from <backup>/before.json with their current _rev.
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

SPIKES = "gear_djinn_spikes"
HEAL_AURA = "effect_hero_djinn_heavy_orb_heal_aura"
ORBS = ("unit_projectile_djinn_heavy_orb", "unit_projectile_djinn_heavy_orb_speed_ball")
SHIELD = "effect_status_shielded_10"
SHIELD_PICKUP = "unit_pickup_shielded_djinn_lamp"
IDS = (SPIKES, HEAL_AURA, SHIELD, SHIELD_PICKUP) + ORBS
WRITABLE = (SPIKES, HEAL_AURA, SHIELD)

SPIKE_DAMAGE_BEFORE, SPIKE_DAMAGE_AFTER = 10, 8
HEAL_BEFORE, HEAL_AFTER = 2, 1.4
SHIELD_BEFORE, SHIELD_AFTER = 1, 0.375


def right_click(card):
    tool = card["tools"][1]
    assert tool["type"] == "shot", card["_id"]
    assert tool["bullet"]["projectile_key"] == "projectile_djinn_spikes_secondary", card["_id"]
    assert tool["bullets"]["count"] == 5, card["_id"]
    return tool["hit_effect"]["damage"]


def aura_heal(card):
    effect = card["effect"]
    assert effect["type"] == "aura" and effect["interval"] == 0.2 and effect["outer_radius"] == 3, card["_id"]
    heals = [e for e in effect["interval_effects"] if e["type"] == "heal"]
    assert len(heals) == 1 and len(effect["interval_effects"]) == 1, card["_id"]
    return heals[0]


def shield_buffs(card):
    assert card["duration"] == 10 and card["effect"]["type"] == "buff", card["_id"]
    buffs = card["effect"]["buffs"]
    assert list(buffs) == ["shield"], card["_id"]
    return buffs


def plan(cards):
    assert cards[SHIELD_PICKUP]["data"]["take_effect"]["constant"] == [SHIELD], "shield pickup changed"
    assert shield_buffs(cards[SHIELD])["shield"] in (SHIELD_BEFORE, SHIELD_AFTER), cards[SHIELD]["effect"]
    for orb in ORBS:
        assert HEAL_AURA in json.dumps(cards[orb]), orb + " no longer uses the heal aura"
    primary = cards[SPIKES]["tools"][0]["hit_effect"]["damage"]
    assert primary["player_damage"] == 10, "primary fire changed unexpectedly"

    damage = right_click(cards[SPIKES])
    assert damage["player_damage"] in (SPIKE_DAMAGE_BEFORE, SPIKE_DAMAGE_AFTER), damage
    assert damage["world_damage"] == SPIKE_DAMAGE_BEFORE, damage
    heal = aura_heal(cards[HEAL_AURA])
    assert math.isclose(heal["player_heal"], HEAL_BEFORE) or math.isclose(heal["player_heal"], HEAL_AFTER), heal

    changed = copy.deepcopy(cards)
    right_click(changed[SPIKES])["player_damage"] = SPIKE_DAMAGE_AFTER
    aura_heal(changed[HEAL_AURA])["player_heal"] = HEAL_AFTER
    shield_buffs(changed[SHIELD])["shield"] = SHIELD_AFTER
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

    before = {card_id: request(card_id) for card_id in IDS}
    after = plan(before)
    assert all(after[card_id] == before[card_id] for card_id in IDS if card_id not in WRITABLE)
    changes = {card_id: after[card_id] for card_id in WRITABLE if after[card_id] != before[card_id]}
    print(json.dumps({
        "write": list(changes),
        "revisions": {card_id: before[card_id]["_rev"] for card_id in changes},
        "spikes_right_click_per_spike": {"before": right_click(before[SPIKES])["player_damage"],
                                         "after": right_click(after[SPIKES])["player_damage"]},
        "orb_heal_per_tick": {"before": aura_heal(before[HEAL_AURA])["player_heal"],
                              "after": aura_heal(after[HEAL_AURA])["player_heal"]},
        "lamp_shield": {"before": shield_buffs(before[SHIELD])["shield"],
                        "after": shield_buffs(after[SHIELD])["shield"]},
    }, indent=2), flush=True)
    if "--apply" not in sys.argv or not changes:
        sys.exit()

    backup = Path("/root/config-backups") / (
        "genie-spikes-orb-heal-shield-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
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
