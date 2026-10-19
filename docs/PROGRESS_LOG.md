

## 2026-10-19 - Replay seek flow control

- Forward/skip-build continue in place; rewind reconstructs the recorded world. Capability 10 gates ordered zone 113 barriers, acknowledged through replay command 5. Batches stop at 128 records/32 KiB, await actual client consumption, and time out after 30 seconds. Final target/resume waits for the last ACK. Legacy clients retain paced batches.
- Replay controls fixture: 37 checks (300-record burst, bounded queue, forged/stale ACKs, repeated seeks, pause preservation, cancellation); TeamFeatures: 170 assertions. Companion Unity client fixture: 24 checks including frame-budget behavior and compact UI. No deployment yet.


## 2026-10-19 - Replay loading clock and bounded seek pipeline

- Replaced stop-and-wait per catch-up batch with at most eight outstanding batches, preserving 128-record/32KiB batch limits and ordered acknowledgements. Final state still waits for all consumed data. Invalid/stale/foreign ACKs cannot advance the window.
- Capability 11 adds negative final barriers: client waits for the Zone spectator HUD to finish loading before acknowledging. Initial playback now reconstructs only through the first non-waiting phase and holds the clock until ready; rewinds clamp to that playable start to avoid a waiting-loader deadlock. Legacy clients retain compatible behavior.
- Validation: 41 server functional checks; optional benchmark adds 12 checks. Same fixture path, 1500 records, injected 100ms ACK delay, three samples: window 1 = 1309/1317/1319 ms (median 1317); window 8 = 227/227/229 ms (median 227). Reproduce with dotnet run --project tests/BNLReloadedServer.ReplayControlsFixture -c Release -- --seek-window-benchmark. This isolates network pacing, excludes client map loading, and is not an end-to-end rewind speed claim. TeamFeatures 170 assertions passed.
- Client compiled successfully using Unity's Roslyn compiler and existing editor response file, with outputs redirected into artifacts/replay-seek-window so the user's running editor was not interrupted. Real replay test still needed. Rewind still reloads/reconstructs because old recordings contain no intermediate snapshots; checkpoint seeking remains a larger change, not implemented here.
