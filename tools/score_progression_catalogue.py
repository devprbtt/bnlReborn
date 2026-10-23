"""Catalogue side of score-based progression (2026-10-23).

- XP curves: each level costs flat + mult * (level - 1)^power. Player 580 / 333 / 0.85, no cap; hero 400 / 245 /
  0.74, capped at 200. Fitted to ~1,840 action score per median match: hero level 10 in ~5 matches on that hero,
  player level 25 in ~41 matches.
- No level rewards: player and hero level/every-level rewards are emptied and every hero's unlock track is cleared,
  since everything is already unlocked and level rewards will be rebuilt separately.
- No level requirements anywhere: ranked hero levels, minimum player levels, matchmaking realm level bands,
  new-player map thresholds and the hero-switch player-level cap are removed, so no account needs a faked level.
- Win bonus 30 % wherever a victory bonus exists.

Read-only by default (prints the plan); ``--apply`` writes revision-checked documents with a private backup and
readback. Re-running after an apply plans nothing. Run on the VPS: ``sudo -n python3 - --apply < this file``.
"""

import base64
import copy
import json
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

PLAYER_XP = {"flat_coeff": 580, "mult_coeff": 333, "power_coeff": 0.85, "max_level": None}
HERO_XP = {"flat_coeff": 400, "mult_coeff": 245, "power_coeff": 0.74, "max_level": 200}
VICTORY_BONUS = 0.3


def plan(card):
    """Returns the changed card and a list of human-readable changes (empty when nothing changes)."""
    new = copy.deepcopy(card)
    notes = []

    def drop(container, key, label):
        if isinstance(container, dict) and container.get(key) is not None:
            notes.append(f"remove {label} = {json.dumps(container[key])[:80]}")
            del container[key]

    if new.get("_id") == "global_logic" and isinstance(new.get("xp_logic"), dict):
        for key, values in (("player_xp", PLAYER_XP), ("hero_xp", HERO_XP)):
            logic = new["xp_logic"].setdefault(key, {})
            for field, value in values.items():
                if logic.get(field) != value:
                    notes.append(f"xp_logic.{key}.{field}: {logic.get(field)} -> {value}")
                    logic[field] = value
            for rewards in ("level_rewards", "every_level_rewards"):
                if logic.get(rewards):
                    notes.append(f"xp_logic.{key}.{rewards}: {len(logic[rewards])} rewards -> none")
                    logic[rewards] = {}

    if new.get("category") == "game_mode":
        bonuses = (new.get("reward_logic") or {}).get("bonuses") or {}
        if "victory" in bonuses and bonuses["victory"] != VICTORY_BONUS:
            notes.append(f"reward_logic.bonuses.victory: {bonuses['victory']} -> {VICTORY_BONUS}")
            bonuses["victory"] = VICTORY_BONUS
        drop(new, "heroes_reqs", "heroes_reqs")
        drop(new, "min_player_level", "min_player_level")
        drop(new, "noob_maps", "noob_maps")
        for i, realm in enumerate((new.get("matchmaker") or {}).get("realms") or []):
            drop(realm, "min_level", f"matchmaker.realms[{i}].min_level")
            drop(realm, "max_level", f"matchmaker.realms[{i}].max_level")

    if new.get("category") == "match":
        drop(new.get("hero_switch_logic"), "max_player_level", "hero_switch_logic.max_player_level")

    if new.get("category") == "unit":
        data = new.get("data")
        if isinstance(data, dict) and data.get("unlockables"):
            notes.append(f"data.unlockables: {len(data['unlockables'])} level rewards -> none")
            data["unlockables"] = {}

    return new, notes


def connect():
    config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
    config = {key.replace("_", "").lower(): value for key, value in config.items()}
    base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
    auth = "Basic " + base64.b64encode((config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()).decode()

    def request(path, data=None):
        req = urllib.request.Request(base + "/" + path, headers={"Authorization": auth, "Content-Type": "application/json"},
                                     data=None if data is None else json.dumps(data).encode(), method="GET" if data is None else "PUT")
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.load(response)
    return request


def main():
    request = connect()
    rows = request("_all_docs?include_docs=true")["rows"]
    cards = {row["id"]: row["doc"] for row in rows if not row["id"].startswith("_design")}
    planned = {}
    for card_id, card in cards.items():
        new, notes = plan(card)
        if notes:
            planned[card_id] = (new, notes)
    print(json.dumps({"cards": len(cards), "changes": {k: n for k, (_, n) in planned.items()}}, indent=2), flush=True)
    if "--apply" not in sys.argv or not planned:
        return

    backup = Path("/root/config-backups") / ("score-progression-catalogue-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps({k: cards[k] for k in planned}, indent=2))
    for card_id, (new, _) in planned.items():
        request(urllib.parse.quote(card_id, safe=""), new)   # carries the read _rev: CouchDB refuses a stale write
    for card_id, (new, _) in planned.items():
        saved = request(urllib.parse.quote(card_id, safe=""))
        assert {k: v for k, v in saved.items() if k != "_rev"} == {k: v for k, v in new.items() if k != "_rev"}, "readback differs: " + card_id
        assert not plan(saved)[1], "still plans changes after apply: " + card_id
    print(json.dumps({"backup": str(backup), "written": len(planned), "verified": True}), flush=True)


if __name__ == "__main__":
    main()
