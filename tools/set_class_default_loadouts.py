"""Set the five regular default blocks for every playable hero by class.

Dry-run by default. --from-file reads a catalogue dump without connecting;
--write-after writes an offline candidate. --apply updates CouchDB on the server
with revision guards, a backup and readback verification. Saved player loadouts
and each hero's special devices are deliberately outside this migration.
"""
import argparse
import base64
import copy
import json
import time
import urllib.parse
import urllib.request
from pathlib import Path


DEFAULTS = {
    "brawn": ["device_block_crate", "device_block_brick", "device_generic_landmine",
              "device_generic_respawn_point", "device_generic_radar"],
    "skills": ["device_block_crate", "device_generic_bomb", "device_generic_bear_trap",
               "device_block_bounce", "device_block_fast_movement"],
    "brains": ["device_block_crate", "device_block_brick", "device_generic_fire_trap",
               "device_generic_heal_station", "device_generic_ammo_resupply"],
}


def index_cards(value):
    if isinstance(value, dict) and "rows" in value:
        value = [row["doc"] for row in value["rows"] if "doc" in row]
    if isinstance(value, dict):
        value = list(value.values())
    result = {}
    for card in value:
        key = card.get("_id", card.get("id"))
        if not key or key in result:
            raise ValueError("Missing or duplicate card id: " + str(key))
        result[key] = card
    return result


def plan(cards):
    for key in {key for devices in DEFAULTS.values() for key in devices}:
        if cards.get(key, {}).get("category") != "device":
            raise ValueError("Required device missing or wrong category: " + key)
    after = copy.deepcopy(cards)
    writes, audit = {}, []
    for key, hero in cards.items():
        data = hero.get("data") or {}
        if not key.startswith("unit_hero_") or hero.get("category") != "unit" or data.get("type") != "player":
            continue
        class_key = data.get("class")
        class_card = cards.get(class_key, {})
        kind = class_card.get("type")
        if class_card.get("category") != "hero_class" or kind not in DEFAULTS:
            raise ValueError("Unrecognized class for " + key + ": " + str(class_key))
        desired = DEFAULTS[kind]
        audit.append({"hero": key, "class": kind,
                      "before": data.get("default_devices"), "after": desired})
        if data.get("default_devices") != desired:
            changed = copy.deepcopy(hero)
            changed["data"]["default_devices"] = desired.copy()
            writes[key] = changed
            after[key] = changed
    if not audit:
        raise ValueError("No player hero cards found")
    return writes, after, audit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from-file", type=Path)
    parser.add_argument("--write-after", type=Path)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--config", type=Path, default=Path("/opt/bnlreloaded/current/Configs/configs.json"))
    parser.add_argument("--backup-dir", type=Path, default=Path("/root/config-backups"))
    args = parser.parse_args()
    if args.from_file and args.apply:
        parser.error("--from-file cannot be combined with --apply")
    if args.write_after and not args.from_file:
        parser.error("--write-after requires --from-file")
    if args.from_file:
        cards = index_cards(json.loads(args.from_file.read_text(encoding="utf-8-sig")))
    else:
        config = {k.replace("_", "").lower(): v for k, v in json.loads(args.config.read_text()).items()}
        base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
        auth = "Basic " + base64.b64encode((config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()).decode()

        def request(key, data=None, query=""):
            req = urllib.request.Request(base + "/" + urllib.parse.quote(key, safe="") + query,
                headers={"Authorization": auth, "Content-Type": "application/json"},
                data=None if data is None else json.dumps(data).encode(),
                method="GET" if data is None else "PUT")
            with urllib.request.urlopen(req, timeout=30) as response:
                return json.load(response)

        cards = index_cards(request("_all_docs", query="?include_docs=true"))
    writes, after, audit = plan(cards)
    print(json.dumps({"write": sorted(writes), "heroes": audit}, indent=2), flush=True)
    if args.write_after:
        args.write_after.write_text(json.dumps(list(after.values()), ensure_ascii=False), encoding="utf-8")
    if not args.apply or not writes:
        return
    backup = args.backup_dir / ("class-default-loadouts-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700, parents=True)
    (backup / "before.json").write_text(json.dumps([cards[key] for key in writes], indent=2), encoding="utf-8")
    # Preserve each _rev. On conflict, stop: already completed writes are safe to
    # keep, and rerunning recalculates the remaining plan from current documents.
    for key, card in writes.items():
        if not card.get("_rev"):
            raise ValueError("Missing revision for " + key)
        request(key, card)
        saved = request(key)
        if {k: v for k, v in saved.items() if k != "_rev"} != {k: v for k, v in card.items() if k != "_rev"}:
            raise RuntimeError("Readback differs for " + key)
    print(json.dumps({"verified": len(writes), "backup": str(backup)}), flush=True)


if __name__ == "__main__":
    main()
