using System.Collections.Concurrent;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Logging;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;

namespace BNLReloadedServer.Database;

// Replay viewing. A replay is addressed by its replay id (ReplayStore.ReplayIdFor),
// which the client derives from a match id, and is entered through the same
// spectate check/request the PLAY browser uses for live matches.
public partial class RegionServerDatabase
{
    private static readonly ConcurrentDictionary<uint, long> ReplayClockOffsets = new();
    private ReplayHost? _replayHost;

    private sealed class ReplayHost(RegionServerDatabase region, Servers.AsyncTaskTcpServer matchServer) : IReplayHost
    {
        public void UpdateScene(uint playerId, Scene scene) => region.UpdateScene(playerId, scene);
        public void SetReplayClock(uint playerId, long? offsetMs) => region.SetReplayClock(playerId, offsetMs);
        public void EndReplay(uint playerId, string instanceId) => region.EndReplay(playerId, instanceId);
        public void SendToSession(Guid session, byte[] framed) =>
            matchServer.FindAsyncSenderTask(session)?.SendPacket(framed);
    }

    // Milliseconds added to this player's clock-sync replies while they watch a replay.
    public static long ReplayClockOffset(uint playerId) => ReplayClockOffsets.GetValueOrDefault(playerId);

    public CustomGameSpectateResult CheckReplay(uint playerId, ulong replayId)
    {
        if (!UserConnected(playerId, out _)) return CustomGameSpectateResult.NoSuchGame;
        return ReplayStore.TryResolve(replayId, out _, out _)
            ? CustomGameSpectateResult.Accepted
            : CustomGameSpectateResult.NoSuchGame;
    }

    public bool WatchReplay(uint playerId, ulong replayId)
    {
        if (!UserConnected(playerId, out var info) || info.GameInstanceId != null ||
            !ReplayStore.TryResolve(replayId, out var path, out var matchId)) return false;

        ReplayInstance instance;
        try
        {
            instance = new ReplayInstance(_replayHost ??= new ReplayHost(this, matchServer), path, matchId, playerId);
        }
        catch (Exception exception)
        {
            Log.Warn(LogCat.Match, $"Replay {matchId} could not be opened: {exception.Message}");
            return false;
        }

        _gameInstances[instance.GameInstanceId] = instance;
        info.CustomGameId = null;
        info.GameInstanceId = instance.GameInstanceId;
        Log.Info(LogCat.Match, $"Replay playback started: {matchId} for {playerId} ({instance.GameInstanceId})");
        instance.SendUserToZone(playerId);
        return true;
    }

    internal void SetReplayClock(uint playerId, long? offsetMs)
    {
        if (offsetMs is { } offset) ReplayClockOffsets[playerId] = offset;
        else if (!ReplayClockOffsets.TryRemove(playerId, out _)) return;
        if (UserConnected(playerId, out var info) &&
            GetService<IServiceTime>(info.Guid, ServiceId.ServiceTime, out var timeService))
        {
            timeService.SendSetOrigin(DateTimeOffset.Now.ToUnixTimeMilliseconds() + (offsetMs ?? 0));
        }
    }

    internal void EndReplay(uint playerId, string instanceId)
    {
        SetReplayClock(playerId, null);
        RemoveFromGameInstance(playerId, instanceId);
        RemoveGameInstance(instanceId);
    }
}
