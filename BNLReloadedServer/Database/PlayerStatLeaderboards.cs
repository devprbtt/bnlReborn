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
    BestStreak
}

public sealed record PlayerLeaderboardRow(int Rank, uint PlayerId, string Nickname, double Value, long Matches);

public sealed record PlayerLeaderboard(int Version, string Stat, string Period, long UpdatedAt, int MinMatches,
    long Players, List<PlayerLeaderboardRow> Rows);

/// <summary>
/// Player stat leaderboards over the match archive. Rounds use the profile eligibility rule
/// (<see cref="LifetimeProfileStats.Eligible"/>), restricted to ranked and casual so private custom games cannot be
/// farmed. Average DPS needs <see cref="MinDpsMatches"/> rounds with damage counters, and win rate and KDR need
/// <see cref="MinRateMatches"/> rounds, so one lucky match cannot top a rate; accolade rankings only use rounds
/// recorded after accolade tracking began. Hours are the time actually played in counted rounds.
/// </summary>
public static class PlayerStatLeaderboards
{
    public const int Limit = 100;
    public const int MinDpsMatches = 10;
    public const int MinRateMatches = 10;
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
        ["best_streak"] = PlayerLeaderboardStat.BestStreak
    };

    /// <param name="recentOnly">Last <see cref="RecentDays"/> days by match end, otherwise the whole archive.</param>
    /// <param name="modeKeys">Game-mode key hashes that count (ranked and casual).</param>
    public static PlayerLeaderboard Read(SQLiteConnection db, PlayerLeaderboardStat stat, bool recentOnly, long nowMs,
        IReadOnlyCollection<long> modeKeys)
    {
        return ReadAll(db, recentOnly, nowMs, modeKeys, onlyStat: stat)[stat];
    }

    // Aggregate the archive once for the complete trophy reconciliation. Include every tied leader beyond top 100.
    public static Dictionary<PlayerLeaderboardStat, PlayerLeaderboard> ReadAll(SQLiteConnection db, bool recentOnly,
        long nowMs, IReadOnlyCollection<long> modeKeys, bool includeAllLeaders = false, PlayerLeaderboardStat? onlyStat = null)
    {
        var after = recentOnly ? nowMs - RecentDays * 86_400_000L : long.MinValue;
        var matches = db.Table<ArchivedMatchRecord>().ToList()
            .Where(m => modeKeys.Contains(m.GameModeKey) && m.EndedAt >= after)
            .ToDictionary(m => m.Id);
        var presences = db.Table<ArchivedMatchPresenceRecord>().ToList()
            .Where(p => matches.ContainsKey(p.MatchId))
            .GroupBy(p => (p.MatchId, p.PlayerId)).ToDictionary(g => g.Key, g => g.ToList());
        var totals = new Dictionary<uint, LifetimeCombatTotals>();
        var nicknames = new Dictionary<uint, (long At, string Name)>();
        foreach (var player in db.Table<ArchivedMatchPlayerRecord>().ToList())
        {
            if (!matches.TryGetValue(player.MatchId, out var match)) continue;
            var round = LifetimeProfileStats.Eligible(match, presences.GetValueOrDefault((player.MatchId, player.PlayerId)) ?? [], player);
            if (round is null) continue;
            var id = (uint)player.PlayerId;
            if (!totals.TryGetValue(id, out var total)) totals[id] = total = new LifetimeCombatTotals();
            round.AddTo(total);
            if (!nicknames.TryGetValue(id, out var known) || match.EndedAt > known.At) nicknames[id] = (match.EndedAt, player.Nickname);
        }
        // Current account names win over the name used in the last archived round.
        var accounts = db.Table<PlayerRecord>().ToList().ToDictionary(p => p.PlayerId, p => p.Username);

        var boards = new Dictionary<PlayerLeaderboardStat, PlayerLeaderboard>();
        foreach (var stat in StatNames.Values.Where(s => onlyStat == null || s == onlyStat))
        {
            var ranked = totals
                .Select(pair => (Id: pair.Key, Value: Value(stat, pair.Value), Matches: pair.Value.Matches))
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

    public static int MinMatches(PlayerLeaderboardStat stat) => stat switch
    {
        PlayerLeaderboardStat.AverageDps => MinDpsMatches,
        PlayerLeaderboardStat.WinRate or PlayerLeaderboardStat.Kdr => MinRateMatches,
        _ => 1
    };

    /// <summary>The ranked value, or null when the player does not qualify for this stat.</summary>
    public static double? Value(PlayerLeaderboardStat stat, LifetimeCombatTotals t) => stat switch
    {
        PlayerLeaderboardStat.Kills => t.Kills,
        PlayerLeaderboardStat.Headshots => t.HeadshotKills,
        PlayerLeaderboardStat.AverageDps => t.DetailedMatches >= MinDpsMatches && t.DetailedSeconds > 0
            ? Math.Round(t.Damage / t.DetailedSeconds, 2) : null,
        PlayerLeaderboardStat.Assists => t.Assists,
        PlayerLeaderboardStat.ObjectiveDamage => t.Objective,
        PlayerLeaderboardStat.Hours => Math.Round(t.Seconds / 3600d, 2),
        PlayerLeaderboardStat.Matches => t.Matches,
        // Percent of counted rounds won.
        PlayerLeaderboardStat.WinRate => t.Matches >= MinRateMatches ? Math.Round(100d * t.Wins / t.Matches, 1) : null,
        // Kills per death; a deathless record divides by one.
        PlayerLeaderboardStat.Kdr => t.Matches >= MinRateMatches ? Math.Round((double)t.Kills / Math.Max(t.Deaths, 1), 2) : null,
        _ when t.AccoladeMatches == 0 => null,
        PlayerLeaderboardStat.FirstKills => t.FirstKills,
        PlayerLeaderboardStat.DoubleKills => t.DoubleKills,
        PlayerLeaderboardStat.MultiKills => t.MultiKills,
        PlayerLeaderboardStat.UltraKills => t.UltraKills,
        PlayerLeaderboardStat.FantasticKills => t.FantasticKills,
        PlayerLeaderboardStat.UnbelievableKills => t.UnbelievableKills,
        _ => t.BestStreak
    };
}
