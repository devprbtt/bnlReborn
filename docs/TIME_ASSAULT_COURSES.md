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

### Completion timing and final client readiness

The matching client advertises capability 14. Server sends zone message 114 plus a fresh UInt64 token after sending the TA snapshot; client returns that token only after its loader is done, its local player and HUD exist, and the loading screen is gone. The server holds the prestart countdown until acceptance. Stale, duplicate and replaced-connection tokens are ignored; timeout does not bypass readiness. TA player creation is included during loading to avoid a circular wait. Legacy clients preserve their native handshake and do not gain this final presentation guarantee without updating.

Completion displays use rounded milliseconds with insignificant trailing zeros removed (02:25.25, 00:12.1, 00:00.005). Stored server result floats and leaderboard ordering remain unmodified. 65 server fixture checks cover course behavior and readiness guards; live client/server playthrough remains pending.
