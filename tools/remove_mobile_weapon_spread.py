"""Remove movement/jump cone modifiers from public hero weapons except Nigel.

Dry-run by default; --from-file is offline only. --apply requires deployment
authorization and writes revision-guarded cards with a private rollback backup.
"""
import argparse
import base64
import copy
import json
import math
import time
import urllib.parse
import urllib.request
from pathlib import Path

NIGEL = "unit_hero_hunter"


def cones(card):
    if card.get("cone_of_fire") is not None:
        yield "cone_of_fire", card["cone_of_fire"]
    for index, tool in enumerate(card.get("tools") or []):
        if tool.get("cone_of_fire") is not None:
            yield f"tools[{index}].cone_of_fire", tool["cone_of_fire"]


def plan(cards):
    heroes = {key: card for key, card in cards.items()
              if key.startswith("unit_hero_") and card.get("scope") == "public"
              and card.get("data", {}).get("type") == "player"}
    if NIGEL not in heroes:
        raise ValueError("Nigel hero card missing; refusing an incomplete catalogue")
    families = {gear for hero in heroes.values() for gear in hero["data"]["gears"]}
    protected = set(heroes[NIGEL]["data"]["gears"])

    def matches(key, bases):
        return any(key == base or key.startswith(base + "_") for base in bases)

    after = copy.deepcopy(cards)
    for key, card in after.items():
        if card.get("category") != "gear" or card.get("scope") != "public":
            continue
        if not matches(key, families) or matches(key, protected) or key.startswith("gear_hunter_"):
            continue
        for path, cone in cones(card):
            for field in ("move_mod", "jump_mod"):
                value = cone.get(field)
                if not isinstance(value, (int, float)) or not math.isfinite(value):
                    raise ValueError(f"Invalid {key}.{path}.{field}")
                cone[field] = 0
    return after


def changes(before, after):
    return {key: [{"path": path, "before": dict(cones(before[key]))[path], "after": cone}
                  for path, cone in cones(after[key]) if dict(cones(before[key]))[path] != cone]
            for key in after if before[key] != after[key]}


def apply(request, before, after, backup_root):
    changed = {key: doc for key, doc in after.items() if doc != before[key]}
    if not changed:
        return None
    backup = backup_root / ("mobile-weapon-spread-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps({k: before[k] for k in changed}, indent=2))
    (backup / "planned.json").write_text(json.dumps(changed, indent=2))
    # Check the whole public snapshot before writing: hero/variant selection may have changed.
    current = {row["doc"]["_id"]: row["doc"] for row in request("_all_docs?include_docs=true")["rows"]
               if row.get("doc", {}).get("scope") == "public"}
    if current != before:
        raise RuntimeError("Catalogue changed during planning")
    written = {}
    try:
        for key, doc in changed.items():
            written[key] = request(key, doc)["rev"]
        for key, doc in changed.items():
            saved = request(key)
            if {k: v for k, v in saved.items() if k != "_rev"} != {k: v for k, v in doc.items() if k != "_rev"}:
                raise RuntimeError("Read-back mismatch: " + key)
    except Exception:
        # Preserve concurrent edits: restore only our own successful revision.
        for key, revision in reversed(list(written.items())):
            old = copy.deepcopy(before[key])
            old["_rev"] = revision
            request(key, old)
        raise
    return str(backup)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from-file")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    if args.from_file:
        if args.apply:
            parser.error("Offline source cannot be applied to production")
        docs = json.loads(Path(args.from_file).read_text(encoding="utf-8-sig"))
    else:
        cfg = {k.replace("_", "").lower(): v for k, v in json.loads(
            Path("/opt/bnlreloaded/current/Configs/configs.json").read_text()).items()}
        base = cfg["couchdbendpoint"].rstrip("/") + "/" + cfg["couchdbdatabasename"]
        auth = "Basic " + base64.b64encode((cfg["couchdbusername"] + ":" + cfg["couchdbpassword"]).encode()).decode()

        def request(key, doc=None):
            path = key if key.startswith("_all_docs?") else urllib.parse.quote(key, safe="")
            req = urllib.request.Request(base + "/" + path,
                headers={"Authorization": auth, "Content-Type": "application/json"},
                data=None if doc is None else json.dumps(doc).encode(), method="GET" if doc is None else "PUT")
            with urllib.request.urlopen(req, timeout=30) as response:
                return json.load(response)

        docs = [row["doc"] for row in request("_all_docs?include_docs=true")["rows"]
                if row.get("doc", {}).get("scope") == "public"]
    before = {doc["_id"]: doc for doc in docs}
    after = plan(before)
    delta = changes(before, after)
    print(json.dumps({"changed_cards": len(delta), "changes": delta}, indent=2), flush=True)
    if args.apply:
        print(json.dumps({"backup": apply(request, before, after, Path("/root/config-backups")),
                          "verified": list(delta)}))


if __name__ == "__main__":
    main()
