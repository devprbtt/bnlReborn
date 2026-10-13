using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using SQLite;

namespace BNLReloadedServer.Database;

// The durable archive is the ledger: replacing/retransmitting a round cannot add
// it twice. Read one SQLite snapshot, including every recorded round, not history's ten.
public sealed class LifetimeCombatTotals
{
    public long Matches { get; set; }
    public long Wins { get; set; }
    public long Losses { get; set; }
    public long Kills { get; set; }
    public long Deaths { get; set; }
    public long Assists { get; set; }
    public long Objective { get; set; }
    public long Built { get; set; }
    public long Destroyed { get; set; }
    public long Earned { get; set; }
    public double Seconds { get; set; }
    public long DetailedMatches { get; set; }
    public double DetailedSeconds { get; set; }
    public double HeadshotKills { get; set; }
    public double DirectKills { get; set; }
    public double Damage { get; set; }
    public double Healing { get; set; }
    // Kill accolades, over the AccoladeMatches rounds recorded since tracking began (AccoladeSince). Headshot
    // accolades are not repeated here: HeadshotKills already reports them.
    public long AccoladeMatches { get; set; }
    public long? AccoladeSince { get; set; }
    public long FirstKills { get; set; }
    public long DoubleKills { get; set; }
    public long MultiKills { get; set; }
    public long UltraKills { get; set; }
    public long FantasticKills { get; set; }
    public long UnbelievableKills { get; set; }
    public int BestStreak { get; set; }
}

/// <summary>MatchPlayers.accolades: format byte, best streak, then the accolade -> count map.</summary>
public static class ArchivedAccolades
{
    private const byte Format = 1;

    public static byte[]? Write(Dictionary<KillAccolade, int>? accolades, int bestStreak)
    {
        if (accolades is null) return null;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Format);
            writer.Write(bestStreak);
            writer.WriteMap(accolades, writer.WriteByteEnum, writer.Write);
        }
        return stream.ToArray();
    }

    /// <summary>Null when the round was not tracked or the blob is not understood.</summary>
    public static (Dictionary<KillAccolade, int> Counts, int BestStreak)? Read(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (reader.ReadByte() != Format) return null;
            var best = reader.ReadInt32();
            var counts = reader.ReadMap<KillAccolade, int, Dictionary<KillAccolade, int>>(reader.ReadByteEnum<KillAccolade>, reader.ReadInt32);
            return best < 0 || counts.Values.Any(v => v < 0) ? null : (counts, best);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { return null; }
    }
}
public sealed record LifetimeHeroStats(long HeroKey, LifetimeCombatTotals Combat);
public sealed record LifetimeProfileStats(int Version, uint PlayerId, long? Since, LifetimeCombatTotals Overall,
    List<LifetimeHeroStats> Heroes, long UnattributedMatches, long ExcludedMatches)
{
    private static readonly PlayerMatchStatType[] RequiredCombatStats =
    [
        PlayerMatchStatType.Earned, PlayerMatchStatType.Built, PlayerMatchStatType.Destroyed,
        PlayerMatchStatType.Objective, PlayerMatchStatType.BlockAssist, PlayerMatchStatType.Kill,
        PlayerMatchStatType.Death, PlayerMatchStatType.Assist
    ];

    public static LifetimeProfileStats Read(SQLiteConnection db, uint playerId)
    {
        var overall = new LifetimeCombatTotals();
        var heroes = new Dictionary<long, LifetimeCombatTotals>();
        long? since = null; long excluded = 0;
        var players = db.Table<ArchivedMatchPlayerRecord>().Where(p => p.PlayerId == playerId).ToList();
        var presences = db.Table<ArchivedMatchPresenceRecord>().Where(p => p.PlayerId == playerId).ToList()
            .GroupBy(p => p.MatchId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var player in players)
        {
            var match = db.Find<ArchivedMatchRecord>(player.MatchId);
            var rows = presences.GetValueOrDefault(player.MatchId) ?? [];
            var keys = rows.Select(p => p.HeroKey).Distinct().ToArray();
            // One eligibility rule for every player's career and hero totals.
            // Raw maps are sparse: an absent counter means zero, an absent map
            // means unrecorded. Never keep kills from an ineligible record.
            if (match == null || match.EndedAt <= match.StartedAt ||
                match.Winner is not (int)TeamType.Team1 and not (int)TeamType.Team2 ||
                keys.Length != 1 || keys[0] == 0 || rows.Count == 0 ||
                rows.Any(p => p.JoinedAt > (p.LeftAt ?? match.EndedAt))) { excluded++; continue; }
            // Merge overlapping reconnect intervals and clamp them to the round.
            var seconds = PlayedSeconds(match, rows);
            Dictionary<PlayerMatchStatType,int> stats;
            Dictionary<ScoreType,float> raw;
            try { stats = ReadStats(player.Stats); raw = ReadRaw(player.RawStats); }
            catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { excluded++; continue; }
            if (seconds <= 0 || raw.Count == 0 ||
                RequiredCombatStats.Any(k => !stats.ContainsKey(k) || stats[k] < 0) ||
                stats.Values.Any(value => value < 0) ||
                raw.Values.Any(v => !float.IsFinite(v) || v < 0) ||
                raw.GetValueOrDefault(ScoreType.KillPlayerCriticalByHero) > stats[PlayerMatchStatType.Kill]) { excluded++; continue; }
            since = Math.Min(since ?? match.StartedAt, match.StartedAt);
            var accolades = ArchivedAccolades.Read(player.Accolades);
            Add(overall, stats, raw, seconds, player.IsWinner);
            AddAccolades(overall, accolades, match.StartedAt);
            if (!heroes.TryGetValue(keys[0], out var combat)) heroes[keys[0]] = combat = new();
            Add(combat, stats, raw, seconds, player.IsWinner);
            AddAccolades(combat, accolades, match.StartedAt);
        }
        return new(2, playerId, since, overall, heroes.Select(p => new LifetimeHeroStats(p.Key, p.Value)).ToList(), 0, excluded);
    }
    public static double PlayedSeconds(ArchivedMatchRecord match, IEnumerable<ArchivedMatchPresenceRecord> rows)
    {
        long end = match.StartedAt, milliseconds = 0;
        foreach (var row in rows.OrderBy(p => p.JoinedAt))
        {
            long start = Math.Max(end, Math.Max(match.StartedAt, row.JoinedAt));
            long stop = Math.Min(match.EndedAt, row.LeftAt ?? match.EndedAt);
            milliseconds += Math.Max(0, stop - start); end = Math.Max(end, stop);
        }
        return milliseconds / 1000d;
    }
    static Dictionary<PlayerMatchStatType,int> ReadStats(byte[] bytes)
    {
        if (bytes is not {Length: > 0}) return [];
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return reader.ReadMap<PlayerMatchStatType,int,Dictionary<PlayerMatchStatType,int>>(reader.ReadByteEnum<PlayerMatchStatType>,reader.ReadInt32);
    }
    static Dictionary<ScoreType,float> ReadRaw(byte[] bytes)
    {
        if (bytes is not {Length: > 0}) return [];
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return reader.ReadMap<ScoreType,float,Dictionary<ScoreType,float>>(reader.ReadByteEnum<ScoreType>,reader.ReadSingle);
    }
    static void AddAccolades(LifetimeCombatTotals t, (Dictionary<KillAccolade, int> Counts, int BestStreak)? accolades, long startedAt)
    {
        if (accolades is not { } a) return;
        t.AccoladeMatches++;
        t.AccoladeSince = Math.Min(t.AccoladeSince ?? startedAt, startedAt);
        t.FirstKills += a.Counts.GetValueOrDefault(KillAccolade.FirstKill);
        t.DoubleKills += a.Counts.GetValueOrDefault(KillAccolade.DoubleKill);
        t.MultiKills += a.Counts.GetValueOrDefault(KillAccolade.MultiKill);
        t.UltraKills += a.Counts.GetValueOrDefault(KillAccolade.UltraKill);
        t.FantasticKills += a.Counts.GetValueOrDefault(KillAccolade.Fantastic);
        t.UnbelievableKills += a.Counts.GetValueOrDefault(KillAccolade.Unbelievable);
        t.BestStreak = Math.Max(t.BestStreak, a.BestStreak);
    }
    static void Add(LifetimeCombatTotals t, Dictionary<PlayerMatchStatType,int> s, Dictionary<ScoreType,float> raw, double seconds, bool winner)
    {
        t.Matches++; t.Seconds += seconds;
        if (winner) t.Wins++; else t.Losses++;
        t.Kills += s.GetValueOrDefault(PlayerMatchStatType.Kill); t.Deaths += s.GetValueOrDefault(PlayerMatchStatType.Death);
        t.Assists += s.GetValueOrDefault(PlayerMatchStatType.Assist); t.Objective += s.GetValueOrDefault(PlayerMatchStatType.Objective);
        t.Built += s.GetValueOrDefault(PlayerMatchStatType.Built); t.Destroyed += s.GetValueOrDefault(PlayerMatchStatType.Destroyed);
        t.Earned += s.GetValueOrDefault(PlayerMatchStatType.Earned);
        if (raw.Count == 0) return;
        t.DetailedMatches++; t.DetailedSeconds += seconds;
        t.HeadshotKills += raw.GetValueOrDefault(ScoreType.KillPlayerCriticalByHero);
        t.DirectKills += raw.GetValueOrDefault(ScoreType.KillPlayerByHero);
        t.Damage += raw.GetValueOrDefault(ScoreType.DamagePlayerByHero) + raw.GetValueOrDefault(ScoreType.DamagePlayerByBlock);
        t.Healing += raw.GetValueOrDefault(ScoreType.HealPlayerByHero) + raw.GetValueOrDefault(ScoreType.HealPlayerByBlock);
    }
}
