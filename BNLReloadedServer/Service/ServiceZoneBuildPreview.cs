using System.IO;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.Service;

public partial class ServiceZone
{
    public bool SupportsBuildPreview { get; private set; }
    private void ReceiveBuildPreview(BinaryReader reader)
    {
        var generation=reader.ReadUInt32();
        var info=BuildInfo.ReadRecord(reader);
        if(SupportsBuildPreview && sender.AssociatedPlayerId is { } playerId && GameInstance is GameInstance instance)
            instance.BuildPreview(playerId,generation,info);
    }
    public void SendBuildPreview(uint unitId,uint generation,bool initial,BuildInfo info)
    {
        if(!SupportsBuildPreview) return;
        using var writer=CreateWriter();
        writer.Write((byte)104); writer.Write(unitId); writer.Write(generation); writer.Write(initial);
        BuildInfo.WriteRecord(writer,info); sender.Send(writer);
    }
}
