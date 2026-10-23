using BNLReloadedServer.ProtocolHelpers;
using Moserware.Skills;

namespace BNLReloadedServer.BaseTypes;

public class EndMatchResults
{
    public uint PlayerId { get; set; }
    public string GameInstanceId { get; set; } = string.Empty;
    public ulong MatchEndTime { get; set; }
    public Key MapKey { get; set; }
    public Key GameModeKey { get; set; }
    public EndMatchData MatchData { get; set; } = new();
    /// <summary>XP for heroes the player switched away from during the match (the final hero's is in MatchData).
    /// Server-internal: region to master only.</summary>
    public Dictionary<Key, float> OtherHeroXp { get; set; } = [];

    public void Write(BinaryWriter writer)
    {
        new BitField(true, true, true, true, true, true, true).Write(writer);
        writer.Write(PlayerId);
        writer.Write(GameInstanceId);
        writer.Write(MatchEndTime);
        Key.WriteRecord(writer, MapKey);
        Key.WriteRecord(writer, GameModeKey);
        EndMatchData.WriteRecord(writer, MatchData);
        writer.Write(OtherHeroXp.Count);
        foreach (var (hero, xp) in OtherHeroXp) { Key.WriteRecord(writer, hero); writer.Write(xp); }
    }

    public void Read(BinaryReader reader)
    {
        var bitField = new BitField(7);
        bitField.Read(reader);
        if (bitField[0])
        {
            PlayerId = reader.ReadUInt32();
        }

        if (bitField[1])
        {
            GameInstanceId = reader.ReadString();
        }

        if (bitField[2])
        {
            MatchEndTime = reader.ReadUInt64();
        }

        if (bitField[3])
        {
            MapKey = Key.ReadRecord(reader);
        }

        if (bitField[4])
        {
            GameModeKey = Key.ReadRecord(reader);
        }

        if (bitField[5])
        {
            MatchData = EndMatchData.ReadRecord(reader);
        }

        if (bitField[6])
        {
            var count = reader.ReadInt32();
            if (count is < 0 or > 256) throw new InvalidDataException("Bad hero XP count");
            OtherHeroXp = new Dictionary<Key, float>(count);
            for (var i = 0; i < count; i++) OtherHeroXp[Key.ReadRecord(reader)] = reader.ReadSingle();
        }
    }

    public static void WriteRecord(BinaryWriter writer, EndMatchResults value) => value.Write(writer);

    public static EndMatchResults ReadRecord(BinaryReader reader)
    {
        var endMatchResults = new EndMatchResults();
        endMatchResults.Read(reader);
        return endMatchResults;
    }
}
