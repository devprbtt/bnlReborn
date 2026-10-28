"""Publish the first weapon skins and their shop items as CouchDB cards.

Weapon skins are private CardSkin cards named skin_weapon_* with no prefab: they recolour the hero's weapons and are
never worn as the character skin. Each is sold by a public shop_item with no dependency that is NOT listed in
shop_logic categories, so released clients never show it; only clients with the SKINS tab list them.

Run on the production host before the server that reads them restarts (the catalogue is read at start). Default is
read-only and prints the plan plus one live shop_item for field comparison. --apply creates the documents only if they
are absent or identical, keeps a private rollback snapshot and reads every document back.
--print prints the cards for offline comparison.
"""
import base64, json, sys, time, urllib.error, urllib.request
from pathlib import Path

PRICE = 2000  # Gold; owner decision 2026-10-27 (about ten average matches)
# id suffix, name, description
SKINS = [
    ("hunter_ice_rifle", "Ice Rifle", "A crystal-clear rifle stock carved from glacier ice."),
    ("hunter_walnut_rifle", "Walnut Rifle", "Hand-finished burl walnut with polished brass fittings."),
    ("hunter_blackout", "Blackout", "Matte black rifle, revolver and cricket bat."),
]


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


hero = fetch("unit_hero_hunter")
assert hero and hero["category"] == "unit", "hero missing"
shop_logic = fetch("shop_logic")
listed = json.dumps(shop_logic or {})
assert not any(k in listed for k in planned), "a weapon skin is listed in shop_logic; released clients would show it"
before = {n: fetch(n) for n in planned}
conflicts = sorted(n for n, d in before.items() if d is not None and body(d) != planned[n])
assert not conflicts, "documents already exist with different content: " + ", ".join(conflicts)
create = sorted(n for n, d in before.items() if d is None)
print(json.dumps({"create": create, "already_identical": sorted(set(planned) - set(create)),
                  "hero_skins_unchanged": hero["data"]["skins"]}), flush=True)
if "--apply" not in sys.argv or not create:
    sys.exit()

backup = Path("/root/config-backups") / ("weapon-skin-cards-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# Skins first: a shop item naming a skin that does not exist yet would fail catalogue validation on reload.
for name in sorted(create, key=lambda n: n.startswith("shop_item_")):
    request(name, planned[name])
after = {n: request(n) for n in planned}
for name, doc in planned.items():
    assert body(after[name]) == doc, "readback differs for " + name
assert fetch("unit_hero_hunter") == hero, "hero changed"
print(json.dumps({"backup": str(backup), "revisions": {n: after[n]["_rev"] for n in sorted(after)}}), flush=True)
