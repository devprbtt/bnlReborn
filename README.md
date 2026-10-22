# BlockNLoad Community Made Server
This is the open source github repo for the bnl private server recreation project.
Originally developed and made by @nascarman9, this is a fork of https://github.com/nascarman9/bnlReloaded

To connect to private server people mainly use launcher made by @devprbtt. It features a few ping compensation fixes and a lot of other optional features.
https://github.com/devprbtt/blocknload-community-launcher/releases/

To use the cdb serializer/deserializer, create a folder called Cache in the base directory (should be in the same directory as the BaseTypes, Database, etc. folders). Put the cdb file from the game assets into the new Cache folder. Then change the toJson and fromJson constants to serialize/deserialize the cdb to a json file/zipped cdb file respectively.


## Hercules effect dropdowns

Hercules uses the CDB `schema` document, not the C# enum, to populate its controls. New buffs must be registered in `custom_types.BuffType.values` as well as the server enum/behavior. New effect variants need their enum entry, variant `children` mapping and record definition together. Never use an undeclared card-level property as the authoring interface.

`tools/prepare_hercules_effects.py --schema schema-export.json --cards effect-cards-export.json --output review-directory` prepares a reviewable schema and migrated cards, preserving input copies with SHA256 provenance. It never edits input files or publishes to CDB. The output directory must be new. It adds `life_steal` beside `player_damage`, `splash_block_damage_bonus` beside `world_damage`, and `on_hit` beside `on_kill` with the nested InstEffect editor. Retired `heal_bane` is not offered as a new buff; Heal Bane is authored via `on_hit`.

The splash modifier is now `effect.buffs.splash_block_damage_bonus` (0.5 = +50%). Card-level values remain readable during migration. If both exist, the buff entry wins, including explicit zero; the preparation tool rejects conflicting values rather than guessing. It only migrates buff cards, preserving other buffs, targeting, metadata and revisions. Missing legacy values are omitted from new card JSON. The server strips this server-only modifier from the old client binary catalogue, so this does not require a client enum update.

Activation order: deploy the matching server first, then merge/publish the schema and migrated card documents using fresh CDB revisions, verify readback, and reload the Hercules schema/reopen the database. Do not publish the new buff keys to an older server: unknown enum keys reject catalogue loading. The supplied tool and this commit do not deploy or publish anything. The local Hercules cache is not the authoritative schema and must not be patched in place.

Tests: `python -m unittest discover -s tests -p test_hercules_effect_schema.py` and `dotnet run --project tests/BNLReloadedServer.TrondsonAoeFixture -c Release`.
