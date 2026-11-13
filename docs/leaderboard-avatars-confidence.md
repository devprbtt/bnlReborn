# Leaderboard avatars and rating confidence

Player stat leaderboard rows add optional `steam_id` (decimal string) and `rating_deviation` (nullable double). Steam IDs remain exact through JSON readers that use floating-point numbers. All boards include known account identities; archived players without a current account return null. Rating deviation is included only for Casual MMR and comes from the account's active TrueSkill standard deviation. Nonfinite/missing uncertainty is null. Ranking, match minimums, paging, and values are unchanged; no schema/protocol migration.

The legacy rating_volatility column is not updated by the current rating path, so it must not be displayed as active volatility. The client instead labels uncertainty as RATING CONFIDENCE: high at SD <= 2.5, medium <= 5, low above 5. These are presentation bands around the default starting SD 25/3, not ranking rules or predictions of the next rating change. Missing uncertainty displays an em dash.

Validation: PlayerLeaderboardFixture includes six production-path checks for exact Steam IDs, per-account deviation, missing identity, all-board avatar metadata, rating-only scope and JSON fields, plus existing leaderboard regressions. Deployment remains separate; older clients ignore additive fields, and newer clients tolerate older responses without them.
