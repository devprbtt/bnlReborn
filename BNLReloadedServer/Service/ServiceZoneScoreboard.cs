using System.Diagnostics;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.Service;

public partial class ServiceZone
{
    public bool SupportsScoreboardMetadata { get; private set; }

    // Only the coarse ISO country code crosses the game protocol. The address remains on the server.
    public string CountryCode => IpCountryLookup.Resolve(peerAddress?.Invoke()) ?? string.Empty;

    private long _lastScoreboardMetadataRequest;

    private void ReceiveScoreboardMetadata()
    {
        if (!SupportsScoreboardMetadata || !sender.AssociatedPlayerId.HasValue) return;

        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastScoreboardMetadataRequest);
        if (previous != 0 && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(500)) return;

        Interlocked.Exchange(ref _lastScoreboardMetadataRequest, now);
        GameInstance?.ScoreboardMetadata(sender.AssociatedPlayerId.Value, this);
    }

    public void SendScoreboardMetadata(IReadOnlyList<ScoreboardPlayerNetworkInfo> players)
    {
        if (!SupportsScoreboardMetadata) return;

        using var writer = CreateWriter();
        writer.Write((byte)ServiceZoneId.MessageScoreboardMetadata);
        writer.Write((ushort)Math.Min(players.Count, ushort.MaxValue));
        foreach (var player in players.Take(ushort.MaxValue))
        {
            writer.Write(player.PlayerId);
            writer.Write((ushort)(player.PingMilliseconds < 0
                ? ushort.MaxValue
                : Math.Min(player.PingMilliseconds, ushort.MaxValue - 1)));
            writer.Write(player.CountryCode);
        }
        sender.Send(writer);
    }
}
