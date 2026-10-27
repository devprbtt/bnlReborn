using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public class ZoneUpdate
{
    public string? ActionScoresJson { get; set; }
    // {"playerId": "skin_weapon_*"} for every player wearing a weapon skin. Last field: clients that predate it
    // read their 13 bits and stop, leaving this string unread in the message buffer.
    public string? WeaponSkinsJson { get; set; }
    public long? WeatherStartTime { get; set; }
    public string? ConquestStateJson { get; set; }
    public string? BlockOwnersJson { get; set; }

    public ZonePhase? Phase { get; set; }

    public MatchStats? Statistics { get; set; }

    public List<SpawnPoint>? SpawnPoints { get; set; }

    public Dictionary<uint, uint?>? PlayerSpawnPoints { get; set; }

    public Dictionary<uint, ulong>? RespawnInfo { get; set; }

    public Dictionary<uint, ZonePlayerInfo>? PlayerInfo { get; set; }

    public SupplyInfo? SupplyInfo { get; set; }

    public List<ZoneObjective>? Objectives { get; set; }

    public float? ResourceCap { get; set; }

    public void Write(BinaryWriter writer)
    {
        new BitField(Phase != null, Statistics != null, SpawnPoints != null, PlayerSpawnPoints != null,
          RespawnInfo != null, PlayerInfo != null, SupplyInfo != null, Objectives != null,
          ResourceCap.HasValue, ConquestStateJson != null, BlockOwnersJson != null, WeatherStartTime.HasValue, ActionScoresJson != null, WeaponSkinsJson != null).Write(writer);
        if (Phase != null)
            ZonePhase.WriteRecord(writer, Phase);
        if (Statistics != null)
            MatchStats.WriteRecord(writer, Statistics);
        if (SpawnPoints != null)
            writer.WriteList(SpawnPoints, SpawnPoint.WriteRecord);
        if (PlayerSpawnPoints != null)
            writer.WriteMap(PlayerSpawnPoints, writer.Write, item => writer.WriteOptionValue(item, writer.Write));
        if (RespawnInfo != null)
            writer.WriteMap(RespawnInfo, writer.Write, writer.Write);
        if (PlayerInfo != null)
            writer.WriteMap(PlayerInfo, writer.Write, ZonePlayerInfo.WriteRecord);
        if (SupplyInfo != null)
            SupplyInfo.WriteRecord(writer, SupplyInfo);
        if (Objectives != null)
            writer.WriteList(Objectives, ZoneObjective.WriteRecord);
        if (ResourceCap.HasValue) writer.Write(ResourceCap.Value);
        if (ConquestStateJson != null) writer.Write(ConquestStateJson);
        if (BlockOwnersJson != null) writer.Write(BlockOwnersJson);
        if (WeatherStartTime.HasValue) writer.Write(WeatherStartTime.Value);
        if (ActionScoresJson != null) writer.Write(ActionScoresJson);
        if (WeaponSkinsJson != null) writer.Write(WeaponSkinsJson);
    }

    public void Read(BinaryReader reader)
    {
        var bitField = new BitField(14);
        bitField.Read(reader);
        Phase = bitField[0] ? ZonePhase.ReadRecord(reader) : null;
        Statistics = bitField[1] ? MatchStats.ReadRecord(reader) : null;
        SpawnPoints = bitField[2] ? reader.ReadList<SpawnPoint, List<SpawnPoint>>(SpawnPoint.ReadRecord) : null;
        PlayerSpawnPoints = bitField[3] ? reader.ReadMap<uint, uint?, Dictionary<uint, uint?>>(reader.ReadUInt32, () => reader.ReadOptionValue(reader.ReadUInt32)) : null;
        RespawnInfo = bitField[4] ? reader.ReadMap<uint, ulong, Dictionary<uint, ulong>>(reader.ReadUInt32, reader.ReadUInt64) : null;
        PlayerInfo = bitField[5] ? reader.ReadMap<uint, ZonePlayerInfo, Dictionary<uint, ZonePlayerInfo>>(reader.ReadUInt32, ZonePlayerInfo.ReadRecord) : null;
        SupplyInfo = bitField[6] ? SupplyInfo.ReadRecord(reader) : null;
        Objectives = bitField[7] ? reader.ReadList<ZoneObjective, List<ZoneObjective>>(ZoneObjective.ReadRecord) : null;
        ResourceCap = bitField[8] ? reader.ReadSingle() : null;
        ConquestStateJson = bitField[9] ? reader.ReadString() : null;
        BlockOwnersJson = bitField[10] ? reader.ReadString() : null;
        WeatherStartTime = bitField[11] ? reader.ReadInt64() : null;
        ActionScoresJson = bitField[12] ? reader.ReadString() : null;
        WeaponSkinsJson = bitField[13] ? reader.ReadString() : null;
    }

    public static void WriteRecord(BinaryWriter writer, ZoneUpdate value) => value.Write(writer);

    public static ZoneUpdate ReadRecord(BinaryReader reader)
    {
        var zoneUpdate = new ZoneUpdate();
        zoneUpdate.Read(reader);
        return zoneUpdate;
    }
}
