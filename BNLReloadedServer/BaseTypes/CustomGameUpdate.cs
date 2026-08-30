using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public class CustomGameUpdate
{
    public string? GameName { get; set; }

    public string? Password { get; set; }

    public CustomGameSettings? Settings { get; set; }

    public List<CustomGamePlayer>? Players { get; set; }

    public CustomGameStatus? Status { get; set; }

    public bool? ForceThirdPerson { get; set; }

    public void Write(BinaryWriter writer)
    {
        new BitField(GameName != null, Password != null, Settings != null, Players != null, Status.HasValue,
            ForceThirdPerson.HasValue, ForceThirdPerson.GetValueOrDefault()).Write(writer);
        if (GameName != null)
            writer.Write(GameName);
        if (Password != null)
            writer.Write(Password);
        if (Settings != null)
            CustomGameSettings.WriteRecord(writer, Settings);
        if (Players != null)
            writer.WriteList(Players, CustomGamePlayer.WriteRecord);
        if (!Status.HasValue)
            return;
        writer.WriteByteEnum(Status.Value);
    }

    public void Read(BinaryReader reader)
    {
        var bitField = new BitField(7);
        bitField.Read(reader);
        GameName = bitField[0] ? reader.ReadString() : null;
        Password = bitField[1] ? reader.ReadString() : null;
        Settings = bitField[2] ? CustomGameSettings.ReadRecord(reader) : null;
        Players = bitField[3] ? reader.ReadList<CustomGamePlayer, List<CustomGamePlayer>>(CustomGamePlayer.ReadRecord) : null;
        Status = bitField[4] ? reader.ReadByteEnum<CustomGameStatus>() : null;
        ForceThirdPerson = bitField[5] ? bitField[6] : null;
    }

    public static void WriteRecord(BinaryWriter writer, CustomGameUpdate value)
    {
        value.Write(writer);
    }

    public static CustomGameUpdate ReadRecord(BinaryReader reader)
    {
        var customGameUpdate = new CustomGameUpdate();
        customGameUpdate.Read(reader);
        return customGameUpdate;
    }
}
