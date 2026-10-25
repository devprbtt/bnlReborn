# Personal MMR v2 and abandonment settlement

Implemented on feature/mmr-v2-abandonment from production dbceafb. Code revision f81d8db305cb8f159f7a5be8c1ff13c0e1360c38 deployed on October 25, 2026 at 15:22 UTC after owner authorization to restart immediately. Public friendly/ranked MatchmakerInitiator matches only; clan/custom/training stay separate. No historical penalties or reset.

## Rating policy

PersonalMmr.Calculate is shared by settlement, fixtures and replay. Preserve native TrueSkill units (display x100). Initial rosters must exceed two players on at least one side to move MMR. Genuine backfill mean/uncertainty remain frozen; initial presence takes precedence over a contaminated backfill flag. Reconnect never changes origin.

Partial-play weights describe lineup participation; a fixture verifies the installed Moserware library honors weight. Returning originals have full weight and no personal/shortage protection. A win keeps its ordinary base; a loss uses the ordinary 1.25x ceiling so reconnecting cannot erase a weak-performance loss penalty. A timely return avoids the abandonment cooldown. No explicit time-discounted loss.

Contribution is 40% averaged kill/assist/enemy-damage percentiles, 30% effective ally healing, 30% positive-only objective percentile. Zero/below-reference objectives stay neutral without defense/opportunity telemetry. Raw destruction, legacy Destroyed, building, bricks, low deaths and action-score totals never award points. Missing components stay neutral, never redistributed.

The open-beta default automatically pools maps/loadouts within the same hero, queue and match type, requiring 10 eligible appearances from five players. Use only v2 records in the last 30 days among the latest 500 settlements, completed before the new match; small unrated matches cannot train beta cohorts. At least 180 seconds and 80% active participation, single hero/loadout and no disconnect are still required. Exact manually supplied map/loadout cohorts require 100 appearances/20 players and take precedence. Sparse cohorts keep performance neutral while outcome, shortage and abandonment apply. Win factor 1+.50P; loss factor 1-.75P for P>=0, otherwise 1-.25P. Reference/weight choices are provisional, not fitted optimums.

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

No historical calibration file is shipped. Missing/invalid Configs/mmr-references-v2.json falls back to automatic beta-v2 cohorts from immutable settled matches. Fresh effective ally-healing counters enable the utility component without an operator step once sufficient samples exist. Exact offline calibration remains available as an override:

```powershell
dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release
dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release -- --replay snapshot.json report.json
dotnet run --project tests/BNLReloadedServer.MmrFixture -c Release -- --calibrate playerData-copy.db references.json CUTOFF_UNIX_MS
```

Calibration opens SQLite read-only, deduplicates matches and excludes abandoned/short/switched/disconnected appearances. Reference timestamps strictly precede evaluated matches. Historical build-inclusive duration and generic healing cannot silently become v2 training samples. Generating an exact calibration file does not install it. The automatic beta path is active at runtime and reconstructs cohorts after restart; it is not shadow mode. Unsupported heroes remain outcome/shortage-only during their warm-up. No class coefficients or the 75%/25% loss sensitivities were changed. Historical bootstrap was rejected because the recent usable sample covered only one hero at beta thresholds.

October 25's 30-day snapshot: 124 matches / 1,073 appearances / 68 players (110 casual, 14 ranked), all parseable with no missing rating seeds. Replay flags 65 possible abandonments and 20 genuine backfills. Comparator: original-roster outcome-only TrueSkill, not exact legacy quit-bug behavior. Historical performance adjustments are zero because required telemetry/reference coverage is missing. A separate sensitivity run uses archive/build start for shortage. Older completion anomalies mean flags are not grounds for retrospective punishment.

Reevaluation did not demonstrate better prediction; abandonment penalties remove more MMR overall. These are enforcement/fairness choices, not proof of a better estimator. See client reports/mmr-30day-20261025.json and hashed source provenance. Further validation needs clean new telemetry, ranked/casual separation, calibration/drift and repeated-player/party audits.

## Delivery

Commit/push all server changes to devprbtt/bnlReborn. Deployment is separate. Before release prepare matching versioned changelog, launcher PatchNotes.cs, in-game RebornHomeNotes.json and website notes. Launcher/in-game notes are not live until the later signed client release. Server release identity is MMR v2 beta; no client version bump. Website and launcher feed are published, while compiled launcher and in-game notes remain prepared for a later signed client release.

## Beta audit and rollout

Run `python tools/mmr_beta_audit.py /path/to/playerData.db --days 7` on a read-only-accessible database or consistent backup. Reports actual net changes, abandonment counts, ordinary adjustments and standardized performance uplift by hero/class; no credentials or account sessions are read. Zero performance is not synonymous with missing calibration. Check class/hero coverage and actual losses as well as aggregate drift.

Deploy only after draining active matches, preserving a consistent SQLite backup, and verifying health on the new server revision. Do not mix v2 result producers with old settlement consumers. No historical reset, recalculation or retrospective penalty. The client wire remains compatible. Code and automated fixtures do not substitute for a live multiplayer acceptance check. Player notes are under MMR v2 beta. The owner separately authorized deployment and online-player notifications; no offline notification campaign was requested in the final rollout instruction.

Deployment verification: active release `/opt/bnlreloaded/releases/f81d8db305cb`; DLL SHA256 `858cdae31d0b617694714663a7b36b6de6fd6f65efeef5dd86d491d2ec202260`; dependency/runtime JSON matched the previous release semantically. Database backup `/root/config-backups/mmr-v2-before-20261025T152240Z/playerData.db` passed quick_check (217 accounts). Rollback pointer snapshot `/root/config-backups/bnl-server-hotfix-20261025T152248Z`. Service active, NRestarts=0, no error-priority journal entries; new MMR tables empty at verification, so no live settled-match acceptance claim. Notifications broadcast to seven online players before restart and five online players after startup. Website/feed plus rules page published and public hashes verified.

## New-player starting rating

New accounts start at native mean 15 (1500 displayed Casual MMR); uncertainty remains 25/3. Both PlayerRecord and PlayerData use the shared default. Existing stored means are not rewritten by this default change. Legacy untouched 2500 accounts and new untouched 1500 accounts remain outside the legacy ladder. The separately authorized one-time Casual adjustment rounds existing means to the nearest 500 displayed points, midpoint up, capped at 4000; existing ranked rows and uncertainty are preserved.

Follow-up 068f927fb3fd0e785af239e92f2ddfacd686c144 deployed at 2026-10-25 15:33 UTC in the same maintenance stop as the authorized rounding transaction. All 217 accounts processed: 134 changed, 83 unchanged; three above 4000 capped. Backup and per-account before/after audit: /root/config-backups/casual-mmr-round-20261025T153313Z/. A digest over every table and every Users column except rating_mean remained identical inside the transaction. Existing Ranked rows, uncertainty and historical records therefore stayed unchanged. Post-start all 217 means verified at 500-point steps and <=4000; service/listeners/API healthy, NRestarts=0, no error journal entries. DLL SHA256 934e896339ebb3d3d05a46dc073875345af31c4c4718be4d6d28a5da91ceb03a. Notifications reached six online players before restart and three after. Published notes verified; compiled launcher/in-game notes await a later signed client. The 4000 cap is only for this one-time adjustment, not subsequent earned ratings. Tests: MMR 3603 checks and League fixture pass with warnings as errors.
