

## 2026-10-19 - Replay seek flow control

- Forward/skip-build continue in place; rewind reconstructs the recorded world. Capability 10 gates ordered zone 113 barriers, acknowledged through replay command 5. Batches stop at 128 records/32 KiB, await actual client consumption, and time out after 30 seconds. Final target/resume waits for the last ACK. Legacy clients retain paced batches.
- Replay controls fixture: 37 checks (300-record burst, bounded queue, forged/stale ACKs, repeated seeks, pause preservation, cancellation); TeamFeatures: 170 assertions. Companion Unity client fixture: 24 checks including frame-budget behavior and compact UI. No deployment yet.
