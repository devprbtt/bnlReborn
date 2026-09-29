using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public class ToolChannel : Tool
{
    public override ToolType Type => ToolType.Channel;

    public float Range { get; set; }

    public List<InstEffect>? IntervalEffects { get; set; }

    public float Interval { get; set; }

    public List<Key>? ConstantEffects { get; set; }

    // Reborn, server-only: ammo taken per pulse while the channel is on a block or
    // a non-player unit (device). Read from the catalogue as block_ammo_rate and
    // never written to clients, whose ToolChannel record has no such field. Null
    // falls back to Ammo.Rate, which stays the drain while channelling a player.
    public float? BlockAmmoRate { get; set; }

    public float? AmmoRateFor(bool targetIsPlayer) =>
        Ammo == null ? null : targetIsPlayer ? Ammo.Rate : BlockAmmoRate ?? Ammo.Rate;

    public override void Write(BinaryWriter writer)
    {
        new BitField(Ammo != null, true, true, IntervalEffects != null, true, ConstantEffects != null).Write(writer);
        if (Ammo != null)
            ToolAmmo.WriteRecord(writer, Ammo);
        writer.Write(AutoSwitch);
        writer.Write(Range);
        if (IntervalEffects != null)
            writer.WriteList(IntervalEffects, InstEffect.WriteVariant);
        writer.Write(Interval);
        if (ConstantEffects != null)
            writer.WriteList(ConstantEffects, Key.WriteRecord);
    }

    public override void Read(BinaryReader reader)
    {
        var bitField = new BitField(6);
        bitField.Read(reader);
        Ammo = bitField[0] ? ToolAmmo.ReadRecord(reader) : null;
        if (bitField[1])
            AutoSwitch = reader.ReadBoolean();
        if (bitField[2])
            Range = reader.ReadSingle();
        IntervalEffects = bitField[3] ? reader.ReadList<InstEffect, List<InstEffect>>(InstEffect.ReadVariant) : null;
        if (bitField[4])
            Interval = reader.ReadSingle();
        ConstantEffects = bitField[5] ? reader.ReadList<Key, List<Key>>(Key.ReadRecord) : null;
    }

    public static void WriteRecord(BinaryWriter writer, ToolChannel value) => value.Write(writer);

    public static ToolChannel ReadRecord(BinaryReader reader)
    {
        var toolChannel = new ToolChannel();
        toolChannel.Read(reader);
        return toolChannel;
    }
}
