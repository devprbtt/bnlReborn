"""Make the two released custom skins public. Run on the VPS; dry-run by default.

Only scope changes. Existing card IDs, art routes and hero lists are preserved.
Archive redundant private grants before removing them, so the existing startup
check does not report public items as invalid grants. No server restart needed.
"""
import argparse
import base64
import copy
import json
import os
import sqlite3
import time
import urllib.request
from pathlib import Path

SKINS = {
    "skin_hunter_arctic_wolf_private": "unit_hero_hunter",
    "skin_boxer_demon_private": "unit_hero_boxer",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    os.umask(0o077)
    root = Path("/opt/bnlreloaded/current")
    config = json.loads((root / "Configs/configs.json").read_text())
    config = {k.replace("_", "").lower(): v for k, v in config.items()}
    base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
    auth = "Basic " + base64.b64encode(
        (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()).decode()

    def request(name, data=None):
        req = urllib.request.Request(
            base + "/" + name,
            headers={"Authorization": auth, "Content-Type": "application/json"},
            data=None if data is None else json.dumps(data).encode(),
            method="GET" if data is None else "PUT")
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)

    before = {name: request(name) for name in SKINS}
    heroes = {hero: request(hero) for hero in SKINS.values()}
    for name, card in before.items():
        assert card["category"] == "skin" and card["scope"] in ("private", "public")
        assert card["hero_key"] == SKINS[name]
        assert name in heroes[SKINS[name]]["data"]["skins"]
    db_path = (root / "PlayerData/playerData.db").resolve(strict=True)
    db = sqlite3.connect(db_path.as_uri() + ("?mode=rw" if args.apply else "?mode=ro"), uri=True, timeout=30)
    db.row_factory = sqlite3.Row
    placeholders = ",".join("?" for _ in SKINS)
    select = f"SELECT * FROM InventoryGrants WHERE item IN ({placeholders})"
    assert db.execute("SELECT 1 FROM AppliedMigrations WHERE name=?",
                      ("private_skin_grants_file_v1",)).fetchone(), "Legacy import has not completed"
    changes = {name: dict(card, scope="public") for name, card in before.items() if card["scope"] != "public"}
    grant_count = len(db.execute(select, tuple(SKINS)).fetchall())
    print(json.dumps({"scope_changes": {name: "public" for name in changes},
                      "redundant_grants_to_archive": grant_count,
                      "all_current_and_future_players": True}), flush=True)
    if not args.apply:
        db.close()
        return
    backup = Path("/root/config-backups") / ("public-custom-skins-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "cards-before.json").write_text(json.dumps(before, indent=2))
    written = {}
    try:
        for name, card in changes.items():
            written[name] = request(name, card)["rev"]
        after = {name: request(name) for name in SKINS}
        for name, card in after.items():
            expected = dict(before[name], scope="public")
            assert {k: v for k, v in card.items() if k != "_rev"} == {
                k: v for k, v in expected.items() if k != "_rev"}, "Unexpected card change"
        for hero, card in heroes.items():
            assert request(hero) == card, "Hero changed concurrently"
    except Exception:
        # Revision checks prevent this rollback from overwriting another operator's changes.
        for name, revision in written.items():
            restore = copy.deepcopy(before[name])
            restore["_rev"] = revision
            request(name, restore)
        raise
    with db:
        db.execute("BEGIN IMMEDIATE")
        grants = [dict(row) for row in db.execute(select, tuple(SKINS))]
        (backup / "grants-before.json").write_text(json.dumps(grants, indent=2))
        db.execute(f"DELETE FROM InventoryGrants WHERE item IN ({placeholders})", tuple(SKINS))
    assert not db.execute(select, tuple(SKINS)).fetchall()
    db.close()
    print(json.dumps({"backup": str(backup), "public": list(SKINS),
                      "archived_grants": len(grants), "revisions": {
                          name: card["_rev"] for name, card in after.items()}}), flush=True)


if __name__ == "__main__":
    main()
