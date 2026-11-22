# Roly F death/fast-respawn cooldown — 2026-11-22

Prepared and tested against live base `0fee0bc8001fa02a59ac4e1b950bddc4a1fd5ac5`; not deployed.

Trigger abilities retain their charge until cancellation or expiry. Death cleared their effects but retained `AbilityTriggered` and its deadline. A quick respawn inherited an active server toggle without its effects and with a ready-looking charge. The next F request cancelled that stale toggle instead of activating the ability.

On the first native death transition, clear the active toggle/duration and call the existing `AbilityUsed(update)` in the death UnitUpdate. This consumes one charge, schedules native cooldown including existing reductions, and publishes charge zero and the exact endpoint immediately. Respawn already transmits that endpoint on the new unit id. Existing cleanup removes trigger effects. Duplicate death and death during an existing cooldown do not reset or consume it again. Manual cancellation and expiry retain their existing behavior.

This lifecycle fix covers both normal Roly F and Rolling Wild without hardcoded tuning or client/CDB changes. Both current variants retain their configured 45-second cooldown, now counted from death. A press during cooldown is rejected without moving its endpoint; the first press after recharge activates normally.

Validation: the new actual-GameZone fixture reproduces the stale-toggle failure on unchanged live source. Fixed code passes **34 checks** across both catalogue variants: activation, death, cooldown packet, effects cleanup, duplicate death, immediate respawn/new-id packet and snapshot, rejected early press, working first press after recharge, manual cancellation and no reset on later death. Existing cooldown21, action-score308 and Second Wind68 checks pass (**431 assertions total**). Release warnings-as-errors build: zero warnings/errors.

A separate KillAccoladeFixture fails its pre-existing archive-layout assertion, `an untracked record ends with the block version and an empty list`, on both unchanged live source and the fixed branch. Both logs are retained; no archive code changes are included.

```powershell
dotnet run --project tests/BNLReloadedServer.TriggerDeathCooldownFixture -c Release -- <deployed-candidate-catalogue.json>
dotnet run --project tests/BNLReloadedServer.AbilityCooldownFixture -c Release
dotnet run --project tests/BNLReloadedServer.ActionScoreFixture -c Release
dotnet run --project tests/BNLReloadedServer.SkillRecoveryFixture -c Release
dotnet build BNLReloadedServer -c Release -warnaserror
```

Evidence/source hashes: client `reports/roly-trigger-death/` and `extracted/work/roly-trigger-death-20261122/`. Source roots were not changed. Branch `fix/roly-trigger-death-cooldown-20261122` must be committed/pushed before rollout; deployment remains separate. No live writes, service restart or client/feed publication performed. After authorized deployment, test low custom respawn times with both variants; existing clients need no update. Retain production0fee0bc for rollback.

Player-facing note: Roly's F now ends and starts its cooldown immediately on death, so quick respawns show the correct remaining cooldown and cannot leave a stale toggle behind.
