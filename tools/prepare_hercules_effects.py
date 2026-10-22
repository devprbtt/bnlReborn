"""Prepare Hercules dropdown schema and card migrations; never edits input or live CDB."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

BUFF = "splash_block_damage_bonus"


def update_schema(source):
    result = copy.deepcopy(source)
    types = result["custom_types"]
    values = types["BuffType"]["values"]
    # Place additions beside related effects, preserving every existing entry.
    for name, after in [("life_steal", "player_damage"), (BUFF, "world_damage")]:
        if name not in values:
            values.insert(values.index(after) + 1, name)
    kinds = types["ConstEffectType"]["values"]
    if "on_hit" not in kinds:
        kinds.insert(kinds.index("on_kill") + 1, "on_hit")
    children = types["ConstEffect"]["children"]
    if "on_hit" in children and children["on_hit"] != "ConstEffectOnHit":
        raise ValueError("Conflicting on_hit schema mapping")
    children["on_hit"] = "ConstEffectOnHit"
    if "ConstEffectOnHit" not in types:
        types["ConstEffectOnHit"] = copy.deepcopy(types["ConstEffectOnKill"])
    # The old ad-hoc field is not an authoring field once cards are migrated.
    types["CardEffect"]["fields"].pop(BUFF, None)
    return result


def migrate_card(source):
    result = copy.deepcopy(source)
    if source.get("category") != "effect" or BUFF not in source:
        return result
    effect = result.get("effect", {})
    if effect.get("type") != "buff":
        raise ValueError("Legacy bonus on non-buff card needs manual migration: " + source.get("_id", "?"))
    buffs = effect.setdefault("buffs", {})
    if BUFF in buffs and buffs[BUFF] != result[BUFF]:
        raise ValueError("Conflicting bonus values: " + source.get("_id", "?"))
    buffs[BUFF] = result.pop(BUFF)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--schema", required=True, type=Path)
    parser.add_argument("--cards", type=Path, help="Optional exported array of card documents")
    parser.add_argument("--output", required=True, type=Path, help="New directory for reviewable outputs")
    args = parser.parse_args()
    sources = [args.schema] + ([args.cards] if args.cards else [])
    raw = [path.read_bytes() for path in sources]
    schema = update_schema(json.loads(raw[0]))
    cards = [migrate_card(card) for card in json.loads(raw[1])] if args.cards else None
    args.output.mkdir(parents=True, exist_ok=False)
    manifest = []
    for index, (path, content) in enumerate(zip(sources, raw)):
        backup = args.output / ("source-" + str(index) + ".json")
        backup.write_bytes(content)
        manifest.append({"source": str(path.resolve()), "sha256": hashlib.sha256(content).hexdigest(), "copy": backup.name})
    (args.output / "schema.json").write_text(json.dumps(schema, indent=2) + "\n", encoding="utf-8")
    if cards is not None:
        (args.output / "cards.json").write_text(json.dumps(cards, indent=2) + "\n", encoding="utf-8")
    (args.output / "provenance.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("Prepared schema and migrations; no live database changes.")


if __name__ == "__main__":
    main()
