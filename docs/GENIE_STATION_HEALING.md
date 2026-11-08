# Genie orb and health-station healing

Genie's normal and Speed Ball orbs use the same aura. The catalogue migration
raises its player heal from 1.4 to 2 HP per 0.2-second tick, matching the current
health-station rate of 10 HP/s. Radius, lifetime, world healing, visuals and
projectile limits are unchanged.

The recipient suppresses Genie orb heal ticks while generic health regeneration
has a health-supply unit among its active sources. Station healing keeps its
existing attribution and rate. Multiple stations still share one regen effect;
overlapping orbs still share the existing recipient cadence. Once the last
station source is removed, an orb can resume. Cosmetic and map-owned stations
use the same rule. Other hero healing and health pickups are unaffected.

## Deployment order

This change is prepared, not deployed. Deploy the server code first, then run
`python3 tools/match_genie_orb_station_healing.py` on the host for a read-only
plan. Apply with `--apply` only as part of the authorized deployment. The tool
backs up all reviewed inputs, checks their revisions and changes only the orb
heal amount. The server catalogue watcher loads that change without a further
restart. Re-running the migration is a no-op.

## Validation

- `GenieStationHealingFixture`: 29 checks with the current production catalogue
  after the offline migration. Normal/Speed Ball overlap, station priority,
  takeover in both directions, seven station variants, map ownership, multiple
  stations, unrelated healing and health cap pass.
- `GenieOrbHealingFixture`: 21 existing overlap, attribution and visual checks pass.
- `tools/tests/test_genie_station_healing.py`: six migration guard/idempotency tests pass.
- Offline comparison of the 2105-card live snapshot changes exactly one field.
- The older `KiraKillRegenFixture` expects a 5 HP/s passive. Current live cards
  route Kira's passive to generic 10 HP/s regen, so that fixture fails identically
  on the unchanged production revision and this branch. No Kira cards or code
  are changed here.

Example offline verification:

```text
python tools/match_genie_orb_station_healing.py --from-file catalogue.json --output candidate.json
dotnet run --project tests/BNLReloadedServer.GenieStationHealingFixture -- candidate.json
dotnet run --project tests/BNLReloadedServer.GenieOrbHealingFixture
python tools/tests/test_genie_station_healing.py -v
```
