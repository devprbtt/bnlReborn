"""Publish the Nigel weapon skins and their shop items as CouchDB cards.

Weapon skins are private CardSkin cards named skin_weapon_* with no prefab: they recolour the hero's weapons and are
never worn as the character skin. Each is sold by a public shop_item with no dependency that is NOT listed in
shop_logic categories, so released clients never show it; only clients with the SKINS tab list them.

Run on the production host before the server that reads them restarts (the catalogue is read at start). Default is
read-only and prints the plan. --apply creates the documents that are absent, updates only the ones listed in UPDATE,
refuses any other difference, keeps a private rollback snapshot and reads every document back.

2026-10-28: Blackout was one skin for the rifle, revolver and cricket bat. A skin now covers one weapon and a character
skin wears one per weapon (server message 54), so Blackout keeps its id (owners keep it) as Blackout Rifle, and the
revolver and bat are their own skins.
--print prints the cards for offline comparison.
"""
import base64, json, sys, time, urllib.error, urllib.request
from pathlib import Path

PRICE = 2000  # Gold; owner decision 2026-10-27 (about ten average matches)
# id suffix, name, description
SKINS = [
    ("hunter_ice_rifle", "Ice Rifle", "A crystal-clear rifle stock carved from glacier ice."),
    ("hunter_walnut_rifle", "Walnut Rifle", "Hand-finished burl walnut with polished brass fittings."),
    ("hunter_blackout", "Blackout Rifle", "Matte black rifle with a dark steel finish."),
    ("hunter_blackout_revolver", "Blackout Revolver", "Old School Nigel's school revolver in matte black."),
    ("hunter_blackout_cricket_bat", "Blackout Cricket Bat", "Old School Nigel's cricket bat in brushed steel with a black carbon grip."),
    ("hunter_gilded_rifle", "Gilded Rifle", "Engraved brass fittings on a deep navy stock."),
    ("hunter_sapphire_rifle", "Sapphire Rifle", "Polished steel scrollwork over sapphire-blue wood."),
]
# Derived weapon skins: a character skin's own weapons for its owner's other skins, on every hero with skins that
# carry their own (and the default skin's weapons in those slots). Owned with that skin (WeaponSkinSelections.Derived),
# so they get a private card and no shop item. id suffix, hero, name (client tools/skin-shop/derived_weapon_skins.py).
DERIVED = [
    ("abe_ice_axe", "unit_hero_abe", "Ice Axe"),
    ("abe_snow_thrower", "unit_hero_abe", "SnowThrower 6000 SUX"),
    ("abe_ghillie_ice_axe", "unit_hero_abe", "Ghillie Ice Axe"),
    ("abe_ghillie_snow_thrower", "unit_hero_abe", "Ghillie SnowThrower"),
    ("cogwheel_gear_grinder", "unit_hero_cogwheel", "Gear Grinder"),
    ("cogwheel_rotary_cannon", "unit_hero_cogwheel", "Rotary Cannon"),
    ("cogwheel_cannon", "unit_hero_cogwheel", "Cannon"),
    ("cogwheel_golden_gear_grinder", "unit_hero_cogwheel", "Golden Gear Grinder"),
    ("cogwheel_golden_rotary_cannon", "unit_hero_cogwheel", "Golden Rotary Cannon"),
    ("cogwheel_golden_cannon", "unit_hero_cogwheel", "Golden Cannon"),
    ("cogwheel_capn_gear_grinder", "unit_hero_cogwheel", "Cap'n Gear Grinder"),
    ("cogwheel_capn_rotary_cannon", "unit_hero_cogwheel", "Cap'n Rotary Cannon"),
    ("cogwheel_capn_cannon", "unit_hero_cogwheel", "Cap'n Cannon"),
    ("cogwheel_dwarven_gear_grinder", "unit_hero_cogwheel", "Dwarven Gear Grinder"),
    ("cogwheel_dwarven_rotary_cannon", "unit_hero_cogwheel", "Dwarven Rotary Cannon"),
    ("cogwheel_dwarven_cannon", "unit_hero_cogwheel", "Dwarven Cannon"),
    ("cogwheel_cambot_gear_grinder", "unit_hero_cogwheel", "Cambot Gear Grinder"),
    ("cogwheel_cambot_rotary_cannon", "unit_hero_cogwheel", "Cambot Rotary Cannon"),
    ("cogwheel_cambot_cannon", "unit_hero_cogwheel", "Cambot Cannon"),
    ("cogwheel_soldered_gear_grinder", "unit_hero_cogwheel", "Soldered Gear Grinder"),
    ("cogwheel_soldered_rotary_cannon", "unit_hero_cogwheel", "Soldered Rotary Cannon"),
    ("cogwheel_soldered_cannon", "unit_hero_cogwheel", "Soldered Cannon"),
    ("engineer_drill", "unit_hero_engineer", "Drill"),
    ("engineer_rivet_gun", "unit_hero_engineer", "Rivet Gun"),
    ("engineer_caulk_gun", "unit_hero_engineer", "Caulk Gun"),
    ("engineer_spaced_out_drill", "unit_hero_engineer", "Spaced Out Drill"),
    ("engineer_spaced_out_rivet_gun", "unit_hero_engineer", "Spaced Out Rivet Gun"),
    ("engineer_spaced_out_caulk_gun", "unit_hero_engineer", "Spaced Out Caulk Gun"),
    ("hunter_machete", "unit_hero_hunter", "Machete"),
    ("hunter_nellie", "unit_hero_hunter", "Nellie"),
    ("hunter_revolver", "unit_hero_hunter", "Revolver"),
    ("hunter_old_school_rifle", "unit_hero_hunter", "Old School Rifle"),
    ("hunter_old_school_cricket_bat", "unit_hero_hunter", "Old School Cricket Bat"),
    ("hunter_old_school_revolver", "unit_hero_hunter", "Old School Revolver"),
    ("hunter_arctic_wolf_rifle", "unit_hero_hunter", "Arctic Wolf Rifle"),
    ("hunter_arctic_wolf_pistol", "unit_hero_hunter", "Arctic Wolf M1911"),
    ("ninja_shuko", "unit_hero_ninja", "Shuko"),
    ("ninja_katana", "unit_hero_ninja", "Katana"),
    ("ninja_ninja_star", "unit_hero_ninja", "Ninja Star"),
    ("ninja_2080_shuko", "unit_hero_ninja", "2080 Shuko"),
    ("ninja_2080_katana", "unit_hero_ninja", "2080 Katana"),
    ("ninja_2080_ninja_star", "unit_hero_ninja", "2080 Ninja Star"),
    ("roly_robo_paw", "unit_hero_roly", "RoboPaw"),
    ("roly_tank_ball_cannons", "unit_hero_roly", "TankBall Cannons"),
    ("roly_high_techy_robo_paw", "unit_hero_roly", "High Techy RoboPaw"),
    ("roly_high_techy_tank_ball_cannons", "unit_hero_roly", "High Techy TankBall Cannons"),
    ("roly_shake_shake_robo_paw", "unit_hero_roly", "Shake Shake RoboPaw"),
    ("roly_shake_shake_tank_ball_cannons", "unit_hero_roly", "Shake Shake TankBall Cannons"),
    ("roly_dive_dive_robo_paw", "unit_hero_roly", "Dive Dive RoboPaw"),
    ("roly_dive_dive_tank_ball_cannons", "unit_hero_roly", "Dive Dive TankBall Cannons"),
    ("sarge_stone_m60", "unit_hero_sarge_stone", "M60 OMG"),
    ("sarge_stone_tommy_gun", "unit_hero_sarge_stone", "Tommy Gun"),
]
# Existing documents this run may change (the 2026-10-28 Blackout split); every other difference is refused.
UPDATE = {"skin_weapon_hunter_blackout", "shop_item_skin_weapon_hunter_blackout"}


def text(value):
    return {"text": value, "data": {}}


def build():
    out = {}
    for suffix, name, description in SKINS:
        skin_id = "skin_weapon_" + suffix
        out[skin_id] = {"_id": skin_id, "category": "skin", "scope": "private", "name": text(name),
                        "hero_key": "unit_hero_" + suffix.split("_")[0], "icon_portrait": skin_id,
                        "icon_portrait_profile": skin_id, "bundle": "weapon_skins_longshot"}
        shop_id = "shop_item_" + skin_id
        out[shop_id] = {"_id": shop_id, "category": "shop_item", "scope": "public", "name": text(name),
                        "description": text(description), "image": skin_id, "available_in_shop": True,
                        "is_new": True, "free_to_try": False, "items": [skin_id], "price_virtual": PRICE}
    for suffix, hero, name in DERIVED:
        skin_id = "skin_weapon_" + suffix
        out[skin_id] = {"_id": skin_id, "category": "skin", "scope": "private", "name": text(name),
                        "hero_key": hero, "icon_portrait": skin_id,
                        "icon_portrait_profile": skin_id, "bundle": "weapon_skins_longshot"}
    return out


planned = build()
if "--print" in sys.argv:
    print(json.dumps(planned, indent=1, sort_keys=True))
    sys.exit()

c = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
c = {k.replace("_", "").lower(): v for k, v in c.items()}
base = c["couchdbendpoint"].rstrip("/") + "/" + c["couchdbdatabasename"]
auth = "Basic " + base64.b64encode((c["couchdbusername"] + ":" + c["couchdbpassword"]).encode()).decode()


def request(name, data=None):
    req = urllib.request.Request(base + "/" + name, headers={"Authorization": auth, "Content-Type": "application/json"},
                                 data=None if data is None else json.dumps(data).encode(),
                                 method="GET" if data is None else "PUT")
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def fetch(name):
    try:
        return request(name)
    except urllib.error.HTTPError as e:
        if e.code != 404:
            raise
        return None


def body(doc):
    return {k: v for k, v in doc.items() if k != "_rev"}


heroes = {h: fetch(h) for h in sorted({"unit_hero_hunter"} | {hero for _, hero, _ in DERIVED})}
assert all(h and h["category"] == "unit" for h in heroes.values()), "hero missing"
hero = heroes["unit_hero_hunter"]
shop_logic = fetch("shop_logic")
listed = json.dumps(shop_logic or {})
assert not any(k in listed for k in planned), "a weapon skin is listed in shop_logic; released clients would show it"
before = {n: fetch(n) for n in planned}
changed = sorted(n for n, d in before.items() if d is not None and body(d) != planned[n])
conflicts = [n for n in changed if n not in UPDATE]
assert not conflicts, "documents already exist with different content: " + ", ".join(conflicts)
create = sorted(n for n, d in before.items() if d is None)
print(json.dumps({"create": create, "update": changed, "already_identical": sorted(set(planned) - set(create) - set(changed)),
                  "hero_skins_unchanged": hero["data"]["skins"]}), flush=True)
if "--apply" not in sys.argv or not (create or changed):
    sys.exit()

backup = Path("/root/config-backups") / ("weapon-skin-cards-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# Skins first: a shop item naming a skin that does not exist yet would fail catalogue validation on reload.
for name in sorted(create, key=lambda n: n.startswith("shop_item_")):
    request(name, planned[name])
for name in changed:
    request(name, dict(planned[name], _rev=before[name]["_rev"]))
after = {n: request(n) for n in planned}
for name, doc in planned.items():
    assert body(after[name]) == doc, "readback differs for " + name
assert all(fetch(h) == doc for h, doc in heroes.items()), "hero changed"
print(json.dumps({"backup": str(backup), "revisions": {n: after[n]["_rev"] for n in sorted(after)}}), flush=True)
