using System.Diagnostics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Logging;
using BNLReloadedServer.Servers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using Moserware.Skills;

namespace BNLReloadedServer.Database;

// What a replay needs from the region: scene changes, the viewer's clock
// offset, cleanup, and raw sends on the viewer's match session.
public interface IReplayHost
{
    void UpdateScene(uint playerId, Scene scene);
    void SetReplayClock(uint playerId, long? offsetMs);
    void EndReplay(uint playerId, string instanceId);
    void SendToSession(Guid session, byte[] framed);
}

// Plays one recorded match to one viewer through the normal spectator join:
// SendUserToZone puts the viewer in a spectator zone scene, PlayerEnterScene
// sends the recorded InitZone, PlayerZoneReady sends the recorded join snapshot
// and then streams the recorded packets on their original timing. The viewer's
// clock is offset to the recording's server time so absolute timestamps in the
// packets (reloads, cooldowns, respawns, look-stream timing) stay valid.
public sealed class ReplayInstance : GameInstanceBase
{
    private static readonly TimeSpan EndHold = TimeSpan.FromSeconds(6);

    private readonly IReplayHost _host;
    private readonly string _path;
    private readonly uint _viewerId;
    private readonly Key _gameMode;
    private readonly Key _matchKey;
    private readonly long _recordedServerTime;
    private readonly CancellationTokenSource _stop = new();
    private Guid? _viewerSession;
    private ReplayReader? _reader;
    private ReplayRecord? _initZone;
    private ReplayRecord? _load;
    private int _streaming;
    private bool _initZonePending;
    private int _ended;

    public string GameInstanceId { get; }
    public string MatchId { get; }

    public ReplayInstance(IReplayHost host, string path, string matchId, uint viewerId)
    {
        _host = host;
        _path = path;
        _viewerId = viewerId;
        MatchId = matchId;
        GameInstanceId = "replay-" + Guid.NewGuid().ToString("N");
        _reader = new ReplayReader(path);
        var metadata = _reader.Metadata.RootElement;
        _gameMode = Key.FromHash(metadata.GetProperty("mode_key").GetUInt32());
        _matchKey = Key.FromHash(metadata.GetProperty("match_key").GetUInt32());
        _recordedServerTime = metadata.GetProperty("server_time_at_start").GetInt64();
        _initZone = _reader.Next();
        _load = _reader.Next();
        if (_initZone?.Kind != MatchRecorder.KindInitZone || _load?.Kind != MatchRecorder.KindLoad)
            throw new InvalidDataException("Replay has no keyframe.");
    }

    public override bool IsStarted => true;
    public override bool IsOver() => Volatile.Read(ref _ended) != 0;
    public override Key GetGameMode() => _gameMode;
    public override bool IsPlayerSpectator(uint playerId) => true;

    public override (Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2) GetTeamRatings() =>
        (new Dictionary<uint, Rating>(), new Dictionary<uint, Rating>());

    public override void SendUserToZone(uint playerId)
    {
        if (playerId != _viewerId) return;
        _host.UpdateScene(playerId, new SceneZone
        {
            GameMode = _gameMode,
            MatchKey = _matchKey,
            MyTeam = TeamType.Neutral,
            IsSpectator = true,
            IsMapEditor = false,
            Restart = false
        });
    }

    public override void LinkGuidToPlayer(uint userId, Guid guid, Guid regionGuid)
    {
        if (userId != _viewerId) return;
        _viewerSession = guid;
        // The scene-entered report can arrive before the match connection links.
        if (_initZonePending && _initZone != null)
        {
            _initZonePending = false;
            Send(_initZone.Data);
        }
    }

    public override void PlayerEnterScene(uint playerId)
    {
        if (playerId != _viewerId || _initZone == null) return;
        if (_viewerSession == null)
        {
            _initZonePending = true;
            return;
        }
        Send(_initZone.Data);
    }

    public override void PlayerZoneReady(uint playerId)
    {
        if (playerId != _viewerId || _load == null || Interlocked.Exchange(ref _streaming, 1) != 0) return;
        // From here the viewer's clock reads the recording's server time.
        _host.SetReplayClock(_viewerId, _recordedServerTime - DateTimeOffset.Now.ToUnixTimeMilliseconds());
        Send(_load.Data);
        _ = Task.Run(StreamAsync);
    }

    private async Task StreamAsync()
    {
        var clock = Stopwatch.StartNew();
        var reader = _reader;
        long records = 0;
        try
        {
            while (!_stop.IsCancellationRequested && reader?.Next() is { } record)
            {
                var wait = record.OffsetMs - clock.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), _stop.Token);
                if (record.Kind == MatchRecorder.KindEnd) break;
                if (record.Kind != MatchRecorder.KindStream) continue;
                Send(record.Data);
                records++;
            }
            if (!_stop.IsCancellationRequested)
            {
                Log.Info(LogCat.Match, $"Replay playback finished: {MatchId} for {_viewerId}, {records} records");
                await Task.Delay(EndHold, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Log.Warn(LogCat.Match, $"Replay playback failed: {MatchId} for {_viewerId}: {exception.Message}");
        }
        End();
    }

    private void Send(byte[] framed)
    {
        if (_viewerSession is { } session) _host.SendToSession(session, framed);
    }

    public override void PlayerLeftInstance(uint userId, KickReason reason)
    {
        if (userId == _viewerId) End();
    }

    public override void PlayerDisconnected(uint userId)
    {
        if (userId == _viewerId) End();
    }

    public override void DetachFromZoneStreams(uint userId)
    {
        if (userId == _viewerId) _stop.Cancel();
    }

    public override void RemoveAllPlayers(Action<uint>? onRemove = null) => End();

    private void End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _stop.Cancel();
        _host.EndReplay(_viewerId, GameInstanceId);
        var reader = Interlocked.Exchange(ref _reader, null);
        // StreamAsync may still be reading; let it observe cancellation first.
        _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => reader?.Dispose());
    }
}
