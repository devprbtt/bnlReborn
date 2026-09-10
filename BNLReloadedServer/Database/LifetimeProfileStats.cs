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
}
public sealed record LifetimeHeroStats(long HeroKey, LifetimeCombatTotals Combat);
public sealed record LifetimeProfileStats(int Version, uint PlayerId, long? Since, LifetimeCombatTotals Overall,
    List<LifetimeHeroStats> Heroes, long UnattributedMatches, long ExcludedMatches)
{
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
                Enum.GetValues<PlayerMatchStatType>().Any(k => !stats.ContainsKey(k) || stats[k] < 0) ||
                raw.Values.Any(v => !float.IsFinite(v) || v < 0) ||
                raw.GetValueOrDefault(ScoreType.KillPlayerCriticalByHero) > stats[PlayerMatchStatType.Kill]) { excluded++; continue; }
            since = Math.Min(since ?? match.StartedAt, match.StartedAt);
            Add(overall, stats, raw, seconds, player.IsWinner);
            if (!heroes.TryGetValue(keys[0], out var combat)) heroes[keys[0]] = combat = new();
            Add(combat, stats, raw, seconds, player.IsWinner);
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
