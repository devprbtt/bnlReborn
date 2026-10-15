# Live spectate routing (2026-10-14)

Live matchmaking browser IDs start at `0x8000000000000001`. Replay IDs also set
the high bit. Checking `ReplayStore.IsReplayId` before the live registry sent
ongoing-match spectate requests to replay lookup, returning `NoSuchGame`.

Both admission checking and joining now resolve registered live matches first.
Unmapped high-bit IDs still use replay lookup; custom-game IDs retain their
existing path. Existing IDs and the wire protocol are unchanged. This preserves
already-issued browser IDs and client replay hashes. A theoretical exact replay
hash/live-ID collision resolves to the registered live match; disjoint ID ranges
would require a separate compatibility migration.

Validation: `dotnet run --project tests/BNLReloadedServer.LiveSpectateFixture`
reproduced rejection before the fix and passes 11 checks after it. The replay
recorder fixture passes 48 assertions. No deployment was performed.
