using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;

// Spectator look stream (zone capability 6): negotiation, byte-exact relay
// framing, malformed-body rejection and the spectator-only recipient rule.
const byte serviceZoneId = 6;
const byte zoneReadyId = 1;
const byte capabilityId = 98;
const byte receiveAimLookId = 107;
const byte aimLookId = 108;
const uint capabilityMagic = 0x42504E47u;
var assertions = 0;

var v5Sender = new FixtureSender();
var v5Zone = new ServiceZone(v5Sender);
Negotiate(v5Zone, 5);
AssertCapability(v5Sender.TakeSingle(), 5);
Assert(!v5Zone.SupportsAimLook, "a v5 client does not negotiate the look stream");
v5Zone.SendAimLook(7, Body(2, 3));
Assert(v5Sender.Messages.Count == 0, "a v5 client never receives look batches");

var v6Sender = new FixtureSender();
var v6Zone = new ServiceZone(v6Sender);
Negotiate(v6Zone, 6);
AssertCapability(v6Sender.TakeSingle(), 6);
Assert(v6Zone.SupportsAimLook && v6Zone.SupportsClanTags && v6Zone.SupportsHeroEmote,
    "v6 keeps every earlier extension and adds the look stream");

var future = new FixtureSender();
var futureZone = new ServiceZone(future);
Negotiate(futureZone, 9);
AssertCapability(future.TakeSingle(), 6);

foreach (var count in new[] { 1, 3, ServiceZone.AimLookMaxSamples })
{
    var body = Body(count, aimTool: count == 3 ? (byte)1 : (byte)255);
    v6Zone.SendAimLook(4242, body);
    using var packet = Reader(v6Sender.TakeSingle());
    Assert(packet.ReadByte() == serviceZoneId && packet.ReadByte() == aimLookId, "look batch uses zone message 108");
    Assert(packet.ReadUInt32() == 4242, "look batch names the streaming player");
    Assert(packet.ReadBytes(body.Length).SequenceEqual(body), $"a {count}-sample body is relayed byte for byte");
    Assert(packet.BaseStream.Position == packet.BaseStream.Length, "look batch has no trailing payload");
}

foreach (var count in new[] { 1, 5, ServiceZone.AimLookMaxSamples })
{
    var body = Body(count);
    var parsed = ServiceZone.ReadAimLookBody(Reader(body));
    Assert(parsed != null && parsed.SequenceEqual(body), $"a {count}-sample body parses whole");
    Assert(body.Length == ServiceZone.AimLookHeaderBytes + count * ServiceZone.AimLookSampleBytes,
        "body length matches the documented layout");
}
Assert(ServiceZone.ReadAimLookBody(Reader(Body(0))) == null, "an empty batch is rejected");
Assert(ServiceZone.ReadAimLookBody(Reader(Body(ServiceZone.AimLookMaxSamples + 1))) == null,
    "an oversized batch is rejected");
var truncated = Body(4);
Assert(ServiceZone.ReadAimLookBody(Reader(truncated[..^1])) == null, "a truncated batch is rejected");
Assert(ServiceZone.ReadAimLookBody(Reader(truncated[..5])) == null, "a truncated header is rejected");
Assert(ServiceZone.ReadAimLookBody(Reader(truncated[..(ServiceZone.AimLookHeaderBytes - 1)])) == null,
    "a header missing its count byte is rejected");

// An unassociated connection has no player to relay for: accepted, ignored.
Receive(v6Zone, receiveAimLookId, writer => writer.Write(Body(3)));
Assert(v6Sender.Messages.Count == 0, "an unassociated sender relays nothing");

// Recipient rule: only loaded, capable spectators other than the sender.
Assert(GameInstance.ShouldRelayAimLook(1, 2, recipientIsSpectator: true, recipientLoaded: true, recipientSupportsAimLook: true),
    "a loaded v6 spectator receives the stream");
Assert(!GameInstance.ShouldRelayAimLook(1, 2, recipientIsSpectator: false, recipientLoaded: true, recipientSupportsAimLook: true),
    "a player never receives another player's aim");
Assert(!GameInstance.ShouldRelayAimLook(1, 1, recipientIsSpectator: true, recipientLoaded: true, recipientSupportsAimLook: true),
    "a stream is never echoed to its sender");
Assert(!GameInstance.ShouldRelayAimLook(1, 2, recipientIsSpectator: true, recipientLoaded: false, recipientSupportsAimLook: true),
    "a spectator still loading the zone is skipped");
Assert(!GameInstance.ShouldRelayAimLook(1, 2, recipientIsSpectator: true, recipientLoaded: true, recipientSupportsAimLook: false),
    "a spectator without the capability is skipped");

Console.WriteLine($"Aim look fixture passed ({assertions} assertions).");
return;

byte[] Body(int count, byte aimTool = 255)
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(123456789L);
        writer.Write(aimTool);
        writer.Write((byte)2);
        writer.Write(0xDEADBEEFu);
        writer.Write(12.5f);
        writer.Write((byte)count);
        for (var index = 0; index < count; index++)
        {
            writer.Write((ushort)(index * 7));
            writer.Write((short)(-1234 + index));
            writer.Write((ushort)(35990 - index));
        }
    }
    return stream.ToArray();
}

void Negotiate(ServiceZone zone, int version) =>
    Receive(zone, zoneReadyId, writer =>
    {
        writer.Write(capabilityMagic);
        writer.Write(version);
    });

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
    Assert(reader.ReadByte() == serviceZoneId && reader.ReadByte() == capabilityId, "capability uses message 98");
    Assert(reader.ReadInt32() == version, $"server advertises negotiated version {version}");
}

BinaryReader Reader(byte[] message) => new(new MemoryStream(message));

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
