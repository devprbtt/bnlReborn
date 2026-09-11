# Block Buster auto-break countdown

Timed Common units labelled SupplyBlockbuster expose the server expiration
deadline through the existing UnitUpdate.BombTimeoutEnd field. No wire-layout
change. Bombs and loose pickups are excluded. The deadline is sent immediately
after creation on the same channel and in full snapshots for late joiners.
Lifetime remains CDB-driven and CleanUpExpired still performs auto-break.

Updated clients show an animated Edo SZ warning during the final ten seconds;
early destruction hides it and overlapping timers show the earliest first.
Old clients accept the existing field but do not gain the animated HUD.

Validation: dotnet run --project tests/BNLReloadedServer.BlockbusterFixture
Eight checks cover lifetime, stable snapshots, wire round trip and exclusions.
Source push is separate from deployment.
