

## 2026-10-19 - Replay seek flow control

- Forward/skip-build continue in place; rewind reconstructs the recorded world. Capability 10 gates ordered zone 113 barriers, acknowledged through replay command 5. Batches stop at 128 records/32 KiB, await actual client consumption, and time out after 30 seconds. Final target/resume waits for the last ACK. Legacy clients retain paced batches.
- Replay controls fixture: 37 checks (300-record burst, bounded queue, forged/stale ACKs, repeated seeks, pause preservation, cancellation); TeamFeatures: 170 assertions. Companion Unity client fixture: 24 checks including frame-budget behavior and compact UI. No deployment yet.


## 2026-10-19 - Replay loading clock and bounded seek pipeline

- Replaced stop-and-wait per catch-up batch with at most eight outstanding batches, preserving 128-record/32KiB batch limits and ordered acknowledgements. Final state still waits for all consumed data. Invalid/stale/foreign ACKs cannot advance the window.
- Capability 11 adds negative final barriers: client waits for the Zone spectator HUD to finish loading before acknowledging. Initial playback now reconstructs only through the first non-waiting phase and holds the clock until ready; rewinds clamp to that playable start to avoid a waiting-loader deadlock. Legacy clients retain compatible behavior.
- Validation: 41 server functional checks; optional benchmark adds 12 checks. Same fixture path, 1500 records, injected 100ms ACK delay, three samples: window 1 = 1309/1317/1319 ms (median 1317); window 8 = 227/227/229 ms (median 227). Reproduce with dotnet run --project tests/BNLReloadedServer.ReplayControlsFixture -c Release -- --seek-window-benchmark. This isolates network pacing, excludes client map loading, and is not an end-to-end rewind speed claim. TeamFeatures 170 assertions passed.
- Client compiled successfully using Unity's Roslyn compiler and existing editor response file, with outputs redirected into artifacts/replay-seek-window so the user's running editor was not interrupted. Real replay test still needed. Rewind still reloads/reconstructs because old recordings contain no intermediate snapshots; checkpoint seeking remains a larger change, not implemented here.

## 2026-10-21 - Action scoring deployment preparation

- Owner authorized deploying action scoring for the Unity custom-game sound test. Integrated feature/player-action-scores with current main, preserving replay pipeline/readiness changes.
- Linux x64 Release publish passed with warnings as errors. ActionScore 55, ReplayControls 41, KillAccolade 49, HealAttribution 17, Reconnect 10 and Auth 4 checks passed; HealAttribution retains an existing nullable fixture warning.
- Published dependency and runtime JSON match live 381a2c5 semantically (line-ending differences only). Existing live configuration and data are retained by the code-hotfix deployer, with rollback on startup health failure.
- Scoring uses built-in defaults when global_logic.action_scores is absent; catalogue values remain optional and editable. Deployment outcome is recorded in the client workspace progress log.

## 2026-10-21 - Hero blocks earn build and destruction action scores

- Include DeviceType.Hero alongside Device in both confirmed placement paths and enemy unit destruction. Reuse CDB build points, cost-based destruction values, objective bonuses and rebuild cooldown. Ordinary terrain, friendly and neutral destruction remain excluded.
- ActionScoreFixture passes 60 checks including hero build/destruction and exclusions; Linux x64 Release publish passes warnings-as-errors. Applying as follow-up to the authorized action-scoring deployment.

## 2026-10-21 - Hero-specific action scoring

- Added CDB-tunable support/action rewards for Tony, Trondson, Genie, Doc, Vander, Sweet Science, Yeti, Sarge, Roly, Cog, Astarella, Kira and Kreepy. Nigel/Ninja unchanged. Confirmed-build and destruction hooks resolve active aura sources; applied effects resolve owning players and throttle repeated applications; actual repair/heal amounts bank into chunks. Doc F healing replaces generic healing for the same amount.
- Lamp loot carries score-only builder provenance without changing gameplay ownership; collected pickup credits another friendly once. Hero block destruction also covers the voxel path.
- ActionScoreFixture: 92 checks. HealAttribution:17, live-catalogue JinxSurge:9, Permafrost:31 and IgnoreCaster:12 checks pass. The older AvalancheSlow fixture expects the obsolete immediate-slow ability and is incompatible with the current area-spawn catalogue; current Permafrost fixture validates the deployed area behavior instead. Initial missing fixture arguments were corrected.
- Companion client adds persistent Interface > Action score HUD, disabling both popup/audio without suppressing totals; Unity 18 popup/state +12 audio checks pass. Deployment is the authorized continuation of action scoring; details recorded in client progress log.
