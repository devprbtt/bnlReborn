using SQLite;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

/// <summary>Stat rankings beside the league (MMR) leaderboard. Names are the public API's stat parameter.</summary>
public enum PlayerLeaderboardStat
{
    Kills,
    Headshots,
    AverageDps,
    Assists,
    ObjectiveDamage,
    Hours,
    Matches,
    WinRate,
    Kdr,
    FirstKills,
    DoubleKills,
    MultiKills,
    UltraKills,
    FantasticKills,
    UnbelievableKills,
    BestStreak,
    CasualMmr,
    ScorePerMinute,
    PlayerLevel
}

public sealed record PlayerLeaderboardRow(int Rank, uint PlayerId, string Nickname, double Value, long Matches, string? SteamId = null, double? RatingDeviation = null);

public sealed record PlayerLeaderboard(int Version, string Stat, string Period, long UpdatedAt, int MinMatches,
    long Players, List<PlayerLeaderboardRow> Rows, int Offset = 0)
{
    /// <summary>Rows [offset, offset + limit) of a fully ranked board; ranks stay those of the whole board.</summary>
    public PlayerLeaderboard Page(int offset, int limit) =>
        this with { Offset = offset, Rows = Rows.Skip(offset).Take(limit).ToList() };
}

/// <summary>
/// Player stat leaderboards over the match archive. Rounds use the profile eligibility rule
/// (<see cref="LifetimeProfileStats.Eligible"/>), restricted to ranked and casual so private custom games cannot be
/// farmed. Match-stat boards need <see cref="MinMatchesPlayed"/> counted rounds, so a handful of lucky matches cannot top
/// it. Count stats (kills, headshots, assists, objective damage, kill-chain accolades) rank the average per match;
/// average DPS, win rate and KDR are already rates; hours, matches and best streak stay totals or the record.
/// Accolade averages divide by, and require, rounds recorded after accolade tracking began. Hours are the time
/// actually played in counted rounds. Casual MMR ranks the account's casual rating among players with that many
/// counted casual rounds.
/// Score per minute uses archived total score divided by played minutes. Player level reads current account
/// progression, needs no matches and is always all-time.
/// </summary>
public static class PlayerStatLeaderboards
{
    public const int Limit = 100;
    // Largest page a client may ask for; boards themselves rank every eligible player.
    public const int MaxPage = 200;
    public const int MinMatchesPlayed = 20;
    // Kept for callers that name the per-stat thresholds; every board now uses MinMatchesPlayed.
    public const int MinDpsMatches = MinMatchesPlayed;
    public const int MinRateMatches = MinMatchesPlayed;
    public const int RecentDays = 30;

    public static readonly IReadOnlyDictionary<string, PlayerLeaderboardStat> StatNames = new Dictionary<string, PlayerLeaderboardStat>
    {
        ["kills"] = PlayerLeaderboardStat.Kills,
        ["headshots"] = PlayerLeaderboardStat.Headshots,
        ["average_dps"] = PlayerLeaderboardStat.AverageDps,
        ["assists"] = PlayerLeaderboardStat.Assists,
        ["objective_damage"] = PlayerLeaderboardStat.ObjectiveDamage,
        ["hours"] = PlayerLeaderboardStat.Hours,
        ["matches"] = PlayerLeaderboardStat.Matches,
        ["win_rate"] = PlayerLeaderboardStat.WinRate,
        ["kdr"] = PlayerLeaderboardStat.Kdr,
        ["first_kills"] = PlayerLeaderboardStat.FirstKills,
        ["double_kills"] = PlayerLeaderboardStat.DoubleKills,
        ["multi_kills"] = PlayerLeaderboardStat.MultiKills,
        ["ultra_kills"] = PlayerLeaderboardStat.UltraKills,
        ["fantastic_kills"] = PlayerLeaderboardStat.FantasticKills,
        ["unbelievable_kills"] = PlayerLeaderboardStat.UnbelievableKills,
        ["best_streak"] = PlayerLeaderboardStat.BestStreak,
        ["casual_mmr"] = PlayerLeaderboardStat.CasualMmr,
        ["score_per_minute"] = PlayerLeaderboardStat.ScorePerMinute,
        ["player_level"] = PlayerLeaderboardStat.PlayerLevel
    };

    /// <param name="recentOnly">Last <see cref="RecentDays"/> days by match end, otherwise the whole archive.</param>
    /// <param name="modeKeys">Game-mode key hashes that count (ranked and casual).</param>
    /// <param name="casualModeKey">The casual mode's key hash; its rounds qualify a player for the casual MMR board.</param>
    public static PlayerLeaderboard Read(SQLiteConnection db, PlayerLeaderboardStat stat, bool recentOnly, long nowMs,
        IReadOnlyCollection<long> modeKeys, long? casualModeKey = null, int limit = Limit)
    {
        return ReadAll(db, recentOnly && stat != PlayerLeaderboardStat.PlayerLevel, nowMs, modeKeys, onlyStat: stat, casualModeKey: casualModeKey, limit: limit)[stat];
    }

    // Aggregate the archive once for the complete trophy reconciliation. Include every tied leader beyond top 100.
    public static Dictionary<PlayerLeaderboardStat, PlayerLeaderboard> ReadAll(SQLiteConnection db, bool recentOnly,
        long nowMs, IReadOnlyCollection<long> modeKeys, bool includeAllLeaders = false, PlayerLeaderboardStat? onlyStat = null,
        long? casualModeKey = null, int limit = Limit)
    {
        var after = recentOnly ? nowMs - RecentDays * 86_400_000L : long.MinValue;
        var matches = db.Table<ArchivedMatchRecord>().ToList()
            .Where(m => modeKeys.Contains(m.GameModeKey) && m.EndedAt >= after)
            .ToDictionary(m => m.Id);
        var presences = db.Table<ArchivedMatchPresenceRecord>().ToList()
            .Where(p => matches.ContainsKey(p.MatchId))
            .GroupBy(p => (p.MatchId, p.PlayerId)).ToDictionary(g => g.Key, g => g.ToList());
        var totals = new Dictionary<uint, LifetimeCombatTotals>();
        var scores = new Dictionary<uint, (double Score, double Seconds, long Matches)>();
        var casualRounds = new Dictionary<uint, long>();
        var nicknames = new Dictionary<uint, (long At, string Name)>();
        foreach (var player in db.Table<ArchivedMatchPlayerRecord>().ToList())
        {
            if (!matches.TryGetValue(player.MatchId, out var match)) continue;
            var round = LifetimeProfileStats.Eligible(match, presences.GetValueOrDefault((player.MatchId, player.PlayerId)) ?? [], player);
            if (round is null) continue;
            var id = (uint)player.PlayerId;
            if (!totals.TryGetValue(id, out var total)) totals[id] = total = new LifetimeCombatTotals();
            round.AddTo(total);
            if(player.TotalScore >= 0) {
                var score=scores.GetValueOrDefault(id);
                scores[id]=(score.Score+player.TotalScore,score.Seconds+round.Seconds,score.Matches+1);
            }
            if (match.GameModeKey == casualModeKey) casualRounds[id] = casualRounds.GetValueOrDefault(id) + 1;
            if (!nicknames.TryGetValue(id, out var known) || match.EndedAt > known.At) nicknames[id] = (match.EndedAt, player.Nickname);
        }
        // Current account names win over the name used in the last archived round.
        var records = db.Table<PlayerRecord>().ToList();
        var identities = records.ToDictionary(p => p.PlayerId);
        var accounts = records.ToDictionary(p => p.PlayerId, p => p.Username);
        // Casual MMR in display points (the profile's casual_mmr), for accounts on the ladder with enough casual rounds.
        var casualMmr = records
            .Where(p => LeagueRanker.IsOnLadder(p) && casualRounds.GetValueOrDefault(p.PlayerId) >= MinMatchesPlayed)
            .ToDictionary(p => p.PlayerId, p => (Value: (double)LeagueRules.Points(p.RatingMean), Matches: casualRounds[p.PlayerId]));

        var boards = new Dictionary<PlayerLeaderboardStat, PlayerLeaderboard>();
        foreach (var stat in StatNames.Values.Where(s => onlyStat == null || s == onlyStat))
        {
            var values = stat switch {
                PlayerLeaderboardStat.CasualMmr => casualMmr.Select(pair => (Id: pair.Key, Value: (double?)pair.Value.Value, Matches: pair.Value.Matches)),
                PlayerLeaderboardStat.ScorePerMinute => scores.Select(pair => (Id: pair.Key,
                    Value: pair.Value.Matches>=MinMatchesPlayed && pair.Value.Seconds>0 ? (double?)Math.Round(pair.Value.Score*60d/pair.Value.Seconds,2) : null, Matches: pair.Value.Matches)),
                PlayerLeaderboardStat.PlayerLevel => records.Select(p => (Id:p.PlayerId,Value:(double?)AccountLevel(p.Progression),Matches:totals.GetValueOrDefault(p.PlayerId)?.Matches??0)),
                _ => totals.Select(pair => (Id: pair.Key, Value: Value(stat, pair.Value), Matches: pair.Value.Matches))
            };
            var ranked = values
                .Where(r => r.Value is > 0)
                .OrderByDescending(r => r.Value).ThenBy(r => stat==PlayerLeaderboardStat.PlayerLevel?0:r.Matches).ThenBy(r => r.Id)
                .ToList();
            var rows = new List<PlayerLeaderboardRow>();
            for (int i = 0; i < ranked.Count && (rows.Count < limit || (includeAllLeaders && ranked[i].Value == ranked[0].Value)); i++)
            {
                // Equal values share a rank (1, 2, 2, 4).
                int rank = i > 0 && ranked[i].Value == ranked[i - 1].Value ? rows[^1].Rank : i + 1;
                var name = accounts.TryGetValue(ranked[i].Id, out var username) && !string.IsNullOrWhiteSpace(username)
                    ? username
                    : nicknames.GetValueOrDefault(ranked[i].Id).Name ?? string.Empty;
                identities.TryGetValue(ranked[i].Id, out var identity);
                double? deviation = stat==PlayerLeaderboardStat.CasualMmr && identity is not null && double.IsFinite(identity.RatingDeviation) && identity.RatingDeviation>=0
                    ? identity.RatingDeviation : null;
                rows.Add(new PlayerLeaderboardRow(rank, ranked[i].Id, name, ranked[i].Value!.Value, ranked[i].Matches,
                    identity is {SteamId: > 0} ? identity.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, deviation));
            }
            boards[stat] = new PlayerLeaderboard(1, StatNames.First(p => p.Value == stat).Key, recentOnly && stat!=PlayerLeaderboardStat.PlayerLevel ? "30d" : "all", nowMs,
                MinMatches(stat), ranked.Count, rows);
        }
        return boards;
    }

    public static int MinMatches(PlayerLeaderboardStat stat) => stat==PlayerLeaderboardStat.PlayerLevel?0:MinMatchesPlayed;

    private static int? AccountLevel(byte[] progression) {
        if(progression is not {Length:>0})return null;
        try {
            var level=PlayerProgression.ReadByteRecord(progression).PlayerProgress?.Level;
            return level is >0?level:null;
        } catch(Exception e) when(e is IOException or ArgumentException or OverflowException or IndexOutOfRangeException) {return null;}
    }

    /// <summary>True for boards that rank the average per counted match rather than a total, rate or record.</summary>
    public static bool IsPerMatchAverage(PlayerLeaderboardStat stat) => stat is PlayerLeaderboardStat.Kills
        or PlayerLeaderboardStat.Headshots or PlayerLeaderboardStat.Assists or PlayerLeaderboardStat.ObjectiveDamage
        or (>= PlayerLeaderboardStat.FirstKills and <= PlayerLeaderboardStat.UnbelievableKills);

    /// <summary>The ranked value, or null when the player does not qualify for this stat.</summary>
    public static double? Value(PlayerLeaderboardStat stat, LifetimeCombatTotals t)
    {
        if (t.Matches < MinMatchesPlayed) return null;
        static double PerMatch(double total, long matches) => Math.Round(total / matches, 2);
        return stat switch
        {
            PlayerLeaderboardStat.Kills => PerMatch(t.Kills, t.Matches),
            PlayerLeaderboardStat.Headshots => PerMatch(t.HeadshotKills, t.Matches),
            PlayerLeaderboardStat.AverageDps => t.DetailedMatches >= MinMatchesPlayed && t.DetailedSeconds > 0
                ? Math.Round(t.Damage / t.DetailedSeconds, 2) : null,
            PlayerLeaderboardStat.Assists => PerMatch(t.Assists, t.Matches),
            PlayerLeaderboardStat.ObjectiveDamage => PerMatch(t.Objective, t.Matches),
            PlayerLeaderboardStat.Hours => Math.Round(t.Seconds / 3600d, 2),
            PlayerLeaderboardStat.Matches => t.Matches,
            // Percent of counted rounds won.
            PlayerLeaderboardStat.WinRate => Math.Round(100d * t.Wins / t.Matches, 1),
            // Kills per death; a deathless record divides by one.
            PlayerLeaderboardStat.Kdr => Math.Round((double)t.Kills / Math.Max(t.Deaths, 1), 2),
            PlayerLeaderboardStat.CasualMmr or PlayerLeaderboardStat.ScorePerMinute or PlayerLeaderboardStat.PlayerLevel => null, // ranked from archive scores/account data in ReadAll
            PlayerLeaderboardStat.BestStreak => t.AccoladeMatches > 0 ? t.BestStreak : null,
            // Kill-chain accolades average over the rounds tracked since accolades began, and need as many of those.
            _ when t.AccoladeMatches < MinMatchesPlayed => null,
            PlayerLeaderboardStat.FirstKills => PerMatch(t.FirstKills, t.AccoladeMatches),
            PlayerLeaderboardStat.DoubleKills => PerMatch(t.DoubleKills, t.AccoladeMatches),
            PlayerLeaderboardStat.MultiKills => PerMatch(t.MultiKills, t.AccoladeMatches),
            PlayerLeaderboardStat.UltraKills => PerMatch(t.UltraKills, t.AccoladeMatches),
            PlayerLeaderboardStat.FantasticKills => PerMatch(t.FantasticKills, t.AccoladeMatches),
            _ => PerMatch(t.UnbelievableKills, t.AccoladeMatches)
        };
    }
}
