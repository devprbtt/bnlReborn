# Score breakdowns (prepared, not deployed)

ActionScoreLedger keeps cumulative points by reason independently from its bounded notification queue. Breakdown totals use the granted, capped points, including Objective bonus as its own source. Zero-score players get an empty breakdown; unavailable historical data stays null.

Baseline/reconnect snapshots and the final snapshot before SendEndMatch contain `players[].reasons` arrays (`reason`, `points`). Routine updates keep the existing award stream and omit full breakdowns. Older clients ignore the optional JSON field.

CompletedMatchPlayer.ScoreBreakdown is appended in an optional type-3 archive trailer, after the existing type-2 rating block (also emitted for rating version zero). Existing readers ignore the trailing score block. An additive nullable `MatchPlayers.score_breakdown` TEXT column stores JSON; the archive API exposes it as `score_breakdown`. Older rows stay null. No replay dependency or automatic historical backfill.

Client hover shows sources in descending score order with points and one-decimal percentages (trailing zero omitted). It validates sum == total before showing percentages; zero and unavailable states are explicit. Long lists scroll and plain text prevents markup injection. Both end-match SCORE and history SCORE use the same component.

Requires server deployment for recording and a client release for hover UI. Branch feature/score-breakdown-tooltips includes the pending fire-trap rebalance. No live deployment or database writes performed during implementation.
