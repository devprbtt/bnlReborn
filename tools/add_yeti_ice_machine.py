"""Ice Machine catalogue migration. Read-only by default; --apply backs up and revision-guards writes.
Deploy matching server code before applying. --from-file JSON [--output JSON] works offline.
"""
import base64, copy, json, sys, time, urllib.error, urllib.parse, urllib.request
from pathlib import Path
GEAR="gear_abe_snow_thrower_ice_machine"
PERK="perk_hero_abe_ice_machine"
SHOP="shop_item_"+PERK
READ=(GEAR,PERK,SHOP,"global_logic","shop_logic")
DESCRIPTION="Snow Thrower right-click converts one hit block into friendly ice instead of placing snow / Snow Thrower right-click fire rate reduced by 35%"
def text(s): return {"text":s,"data":{}}
def without_rev(c):return {k:v for k,v in c.items() if k not in ("_rev","hercules_metadata")}
def clone(c,key):
    r=copy.deepcopy(without_rev(c));r["_id"]=key;return r
def plan(cards):
    ice=cards["block_ice"]
    assert ice["has_team"] and ice["special"]["affect_team"]=="opponent", "Ice must affect enemies only"
    gear=clone(cards["gear_abe_snow_thrower"],GEAR)
    alt=gear["tools"][1]; assert alt["type"]=="shot"
    effects=alt["hit_effect"]["instant"]
    spawning=[i for i,e in enumerate(effects) if e.get("type")=="blocks_spawn"]
    assert len(spawning)==1 and effects[spawning[0]]["pattern"]=={"type":"one","block_key":"block_snow"}
    effects[spawning[0]]={"type":"replace_blocks","replace_with":"block_ice","range":0,"interrupt":None,"targeting":None,"impact":None}
    timing=alt["timing"];pre=timing.get("pre_attack_time") or 0
    timing["attack_time"]=(pre+timing["attack_time"])/0.65-pre
    gear["description"]=text("Slow and damage enemies with freezing snow. <BINDING_CAST2> converts one hit block into friendly ice, with 35% lower secondary fire rate.")
    perk=clone(cards["perk_hero_abe_chill_dude"],PERK)
    perk.update(icon="shop_perk_hero_abe_facewash",name=text("Ice Machine"),description=text(DESCRIPTION),description_2=text(""),label="ice_machine",perk_mods=[{"type":"gear","replace_from":"gear_abe_snow_thrower","replace_to":GEAR}])
    shop=clone(cards["shop_item_perk_hero_abe_chill_dude"],SHOP)
    shop.update(name=text("Ice Machine"),description=text(DESCRIPTION),image=perk["icon"],items=[PERK])
    glob=copy.deepcopy(cards["global_logic"]);perks=glob["perks"]["heroes"]["unit_hero_abe"]
    if PERK not in perks:perks.append(PERK)
    logic=copy.deepcopy(cards["shop_logic"])
    categories=[c for c in logic["shop"]["categories"] if "shop_item_perk_hero_abe_chill_dude" in (c.get("items") or [])]
    assert len(categories)==1
    if SHOP not in categories[0]["items"]:categories[0]["items"].append(SHOP)
    desired={GEAR:gear,PERK:perk,SHOP:shop,"global_logic":glob,"shop_logic":logic};writes={}
    for key,card in desired.items():
        if key in cards and key in (GEAR,PERK,SHOP):assert without_rev(cards[key])==card,"Conflicting "+key
        if key not in cards or without_rev(cards[key])!=without_rev(card):
            if key in cards:card["_rev"]=cards[key]["_rev"]
            writes[key]=card
    after=dict(cards);after.update(desired)
    return writes,[],after

def audit(cards):
    return {"perks":cards["global_logic"]["perks"]["heroes"]["unit_hero_abe"],"timing":cards[GEAR]["tools"][1]["timing"],"icon":cards[PERK]["icon"],"ice_affects":cards["block_ice"]["special"]["affect_team"]}

if "--from-file" in sys.argv:
    cards={c["_id"]:c for c in json.loads(Path(sys.argv[sys.argv.index("--from-file")+1]).read_text(encoding="utf-8"))}
    writes,deletes,after=plan(cards)
    assert not plan(after)[0], "Migration must be idempotent"
    if "--output" in sys.argv:Path(sys.argv[sys.argv.index("--output")+1]).write_text(json.dumps(list(after.values())),encoding="utf-8")
    print(json.dumps({"writes":list(writes),"after":audit(after)},indent=2));sys.exit()

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
    "yeti-ice-machine-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2, ensure_ascii=False))
# Write gear before the perk, and the perk/shop item before their listings.
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
