"""Put four character skins on sale at 10,000 Gold each (owner decision 2026-10-29): Arctic Wolf, Darklord (renamed from
"Demon", owner 2026-10-29), Christmas Boris and Snow Globe Vander (Christmas Vander).

Each skin card becomes private (owned only once bought or granted) and gets a public shop item at 10,000 Gold, listed
in no shop_logic category, as the weapon skins' are: the Reborn client's Catalogue > SKINS > CHARACTER SKINS tab sells
it, released clients never list it, and the Reborn client lists it in Loadouts for its owners. Arctic Wolf, Darklord
and Christmas Boris were public, so every account owned them: the server's one-time startup migration
skin_sale_owners_v1 (MasterServerDatabase.GrantSaleSkins) grants them to every account existing at that start, so
nobody loses one. Snow Globe Vander is new: its card is created and appended to unit_hero_magnus's skins.

Order: the client release carrying the art (Darklord and Christmas Vander banners, Christmas Vander's models and
portraits) is live first. Then, on the production host, run this read-only, then --apply, then restart the server that
carries the migration (the catalogue and the grants are read at start, so the cards turn private and the grants land
in the same start). --apply writes revision-checked documents (a concurrent edit fails instead of being overwritten),
keeps a private rollback snapshot and reads every document back. --from-file CATALOGUE.json --print prints the
documents this would publish, for offline comparison.
"""
import base64, copy, json, sys, time, urllib.error, urllib.request
from pathlib import Path

PRICE = 10000  # Gold; owner decision 2026-10-29
# skin card id, shop item id, hero, description
SALES = [
    ("skin_hunter_arctic_wolf_private", "shop_skin_hunter_arctic_wolf_private", "unit_hero_hunter",
     "Nigel in winter camouflage, with a suppressed rifle and M1911 that each reload their own way."),
    ("skin_boxer_demon_private", "shop_skin_boxer_demon_private", "unit_hero_boxer",
     "Sweet Science as the Darklord, his fists and tattoos burning with violet light."),
    ("skin_abe_christmas", "shop_skin_abe_christmas", "unit_hero_abe",
     "Boris the yeti dressed for Christmas, from his Santa hat to his festive coat."),
    ("skin_magnus_christmas", "shop_skin_magnus_christmas", "unit_hero_magnus",
     "Vander Graaf in a navy peacoat and Fair Isle scarf, with a snowy village in his globe."),
]
# Card names changed with the sale (owner 2026-10-29); the shop items take the card's name.
RENAME = {"skin_boxer_demon_private": "Darklord"}
# Snow Globe Vander's new card, cut from Vander Graaf's (bundles and music).
NEW_SKIN, NEW_SOURCE = "skin_magnus_christmas", "skin_magnus_s1"


def text(value):
    return {"text": value, "data": {}}


def new_skin(source):
    card = {"_id": NEW_SKIN, "category": "skin", "scope": "private",
            "prefab": "Prefabs/Units/MagnusChristmas", "fps_prefab": "Prefabs/Player/MagnusPlayerChristmas",
            "icon_portrait": "portrait_magnus_christmas", "icon_portrait_profile": NEW_SKIN,
            "bundle": source["bundle"], "fps_bundle": source["fps_bundle"],
            "name": text("Snow Globe Vander"), "hero_key": "unit_hero_magnus"}
    for music in ("learning_music", "lockin_music", "death_music_sting"):
        if source.get(music) is not None:
            card[music] = source[music]
    return card


def build(docs):
    """Returns {doc id: new document}: each skin card, its shop item, and Vander's unit listing the new skin."""
    out = {}
    for skin_id, shop_id, hero_id, description in SALES:
        if skin_id == NEW_SKIN and skin_id not in docs:
            card = new_skin(docs[NEW_SOURCE])
        else:
            card = copy.deepcopy(docs[skin_id])
            assert card["category"] == "skin" and card.get("hero_key") == hero_id, skin_id
            card["scope"] = "private"
            if skin_id in RENAME:
                card["name"] = text(RENAME[skin_id])
        out[skin_id] = card
        shop = {"_id": shop_id, "category": "shop_item", "scope": "public", "name": copy.deepcopy(card["name"]),
                "description": text(description), "short_description": text("Character skin"),
                "image": card["icon_portrait_profile"], "available_in_shop": True, "is_new": True, "free_to_try": False,
                "items": [skin_id], "dependency": hero_id, "price_virtual": PRICE}
        if shop_id in docs and "_rev" in docs[shop_id]:
            shop["_rev"] = docs[shop_id]["_rev"]
        out[shop_id] = shop
        if hero_id not in out:
            out[hero_id] = copy.deepcopy(docs[hero_id])
        skins = out[hero_id]["data"].setdefault("skins", [])
        if skin_id not in skins:
            skins.append(skin_id)
    return out


if "--from-file" in sys.argv:
    docs = {d["_id"]: d for d in json.loads(Path(sys.argv[sys.argv.index("--from-file") + 1]).read_text(encoding="utf-8"))}
    print(json.dumps(build(docs), indent=1, sort_keys=True))
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


names = sorted({NEW_SOURCE} | {n for sale in SALES for n in sale[:3]})
before = {n: fetch(n) for n in names}
for skin_id, _, hero_id, _ in SALES:
    assert before[hero_id] and before[hero_id]["category"] == "unit", "hero missing: " + hero_id
    assert skin_id == NEW_SKIN or before[skin_id], "skin missing: " + skin_id
shop_logic = fetch("shop_logic")
listed = json.dumps(shop_logic or {})
assert not any(sale[1] in listed for sale in SALES), "a shop item is listed in shop_logic; released clients would show it"
planned = build({k: v for k, v in before.items() if v is not None})
changes = {k: v for k, v in planned.items() if {x: y for x, y in (before.get(k) or {}).items() if x != "_rev"} != {x: y for x, y in v.items() if x != "_rev"}}
print(json.dumps({"write": sorted(changes), "price": PRICE,
                  "scope_before": {s[0]: (before.get(s[0]) or {}).get("scope") for s in SALES}}), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("character-skin-sale-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# Skin cards, then the shop items selling them, then the heroes: each document refers only to ones already written.
rank = lambda n: 0 if n.startswith("skin_") else 1 if n.startswith("shop_") else 2
for name in sorted(changes, key=rank):
    request(name, changes[name])
after = {n: request(n) for n in planned}
for name, doc in planned.items():
    saved = {k: v for k, v in after[name].items() if k != "_rev"}
    assert saved == {k: v for k, v in doc.items() if k != "_rev"}, "readback differs for " + name
assert request(NEW_SOURCE) == before[NEW_SOURCE], "source skin changed"
assert not any(sale[1] in json.dumps(fetch("shop_logic") or {}) for sale in SALES), "shop_logic changed"
print(json.dumps({"backup": str(backup), "revisions": {n: after[n]["_rev"] for n in sorted(after)}}), flush=True)
