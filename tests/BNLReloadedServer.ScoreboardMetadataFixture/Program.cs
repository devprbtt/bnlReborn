using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}

var direct = new ProxyProtocolV1Reader();
var directResult = direct.Accept([0x0c, 0x01, 0x02]);
Check(directResult.Ready && !directResult.Invalid && directResult.Address == null &&
      directResult.Payload.SequenceEqual(new byte[] { 0x0c, 0x01, 0x02 }), "ordinary game packets pass without a proxy header");

var proxied = new ProxyProtocolV1Reader();
var first = proxied.Accept("PROXY TCP4 203.0."u8);
Check(!first.Ready, "fragmented proxy header waits for completion");
var second = proxied.Accept("113.9 172.252.236.4 43120 28102\r\n\x0c\x01"u8);
Check(second.Ready && !second.Invalid && second.Address!.Equals(IPAddress.Parse("203.0.113.9")) &&
      second.Peer == "203.0.113.9:43120" && second.Payload.SequenceEqual(new byte[] { 0x0c, 0x01 }),
    "proxy header yields the real IPv4 endpoint and preserves coalesced game bytes");

var proxiedV6 = new ProxyProtocolV1Reader();
var v6 = proxiedV6.Accept("PROXY TCP6 2001:db8::9 2a0e:4007:fffe:703::233 43120 28102\r\n\x01"u8);
Check(v6.Ready && !v6.Invalid && v6.Address!.Equals(IPAddress.Parse("2001:db8::9")) &&
      v6.Payload.SequenceEqual(new byte[] { 0x01 }), "proxy header accepts IPv6 endpoints");

var invalid = new ProxyProtocolV1Reader().Accept("PROXY TCP4 nope 172.252.236.4 12 28102\r\n"u8);
Check(invalid.Ready && invalid.Invalid, "malformed proxy header is rejected");

var countryPath = Path.Combine(Path.GetTempPath(), "bnl-scoreboard-country-" + Guid.NewGuid() + ".csv.gz");
try
{
    await using (var file = File.Create(countryPath))
    await using (var gzip = new GZipStream(file, CompressionMode.Compress))
    await using (var writer = new StreamWriter(gzip, Encoding.UTF8))
    {
        await writer.WriteLineAsync("\"1.0.0.0\",\"1.0.0.255\",\"AU\"");
        await writer.WriteLineAsync("\"8.8.8.0\",\"8.8.8.255\",\"US\"");
        await writer.WriteLineAsync("\"2001:4860::\",\"2001:4860:ffff:ffff:ffff:ffff:ffff:ffff\",\"US\"");
    }
    Environment.SetEnvironmentVariable("BNL_IP_COUNTRY_DATABASE", countryPath);
    Check(IpCountryLookup.Resolve(IPAddress.Parse("1.0.0.7")) == "AU", "offline database resolves IPv4 country");
    Check(IpCountryLookup.Resolve(IPAddress.Parse("2001:4860::8888")) == "US", "offline database resolves IPv6 country");
    Check(IpCountryLookup.Resolve(IPAddress.Loopback) == null, "loopback never exposes a country");
}
finally
{
    File.Delete(countryPath);
}

var sender = new FixtureSender { AssociatedPlayerId = 10 };
var ping = new ServicePing(sender);
Check(ping.RoundTripMilliseconds == -1, "ping starts unknown");
ping.SendLivenessProbe();
Thread.Sleep(5);
using (var stream = new MemoryStream([1]))
using (var reader = new BinaryReader(stream))
    Check(ping.Receive(reader) && ping.RoundTripMilliseconds >= 1, "liveness pong records round-trip time");

var zone = new ServiceZone(sender, () => IPAddress.Parse("8.8.8.8"));
typeof(ServiceZone).GetProperty(nameof(ServiceZone.SupportsScoreboardMetadata),
        BindingFlags.Instance | BindingFlags.Public)!.SetValue(zone, true);
zone.SendScoreboardMetadata([
    new ScoreboardPlayerNetworkInfo(10, 42, "US"),
    new ScoreboardPlayerNetworkInfo(11, -1, "")
]);
var packet = sender.Packets.Last();
using (var stream = new MemoryStream(packet))
using (var reader = new BinaryReader(stream))
{
    Check(reader.ReadByte() == (byte)ServiceId.ServiceZone && reader.ReadByte() == 106 && reader.ReadUInt16() == 2,
        "scoreboard snapshot uses the negotiated extension message");
    Check(reader.ReadUInt32() == 10 && reader.ReadUInt16() == 42 && reader.ReadString() == "US",
        "scoreboard snapshot carries player ping and country only");
    Check(reader.ReadUInt32() == 11 && reader.ReadUInt16() == ushort.MaxValue && reader.ReadString() == "",
        "unknown ping and country have explicit wire values");
    Check(stream.Position == stream.Length, "scoreboard snapshot has no trailing address data");
}

Console.WriteLine($"Scoreboard metadata fixture passed: {checks} checks.");

sealed class FixtureSender : ISender
{
    public uint? AssociatedPlayerId { get; set; }
    public int SenderCount => 1;
    public List<byte[]> Packets { get; } = [];
    public void Send(BinaryWriter writer) => Packets.Add(((MemoryStream)writer.BaseStream).ToArray());
    public void Send(byte[] buffer) => Packets.Add(buffer.ToArray());
    public void SendExcept(BinaryWriter writer, List<Guid> excluded) => Send(writer);
    public void Subscribe(Guid sessionId) { }
    public void Unsubscribe(Guid sessionId) { }
    public void UnsubscribeAll() { }
}
