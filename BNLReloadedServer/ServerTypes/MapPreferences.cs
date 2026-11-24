using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

// Per-account soft preferences. Clients persist their own choices and publish
// them on each connection; a pop captures immutable copies for its actual roster.
internal static class MapPreferences
{
    // Capability 2 adds the independent 5v5 slot; command framing remains v1.
    internal const byte Version = 2;
    internal const byte WireVersion = 1;
    internal const byte Casual4v4 = 0, Ranked = 1, Casual5v5 = 2;
    internal const int MaximumChoices = 512;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<(uint Player, byte Mode), Dictionary<uint, bool>> Players = [];

    internal static bool TryRead(BinaryReader reader, out byte mode, out Dictionary<uint, bool> choices)
    {
        mode = 0; choices = [];
        try
        {
            if (reader.ReadByte() != WireVersion) return false;
            mode = reader.ReadByte();
            int count = reader.ReadUInt16();
            if (mode > Casual5v5 || count > MaximumChoices) return false;
            // SessionReader may supply several coalesced frames in this stream.
            // Consume this command's declared records only, leaving following frames intact.
            if (reader.BaseStream.Length - reader.BaseStream.Position < count * 5L) return false;
            for (int i = 0; i < count; i++)
            {
                uint key = reader.ReadUInt32(); byte value = reader.ReadByte();
                if (key == 0 || value > 1 || !choices.TryAdd(key, value != 0)) return false;
            }
            return true;
        }
        catch (EndOfStreamException) { return false; }
    }

    internal static void Set(uint player, byte mode, IReadOnlyDictionary<uint, bool> choices, ISet<uint> validMaps)
    {
        if (player == 0 || mode > Casual5v5 || choices.Count > MaximumChoices) return;
        var filtered = choices.Where(p => validMaps.Contains(p.Key)).ToDictionary();
        lock (Gate) Players[(player, mode)] = filtered;
    }

    internal static IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, bool>> Snapshot(IEnumerable<uint> roster, GameRankingType ranking, int playersPerTeam = 4)
    {
        byte? mode = ranking switch { GameRankingType.Friendly or GameRankingType.Graveyard => playersPerTeam == 5 ? Casual5v5 : Casual4v4,
            GameRankingType.Ranked => Ranked, _ => null };
        var result = new Dictionary<uint, IReadOnlyDictionary<uint, bool>>();
        if (mode == null) return result;
        lock (Gate)
            foreach (uint player in roster.Distinct())
                if (Players.TryGetValue((player, mode.Value), out var choices)) result[player] = new Dictionary<uint, bool>(choices);
        return result;
    }
}
