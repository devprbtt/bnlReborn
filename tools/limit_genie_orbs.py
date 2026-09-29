"""Limit each Genie to two heavy orbs: a third removes her oldest.

Sets count_limit {limit 2, scope owner} on both heavy-orb projectile units (base and Speed Ball). The base
card already carries it, but projectile units ignored count_limit until the server fix on
fix/genie-orb-limit, so run this only once that server is live. Everything else on the cards is unchanged.

Run on the production host. Default is read-only and prints the plan. --apply writes each changed card with
its current revision (a concurrent editor save fails instead of being overwritten), keeps a private backup
and reads every card back. A re-run is a no-op. No restart: the server's change watcher reloads cards.
"""
import base64, json, sys, time, urllib.request
from pathlib import Path

ORBS = ["unit_projectile_djinn_heavy_orb", "unit_projectile_djinn_heavy_orb_speed_ball"]
LIMIT = {"limit": 2, "scope": "owner", "drop_last": False}

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


before = {name: request(name) for name in ORBS}
for name, card in before.items():
    assert card["category"] == "unit" and card["data"]["type"] == "projectile", name + " is not a projectile unit"
changes = {name: dict(card, count_limit=LIMIT) for name, card in before.items() if card.get("count_limit") != LIMIT}
print(json.dumps({"planned": {name: {"before": before[name].get("count_limit"), "after": LIMIT} for name in changes}}), flush=True)
if "--apply" not in sys.argv or not changes:
    sys.exit()

backup = Path("/root/config-backups") / ("genie-orb-limit-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
backup.mkdir(mode=0o700)
(backup / "before.json").write_text(json.dumps(before, indent=2))
revisions = {}
for name, after in changes.items():
    request(name, after)
    saved = request(name)
    assert {k: v for k, v in saved.items() if k != "_rev"} == {k: v for k, v in after.items() if k != "_rev"}, name + " readback differs"
    revisions[name] = saved["_rev"]
for name in ORBS:
    if name not in changes:
        assert request(name) == before[name], name + " changed unexpectedly"
print(json.dumps({"backup": str(backup), "revisions": revisions}), flush=True)
