# Healing perk sources (2026-11-09; NOT DEPLOYED)

Player-facing notes:
- Fast Restock's healing bonus now applies only to health/ammo stations, including health-station medkits.
- Health Scavenger boosts healing from every source, including stations, hero tools/abilities, pickups, regeneration, mining, supplies and source-less recovery.

Fast Restock retains its catalogue-defined 15% ammo gain and 15% brick penalty; Health Scavenger retains its 37% global healing bonus and 10% max-health penalty. This request changes healing scope only. Fast Restock perk/shop descriptions now state the split; obsolete translations fall back to corrected English. Full replication and incremental refresh both apply the description correction.

Implementation: exclude Fast Restock's HealthGain entry from global buff aggregation; evaluate that same catalogue entry only for station-origin healing. Identify health/ammo station units through existing supply labels/minimap types and the two station-medkit card identities. Genie lamp and dropped-health pickups are not station healing. Keep Health Scavenger and other HealthGain effects on the shared AddHealth path. No new wire buff type, client rebuild, map change or CouchDB edit. Direct heals carry EffectSource into AddHealth. Mixed regeneration apportions the station bonus by originating rate, including shared effect keys with distinct origins; healing attribution and enemy-damage credit use the resulting effective weights, capped by actual health restored. The bonus is applied once.

Validation: HealingPerkSourcesFixture 95 checks using actual live perk tuning in a real GameZone harness; HealAttribution 22, BrainsRecovery 24, HealBane 18, GenieStationHealing 29. Total 188 checks. Release warnings-as-errors build passed with zero warnings/errors. Includes station/hero/pickup/self/block instant heals, mixed regeneration and attribution, shared regen keys, mining/supply recovery, heal reduction, health caps, unequip, and fresh incremental tooltip updates. No live match/client test.

Reproduce from server root:
```
dotnet run --project tests/BNLReloadedServer.HealingPerkSourcesFixture -- <catalogue-json>
dotnet run --project tests/BNLReloadedServer.HealAttributionFixture
dotnet run --project tests/BNLReloadedServer.BrainsRecoveryFixture
dotnet run --project tests/BNLReloadedServer.HealBaneFixture
dotnet run --project tests/BNLReloadedServer.GenieStationHealingFixture -- <catalogue-json>
dotnet build BNLReloadedServer -c Release -warnaserror
```
Input: existing snapshot J:/github/blocknload-unity-upgrade/extracted/work/beta92-server-inputs/server-live-cards.json; SHA-256 03c9857c2e9bebc487fc1fc651c5ec7fcbac3331ee9c506d7188f708ca50be45. Snapshot and build outputs remain outside Git.

Branch builds on pending Shinobi Tactics/Walk It Off retirement 7ee3054. Time Assault fixed spawn remains a separate branch. Deployment requires separate authorization; rollback to prior server revision. After deployment, verify station, medkit, hero-heal and mixed-regen cases in the client, and reconnect to receive corrected catalogue descriptions. No public feed release performed.
