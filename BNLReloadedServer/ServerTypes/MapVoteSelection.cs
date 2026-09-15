using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

internal static class MapVoteSelection
{
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
