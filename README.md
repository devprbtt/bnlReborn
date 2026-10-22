# BlockNLoad Community Made Server
This is the open source github repo for the bnl private server recreation project.
Originally developed and made by @nascarman9, this is a fork of https://github.com/nascarman9/bnlReloaded

To connect to private server people mainly use launcher made by @devprbtt. It features a few ping compensation fixes and a lot of other optional features.
https://github.com/devprbtt/blocknload-community-launcher/releases/

To use the cdb serializer/deserializer, create a folder called Cache in the base directory (should be in the same directory as the BaseTypes, Database, etc. folders). Put the cdb file from the game assets into the new Cache folder. Then change the toJson and fromJson constants to serialize/deserialize the cdb to a json file/zipped cdb file respectively.


## Player action scores

Scores are authoritative and scoped to a match, preserved across deaths/rejoins, and included in live zone updates and end-match totals. This is the score foundation; it does not introduce XP or level rewards. Waiting arena, map editor, waiting/tutorial phases and completed matches do not award points. A matching client renders the score popup and Tab/result columns.

Edit **`global_logic.action_scores`** in the CDB. The complete starting section is in [`catalogue/action-scores.json`](catalogue/action-scores.json): merge this section into the existing `global_logic` document, preserving its other fields and current CouchDB `_rev`; do not replace the whole card with the fragment. All field names use snake_case, for example `cube`, `double_kill`, `objective_radius`, `objective_bonus`, and `cube_assist_window`.

The server's existing CDB watcher validates catalogue edits. Invalid scores are rejected before replacing the current catalogue. Each new match takes an independent copy; active matches retain their starting values. Missing sections/fields keep the documented defaults for older CDBs. The old `BNL_ACTION_SCORE_RULES` environment-file override is removed: CDB is the authoritative configuration source. These fields are server-only and do not change legacy client binary cards. No live CDB edit, deployment or restart is performed by this change.

`heal_per_point` is HP per point (default10); `device_damage_per_point` is damage per point (20); `continuous_chunk` is the minimum payout in points (5). `objective_bonus` is a fraction (0.25 means25%), `objective_radius` is world units (12), and `spot_cooldown`, `build_cooldown`, `cube_assist_window` are seconds (15/30/30). Device destruction uses `device_cost_per_point`, `device_min`, and `device_max`.

| Action | Default points |
| --- | ---: |
| Kill / assist / headshot bonus | 50 / 25 / 10 |
| First kill | 60 |
| Double / multi / ultra kill | 75 / 100 / 125 |
| Fantastic / unbelievable | 150 / 175 |
| Supply / blockbuster pickup | 25 / 100 |
| Cube destroyed / assist | 250 / 75 |
| Device / hero block built | ceil(actual resource cost / 20), clamped 1-50; free builds give 0 |
| Enemy device destroyed | ceil(base resource cost / 20), clamped 5–50 |
| Actual teammate healing | 5 per 50 HP |
| Device damage to enemy players | 5 per 100 damage |
| Radar / ability spot | 5 / 10 |
| Action within 12 world units of a live cube or blockbuster drop zone | +25%, floored with minimum 1 |

Kill accolades replace the regular kill award and label: e.g. Double kill +75, not Kill +50 plus another +75. Headshot +10 and objective bonuses still stack. The highest kill-chain tier wins; first kill uses 60. The authoritative existing eight-second chain resets on death or expiry, with the sixth and subsequent chain kills using Unbelievable. Tunable `first_kill`, `double_kill`, `multi_kill`, `ultra_kill`, `fantastic`, and `unbelievable` must increase above `kill` in that order.

Objective proximity checks the action location, applies once even if zones overlap, and uses either team's cubes. Device damage and healing bank partial progress until a five-point chunk. Cube destruction grants exactly 250 to the destroyer (even at range) and each living, active teammate within `objective_radius` (default12 units, inclusive) of the destroyed cube. Damage contribution is not required for nearby teammates; defenders are excluded. This full shared reward does not receive an additional proximity bonus or duplicate assist. Recent contributors outside the area retain the cube assist award (75 plus the existing objective bonus). Cube assists require damage within 30 seconds. Repeated spotting of the same target has a shared 15-second cooldown per spotter; rebuilding at the same grid cell has a 30-second cooldown. Self/friendly damage, self healing, full-health healing, neutral-device destruction and disconnect cleanup award no points. Device type refers to deployable devices, not ordinary terrain blocks.

`ZoneUpdate` appends optional `ActionScoresJson` at bit 12 (the bitfield remains two bytes). Baselines transmit totals without historical notifications; incremental updates carry sequenced awards for deduplication. End results use `Total` and statistic 12 (`ActionScore`). Archives replace this cumulative score on rejoin while continuing to accumulate legacy session statistics. Balance changes apply only to new matches.

Validation: `dotnet run --project tests/BNLReloadedServer.ActionScoreFixture -c Release`.

## Hero-specific action scores

These are additional support rewards, except Miracle Cure which replaces the generic healing award for that same restored health. All defaults are editable under global_logic.action_scores; a match snapshots settings at creation.

| Hero | Confirmed action | Starting points |
| --- | --- | --- |
| Tony | Ally completes build with F build-speed buff | 5 |
| Tony | Enemy-caused device health damage repaired | 5 per 50 HP |
| Trondson | Boosted ally destroys an enemy device (including boosted drills) | 10 |
| Genie | Another friendly collects a pickup from his lamp | 10 |
| Genie | Jinx applied to enemy player/device | 10 |
| Doc | Ally health restored by F | 5 per 50 HP, replaces generic heal credit |
| Vander | Cage affects an enemy | 10 |
| Sweet Science | EMP affects enemy device | 10 |
| Yeti | Avalanche slow affects enemy | 10 |
| Sarge | Grenade damages enemy player | 10 per hit |
| Roly | Barrel/goo affects enemy player | 10 |
| Cog | Mortar destroys enemy device | 10 additional |
| Astarella | F boost applied to another ally | 10 |
| Kira | F reload buff applied to another ally | 5 |
| Kreepy | F debuff applied to enemy | 10 |
| Nigel / Ninja | No additional hero action | â€” |

Effect rewards use hero_effect_cooldown (15 seconds per owner/reason/target); grenade hits and distinct collected pickups do not share that throttle. Build support uses the existing per-cell build cooldown. Existing objective proximity bonuses apply. No self-support awards; rejected/immune/dead-target applications do not score.

Repair XP only counts outstanding enemy health damage, including player-owned devices and hero blocks. Every heal consumes this budget; self, friendly, environmental damage, shield absorption and overhealing grant no credit.

Build scoring uses `build_cost_per_point` (20), `build_min` (1), and `build_max` (50) in `global_logic.action_scores`. Both confirmed placement routes pass the charged TotalCost, including cost modifiers. The retired flat `build` field is ignored. Build time is not a separate multiplier; expensive devices earn more by resource investment. Objective bonuses apply afterward, and per-cell rebuild cooldown remains.

Build-point exclusions (all variants): speed pads, bounce pads, thorn bushes, glue, caltrops, bear traps and force gates. Other action rewards are unchanged.
