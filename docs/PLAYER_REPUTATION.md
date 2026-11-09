# Player reputation v1

Server-owned public reputation, separate from skill rating. Every existing/new player starts at 100. No historical backfill: the first startup creates a persistent policy epoch; matches started before that epoch do not count. Deploy with the corresponding client presentation in a later release.

## Rules

- Casual and ranked matches only. Unknown/cancelled results and custom/practice/TA games do not affect reputation.
- First abandonment costs 10, second consecutive abandonment 15, third and subsequent abandonments 20. No daily reset or idle-time recovery.
- Quit, AFK removal, and disconnects exceeding the grace allowance count as abandonment. Only one event per player/match. Quitting then returning does not undo the deduction.
- A qualifying clean completion grants 1 point and resets the abandonment streak. It requires staying through the result, at least 180 seconds of active-phase participation, and positive recorded action score. Winning and score magnitude do not affect recovery. Backfills qualify on the same criteria; brief or inactive completions neither recover points nor reset the streak. Accepted surrender is a normal finish.
- Disconnect absence is accumulated across a match, with a 120-second budget. Returns at exactly 120 seconds are accepted. A final connection loss with less than this budget left before the result incurs no reputation deduction, but earns no completion reward. Existing skill/cooldown logic still considers an unreturned disconnect abandonment; its timed-return allowance is now also 120 seconds. The existing shorter configured reconnect cap remains respected.
- Disconnect expiry no longer relabels the participation record as an explicit quit. Explicit leaving still does.
- Clamp scores to 0..100. No chat penalties, reputation-based bans, matchmaking segregation, or new queue cooldown schedule in v1.

## Storage and API

ReputationPolicy stores the rollout epoch. ReputationEvents stores each player's match ID, reason, actual score change, resulting score and consecutive-abandonment state. Events are committed in the same transaction as the archived match. Duplicate IDs cannot score again; late deliveries recompute the player's ledger in deterministic result order. Restarting does not reset the score or streak. Changes settle at match completion, not at initial socket loss.

GET /api/reputation/{playerId} is public and returns version, player_id, score and status. Missing players return 404; invalid IDs return 400. Public writes are not supported. No participant details or private ledger are exposed. Clients display unknown when the endpoint is unavailable, not a fabricated 100.

Bands: 80..100 Good (green), 60..79 Fair (yellow), 40..59 Low (orange), 0..39 Very low (red). The number and label accompany color for accessibility.

## Verification

Run `dotnet run --project tests/BNLReloadedServer.ReputationFixture -c Release`. Covers persistent score/streak, subsequent-day escalation, qualifying recovery, floor, idempotency, late results, transactional rollback, exclusions, two-minute boundaries, backfills, surrender, public read-only route, missing player and real API serialization. Also run MmrFixture, BackfillReentryFixture, ReconnectFixture and ZoneLeaveOrderFixture after reconnect changes.

No production deployment performed for this feature. Production still runs supply-schedule hotfix 306b1f5.
