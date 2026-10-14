using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Service;

public class ServiceTime(ISender sender) : IServiceTime
{
    private enum ServiceTimeId : byte
    {
        MessageSetOrigin = 0,
        MessageSync = 1
    }

    private static BinaryWriter CreateWriter()
    {
        var memStream = new MemoryStream();
        var writer = new BinaryWriter(memStream);
        writer.Write((byte)ServiceId.ServiceTime);
        return writer;
    }

    public void SendSetOrigin(long time)
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServiceTimeId.MessageSetOrigin);
        writer.Write(time);
        sender.Send(writer);
    }

    public void SendSync(ushort rpcId, long? time, string? error = null)
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServiceTimeId.MessageSync);
        writer.Write(rpcId);
        if (time != null)
        {
            writer.Write((byte)0);
            writer.Write(time.Value);
        }
        else
        {
            writer.Write(byte.MaxValue);
            writer.Write(error!);
        }
        sender.Send(writer);
    }

    private void ReceiveSync(BinaryReader reader)
    {
        var rpcId = reader.ReadUInt16();
        // A replay viewer's clock follows the recording's server time.
        var offset = sender.AssociatedPlayerId is { } playerId
            ? BNLReloadedServer.Database.RegionServerDatabase.ReplayClockOffset(playerId)
            : 0;
        SendSync(rpcId, DateTimeOffset.Now.ToUnixTimeMilliseconds() + offset);
    }

    public bool Receive(BinaryReader reader)
    {
        var serviceTimeId = reader.ReadByte();
        ServiceTimeId? timeEnum = null;
        if (Enum.IsDefined(typeof(ServiceTimeId), serviceTimeId))
        {
            timeEnum = (ServiceTimeId)serviceTimeId;
        }

        Log.Debug(LogCat.Net, $"ServiceTimeId: {Log.EnumName(timeEnum, serviceTimeId)}");

        switch (timeEnum)
        {
            case ServiceTimeId.MessageSync:
                ReceiveSync(reader);
                break;
            default:
                Log.Warn(LogCat.Net, $"Time service received unsupported serviceId: {Log.EnumName(timeEnum, serviceTimeId)}");
                return false;
        }

        return true;
    }
}
