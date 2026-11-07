# Time Assault course selection

Prepared 2026-11-07. The existing CDB global_logic.time_trial.courses list supports multiple courses. The client and region server previously always used its first entry; current catalogue data includes six maps with their own hero, device loadout, goals and objectives.

The new Matchmaker request 35 carries a uint16 RPC ID and a map Key. Its reply carries the same RPC ID, success discriminator byte 0, then a boolean start result. Region validation requires a connected solo player outside a running instance, an unambiguous configured course, a valid hero/map card and a loadable TimeTrial map. It validates before leaving a queue. Existing request 17 keeps its featured-course behavior for old clients.

Restart resolves the current instance's MapInfoCard before teardown. It does not reset to the first course. A still-loading instance ignores restart, and missing/removed map/course data leaves the current run untouched.

No CDB edits or database migrations are necessary. Deploy the server before the client that exposes the new picker. No deployment has been performed. The separate casual free-pick branch is not included.

Validation: Release warning-as-error build; TimeAssaultFixture with the local catalogue export checks all six real .bnlbin payloads, course-specific data, legacy fallback, invalid/duplicate rejection, service request/reply, launch guards and restart guards (48 checks). Restart tests stop at the teardown boundary rather than creating live network zones; no full live playthrough is claimed.

Run from server root:

```powershell
dotnet run --project tests/BNLReloadedServer.TimeAssaultFixture -c Release -- <catalogue.json> <fixture-output-directory>
```

Player-facing note: Choose any available Time Assault course from the Play menu. Each course keeps its own hero, loadout, objectives and records. Restart retries your chosen course instead of returning to the featured map.
