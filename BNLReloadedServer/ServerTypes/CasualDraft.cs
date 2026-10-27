using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.ServerTypes;

/// <summary>One match's ballot. A vote is final, authenticated by the lobby, and scoped to its phase.</summary>
public sealed class CasualDraft
{
    public static readonly Key[] Classes = [CatalogueHelper.SkillsClassKey, CatalogueHelper.BrawnClassKey, CatalogueHelper.BrainsClassKey];
    public int Phase { get; private set; } = -1;
    public long Deadline { get; private set; }
    public Dictionary<uint, Key> Votes { get; } = [];
    public Key[] Candidates { get; private set; } = [];
    public bool Voting => Phase is >= 0 and < 3;

    public void Begin(int phase, IEnumerable<Key> candidates, long now)
    {
        Phase = phase; Candidates = candidates.Distinct().ToArray(); Votes.Clear(); Deadline = now + 15000;
    }

    public bool Vote(uint player, int phase, Key hero, long now)
    {
        if (!Voting || phase != Phase || now >= Deadline || !Candidates.Contains(hero)) return false;
        return Votes.TryAdd(player, hero);
    }

    public Key Winner(Random random)
    {
        if (Candidates.Length == 0) throw new InvalidOperationException("No legal casual ban candidates");
        var counts = Candidates.Select(h => (Hero: h, Count: Votes.Values.Count(v => v == h))).ToArray();
        int most = counts.Max(h => h.Count);
        var tied = counts.Where(h => h.Count == most).ToArray();
        return tied[random.Next(tied.Length)].Hero;
    }

    public void Finish() { Phase = 3; Deadline = 0; Candidates = []; Votes.Clear(); }

    public static uint[] Order(IEnumerable<RankedSnakeDraft.Player> roster, Random random)
    {
        var players = roster.ToArray();
        var teams = new[] { TeamType.Team1, TeamType.Team2 }.Select(t =>
        {
            var ids = players.Where(p => p.Team == t).Select(p => p.Id).ToArray();
            random.Shuffle(ids);
            return new Queue<uint>(ids);
        }).ToArray();
        var result = new List<uint>(); int side = random.Next(2), count = 1;
        while (teams.Any(q => q.Count > 0))
        {
            for (int i = 0; i < count && teams[side].Count > 0; i++) result.Add(teams[side].Dequeue());
            side = 1 - side; count = 2;
        }
        return result.ToArray();
    }

    public record Seat(uint Id, TeamType Team, Key Hero);
    public record Hero(Key Key, Key Class);

    /// <summary>Heroes may repeat. Each required team must still be able to fill its missing classes,
    /// assigning different remaining seats to each missing class and respecting individual ownership.</summary>
    public static bool CanComplete(IReadOnlyList<Seat> seats, IReadOnlyList<Hero> heroes,
        Func<uint, Key, bool> owns, IReadOnlySet<TeamType> minimumTeams)
    {
        foreach (var seat in seats)
            if (seat.Hero == Key.None ? !heroes.Any(h => owns(seat.Id, h.Key)) :
                !heroes.Any(h => h.Key == seat.Hero && owns(seat.Id, h.Key))) return false;
        foreach (var team in minimumTeams)
        {
            var members = seats.Where(s => s.Team == team).ToArray();
            var present = members.Where(s => s.Hero != Key.None)
                .Select(s => heroes.First(h => h.Key == s.Hero).Class).ToHashSet();
            var missing = Classes.Where(c => !present.Contains(c)).ToArray();
            var pending = members.Where(s => s.Hero == Key.None).ToArray();
            if (missing.Length > pending.Length) return false;
            var assigned = new HashSet<uint>();
            bool Assign(int index)
            {
                if (index == missing.Length) return true;
                foreach (var seat in pending)
                {
                    if (assigned.Contains(seat.Id) || !heroes.Any(h => h.Class == missing[index] && owns(seat.Id, h.Key))) continue;
                    assigned.Add(seat.Id);
                    if (Assign(index + 1)) return true;
                    assigned.Remove(seat.Id);
                }
                return false;
            }
            if (!Assign(0)) return false;
        }
        return true;
    }
}
