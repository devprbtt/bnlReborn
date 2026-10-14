using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;

// Replay recording: the sender tap, the file format round trip, finish/abort,
// and retention. Uses a private directory so it never touches real replays.
var directory = Path.Combine(Path.GetTempPath(), "bnl-replay-fixture-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("BNL_REPLAY_DIR", directory);
Environment.SetEnvironmentVariable("BNL_REPLAY_MAX_GB", "0.000001"); // ~1 KiB cap for the size test
var assertions = 0;

try
{
    Assert(ReplayStore.Directory == directory, "the replay directory honours BNL_REPLAY_DIR");
    Assert(ReplayStore.FileNameFor("52d0-12ab:1791828274361") == "52d0-12ab_1791828274361.bnlreplay",
        "match ids map to portable file names");

    // ---- RecordingSender ---------------------------------------------------
    var inner = new FixtureSender();
    var sender = new RecordingSender(inner);
    Assert(sender.SenderCount == inner.SenderCount, "an idle recording sender adds no listener");
    sender.Send(Writer(1, 2, 3));
    Assert(inner.Messages.Count == 1 && inner.Messages[0].SequenceEqual(new byte[] { 3, 1, 2, 3 }),
        "a passthrough send reaches the sessions framed");

    var recorder = MatchRecorder.TryStart("instance-a:1000", 1000, new { match_id = "instance-a:1000" })!;
    Assert(recorder != null, "a recorder starts in the configured directory");
    recorder!.RecordKeyframe(new byte[] { 2, 9, 9 }, new byte[] { 1, 8 });
    sender.Attach(recorder);
    Assert(sender.SenderCount == inner.SenderCount + 1, "an attached recorder counts as a listener so update paths still run");
    sender.Send(Writer(4, 5));
    sender.Send(new byte[] { 1, 6 });
    sender.SendExcept(Writer(7), [Guid.NewGuid()]);
    var aim = ServiceZone.BuildAimLookPacket(42, new byte[] { 0xAA, 0xBB });
    recorder.RecordStream(aim);
    Assert(sender.Detach() == recorder && sender.SenderCount == inner.SenderCount, "detaching removes the extra listener");
    sender.Send(Writer(9));
    recorder.Finish(2000, "ObjectivesDestroyed");
    recorder.Finish(3000, "ignored");
    await recorder.Completion;

    var path = ReplayStore.PathFor("instance-a:1000");
    Assert(path != null && !File.Exists(path + ".part"), "a finished recording is renamed from .part and listed");

    using (var reader = new ReplayReader(path!))
    {
        Assert(reader.Metadata.RootElement.GetProperty("match_id").GetString() == "instance-a:1000",
            "metadata round-trips");
        var records = new List<ReplayRecord>();
        while (reader.Next() is { } record) records.Add(record);
        Assert(records.Select(r => r.Kind).SequenceEqual(new byte[]
        {
            MatchRecorder.KindInitZone, MatchRecorder.KindLoad,
            MatchRecorder.KindStream, MatchRecorder.KindStream, MatchRecorder.KindStream, MatchRecorder.KindStream,
            MatchRecorder.KindEnd
        }), "keyframe, four stream records and the footer are written in order");
        Assert(records[0].Data.SequenceEqual(new byte[] { 2, 9, 9 }) && records[1].Data.SequenceEqual(new byte[] { 1, 8 }),
            "keyframe payloads round-trip");
        Assert(records[2].Data.SequenceEqual(new byte[] { 2, 4, 5 }), "a writer send is recorded framed like the wire");
        Assert(records[3].Data.SequenceEqual(new byte[] { 1, 6 }), "a buffered byte send is recorded as is");
        Assert(records[4].Data.SequenceEqual(new byte[] { 1, 7 }), "SendExcept is recorded (spectators are never excluded)");
        Assert(records[5].Data.SequenceEqual(aim), "a relayed look batch is recorded as its spectator packet");
        Assert(!records.Any(r => r.Kind == MatchRecorder.KindStream && r.Data.SequenceEqual(new byte[] { 1, 9 })),
            "nothing is recorded after detaching");
        for (var index = 1; index < records.Count; index++)
            Assert(records[index].OffsetMs >= records[index - 1].OffsetMs, "offsets never go backwards");
        using var footer = System.Text.Json.JsonDocument.Parse(records[^1].Data);
        Assert(footer.RootElement.GetProperty("ended_at").GetInt64() == 2000 &&
               footer.RootElement.GetProperty("end_reason").GetString() == "ObjectivesDestroyed",
            "the first Finish wins and writes the footer");
    }

    // The aim packet is exactly what ServiceZone.SendAimLook delivers.
    var aimSender = new FixtureSender();
    var zone = new ServiceZone(aimSender);
    Negotiate(zone);
    aimSender.Messages.Clear();
    zone.SendAimLook(42, new byte[] { 0xAA, 0xBB });
    Assert(aimSender.Messages.Count == 1 && aimSender.Messages[0].SequenceEqual(aim),
        "the recorded look packet matches the live spectator packet byte for byte");

    // ---- Abort -------------------------------------------------------------
    var aborted = MatchRecorder.TryStart("instance-b:1000", 1000, new { })!;
    aborted.RecordStream(new byte[] { 1, 1 });
    aborted.Abort();
    aborted.Finish(5, "late");
    await aborted.Completion;
    Assert(ReplayStore.PathFor("instance-b:1000") == null &&
           !Directory.EnumerateFiles(directory, "instance-b*").Any(), "an aborted recording leaves no file");

    // ---- Retention ---------------------------------------------------------
    var expired = Path.Combine(directory, "old_1.bnlreplay");
    File.WriteAllBytes(expired, new byte[16]);
    File.SetLastWriteTimeUtc(expired, DateTime.UtcNow - TimeSpan.FromDays(8));
    var stale = Path.Combine(directory, "crashed_1.bnlreplay.part");
    File.WriteAllBytes(stale, new byte[16]);
    File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(7));
    var fresh = Path.Combine(directory, "fresh_1.bnlreplay.part");
    File.WriteAllBytes(fresh, new byte[16]);
    Assert(ReplayStore.PathFor("old:1") == null, "an expired replay is not offered even before cleanup");
    var older = Path.Combine(directory, "big_1.bnlreplay");
    var newer = Path.Combine(directory, "big_2.bnlreplay");
    File.WriteAllBytes(older, new byte[700]);
    File.SetLastWriteTimeUtc(older, DateTime.UtcNow - TimeSpan.FromDays(2));
    File.WriteAllBytes(newer, new byte[700]);
    File.SetLastWriteTimeUtc(newer, DateTime.UtcNow - TimeSpan.FromDays(1));
    ReplayStore.EnforceLimits();
    Assert(!File.Exists(expired), "replays older than seven days are deleted");
    Assert(!File.Exists(stale), "abandoned partial files are deleted");
    Assert(File.Exists(fresh), "an in-progress partial file is kept");
    Assert(!File.Exists(older), "over the size cap, the oldest replay goes first");
    Assert(File.Exists(newer) && File.Exists(path!), "newer replays that fit the cap are kept");

    // ---- Replay ids --------------------------------------------------------
    var replayId = ReplayStore.ReplayIdFor("instance-p:5000");
    Assert(ReplayStore.IsReplayId(replayId) && replayId == ReplayStore.ReplayIdFor("instance-p:5000"),
        "replay ids are stable and carry the replay bit");
    Assert(!ReplayStore.IsReplayId(12345UL), "sequential live/custom game ids are never replay ids");
    Assert(ReplayStore.ReplayIdFor("52d01256-5318-4848-bc9f-71748de64e8d:1791828274361") == 0xA27FB1500374E977UL,
        "the replay id is the documented FNV-1a value the client recomputes");

    // ---- Playback ----------------------------------------------------------
    var recordedServerTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() - 3_600_000;
    var played = MatchRecorder.TryStart("instance-p:5000", 5000, new
    {
        match_id = "instance-p:5000",
        server_time_at_start = recordedServerTime,
        mode_key = 1111u,
        match_key = 2222u
    })!;
    played.RecordKeyframe(new byte[] { 2, 0x11, 0x11 }, new byte[] { 2, 0x22, 0x22 });
    played.RecordStream(new byte[] { 1, 0x31 });
    Thread.Sleep(150);
    played.RecordStream(new byte[] { 1, 0x32 });
    Thread.Sleep(150);
    played.RecordStream(new byte[] { 1, 0x33 });
    played.Finish(9000, "ObjectivesDestroyed");
    await played.Completion;
    Assert(ReplayStore.TryResolve(replayId, out var resolvedPath, out var resolvedMatch) &&
           resolvedMatch == "instance-p:5000" && resolvedPath.EndsWith("instance-p_5000.bnlreplay"),
        "a replay id resolves to its file and match id");
    Assert(!ReplayStore.TryResolve(ReplayStore.ReplayIdFor("missing:1"), out _, out _), "an unknown replay id does not resolve");

    var host = new FakeHost();
    var viewer = 77u;
    var session = Guid.NewGuid();
    var instance = new BNLReloadedServer.Database.ReplayInstance(host, resolvedPath, resolvedMatch, viewer);
    instance.SendUserToZone(viewer);
    var scene = host.Scenes.Single() as BNLReloadedServer.BaseTypes.SceneZone;
    Assert(scene != null && scene.IsSpectator && scene.GameMode.Hash == 1111u && scene.MatchKey.Hash == 2222u &&
           scene.MyTeam == BNLReloadedServer.BaseTypes.TeamType.Neutral,
        "the viewer is sent to a spectator zone scene with the recorded mode and match");
    Assert(instance.IsStarted && instance.IsPlayerSpectator(viewer) && !instance.IsOver(),
        "a replay instance presents as a started match the viewer spectates");

    instance.PlayerEnterScene(viewer);
    Assert(host.Sent.Count == 0, "InitZone waits for the viewer's match connection");
    instance.LinkGuidToPlayer(viewer, session, Guid.NewGuid());
    Assert(host.Sent.Count == 1 && host.Sent[0].Data.SequenceEqual(new byte[] { 2, 0x11, 0x11 }) &&
           host.Sent[0].Session == session, "linking delivers the pending InitZone to the viewer's session");

    var readyAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    instance.PlayerZoneReady(viewer);
    instance.PlayerZoneReady(viewer);
    Assert(host.ClockOffsets.Count == 1 && host.ClockOffsets[0] is { } clock &&
           Math.Abs(readyAt + clock - recordedServerTime) < 200,
        "zone ready shifts the viewer's clock to the recording's server time");
    Assert(host.Sent.Count >= 2 && host.Sent[1].Data.SequenceEqual(new byte[] { 2, 0x22, 0x22 }),
        "zone ready sends the join snapshot once, before the stream");

    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (host.Ended.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(50);
    var stream = host.Sent.Skip(2).ToList();
    Assert(stream.Select(s => s.Data[1]).SequenceEqual(new byte[] { 0x31, 0x32, 0x33 }),
        "recorded packets are streamed in order");
    Assert(stream[1].At - stream[0].At >= 120 && stream[2].At - stream[1].At >= 120,
        "packets keep their recorded spacing");
    Assert(host.Ended.SequenceEqual(new[] { (viewer, instance.GameInstanceId) }) && instance.IsOver() &&
           host.ClockOffsets.Count == 1, "the end of the recording returns the viewer and closes the instance");

    var leaving = new FakeHost();
    var early = new BNLReloadedServer.Database.ReplayInstance(leaving, resolvedPath, resolvedMatch, viewer);
    early.LinkGuidToPlayer(viewer, session, Guid.NewGuid());
    early.PlayerEnterScene(viewer);
    early.PlayerZoneReady(viewer);
    early.PlayerLeftInstance(viewer, BNLReloadedServer.BaseTypes.KickReason.MatchQuit);
    var sentAtLeave = leaving.Sent.Count;
    await Task.Delay(600);
    Assert(leaving.Ended.Count == 1 && leaving.Sent.Count == sentAtLeave,
        "leaving stops the stream immediately and cleans up once");
    early.PlayerDisconnected(viewer);
    Assert(leaving.Ended.Count == 1, "cleanup runs once");

    Console.WriteLine($"Replay recorder fixture passed ({assertions} assertions).");
}
finally
{
    try { Directory.Delete(directory, true); } catch { /* best effort */ }
}
return;

BinaryWriter Writer(params byte[] payload)
{
    var writer = new BinaryWriter(new MemoryStream());
    writer.Write(payload);
    return writer;
}

void Negotiate(ServiceZone zone)
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)1);
        writer.Write(0x42504E47u);
        writer.Write(6);
    }
    stream.Position = 0;
    using var reader = new BinaryReader(stream);
    Assert(zone.Receive(reader), "zone ready is accepted");
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"Failed: {message}");
    assertions++;
}

sealed class FakeHost : BNLReloadedServer.Database.IReplayHost
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    public List<BNLReloadedServer.BaseTypes.Scene> Scenes { get; } = new();
    public List<long?> ClockOffsets { get; } = new();
    public List<(uint, string)> Ended { get; } = new();
    public List<(Guid Session, byte[] Data, long At)> Sent { get; } = new();
    public void UpdateScene(uint playerId, BNLReloadedServer.BaseTypes.Scene scene) => Scenes.Add(scene);
    public void SetReplayClock(uint playerId, long? offsetMs) => ClockOffsets.Add(offsetMs);
    public void EndReplay(uint playerId, string instanceId) { lock (Ended) Ended.Add((playerId, instanceId)); }
    public void SendToSession(Guid session, byte[] framed) { lock (Sent) Sent.Add((session, framed, _clock.ElapsedMilliseconds)); }
}

sealed class FixtureSender : ISender
{
    public uint? AssociatedPlayerId { get; set; }
    public int SenderCount => 1;
    public List<byte[]> Messages { get; } = new();
    public void Send(BinaryWriter writer) => Messages.Add(RecordingSender.Frame(writer));
    public void Send(byte[] buffer) => Messages.Add(buffer.ToArray());
    public void SendExcept(BinaryWriter writer, List<Guid> excluded) => Send(writer);
    public void Subscribe(Guid sessionId) { }
    public void Unsubscribe(Guid sessionId) { }
    public void UnsubscribeAll() { }
}
