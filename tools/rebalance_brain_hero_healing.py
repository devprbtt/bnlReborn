"""Give every Brains hero a default way to heal teammates.

- Trondson: Sonic Staff alt fire (Echo Pulse) heals allies in its radius for an
  instant 15 HP plus 5 HP/s for 4 s; the pulse now costs 20 ammo (26 with
  Echo-Location, which keeps its +6 penalty).
- Genie: the heavy orb no longer damages anything. It passes through enemies,
  stops on the first teammate it touches, and heals allies within 3 m at
  5 HP/s (Trondson's rate). The Speed Ball orb behaves the same, only faster.
- Tony: the base Caulk Gun heals allied Heroes (3 HP per tick, the former
  Healing Caulk values) and keeps its slow. The Healing Caulk perk, its shop
  item and its gear variant are unlisted and deleted.
- Vander: the Static Gloves beam heals allied Heroes 1 HP per 0.1 s tick
  (10 HP/s, Tony's primary Caulk rate).
- Doc: every Globe Gun globe heals allied Heroes within 2.5 m of where it
  bursts for 6 HP. Doc does not heal herself.

Run on the production host. The default mode is read-only and prints the plan.
``--apply`` creates, updates and deletes revision-checked documents, stores a
private rollback copy, and reads every document back. The catalogue watcher
applies the change without a server restart.

For an offline audit, pass ``--from-file CATALOGUE.json``. This accepts the
normal catalogue array and never connects to CouchDB or writes anything.
"""

import base64
import copy
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

# Trondson
STAFF_IDS = {
    "gear_trondson_sonic_staff": (5, 20),
    "gear_trondson_sonic_staff_echo_location": (11, 26),
}
STAFF_BURST_HEAL = 15
STAFF_REGEN_EFFECT = "effect_hero_trondson_sonic_staff_regen"
BRAINS_REGEN_BUFF = "effect_status_regen_health_brains_5"
BRAINS_REGEN_PER_SECOND = 5
STAFF_REGEN_DURATION = 4

# Genie
ORB_UNIT_IDS = {
    "unit_projectile_djinn_heavy_orb": "effect_hero_djinn_heavy_orb_aoe",
    "unit_projectile_djinn_heavy_orb_speed_ball": "effect_hero_djinn_heavy_orb_aoe_speed_ball",
}
ORB_HEAL_AURA = "effect_hero_djinn_heavy_orb_heal_aura"
ORB_HEAL_RADIUS = 3
ORB_HEAL_INTERVAL = 0.2
ORB_HEAL_PER_TICK = 2  # Doubled by double_brain_hero_healing.py (was 1, Trondson's rate).
ORB_DEATH_IMPACT = "impact_explosion_djinn_orbs_secondary"
SPEED_BALL_TEXT_IDS = ("perk_hero_djinn_speed_ball", "shop_item_perk_hero_djinn_speed_ball")
SPEED_BALL_OLD_TEXT = ("Ifrit's Grudge alt fire moves 100% faster and deals 3 burn damage for 3s / "
                       "Ifrit's Grudge alt fire explosion deals 30% less damage")
SPEED_BALL_NEW_TEXT = "Ifrit's Grudge heavy orb moves 100% faster"
ORB_GEAR_IDS = ("gear_djinn_orbs", "gear_djinn_orbs_speed_ball")
ORB_GEAR_OLD_TEXT = {
    "gear_djinn_orbs": "Explode your enemies with explosive magic orbs! <BINDING_CAST1> to fire fast orbs, "
                       "<BINDING_CAST2> to launch the heavy orb.",
    "gear_djinn_orbs_speed_ball": "Explode your enemies with magic orbs! <BINDING_CAST1> to fire fast orbs, "
                                  "<BINDING_CAST2> to launch the heavy orb.",
}
ORB_GEAR_NEW_TEXT = ("Explode your enemies with explosive magic orbs! <BINDING_CAST1> to fire fast orbs, "
                     "<BINDING_CAST2> to launch a healing orb that stops on the first ally it touches.")

# Tony
CAULK_ID = "gear_engineer_caulk_gun"
CAULK_PLAYER_HEAL = 6  # Doubled from the Healing Caulk value 3.
CAULK_OLD_TEXT = ("Amazing tool that fixes blocks and slows your enemies. Alt-Fire to repair faster at the cost "
                  "of more ammo. Repairing your own turrets buffs their damage output.")
CAULK_NEW_TEXT = ("Amazing tool that fixes blocks, heals allied Heroes and slows your enemies. Alt-Fire to repair "
                  "faster at the cost of more ammo. Repairing your own turrets buffs their damage output.")
HEALING_CAULK_PERK = "perk_hero_engineer_healing_caulk"
HEALING_CAULK_SHOP_ITEM = "shop_item_perk_hero_engineer_healing_caulk"
HEALING_CAULK_GEAR = "gear_engineer_caulk_gun_healing_caulk"
DELETE_IDS = (HEALING_CAULK_SHOP_ITEM, HEALING_CAULK_PERK, HEALING_CAULK_GEAR)

# Vander
GLOVES_ID = "gear_magnus_static_gloves"
GLOVES_BEAM_HEAL = 2  # Doubled from 1.
GLOVES_OLD_TEXT = ("Unleash bolts of lightning that deal moderate damage to enemies or activate Tesla Coils. "
                   "Hold <BINDING_CAST2> to channel a continuous beam of lightning for close range combat.")
GLOVES_NEW_TEXT = ("Unleash bolts of lightning that deal moderate damage to enemies or activate Tesla Coils. "
                   "Hold <BINDING_CAST2> to channel a continuous beam of lightning that damages enemies and "
                   "heals allied Heroes.")

# Doc
GLOBE_IDS = ("gear_doc_eliza_bubble_gun", "gear_doc_eliza_bubble_gun_beautiful_bubbles")
GLOBE_HEAL = 12  # Doubled from 6.
GLOBE_HEAL_RADIUS = 2.5
GLOBE_OLD_TEXT = "Launches globes of corrosive acid in a ballistic arc. <BINDING_CAST2> for a close range lob."
GLOBE_NEW_TEXT = ("Launches globes of corrosive acid in a ballistic arc that heal allied Heroes where they burst. "
                  "<BINDING_CAST2> for a close range lob.")

TEMPLATE_IDS = ("effect_status_regen_health", "effect_status_regen_health_doc_eliza_others")
CREATE_IDS = (BRAINS_REGEN_BUFF, STAFF_REGEN_EFFECT, ORB_HEAL_AURA)
UPDATE_IDS = (tuple(STAFF_IDS) + tuple(ORB_UNIT_IDS) + ORB_GEAR_IDS + SPEED_BALL_TEXT_IDS +
              (CAULK_ID, GLOVES_ID) + GLOBE_IDS + ("global_logic", "shop_logic"))
READ_IDS = CREATE_IDS + UPDATE_IDS + DELETE_IDS + TEMPLATE_IDS


def ally_heal(amount):
    return {
        "type": "heal",
        "interrupt": None,
        "targeting": {
            "affected_labels": None,
            "affected_units": ["player"],
            "affected_team": "friendly",
            "caster_owned_only": False,
            "ignore_caster": True,
        },
        "impact": None,
        "player_heal": amount,
        "world_heal": 0,
        "objective_heal": 0,
        "forcefield_amount": 0,
    }


def is_ally_heal(effect, amount):
    return effect.get("type") == "heal" and effect.get("player_heal") == amount and \
        (effect.get("targeting") or {}).get("affected_team") == "friendly" and \
        (effect.get("targeting") or {}).get("ignore_caster") is True


def set_text(card, field, old, new):
    current = card[field]["text"]
    assert current in (old, new), f"unexpected {field} on {card['_id']}: {current!r}"
    card[field]["text"] = new


def new_card(template, card_id):
    card = copy.deepcopy(template)
    for key in ("_rev", "hercules_metadata"):
        card.pop(key, None)
    card["_id"] = card_id
    return card


def desired_new_cards(docs):
    regen = new_card(docs["effect_status_regen_health"], BRAINS_REGEN_BUFF)
    assert regen["effect"]["type"] == "buff" and regen["effect"]["buffs"] == {"health_regen": 10}
    regen["effect"]["buffs"] = {"health_regen": BRAINS_REGEN_PER_SECOND}
    regen["scores"] = None  # Healing is already credited per HP; don't also award per application.

    staff = new_card(docs["effect_status_regen_health_doc_eliza_others"], STAFF_REGEN_EFFECT)
    assert staff["effect"]["type"] == "self" and staff["effect"]["constant_effects"] == ["effect_status_regen_health"]
    assert staff["effect"]["targeting"]["affected_team"] == "friendly"
    assert staff["effect"]["targeting"]["ignore_caster"] is True
    staff["duration"] = STAFF_REGEN_DURATION
    staff["effect"]["constant_effects"] = [BRAINS_REGEN_BUFF]

    aura = {
        "_id": ORB_HEAL_AURA,
        "scope": "public",
        "category": "effect",
        "prefab_unit": "",
        "prefab_player": "",
        "sound_for_player": None,
        "sound_for_unit": None,
        "gui_info": None,
        "positive": True,
        "interrupt": None,
        "scores": None,
        "duration": None,
        "labels": [],
        "effect": {
            "type": "aura",
            "targeting": {
                "affected_labels": None,
                "affected_units": ["player"],
                "affected_team": "friendly",
                "caster_owned_only": False,
                "ignore_caster": False,
            },
            "outer_radius": ORB_HEAL_RADIUS,
            "inner_radius": None,
            "interval_effects": [{
                "type": "heal",
                "interrupt": None,
                "targeting": None,
                "impact": None,
                "player_heal": ORB_HEAL_PER_TICK,
                "world_heal": 0,
                "objective_heal": 0,
                "forcefield_amount": 0,
            }],
            "interval": ORB_HEAL_INTERVAL,
            "enter_effect": None,
            "leave_effect": None,
            "constant_effects": [],
        },
    }
    return {BRAINS_REGEN_BUFF: regen, STAFF_REGEN_EFFECT: staff, ORB_HEAL_AURA: aura}


def migrate_staff(card):
    old_cost, new_cost = STAFF_IDS[card["_id"]]
    changed = copy.deepcopy(card)
    pulse = changed["tools"][1]
    bunch = pulse["hit_effect"]
    assert pulse["type"] == "shot" and bunch["type"] == "all_units_bunch"
    assert "effect_hero_trondson_sonic_staff_marker" in bunch["constant"]
    assert pulse["ammo"]["rate"] in (old_cost, new_cost), f"unexpected pulse cost on {card['_id']}"
    pulse["ammo"]["rate"] = new_cost
    if not any(is_ally_heal(e, STAFF_BURST_HEAL) for e in bunch["instant"]):
        assert not any(e.get("type") == "heal" for e in bunch["instant"]), f"unexpected heal on {card['_id']}"
        bunch["instant"].append(ally_heal(STAFF_BURST_HEAL))
    if STAFF_REGEN_EFFECT not in bunch["constant"]:
        bunch["constant"].append(STAFF_REGEN_EFFECT)
    set_text(changed, "description",
             "Trondson's trusty Sonic Staff acts as a multi-purpose tool and weapon. <BINDING_CAST1> fires pulses "
             "of destructive sonic energy while <BINDING_CAST2> briefly detects all enemies around Trondson.",
             "Trondson's trusty Sonic Staff acts as a multi-purpose tool and weapon. <BINDING_CAST1> fires pulses "
             "of destructive sonic energy while <BINDING_CAST2> briefly detects all enemies around Trondson and "
             "heals nearby allies.")
    return changed


def migrate_orb_unit(card):
    old_aoe = ORB_UNIT_IDS[card["_id"]]
    changed = copy.deepcopy(card)
    assert changed["init_effects"] in ([old_aoe], [ORB_HEAL_AURA]), f"unexpected orb effects on {card['_id']}"
    changed["init_effects"] = [ORB_HEAL_AURA]
    data = changed["data"]
    assert data["type"] == "projectile" and data["collide_with"] in ("opponent", "friendly")
    assert data["world_collision_effect"] is None and data["player_collision_effect"] is None
    data["collide_with"] = "friendly"
    data["die_on_player_collision"] = False
    death = data["death_effect"]
    assert death["impact"] == ORB_DEATH_IMPACT and death["type"] in ("splash_damage", "bunch")
    data["death_effect"] = {
        "type": "bunch",
        "interrupt": None,
        "targeting": None,
        "impact": ORB_DEATH_IMPACT,
        "break_on_effect_fail": False,
        "instant": [],
        "constant": [],
    }
    return changed


def migrate_orb_gear(card):
    changed = copy.deepcopy(card)
    set_text(changed, "description", ORB_GEAR_OLD_TEXT[card["_id"]], ORB_GEAR_NEW_TEXT)
    return changed


def migrate_speed_ball_text(card):
    changed = copy.deepcopy(card)
    set_text(changed, "description", SPEED_BALL_OLD_TEXT, SPEED_BALL_NEW_TEXT)
    return changed


def migrate_caulk(card, healing_variant):
    changed = copy.deepcopy(card)
    for index, tool in enumerate(changed["tools"]):
        effects = tool["interval_effects"]
        assert tool["type"] == "channel"
        assert any(e["type"] == "bunch" for e in effects), "base Caulk Gun lost its slow"
        if any(is_ally_heal(e, CAULK_PLAYER_HEAL) for e in effects):
            continue
        assert healing_variant is not None, "Healing Caulk variant is gone but the base gun does not heal"
        source = [e for e in healing_variant["tools"][index]["interval_effects"] if is_ally_heal(e, 3)]
        assert len(source) == 1, f"Healing Caulk tool {index} has no single ally heal"
        world_heal = next(i for i, e in enumerate(effects) if e["type"] == "heal")
        effects.insert(world_heal + 1, dict(copy.deepcopy(source[0]), player_heal=CAULK_PLAYER_HEAL))
    set_text(changed, "description", CAULK_OLD_TEXT, CAULK_NEW_TEXT)
    return changed


def migrate_gloves(card):
    changed = copy.deepcopy(card)
    beam = changed["tools"][1]
    assert beam["type"] == "spinup" and beam["timing"]["attack_time"] == 0.1
    instant = beam["hit_effect"]["instant"]
    if not any(is_ally_heal(e, GLOVES_BEAM_HEAL) for e in instant):
        instant.append(ally_heal(GLOVES_BEAM_HEAL))
    set_text(changed, "description", GLOVES_OLD_TEXT, GLOVES_NEW_TEXT)
    return changed


def globe_heal():
    return {
        "type": "all_units_bunch",
        "interrupt": None,
        "targeting": None,
        "impact": None,
        "range": GLOBE_HEAL_RADIUS,
        "break_on_effect_fail": False,
        "instant": [ally_heal(GLOBE_HEAL)],
        "constant": [],
    }


def migrate_globe(card):
    changed = copy.deepcopy(card)
    for tool in changed["tools"]:
        instant = tool["hit_effect"]["instant"]
        assert [e["type"] for e in instant[:2]] == ["splash_damage", "damage"]
        assert instant[0]["radius"] == GLOBE_HEAL_RADIUS
        if globe_heal() not in instant:
            instant.append(globe_heal())
    set_text(changed, "description", GLOBE_OLD_TEXT, GLOBE_NEW_TEXT)
    return changed


def migrate_global_logic(card):
    changed = copy.deepcopy(card)
    perks = changed["perks"]["heroes"]["unit_hero_engineer"]
    if HEALING_CAULK_PERK in perks:
        perks.remove(HEALING_CAULK_PERK)
    return changed


def migrate_shop_logic(card):
    changed = copy.deepcopy(card)
    for category in changed["shop"]["categories"]:
        if HEALING_CAULK_SHOP_ITEM in category["items"]:
            category["items"].remove(HEALING_CAULK_SHOP_ITEM)
    return changed


def plan(docs):
    """docs maps id -> current document or None. Returns (creates, updates, deletes)."""
    for template in TEMPLATE_IDS:
        assert docs[template] is not None, "missing template " + template
    wanted_new = desired_new_cards(docs)
    creates = {}
    for card_id, wanted in wanted_new.items():
        current = docs[card_id]
        if current is None:
            creates[card_id] = wanted
        else:
            body = {k: v for k, v in current.items() if k not in ("_rev", "hercules_metadata")}
            assert body == wanted, f"{card_id} already exists with different content"

    migrators = {card_id: migrate_staff for card_id in STAFF_IDS}
    migrators.update({card_id: migrate_orb_unit for card_id in ORB_UNIT_IDS})
    migrators.update({card_id: migrate_orb_gear for card_id in ORB_GEAR_IDS})
    migrators.update({card_id: migrate_speed_ball_text for card_id in SPEED_BALL_TEXT_IDS})
    migrators.update({card_id: migrate_globe for card_id in GLOBE_IDS})
    migrators[CAULK_ID] = lambda card: migrate_caulk(card, docs[HEALING_CAULK_GEAR])
    migrators[GLOVES_ID] = migrate_gloves
    migrators["global_logic"] = migrate_global_logic
    migrators["shop_logic"] = migrate_shop_logic
    updates = {}
    for card_id in UPDATE_IDS:
        assert docs[card_id] is not None, "missing " + card_id
        after = migrators[card_id](docs[card_id])
        if after != docs[card_id]:
            updates[card_id] = after

    deletes = [card_id for card_id in DELETE_IDS if docs[card_id] is not None]
    return creates, updates, deletes


def references(docs, card_id):
    return sorted(other for other, doc in docs.items()
                  if doc is not None and other != card_id and card_id in json.dumps(doc))


def audit(docs):
    """Verify the finished state; docs maps id -> document or None."""
    for card_id in DELETE_IDS:
        assert docs[card_id] is None, card_id + " still exists"
        dangling = references(docs, card_id)
        assert not dangling, f"{card_id} still referenced by {dangling}"
    regen = docs[BRAINS_REGEN_BUFF]["effect"]["buffs"]["health_regen"]
    staff_effect = docs[STAFF_REGEN_EFFECT]
    assert staff_effect["effect"]["constant_effects"] == [BRAINS_REGEN_BUFF]
    result = {"trondson": {}, "genie": {}, "doc": {}}
    for card_id, (_, cost) in STAFF_IDS.items():
        pulse = docs[card_id]["tools"][1]
        bunch = pulse["hit_effect"]
        assert pulse["ammo"]["rate"] == cost
        assert sum(is_ally_heal(e, STAFF_BURST_HEAL) for e in bunch["instant"]) == 1
        assert STAFF_REGEN_EFFECT in bunch["constant"]
        result["trondson"][card_id] = {"pulse_cost": cost, "radius": bunch["range"], "burst": STAFF_BURST_HEAL,
                                       "regen_per_second": regen, "regen_seconds": staff_effect["duration"]}
    aura = docs[ORB_HEAL_AURA]["effect"]
    orb_rate = aura["interval_effects"][0]["player_heal"] / aura["interval"]
    for card_id in ORB_UNIT_IDS:
        data = docs[card_id]["data"]
        assert docs[card_id]["init_effects"] == [ORB_HEAL_AURA]
        assert data["collide_with"] == "friendly" and data["die_on_player_collision"] is False
        assert "damage" not in json.dumps(data["death_effect"])
        result["genie"][card_id] = {"collide_with": data["collide_with"], "stops_on_ally": True,
                                    "heal_per_second": orb_rate, "radius": aura["outer_radius"],
                                    "max_speed": data["max_speed"]}
    caulk = docs[CAULK_ID]
    assert HEALING_CAULK_PERK not in docs["global_logic"]["perks"]["heroes"]["unit_hero_engineer"]
    assert HEALING_CAULK_SHOP_ITEM not in json.dumps(docs["shop_logic"])
    result["tony"] = {
        f"tool_{i}": {"ally_heal_per_second": round(CAULK_PLAYER_HEAL / t["interval"], 3),
                      "slows": any(e["type"] == "bunch" for e in t["interval_effects"])}
        for i, t in enumerate(caulk["tools"])
        if sum(is_ally_heal(e, CAULK_PLAYER_HEAL) for e in t["interval_effects"]) == 1
    }
    assert len(result["tony"]) == 2
    beam = docs[GLOVES_ID]["tools"][1]
    assert sum(is_ally_heal(e, GLOVES_BEAM_HEAL) for e in beam["hit_effect"]["instant"]) == 1
    result["vander"] = {"beam_heal_per_second": GLOVES_BEAM_HEAL / beam["timing"]["attack_time"]}
    for card_id in GLOBE_IDS:
        for tool in docs[card_id]["tools"]:
            assert tool["hit_effect"]["instant"].count(globe_heal()) == 1
        result["doc"][card_id] = {"heal_per_globe": GLOBE_HEAL, "radius": GLOBE_HEAL_RADIUS}
    return result


def summary(creates, updates, deletes):
    return {"create": list(creates), "update": list(updates), "delete": deletes}


if "--from-file" in sys.argv:
    source = Path(sys.argv[sys.argv.index("--from-file") + 1])
    all_docs = {doc["_id"]: doc for doc in json.loads(source.read_text(encoding="utf-8"))}
    before = {card_id: all_docs.get(card_id) for card_id in READ_IDS}
    creates, updates, deletes = plan(before)
    after = dict(all_docs)
    after.update(creates)
    after.update(updates)
    for card_id in deletes:
        del after[card_id]
    after = {card_id: after.get(card_id) for card_id in after.keys() | set(READ_IDS)}
    print(json.dumps({**summary(creates, updates, deletes), "after": audit(after)}, indent=2))
    sys.exit()

config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
config = {key.replace("_", "").lower(): value for key, value in config.items()}
base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
auth = "Basic " + base64.b64encode(
    (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
).decode()


def request(path, data=None, method=None):
    req = urllib.request.Request(
        base + "/" + path,
        headers={"Authorization": auth, "Content-Type": "application/json"},
        data=None if data is None else json.dumps(data).encode(),
        method=method or ("GET" if data is None else "PUT"),
    )
    with urllib.request.urlopen(req, timeout=60) as response:
        return json.load(response)


def get(card_id):
    try:
        return request(urllib.parse.quote(card_id, safe=""))
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def all_docs():
    rows = request("_all_docs?include_docs=true")["rows"]
    return {row["id"]: row["doc"] for row in rows if not row["id"].startswith("_design")}


before = {card_id: get(card_id) for card_id in READ_IDS}
creates, updates, deletes = plan(before)
projected = all_docs()
projected.update(creates)
projected.update(updates)
for card_id in deletes:
    projected.pop(card_id, None)
projected = {card_id: projected.get(card_id) for card_id in projected.keys() | set(READ_IDS)}
print(json.dumps({**summary(creates, updates, deletes),
                  "revisions": {card_id: (doc or {}).get("_rev") for card_id, doc in before.items()},
                  "after": audit(projected)}, indent=2), flush=True)
if "--apply" not in sys.argv or not (creates or updates or deletes):
    sys.exit()

backup = Path("/root/config-backups") / (
    "brain-hero-healing-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
# New effects first so no card ever references a missing key; unlist the perk
# before deleting it for the same reason.
for card_id, card in creates.items():
    request(urllib.parse.quote(card_id, safe=""), card)
for card_id in UPDATE_IDS:
    if card_id in updates:
        request(urllib.parse.quote(card_id, safe=""), updates[card_id])  # Carries _rev; concurrent edits fail.
tombstones = {}
for card_id in deletes:
    rev = urllib.parse.quote(before[card_id]["_rev"], safe="")
    tombstones[card_id] = request(urllib.parse.quote(card_id, safe="") + "?rev=" + rev, method="DELETE")["rev"]

saved = all_docs()
saved = {card_id: saved.get(card_id) for card_id in saved.keys() | set(READ_IDS)}
for card_id, planned in {**creates, **updates}.items():
    assert {k: v for k, v in saved[card_id].items() if k != "_rev"} == {
        k: v for k, v in planned.items() if k != "_rev"
    }, "readback differs for " + card_id
print(json.dumps({"backup": str(backup),
                  "revisions": {card_id: (saved[card_id] or {}).get("_rev") for card_id in READ_IDS},
                  "tombstones": tombstones,
                  "verified": audit(saved)}, indent=2), flush=True)
