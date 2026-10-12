using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.ServerTypes;

// A match owns this instance. Assignments survive lobby departure and reconnect.
public sealed class RandomHeroRoster
{
    private readonly Key[] roster;
    private readonly Dictionary<TeamType, Queue<Key>> decks;
    private readonly Dictionary<uint, (TeamType Team, Key Hero)> assignments = new();
    public IReadOnlyList<Key> Heroes => Array.AsReadOnly(roster);

    public RandomHeroRoster(IEnumerable<(Key Hero, Key Class)> candidates, IReadOnlyList<Key> classes, Random random)
    {
        var pool = candidates.DistinctBy(h => h.Hero).Where(h => classes.Contains(h.Class)).ToArray();
        var picked = new List<Key>();
        foreach (var heroClass in classes.Distinct())
        {
            var members = pool.Where(h => h.Class == heroClass).ToArray();
            if (members.Length == 0) throw new InvalidOperationException("Random heroes needs every class.");
            picked.Add(members[random.Next(members.Length)].Hero);
        }
        var remaining = pool.Where(h => !picked.Contains(h.Hero)).Select(h => h.Hero).ToArray();
        random.Shuffle(remaining);
        picked.AddRange(remaining.Take(5 - picked.Count));
        if (picked.Count != 5) throw new InvalidOperationException("Random heroes needs five distinct heroes.");
        roster = picked.ToArray();
        decks = new();
        foreach (var team in new[] { TeamType.Team1, TeamType.Team2 })
        {
            var deck = roster.ToArray();
            random.Shuffle(deck);
            decks[team] = new Queue<Key>(deck);
        }
    }

    public Key Assign(uint playerId, TeamType team)
    {
        if (assignments.TryGetValue(playerId, out var old))
        {
            if (old.Team != team) throw new InvalidOperationException("Cannot switch teams during random heroes.");
            return old.Hero;
        }
        if (!decks.TryGetValue(team, out var deck) || deck.Count == 0)
            throw new InvalidOperationException("Random heroes supports five players per team.");
        var hero = deck.Dequeue();
        assignments.Add(playerId, (team, hero));
        return hero;
    }

    public static RandomHeroRoster CreateFromCatalogue() => new(
        CatalogueHelper.GetHeroes().Select(h => (h, (h.GetCard<CardUnit>()?.Data as UnitDataPlayer)?.Class ?? Key.None)),
        new[] { CatalogueHelper.BrawnClassKey, CatalogueHelper.SkillsClassKey, CatalogueHelper.BrainsClassKey }, Random.Shared);
}
