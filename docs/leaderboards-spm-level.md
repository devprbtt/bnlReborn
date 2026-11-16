# Score per minute and player level leaderboards

The existing public `GET /api/leaderboards/players` endpoint accepts two additional `stat` names:

- `score_per_minute`: sum of archived `MatchPlayers.total_score` divided by merged, connected playtime in minutes. Uses the same eligible Ranked/Casual rounds as the existing match leaderboards, needs 20 valid counted rounds, and supports `period=all` and `period=30d`. Negative-score rounds are excluded; zero-score rounds still contribute time. Values round to two decimals after aggregation, not per match.
- `player_level`: current account level from stored progression. Includes valid accounts without a match minimum. Invalid/missing progression is skipped. The board always returns `period=all`, even if requested with `30d`; level is current progression rather than an activity-window statistic. Equal levels share rank and order by player ID. Rows retain lifetime qualifying match counts for the existing matches column.

Response version, paging, nicknames and rank conventions are unchanged. These boards do not add new trophy definitions or grants. The client displays the existing progression insignia beside each level number.

Validation: `dotnet run --project tests/BNLReloadedServer.PlayerLeaderboardFixture -c Release -warnaserror`. This covers weighted score/time aggregation, recent filtering, custom-game exclusion, minimum rounds, account levels, ties, malformed progression and paging. Setting `BNL_LEADERBOARD_FIXTURE_OUTPUT` writes synthetic JSON for the Unity UI review.

Deploy this server support before releasing the client options. This branch does not deploy a server or publish a client.
