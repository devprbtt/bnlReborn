using System.Numerics;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.BaseTypes;

const byte serviceZoneId = 6;
const byte zoneReadyId = 1;
const byte capabilityId = 98;
const byte teamPingId = 100;
const byte heroEmoteId = 102;
const uint capabilityMagic = 0x42504E47u;
var assertions = 0;

var legacySender = new FixtureSender();
var legacyZone = new ServiceZone(legacySender);
Receive(legacyZone, zoneReadyId);
Assert(!legacyZone.SupportsTeamPing && !legacyZone.SupportsHeroEmote,
    "a vanilla client remains unmodified when it sends no capability trailer");
Assert(legacySender.Messages.Count == 0, "a vanilla client receives no extension message");
legacyZone.SendTeamPing(7, new Vector3(1, 2, 3), Vector3.UnitY);
legacyZone.SendHeroEmote(7, true, 2);
Assert(legacySender.Messages.Count == 0, "extension messages are gated before negotiation");

var v1Sender = new FixtureSender();
var v1Zone = new ServiceZone(v1Sender);
Receive(v1Zone, zoneReadyId, writer =>
{
    writer.Write(capabilityMagic);
    writer.Write(1);
});
Assert(v1Zone.SupportsTeamPing && !v1Zone.SupportsHeroEmote,
    "protocol v1 negotiates team pings without emotes");
AssertCapability(v1Sender.TakeSingle(), 1);
v1Zone.SendTeamPing(42, new Vector3(1.5f, 2.5f, 3.5f), Vector3.UnitZ);
AssertTeamPing(v1Sender.TakeSingle(), 42, new Vector3(1.5f, 2.5f, 3.5f), Vector3.UnitZ);
v1Zone.SendHeroEmote(42, true, 4);
Assert(v1Sender.Messages.Count == 0, "protocol v1 cannot receive hero emotes");

var v2Sender = new FixtureSender();
var v2Zone = new ServiceZone(v2Sender);
Receive(v2Zone, zoneReadyId, writer =>
{
    writer.Write(capabilityMagic);
    writer.Write(2);
});
Assert(v2Zone.SupportsTeamPing && v2Zone.SupportsHeroEmote,
    "protocol v2 negotiates team pings and hero emotes");
AssertCapability(v2Sender.TakeSingle(), 2);
v2Zone.SendHeroEmote(99, true, 3);
AssertHeroEmote(v2Sender.TakeSingle(), 99, true, 3);
v2Zone.SendHeroEmote(99, false, -1);
AssertHeroEmote(v2Sender.TakeSingle(), 99, false, -1);

var preview = new BuildInfo();
legacyZone.SendBuildPreview(8, 9, true, preview);
v1Zone.SendBuildPreview(8, 9, true, preview);
v2Zone.SendBuildPreview(8, 9, true, preview);
Assert(legacySender.Messages.Count == 0 && v1Sender.Messages.Count == 0 && v2Sender.Messages.Count == 0,
    "old clients never receive build preview packets");
Assert(!v2Zone.SupportsBuildPreview, "v2 keeps emotes but has no preview extension");
var v3Sender = new FixtureSender();
var v3Zone = new ServiceZone(v3Sender);
Receive(v3Zone, zoneReadyId, writer => { writer.Write(capabilityMagic); writer.Write(3); });
AssertCapability(v3Sender.TakeSingle(), 3);
Assert(v3Zone.SupportsBuildPreview && v3Zone.SupportsHeroEmote && v3Zone.SupportsTeamPing,
    "v3 supports all three extensions");
foreach (bool initial in new[] { true, false })
{
    v3Zone.SendBuildPreview(8, 9, initial, preview);
    using var packet = Reader(v3Sender.TakeSingle());
    Assert(packet.ReadByte() == serviceZoneId && packet.ReadByte() == 104, "preview uses zone message 104");
    Assert(packet.ReadUInt32() == 8 && packet.ReadUInt32() == 9 && packet.ReadBoolean() == initial,
        "preview preserves unit, generation and initial flag");
    var decoded = BuildInfo.ReadRecord(packet);
    Assert(decoded.ShowGhost == preview.ShowGhost && decoded.ToolIndex == preview.ToolIndex,
        "preview record round trips");
    Assert(packet.BaseStream.Position == packet.BaseStream.Length, "preview has no trailing bytes");
}
Receive(v3Zone, 103, writer => { writer.Write(9u); BuildInfo.WriteRecord(writer, preview); });
Assert(v3Sender.Messages.Count == 0, "unassociated sender cannot relay a build preview");

const float serverSecondsPerTick = 0.05f;
var primaryCaulkTicks = GameZone.GetChannelIntervalTicks(0.3f, serverSecondsPerTick);
var alternateCaulkTicks = GameZone.GetChannelIntervalTicks(0.15f, serverSecondsPerTick);
Assert(primaryCaulkTicks == 6, "primary caulk repeats every six server ticks");
Assert(alternateCaulkTicks == 3, "alternate caulk repeats every three server ticks");
Assert(GameZone.GetChannelIntervalTicks(0f, serverSecondsPerTick) == 1,
    "zero-length catalogue intervals are clamped to one tick");

var nextPulseTick = GameZone.GetNextChannelPulseTick(100, primaryCaulkTicks);
Assert(nextPulseTick == 106, "a repeat pulse is scheduled relative to channel start");
Assert(!GameZone.IsChannelPulseDue(105, nextPulseTick), "a channel does not pulse early");
Assert(GameZone.IsChannelPulseDue(106, nextPulseTick), "a channel pulses at its due tick");
Assert(GameZone.GetNextChannelPulseTick(108, primaryCaulkTicks) == 114,
    "a delayed tick schedules from now instead of issuing catch-up pulses");

Console.WriteLine($"Server team-feature protocol fixture passed ({assertions} assertions).");
return;

void Receive(ServiceZone zone, byte messageId, Action<BinaryWriter>? append = null)
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(messageId);
        append?.Invoke(writer);
    }
    stream.Position = 0;
    using var reader = new BinaryReader(stream);
    Assert(zone.Receive(reader), $"message {messageId} is accepted");
}

void AssertCapability(byte[] message, int version)
{
    using var reader = Reader(message);
    Assert(reader.ReadByte() == serviceZoneId, "capability uses the zone service");
    Assert(reader.ReadByte() == capabilityId, "capability uses message 98");
    Assert(reader.ReadInt32() == version, "server advertises negotiated protocol version");
    Assert(reader.BaseStream.Position == reader.BaseStream.Length, "capability has no trailing payload");
}

void AssertTeamPing(byte[] message, uint playerId, Vector3 position, Vector3 normal)
{
    using var reader = Reader(message);
    Assert(reader.ReadByte() == serviceZoneId, "ping uses the zone service");
    Assert(reader.ReadByte() == teamPingId, "ping uses message 100");
    Assert(reader.ReadUInt32() == playerId, "ping preserves the sender player id");
    Assert(ReadVector3(reader) == position, "ping preserves its position");
    Assert(ReadVector3(reader) == normal, "ping preserves its surface normal");
    Assert(reader.BaseStream.Position == reader.BaseStream.Length, "ping has no trailing payload");
}

void AssertHeroEmote(byte[] message, uint playerId, bool active, int emoteIndex)
{
    using var reader = Reader(message);
    Assert(reader.ReadByte() == serviceZoneId, "emote uses the zone service");
    Assert(reader.ReadByte() == heroEmoteId, "emote uses message 102");
    Assert(reader.ReadUInt32() == playerId, "emote preserves the sender player id");
    Assert(reader.ReadBoolean() == active, "emote preserves its active state");
    Assert(reader.ReadInt32() == emoteIndex, "emote preserves its animation index");
    Assert(reader.BaseStream.Position == reader.BaseStream.Length, "emote has no trailing payload");
}

BinaryReader Reader(byte[] message) => new(new MemoryStream(message));

Vector3 ReadVector3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"Failed: {message}");
    assertions++;
}

sealed class FixtureSender : ISender
{
    public uint? AssociatedPlayerId { get; set; }
    public int SenderCount => 1;
    public Queue<byte[]> Messages { get; } = new();

    public byte[] TakeSingle()
    {
        if (Messages.Count != 1)
            throw new InvalidOperationException($"Expected one queued message, found {Messages.Count}.");
        return Messages.Dequeue();
    }

    public void Send(BinaryWriter writer) => Messages.Enqueue(((MemoryStream)writer.BaseStream).ToArray());
    public void Send(byte[] buffer) => Messages.Enqueue(buffer.ToArray());
    public void SendExcept(BinaryWriter writer, List<Guid> excluded) => Send(writer);
    public void Subscribe(Guid sessionId) { }
    public void Unsubscribe(Guid sessionId) { }
    public void UnsubscribeAll() { }
}
