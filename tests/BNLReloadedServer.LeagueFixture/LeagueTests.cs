using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.Database;
using Moserware.Skills;
using SQLite;

public static class LeagueTests
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static readonly DateTimeOffset Oct13 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        Rules();
        var path = Path.Combine(Path.GetTempPath(), "bnl-league-" + Guid.NewGuid() + ".db");
        try { Lifecycle(path); }
        finally { File.Delete(path); }
        Gate();
    }

    static void Rules()
    {
        Check(LeagueRules.TierOf(17.99) == LeagueTier.Bronze && LeagueRules.TierOf(18) == LeagueTier.Silver && LeagueRules.TierOf(24) == LeagueTier.Gold &&
              LeagueRules.TierOf(29) == LeagueTier.Masters && LeagueRules.TierOf(34) == LeagueTier.Champions && LeagueRules.TierOf(90) == LeagueTier.Champions,
            "tiers start at 1800, 2400, 2900 and 3400 MMR, and Champions has no cap");
        var resets = new[] { 47.56, 31.2, 26.0, 20.5, 9.0 }.Select(m => LeagueRules.Points(LeagueRules.SoftReset(m, 1.0).Mean)).ToArray();
        Check(resets.SequenceEqual([3500, 3000, 2500, 1900, 1500]), "a new season restarts each tier 100 MMR above its floor (Bronze at 1500): " + string.Join(", ", resets));
        Check(LeagueRules.SoftReset(26, 1.2).StandardDeviation == LeagueRules.ResetDeviation && LeagueRules.SoftReset(26, 5).StandardDeviation == 5,
            "a reset restores enough uncertainty for the new season to move");
        var dec = LeagueRules.SeasonAt(new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.Zero));
        Check(dec.Id == "2026-12" && DateTimeOffset.FromUnixTimeMilliseconds(dec.End) == new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "seasons are calendar months in UTC, across the year boundary");
        Check(LeagueSeasons.SeasonLabel("2026-10") == "October 2026 season", "seasons are named by month");

        // How far one even 5v5 loss moves a freshly reset player, and how many it takes to drop a tier.
        var team = Enumerable.Range(0, 5).ToArray();
        int losses = 0; var me = LeagueRules.SoftReset(26, 1);
        while (LeagueRules.TierOf(me.Mean) == LeagueTier.Gold && losses < 20)
        {
            var winners = team.ToDictionary(i => new Player<int>(100 + i), _ => new Rating(25.5, 3));
            var losers = team.ToDictionary(i => new Player<int>(i), i => i == 0 ? me : new Rating(25.5, 3));
            me = TrueSkillCalculator.CalculateNewRatings(Databases.DefaultGameInfo, [winners, losers], 1, 2).First(r => r.Key.Id == 0).Value;
            losses++;
        }
        Console.WriteLine("INFO a reset Gold player is demoted after " + losses + " straight even losses");
        Check(losses >= 2, "one loss right after the reset cannot demote a player");
    }

    static Dictionary<uint, Rating> Hidden(params (uint Id, double Mean)[] players) =>
        players.ToDictionary(p => p.Id, p => new Rating(p.Mean, 5));

    static void Lifecycle(string path)
    {
        using var db = new SQLiteConnection(path);
        LeagueSeasons.CreateTables(db); Trophies.CreateTables(db); db.CreateTable<PlayerRecord>();
        for (int i = 1; i <= 300; i++) db.Insert(new PlayerRecord { Username = "P" + i });
        var casual = db.Find<PlayerRecord>(1u);
        casual.RatingMean = 41.23; db.Update(casual);
        Check(LeagueSeasons.PlayerView(db, 1).CasualMmr == 4123, "casual MMR is available before ranked placements or a season");
        Check(LeagueSeasons.PlayerView(db, 9999).CasualMmr == null, "unknown player has no casual MMR");
        casual.RatingMean = 0; db.Update(casual);
        Check(LeagueSeasons.PlayerView(db, 1).CasualMmr == 0, "zero casual MMR stays zero");
        casual.RatingMean = 41.23; db.Update(casual);

        // Season one seeds everyone with five rated matches at the reset start of their tier; newcomers need placements.
        var hidden = Hidden((1, 40), (2, 31), (3, 26), (4, 20), (5, 12), (6, 30), (7, 45));
        var matches = new Dictionary<uint, int> { [1] = 9, [2] = 5, [3] = 30, [4] = 7, [5] = 5, [6] = 4, [7] = 12 };
        var closed = LeagueSeasons.Advance(db, Oct13, id => matches.GetValueOrDefault(id), hidden);
        var season = LeagueSeasons.Current(db)!;
        LeagueSeasons.Refresh(db);
        var profileRatings = LeagueSeasons.PlayerView(db, 1);
        Check(profileRatings.CasualMmr == 4123 && profileRatings.Points == 3500, "casual MMR remains separate from reset ranked points");
        var ratingsJson = JsonSerializer.Serialize(profileRatings, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Check(JsonDocument.Parse(ratingsJson).RootElement.GetProperty("casual_mmr").GetInt32() == 4123, "public player response carries casual_mmr in display points");
        Check(closed.Count == 0 && season.Id == "2026-10" && season.StartsAt == Oct13.ToUnixTimeMilliseconds() &&
              season.EndsAt == new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), "the first season opens now and ends with the month");
        var seeded = db.Table<LeagueStandingRecord>().ToList().ToDictionary(r => (uint)r.PlayerId, r => LeagueRules.Points(r.Mean));
        Check(seeded.Count == 6 && seeded[7] == 3500 && seeded[1] == 3500 && seeded[2] == 3000 && seeded[3] == 2500 && seeded[4] == 1900 && seeded[5] == 1500,
            "established players start season one at their tier's reset value");
        Check(!seeded.ContainsKey(6) && db.Find<LeaguePlayerRecord>(6L) == null, "a player with four rated matches still plays placements");
        LeagueSeasons.Refresh(db);
        Check(LeagueSeasons.Snapshot[1].Tier == LeagueTier.Champions && LeagueSeasons.Snapshot[1].Provisional && LeagueSeasons.Snapshot[1].Rank == null,
            "seeded players show their tier before playing, unranked until their first match");
        Check(LeagueRanker.Derive(1) is { Tier: 4, Division: 0, Points: 3500 } && LeagueRanker.Derive(6) == null && LeagueRanker.SeasonLabel(LeagueRanker.Derive(2)) == "MASTERS",
            "the profile badge carries tier 0-4 without divisions; players in placements have none");

        // Placements: a newcomer who wins five times in a row is capped at Masters.
        // Each placement game is against a fresh, even lobby (as matchmaking would pair them): 10-14 always win, 20-24 always lose.
        var team = Enumerable.Range(10, 5).Select(i => (uint)i).ToList();
        var opponents = Enumerable.Range(20, 5).Select(i => (uint)i).ToList();
        var all = Hidden(Enumerable.Range(10, 100).Select(i => ((uint)i, 25.0)).ToArray());
        List<uint> Fresh(int game, int side) => Enumerable.Range(40 + game * 10 + side * 5, 5).Select(i => (uint)i).ToList();
        for (int game = 0; game < 5; game++)
        {
            var now = Oct13.AddHours(game + 1);
            var excluded = game == 4 ? new HashSet<uint> { 14 } : new HashSet<uint>();
            if (game == 4) Check(db.Find<LeaguePlayerRecord>(10L) is { PlacementGames: 4, PlacedAt: null }, "four placement games do not place a player");
            LeagueSeasons.RecordMatch(db, season, team, Fresh(game, 0), excluded, all, now);
            LeagueSeasons.RecordMatch(db, season, Fresh(game, 1), opponents, new HashSet<uint>(), all, now);
        }
        Check(db.Find<LeaguePlayerRecord>(10L)?.PlacedAt != null && db.Find<LeaguePlayerRecord>(14L)!.PlacementGames == 4,
            "the fifth game places a player; an excluded player's placement does not advance");
        var placedRow = db.Table<LeagueStandingRecord>().First(r => r.PlayerId == 10);
        Check(placedRow.Mean <= LeagueRules.PlacementCap && LeagueRules.TierOf(placedRow.Mean) <= LeagueTier.Masters && placedRow.Games == 5 && placedRow.Wins == 5,
            "placements top out at Masters (" + LeagueRules.Points(placedRow.Mean) + ")");
        var loser = db.Table<LeagueStandingRecord>().First(r => r.PlayerId == 20);
        Check(db.Find<LeaguePlayerRecord>(20L)?.PlacedAt != null && LeagueRules.TierOf(loser.Mean) == LeagueTier.Bronze,
            "five straight losses place a player in Bronze (" + LeagueRules.Points(loser.Mean) + ")");
        // A newcomer whose hidden rating is already Champions-level still places no higher than Masters.
        var strong = Enumerable.Range(200, 5).Select(i => (uint)i).ToList();
        var strongRatings = Hidden(Enumerable.Range(200, 60).Select(i => ((uint)i, i < 205 ? 36.0 : 36.0)).ToArray());
        for (int game = 0; game < 5; game++)
            LeagueSeasons.RecordMatch(db, season, strong, Enumerable.Range(205 + game * 5, 5).Select(i => (uint)i).ToList(), new HashSet<uint>(), strongRatings, Oct13.AddHours(game + 1));
        var strongRow = db.Table<LeagueStandingRecord>().First(r => r.PlayerId == 200);
        Check(db.Find<LeaguePlayerRecord>(200L)?.PlacedAt != null && strongRow.Mean == LeagueRules.PlacementCap,
            "a Champions-level newcomer is capped at the top of Masters (" + LeagueRules.Points(strongRow.Mean) + ")");
        Check(LeagueSeasons.RecordMatch(db, season, [1, 2], [3, 4], new HashSet<uint>(), hidden, Oct13.AddHours(6)).Count == 0,
            "2v2 results are ignored, as for the hidden rating");

        // Season play for the seeded players (ranked needs five games to count for rewards).
        var seededTeam = new List<uint> { 1, 2, 3, 10, 11 }; var other = new List<uint> { 4, 5, 20, 21, 22 };
        var ratings = Hidden(seededTeam.Concat(other).Select(i => (i, 25.0)).ToArray());
        for (int game = 0; game < 5; game++) LeagueSeasons.RecordMatch(db, season, seededTeam, other, new HashSet<uint>(), ratings, Oct13.AddDays(1).AddHours(game));
        // Player 7 (highest rating) plays only two games this season.
        for (int game = 0; game < 2; game++)
            LeagueSeasons.RecordMatch(db, season, [7, 30, 29, 28, 27], [26, 25, 24, 23, 19], new HashSet<uint>(), Hidden(Enumerable.Range(7, 25).Select(i => ((uint)i, 25.0)).ToArray()), Oct13.AddDays(2).AddHours(game));
        LeagueSeasons.Refresh(db);
        var standings = LeagueSeasons.Snapshot.Values.Where(s => s.Rank != null).OrderBy(s => s.Rank).ToList();
        Check(standings.Select(s => s.Rank).SequenceEqual(Enumerable.Range(1, standings.Count).Select(i => (int?)i)) &&
              standings.Zip(standings.Skip(1)).All(p => p.First.Mean >= p.Second.Mean),
            "the season leaderboard ranks everyone who has played this season");
        Check(standings.All(s => LeagueSeasons.Snapshot[s.PlayerId].Games > 0) && !standings.Any(s => s.PlayerId == 14),
            "players still in placements are not on the leaderboard");

        // Month end: rewards, results and a soft reset for next season.
        var nov2 = new DateTimeOffset(2026, 11, 2, 0, 1, 0, TimeSpan.Zero);
        closed = LeagueSeasons.Advance(db, nov2, _ => 0, hidden);
        Check(closed.Count == 1 && closed[0].Season == "2026-10" && LeagueSeasons.Current(db)!.Id == "2026-11", "the month end closes the season and opens the next");
        var results = db.Table<LeagueResultRecord>().ToList();
        var rankedResults = results.Where(r => r.Rank != null).OrderBy(r => r.Rank).ToList();
        Check(results.Single(r => r.PlayerId == 7) is { Rank: null, Games: 2 } && grants0(db).All(g => g.PlayerId != 7),
            "the season's highest rating with only two games is not ranked or rewarded");
        Check(rankedResults.All(r => r.Games >= LeagueRules.MinSeasonGamesForRewards) && results.Any(r => r.Rank == null),
            "only players with five ranked games this season are ranked at the end");
        var grants = db.Table<PlayerTrophyRecord>().ToList();
        Check(grants.Count(g => g.TrophyId == "league-1st") == 1 && grants.Count(g => g.TrophyId == "league-2nd") == 1 && grants.Count(g => g.TrophyId == "league-3rd") == 1 &&
              grants.Single(g => g.TrophyId == "league-1st").PlayerId == rankedResults[0].PlayerId, "the top three receive podium trophies");
        Check(rankedResults.All(r => grants.Any(g => g.PlayerId == r.PlayerId && g.TrophyId == LeagueRules.FinishTrophy((LeagueTier)r.Tier))) &&
              grants.Count(g => g.TrophyId.StartsWith("league-") && !g.TrophyId.EndsWith("st") && !g.TrophyId.EndsWith("nd") && !g.TrophyId.EndsWith("rd")) == rankedResults.Count,
            "every ranked finisher receives their tier trophy, once");
        Check(grants.All(g => g.GrantedBy == "league" && g.Note.StartsWith("October 2026 season · #")), "awards record the season, place and MMR");
        Check(LeagueRules.RewardTrophies.All(t => db.Find<TrophyRecord>(t.Id) is { ImageVersion: 0 }), "reward trophies are created without art for admins to add");
        Check(LeagueSeasons.Advance(db, nov2.AddMinutes(1), _ => 0, hidden).Count == 0 && db.Table<PlayerTrophyRecord>().Count() == grants.Count,
            "a season closes once; later checks award nothing more");

        LeagueSeasons.Refresh(db);
        var champion = LeagueSeasons.Snapshot[(uint)rankedResults[0].PlayerId];
        Check(champion.Provisional && champion.Rank == null && LeagueRules.Points(champion.Mean) == LeagueRules.Points(LeagueRules.SoftReset(rankedResults[0].Mean, 1).Mean),
            "next season shows each player's soft-reset tier until they play");
        var nextSeason = LeagueSeasons.Current(db)!;
        var start = LeagueSeasons.StartingRating(db, (uint)rankedResults[0].PlayerId, new Rating(25, 8));
        Check(LeagueRules.Points(start.Mean) == LeagueRules.Points(champion.Mean), "the first game of the season starts from that soft reset");
        Check(LeagueSeasons.StartingRating(db, 14, new Rating(25, 8)).Mean == db.Table<LeagueStandingRecord>().First(r => r.PlayerId == 14).Mean,
            "unfinished placements carry over instead of resetting");
        Check(LeagueSeasons.StartingRating(db, 299, new Rating(27, 2)) is { Mean: 27 } r && r.StandardDeviation == Databases.DefaultSd,
            "a new player's placements start from their hidden rating, fully uncertain");

        // Skipping a whole season still closes each month in turn.
        closed = LeagueSeasons.Advance(db, new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero), _ => 0, hidden);
        Check(closed.Select(c => c.Season).SequenceEqual(["2026-11", "2026-12"]) && LeagueSeasons.Current(db)!.Id == "2027-01", "missed month ends are closed in order");

        LeagueSeasons.Refresh(db);
        var view = LeagueSeasons.PlayerView(db, (uint)rankedResults[0].PlayerId);
        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Check(view.Placed && view.Provisional && view.History.Count == 1 && view.History[0].Rank == 1 && json.Contains("\"placement_games\":") && json.Contains("\"history\":[{\"season\":\"2026-10\""),
            "a player's league view carries placement, standing and the seasons they played");
        var season1 = JsonSerializer.Serialize(LeagueSeasons.SeasonView(db), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Check(season1.Contains("\"season\":\"2027-01\"") && season1.Contains("{\"name\":\"CHAMPIONS\",\"min_points\":3400}") && season1.Contains("\"placement_games\":5"),
            "the season view lists tiers with their MMR floors");
        Check(LeagueSeasons.PlayerView(db, 14) is { Placed: false, PlacementGames: 4, Tier: null }, "a player in placements reports their progress");
    }

    static List<PlayerTrophyRecord> grants0(SQLiteConnection db) => db.Table<PlayerTrophyRecord>().ToList();

    static void Gate()
    {
        var gate = typeof(BNLReloadedServer.ControlPanel.ControlPanelServer).GetMethod("IsPublicReadRequest", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)gate.Invoke(null, ["GET", "/api/league"])! && (bool)gate.Invoke(null, ["GET", "/api/league/players/3"])! && !(bool)gate.Invoke(null, ["POST", "/api/league"])!,
            "the season and player league views are public reads");
    }
}
