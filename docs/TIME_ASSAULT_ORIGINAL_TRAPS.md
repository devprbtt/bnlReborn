# Original Time Assault trap stats

Prepared on production base `faf74385b554d8f79ab13c88aa544210e93220a0`.
Deployment is separate; this change has not been deployed or tested in a live client.

Player-facing note: Time Assault course traps now use their original authored
stats, including a two-block Acid Trap trigger radius. Trap damage, burning,
poison, slowing, rooting and gravity effects follow the original course data.
Other modes retain their current balance.

## Baseline

The user requested the stats used by Paul's original Time Assault maps. The
original installed client's downloaded CDB includes all six course definitions.
They refer to the base trap units, not the player's `_lvl01` upgrade cards.
In particular the course Acid Trap is radius 2.0 even though the cached player
level-1 variant anomalously says 3.0. No new live connection to Paul's server
was made; this is the preserved original installed cache from the investigation.

Input SHA-256:

- Original compressed CDB: `eb81164a196a688c058d6c49ae07c66d7a59bb9358ae1c46fb1628ef08f765fd`.
- Decoded original JSON: `fe841debc430951cab60a30f41ade3c767b000a7d75044068dbbedbfc6530db5`.
- Reborn raw server catalogue snapshot: `8b3475b158eab02bf0b87802d787da633d3710b35dd9e90808a285ff9d0386ef`.
- Embedded isolated baseline: `d7eeeb0aa0bf7a6084df9c3abb4bc4dbcadc88148ae7232a9da9bd4376c501ae`.

The inputs are copied under the client repository's ignored
`extracted/work/trap-trigger-investigation-20261122/`. The raw Reborn server
snapshot is required for fixtures because the client wire CDB omits server-only
class-perk settings. The original wire CDB decodes exactly to its payload end.

## Scope and implementation

Embed 30 isolated original cards: seven authored proximity-trap units, their
spawned units/constant effects, and eight ground-trap/fire/goo blocks. Rewrite
internal gameplay references so no child cloud, poison, fire, slow or root
silently falls back to current multiplayer tuning. Reuse installed prefab,
material, sound and impact addresses. All retained presentation dependencies
exist in the current catalogue.

Register the cards in the normal public catalogue blob so existing clients
understand their unit/effect keys and 16-bit block IDs. Historical block IDs
64000–64007 are isolated, deterministic and checked for collisions. Registration
is idempotent and does not modify the ordinary cards.

Only map-authored units in authoritative `MatchType.TimeTrial` zones select
historical keys. Remap the runtime terrain copy's Glue, Caltrops, Astro Caltrops,
Bear Trap, Astro Bear Trap, Fake Block and existing trap fire cells before
stability/snapshot initialization. Preserve positions, rotations, teams, damage
and visual/light data. Lava, no-build cells, map files, course layouts, rewards,
timers, player loadouts, and all non-Time-Assault gameplay cards remain intact.
Spawned fire/goo use the matching historical dependency cards too.

Examples: Acid Trap trigger 2.6 → 2.0; Landmine damage 55 → 50 and blast radius
3.5 → 3.0; Fire Trap explosion damage 35 → 30. Full original stats are restored,
so not every change is a reduction (original Bear Trap damage is 35 versus
Reborn's 30). Proximity collision shape and wall checking are not changed.

No live CDB writes or database migration are required. Existing clients receive
the added cards on reconnect after deployment. No client rebuild is required.
Existing leaderboard records are retained; they may have been set with the
previous trap balance. No score reset or record relabeling is included.

## Reproduction and validation

From the server repository root, substitute the copied input paths:

```powershell
python tools/generate_time_assault_traps.py <original-catalogue.json>
dotnet run --project tests/BNLReloadedServer.TimeAssaultTrapsFixture -c Release -- <reborn-raw-catalogue.json> <original-catalogue.json>
dotnet run --project tests/BNLReloadedServer.TimeAssaultSpawnFixture -c Release -- <reborn-raw-catalogue.json>
dotnet run --project tests/BNLReloadedServer.TimeAssaultFixture -c Release -- <reborn-raw-catalogue.json> <output-directory>
dotnet build BNLReloadedServer -c Release -warnaserror
```

37,537 trap assertions passed across six actual course payloads: 784 proximity
traps and 9,789 remapped ground cells. The fixture checks every historical card
against its independent original input, real `CreateMapUnits`/factory/trigger
sphere behavior, terrain and native CDB wire round trips, complete dependency
cards, ordinary-mode initialization, unchanged map objects, block-ID collision
rejection, and replication/reload isolation. Existing fixed-spawn fixture: 504
checks. Existing Time Assault fixture: 106 checks. Release build: zero warnings
and errors. Generator reproduction matches the embedded baseline hash.

Deployment acceptance still needs reconnect plus playing/restarting courses,
including acid activation, fire spread/burning, mine blasts, gravity, ground
traps and their visuals. Roll back with the preceding server revision; no map,
catalogue database or player-data rollback is needed for this code-only change.


## Deployment dependency follow-up

The initial server restart on 777625b passed its health gate. Readback of every live historical card then caught material_caltrops missing from the current catalogue; the earlier wire fidelity check preserved the hash but did not establish that the target existed. Added an isolated original material card and reference-resolution assertions over every historical card. Thirty cards now include the original Caltrops entry sound and footstep material. Current-live-catalogue trap fixture passes 37,537 assertions. This correction supersedes the initial deployment candidate; live revision/hash/catalogue verification is required after its rollout.
