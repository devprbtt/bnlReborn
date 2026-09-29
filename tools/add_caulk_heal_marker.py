"""Show a buff icon on teammates healed by Tony's Caulk Gun.

The ally heal is an instant heal per tick, which has no HUD icon. Each tick now
also applies effect_hero_engineer_caulk_heal_marker to the healed teammate: a
0.5 s positive wrapper with no gameplay effect, shaped like Trondson's regen
wrapper and using its icon (gameplayeffects_regenhealth). It is applied through
a friendly-player bunch (caster ignored), mirroring the enemy slow bunch, on
both tools of the base gun and the Sticky Caulk variant.

Read-only by default; ``--apply`` writes revision-checked documents with a
private backup and readback. Re-running plans nothing.
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

MARKER_ID = "effect_hero_engineer_caulk_heal_marker"
GEAR_IDS = ("gear_engineer_caulk_gun", "gear_engineer_caulk_gun_sticky_caulk")
TEMPLATE_ID = "effect_hero_trondson_sonic_staff_regen"
DURATION = 0.5

FRIENDLY_PLAYERS = {"affected_labels": None, "affected_units": ["player"], "affected_team": "friendly",
                    "caster_owned_only": False, "ignore_caster": True}


def marker_bunch():
    return {"type": "bunch", "interrupt": None, "targeting": copy.deepcopy(FRIENDLY_PLAYERS), "impact": None,
            "break_on_effect_fail": False, "instant": [], "constant": [MARKER_ID]}


def is_marker_bunch(effect):
    return effect.get("type") == "bunch" and effect.get("constant") == [MARKER_ID]


def plan(cards):
    template = cards[TEMPLATE_ID]
    marker = copy.deepcopy(cards.get(MARKER_ID) or {})
    marker.update({
        "_id": MARKER_ID, "scope": "public", "prefab_unit": "", "prefab_player": "",
        "sound_for_player": None, "sound_for_unit": None, "gui_info": copy.deepcopy(template["gui_info"]),
        "positive": True, "interrupt": None, "scores": None, "duration": DURATION, "labels": ["heal"],
        "effect": {"type": "self", "targeting": copy.deepcopy(FRIENDLY_PLAYERS), "interval_effects": [],
                   "interval": 0, "constant_effects": []},
        "category": "effect",
    })
    assert marker["gui_info"]["icon"] == "gameplayeffects_regenhealth"
    result = {MARKER_ID: marker}
    for gear_id in GEAR_IDS:
        gear = copy.deepcopy(cards[gear_id])
        for tool in gear["tools"]:
            effects = tool["interval_effects"]
            if not any(map(is_marker_bunch, effects)):
                ally_heals = [i for i, e in enumerate(effects) if e.get("type") == "heal"
                              and (e.get("targeting") or {}).get("affected_units") == ["player"]]
                assert len(ally_heals) == 1, gear_id + " tool without a single ally heal"
                effects.insert(ally_heals[0] + 1, marker_bunch())
        result[gear_id] = gear
    return result


def audit(cards):
    result = {"marker_duration": cards[MARKER_ID]["duration"], "marker_icon": cards[MARKER_ID]["gui_info"]["icon"]}
    for gear_id in GEAR_IDS:
        result[gear_id] = [sum(map(is_marker_bunch, t["interval_effects"])) for t in cards[gear_id]["tools"]]
        assert result[gear_id] == [1, 1]
    return result


config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
config = {key.replace("_", "").lower(): value for key, value in config.items()}
base_url = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
auth = "Basic " + base64.b64encode(
    (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
).decode()


def request(name, data=None):
    req = urllib.request.Request(
        base_url + "/" + urllib.parse.quote(name, safe=""),
        headers={"Authorization": auth, "Content-Type": "application/json"},
        data=None if data is None else json.dumps(data).encode(),
        method="GET" if data is None else "PUT",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if data is None and error.code == 404:
            return None
        raise


IDS = (MARKER_ID, TEMPLATE_ID) + GEAR_IDS
before = {card_id: request(card_id) for card_id in IDS}
before = {k: v for k, v in before.items() if v is not None}
after = plan(before)  # Marker first, so the guns never reference a missing card.
changes = {k: v for k, v in after.items() if v != before.get(k)}
print(json.dumps({"write": list(changes), "revisions": {k: v.get("_rev") for k, v in before.items()},
                  "after": audit({**before, **after})}, indent=2), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("caulk-heal-marker-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
for card_id, card in changes.items():
    request(card_id, card)
saved = {card_id: request(card_id) for card_id in after}
for card_id, planned in after.items():
    assert {k: v for k, v in saved[card_id].items() if k != "_rev"} == \
        {k: v for k, v in planned.items() if k != "_rev"}, "readback differs for " + card_id
print(json.dumps({"backup": str(backup), "revisions": {k: v["_rev"] for k, v in saved.items()},
                  "verified": audit({**before, **saved})}, indent=2), flush=True)
