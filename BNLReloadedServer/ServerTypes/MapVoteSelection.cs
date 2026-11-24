using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

internal static class MapVoteSelection
{
    internal static List<Key> SelectForPlayers(IEnumerable<CardMap> maps, int count, GameRankingType ranking, Random random,
        IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, bool>> preferences)
    {
        var eligible = maps.DistinctBy(m => m.Key).ToList();
        var keys = eligible.Select(m => m.Key.Hash).ToHashSet();
        int required = Math.Min(eligible.Count, Math.Max(3, count));
        // No selected eligible maps means "server decides", not a veto of all maps.
        var ballots = preferences.Values.Where(b => b.Any(p => p.Value && keys.Contains(p.Key))).ToArray();
        if (ballots.Length == 0) return Select(eligible, required, ranking, random);
        var allowed = new List<CardMap>(); var ties = new List<CardMap>(); var rejected = new List<CardMap>();
        foreach (var map in eligible)
        {
            int yes = 0, no = 0;
            foreach (var ballot in ballots)
                if (ballot.TryGetValue(map.Key.Hash, out bool value)) { if (value) yes++; else no++; }
            if (yes > no) allowed.Add(map);
            else if (yes < no) rejected.Add(map);
            else if (random.Next(2) == 0) allowed.Add(map);
            else ties.Add(map);
        }
        void Fill(List<CardMap> candidates)
        {
            if (allowed.Count >= required) return;
            var chosen = Select(candidates, required - allowed.Count, ranking, random).ToHashSet();
            allowed.AddRange(candidates.Where(m => chosen.Contains(m.Key)));
        }
        // Resolve shortages on the server: remaining ties before majority-rejected
        // maps, always keeping the original mode's playable/eligible constraints.
        Fill(ties); Fill(rejected);
        return Select(allowed, required, ranking, random);
    }

    internal static double ValidWeight(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value > 0
        ? value.Value : 1d;

    // Weighted sampling without replacement: one map card can occupy only one slot.
    internal static List<Key> Select(IEnumerable<CardMap> maps, int count, GameRankingType ranking, Random random)
    {
        bool casual = ranking is GameRankingType.Friendly or GameRankingType.Graveyard;
        var remaining = maps.DistinctBy(m => m.Key)
            .Select(m => (m.Key, Weight: casual ? ValidWeight(m.CasualVoteWeight) : 1d)).ToList();
        var result = new List<Key>();
        while (result.Count < count && remaining.Count > 0)
        {
            // Normalize every draw to avoid overflowing sums of finite CDB weights.
            double scale = remaining.Max(m => m.Weight);
            double total = remaining.Sum(m => m.Weight / scale);
            double draw = random.NextDouble() * total;
            int selected = remaining.Count - 1;
            for (int i = 0; i < remaining.Count; i++)
            {
                draw -= remaining[i].Weight / scale;
                if (draw < 0) { selected = i; break; }
            }
            result.Add(remaining[selected].Key);
            remaining.RemoveAt(selected);
        }
        return result;
    }
}
