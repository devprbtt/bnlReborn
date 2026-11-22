using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

namespace BNLReloadedServer.Database;

// Frozen map-authored cards from the original client's downloaded CDB. These are
// separate catalogue identities so other matches never inherit historical stats.
public static class TimeAssaultTraps
{
    private sealed class Baseline
    {
        public Dictionary<string, string> MapUnitOverrides { get; set; } = [];
        public Dictionary<string, string> MapBlockOverrides { get; set; } = [];
        public List<Card> Cards { get; set; } = [];
    }

    private static Baseline Load()
    {
        using var stream = typeof(TimeAssaultTraps).Assembly.GetManifestResourceStream(
            "BNLReloadedServer.Database.TimeAssaultOriginalTraps.json")
            ?? throw new InvalidDataException("Missing original Time Assault trap baseline");
        return JsonSerializer.Deserialize<Baseline>(stream, JsonHelper.DefaultSerializerSettings)
            ?? throw new InvalidDataException("Invalid original Time Assault trap baseline");
    }

    private static readonly Baseline Mapping = Load();
    private static readonly Dictionary<Key, Key> UnitKeys = Mapping.MapUnitOverrides
        .ToDictionary(p => new Key(p.Key), p => new Key(p.Value));

    public static void Register(List<Card> cards)
    {
        // Read fresh mutable instances on every replication; do not share them
        // with previous catalogue snapshots or mutate original gameplay cards.
        var historical = Load().Cards;
        var reserved = historical.Select(c => c.Id).ToHashSet();
        var occupied = cards.OfType<CardBlock>().Where(c => !reserved.Contains(c.Id))
            .Select(c => c.BlockId).ToHashSet();
        foreach (var block in historical.OfType<CardBlock>())
            if (!occupied.Add(block.BlockId))
                throw new InvalidDataException($"Time Assault trap block id collision: {block.BlockId}");
        cards.RemoveAll(c => reserved.Contains(c.Id));
        cards.AddRange(historical);
    }

    public static MapUnit SelectMapUnit(MapUnit unit, MatchType match)
    {
        if (match != MatchType.TimeTrial || !UnitKeys.TryGetValue(unit.UnitKey, out var key)) return unit;
        if (key.GetCard<CardUnit>() == null) throw new InvalidDataException("Time Assault trap baseline not registered");
        return new MapUnit { UnitKey = key, Position = unit.Position, Rotation = unit.Rotation, Team = unit.Team };
    }

    public static void ApplyTerrain(MapBinary terrain, MatchType match)
    {
        if (match != MatchType.TimeTrial) return;
        var ids = new Dictionary<ushort, ushort>();
        foreach (var (source, target) in Mapping.MapBlockOverrides)
        {
            var original = new Key(source).GetCard<CardBlock>();
            var replacement = new Key(target).GetCard<CardBlock>()
                ?? throw new InvalidDataException("Time Assault trap baseline not registered");
            if (original != null) ids.Add(original.BlockId, replacement.BlockId);
        }
        for (int x = 0; x < terrain.Size.x; x++)
        for (int y = 0; y < terrain.Size.y; y++)
        for (int z = 0; z < terrain.Size.z; z++)
        {
            var block = terrain[new Vector3s(x, y, z)];
            if (ids.TryGetValue(block.Id, out var id)) block.Id = id;
        }
    }
}
