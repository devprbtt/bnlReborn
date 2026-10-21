# BlockNLoad Community Made Server
This is the open source github repo for the bnl private server recreation project.
Originally developed and made by @nascarman9, this is a fork of https://github.com/nascarman9/bnlReloaded

To connect to private server people mainly use launcher made by @devprbtt. It features a few ping compensation fixes and a lot of other optional features.
https://github.com/devprbtt/blocknload-community-launcher/releases/

To use the cdb serializer/deserializer, create a folder called Cache in the base directory (should be in the same directory as the BaseTypes, Database, etc. folders). Put the cdb file from the game assets into the new Cache folder. Then change the toJson and fromJson constants to serialize/deserialize the cdb to a json file/zipped cdb file respectively.


## Player action scores

Scores are authoritative and scoped to a match, preserved across deaths/rejoins, and included in live zone updates and end-match totals. This is the score foundation; it does not introduce XP or level rewards. Waiting arena, map editor, waiting/tutorial phases and completed matches do not award points. A matching client renders the score popup and Tab/result columns.

Set `BNL_ACTION_SCORE_RULES` to a JSON file path to override any property of `ActionScoreRules` when a match is created. Missing fields retain defaults; invalid values fail validation. Example: `{"Kill":50,"Headshot":10,"ObjectiveBonus":0.25,"ObjectiveRadius":12}`. No deployment or restart is performed by this change.

| Action | Default points |
| --- | ---: |
| Kill / assist / headshot bonus | 50 / 25 / 10 |
| First kill | 60 |
| Double / multi / ultra kill | 75 / 100 / 125 |
| Fantastic / unbelievable | 150 / 175 |
| Supply / blockbuster pickup | 25 / 100 |
| Cube destroyed / assist | 200 / 75 |
| Device built | 10 |
| Enemy device destroyed | ceil(base resource cost / 20), clamped 5–50 |
| Actual teammate healing | 5 per 50 HP |
| Device damage to enemy players | 5 per 100 damage |
| Radar / ability spot | 5 / 10 |
| Action within 12 world units of a live cube or blockbuster drop zone | +25%, floored with minimum 1 |

Kill accolades replace the regular kill award and label: e.g. Double kill +75, not Kill +50 plus another +75. Headshot +10 and objective bonuses still stack. The highest kill-chain tier wins; first kill uses 60. The authoritative existing eight-second chain resets on death or expiry, with the sixth and subsequent chain kills using Unbelievable. Tunable `FirstKill`, `DoubleKill`, `MultiKill`, `UltraKill`, `Fantastic`, and `Unbelievable` must increase above `Kill` in that order.

Objective proximity checks the action location, applies once even if zones overlap, and uses either team's cubes. Device damage and healing bank partial progress until a five-point chunk. Cube assists require damage within 30 seconds. Repeated spotting of the same target has a shared 15-second cooldown per spotter; rebuilding at the same grid cell has a 30-second cooldown. Self/friendly damage, self healing, full-health healing, neutral-device destruction and disconnect cleanup award no points. Device type refers to deployable devices, not ordinary terrain blocks.

`ZoneUpdate` appends optional `ActionScoresJson` at bit 12 (the bitfield remains two bytes). Baselines transmit totals without historical notifications; incremental updates carry sequenced awards for deduplication. End results use `Total` and statistic 12 (`ActionScore`). Archives replace this cumulative score on rejoin while continuing to accumulate legacy session statistics. Balance changes apply only to new matches.

Validation: `dotnet run --project tests/BNLReloadedServer.ActionScoreFixture -c Release`.
