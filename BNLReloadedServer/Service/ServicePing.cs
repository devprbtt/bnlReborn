using BNLReloadedServer.Database;
using System.Diagnostics;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Service;

public class ServicePing(ISender sender) : IServicePing
{
    private enum ServicePingId : byte
    {
        MessageServerPing = 0,
        MessageServerPong = 1,
        MessageClientPing = 2,
        MessageClientPong = 3
    }

    private static BinaryWriter CreateWriter()
    {
        var memStream = new MemoryStream();
        var writer = new BinaryWriter(memStream);
        writer.Write((byte)ServiceId.ServicePing);
        return writer;
    }

    private int _missedProbes;
    private readonly Queue<long> _probeTimestamps = new();
    private readonly object _probeLock = new();
    private int _roundTripMilliseconds = -1;

    public int RoundTripMilliseconds => Volatile.Read(ref _roundTripMilliseconds);

    public void SendServerPing()
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServicePingId.MessageServerPing);
        sender.Send(writer);
    }

    public int SendLivenessProbe()
    {
        var missed = Interlocked.Increment(ref _missedProbes);
        lock (_probeLock)
        {
            _probeTimestamps.Enqueue(Stopwatch.GetTimestamp());
        }
        SendServerPing();
        return missed;
    }

    private void ReceiveServerPong(BinaryReader reader)
    {
        Interlocked.Exchange(ref _missedProbes, 0);
        long started;
        lock (_probeLock)
        {
            if (!_probeTimestamps.TryDequeue(out started)) return;
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Volatile.Write(ref _roundTripMilliseconds, (int)Math.Clamp(Math.Round(elapsed), 0, ushort.MaxValue - 1));
    }

    private void ReceiveClientPing(BinaryReader reader)
    {
        SendClientPong();
    }

    public void SendClientPong()
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServicePingId.MessageClientPong);
        sender.Send(writer);
    }

    public bool Receive(BinaryReader reader)
    {
        var servicePingId = reader.ReadByte();
        ServicePingId? pingEnum = null;
        if (Enum.IsDefined(typeof(ServicePingId), servicePingId))
        {
            pingEnum = (ServicePingId)servicePingId;
        }

        Log.Debug(LogCat.Net, $"ServicePingId: {Log.EnumName(pingEnum, servicePingId)}");

        switch (pingEnum)
        {
            case ServicePingId.MessageServerPong:
                ReceiveServerPong(reader);
                break;
            case ServicePingId.MessageClientPing:
                ReceiveClientPing(reader);
                break;
            default:
                Log.Warn(LogCat.Net, $"Unknown service ping id {Log.EnumName(pingEnum, servicePingId)}");
                return false;
        }

        return true;
    }
}
