"""Publish the Christmas Boris (Yeti) skin card and list it under unit_hero_abe.

The client assets (Prefabs/Units/AbeChristmas in character_abe, Prefabs/Player/AbePlayerChristmas in fps_abe_s2,
portrait_abe_christmas[_small], shop_abe_christmas[_featured]) ship in a signed client release. Apply this only
after that release is live: a client without the prefab cannot show a card that points at it.

Run on the production host. Default is read-only and prints the planned change. --apply writes revision-checked
documents (a concurrent edit fails instead of being overwritten), keeps a private rollback snapshot, and needs no
restart. --scope private publishes it unowned for grant-based testing; the default is public, like Arctic Wolf.
--from-file CATALOGUE.json --print prints the cards this would publish, for offline comparison.
"""
import base64, copy, json, sys, time, urllib.error, urllib.request
from pathlib import Path

SKIN_ID, HERO_ID, SOURCE_ID = "skin_abe_christmas", "unit_hero_abe", "skin_abe_s2"
SCOPE = sys.argv[sys.argv.index("--scope") + 1] if "--scope" in sys.argv else "public"
assert SCOPE in ("public", "private"), SCOPE


def build(docs):
    """Returns {doc id: new document} for the skin card and the hero unit that lists it."""
    source = docs[SOURCE_ID]
    card = {"_id": SKIN_ID, "category": "skin", "scope": SCOPE,
            "prefab": "Prefabs/Units/AbeChristmas", "fps_prefab": "Prefabs/Player/AbePlayerChristmas",
            "icon_portrait": "portrait_abe_christmas", "icon_portrait_profile": "shop_abe_christmas",
            "bundle": source["bundle"], "fps_bundle": source["fps_bundle"],
            "name": {"text": "Christmas Boris", "data": {}}, "hero_key": HERO_ID}
    for music in ("learning_music", "lockin_music", "death_music_sting"):
        if source.get(music) is not None:
            card[music] = source[music]
    if SKIN_ID in docs and "_rev" in docs[SKIN_ID]:
        card["_rev"] = docs[SKIN_ID]["_rev"]
    unit = copy.deepcopy(docs[HERO_ID])
    skins = unit["data"].setdefault("skins", [])
    if SKIN_ID not in skins:
        skins.append(SKIN_ID)
    return {SKIN_ID: card, HERO_ID: unit}


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


before = {n: fetch(n) for n in (SOURCE_ID, HERO_ID, SKIN_ID)}
assert before[SOURCE_ID] and before[SOURCE_ID]["category"] == "skin", "source skin missing"
assert before[HERO_ID] and before[HERO_ID]["category"] == "unit", "hero missing"
planned = build({k: v for k, v in before.items() if v is not None})
changes = {k: v for k, v in planned.items() if before.get(k) != v}
print(json.dumps({"write": sorted(changes), "new_card": before[SKIN_ID] is None, "scope": SCOPE,
                  "hero_skins_after": planned[HERO_ID]["data"]["skins"]}), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("abe-christmas-skin-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# Card first: a hero listing a skin that does not exist yet would fail catalogue validation on reload.
for name in sorted(changes, key=lambda n: n == HERO_ID):
    request(name, changes[name])
after = {n: request(n) for n in planned}
for name, doc in planned.items():
    saved = {k: v for k, v in after[name].items() if k != "_rev"}
    assert saved == {k: v for k, v in doc.items() if k != "_rev"}, "readback differs for " + name
assert request(SOURCE_ID) == before[SOURCE_ID], "source skin changed"
print(json.dumps({"backup": str(backup), "revisions": {n: after[n]["_rev"] for n in sorted(after)}}), flush=True)
