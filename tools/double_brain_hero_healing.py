"""Double the ally healing added by rebalance_brain_hero_healing.py.

Tony's Caulk Gun 3 -> 6 HP per tick (20/40 HP/s), Vander's beam 1 -> 2 HP per
0.1 s tick (20 HP/s), Genie's heavy orb aura 1 -> 2 HP per 0.2 s (10 HP/s) and
Doc's Globe Gun burst 6 -> 12 HP. Trondson is unchanged, so Genie's orb no
longer matches his 5 HP/s regen.

Same modes as the original tool: read-only by default, ``--apply`` writes
revision-checked documents with a private backup and readback, and
``--from-file CATALOGUE.json`` audits offline.
"""

import base64
import copy
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

CAULK_ID = "gear_engineer_caulk_gun"
GLOVES_ID = "gear_magnus_static_gloves"
ORB_HEAL_AURA = "effect_hero_djinn_heavy_orb_heal_aura"
GLOBE_IDS = ("gear_doc_eliza_bubble_gun", "gear_doc_eliza_bubble_gun_beautiful_bubbles")
CARD_IDS = (CAULK_ID, GLOVES_ID, ORB_HEAL_AURA) + GLOBE_IDS
CAULK = (3, 6)
BEAM = (1, 2)
ORB = (1, 2)
GLOBE = (6, 12)


def is_ally_heal(effect):
    targeting = effect.get("targeting") or {}
    return effect.get("type") == "heal" and targeting.get("affected_team") == "friendly" and \
        targeting.get("affected_units") == ["player"] and targeting.get("ignore_caster") is True


def bump(effect, old_new, where):
    old, new = old_new
    assert effect["player_heal"] in (old, new), f"unexpected heal {effect['player_heal']} in {where}"
    effect["player_heal"] = new


def migrate(card):
    changed = copy.deepcopy(card)
    card_id = card["_id"]
    if card_id == CAULK_ID:
        for tool in changed["tools"]:
            heals = [e for e in tool["interval_effects"] if is_ally_heal(e)]
            assert len(heals) == 1, "Caulk Gun tool without a single ally heal"
            bump(heals[0], CAULK, card_id)
    elif card_id == GLOVES_ID:
        heals = [e for e in changed["tools"][1]["hit_effect"]["instant"] if is_ally_heal(e)]
        assert len(heals) == 1, "beam without a single ally heal"
        bump(heals[0], BEAM, card_id)
    elif card_id == ORB_HEAL_AURA:
        effect = changed["effect"]
        assert effect["type"] == "aura" and effect["interval"] == 0.2 and len(effect["interval_effects"]) == 1
        bump(effect["interval_effects"][0], ORB, card_id)
    else:
        for tool in changed["tools"]:
            bunches = [e for e in tool["hit_effect"]["instant"] if e["type"] == "all_units_bunch"]
            assert len(bunches) == 1 and bunches[0]["range"] == 2.5 and len(bunches[0]["instant"]) == 1
            assert is_ally_heal(bunches[0]["instant"][0])
            bump(bunches[0]["instant"][0], GLOBE, card_id)
    return changed


def audit(cards):
    caulk = cards[CAULK_ID]["tools"]
    beam = cards[GLOVES_ID]["tools"][1]
    aura = cards[ORB_HEAL_AURA]["effect"]
    result = {
        "tony_hp_per_second": [next(e for e in t["interval_effects"] if is_ally_heal(e))["player_heal"] / t["interval"]
                               for t in caulk],
        "vander_hp_per_second": next(e for e in beam["hit_effect"]["instant"] if is_ally_heal(e))["player_heal"]
                                / beam["timing"]["attack_time"],
        "genie_hp_per_second": aura["interval_effects"][0]["player_heal"] / aura["interval"],
        "doc_hp_per_globe": {card_id: [next(e for e in t["hit_effect"]["instant"] if e["type"] == "all_units_bunch")
                                       ["instant"][0]["player_heal"] for t in cards[card_id]["tools"]]
                             for card_id in GLOBE_IDS},
    }
    assert [round(v, 6) for v in result["tony_hp_per_second"]] == [20, 40]
    assert round(result["vander_hp_per_second"], 6) == 20 and round(result["genie_hp_per_second"], 6) == 10
    assert all(v == [GLOBE[1], GLOBE[1]] for v in result["doc_hp_per_globe"].values())
    return result


if "--from-file" in sys.argv:
    source = Path(sys.argv[sys.argv.index("--from-file") + 1])
    all_docs = {doc["_id"]: doc for doc in json.loads(source.read_text(encoding="utf-8"))}
    before = {card_id: all_docs[card_id] for card_id in CARD_IDS}
    after = {card_id: migrate(card) for card_id, card in before.items()}
    print(json.dumps({"write": [c for c in CARD_IDS if before[c] != after[c]], "after": audit(after)}, indent=2))
    sys.exit()

config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
config = {key.replace("_", "").lower(): value for key, value in config.items()}
base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
auth = "Basic " + base64.b64encode(
    (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
).decode()


def request(name, data=None):
    req = urllib.request.Request(
        base + "/" + urllib.parse.quote(name, safe=""),
        headers={"Authorization": auth, "Content-Type": "application/json"},
        data=None if data is None else json.dumps(data).encode(),
        method="GET" if data is None else "PUT",
    )
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)


before = {card_id: request(card_id) for card_id in CARD_IDS}
after = {card_id: migrate(card) for card_id, card in before.items()}
changes = {card_id: card for card_id, card in after.items() if card != before[card_id]}
print(json.dumps({"write": list(changes), "revisions": {c: before[c]["_rev"] for c in CARD_IDS},
                  "after": audit(after)}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("brain-hero-healing-double-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for card_id in CARD_IDS:
    if card_id in changes:
        request(card_id, changes[card_id])  # Carries _rev; concurrent edits fail.
saved = {card_id: request(card_id) for card_id in CARD_IDS}
for card_id, planned in after.items():
    assert {k: v for k, v in saved[card_id].items() if k != "_rev"} == \
        {k: v for k, v in planned.items() if k != "_rev"}, "readback differs for " + card_id
print(json.dumps({"backup": str(backup), "revisions": {c: saved[c]["_rev"] for c in CARD_IDS},
                  "verified": audit(saved)}, indent=2), flush=True)
