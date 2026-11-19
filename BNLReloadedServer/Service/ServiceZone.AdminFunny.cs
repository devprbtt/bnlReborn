using System.Numerics;
using System.Globalization;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
namespace BNLReloadedServer.Service;

public partial class ServiceZone
{
    private static readonly string[] FunnyActions = ["on","off","grab","aim","charge","throw","drop","beam_charge","beam_fire","beam_cancel","fireball"];
    private void ReceiveAdminFunny(BinaryReader reader)
    {
        if (reader.BaseStream.Length-reader.BaseStream.Position != 21) return;
        var action=reader.ReadByte(); var target=reader.ReadUInt32(); var distance=reader.ReadSingle();
        var direction=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
        if (!SupportsAdminFunnyMode || action>=FunnyActions.Length || (action >= 7 && !SupportsAdminBeam) || !sender.AssociatedPlayerId.HasValue ||
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
