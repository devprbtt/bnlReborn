using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

/// <summary>
/// Fires when the owner's own weapon directly hits an enemy player, running <see cref="Effect"/> on that
/// player with the owner as source; the on-hit counterpart of on_kill. Heal Bane is one: its perk names
/// the debuff it applies, so strength and duration are edited on cards like every other perk.
/// Server-only: clients receive it as an inert buff (see <see cref="ClientView"/>).
/// </summary>
public class ConstEffectOnHit : ConstEffect
{
    public override ConstEffectType Type => ConstEffectType.OnHit;

    public InstEffect? Effect { get; set; }

    public override ConstEffect ClientView() => new ConstEffectBuff { Targeting = Targeting, Buffs = [] };

    public override void Write(BinaryWriter writer)
    {
        new BitField(Targeting != null, Effect != null).Write(writer);
        if (Targeting != null)
            EffectTargeting.WriteRecord(writer, Targeting);
        if (Effect != null)
            InstEffect.WriteVariant(writer, Effect);
    }

    public override void Read(BinaryReader reader)
    {
        var bitField = new BitField(2);
        bitField.Read(reader);
        Targeting = bitField[0] ? EffectTargeting.ReadRecord(reader) : null;
        Effect = bitField[1] ? InstEffect.ReadVariant(reader) : null;
    }
}
