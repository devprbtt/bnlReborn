"""Raise Nigel's base-rifle headshot to the standard Brawn two-shot breakpoint.

The base Nellie body shot remains 40.  Its critical modifier changes from 1.5
to 2.1, making a full-damage headshot 84 and two headshots 168.  That is 105%
of the 160 HP used by standard Brawn heroes, leaving 8 HP of healing headroom.

Run on the production host.  The default mode is read-only and prints the
plan.  ``--apply`` writes the revision-checked rifle document, saves a private
rollback copy, and reads the result back.  The catalogue watcher makes an
applied change available without a server restart.

For an offline audit, pass ``--from-file CATALOGUE.json``.  This accepts the
normal catalogue array and never connects to CouchDB or writes anything.
"""

import base64
import copy
import json
import math
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

RIFLE = "gear_hunter_rifle"
REFERENCE_BRAWN = "unit_hero_boxer"
HIGH_CALIBER_RIFLE = "gear_hunter_rifle_high_caliber_rounds"
EXPECTED_BODY_DAMAGE = 40
OLD_CRIT_MODIFIER = 1.5
NEW_CRIT_MODIFIER = 2.1
STANDARD_BRAWN_HEALTH = 160
HEADSHOT_COUNT = 2
EXTRA_DAMAGE_FRACTION = 0.05


def shot_effect(card):
    assert card["category"] == "gear", f"{card.get('_id')} is not gear"
    assert card["prefab"] == "sniper_rifle", f"{card.get('_id')} is not Nigel's rifle"
    tools = card["tools"]
    assert tools and tools[0]["type"] == "shot", "unexpected primary tool"
    effect = tools[0]["hit_effect"]
    assert effect["type"] == "damage", "unexpected primary hit effect"
    return effect


def migrate(rifle):
    """Return the guarded balance update, or the input if it is already applied."""
    effect = shot_effect(rifle)
    damage = effect["damage"]
    assert damage["player_damage"] == EXPECTED_BODY_DAMAGE, \
        f"unexpected body damage: {damage['player_damage']}"
    if math.isclose(effect["crit_modifier"], NEW_CRIT_MODIFIER):
        return rifle
    assert math.isclose(effect["crit_modifier"], OLD_CRIT_MODIFIER), \
        f"unexpected critical modifier: {effect['crit_modifier']}"
    changed = copy.deepcopy(rifle)
    shot_effect(changed)["crit_modifier"] = NEW_CRIT_MODIFIER
    return changed


def audit(rifle, brawn, high_caliber=None):
    health = brawn["health"]["health"]
    assert brawn["category"] == "unit" and brawn["data"]["class"] == "hero_class_brawn"
    assert health["max_health"] == STANDARD_BRAWN_HEALTH and health["toughness"] == 0
    effect = shot_effect(rifle)
    per_headshot = effect["damage"]["player_damage"] * effect["crit_modifier"]
    two_headshots = per_headshot * HEADSHOT_COUNT
    required = STANDARD_BRAWN_HEALTH * (1 + EXTRA_DAMAGE_FRACTION)
    assert math.isclose(two_headshots, required), (two_headshots, required)
    result = {
        "body_shot": effect["damage"]["player_damage"],
        "crit_modifier": effect["crit_modifier"],
        "headshot": per_headshot,
        "two_headshots": two_headshots,
        "standard_brawn_health": STANDARD_BRAWN_HEALTH,
        "healing_headroom": two_headshots - STANDARD_BRAWN_HEALTH,
        "full_damage_range": effect["falloff"]["max_damage_range"],
    }
    if high_caliber is not None:
        high_effect = shot_effect(high_caliber)
        result["high_caliber_headshot_unchanged"] = (
            high_effect["damage"]["player_damage"] * high_effect["crit_modifier"]
        )
    return result


def load_offline(path):
    docs = {doc["_id"]: doc for doc in json.loads(Path(path).read_text(encoding="utf-8"))}
    before = docs[RIFLE]
    after = migrate(before)
    return before, after, docs[REFERENCE_BRAWN], docs.get(HIGH_CALIBER_RIFLE)


if "--from-file" in sys.argv:
    source = sys.argv[sys.argv.index("--from-file") + 1]
    before, after, brawn, high_caliber = load_offline(source)
    print(json.dumps({
        "change": before != after,
        "before_crit_modifier": shot_effect(before)["crit_modifier"],
        "after": audit(after, brawn, high_caliber),
    }, indent=2))
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


def fetch(name):
    try:
        return request(name)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


before = request(RIFLE)
brawn = request(REFERENCE_BRAWN)
# Some historical catalogues contain this variant, while the current live
# catalogue does not.  Audit it when present without making it a prerequisite.
high_caliber = fetch(HIGH_CALIBER_RIFLE)
after = migrate(before)
plan = {
    "change": before != after,
    "rifle_revision": before["_rev"],
    "before_crit_modifier": shot_effect(before)["crit_modifier"],
    "after": audit(after, brawn, high_caliber),
}
print(json.dumps(plan, indent=2), flush=True)
if "--apply" not in sys.argv or after == before:
    sys.exit()

backup = Path("/root/config-backups") / (
    "nigel-brawn-headshots-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(
    json.dumps({RIFLE: before, REFERENCE_BRAWN: brawn, HIGH_CALIBER_RIFLE: high_caliber}, indent=2)
)
request(RIFLE, after)  # Carries _rev: a concurrent edit fails instead of being overwritten.
saved = request(RIFLE)
assert {key: value for key, value in saved.items() if key != "_rev"} == {
    key: value for key, value in after.items() if key != "_rev"
}, "rifle readback differs"
assert request(REFERENCE_BRAWN) == brawn, "reference Brawn changed"
if high_caliber is not None:
    assert request(HIGH_CALIBER_RIFLE) == high_caliber, "High Caliber rifle changed"
print(json.dumps({
    "backup": str(backup),
    "revision": saved["_rev"],
    "verified": audit(saved, brawn, high_caliber),
}, indent=2), flush=True)
