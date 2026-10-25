# Personal MMR v2 and abandonment settlement

Implemented on feature/mmr-v2-abandonment from production dbceafb. Not deployed. Public friendly/ranked MatchmakerInitiator matches only; clan/custom/training stay separate. No historical penalties or reset.

## Rating policy

PersonalMmr.Calculate is shared by settlement, fixtures and replay. Preserve native TrueSkill units (display x100). Initial rosters must exceed two players on at least one side to move MMR. Genuine backfill mean/uncertainty remain frozen; initial presence takes precedence over a contaminated backfill flag. Reconnect never changes origin.

Partial-play weights describe lineup participation; a fixture verifies the installed Moserware library honors weight. Returning originals have full weight and no personal/shortage protection. A win keeps its ordinary base; a loss uses the ordinary 1.25x ceiling so reconnecting cannot erase a weak-performance loss penalty. A timely return avoids the abandonment cooldown. No explicit time-discounted loss.

Contribution is 40% averaged kill/assist/enemy-damage percentiles, 30% effective ally healing, 30% positive-only objective percentile. Zero/below-reference objectives stay neutral without defense/opportunity telemetry. Raw destruction, legacy Destroyed, building, bricks, low deaths and action-score totals never award points. Missing components stay neutral, never redistributed.

Require matching hero, queue, map, match type and perk/device loadout; at least 180 seconds and 80% active participation, single hero/loadout, no disconnect, and 100 reference appearances from 20 players. Sparse cohorts deliberately disable performance. Win factor 1+.50P; loss factor 1-.75P for P>=0, otherwise 1-.25P. Reference/weight choices are provisional, not fitted optimums.

Missing-slot exposure uses first assault through authoritative end; missing active boundaries disable shortage. Short-team win/loss factors 1+.50h / 1-.75h; favored-team win 1-.75h, loss 1. Final combined factors are [.25,1.50] wins and [.25,1.25] losses. Discounts scale variance movement; boosts never accelerate certainty. The active interval includes later build/intermission phases; a future per-phase timeline could refine this heuristic.

## Abandonment and queueing

Both public queues use at most 45 disconnected seconds in total across the match (shorter configured grace still applies to each disconnect). Explicit Quit/Inactivity, unrecovered Disconnect at match end, or a total reconnect gaps over 45 seconds means abandonment. Original abandoners lose 1.25x the more severe of original-roster counterfactual loss and final-lineup counterfactual loss with that player fully weighted. Minimum counterfactual loss .25 native units gives a minimum penalty of 31.25 displayed points. No personal/shortage discount and no second end-match loss, even when the team wins.

An all-left Abandoned result penalizes documented abandoners. Unknown/server-invalid outcomes are void. Backfillers retain zero MMR but get misconduct cooldowns. Post-outcome departures do not count: reward-screen delays do not extend the punishable interval. Missing a player unit never exempts a quitter because settlement traverses the participation archive. Small-match MMR exemptions do not exempt queue penalties.

Cooldowns are 5 / 15 / 60 minutes for zero / one / two-or-more previous abandonments within 24h. They begin at settlement, persist in SQLite and reload on restart. Pending-match locks prevent another public match before settlement. Squad entry checks every member; Matchmaker.AddPlayer checks again. Notifications explain pending results/cooldowns. Network failure cannot be distinguished from Alt+F4; timely actual return is the exemption. No absolute claim that collusion or undetected network loss is impossible.

## Storage and recovery

CompletedMatchRecord appends a rating trailer carrying version, active start, match type and effective healing. Old packets remain version 0. New public matches stop independent immediate-quit/end-list rating calls. Deploy as one drained server update, not mixed old/new settlement paths.

MmrInbox durably freezes the first binary input. Under the master player-write lock, one SQLite transaction writes MmrMatches/MmrSettlements, updates only Users rating columns, updates ranked placement/season ratings, records cooldowns and the archive. Match/player keys omit version so another version cannot pay again. Existing legacy archives are rejected. Out-of-order overlapping results require explicit operator correction, never another increment.

The inbox is acknowledged only after committed rating caches are refreshed; a post-commit refresh failure is retried without awarding twice.

Inbox recovery runs after startup and every 30 seconds in chronological order. Cache ratings and league state refresh before queue locks release; bans publish only after commit. Recovery failure leaves affected players blocked and logs errors. In-progress reservations are process-local: interruption before a completed result is persisted is a server incident, not invented misconduct. A restart clears those interrupted in-progress reservations but does not clear persisted cooldowns or pending completed results.

Effective ally healing reuses RepairDamageCredit on players: actual enemy health damage creates a budget, every actual heal consumes it, and only a different allied player earns ScoreType 84. Self/fall/friendly damage, overheal and passive recovery cannot create credit. This prevents simple self-damage/heal loops; cooperative opponents remain an abuse-monitoring concern. Metadata and effective healing survive in immutable MmrMatches payloads; old public profile counters remain compatible.

## Calibration and replay

No calibration file is shipped. Missing/invalid Configs/mmr-references-v2.json means neutral performance; outcome/shortage/abandonment rules still function. Generate candidate references from new v2 telemetry, review offline on held-out later matches, then install a validated calibration separately:

```powershell
dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release
dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release -- --replay snapshot.json report.json
dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release -- --calibrate playerData-copy.db references.json CUTOFF_UNIX_MS
```

Calibration opens SQLite read-only, deduplicates matches and excludes abandoned/short/switched/disconnected appearances. Reference timestamps strictly precede evaluated matches. Historical build-inclusive duration and generic healing cannot silently become v2 training samples. Generating a calibration file does not activate it.

October 25's 30-day snapshot: 124 matches / 1,073 appearances / 68 players (110 casual, 14 ranked), all parseable with no missing rating seeds. Replay flags 65 possible abandonments and 20 genuine backfills. Comparator: original-roster outcome-only TrueSkill, not exact legacy quit-bug behavior. Historical performance adjustments are zero because required telemetry/reference coverage is missing. A separate sensitivity run uses archive/build start for shortage. Older completion anomalies mean flags are not grounds for retrospective punishment.

Reevaluation did not demonstrate better prediction; abandonment penalties remove more MMR overall. These are enforcement/fairness choices, not proof of a better estimator. See client reports/mmr-30day-20261025.json and hashed source provenance. Further validation needs clean new telemetry, ranked/casual separation, calibration/drift and repeated-player/party audits.

## Delivery

Commit/push all server changes to devprbtt/bnlReborn. Deployment is separate. Before release prepare matching versioned changelog, launcher PatchNotes.cs, in-game RebornHomeNotes.json and website notes. Launcher/in-game notes are not live until the later signed client release. This branch reserves no release identity and publishes nothing.
