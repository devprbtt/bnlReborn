# Class perk balance in CouchDB

The first mandatory class perks are public `CardPerk` documents with `slot_type: "class"` (wire enum 4). They are automatic class passives, not shop purchases or ordinary inventory perks. Brawn/Brains/Skill II and III remain unavailable.

| Document | `class_perk` field | Default | Accepted range |
| --- | --- | --- | --- |
| `perk_class_brawn_1` | `out_of_combat_seconds` | 5 | 0..60 seconds |
| `perk_class_brawn_1` | `spawn_distance` | 1.15 | 1..3 world units |
| `perk_class_brains_1` | `healing_return_percent` | 50 | 0..100 percent |
| `perk_class_skill_1` | `out_of_combat_seconds` | 5 | 0..60 seconds |
| `perk_class_skill_1` | `regen_max_health_percent_per_second` | 10 | 0..100 percent |

Edit the numeric fields inside `class_perk` through CDB. Do not type a percent sign. Fractions are supported. Missing cards/fields retain the existing defaults for staged rollout. Invalid/nonfinite values are rejected before replacing the server catalogue. Keep descriptions as templates: `{out_of_combat_seconds}`, `{healing_return_percent}`, `{regen_max_health_percent_per_second}`, `{spawn_distance}`. The server resolves them into the existing binary CardPerk record; no new wire fields are appended. Updated clients read these names/descriptions from the catalogue; old clients retain their fixed descriptions until updated. Players refresh the catalogue on reconnect.

CDB replication changes healing rates for existing heroes on their next heal/tick. Brawn's delay is evaluated against its most recent combat time. Skill records its next eligibility time on combat/respawn, so editing its delay affects the next such event; an already recorded deadline is retained. Health-station exclusion, actual-healing accounting, grounded/clearance checks, team restriction and combat triggers remain server rules.

## First activation

1. Build and deploy this server revision using the normal DLL/PDB hotfix procedure. **Do not restart while players are in a casual match.** Read `/api/public/home` from the local control panel and inspect `play.matches` immediately before any restart. If a match is active or presence is unavailable, leave the server running and defer activation. Do not install these cards into an older running binary: it cannot parse the new enum.
2. Stage `tools/register_class_perks.py` and its `catalogue/class-perks` JSON directory on the host. Run the script with sudo (dry run first), then `--apply`. It reads credentials only from the host config, checks deployed binary support, creates only missing documents and preserves existing tuned values. It writes a private before/after snapshot under `/root/config-backups/class-perks-*`.
3. Verify three change-watcher events and CDB readback, then reconnect an updated client to obtain resolved descriptions. Subsequent numeric changes need no server/client rebuild or restart.

Rollback before reverting the server binary: remove only cards created by this migration using their current CDB revisions, backed up in the migration snapshot, then verify the catalogue watcher processed deletion. Never delete preexisting tuned cards without restoring the saved document. The old binary cannot ingest class enum 4.

## Validation

Run the BrawnRally, BrainsRecovery, SkillRecovery and ClassPerkCatalogue fixtures under `tests/`. They verify original gameplay predicates, altered CDB rates/delays on existing heroes, serialization compatibility, fractional text and rejection of invalid updates without replacing the snapshot.
