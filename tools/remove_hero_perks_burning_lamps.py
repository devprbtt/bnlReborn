"""Remove three hero perks and give Trondson's Kobold Lamps the Fire Lamps burn by default.

Removed (unlisted from global_logic and shop_logic, then the perk, its shop item and its
perk-exclusive cards are deleted):
- Doc: Beautiful Bubbles (Globe Gun damage +15% / Chem Grenade ammo -3)
- Cogwheel: Heavier Metal (health +20% / movement -15%)
- Trondson: Fire Lamps (lamps burn for 7.5 s / lamp pool -4)

Kobold Lamps: both tools inherit the current live Fire Lamps burn (including its duration),
under a new id. The pool (12) and reload flags stay those of the base lamps; only the burn
moves over. The English description gains one sentence and its stale translations are dropped.
The Splashing Damage shop item, which was named "Beautiful Bubbles", takes its perk's name.
Saved loadouts holding a deleted perk are stripped by PlayerDataSanitizer at the next login.

Run on the production host. The default mode is read-only and prints the plan. ``--apply``
writes with current revisions as concurrency guards, deletes with the current revision, saves
every original card under ``/root/config-backups`` and reads the result back. A re-run after a
successful apply is a no-op. The catalogue watcher loads the change without a restart.

For an offline audit, pass ``--from-file CATALOGUE.json``; it never connects or writes.
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

REMOVED = {
    "unit_hero_doc_eliza": "perk_hero_eliza_beautiful_bubbles",
    "unit_hero_cogwheel": "perk_hero_cogwheel_heavier_metal",
    "unit_hero_trondson": "perk_hero_trondson_fire_lamps",
}
REMAINING = {
    "unit_hero_doc_eliza": ["perk_hero_eliza_splashing_damage", "perk_hero_eliza_alive"],
    "unit_hero_cogwheel": ["perk_hero_cogwheel_last_bot_shooting", "perk_hero_cogwheel_pusher",
                           "perk_hero_cogwheel_grounded"],
    "unit_hero_trondson": ["perk_hero_trondson_echo_location", "perk_hero_trondson_singin_in_the_mine"],
}
REMOVED_SHOP_ITEMS = tuple("shop_item_" + perk for perk in REMOVED.values())
LAMPS = "gear_trondson_kobold_lamps"
FIRE_LAMPS = "gear_trondson_kobold_lamps_fire_lamps"
OLD_BURN = "effect_hero_trondson_kobold_lamp_burning_fire_lamps"
BURN = "effect_hero_trondson_kobold_lamp_burning"
SPLASHING_PERK = "perk_hero_eliza_splashing_damage"
SPLASHING_ITEM = "shop_item_perk_hero_eliza_splashing_damage"
# Referrers before referents, so no step leaves a live card pointing at a deleted one.
DELETED = REMOVED_SHOP_ITEMS + tuple(REMOVED.values()) + (
    "gear_doc_eliza_bubble_gun_beautiful_bubbles",
    "gear_doc_eliza_chem_grenade_beautiful_bubbles",
    "effect_perk_heavier_metal_pos",
    "effect_perk_heavy_metal_neg",
    FIRE_LAMPS,
    OLD_BURN,
)
UPDATED = ("global_logic", "shop_logic", LAMPS, SPLASHING_ITEM)
BRICKED_UP = "gear_trondson_kobold_lamps_bricked_up"
READ = UPDATED + DELETED + (BURN, SPLASHING_PERK, BRICKED_UP)

LAMPS_TEXT_BASE = ("Enchanted lamps fueled by earth spirits. While originally designed as an excavation tool "
                   "they also make effective weapons. <BINDING_CAST1> throws a lamp that explodes on landing, "
                   "<BINDING_CAST2> converts all Blocks around the point it lands into stone Blocks.")
LAMPS_TEXT = LAMPS_TEXT_BASE + " Enemies hit burn for 7.5 seconds."


def text(value):
    return {"text": value, "data": {}}


def without_rev(card):
    return {key: value for key, value in card.items() if key not in ("_rev", "hercules_metadata")}


def burn_lists(gear):
    """The two unit_const_effects lists the Fire Lamps variant filled: primary and secondary hit."""
    tools = gear["tools"]
    assert len(tools) == 2, "expected two lamp tools"
    primary = tools[0]["hit_effect"]
    secondary = tools[1]["hit_effect"]["instant"][0]  # then the stone-conversion effect
    assert primary["type"] == "splash_damage" and secondary["type"] == "splash_damage", "unexpected lamp hit effects"
    return [primary, secondary]


def burn_effect(old):
    assert old["effect"] in ({"type": "buff", "targeting": None, "buffs": {"burning": amount}} for amount in (3, 5)), old["effect"]
    assert old["duration"] in (3, 7.5) and old["positive"] is False, old
    card = {key: copy.deepcopy(value) for key, value in old.items() if key not in ("_id", "_rev", "hercules_metadata")}
    card["_id"] = BURN
    return card


def migrate_global_logic(card):
    changed = copy.deepcopy(card)
    heroes = changed["perks"]["heroes"]
    for hero, perk in REMOVED.items():
        heroes[hero] = [p for p in heroes[hero] if p != perk]
        assert set(REMAINING[hero]).issubset(heroes[hero]), (hero, heroes[hero])
        # Preserve newer live perks (Upgraded Gears / Bricked Up).
    return changed


def migrate_shop_logic(card):
    changed = copy.deepcopy(card)
    for category in changed["shop"]["categories"]:
        category["items"] = [item for item in category.get("items") or [] if item not in REMOVED_SHOP_ITEMS]
    return changed


def migrate_lamps(card, fire_lamps, duration=7.5):
    lamp_text = LAMPS_TEXT_BASE + f" Enemies hit burn for {duration:g} seconds."
    changed = copy.deepcopy(card)
    for hit in burn_lists(changed):
        assert hit["unit_const_effects"] in ([], [BURN], [OLD_BURN]), hit["unit_const_effects"]
        hit["unit_const_effects"] = [BURN]
    assert changed["ammo"][0]["pool"]["pool_size"] == 12, "lamp pool changed"
    current = changed["description"]["text"]
    assert current in (LAMPS_TEXT_BASE, LAMPS_TEXT, lamp_text), current
    if current != lamp_text:
        changed["description"] = text(lamp_text)
    if fire_lamps is not None:
        # The burn is exactly the live Fire Lamps hit, renamed.
        expected = [[BURN if e == OLD_BURN else e for e in hit["unit_const_effects"]] for hit in burn_lists(fire_lamps)]
        assert [hit["unit_const_effects"] for hit in burn_lists(changed)] == expected, expected
    return changed


def migrate_splashing_item(card, perk):
    changed = copy.deepcopy(card)
    assert changed["items"] == [SPLASHING_PERK], changed["items"]
    changed["name"] = copy.deepcopy(perk["name"])
    return changed


def references(cards, key):
    needle = json.dumps(key)
    return sorted(card_id for card_id, card in cards.items() if card_id != key and needle in json.dumps(card))


def plan(cards):
    """Return (writes, deletes, after) for a {card_id: card} catalogue view."""
    existing_burn = cards.get(BURN)
    if existing_burn is None:
        desired_burn = burn_effect(cards[OLD_BURN])
    else:
        desired_burn = without_rev(existing_burn)
        assert desired_burn["effect"]["buffs"] in ({"burning": 3}, {"burning": 5}) and desired_burn["duration"] in (3, 7.5), "a different " + BURN + " exists"
    migrated = {
        "global_logic": migrate_global_logic(cards["global_logic"]),
        "shop_logic": migrate_shop_logic(cards["shop_logic"]),
        LAMPS: migrate_lamps(cards[LAMPS], cards.get(FIRE_LAMPS), desired_burn["duration"]),
        SPLASHING_ITEM: migrate_splashing_item(cards[SPLASHING_ITEM], cards[SPLASHING_PERK]),
    }
    if BRICKED_UP in cards:
        variant = copy.deepcopy(cards[BRICKED_UP])
        for hit in burn_lists(variant):
            hit["unit_const_effects"] = [BURN if e == OLD_BURN else e for e in hit["unit_const_effects"]]
        migrated[BRICKED_UP] = variant
    writes = {card_id: card for card_id, card in migrated.items() if card != cards[card_id]}
    if existing_burn is None:
        writes = {BURN: desired_burn, **writes}  # created before the lamps reference it
    deletes = [card_id for card_id in DELETED if card_id in cards]

    after = {card_id: card for card_id, card in cards.items() if card_id not in DELETED}
    after.update(migrated)
    after[BURN] = existing_burn or desired_burn
    for card_id in DELETED:
        dangling = references(after, card_id)
        assert not dangling, f"{card_id} still referenced by {dangling}"
    return writes, deletes, after


def audit(cards):
    heroes = cards["global_logic"]["perks"]["heroes"]
    shop_items = [item for category in cards["shop_logic"]["shop"]["categories"] for item in category.get("items") or []]
    return {
        "hero_perks": {hero: heroes[hero] for hero in REMOVED},
        "shop_items": len(shop_items),
        "lamp_burn": [hit["unit_const_effects"] for hit in burn_lists(cards[LAMPS])],
        "lamp_pool": cards[LAMPS]["ammo"][0]["pool"]["pool_size"],
        "lamp_text": cards[LAMPS]["description"]["text"],
        "burn": {"duration": cards[BURN]["duration"], "buffs": cards[BURN]["effect"]["buffs"]},
        "splashing_item_name": cards[SPLASHING_ITEM]["name"]["text"],
        "removed_present": [card_id for card_id in DELETED if card_id in cards],
    }


if __name__ == "__main__":
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
    assert plan(after)[:2] == ({}, []), "migration must be idempotent"
    print(json.dumps({"write": list(writes), "delete": deletes,
                      "revisions": {card_id: before[card_id]["_rev"] for card_id in list(writes) + deletes
                                    if card_id in before},
                      "after": audit(after)}, indent=2, ensure_ascii=False), flush=True)
    if "--apply" not in sys.argv or not (writes or deletes):
        sys.exit()

    backup = Path("/root/config-backups") / (
        "remove-hero-perks-burning-lamps-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    )
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps(before, indent=2, ensure_ascii=False))
    # The new burn exists before the lamps reference it; unlisting precedes deletion.
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
