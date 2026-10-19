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
    private readonly object _gate = new();
    private readonly ReplayTimeline _timeline;
    private CancellationTokenSource? _playback;
    private readonly Stopwatch _clock = new();
    private long _position;
    private long _seekTarget;
    private bool _paused;
    private bool _seeking;
    private bool _controls;
    private bool _acknowledgedSeeking;
    private int _barrierSequence;
    private readonly List<(int Id, TaskCompletionSource<bool> Completion)> _barriers = [];
    private bool _presentationReady;
    private readonly int _seekWindow;

    private int _generation;
    private ReplayRecord? _initZone;
    private ReplayRecord? _load;
    private int _streaming;
    private bool _initZonePending;
    private int _ended;

    public string GameInstanceId { get; }
    public string MatchId { get; }

    public ReplayInstance(IReplayHost host, string path, string matchId, uint viewerId, int seekWindow = 8)
    {
        _host = host;
        _seekWindow = Math.Clamp(seekWindow, 1, 8);
        _path = path;
        _viewerId = viewerId;
        MatchId = matchId;
        GameInstanceId = "replay-" + Guid.NewGuid().ToString("N");
        using var reader = new ReplayReader(path);
        _timeline = new ReplayTimeline(path);
        var metadata = reader.Metadata.RootElement;
        _gameMode = Key.FromHash(metadata.GetProperty("mode_key").GetUInt32());
        _matchKey = Key.FromHash(metadata.GetProperty("match_key").GetUInt32());
        _recordedServerTime = metadata.GetProperty("server_time_at_start").GetInt64();
        _initZone = reader.Next();
        _load = reader.Next();
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
            Restart = _seeking
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

    public void EnableControls(uint playerId, bool acknowledgedSeeking = false, bool presentationReady = false)
    {
        if (playerId == _viewerId) { _controls = true; _acknowledgedSeeking = acknowledgedSeeking; _presentationReady = presentationReady; }
    }

    private long Position => Math.Min(_timeline.DurationMs, _position + _clock.ElapsedMilliseconds);

    // Commands are accepted only on this viewer's replay instance, never a live game.
    // 0=query, 1=pause, 2=resume, 3=relative seek in ms, 4=skip current build, 5=catch-up ACK.
    public void Control(uint playerId, byte command, int argument)
    {
        lock (_gate)
        {
            if (playerId != _viewerId || !_controls || IsOver()) return;
            if (command == 5)
            {
                // ACKs are ordered on the viewer's connection. A valid later ACK
                // confirms all preceding batches too; stale/foreign ids cannot advance.
                if (_seeking && _acknowledgedSeeking)
                {
                    int index = _barriers.FindIndex(b => b.Id == argument);
                    for (int i = 0; i <= index; i++) _barriers[i].Completion.TrySetResult(true);
                }
                return;
            }
            if (_streaming == 0 || _seeking) return;
            if (command == 0) { SendState(); return; }
            if (command is 1 or 2)
            {
                if (command == 2 && Position >= _timeline.DurationMs) { SendState(); return; }
                _position = Position;
                _clock.Reset();
                _paused = command == 1;
                if (!_paused) _clock.Start();
                SetClock();
                SendState();
                return;
            }
            long target;
            if (command == 3) target = Position + Math.Clamp(argument, -60000, 60000);
            else if (command == 4) target = _timeline.NextCombat(Position);
            else return;
            if (target < 0 && command == 4) { SendState(); return; }
            target = Math.Clamp(target, _presentationReady ? _timeline.FirstPlayableOffset : 0, Math.Max(0L, (long)_timeline.DurationMs - 1));
            _position = Position;
            _clock.Reset();
            _seekTarget = target;
            _seeking = true;
            // Forward catch-up can continue from the existing reader/world.
            // Only rewind requires reconstructing from the initial snapshot.
            if (target >= _position)
            {
                SendState();
                return;
            }
            _generation++;
            _playback?.Cancel();
            _barriers.Clear();
            _streaming = 0;
            SendState(); // unfreeze client before its scene reload
            SendUserToZone(_viewerId);
        }
    }

    private void SetClock() => _host.SetReplayClock(_viewerId,
        _recordedServerTime + Position - DateTimeOffset.Now.ToUnixTimeMilliseconds());

    private void SendState()
    {
        if (!_controls) return;
        using var writer = new BinaryWriter(new MemoryStream());
        writer.Write((byte)6);
        writer.Write((byte)111);
        writer.Write(_recordedServerTime);
        writer.Write(Position);
        writer.Write((long)_timeline.DurationMs);
        writer.Write(_paused);
        writer.Write(_seeking);
        writer.Write(_timeline.NextCombat(Position));
        Send(RecordingSender.Frame(writer));
    }

    public override void PlayerZoneReady(uint playerId)
    {
        lock (_gate)
        {
            if (playerId != _viewerId || _load == null || IsOver() || _streaming != 0) return;
            _streaming = 1;
            _position = 0;
            _clock.Reset();
            if (_presentationReady && !_seeking)
            {
                _seekTarget = _timeline.FirstPlayableOffset;
                _seeking = true;
            }
            if (!_seeking && !_paused) _clock.Start();
            SetClock();
            SendState();
            Send(_load.Data);
            _playback = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            int generation = _generation;
            var cancellation = _playback;
            _ = Task.Run(() => StreamAsync(generation, cancellation));
        }
    }

    private async Task StreamAsync(int generation, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            using var reader = new ReplayReader(_path);
            reader.Next(); reader.Next(); // InitZone and join snapshot were sent by the join handshake.
            var next = reader.Next();
            int batchBytes = 0, batchRecords = 0;
            long lastState = 0;
            var updates = Stopwatch.StartNew();
            while (next != null)
            {
                bool delivered = false, catchup = false, finishSeek = false;
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    if (_seeking)
                    {
                        if (next.Kind == MatchRecorder.KindEnd || next.OffsetMs > _seekTarget) finishSeek = true;
                        else
                        {
                            _position = next.OffsetMs;
                            SendState();
                            if (next.Kind == MatchRecorder.KindStream) Send(next.Data);
                            delivered = catchup = true;
                        }
                    }
                    else if (!_paused && next.OffsetMs <= Position)
                    {
                        if (next.Kind == MatchRecorder.KindEnd) break;
                        if (next.Kind == MatchRecorder.KindStream) Send(next.Data);
                        delivered = true;
                    }
                    if (!_seeking && _controls && updates.ElapsedMilliseconds - lastState >= 500)
                    {
                        SendState();
                        lastState = updates.ElapsedMilliseconds;
                    }
                }
                if (catchup)
                {
                    batchBytes += next.Data.Length;
                    if (batchBytes >= 32 * 1024 || ++batchRecords >= 128)
                    {
                        await DrainSeekBatchAsync(token);
                        batchBytes = batchRecords = 0;
                    }
                }
                if (finishSeek)
                {
                    await DrainSeekBatchAsync(token, true);
                    lock (_gate)
                    {
                        token.ThrowIfCancellationRequested();
                        _position = _seekTarget;
                        _seeking = false;
                        _clock.Reset();
                        if (!_paused) _clock.Start();
                        SetClock(); SendState();
                    }
                    batchBytes = batchRecords = 0;
                }
                if (delivered) next = reader.Next();
                else if (!finishSeek) await Task.Delay(10, token);
            }
            if (_controls)
            {
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    _position = _timeline.DurationMs;
                    _clock.Reset();
                    _paused = true;
                    SetClock(); SendState();
                }
                // Keep the viewer in the replay so they can rewind after the final frame.
                await Task.Delay(Timeout.Infinite, token);
            }
            else await Task.Delay(EndHold, token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception)
        {
            Log.Warn(LogCat.Match, $"Replay playback failed: {MatchId} for {_viewerId}: {exception.Message}");
        }
        finally { lock (_gate) { if (ReferenceEquals(_playback, cancellation)) _playback = null; cancellation.Dispose(); } }
        lock (_gate) { if (generation == _generation) End(); }
    }

    private async Task DrainSeekBatchAsync(CancellationToken token, bool final = false)
    {
        if (!_acknowledgedSeeking)
        {
            await Task.Delay(20, token);
            return;
        }
        Task? wait = null;
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            _barriers.RemoveAll(b => b.Completion.Task.IsCompletedSuccessfully);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int id = ++_barrierSequence;
            // Capability 11: negative final barriers additionally wait for the
            // spectator HUD/scene to finish loading before playback starts.
            if (final && _presentationReady) id = -id;
            _barriers.Add((id, completion));
            using var writer = new BinaryWriter(new MemoryStream());
            writer.Write((byte)6);
            writer.Write((byte)113);
            writer.Write(id);
            Send(RecordingSender.Frame(writer));
            if (final) wait = Task.WhenAll(_barriers.Select(b => b.Completion.Task));
            else if (_barriers.Count >= _seekWindow) wait = _barriers[0].Completion.Task;
        }
        if (wait != null) await wait.WaitAsync(TimeSpan.FromSeconds(30), token);
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
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) return;
            _stop.Cancel();
            _clock.Stop();
            _host.EndReplay(_viewerId, GameInstanceId);
        }
    }
}
