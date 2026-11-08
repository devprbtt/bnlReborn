# Leave cooldown timing

Cooldown expiry is the qualifying departure timestamp plus the existing 5/15/60-minute escalation. Saving a match result no longer starts or extends this clock. The unfinished-match reservation still prevents queueing until the result is saved; afterwards only any unserved cooldown remains. Existing reconnect forgiveness, abandonment classification, escalation history window, and longer existing penalties are preserved.

For repeated disconnects, use the departure in the segment that exhausts the cumulative reconnect grace. No database migration is required. Previously settled penalties are not rewritten.

Validation (2026-11-07):
- `dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release`: 3656 checks passed, including departure timing, late settlement/retry, persistence, reconnect, escalation, and pending-match guards.
- ReconnectFixture: 10 checks passed.
- PublicQueueRulesFixture: 64 checks passed.
- BackfillReentryFixture: passed.

Prepared server-side only; not deployed or verified against a live match.
