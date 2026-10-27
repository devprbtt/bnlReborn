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

    /// <summary>Check that a candidate still permits a complete legal draft for BOTH teams.
    /// Includes ownership, global uniqueness, class caps and class minimums, including future seats.</summary>
    public static bool CanComplete(IReadOnlyList<Seat> seats, IReadOnlyList<Hero> heroes,
        Func<uint, Key, bool> owns, IReadOnlySet<TeamType> minimumTeams)
    {
        var used = new HashSet<Key>();
        var counts = new Dictionary<(TeamType, Key), int>();
        var pending = new List<Seat>();
        foreach (var seat in seats)
        {
            if (seat.Hero == Key.None) { pending.Add(seat); continue; }
            var hero = heroes.FirstOrDefault(h => h.Key == seat.Hero);
            if (hero == null || !used.Add(hero.Key) || !owns(seat.Id, hero.Key)) return false;
            var index = (seat.Team, hero.Class);
            counts[index] = counts.GetValueOrDefault(index) + 1;
            if (counts[index] > 2) return false;
        }
        bool Search(List<Seat> remaining)
        {
            if (heroes.Count(h => !used.Contains(h.Key)) < remaining.Count) return false;
            foreach (var c in Classes)
                if (minimumTeams.Count(t => counts.GetValueOrDefault((t, c)) == 0) >
                    heroes.Count(h => h.Class == c && !used.Contains(h.Key))) return false;
            foreach (var team in minimumTeams)
            {
                var missing = Classes.Where(c => counts.GetValueOrDefault((team, c)) == 0).ToArray();
                if (missing.Length > remaining.Count(s => s.Team == team)) return false;
                if (missing.Any(c => !heroes.Any(h => h.Class == c && !used.Contains(h.Key) &&
                    remaining.Any(s => s.Team == team && owns(s.Id, h.Key))))) return false;
            }
            if (remaining.Count == 0) return true;
            var choices = remaining.Select(s => (Seat: s, Options: heroes.Where(h => !used.Contains(h.Key) &&
                counts.GetValueOrDefault((s.Team, h.Class)) < 2 && owns(s.Id, h.Key)).ToArray()))
                .OrderBy(x => x.Options.Length).First();
            var next = remaining.Where(s => s.Id != choices.Seat.Id).ToList();
            foreach (var hero in choices.Options.OrderBy(h => counts.GetValueOrDefault((choices.Seat.Team, h.Class))))
            {
                var index = (choices.Seat.Team, hero.Class);
                counts[index] = counts.GetValueOrDefault(index) + 1; used.Add(hero.Key);
                if (Search(next)) return true;
                counts[index]--; used.Remove(hero.Key);
            }
            return false;
        }
        var available = heroes.Where(h => !used.Contains(h.Key)).ToArray();
        // Public hero pools need only a three-class capacity check, not permutations of equivalent heroes.
        if (heroes.All(h => Classes.Contains(h.Class)) && pending.All(s => available.All(h => owns(s.Id, h.Key))))
        {
            var supply = Classes.Select(c => available.Count(h => h.Class == c)).ToArray();
            var teams = seats.Select(s => s.Team).Union(minimumTeams).ToArray();
            bool Allocate(int teamIndex)
            {
                if (teamIndex == teams.Length) return true;
                var team = teams[teamIndex];
                int slots = pending.Count(s => s.Team == team);
                var existing = Classes.Select(c => counts.GetValueOrDefault((team, c))).ToArray();
                for (int skill = 0; skill <= 2; skill++)
                for (int brawn = 0; brawn <= 2; brawn++)
                {
                    int brain = slots - skill - brawn;
                    if (brain < 0 || brain > 2) continue;
                    int[] allocation = [skill, brawn, brain];
                    if (Enumerable.Range(0, 3).Any(c => allocation[c] > supply[c] ||
                        existing[c] + allocation[c] > 2 || minimumTeams.Contains(team) && existing[c] + allocation[c] < 1)) continue;
                    for (int c = 0; c < 3; c++) supply[c] -= allocation[c];
                    if (Allocate(teamIndex + 1)) return true;
                    for (int c = 0; c < 3; c++) supply[c] += allocation[c];
                }
                return false;
            }
            return Allocate(0);
        }
        return Search(pending);
    }
}
