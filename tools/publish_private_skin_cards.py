"""Publish the private skins as ordinary CouchDB cards with scope "private".

The server used to add these two cards in code (PrivateSkinRegistration) at every catalogue load. Now the
catalogue only says which items exist, and ownership comes from the server's InventoryGrants table, so the
cards must live in CouchDB before a server without that registration starts.

Run on the production host. Default is read-only and prints the planned change. --apply writes
revision-checked documents (a concurrent edit fails instead of being overwritten), keeps a private
rollback snapshot, and needs no restart: the running server keeps its own copy until it is replaced.
--from-file CATALOGUE.json --print prints the cards this would publish, for offline comparison.
"""
import base64, copy, json, sys, time, urllib.error, urllib.request
from pathlib import Path

# hero, variant, name, source skin, unit prefab, first-person prefab, first-person bundle
SKINS = [
    ("hunter", "arctic_wolf", "Arctic Wolf", "skin_hunter_s1",
     "LongshotArcticWolfPrivate", "LongshotPlayerArcticWolfPrivate", "fps_longshot_s1"),
    ("boxer", "demon", "Demon", "skin_boxer_s6",
     "SweetScienceDarklordFinal", "SweetSciencePlayerS7", "fps_sweetscience_s6"),
]


def build(docs):
    """Returns {doc id: new document} for the two skin cards and the two hero units that list them."""
    out = {}
    for hero, variant, name, source_id, prefab, fps_prefab, fps_bundle in SKINS:
        source, unit_id = docs[source_id], "unit_hero_" + hero
        skin_id = f"skin_{hero}_{variant}_private"
        card = {"_id": skin_id, "category": "skin", "scope": "private",
                "prefab": "Prefabs/Units/" + prefab, "fps_prefab": "Prefabs/Player/" + fps_prefab,
                "icon_portrait": f"portrait_{hero}_{variant}_private",
                "icon_portrait_profile": f"shop_{hero}_{variant}_private",
                "bundle": source["bundle"], "fps_bundle": fps_bundle,
                "name": {"text": name, "data": {}}, "hero_key": unit_id}
        for music in ("learning_music", "lockin_music", "death_music_sting"):
            if source.get(music) is not None:
                card[music] = source[music]
        if skin_id in docs and "_rev" in docs[skin_id]:
            card["_rev"] = docs[skin_id]["_rev"]
        out[skin_id] = card

        unit = copy.deepcopy(out.get(unit_id, docs[unit_id]))
        skins = unit["data"].setdefault("skins", [])
        if skin_id not in skins:
            skins.append(skin_id)
        out[unit_id] = unit
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


names = sorted({s[3] for s in SKINS} | {"unit_hero_" + s[0] for s in SKINS} | {f"skin_{s[0]}_{s[1]}_private" for s in SKINS})
before = {n: fetch(n) for n in names}
assert all(before[s[3]] and before[s[3]]["category"] == "skin" for s in SKINS), "source skin missing"
assert all(before["unit_hero_" + s[0]] and before["unit_hero_" + s[0]]["category"] == "unit" for s in SKINS), "hero missing"
planned = build({k: v for k, v in before.items() if v is not None})
changes = {k: v for k, v in planned.items() if before.get(k) != v}
print(json.dumps({"write": sorted(changes),
                  "new_cards": sorted(k for k in planned if before.get(k) is None),
                  "hero_skins_after": {k: v["data"]["skins"] for k, v in planned.items() if k.startswith("unit_hero_")}}),
      flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("private-skin-cards-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# Cards first: a hero listing a skin that does not exist yet would fail catalogue validation on reload.
for name in sorted(changes, key=lambda n: n.startswith("unit_hero_")):
    request(name, changes[name])
after = {n: request(n) for n in planned}
for name, doc in planned.items():
    saved = {k: v for k, v in after[name].items() if k != "_rev"}
    assert saved == {k: v for k, v in doc.items() if k != "_rev"}, "readback differs for " + name
for s in SKINS:
    assert request(s[3]) == before[s[3]], "source skin changed: " + s[3]
print(json.dumps({"backup": str(backup), "revisions": {n: after[n]["_rev"] for n in sorted(after)}}), flush=True)
