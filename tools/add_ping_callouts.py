"""Add three team callouts: "Farm over here", "Thanks" and "Nice shot!".

Each is a notification card (category notification) that the client sends with ServicePlayer's
PlayerCommand and teammates see above the sender, in chat and on the minimap; the server relays any
command key, so no server code changes. The cards are also listed in global_logic.commands_logic so
the callout wheel (TeamPingWheel) and the legacy commands menu find their titles:
- note_command_farm       left menu, after Retreat; pickaxe icon and the FollowMe minimap marker
                          ("over here"), no voice line (the existing ones would say something else)
- note_command_thanks     right menu; text only, like Affirmative/Negative
- note_command_nice_shot  right menu; text only

Clients older than the new wheel never offer them but display them correctly when a teammate sends one.

Run on the production host. The default mode is read-only and prints the plan. ``--apply`` creates the
cards first, then writes global_logic with its current revision as a concurrency guard, saves the
original under ``/root/config-backups`` and reads the result back. A re-run after a successful apply is
a no-op. The catalogue watcher loads the change without a restart.

For an offline audit, pass ``--from-file CATALOGUE.json``; it never connects or writes.
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

LANGUAGES = ("fr", "ru", "es", "de", "pt")


def localized(english, translations):
    assert set(translations) == set(LANGUAGES), translations
    return {"text": english,
            "data": {lang: {"original": english, "translation": translations[lang]} for lang in LANGUAGES}}


CALLOUTS = [
    {
        "id": "note_command_farm", "menu": "left_menu", "after": "note_command_retreat",
        "title": localized("Farm Over Here", {"fr": "Farmez ici", "ru": "Фармим здесь", "es": "Recolectad aquí",
                                               "de": "Hier farmen", "pt": "Farmem aqui"}),
        "text": localized("<PLAYER>: Farm over here", {"fr": "<PLAYER> : Farmez ici", "ru": "<PLAYER>: Фармим здесь",
                                                       "es": "<PLAYER>: Recolectad aquí", "de": "<PLAYER>: Hier farmen",
                                                       "pt": "<PLAYER>: Farmem aqui"}),
        "icon": "abe_icon_tool", "minimap": "FollowMe",
    },
    {
        "id": "note_command_thanks", "menu": "right_menu", "after": None,
        "title": localized("Thanks", {"fr": "Merci", "ru": "Спасибо", "es": "Gracias", "de": "Danke", "pt": "Obrigado"}),
        "text": localized("<PLAYER>: Thanks!", {"fr": "<PLAYER> : Merci !", "ru": "<PLAYER>: Спасибо!",
                                                "es": "<PLAYER>: ¡Gracias!", "de": "<PLAYER>: Danke!",
                                                "pt": "<PLAYER>: Obrigado!"}),
        "icon": None, "minimap": None,
    },
    {
        "id": "note_command_nice_shot", "menu": "right_menu", "after": None,
        "title": localized("Nice Shot!", {"fr": "Joli tir !", "ru": "Отличный выстрел!", "es": "¡Buen tiro!",
                                          "de": "Guter Schuss!", "pt": "Belo tiro!"}),
        "text": localized("<PLAYER>: Nice shot!", {"fr": "<PLAYER> : Joli tir !", "ru": "<PLAYER>: Отличный выстрел!",
                                                   "es": "<PLAYER>: ¡Buen tiro!", "de": "<PLAYER>: Guter Schuss!",
                                                   "pt": "<PLAYER>: Belo tiro!"}),
        "icon": None, "minimap": None,
    },
]
NEW_IDS = tuple(callout["id"] for callout in CALLOUTS)
READ = NEW_IDS + ("global_logic", "note_command_retreat", "note_command_response_affirmative")
LEGACY_BINDINGS = 12  # UiCommandsMenu binds PlayerCommand1..12 (UserAction 153..164) per menu


def without_rev(card):
    return {key: value for key, value in card.items() if key not in ("_rev", "hercules_metadata")}


def notification(callout, template):
    """A card shaped like the live command cards (template: an existing note_command card)."""
    card = {"_id": callout["id"], "category": "notification", "scope": template["scope"],
            "small_notify_text": copy.deepcopy(callout["text"])}
    if callout["icon"]:
        card["world_notify_icon"] = callout["icon"]
    if callout["minimap"]:
        card["minimap_notify_prefab"] = callout["minimap"]
    card["show_on_kill_scroll"] = template["show_on_kill_scroll"]
    card["range"] = template["range"]
    return card


def keys(entries):
    found = []
    for entry in entries or []:
        if entry.get("command_key"):
            found.append(entry["command_key"])
        found.extend(keys(entry.get("subcommands")))
    return found


def migrate_global_logic(card):
    changed = copy.deepcopy(card)
    commands = changed["commands_logic"]
    for callout in CALLOUTS:
        if callout["id"] in keys(commands["left_menu"]) + keys(commands["right_menu"]):
            continue
        menu = commands[callout["menu"]]
        entry = {"title": copy.deepcopy(callout["title"]), "command_key": callout["id"], "subcommands": []}
        if callout["after"]:
            index = next(i for i, item in enumerate(menu) if item.get("command_key") == callout["after"])
            menu.insert(index + 1, entry)
        else:
            menu.append(entry)
    for side in ("left_menu", "right_menu"):
        assert len(commands[side]) <= LEGACY_BINDINGS, (side, len(commands[side]))
    return changed


def plan(cards):
    """Return (writes, after) for a {card_id: card} catalogue view; new cards come before global_logic."""
    template = cards["note_command_response_affirmative"]
    writes = {}
    for callout in CALLOUTS:
        desired = notification(callout, template)
        existing = cards.get(callout["id"])
        if existing is None:
            writes[callout["id"]] = desired
        else:
            assert without_rev(existing) == desired, "a different " + callout["id"] + " exists"
    for icon in {c["icon"] for c in CALLOUTS if c["icon"]}:
        assert icon == "abe_icon_tool", icon  # a GUI atlas sprite, checked in the client preview
    for minimap in {c["minimap"] for c in CALLOUTS if c["minimap"]}:
        assert any(c.get("minimap_notify_prefab") == minimap for c in cards.values()), minimap
    migrated = migrate_global_logic(cards["global_logic"])
    if migrated != cards["global_logic"]:
        writes["global_logic"] = migrated
    after = dict(cards)
    after.update(writes)
    for card_id in NEW_IDS:
        assert card_id in after
    return writes, after


def audit(cards):
    commands = cards["global_logic"]["commands_logic"]
    return {
        "left_menu": [item["title"]["text"] for item in commands["left_menu"]],
        "right_menu": [item["title"]["text"] for item in commands["right_menu"]],
        "callouts": {card_id: {"text": cards[card_id]["small_notify_text"]["text"],
                               "icon": cards[card_id].get("world_notify_icon"),
                               "minimap": cards[card_id].get("minimap_notify_prefab")}
                     for card_id in NEW_IDS if card_id in cards},
        "listed": [card_id for card_id in NEW_IDS if card_id in keys(commands["left_menu"]) + keys(commands["right_menu"])],
    }


if __name__ == "__main__":
    if "--from-file" in sys.argv:
        source = Path(sys.argv[sys.argv.index("--from-file") + 1])
        catalogue = {card["_id"]: card for card in json.loads(source.read_text(encoding="utf-8"))}
        writes, after = plan(catalogue)
        print(json.dumps({"write": list(writes), "after": audit(after)}, indent=2, ensure_ascii=False))
        if "--write-after" in sys.argv:
            Path(sys.argv[sys.argv.index("--write-after") + 1]).write_text(
                json.dumps(list(after.values()), ensure_ascii=False), encoding="utf-8")
        sys.exit()

    config = json.loads(Path("/opt/bnlreloaded/current/Configs/configs.json").read_text())
    config = {key.replace("_", "").lower(): value for key, value in config.items()}
    base = config["couchdbendpoint"].rstrip("/") + "/" + config["couchdbdatabasename"]
    auth = "Basic " + base64.b64encode(
        (config["couchdbusername"] + ":" + config["couchdbpassword"]).encode()
    ).decode()

    def request(name, data=None, method=None, query=""):
        req = urllib.request.Request(
            base + "/" + urllib.parse.quote(name, safe="") + query,
            headers={"Authorization": auth, "Content-Type": "application/json"},
            data=None if data is None else json.dumps(data).encode(),
            method=method or ("GET" if data is None else "PUT"),
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

    rows = request("_all_docs", query="?include_docs=true")["rows"]
    catalogue = {row["id"]: row["doc"] for row in rows if not row["id"].startswith("_design")}
    before = {card_id: catalogue[card_id] for card_id in READ if card_id in catalogue}
    writes, after = plan(catalogue)
    print(json.dumps({"write": list(writes),
                      "revisions": {card_id: before[card_id]["_rev"] for card_id in writes if card_id in before},
                      "after": audit(after)}, indent=2, ensure_ascii=False), flush=True)
    if "--apply" not in sys.argv or not writes:
        sys.exit()

    backup = Path("/root/config-backups") / ("add-ping-callouts-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup / "before.json").write_text(json.dumps(before, indent=2, ensure_ascii=False))
    for card_id, card in writes.items():
        request(card_id, card)  # global_logic carries its _rev; a concurrent edit fails instead of being lost.

    saved = {card_id: card for card_id in READ if (card := fetch(card_id)) is not None}
    for card_id in writes:
        assert without_rev(saved[card_id]) == without_rev(after[card_id]), "readback differs for " + card_id
    verified = dict(after)
    verified.update(saved)
    print(json.dumps({"backup": str(backup), "revisions": {card_id: saved[card_id]["_rev"] for card_id in writes},
                      "verified": audit(verified)}, indent=2, ensure_ascii=False), flush=True)
