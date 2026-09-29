"""Move the Caulk Gun's enemy slow into a Tony hero perk, Sticky Caulk.

Base gear_engineer_caulk_gun loses the opponent bunch that applied
effect_hero_engineer_caulk_slow on both tools. The perk swaps in
gear_engineer_caulk_gun_sticky_caulk: the pre-change gun (slow kept) with both
tools' ammo rate doubled (1 -> 2, 3 -> 6). Perk and shop item follow the Super
Rivet Gun / Healing Caulk layout (hero slot, level 3, 11000 coins), listed in
global_logic.perks.heroes.unit_hero_engineer and the shop's perk category.

The released client (beta.43) does not have the new icon, so the perk and shop
item use the shipped Healing Caulk icon until ``--final-icon`` is run after
a client release carrying shop_perk_hero_engineer_sticky_caulk.

Read-only by default; ``--apply`` writes revision-checked documents with a
private backup and readback. Re-running plans nothing.
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

BASE_ID = "gear_engineer_caulk_gun"
GEAR_ID = "gear_engineer_caulk_gun_sticky_caulk"
PERK_ID = "perk_hero_engineer_sticky_caulk"
SHOP_ID = "shop_item_perk_hero_engineer_sticky_caulk"
SLOW_ID = "effect_hero_engineer_caulk_slow"
TEMPLATE_SHOP_ID = "shop_item_perk_hero_engineer_super_rivet_gun"
AFTER_SHOP_ITEM = "shop_item_perk_hero_engineer_world_builder"
PLACEHOLDER_ICON = "shop_perk_hero_engineer_healing_caulk"
FINAL_ICON = "shop_perk_hero_engineer_sticky_caulk"
ICON = FINAL_ICON if "--final-icon" in sys.argv else PLACEHOLDER_ICON

NAME = "Sticky Caulk"
PERK_TEXT = "Caulk Gun slows enemies / Caulk Gun uses ammo twice as fast"
TAIL = "Alt-Fire to repair faster at the cost of more ammo. Repairing your own turrets buffs their damage output."
BASE_TEXT = "Amazing tool that fixes blocks and heals allied Heroes. " + TAIL
GEAR_TEXT = "Amazing tool that fixes blocks, heals allied Heroes and slows your enemies. Uses ammo twice as fast. " + TAIL


def is_slow_bunch(effect):
    targeting = effect.get("targeting") or {}
    return effect.get("type") == "bunch" and effect.get("constant") == [SLOW_ID] and \
        targeting.get("affected_team") == "opponent"


def text(value):
    return {"text": value, "data": {}}


def plan(cards):
    """Return the desired document for every id this tool owns."""
    base = cards[BASE_ID]
    existing_gear = cards.get(GEAR_ID)
    if existing_gear is not None:
        source = copy.deepcopy(existing_gear)
    else:
        source = copy.deepcopy(base)
        assert all(sum(map(is_slow_bunch, t["interval_effects"])) == 1 for t in source["tools"]), \
            "base Caulk Gun no longer has exactly one slow per tool; build the variant by hand"
        for tool in source["tools"]:
            assert tool["ammo"]["rate"] in (1, 3)
            tool["ammo"]["rate"] *= 2
        source.pop("_rev", None)
        source.pop("hercules_metadata", None)
        source["_id"] = GEAR_ID
    source["description"] = text(GEAR_TEXT)

    new_base = copy.deepcopy(base)
    for tool in new_base["tools"]:
        tool["interval_effects"] = [e for e in tool["interval_effects"] if not is_slow_bunch(e)]
    new_base["description"] = text(BASE_TEXT)

    perk = copy.deepcopy(cards.get(PERK_ID) or {})
    perk.update({
        "_id": PERK_ID, "scope": "public", "icon": ICON, "name": text(NAME), "description": text(PERK_TEXT),
        "label": "sticky_caulk", "level": 3, "slot_type": "hero", "hero_dependency": "unit_hero_engineer",
        "perk_mods": [{"type": "gear", "replace_from": BASE_ID, "replace_to": GEAR_ID}], "category": "perk",
    })

    template = cards[TEMPLATE_SHOP_ID]
    shop = copy.deepcopy(cards.get(SHOP_ID) or {})
    shop.update({
        "_id": SHOP_ID, "scope": "public", "name": text(NAME), "description": text(PERK_TEXT),
        "short_description": copy.deepcopy(template["short_description"]),
        "image": ICON, "featured_image": ICON, "available_in_shop": True, "release_date": None,
        "is_new": False, "free_to_try": False, "items": [PERK_ID], "dependency": None,
        "duration_hours": None, "price_real": None, "price_virtual": template["price_virtual"],
        "promotion": None, "category": "shop_item",
    })

    global_logic = copy.deepcopy(cards["global_logic"])
    engineer = global_logic["perks"]["heroes"]["unit_hero_engineer"]
    if PERK_ID not in engineer:
        engineer.append(PERK_ID)

    shop_logic = copy.deepcopy(cards["shop_logic"])
    categories = [c for c in shop_logic["shop"]["categories"] if AFTER_SHOP_ITEM in c.get("items", [])]
    assert len(categories) == 1, "perk shop category not found"
    items = categories[0]["items"]
    if SHOP_ID not in items:
        items.insert(items.index(AFTER_SHOP_ITEM) + 1, SHOP_ID)

    # Write order: the variant and perk exist before anything points at them;
    # the base loses its slow last.
    return {GEAR_ID: source, PERK_ID: perk, SHOP_ID: shop, "global_logic": global_logic,
            "shop_logic": shop_logic, BASE_ID: new_base}


def audit(cards):
    base_tools = cards[BASE_ID]["tools"]
    gear_tools = cards[GEAR_ID]["tools"]
    result = {
        "base_slow_bunches": [sum(map(is_slow_bunch, t["interval_effects"])) for t in base_tools],
        "base_ammo_rates": [t["ammo"]["rate"] for t in base_tools],
        "variant_slow_bunches": [sum(map(is_slow_bunch, t["interval_effects"])) for t in gear_tools],
        "variant_ammo_rates": [t["ammo"]["rate"] for t in gear_tools],
        "variant_ally_heal": [[e["player_heal"] for e in t["interval_effects"] if e.get("type") == "heal"
                               and (e.get("targeting") or {}).get("affected_units") == ["player"]]
                              for t in gear_tools],
        "perk_icon": cards[PERK_ID]["icon"],
        "engineer_perks": cards["global_logic"]["perks"]["heroes"]["unit_hero_engineer"],
    }
    assert result["base_slow_bunches"] == [0, 0] and result["variant_slow_bunches"] == [1, 1]
    assert result["base_ammo_rates"] == [1, 3] and result["variant_ammo_rates"] == [2, 6]
    assert PERK_ID in result["engineer_perks"]
    # The variant must stay in step with the base gun's current heal and ammo pool.
    assert cards[GEAR_ID]["ammo"] == cards[BASE_ID]["ammo"], "variant ammo pool differs from base"
    return result


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
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if data is None and error.code == 404:
            return None
        raise


IDS = (BASE_ID, GEAR_ID, PERK_ID, SHOP_ID, TEMPLATE_SHOP_ID, "global_logic", "shop_logic")
before = {card_id: request(card_id) for card_id in IDS}
before = {k: v for k, v in before.items() if v is not None}
after = plan(before)
changes = {k: v for k, v in after.items() if v != before.get(k)}
print(json.dumps({"write": list(changes), "revisions": {k: v.get("_rev") for k, v in before.items()},
                  "after": audit({**before, **after})}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("sticky-caulk-perk-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for card_id, card in changes.items():
    request(card_id, card)  # Existing documents carry _rev; concurrent edits fail.
saved = {card_id: request(card_id) for card_id in after}
for card_id, planned in after.items():
    assert {k: v for k, v in saved[card_id].items() if k != "_rev"} == \
        {k: v for k, v in planned.items() if k != "_rev"}, "readback differs for " + card_id
print(json.dumps({"backup": str(backup), "revisions": {k: v["_rev"] for k, v in saved.items()},
                  "verified": audit({**before, **saved})}, indent=2), flush=True)
