using SQLite;

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
    CasualMmr
}

public sealed record PlayerLeaderboardRow(int Rank, uint PlayerId, string Nickname, double Value, long Matches);

public sealed record PlayerLeaderboard(int Version, string Stat, string Period, long UpdatedAt, int MinMatches,
    long Players, List<PlayerLeaderboardRow> Rows);

/// <summary>
/// Player stat leaderboards over the match archive. Rounds use the profile eligibility rule
/// (<see cref="LifetimeProfileStats.Eligible"/>), restricted to ranked and casual so private custom games cannot be
/// farmed. Every board needs <see cref="MinMatchesPlayed"/> counted rounds, so a handful of lucky matches cannot top
/// it. Count stats (kills, headshots, assists, objective damage, kill-chain accolades) rank the average per match;
/// average DPS, win rate and KDR are already rates; hours, matches and best streak stay totals or the record.
/// Accolade averages divide by, and require, rounds recorded after accolade tracking began. Hours are the time
/// actually played in counted rounds. Casual MMR ranks the account's casual rating among players with that many
/// counted casual rounds.
/// </summary>
public static class PlayerStatLeaderboards
{
    public const int Limit = 100;
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
        ["casual_mmr"] = PlayerLeaderboardStat.CasualMmr
    };

    /// <param name="recentOnly">Last <see cref="RecentDays"/> days by match end, otherwise the whole archive.</param>
    /// <param name="modeKeys">Game-mode key hashes that count (ranked and casual).</param>
    /// <param name="casualModeKey">The casual mode's key hash; its rounds qualify a player for the casual MMR board.</param>
    public static PlayerLeaderboard Read(SQLiteConnection db, PlayerLeaderboardStat stat, bool recentOnly, long nowMs,
        IReadOnlyCollection<long> modeKeys, long? casualModeKey = null)
    {
        return ReadAll(db, recentOnly, nowMs, modeKeys, onlyStat: stat, casualModeKey: casualModeKey)[stat];
    }

    // Aggregate the archive once for the complete trophy reconciliation. Include every tied leader beyond top 100.
    public static Dictionary<PlayerLeaderboardStat, PlayerLeaderboard> ReadAll(SQLiteConnection db, bool recentOnly,
        long nowMs, IReadOnlyCollection<long> modeKeys, bool includeAllLeaders = false, PlayerLeaderboardStat? onlyStat = null,
        long? casualModeKey = null)
    {
        var after = recentOnly ? nowMs - RecentDays * 86_400_000L : long.MinValue;
        var matches = db.Table<ArchivedMatchRecord>().ToList()
            .Where(m => modeKeys.Contains(m.GameModeKey) && m.EndedAt >= after)
            .ToDictionary(m => m.Id);
        var presences = db.Table<ArchivedMatchPresenceRecord>().ToList()
            .Where(p => matches.ContainsKey(p.MatchId))
            .GroupBy(p => (p.MatchId, p.PlayerId)).ToDictionary(g => g.Key, g => g.ToList());
        var totals = new Dictionary<uint, LifetimeCombatTotals>();
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
            if (match.GameModeKey == casualModeKey) casualRounds[id] = casualRounds.GetValueOrDefault(id) + 1;
            if (!nicknames.TryGetValue(id, out var known) || match.EndedAt > known.At) nicknames[id] = (match.EndedAt, player.Nickname);
        }
        // Current account names win over the name used in the last archived round.
        var records = db.Table<PlayerRecord>().ToList();
        var accounts = records.ToDictionary(p => p.PlayerId, p => p.Username);
        // Casual MMR in display points (the profile's casual_mmr), for accounts on the ladder with enough casual rounds.
        var casualMmr = records
            .Where(p => LeagueRanker.IsOnLadder(p) && casualRounds.GetValueOrDefault(p.PlayerId) >= MinMatchesPlayed)
            .ToDictionary(p => p.PlayerId, p => (Value: (double)LeagueRules.Points(p.RatingMean), Matches: casualRounds[p.PlayerId]));

        var boards = new Dictionary<PlayerLeaderboardStat, PlayerLeaderboard>();
        foreach (var stat in StatNames.Values.Where(s => onlyStat == null || s == onlyStat))
        {
            var ranked = (stat == PlayerLeaderboardStat.CasualMmr
                    ? casualMmr.Select(pair => (Id: pair.Key, Value: (double?)pair.Value.Value, Matches: pair.Value.Matches))
                    : totals.Select(pair => (Id: pair.Key, Value: Value(stat, pair.Value), Matches: pair.Value.Matches)))
                .Where(r => r.Value is > 0)
                .OrderByDescending(r => r.Value).ThenBy(r => r.Matches).ThenBy(r => r.Id)
                .ToList();
            var rows = new List<PlayerLeaderboardRow>();
            for (int i = 0; i < ranked.Count && (rows.Count < Limit || (includeAllLeaders && ranked[i].Value == ranked[0].Value)); i++)
            {
                // Equal values share a rank (1, 2, 2, 4).
                int rank = i > 0 && ranked[i].Value == ranked[i - 1].Value ? rows[^1].Rank : i + 1;
                var name = accounts.TryGetValue(ranked[i].Id, out var username) && !string.IsNullOrWhiteSpace(username)
                    ? username
                    : nicknames.GetValueOrDefault(ranked[i].Id).Name ?? string.Empty;
                rows.Add(new PlayerLeaderboardRow(rank, ranked[i].Id, name, ranked[i].Value!.Value, ranked[i].Matches));
            }
            boards[stat] = new PlayerLeaderboard(1, StatNames.First(p => p.Value == stat).Key, recentOnly ? "30d" : "all", nowMs,
                MinMatches(stat), ranked.Count, rows);
        }
        return boards;
    }

    public static int MinMatches(PlayerLeaderboardStat stat) => MinMatchesPlayed;

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
            PlayerLeaderboardStat.CasualMmr => null, // ranked from the account rating in ReadAll
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
