using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
using NetCoreServer;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Servers;

internal static class SessionLiveness
{
    private const double MinimumAuthenticatedSilenceSeconds = 60;

    public static CancellationTokenSource? Start(IServicePing ping, TcpSession session, string label,
        SessionSender sender, SessionReader reader, Func<string?> peer)
    {
        var interval = Databases.ConfigDatabase.PingIntervalSeconds();
        var configuredMaxMissed = Databases.ConfigDatabase.MaxMissedPings();
        if (interval <= 0 || configuredMaxMissed <= 0) return null;

        // Existing production configs predate the longer loading grace and contain an
        // explicit value of 11, so a property initializer alone cannot migrate them.
        var minimumMaxMissed = (int)Math.Ceiling(MinimumAuthenticatedSilenceSeconds / interval);
        var maxMissed = Math.Max(configuredMaxMissed, minimumMaxMissed);

        var cts = new CancellationTokenSource();
        _ = Run(ping, session, label, sender, reader, peer, TimeSpan.FromSeconds(interval), maxMissed, cts.Token);
        return cts;
    }

    private static async Task Run(IServicePing ping, TcpSession session, string label, SessionSender sender,
        SessionReader reader, Func<string?> peer, TimeSpan interval, int maxMissed, CancellationToken token)
    {
        // These lines say a session is being hung up on, so they name it the same way the connect
        // line did. Resolved per line rather than captured: a session torn down early has no
        // address left, and the next line may still find one.
        string Describe() => peer() is { } address
            ? $"{address} ({Log.ShortId(session.Id)})"
            : Log.ShortId(session.Id);

        // The same budget either way: a session gets this long to prove it is still there, whether
        // it does so by answering probes or by saying anything at all.
        var silenceBudget = interval * maxMissed;

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(token))
            {
                if (sender.AssociatedPlayerId == null)
                {
                    if (reader.SinceLastPacket <= silenceBudget) continue;

                    Log.Warn(LogCat.Conn, $"{label} session {Describe()} sent nothing for " +
                                          $"{silenceBudget.TotalSeconds:0}s without logging in, disconnecting it");
                    session.Disconnect();
                    return;
                }

                var missedProbes = ping.SendLivenessProbe();
                if (missedProbes <= maxMissed) continue;

                // A pong is the normal acknowledgement, but any received packet proves the
                // connection is alive. Do not disconnect an active session merely because its
                // dedicated pong was delayed behind other client work.
                var silence = reader.SinceLastPacket;
                if (silence <= silenceBudget) continue;

                Log.Warn(LogCat.Conn, $"{label} session {Describe()} missed {maxMissed} pings and sent nothing " +
                                          $"for {silence.TotalSeconds:0}s, disconnecting it");
                session.Disconnect();
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Error(LogCat.Conn, $"{label} session {Describe()} liveness probe failed", e);
        }
    }
}
