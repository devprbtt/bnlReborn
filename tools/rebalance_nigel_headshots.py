"""Set Nigel's base-rifle full-damage headshot to exactly 70 damage.

The base Nellie body shot remains 40.  Its critical modifier changes from 1.5
to 1.75, making a full-damage headshot exactly 70.

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
import urllib.parse
import urllib.request
from pathlib import Path

RIFLE = "gear_hunter_rifle"
EXPECTED_BODY_DAMAGE = 40
OLD_CRIT_MODIFIER = 1.5
NEW_CRIT_MODIFIER = 1.75
TARGET_HEADSHOT_DAMAGE = 70


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


def audit(rifle):
    effect = shot_effect(rifle)
    per_headshot = effect["damage"]["player_damage"] * effect["crit_modifier"]
    assert math.isclose(per_headshot, TARGET_HEADSHOT_DAMAGE), per_headshot
    return {
        "body_shot": effect["damage"]["player_damage"],
        "crit_modifier": effect["crit_modifier"],
        "headshot": per_headshot,
        "full_damage_range": effect["falloff"]["max_damage_range"],
    }


def load_offline(path):
    docs = {doc["_id"]: doc for doc in json.loads(Path(path).read_text(encoding="utf-8"))}
    before = docs[RIFLE]
    after = migrate(before)
    return before, after


if "--from-file" in sys.argv:
    source = sys.argv[sys.argv.index("--from-file") + 1]
    before, after = load_offline(source)
    print(json.dumps({
        "change": before != after,
        "before_crit_modifier": shot_effect(before)["crit_modifier"],
        "after": audit(after),
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


before = request(RIFLE)
after = migrate(before)
plan = {
    "change": before != after,
    "rifle_revision": before["_rev"],
    "before_crit_modifier": shot_effect(before)["crit_modifier"],
    "after": audit(after),
}
print(json.dumps(plan, indent=2), flush=True)
if "--apply" not in sys.argv or after == before:
    sys.exit()

backup = Path("/root/config-backups") / (
    "nigel-70-headshots-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
)
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(
    json.dumps({RIFLE: before}, indent=2)
)
request(RIFLE, after)  # Carries _rev: a concurrent edit fails instead of being overwritten.
saved = request(RIFLE)
assert {key: value for key, value in saved.items() if key != "_rev"} == {
    key: value for key, value in after.items() if key != "_rev"
}, "rifle readback differs"
print(json.dumps({
    "backup": str(backup),
    "revision": saved["_rev"],
    "verified": audit(saved),
}, indent=2), flush=True)
