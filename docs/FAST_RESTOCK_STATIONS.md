# Fast Restock: editable CDB descriptions and station-only gains

Prepared 2026-11-18; deployment and live CDB writes remain separate.

Fast Restock increases healing and ammo received from health/ammo stations,
including station medkits. Hero abilities, other pickups, self regeneration,
mining, supplies and source-less gains receive no Fast Restock bonus. Mixed
regeneration boosts only the station contribution. Ammo attribution uses the
boosted contributions and actual reserve ammo restored, including pool caps.

The existing CDB effect `effect_perk_fast_restock_pos` remains authoritative:
`effect.buffs.health_gain` and `effect.buffs.ammo_gain` control the percentages.
The existing negative effect controls the brick-income penalty. No percentage
is embedded in runtime code and no new protocol field or client build is needed.

The server no longer rewrites Fast Restock perk/shop descriptions or clears their
translations on replication or incremental refresh. Edit `description.text` and
`description.data` directly in CDB. Those values also survive wire serialization.

`tools/fix_fast_restock_stations.py` performs a one-time correction of the two
known obsolete English descriptions, preserving percentages and the brick
penalty. It clears stale translations only when correcting those phrases.
Unrecognized/custom wording remains untouched; later edits are never rewritten
by runtime code. The tool defaults to read-only online inspection, uses revision
guards, dependency rechecks, private backup and readback for `--apply`, and
supports offline snapshots:

```powershell
python tools/fix_fast_restock_stations.py --from-file <snapshot.json> --output <candidate.json>
python -m unittest discover -s tools/tests -p test_fast_restock_stations.py
dotnet run --project tests/BNLReloadedServer.HealingPerkSourcesFixture -- <candidate.json>
dotnet build BNLReloadedServer -c Release -warnaserror
```

Deploy the server fix before applying the CDB migration. After both steps,
reconnect the client to receive the corrected catalogue. Verify station healing,
station ammo, hero regeneration and a custom localized description in-game.
Neither deployment nor CDB changes were performed during preparation.

This supersedes the Fast Restock ammo-scope and description-override behavior
recorded in `HEALING_PERK_SOURCES.md`; Health Scavenger behavior is unchanged.
