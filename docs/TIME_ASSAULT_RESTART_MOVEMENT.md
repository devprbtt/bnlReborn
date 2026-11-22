# Hold the authored Time Assault start until GO

Prepared on production `54a8a3b287de8beb6b00ed566b079067261dca72`. Not deployed.

Player-facing note: Holding movement keys while restarting Time Assault can no
longer change the starting position before GO. Loading, READY and countdown all
hold the runner at the starting position. Normal movement resumes at GO.

The client prestart predicate covered Waiting/Build but omitted TutorialInit,
the initial phase of a restarted TimeTrial zone. ReceivedMoveRequest also lacked
an authoritative prestart restriction, so early client prediction could become
the real start position before the visible countdown stopped further movement.

The server now consumes TimeTrial player movement in TutorialInit, Waiting and
Build without moving the unit or invoking UnitMoved, terrain, trap or pickup
callbacks. Fresh aim and monotonic heartbeat timestamps are retained; locomotion
velocity/flags are cleared. The unbuffered native Teleport maneuver corrects
position prediction immediately, even before the game simulation buffer starts.
No new protocol capability or client build is needed for that authority guard.
Packets sampled before the Assault start timestamp are rejected even if they
arrive after GO; late packets cannot rewind a runner who already started.
Fresh movement at GO and other-mode/non-player movement retain their old path.

The paired client change covers TutorialInit and missing initial phase, and
returns before CharacterController.Move or any movement layer while locked.
It clears carried sprint/climb/jump/dash/ground-slam, velocity, acceleration,
impulse/pull/slip state. This removes visual prediction drift too, and needs a
future client build. It is recorded in the client repository under
docs/TIME_ASSAULT_RESTART_MOVEMENT.md.

## Validation

The new fixture fails against the unchanged production source with
`held Shift+W cannot move exact authored spawn during TutorialInit`. It passes
6,900 checks against the fix: six actual courses, initial load plus two restarts,
25 held-input movement packets per prestart phase, exact authored position,
zero locomotion state, retained aiming, no movement callbacks, native correction
wire payloads, stale/reordered packets, release at GO, other modes and non-player
units. Existing fixed-spawn fixture passes 504 checks. Release build passes with
warnings as errors. Client Unity edit-mode fixture passes 4,326 checks, executing
the actual MovementController.Update guard over 120 frames per phase/restart.
This is controlled regression coverage, not a live keyboard/gameplay acceptance.

From this repository root:

```powershell
dotnet run --project tests/BNLReloadedServer.TimeAssaultMovementFixture -c Release -- <raw-live-catalogue.json>
dotnet run --project tests/BNLReloadedServer.TimeAssaultSpawnFixture -c Release -- <raw-live-catalogue.json>
dotnet build BNLReloadedServer -c Release -warnaserror
```

For the before-fix reproduction, add
`-p:ServerProjectPath=<production-source>/BNLReloadedServer/BNLReloadedServer.csproj`
before `--`. Run from a server checkout containing the six Maps payloads.
Fixture snapshot SHA256:
`f42342e58ff7dcb1a98d6a0ba820c6f6b5411d1d2bde1478fab961bb13dbf48f`.

No map, CDB, timer, trap-stat, reward or leaderboard edits. Previous records
retain runs with the old behavior. Deployment follows separate authorization.
Live acceptance: hold Shift+W continuously through loading and several restarts,
check the exact countdown start position on each course, and verify immediate
movement at GO. Rollback restores the preceding server revision.
