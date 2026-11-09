using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

[Table("ReputationPolicy")]
public sealed class ReputationPolicy
{
    [PrimaryKey] public int Version { get; set; }
    public long StartedAt { get; set; }
}
[Table("ReputationEvents")]
public sealed class ReputationEvent
{
    [PrimaryKey] public string Id { get; set; } = "";
    [Indexed] public uint PlayerId { get; set; }
    public string MatchId { get; set; } = "";
    public long EndedAt { get; set; }
    public string Reason { get; set; } = "";
    public int Delta { get; set; }
    public int ScoreAfter { get; set; }
    public int StreakAfter { get; set; }
}
public sealed record ReputationView(int Version, uint PlayerId, int Score, string Status);

// Independent of skill rating and queue bans. No chat deductions or historical backfill.
// Call Apply inside the match archive transaction so archive retries cannot double-score.
public static class Reputation
{
    public const int Version = 1, InitialScore = 100, ReconnectSeconds = 120, RecoverySeconds = 180;
    public static void CreateTables(SQLiteConnection db) => CreateTables(db, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    public static void CreateTables(SQLiteConnection db, long now)
    {
        db.CreateTable<ReputationPolicy>(); db.CreateTable<ReputationEvent>();
        db.Execute("INSERT OR IGNORE INTO ReputationPolicy (Version, StartedAt) VALUES (?, ?)", Version, now);
    }
    public static string Status(int score) => score >= 80 ? "Good" : score >= 60 ? "Fair" : score >= 40 ? "Low" : "Very low";
    public static ReputationView Read(SQLiteConnection db, uint player)
    {
        var last = db.Table<ReputationEvent>().Where(e => e.PlayerId == player)
            .OrderByDescending(e => e.EndedAt).ThenByDescending(e => e.MatchId).FirstOrDefault();
        int score = last?.ScoreAfter ?? InitialScore;
        return new(Version, player, score, Status(score));
    }
    public static string? Classify(CompletedMatchRecord match, CompletedMatchPlayer player)
    {
        var segments = player.Presences.OrderBy(s => s.JoinedAt).ToArray();
        ulong absent = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.LeftAt is not { } left || left >= match.EndedAt) continue;
            if (s.LeaveKind == MatchLeaveKind.Quit) return "Quit";
            if (s.LeaveKind == MatchLeaveKind.Inactivity) return "AFK";
            if (s.LeaveKind == MatchLeaveKind.Disconnect)
            {
                // End-of-match grace protects a connection lost just before the result.
                ulong next = i + 1 < segments.Length ? segments[i + 1].JoinedAt : match.EndedAt;
                absent += next > left ? next - left : 0;
                if (absent > ReconnectSeconds * 1000UL) return "Disconnect";
            }
        }
        if (segments.Length == 0 || segments[^1].LeaveKind != MatchLeaveKind.MatchEnded ||
            segments.Any(s => s.LeaveKind == MatchLeaveKind.Kicked) || match.ActiveStartedAt is null ||
            PersonalMmr.Seconds(match, player) < RecoverySeconds || player.TotalScore <= 0)
            return null;
        return "Completion";
    }
    public static void Apply(SQLiteConnection db, CompletedMatchRecord match)
    {
        var policy = db.Find<ReputationPolicy>(Version);
        if (policy is null || !PersonalMmr.Public(match.GameModeKey) ||
            match.StartedAt < (ulong)policy.StartedAt || match.EndedAt <= match.StartedAt ||
            match.EndReason == MatchEndReason.Unknown) return;
        foreach (var player in match.Players)
        {
            var id = $"{match.MatchId}/{player.PlayerId}";
            if (db.Find<ReputationEvent>(id) != null || Classify(match, player) is not { } reason) continue;
            db.Insert(new ReputationEvent { Id = id, MatchId = match.MatchId, PlayerId = player.PlayerId,
                EndedAt = checked((long)match.EndedAt), Reason = reason });
            // Late region results remain deterministic: recalculate this player's ledger in match order.
            int score = InitialScore, streak = 0;
            foreach (var entry in db.Table<ReputationEvent>().Where(e => e.PlayerId == player.PlayerId)
                         .OrderBy(e => e.EndedAt).ThenBy(e => e.MatchId).ToList())
            {
                int change;
                if (entry.Reason == "Completion") { change = 1; streak = 0; }
                else { change = streak == 0 ? -10 : streak == 1 ? -15 : -20; streak = Math.Min(2, streak + 1); }
                int next = Math.Clamp(score + change, 0, InitialScore);
                entry.Delta = next - score; entry.ScoreAfter = next; entry.StreakAfter = streak;
                db.Update(entry); score = next;
            }
        }
    }
}
