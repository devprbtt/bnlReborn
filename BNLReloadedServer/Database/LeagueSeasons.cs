using BNLReloadedServer.BaseTypes;
using Moserware.Skills;
using SQLite;

namespace BNLReloadedServer.Database;

[Table("LeagueSeasons")]
public sealed class LeagueSeasonRecord
{
    [PrimaryKey, Column("id")] public string Id { get; set; } = string.Empty; // "2026-10"
    [Column("starts_at_ms")] public long StartsAt { get; set; }
    [Column("ends_at_ms")] public long EndsAt { get; set; }
    [Column("closed_at_ms")] public long? ClosedAt { get; set; }
}

/// <summary>A player's league rating in one season. Only ranked matches move it.</summary>
[Table("LeagueStandings")]
public sealed class LeagueStandingRecord
{
    [PrimaryKey, AutoIncrement, Column("row_id")] public long RowId { get; set; }
    [Indexed(Name = "IX_LeagueStandings_Player", Order = 1, Unique = true), Column("season_id")] public string SeasonId { get; set; } = string.Empty;
    [Indexed(Name = "IX_LeagueStandings_Player", Order = 2, Unique = true), Column("player_id")] public long PlayerId { get; set; }
    [Column("rating_mean")] public double Mean { get; set; }
    [Column("rating_sd")] public double Deviation { get; set; }
    [Column("games")] public int Games { get; set; }
    [Column("wins")] public int Wins { get; set; }
    [Column("updated_at_ms")] public long UpdatedAt { get; set; }
}

/// <summary>Placement progress, which carries across seasons until a player is placed once.</summary>
[Table("LeaguePlayers")]
public sealed class LeaguePlayerRecord
{
    [PrimaryKey, Column("player_id")] public long PlayerId { get; set; }
    [Column("placement_games")] public int PlacementGames { get; set; }
    [Column("placed_at_ms")] public long? PlacedAt { get; set; }
}

/// <summary>Final standings, written once when a season closes.</summary>
[Table("LeagueResults")]
public sealed class LeagueResultRecord
{
    [PrimaryKey, AutoIncrement, Column("row_id")] public long RowId { get; set; }
    [Indexed(Name = "IX_LeagueResults_Player", Order = 1, Unique = true), Column("season_id")] public string SeasonId { get; set; } = string.Empty;
    [Indexed(Name = "IX_LeagueResults_Player", Order = 2, Unique = true), Column("player_id")] public long PlayerId { get; set; }
    [Column("rank")] public int? Rank { get; set; } // null: not enough games to be ranked
    [Column("tier")] public int Tier { get; set; }
    [Column("rating_mean")] public double Mean { get; set; }
    [Column("games")] public int Games { get; set; }
    [Column("wins")] public int Wins { get; set; }
}

public enum LeagueTier { Bronze = 0, Silver = 1, Gold = 2, Masters = 3, Champions = 4 }

/// <summary>League tiers, the monthly season clock, soft reset and placement rules. Ratings are TrueSkill means; the game shows them x100.</summary>
public static class LeagueRules
{
    public const int PlacementGames = 5;
    public const int MinSeasonGamesForRewards = 5;
    // Tier floors from the October 2026 rating distribution (displayed MMR 1800 / 2400 / 2900 / 3400). Champions has no cap.
    public static readonly double[] TierFloors = [double.NegativeInfinity, 18.0, 24.0, 29.0, 34.0];
    public static readonly string[] TierNames = ["BRONZE", "SILVER", "GOLD", "MASTERS", "CHAMPIONS"];
    // A new season starts each player a little above the floor of their tier, so one loss does not demote them.
    public const double ResetBuffer = 1.0;
    // Bronze has no floor; its players restart here.
    public const double BronzeReset = 15.0;
    // Reset uncertainty: enough for a fresh season to move, not so much that one game swings a tier.
    public const double ResetDeviation = 3.0;
    // Placement games move the rating twice as far, so five games can span Bronze to Masters
    // (plain TrueSkill from an even start covers only about 2070-2930 MMR in five games).
    public const double PlacementBoost = 2.0;
    // Placements may reach the top of Masters, never Champions.
    public const double PlacementCap = 34.0 - 0.01;
    // A season standing for players who joined before the league: five rated matches (any mode) counts as placed.
    public const int SeedMatches = 5;

    public static LeagueTier TierOf(double mean)
    {
        for (int t = TierFloors.Length - 1; t > 0; t--) if (mean >= TierFloors[t]) return (LeagueTier)t;
        return LeagueTier.Bronze;
    }

    public static int Points(double mean) => (int)Math.Round(mean * 100);

    public static Rating SoftReset(double mean, double deviation)
    {
        var tier = TierOf(mean);
        var start = tier == LeagueTier.Bronze ? BronzeReset : TierFloors[(int)tier] + ResetBuffer;
        return new Rating(start, Math.Max(deviation, ResetDeviation));
    }

    /// <summary>The calendar month (UTC) containing <paramref name="at"/>.</summary>
    public static (string Id, long Start, long End) SeasonAt(DateTimeOffset at)
    {
        var start = new DateTimeOffset(at.UtcDateTime.Year, at.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (start.ToString("yyyy-MM"), start.ToUnixTimeMilliseconds(), start.AddMonths(1).ToUnixTimeMilliseconds());
    }

    public static readonly (string Id, string Name, string Description)[] RewardTrophies =
    [
        ("league-1st", "Season Winner", "Finished a monthly league season first overall."),
        ("league-2nd", "Season Runner-up", "Finished a monthly league season second overall."),
        ("league-3rd", "Season Third Place", "Finished a monthly league season third overall."),
        ("league-bronze", "Bronze League", "Finished a monthly league season in Bronze."),
        ("league-silver", "Silver League", "Finished a monthly league season in Silver."),
        ("league-gold", "Gold League", "Finished a monthly league season in Gold."),
        ("league-masters", "Masters League", "Finished a monthly league season in Masters."),
        ("league-champions", "Champions League", "Finished a monthly league season in Champions.")
    ];

    public static string FinishTrophy(LeagueTier tier) => "league-" + tier.ToString().ToLowerInvariant();
}

public sealed record LeagueStanding(uint PlayerId, LeagueTier Tier, double Mean, int? Rank, int Games, int Wins, bool Provisional);
public sealed record LeagueTierView(string Name, int MinPoints);
public sealed record LeagueSeasonView(int Version, string Season, long StartsAt, long EndsAt, int PlacementGames, int MinSeasonGames, List<LeagueTierView> Tiers);
public sealed record LeagueHistoryView(string Season, int? Rank, string Tier, int Points, int Games, int Wins);
public sealed record LeaguePlayerView(int Version, uint PlayerId, string Season, bool Placed, int PlacementGames, string? Tier, int? Points, int? Rank,
    int Games, int Wins, bool Provisional, List<LeagueHistoryView> History, int? CasualMmr = null);
public sealed record LeagueClose(string Season, List<LeagueResultRecord> Results, List<(uint PlayerId, string TrophyId)> Rewards);

/// <summary>
/// Monthly ranked seasons over the player database. Pure functions over a SQLite connection; the master holds its lock
/// around them. <see cref="Snapshot"/> is the in-memory view the profile badge and leaderboard read synchronously.
/// </summary>
public static class LeagueSeasons
{
    private static volatile Dictionary<uint, LeagueStanding> _snapshot = [];

    public static IReadOnlyDictionary<uint, LeagueStanding> Snapshot => _snapshot;

    public static void CreateTables(SQLiteConnection db)
    {
        db.CreateTable<LeagueSeasonRecord>(); db.CreateTable<LeagueStandingRecord>();
        db.CreateTable<LeaguePlayerRecord>(); db.CreateTable<LeagueResultRecord>();
    }

    public static LeagueSeasonRecord? Current(SQLiteConnection db) =>
        db.Table<LeagueSeasonRecord>().Where(s => s.ClosedAt == null).OrderByDescending(s => s.StartsAt).FirstOrDefault();

    /// <summary>
    /// Opens the first season (seeding everyone already established) or closes overdue seasons and opens the next.
    /// Returns any seasons closed, so the caller can announce rewards and refresh badges.
    /// </summary>
    public static List<LeagueClose> Advance(SQLiteConnection db, DateTimeOffset now, Func<uint, int> ratedMatches,
        IReadOnlyDictionary<uint, Rating> hiddenRatings)
    {
        var closed = new List<LeagueClose>();
        var current = Current(db);
        if (current == null)
        {
            var (id, start, end) = LeagueRules.SeasonAt(now);
            if (db.Find<LeagueSeasonRecord>(id) != null) id += "b"; // never reuse a closed season's id
            // The first season begins when the league is switched on and ends with that calendar month.
            current = new LeagueSeasonRecord { Id = id, StartsAt = now.ToUnixTimeMilliseconds(), EndsAt = end };
            db.Insert(current);
            if (db.Table<LeaguePlayerRecord>().Count() == 0) Seed(db, current, now, ratedMatches, hiddenRatings);
        }
        while (now.ToUnixTimeMilliseconds() >= current.EndsAt)
        {
            closed.Add(Close(db, current, now));
            var (id, start, end) = LeagueRules.SeasonAt(DateTimeOffset.FromUnixTimeMilliseconds(current.EndsAt));
            current = new LeagueSeasonRecord { Id = id, StartsAt = start, EndsAt = end };
            db.Insert(current);
        }
        return closed;
    }

    /// <summary>Season one: everyone with enough rated history is placed at the soft-reset start of their current tier.</summary>
    private static void Seed(SQLiteConnection db, LeagueSeasonRecord season, DateTimeOffset now, Func<uint, int> ratedMatches,
        IReadOnlyDictionary<uint, Rating> hiddenRatings)
    {
        foreach (var (playerId, rating) in hiddenRatings)
        {
            if (ratedMatches(playerId) < LeagueRules.SeedMatches) continue;
            var start = LeagueRules.SoftReset(rating.Mean, rating.StandardDeviation);
            db.Insert(new LeaguePlayerRecord { PlayerId = playerId, PlacementGames = LeagueRules.PlacementGames, PlacedAt = now.ToUnixTimeMilliseconds() });
            db.Insert(new LeagueStandingRecord { SeasonId = season.Id, PlayerId = playerId, Mean = start.Mean, Deviation = start.StandardDeviation, UpdatedAt = now.ToUnixTimeMilliseconds() });
        }
    }

    /// <summary>Where a player starts this season: continuing placements, a soft reset of their last season, or a fresh placement.</summary>
    public static Rating StartingRating(SQLiteConnection db, uint playerId, Rating hidden)
    {
        long id = playerId;
        var last = db.Table<LeagueStandingRecord>().Where(s => s.PlayerId == id).OrderByDescending(s => s.UpdatedAt).FirstOrDefault();
        var placed = db.Find<LeaguePlayerRecord>(id)?.PlacedAt != null;
        if (last == null) return new Rating(hidden.Mean, Databases.DefaultSd); // placements start from the hidden rating, fully uncertain
        if (!placed) return new Rating(last.Mean, last.Deviation); // placement progress continues
        return LeagueRules.SoftReset(last.Mean, last.Deviation);
    }

    /// <summary>
    /// Applies one ranked result. Excluded players (late joiners, the rest of the table when someone quits) are not changed.
    /// Returns the players whose standing changed.
    /// </summary>
    public static List<uint> RecordMatch(SQLiteConnection db, LeagueSeasonRecord season, IReadOnlyList<uint> winners, IReadOnlyList<uint> losers,
        IReadOnlySet<uint> excluded, IReadOnlyDictionary<uint, Rating> hiddenRatings, DateTimeOffset now)
    {
        if (winners.Count <= 2 && losers.Count <= 2) return []; // same rule as the hidden rating: too few players to read anything
        var rows = new Dictionary<uint, LeagueStandingRecord>();
        foreach (var playerId in winners.Concat(losers).Distinct())
        {
            long id = playerId; string seasonId = season.Id;
            var row = db.Table<LeagueStandingRecord>().Where(s => s.SeasonId == seasonId && s.PlayerId == id).FirstOrDefault();
            if (row == null)
            {
                var start = StartingRating(db, playerId, hiddenRatings.GetValueOrDefault(playerId) ?? new Rating(Databases.DefaultMean, Databases.DefaultSd));
                row = new LeagueStandingRecord { SeasonId = season.Id, PlayerId = playerId, Mean = start.Mean, Deviation = start.StandardDeviation, UpdatedAt = now.ToUnixTimeMilliseconds() };
            }
            rows[playerId] = row;
        }
        var winnerTeam = winners.Distinct().ToDictionary(p => new Player<uint>(p), p => new Rating(rows[p].Mean, rows[p].Deviation));
        var loserTeam = losers.Distinct().ToDictionary(p => new Player<uint>(p), p => new Rating(rows[p].Mean, rows[p].Deviation));
        var updated = TrueSkillCalculator.CalculateNewRatings(Databases.DefaultGameInfo, [winnerTeam, loserTeam], 1, 2);
        var changed = new List<uint>();
        foreach (var (player, rating) in updated)
        {
            var playerId = player.Id;
            if (excluded.Contains(playerId)) continue;
            var row = rows[playerId];
            var league = db.Find<LeaguePlayerRecord>((long)playerId) ?? new LeaguePlayerRecord { PlayerId = playerId };
            var boost = league.PlacedAt == null ? LeagueRules.PlacementBoost : 1.0;
            row.Mean += (rating.Mean - row.Mean) * boost; row.Deviation = rating.StandardDeviation;
            row.Games++; if (winners.Contains(playerId)) row.Wins++;
            row.UpdatedAt = now.ToUnixTimeMilliseconds();
            if (league.PlacedAt == null)
            {
                league.PlacementGames++;
                if (league.PlacementGames >= LeagueRules.PlacementGames)
                {
                    league.PlacedAt = now.ToUnixTimeMilliseconds();
                    row.Mean = Math.Min(row.Mean, LeagueRules.PlacementCap);
                }
                db.InsertOrReplace(league);
            }
            if (row.RowId == 0) db.Insert(row); else db.Update(row);
            changed.Add(playerId);
        }
        return changed;
    }

    // Same policy as casual, but on independent seasonal ratings; placement semantics remain intact.
    public static void RecordPersonalMatch(SQLiteConnection db, LeagueSeasonRecord season, CompletedMatchRecord match,
        IReadOnlyDictionary<uint, Rating> hidden, IReadOnlyList<MmrReference> references)
    {
        var rows = new Dictionary<uint, LeagueStandingRecord>();
        foreach(var p in match.Players)
        {
            long id=p.PlayerId; string seasonId=season.Id;
            var row=db.Table<LeagueStandingRecord>().Where(s=>s.PlayerId==id&&s.SeasonId==seasonId).FirstOrDefault();
            if(row is null)
            {
                var start=StartingRating(db,p.PlayerId,hidden[p.PlayerId]);
                row=new LeagueStandingRecord {SeasonId=seasonId,PlayerId=id,Mean=start.Mean,Deviation=start.StandardDeviation};
            }
            rows[p.PlayerId]=row;
        }
        var awards=PersonalMmr.Calculate(match,rows.ToDictionary(p=>p.Key,p=>new Rating(p.Value.Mean,p.Value.Deviation)),references);
        foreach(var a in awards.Where(a=>a.Reason is "outcome" or "abandonment"))
        {
            var row=rows[a.PlayerId];
            var league=db.Find<LeaguePlayerRecord>((long)a.PlayerId)??new LeaguePlayerRecord {PlayerId=a.PlayerId};
            var boost=league.PlacedAt is null?LeagueRules.PlacementBoost:1;
            row.Mean+=a.Delta*boost; row.Deviation=a.Deviation; row.Games++;
            if(match.Winner==PersonalMmr.Team(match.Players.Single(p=>p.PlayerId==a.PlayerId)))row.Wins++;
            row.UpdatedAt=(long)match.EndedAt;
            if(league.PlacedAt is null)
            {
                league.PlacementGames++;
                if(league.PlacementGames>=LeagueRules.PlacementGames)
                {league.PlacedAt=(long)match.EndedAt;row.Mean=Math.Min(row.Mean,LeagueRules.PlacementCap);}
                db.InsertOrReplace(league);
            }
            if(row.RowId==0)db.Insert(row);else db.Update(row);
        }
    }

    /// <summary>Final standings and rewards for a season: podium trophies for the top three, a tier trophy for every ranked finisher.</summary>
    public static LeagueClose Close(SQLiteConnection db, LeagueSeasonRecord season, DateTimeOffset now)
    {
        string seasonId = season.Id;
        var placed = db.Table<LeaguePlayerRecord>().Where(p => p.PlacedAt != null).ToList().Select(p => p.PlayerId).ToHashSet();
        var rows = db.Table<LeagueStandingRecord>().Where(s => s.SeasonId == seasonId).ToList();
        var ranked = rows.Where(r => placed.Contains(r.PlayerId) && r.Games >= LeagueRules.MinSeasonGamesForRewards)
            .OrderByDescending(r => r.Mean).ThenByDescending(r => r.Games).ThenBy(r => r.PlayerId).ToList();
        var results = new List<LeagueResultRecord>();
        var rewards = new List<(uint, string)>();
        foreach (var row in rows)
        {
            var position = ranked.IndexOf(row);
            var result = new LeagueResultRecord
            {
                SeasonId = season.Id, PlayerId = row.PlayerId, Rank = position >= 0 ? position + 1 : null,
                Tier = (int)LeagueRules.TierOf(row.Mean), Mean = row.Mean, Games = row.Games, Wins = row.Wins
            };
            db.Insert(result); results.Add(result);
        }
        EnsureRewardTrophies(db, now);
        var label = SeasonLabel(season.Id);
        foreach (var result in results.Where(r => r.Rank != null).OrderBy(r => r.Rank))
        {
            var playerId = (uint)result.PlayerId;
            var note = label + " · #" + result.Rank + " · " + LeagueRules.Points(result.Mean) + " MMR";
            if (result.Rank <= 3)
            {
                var podium = LeagueRules.RewardTrophies[result.Rank.Value - 1].Id;
                if (Trophies.Grant(db, playerId, podium, "league", note, now.ToUnixTimeMilliseconds()).Result == TrophyResult.Ok) rewards.Add((playerId, podium));
            }
            var finish = LeagueRules.FinishTrophy((LeagueTier)result.Tier);
            if (Trophies.Grant(db, playerId, finish, "league", note, now.ToUnixTimeMilliseconds()).Result == TrophyResult.Ok) rewards.Add((playerId, finish));
        }
        season.ClosedAt = now.ToUnixTimeMilliseconds();
        db.Update(season);
        return new LeagueClose(season.Id, results, rewards);
    }

    /// <summary>League reward trophies exist from the first season; admins add their art later in the control panel.</summary>
    public static void EnsureRewardTrophies(SQLiteConnection db, DateTimeOffset now)
    {
        foreach (var (id, name, description) in LeagueRules.RewardTrophies)
            if (db.Find<TrophyRecord>(id) == null)
                db.Insert(new TrophyRecord { Id = id, Name = name, Description = description, ImageVersion = 0, CreatedAt = now.ToUnixTimeMilliseconds() });
    }

    public static string SeasonLabel(string seasonId) =>
        DateTime.TryParseExact(seasonId.TrimEnd('b'), "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month)
            ? month.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + " season"
            : seasonId + " season";

    /// <summary>
    /// Rebuilds the in-memory standings: this season's rows, and a provisional (soft-reset) standing for placed players
    /// who have not played yet, so their badge carries into the new season.
    /// </summary>
    public static void Refresh(SQLiteConnection db)
    {
        var season = Current(db);
        if (season == null) { _snapshot = []; return; }
        string seasonId = season.Id;
        var placed = db.Table<LeaguePlayerRecord>().Where(p => p.PlacedAt != null).ToList().Select(p => (uint)p.PlayerId).ToHashSet();
        var current = db.Table<LeagueStandingRecord>().Where(s => s.SeasonId == seasonId).ToList()
            .Where(r => placed.Contains((uint)r.PlayerId)).ToDictionary(r => (uint)r.PlayerId);
        var order = current.Values.Where(r => r.Games > 0).OrderByDescending(r => r.Mean).ThenByDescending(r => r.Games).ThenBy(r => r.PlayerId).ToList();
        var snapshot = new Dictionary<uint, LeagueStanding>();
        for (int i = 0; i < order.Count; i++)
        {
            var r = order[i];
            snapshot[(uint)r.PlayerId] = new LeagueStanding((uint)r.PlayerId, LeagueRules.TierOf(r.Mean), r.Mean, i + 1, r.Games, r.Wins, false);
        }
        var latest = db.Table<LeagueStandingRecord>().ToList().GroupBy(r => (uint)r.PlayerId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.UpdatedAt).First());
        foreach (var playerId in placed)
        {
            if (snapshot.ContainsKey(playerId)) continue;
            if (current.TryGetValue(playerId, out var unplayed))
                snapshot[playerId] = new LeagueStanding(playerId, LeagueRules.TierOf(unplayed.Mean), unplayed.Mean, null, 0, 0, true);
            else if (latest.TryGetValue(playerId, out var last))
            {
                var reset = LeagueRules.SoftReset(last.Mean, last.Deviation);
                snapshot[playerId] = new LeagueStanding(playerId, LeagueRules.TierOf(reset.Mean), reset.Mean, null, 0, 0, true);
            }
        }
        _snapshot = snapshot;
    }

    public static LeagueSeasonView SeasonView(SQLiteConnection db)
    {
        var season = Current(db);
        return new LeagueSeasonView(1, season?.Id ?? "", season?.StartsAt ?? 0, season?.EndsAt ?? 0, LeagueRules.PlacementGames, LeagueRules.MinSeasonGamesForRewards,
            LeagueRules.TierNames.Select((name, i) => new LeagueTierView(name, i == 0 ? 0 : LeagueRules.Points(LeagueRules.TierFloors[i]))).ToList());
    }

    public static LeaguePlayerView PlayerView(SQLiteConnection db, uint playerId)
    {
        long id = playerId;
        var season = Current(db);
        var league = db.Find<LeaguePlayerRecord>(id);
        var player = db.Find<PlayerRecord>(playerId);
        var placed = league?.PlacedAt != null;
        Snapshot.TryGetValue(playerId, out var standing);
        var history = db.Table<LeagueResultRecord>().Where(r => r.PlayerId == id).ToList().OrderByDescending(r => r.SeasonId)
            .Select(r => new LeagueHistoryView(r.SeasonId, r.Rank, LeagueRules.TierNames[r.Tier], LeagueRules.Points(r.Mean), r.Games, r.Wins)).ToList();
        return new LeaguePlayerView(1, playerId, season?.Id ?? "", placed, league?.PlacementGames ?? 0,
            placed && standing != null ? LeagueRules.TierNames[(int)standing.Tier] : null,
            placed && standing != null ? LeagueRules.Points(standing.Mean) : null,
            standing?.Rank, standing?.Games ?? 0, standing?.Wins ?? 0, standing?.Provisional ?? false, history,
            player != null ? LeagueRules.Points(player.RatingMean) : null);
    }
}
