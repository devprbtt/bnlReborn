using System.Numerics;
using System.Globalization;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
namespace BNLReloadedServer.Service;

public partial class ServiceZone
{
    public void SendAdminSpectator(bool active)
    {
        if(!SupportsAdminSpectator)return;
        using var writer=CreateWriter();writer.Write((byte)120);writer.Write(active);sender.Send(writer);
    }
    private static readonly string[] FunnyActions = ["on","off","grab","aim","charge","throw","drop","beam_charge","beam_fire","beam_cancel","fireball","well_on","well_off","repulse_charge","repulse_fire","gravity_cancel","invis_toggle"];
    private void ReceiveAdminFunny(BinaryReader reader)
    {
        // SessionReader shares its receive stream across coalesced TCP frames. Remaining
        // bytes can include the next frame; consume only this request's fixed-size body.
        if (reader.BaseStream.Length-reader.BaseStream.Position < 21) return;
        var action=reader.ReadByte(); var target=reader.ReadUInt32(); var distance=reader.ReadSingle();
        var direction=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
        if (!SupportsAdminFunnyMode || action>=FunnyActions.Length || (action >= 7 && !SupportsAdminBeam) || (action>=11 && !SupportsAdminGravity) || (action>=16 && !SupportsAdminInvisibility) || !sender.AssociatedPlayerId.HasValue ||
            !AdminFunnyRules.IsAdmin(Databases.PlayerDatabase.GetPlayerDataNoWait(sender.AssociatedPlayerId.Value)?.Role)) return;
        GameInstance?.DebugZoneCommand(sender.AssociatedPlayerId.Value,"admin_funny",
            [FunnyActions[action],target.ToString(CultureInfo.InvariantCulture),distance.ToString("R",CultureInfo.InvariantCulture),
             direction.X.ToString("R",CultureInfo.InvariantCulture),direction.Y.ToString("R",CultureInfo.InvariantCulture),direction.Z.ToString("R",CultureInfo.InvariantCulture)]);
    }
    // Capability 16: observer VFX only. Origin, endpoint, timing and strength are server-owned.
    public void SendAdminBeam(uint admin, byte phase, long time, Vector3 origin, Vector3 end, float charge)
    {
        if (!SupportsAdminBeam) return;
        using var writer = CreateWriter(); writer.Write((byte)117);
        writer.Write(admin); writer.Write(phase); writer.Write(time);
        writer.Write(origin.X); writer.Write(origin.Y); writer.Write(origin.Z);
        writer.Write(end.X); writer.Write(end.Y); writer.Write(end.Z); writer.Write(charge);
        sender.Send(writer);
    }
    public void SendAdminFireball(uint id, uint admin, byte phase, Vector3 position, Vector3 velocity)
    {
        if (!SupportsAdminBeam) return;
        using var writer=CreateWriter(); writer.Write((byte)118); writer.Write(id); writer.Write(admin); writer.Write(phase);
        writer.Write(position.X); writer.Write(position.Y); writer.Write(position.Z);
        writer.Write(velocity.X); writer.Write(velocity.Y); writer.Write(velocity.Z); sender.Send(writer);
    }
    public void SendAdminGravity(uint admin, byte phase, long time, Vector3 origin, float radius, float strength)
    {
        if(!SupportsAdminGravity) return;
        using var writer=CreateWriter();writer.Write((byte)119);writer.Write(admin);writer.Write(phase);writer.Write(time);
        writer.Write(origin.X);writer.Write(origin.Y);writer.Write(origin.Z);writer.Write(radius);writer.Write(strength);sender.Send(writer);
    }
    // Capability 15; never sent to older clients. 115 request, 116 state.
    public void SendAdminFunny(uint admin, uint target, bool enabled, byte action,
        Vector3 position, Vector3 velocity, float charge)
    {
        if (!SupportsAdminFunnyMode) return;
        using var writer = CreateWriter();
        writer.Write((byte)116);
        writer.Write(admin); writer.Write(target); writer.Write(enabled); writer.Write(action);
        writer.Write(position.X); writer.Write(position.Y); writer.Write(position.Z);
        writer.Write(velocity.X); writer.Write(velocity.Y); writer.Write(velocity.Z);
        writer.Write(charge);
        sender.Send(writer);
    }
}
