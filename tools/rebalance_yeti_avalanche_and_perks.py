"""Turn Yeti's Avalanche into an area slow and remove the Facewash and Yuri 'n Ice hero perks.

Avalanche keeps its marker throw and 30-second cooldown. Instead of spawning the snow-block
shower, the marker's landing applies a new 2-second 30% slow to every enemy player within
5 blocks and plays the existing Snow Thrower splash impact. The Facewash and Yuri 'n Ice
perks are unlisted from global_logic and shop_logic, then their perk, shop item and
perk-exclusive gear/effect/ability/unit cards are deleted. Saved loadouts that still hold a
deleted perk are stripped by PlayerDataSanitizer at the player's next login.

Run on the production host. The default mode is read-only and prints the plan. ``--apply``
creates and writes cards with their current revisions as concurrency guards, deletes with
the current revision, saves every original card under ``/root/config-backups`` and reads
the result back. A re-run after a successful apply is a no-op. The catalogue watcher loads
the change without a server restart.

For an offline audit, pass ``--from-file CATALOGUE.json``. This accepts the normal
catalogue array and never connects to CouchDB or writes anything.
"""

import base64
import copy
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

HERO = "unit_hero_abe"
ABILITY = "ability_abe_avalanche"
OLD_SHOWER_UNIT = "unit_dummy_abe_avalanche"
SLOW_EFFECT = "effect_hero_abe_avalanche_slow"
SLOW_TEMPLATE = "effect_hero_abe_snow_thrower_slow"
LANDING_IMPACT = "impact_abe_snow_thrower_splash"
SLOW_RADIUS = 5
SLOW_DURATION = 2
SLOW = 0.3
REMOVED_PERKS = ("perk_hero_abe_facewash", "perk_hero_abe_yuri_n_ice")
REMOVED_SHOP_ITEMS = ("shop_item_perk_hero_abe_facewash", "shop_item_perk_hero_abe_yuri_n_ice")
# Referrers before referents, so no step leaves a live card pointing at a deleted one.
DELETED = REMOVED_SHOP_ITEMS + REMOVED_PERKS + (
    "gear_abe_snow_thrower_facewash",
    "effect_hero_abe_snow_thrower_slow_face_wash",
    "ability_abe_avalanche_yuri_n_ice",
    "unit_dummy_abe_avalanche_yuri_n_ice",
)
UPDATED = ("global_logic", "shop_logic", HERO, ABILITY)
READ = UPDATED + DELETED + (SLOW_EFFECT, SLOW_TEMPLATE, LANDING_IMPACT, "perk_hero_abe_chill_dude")

ABILITY_TEXT = ("Yury throws a marker that slows enemies within 5 blocks of where it lands "
                "by 30% for 2 seconds.")
TIP_OLD_PREFIX = "Avalanche allows Yury to quickly cover a large area with Snow Blocks"
TIP_TEXT = ("Avalanche slows every enemy near the marker's landing spot. Throw it where the "
            "enemy team is grouped up or about to push.")

HIT_EFFECT = {
    "type": "all_units_bunch",
    "interrupt": None,
    "targeting": {
        "affected_labels": None,
        "affected_units": ["player"],
        "affected_team": "opponent",
        "caster_owned_only": False,
        "ignore_caster": True,
    },
    "impact": LANDING_IMPACT,
    "range": SLOW_RADIUS,
    "break_on_effect_fail": False,
    "instant": [],
    "constant": [SLOW_EFFECT],
}
OLD_HIT_EFFECT = {
    "type": "bunch",
    "interrupt": None,
    "targeting": None,
    "impact": None,
    "break_on_effect_fail": False,
    "instant": [{
        "type": "unit_spawn",
        "interrupt": None,
        "targeting": None,
        "impact": None,
        "unit_key": OLD_SHOWER_UNIT,
    }],
    "constant": [],
}
# The Snow Thrower slow scaled from 40% to 30%, including its dash penalty (0.2 -> 0.15).
SLOW_BUFFS = {
    "run_speed": -SLOW,
    "sprint_speed": -SLOW,
    "jump_height": -SLOW,
    "dash_time": 0.15,
    "dash_distance": -0.15,
    "swim_speed": -SLOW,
}


def text(value):
    return {"text": value, "data": {}}


def slow_effect(template):
    assert template["effect"]["type"] == "buff" and template["duration"] == 2, "unexpected Snow Thrower slow"
    card = {key: copy.deepcopy(value) for key, value in template.items()
            if key not in ("_id", "_rev", "hercules_metadata")}
    card["_id"] = SLOW_EFFECT
    card["duration"] = SLOW_DURATION
    card["effect"]["buffs"] = dict(SLOW_BUFFS)
    return card


def without_rev(card):
    return {key: value for key, value in card.items() if key not in ("_rev", "hercules_metadata")}


def migrate_global_logic(card):
    changed = copy.deepcopy(card)
    perks = changed["perks"]["heroes"][HERO]
    changed["perks"]["heroes"][HERO] = [perk for perk in perks if perk not in REMOVED_PERKS]
    assert changed["perks"]["heroes"][HERO] == ["perk_hero_abe_chill_dude"], perks
    tips = [tip for tip in changed["tips_logic"]["specific_hero_tips"][HERO]
            if tip.get("image") == "activeability_icon_abe"]
    assert len(tips) == 1, "expected one Avalanche loading tip"
    current = tips[0]["tip_text"]["text"]
    assert current.startswith(TIP_OLD_PREFIX) or current == TIP_TEXT, current
    tips[0]["tip_text"] = text(TIP_TEXT)
    return changed


def migrate_shop_logic(card):
    changed = copy.deepcopy(card)
    for category in changed["shop"]["categories"]:
        category["items"] = [item for item in category.get("items") or [] if item not in REMOVED_SHOP_ITEMS]
    return changed


def migrate_hero(card):
    changed = copy.deepcopy(card)
    ability = changed["data"]["gui_info"]["active_ability"]
    assert ability["name"]["text"] == "Avalanche"
    assert changed["data"]["active_ability_key"] == ABILITY
    ability["description"] = text(ABILITY_TEXT)
    return changed


def migrate_ability(card):
    assert card["charges"] == {"max_charges": 1, "charge_cooldown": 30}, card["charges"]
    assert card["behavior"]["application"]["projectile_key"] == "projectile_abe_avalanche_marker"
    current = card["behavior"]["hit_effect"]
    assert current in (OLD_HIT_EFFECT, HIT_EFFECT), f"unexpected Avalanche hit effect: {current}"
    changed = copy.deepcopy(card)
    changed["behavior"]["hit_effect"] = copy.deepcopy(HIT_EFFECT)
    return changed


def references(cards, key):
    needle = json.dumps(key)
    return sorted(card_id for card_id, card in cards.items() if card_id != key and needle in json.dumps(card))


def plan(cards):
    """Return (writes, deletes, after) for a {card_id: card} catalogue view."""
    for key in (SLOW_TEMPLATE, LANDING_IMPACT, "perk_hero_abe_chill_dude"):
        assert key in cards, "missing " + key
    migrated = {
        "global_logic": migrate_global_logic(cards["global_logic"]),
        "shop_logic": migrate_shop_logic(cards["shop_logic"]),
        HERO: migrate_hero(cards[HERO]),
        ABILITY: migrate_ability(cards[ABILITY]),
    }
    desired_effect = slow_effect(cards[SLOW_TEMPLATE])
    existing_effect = cards.get(SLOW_EFFECT)
    if existing_effect is not None:
        assert without_rev(existing_effect) == desired_effect, "a different " + SLOW_EFFECT + " already exists"
    writes = {card_id: card for card_id, card in migrated.items() if card != cards[card_id]}
    if existing_effect is None:
        writes = {SLOW_EFFECT: desired_effect, **writes}
    deletes = [card_id for card_id in DELETED if card_id in cards]

    after = {card_id: card for card_id, card in cards.items() if card_id not in DELETED}
    after.update(migrated)
    after[SLOW_EFFECT] = existing_effect or desired_effect
    for card_id in DELETED:
        dangling = references(after, card_id)
        assert not dangling, f"{card_id} still referenced by {dangling}"
    return writes, deletes, after


def audit(cards):
    ability = cards[ABILITY]["behavior"]["hit_effect"]
    effect = cards[SLOW_EFFECT]
    return {
        "yeti_hero_perks": cards["global_logic"]["perks"]["heroes"][HERO],
        "avalanche_cooldown": cards[ABILITY]["charges"]["charge_cooldown"],
        "avalanche_hit_effect": {key: ability[key] for key in ("type", "range", "impact", "constant")},
        "avalanche_targeting": ability["targeting"],
        "slow": {"duration": effect["duration"], "buffs": effect["effect"]["buffs"]},
        "ability_text": cards[HERO]["data"]["gui_info"]["active_ability"]["description"]["text"],
        "old_shower_unit_referenced_by": references(cards, OLD_SHOWER_UNIT),
        "removed_present": [card_id for card_id in DELETED if card_id in cards],
    }


if "--from-file" in sys.argv:
    source = Path(sys.argv[sys.argv.index("--from-file") + 1])
    catalogue = {card["_id"]: card for card in json.loads(source.read_text(encoding="utf-8"))}
    writes, deletes, after = plan(catalogue)
    print(json.dumps({"write": list(writes), "delete": deletes, "after": audit(after)}, indent=2,
                     ensure_ascii=False))
    sys.exit()

config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
config = {key.replace("_", "").lower(): value for key, value in config.items()}
base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
auth = "Basic " + base64.b64encode(
    (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
).decode()


def request(name, data=None, method=None, query=""):
    req = urllib.request.Request(
        base + "/" + urllib.parse.quote(name, safe="") + query,
        headers={"Authorization": auth, "Content-Type": "application/json"},
        data=None if data is None else json.dumps(data).encode(),
        method=method or ("GET" if data is None else "PUT"),
    )
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)


def fetch(name):
    try:
        return request(name)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


# The whole catalogue, so the dangling-reference check covers every card, not just these.
rows = request("_all_docs", query="?include_docs=true")["rows"]
catalogue = {row["id"]: row["doc"] for row in rows if not row["id"].startswith("_design")}
before = {card_id: catalogue[card_id] for card_id in READ if card_id in catalogue}
writes, deletes, after = plan(catalogue)
print(json.dumps({"write": list(writes), "delete": deletes,
                  "revisions": {card_id: before[card_id]["_rev"] for card_id in list(writes) + deletes
                                if card_id in before},
                  "after": audit(after)}, indent=2, ensure_ascii=False), flush=True)
if "--apply" not in sys.argv or not (writes or deletes):
    sys.exit()

backup = Path("/root/config-backups") / (
    "yeti-avalanche-slow-perk-removal-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2, ensure_ascii=False))
# The new effect exists before the ability references it; unlisting precedes deletion.
for card_id, card in writes.items():
    request(card_id, card)  # Carries _rev where the card exists; concurrent edits fail.
for card_id in deletes:
    request(card_id, method="DELETE", query="?rev=" + urllib.parse.quote(before[card_id]["_rev"]))

saved = {card_id: card for card_id in READ if (card := fetch(card_id)) is not None}
for card_id in deletes:
    assert card_id not in saved, card_id + " still exists"
for card_id in writes:
    assert without_rev(saved[card_id]) == without_rev(after[card_id]), "readback differs for " + card_id
verified = dict(after)
verified.update(saved)
print(json.dumps({"backup": str(backup),
                  "revisions": {card_id: saved[card_id]["_rev"] for card_id in writes},
                  "deleted": deletes,
                  "verified": audit(verified)}, indent=2, ensure_ascii=False), flush=True)
