using System.Numerics;
using System.Globalization;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
namespace BNLReloadedServer.Service;

public partial class ServiceZone
{
    private static readonly string[] FunnyActions = ["on","off","grab","aim","charge","throw","drop"];
    private void ReceiveAdminFunny(BinaryReader reader)
    {
        if (reader.BaseStream.Length-reader.BaseStream.Position != 21) return;
        var action=reader.ReadByte(); var target=reader.ReadUInt32(); var distance=reader.ReadSingle();
        var direction=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
        if (!SupportsAdminFunnyMode || action>=FunnyActions.Length || !sender.AssociatedPlayerId.HasValue ||
            !AdminFunnyRules.IsAdmin(Databases.PlayerDatabase.GetPlayerDataNoWait(sender.AssociatedPlayerId.Value)?.Role)) return;
        GameInstance?.DebugZoneCommand(sender.AssociatedPlayerId.Value,"admin_funny",
            [FunnyActions[action],target.ToString(CultureInfo.InvariantCulture),distance.ToString("R",CultureInfo.InvariantCulture),
             direction.X.ToString("R",CultureInfo.InvariantCulture),direction.Y.ToString("R",CultureInfo.InvariantCulture),direction.Z.ToString("R",CultureInfo.InvariantCulture)]);
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
