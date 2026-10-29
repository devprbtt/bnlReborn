"""Publish Snow Globe Vander (Christmas Vander) for sale: its skin card, a 10,000 Gold shop item, and Vander's skin list.

The client assets (Prefabs/Units/MagnusChristmas in character_magnus, Prefabs/Player/MagnusPlayerChristmas in
fps_magnus_s1, the shop banner skin_magnus_christmas and portrait_magnus_christmas[_small] as embedded art) ship in a
signed client release. Apply this only after that release is live: a client without the prefab cannot show a card
that points at it.

The skin card is private (owned only once bought), so the Reborn client's Catalogue > SKINS > CHARACTER SKINS tab
sells it. Its shop item is public and listed in no shop_logic category, as the weapon skins' are, so released clients
never show it in their stock hero shop; the Reborn client lists it in Loadouts for its owners.

Run on the production host. Default is read-only and prints the planned change. --apply writes revision-checked
documents (a concurrent edit fails instead of being overwritten), keeps a private rollback snapshot, and needs no
restart. --from-file CATALOGUE.json --print prints the documents this would publish, for offline comparison.
"""
import base64, copy, json, sys, time, urllib.error, urllib.request
from pathlib import Path

SKIN_ID, SHOP_ID, HERO_ID, SOURCE_ID = "skin_magnus_christmas", "shop_skin_magnus_christmas", "unit_hero_magnus", "skin_magnus_s1"
PRICE = 10000  # Gold; owner decision 2026-10-29
NAME = "Snow Globe Vander"
DESCRIPTION = "Vander Graaf in a navy peacoat and Fair Isle scarf, with a snowy village in his globe."


def text(value):
    return {"text": value, "data": {}}


def build(docs):
    """Returns {doc id: new document} for the skin card, its shop item and the hero unit that lists it."""
    source = docs[SOURCE_ID]
    card = {"_id": SKIN_ID, "category": "skin", "scope": "private",
            "prefab": "Prefabs/Units/MagnusChristmas", "fps_prefab": "Prefabs/Player/MagnusPlayerChristmas",
            "icon_portrait": "portrait_magnus_christmas", "icon_portrait_profile": SKIN_ID,
            "bundle": source["bundle"], "fps_bundle": source["fps_bundle"],
            "name": text(NAME), "hero_key": HERO_ID}
    for music in ("learning_music", "lockin_music", "death_music_sting"):
        if source.get(music) is not None:
            card[music] = source[music]
    shop = {"_id": SHOP_ID, "category": "shop_item", "scope": "public", "name": text(NAME),
            "description": text(DESCRIPTION), "short_description": text("Character skin"), "image": SKIN_ID,
            "available_in_shop": True, "is_new": True, "free_to_try": False, "items": [SKIN_ID],
            "dependency": HERO_ID, "price_virtual": PRICE}
    for doc, new in ((SKIN_ID, card), (SHOP_ID, shop)):
        if doc in docs and "_rev" in docs[doc]:
            new["_rev"] = docs[doc]["_rev"]
    unit = copy.deepcopy(docs[HERO_ID])
    skins = unit["data"].setdefault("skins", [])
    if SKIN_ID not in skins:
        skins.append(SKIN_ID)
    return {SKIN_ID: card, SHOP_ID: shop, HERO_ID: unit}


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


before = {n: fetch(n) for n in (SOURCE_ID, HERO_ID, SKIN_ID, SHOP_ID)}
assert before[SOURCE_ID] and before[SOURCE_ID]["category"] == "skin", "source skin missing"
assert before[HERO_ID] and before[HERO_ID]["category"] == "unit", "hero missing"
shop_logic = fetch("shop_logic")
assert SHOP_ID not in json.dumps(shop_logic or {}), "the shop item is listed in shop_logic; released clients would show it"
planned = build({k: v for k, v in before.items() if v is not None})
changes = {k: v for k, v in planned.items() if before.get(k) != v}
print(json.dumps({"write": sorted(changes), "new_card": before[SKIN_ID] is None, "new_shop_item": before[SHOP_ID] is None,
                  "price": PRICE, "hero_skins_after": planned[HERO_ID]["data"]["skins"]}), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("vander-christmas-skin-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# Card, then the shop item that sells it, then the hero: each document refers only to ones already written.
order = [SKIN_ID, SHOP_ID, HERO_ID]
for name in sorted(changes, key=order.index):
    request(name, changes[name])
after = {n: request(n) for n in planned}
for name, doc in planned.items():
    saved = {k: v for k, v in after[name].items() if k != "_rev"}
    assert saved == {k: v for k, v in doc.items() if k != "_rev"}, "readback differs for " + name
assert request(SOURCE_ID) == before[SOURCE_ID], "source skin changed"
assert SHOP_ID not in json.dumps(fetch("shop_logic") or {}), "shop_logic changed"
print(json.dumps({"backup": str(backup), "revisions": {n: after[n]["_rev"] for n in sorted(after)}}), flush=True)
