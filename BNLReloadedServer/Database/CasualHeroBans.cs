using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>One closed casual-draft ban ballot: the hero it banned and every candidate's vote count.</summary>
[Table("CasualHeroBans")]
public sealed class CasualHeroBanRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Column("session")] public string Session { get; set; } = string.Empty;
    [Column("phase")] public int Phase { get; set; }
    [Column("class_key")] public long ClassKey { get; set; }
    [Column("hero_key")] public long HeroKey { get; set; }
    // Card id at ban time, so the raw table stays readable after a catalogue change.
    [Column("hero_id")] public string HeroId { get; set; } = string.Empty;
    [Column("hero_votes")] public int HeroVotes { get; set; }
    // Zero voters means the ban was a random pick among all candidates, not a choice.
    [Column("voters")] public int Voters { get; set; }
    [Column("players")] public int Players { get; set; }
    // Every candidate as "hash:votes", comma separated, including candidates nobody voted for.
    [Column("ballot")] public string Ballot { get; set; } = string.Empty;
    [Indexed, Column("banned_at")] public long BannedAt { get; set; }
}

public sealed record CasualHeroBanStat(long HeroKey, long ClassKey, int Bans, int VotedBans, int RandomBans, int Offered, int Votes);

public sealed record CasualHeroBanSummary(long Since, int Ballots, int Drafts, int UnvotedBallots, List<CasualHeroBanStat> Heroes);

/// <summary>
/// Casual draft ban history. The lobby reports through <see cref="Recorder"/> so it never opens the
/// player database itself; the master database installs the recorder, fixtures can install their own.
/// </summary>
public static class CasualHeroBans
{
    public static Action<CasualHeroBanRecord>? Recorder { get; set; }

    public static CasualHeroBanRecord Create(string session, int phase, Key classKey, Key hero,
        IReadOnlyCollection<Key> candidates, IReadOnlyDictionary<uint, Key> votes, int players, long now) => new()
    {
        Session = session, Phase = phase, ClassKey = classKey.Hash, HeroKey = hero.Hash,
        HeroId = hero.GetCard<CardUnit>()?.Id ?? hero.Hash.ToString(),
        HeroVotes = votes.Values.Count(v => v == hero), Voters = votes.Count, Players = players,
        Ballot = string.Join(",", candidates.Select(c => c.Hash + ":" + votes.Values.Count(v => v == c))),
        BannedAt = now
    };

    public static IEnumerable<(long Hero, int Votes)> ReadBallot(string ballot) =>
        ballot.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(entry => entry.Split(':'))
            .Where(p => p.Length == 2 && long.TryParse(p[0], out _) && int.TryParse(p[1], out _))
            .Select(p => (long.Parse(p[0]), int.Parse(p[1])));

    private sealed class Tally(long classKey)
    {
        public readonly long ClassKey = classKey;
        public int Bans, Voted, Random, Offered, Votes;
    }

    public static CasualHeroBanSummary Summarize(IReadOnlyCollection<CasualHeroBanRecord> rows, long since)
    {
        var heroes = new Dictionary<long, Tally>();
        Tally For(long hero, long classKey) => heroes.TryGetValue(hero, out var t) ? t : heroes[hero] = new Tally(classKey);
        foreach (var row in rows)
        {
            foreach (var (hero, votes) in ReadBallot(row.Ballot))
            {
                var t = For(hero, row.ClassKey);
                t.Offered++; t.Votes += votes;
            }
            var banned = For(row.HeroKey, row.ClassKey);
            banned.Bans++;
            if (row.HeroVotes > 0) banned.Voted++;
            if (row.Voters == 0) banned.Random++;
        }
        return new(since, rows.Count, rows.Select(r => r.Session).Distinct().Count(), rows.Count(r => r.Voters == 0),
            heroes.Select(h => new CasualHeroBanStat(h.Key, h.Value.ClassKey, h.Value.Bans, h.Value.Voted, h.Value.Random, h.Value.Offered, h.Value.Votes))
                .OrderByDescending(h => h.VotedBans).ThenByDescending(h => h.Votes).ThenByDescending(h => h.Bans).ToList());
    }
}
