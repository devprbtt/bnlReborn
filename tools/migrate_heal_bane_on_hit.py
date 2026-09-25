"""Turn Heal Bane's perk effect into an on_hit trigger that names its debuff.

Before: effect_perk_heal_bane_pos was {"type":"buff","buffs":{"heal_bane":1}}, a flag the server read to apply
a debuff whose key lived in C#. After: the perk card itself says what a hit applies, like on_kill perks do:
{"type":"on_hit","effect":{"type":"bunch","constant":["effect_heal_bane_debuff"],...}}. Strength and duration
stay on effect_heal_bane_debuff; a variant is a new debuff card plus a perk effect pointing at it.

Run on the production host only after a server that understands on_hit is live (an older one cannot parse
it). Default is read-only and prints the plan. --apply writes the revision-checked document, keeps a private
rollback snapshot and reads it back. No restart: the running server's change watcher reloads the card.
--from-file CATALOGUE.json --print prints the migrated card for offline checks.
"""
import base64, copy, json, sys, time, urllib.error, urllib.request
from pathlib import Path

PERK_EFFECT = "effect_perk_heal_bane_pos"
DEBUFF = "effect_heal_bane_debuff"
ON_HIT = {
    "type": "on_hit",
    "targeting": None,
    "effect": {
        "type": "bunch",
        "interrupt": None,
        "targeting": {"affected_labels": None, "affected_units": ["player"], "affected_team": "opponent",
                      "caster_owned_only": False, "ignore_caster": True},
        "impact": None,
        "break_on_effect_fail": False,
        "instant": [],
        "constant": [DEBUFF],
    },
}


def migrate(perk):
    """Returns the migrated card, the same card if already migrated, or raises if it is not the known shape."""
    if perk["effect"] == ON_HIT:
        return perk
    assert perk["effect"] == {"type": "buff", "targeting": None, "buffs": {"heal_bane": 1}}, \
        "unexpected current effect: " + json.dumps(perk["effect"])
    new = copy.deepcopy(perk)
    new["effect"] = copy.deepcopy(ON_HIT)
    return new


if "--from-file" in sys.argv:
    docs = {d["_id"]: d for d in json.loads(Path(sys.argv[sys.argv.index("--from-file") + 1]).read_text(encoding="utf-8"))}
    assert docs[DEBUFF]["category"] == "effect"
    print(json.dumps({PERK_EFFECT: migrate(docs[PERK_EFFECT])}, indent=1))
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


before = request(PERK_EFFECT)
debuff = request(DEBUFF)
assert before["category"] == "effect" and debuff["category"] == "effect"
after = migrate(before)
print(json.dumps({"change": after != before, "before": before["effect"], "after": after["effect"],
                  "debuff": {"duration": debuff.get("duration"), "buffs": debuff["effect"].get("buffs")}}), flush=True)
if "--apply" not in sys.argv or after == before:
    sys.exit()

backup = Path("/root/config-backups") / ("heal-bane-on-hit-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps({PERK_EFFECT: before, DEBUFF: debuff}, indent=2))
request(PERK_EFFECT, after)  # carries before's _rev: a concurrent edit fails instead of being overwritten
saved = request(PERK_EFFECT)
assert {k: v for k, v in saved.items() if k != "_rev"} == {k: v for k, v in after.items() if k != "_rev"}, "readback differs"
assert request(DEBUFF) == debuff, "debuff changed"
print(json.dumps({"backup": str(backup), "revision": saved["_rev"]}), flush=True)
