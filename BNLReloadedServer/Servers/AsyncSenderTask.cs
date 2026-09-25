using NetCoreServer;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Servers;

public class AsyncSenderTask
{
    private readonly TcpSession _session;
    private int _stopped;

    public Guid Id { get; }

    public AsyncSenderTask(TcpSession session)
    {
        _session = session;
        Id = session.Id;
    }

    public void SendPacket(byte[] packet)
    {
        if (Volatile.Read(ref _stopped) != 0) return;

        try
        {
            // NetCoreServer's asynchronous send path owns a per-session lock and ordered buffer.
            // Queue into it directly from the caller. The previous extra Channel required a
            // ThreadPool continuation before even a CheckVersion reply could reach the socket;
            // under transient worker starvation that left new instance sessions connected but
            // silent until clients timed out and retried.
            _session.SendAsync(packet);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (_session.IsConnected)
                Log.Error(LogCat.Net, $"Failed to queue packet on session {_session.Id}", e);
        }
    }

    public void Stop() => Interlocked.Exchange(ref _stopped, 1);
}
