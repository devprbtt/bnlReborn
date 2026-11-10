# Time Assault fixed spawn (2026-11-09)

Player-facing note: Every player now starts and restarts each Time Assault course at the same exact position. Removed random starting offsets that could place some players closer to the exit.

Prepared, not deployed. Each of the six configured course maps already contains one Team1 base marker. The shared spawn resolver previously sampled up to 25 positions in a two-block radius around that marker. TimeTrial now uses the exact authored X/Z and standard +0.08 Y feet clearance. Existing map-authored facing is retained. The common resolver covers initial creation, restart (fresh zone), respawn and recall. Time Assault ignores client spawn-selection/deselect requests so they cannot override the course default. No map assets, rewards, timers or existing leaderboard records are changed; historical records can include the previous random offset.

Validation on the actual six course payloads: 504 fixed-spawn checks (16 independent zone/player states per course, three radii, default marker, selection rejection, terrain foot/head clearance; other-mode scatter retains 25 positions). Existing TimeAssaultFixture: 106 checks. Release build with warnings as errors: zero warnings/errors. No live client/course playthrough performed.

From the repository root:
```
dotnet run --project tests/BNLReloadedServer.TimeAssaultSpawnFixture -- <catalogue-json>
dotnet run --project tests/BNLReloadedServer.TimeAssaultFixture -- <catalogue-json> <output-directory>
dotnet build BNLReloadedServer -c Release -warnaserror
```

Catalogue fixture input: existing snapshot J:/github/blocknload-unity-upgrade/extracted/work/beta92-server-inputs/server-live-cards.json. SHA-256: 03c9857c2e9bebc487fc1fc651c5ec7fcbac3331ee9c506d7188f708ca50be45. Input snapshot/build output remain outside Git.

Deploy separately after authorization. No client rebuild or database migration needed. Roll back by restoring the preceding server revision. Live acceptance: start/restart each course several times and compare position/facing at the countdown, then complete a run.
