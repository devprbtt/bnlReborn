"""Astarella default knockback and Fly Away Bad Guy rework.
Read-only unless --apply. Revision-guarded CouchDB writes with private backup.
--from-file accepts a catalogue array for a fully offline plan.
"""
import argparse
import base64
import copy
import json
import time
import urllib.request
import urllib.parse
from pathlib import Path

BASE = "unit_projectile_astro_saucer"
PERK = BASE + "_fly_away_bad_guy"
GEAR = "gear_astro_saucer_launcher"
PERK_GEAR = GEAR + "_fly_away_bad_guy"
CARD = "perk_hero_astro_fly_away_bad_guy"
SHOP = "shop_item_" + CARD
IDS = (BASE, PERK, GEAR, PERK_GEAR, CARD, SHOP)

def effect(card, kind):
    found = [x for x in card["data"]["death_effect"]["instant"] if x["type"] == kind]
    assert len(found) == 1, (card["_id"], kind)
    return found[0]

def plan(cards):
    result = copy.deepcopy({key: cards[key] for key in IDS})
    for key in (BASE, PERK):
        c = result[key]
        assert c["category"] == "unit" and c["data"]["type"] == "projectile"
        knock = effect(c, "knockback")
        assert knock["force"] in (5, 8.5) and knock["midair_force"] in (5, 8.5)
        knock["force"] = knock["midair_force"] = 8.5
        damage = effect(c, "splash_damage")["damage"]
        assert damage["player_damage"] in (21, 24) and damage["world_damage"] == 26
        damage["player_damage"] = 24 if key == BASE else 21
        damage["world_damage"] = 26
    data = result[PERK]["data"]
    assert data["acceleration"] in (100,150) and data["max_speed"] in (100,150)
    # Scaling the cap alone does nothing: the saucer stops accelerating at 0.85 seconds.
    data["acceleration"] = data["max_speed"] = 150
    assert result[BASE]["data"]["acceleration"] == result[BASE]["data"]["max_speed"] == 100
    assert result[BASE]["data"]["movement_timeout"] == data["movement_timeout"] == 0.85
    bullet = result[PERK_GEAR]["tools"][0]["bullet"]
    assert bullet["unit_projectile_key"] == PERK and bullet["min_speed"] in (1,1.5)
    assert result[GEAR]["tools"][0]["bullet"]["min_speed"] == 1
    bullet["min_speed"] = 1.5
    assert result[GEAR]["reload"]["reload_time"] == 2
    assert result[PERK_GEAR]["reload"]["reload_time"] in (2,2.3)
    result[PERK_GEAR]["reload"]["reload_time"] = 2
    description = {"text": "Saucer Launcher saucers fly 50% faster / Saucer Launcher deals 21 player damage", "data": {}}
    result[CARD]["description"] = copy.deepcopy(description)
    result[SHOP]["description"] = copy.deepcopy(description)
    return result

def summary(cards):
    return {key: {"damage":effect(cards[key],"splash_damage")["damage"],
                  "knockback":effect(cards[key],"knockback")["force"],
                  "acceleration":cards[key]["data"]["acceleration"],
                  "max_speed":cards[key]["data"]["max_speed"]} for key in (BASE,PERK)}

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--from-file")
    parser.add_argument("--apply",action="store_true")
    args=parser.parse_args()
    if args.from_file:
        assert not args.apply, "Offline source cannot be applied to production"
        docs=json.loads(Path(args.from_file).read_text(encoding="utf-8-sig"))
        before={c["_id"]:c for c in docs}
    else:
        cfg={k.replace("_", "").lower():v for k,v in json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text()).items()}
        base=cfg["couchdbendpoint"].rstrip("/")+"/"+cfg["couchdbdatabasename"]
        auth="Basic "+base64.b64encode((cfg["couchdbusername"]+":"+cfg["couchdbpassword"]).encode()).decode()
        def request(key,doc=None):
            req=urllib.request.Request(base+"/"+urllib.parse.quote(key,safe=""),headers={"Authorization":auth,"Content-Type":"application/json"},
                data=None if doc is None else json.dumps(doc).encode(),method="GET" if doc is None else "PUT")
            with urllib.request.urlopen(req,timeout=30) as response:return json.load(response)
        before={key:request(key) for key in IDS}
    after=plan(before)
    changed={k:v for k,v in after.items() if v!=before[k]}
    print(json.dumps({"write":list(changed),"after":summary(after)},indent=2),flush=True)
    if not args.apply or not changed:return
    backup=Path("/root/config-backups")/("astarella-saucer-rework-"+time.strftime("%Y%m%dT%H%M%SZ",time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup/"before.json").write_text(json.dumps({k:before[k] for k in IDS},indent=2))
    (backup/"planned.json").write_text(json.dumps(after,indent=2))
    # Preserve _rev concurrency guards, and preflight every card before the first write.
    for key in IDS:assert request(key)==before[key], "Catalogue changed during planning: "+key
    written={}
    try:
        for key,doc in changed.items():written[key]=request(key,doc)["rev"]
        for key in IDS:
            saved=request(key)
            assert {k:v for k,v in saved.items() if k!="_rev"}=={k:v for k,v in after[key].items() if k!="_rev"},key
    except Exception:
        # Roll back only our own revisions; never overwrite a concurrent operator edit.
        for key,revision in reversed(list(written.items())):
            old=copy.deepcopy(before[key]);old["_rev"]=revision;request(key,old)
        raise
    print(json.dumps({"backup":str(backup),"verified":list(changed)}))

if __name__=="__main__":main()
