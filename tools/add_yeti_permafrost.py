"""Prepare persistent Avalanche and Permafrost. Default: read-only production plan.
--from-file JSON [--output JSON] works offline. --apply requires the matching server
and VFX client to be released first. Writes are revision guarded and backed up.
"""
import base64, copy, json, sys, time, urllib.error, urllib.parse, urllib.request
from pathlib import Path
HERO = "unit_hero_abe"
ABILITY = "ability_abe_avalanche"
PERK = "perk_hero_abe_permafrost"
SHOP = "shop_item_" + PERK
PERK_ABILITY = "ability_abe_avalanche_permafrost"
AREA = "unit_abe_avalanche_area"
PERK_AREA = "unit_abe_permafrost_area"
SLOW = "effect_abe_avalanche_area_slow"
ROOT = "effect_abe_permafrost_root"
READ = (SLOW, ROOT, AREA, PERK_AREA, PERK_ABILITY, PERK, SHOP, ABILITY, HERO, "global_logic", "shop_logic")
DESCRIPTION = ("Avalanche lasts 4 seconds. Enemies who stay inside for 2 continuous seconds "
               "are rooted for 0.5 seconds, once per cast / Avalanche radius reduced by 20%")
def text(s): return {"text": s, "data": {}}
def without_rev(c): return {k:v for k,v in c.items() if k not in ("_rev", "hercules_metadata")}
def clone(c, key):
    r=copy.deepcopy(without_rev(c)); r["_id"]=key; return r
def spawn(key):
    return {"type":"unit_spawn", "unit_key":key, "impact":"impact_abe_snow_thrower_splash",
            "targeting":None, "interrupt":None}
def plan(cards):
    desired={}
    slow=clone(cards["effect_hero_abe_avalanche_slow"], SLOW)
    slow.update(duration=None, scores=0)
    desired[SLOW]=slow
    root=clone(cards["effect_kreepy_banana_lock_root"], ROOT)
    root.update(duration=0.5, scores=0)
    root["prefab_unit"]=slow["prefab_unit"]; root["prefab_player"]=slow["prefab_player"]
    desired[ROOT]=root
    for key in (AREA, PERK_AREA):
        area=clone(cards["unit_dummy_abe_avalanche"],key)
        area.update(prefab=None, data={"type":"common"}, movement=None, lifetime=4,
                    init_effects=[], enabled_effects=[], ground_only=False, allow_underwater=True)
        desired[key]=area
    ability=copy.deepcopy(cards[ABILITY])
    assert ability["charges"] == {"max_charges":1,"charge_cooldown":30}
    old=ability["behavior"]["hit_effect"]
    assert old == spawn(AREA) or (old.get("type")=="all_units_bunch" and old.get("range")==5
        and old.get("constant")==["effect_hero_abe_avalanche_slow"]), "Unexpected base Avalanche; review before migrating"
    ability["behavior"]["hit_effect"]=spawn(AREA)
    perk_ability=clone(ability,PERK_ABILITY)
    perk_ability["behavior"]["hit_effect"]=spawn(PERK_AREA)
    desired[PERK_ABILITY]=perk_ability
    perk=clone(cards["perk_hero_abe_chill_dude"],PERK)
    perk.update(icon="shop_perk_hero_abe_yuri_n_ice",name=text("Permafrost"),description=text(DESCRIPTION),
                description_2=text(""),label="permafrost",perk_mods=[{"type":"ability","replace_ability":PERK_ABILITY}])
    desired[PERK]=perk
    shop=clone(cards["shop_item_perk_hero_abe_chill_dude"],SHOP)
    shop.update(name=text("Permafrost"),description=text(DESCRIPTION),image=perk["icon"],items=[PERK])
    desired[SHOP]=shop
    desired[ABILITY]=ability
    hero=copy.deepcopy(cards[HERO])
    hero["data"]["gui_info"]["active_ability"]["description"]=text(
        "Yury throws a marker that creates a 5-block-radius Avalanche for 4 seconds, slowing enemies inside by 30%.")
    desired[HERO]=hero
    glob=copy.deepcopy(cards["global_logic"])
    perks=glob["perks"]["heroes"][HERO]
    if PERK not in perks: perks.append(PERK)
    for tip in glob["tips_logic"]["specific_hero_tips"][HERO]:
        if tip.get("image")=="activeability_icon_abe":
            tip["tip_text"]=text("Avalanche leaves a slowing area for 4 seconds. Throw it where enemies are grouped or about to push.")
    desired["global_logic"]=glob
    shop_logic=copy.deepcopy(cards["shop_logic"])
    categories=[c for c in shop_logic["shop"]["categories"] if "shop_item_perk_hero_abe_chill_dude" in (c.get("items") or [])]
    assert len(categories)==1
    if SHOP not in categories[0]["items"]: categories[0]["items"].append(SHOP)
    desired["shop_logic"]=shop_logic
    writes={}
    for key,card in desired.items():
        if key in cards and key not in (ABILITY,HERO,"global_logic","shop_logic"):
            assert without_rev(cards[key])==without_rev(card), "Conflicting existing card: "+key
        if key not in cards or without_rev(cards[key])!=without_rev(card):
            if key in cards: card["_rev"]=cards[key]["_rev"]
            writes[key]=card
    after=dict(cards); after.update(desired)
    return writes, [], after

def audit(cards):
    return {"perks":cards["global_logic"]["perks"]["heroes"][HERO],
            "normal_radius":5,"permafrost_radius":4,"lifetime":cards[AREA]["lifetime"],
            "root_duration":cards[ROOT]["duration"],"icon":cards[PERK]["icon"],
            "description":cards[PERK]["description"]["text"]}

if "--from-file" in sys.argv:
    cards={c["_id"]:c for c in json.loads(Path(sys.argv[sys.argv.index("--from-file")+1]).read_text(encoding="utf-8"))}
    writes,deletes,after=plan(cards)
    if "--output" in sys.argv:
        Path(sys.argv[sys.argv.index("--output")+1]).write_text(json.dumps(list(after.values())),encoding="utf-8")
    print(json.dumps({"writes":list(writes),"audit":audit(after)},indent=2))
    assert not plan(after)[0], "Migration is not idempotent"
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
    "yeti-permafrost-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
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
