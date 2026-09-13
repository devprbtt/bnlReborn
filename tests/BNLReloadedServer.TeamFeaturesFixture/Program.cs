using System.Numerics;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Text.Json;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

const byte serviceZoneId = 6;
const byte zoneReadyId = 1;
const byte capabilityId = 98;
const byte teamPingId = 100;
const byte heroEmoteId = 102;
const uint capabilityMagic = 0x42504E47u;
var assertions = 0;

var waitingArenaMap = new MapData
{
    SpawnPoints =
    [
        new MapSpawnPoint { Position = new Vector3(20.5f, 10f, 30.5f), Team = TeamType.Neutral }
    ]
};
var waitingArena = new WaitingArenaInitiator(new CardGameMode(), waitingArenaMap);
Assert(waitingArena.AddPlayer(10) == TeamType.Neutral && waitingArena.AddPlayer(11) == TeamType.Neutral,
    "waiting arena assigns every player to the neutral FFA team");
for (uint playerId = 1000; playerId < 1256; playerId++) waitingArena.AddPlayer(playerId);
Assert(waitingArena.PlayerCount == 258, "waiting arena accepts an unbounded player set beyond normal team limits");
Assert(waitingArena.GetResourceCap() == 1000 && waitingArena.GetResourceAmount() == 1000,
    "waiting arena starts players at its 1,000-brick cap");
Assert(waitingArena.GetRespawnTimeOverride() == 2f,
    "waiting arena respawns players after two seconds");
Assert(!waitingArena.UsesPhaseBarriers(),
    "waiting arena removes both teams' protected phase force fields");
Assert(!waitingArena.AllowsTeamCommunication(),
    "waiting arena disables team-only callouts and map pings");
Assert(WaitingArenaInitiator.SpawnPositions.Count == 12,
    "waiting arena exposes twelve randomized spawn areas");
Assert(WaitingArenaInitiator.SelectRandomSpawn([7u, 9u], 1u, 7u) == 9u,
    "waiting arena excludes the previous spawn when another is available");
Assert(WaitingArenaInitiator.SelectRandomSpawn([], 3u) == 3u,
    "waiting arena spawn selection retains a safe fallback");
AssertWaitingArenaSpawnSupport();
Assert(waitingArena.IsInSpawnNoBuildZone(new Vector3s(20, 25, 30)),
    "waiting arena spawn protection covers the full vertical column");
Assert(!waitingArena.IsInSpawnNoBuildZone(new Vector3s(27, 10, 30)),
    "waiting arena spawn protection ends outside its six-block radius");
Assert(Unit.DoesFreeForAllRelationshipApply(RelativeTeamType.Friendly, 10, 10),
    "FFA players retain their own beneficial effects");
Assert(!Unit.DoesFreeForAllRelationshipApply(RelativeTeamType.Opponent, 10, 10),
    "FFA players cannot target themselves as opponents");
Assert(!Unit.DoesFreeForAllRelationshipApply(RelativeTeamType.Friendly, 10, 11) &&
       Unit.DoesFreeForAllRelationshipApply(RelativeTeamType.Opponent, 10, 11),
    "different FFA players are opponents and never teammates");
Assert(!Unit.DoesFreeForAllRelationshipApply(RelativeTeamType.Friendly, 10, null) &&
       Unit.DoesFreeForAllRelationshipApply(RelativeTeamType.Opponent, 10, null),
    "teamless map sources cannot grant friendly effects to FFA players");
Assert(Unit.DoesCombatRelationshipApply(true, RelativeTeamType.Opponent,
        TeamType.Neutral, 10, TeamType.Neutral, 11) &&
       !Unit.DoesCombatRelationshipApply(true, RelativeTeamType.Friendly,
        TeamType.Neutral, 10, TeamType.Neutral, 11),
    "FFA combat filtering treats different owners on the same neutral team as enemies");
Assert(Unit.DoesCombatRelationshipApply(true, RelativeTeamType.Friendly,
        TeamType.Neutral, 10, TeamType.Neutral, 10) &&
       !Unit.DoesCombatRelationshipApply(true, RelativeTeamType.Opponent,
        TeamType.Neutral, 10, TeamType.Neutral, 10),
    "FFA combat filtering keeps a player's own devices and effects friendly");

var playerCard = new CardUnit
{
    Id = "fixture_waiting_arena_player",
    Data = new UnitDataPlayer(),
    Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } }
};
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(catalogue.All.Append(playerCard).ToList());
var statsUpdater = CreateFixtureUpdater();
var victim = new Unit(100, new UnitInit { Key = playerCard.Key, Team = TeamType.Neutral, PlayerId = 10 }, statsUpdater);
var creditedKiller = new Unit(101, new UnitInit { Key = playerCard.Key, Team = TeamType.Neutral, PlayerId = 11 }, statsUpdater);
victim.KillStatsUpdate(TeamType.Neutral, false, creditedKiller, creditedKiller, [], true);
Assert(creditedKiller.Stats?.GetValueOrDefault(ScoreType.Kills) == 1,
    "FFA opponent kills count even though both players use the neutral protocol team");
Assert(creditedKiller.Stats?.GetValueOrDefault(ScoreType.KillPlayerByHero) == 1,
    "FFA opponent hero kills retain normal kill-source statistics");

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

UnitUpdater CreateFixtureUpdater()
{
    var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
    var callbacks = constructor.GetParameters().Select(parameter =>
    {
        var invoke = parameter.ParameterType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters()
            .Select(argument => Expression.Parameter(argument.ParameterType, argument.Name)).ToArray();
        return (object)Expression.Lambda(parameter.ParameterType, Expression.Default(invoke.ReturnType), parameters).Compile();
    }).ToArray();
    return (UnitUpdater)constructor.Invoke(callbacks);
}

void AssertWaitingArenaSpawnSupport()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    FileInfo? mapFile = null;
    while (directory != null && mapFile == null)
    {
        var candidate = new FileInfo(Path.Combine(directory.FullName, "Maps", "map_sr2_search_and_destroy.bnlbin"));
        if (candidate.Exists) mapFile = candidate;
        directory = directory.Parent;
    }
    Assert(mapFile != null, "Search and Destroy map is available for spawn validation");

    using var outer = Inflate(File.ReadAllBytes(mapFile!.FullName));
    using var document = JsonDocument.Parse(outer);
    var map = document.RootElement.GetProperty("map");
    var size = map.GetProperty("size");
    var sizeY = size.GetProperty("y").GetInt32();
    var sizeZ = size.GetProperty("z").GetInt32();
    var sizeX = size.GetProperty("x").GetInt32();
    using var blocksStream = Inflate(Convert.FromBase64String(map.GetProperty("blocks_data").GetString()!));
    var blocks = blocksStream.ToArray();
    var stride = blocks.Length / (sizeX * sizeY * sizeZ);

    ushort BlockId(int x, int y, int z)
    {
        var offset = ((x * sizeY + y) * sizeZ + z) * stride;
        return stride == 4 ? blocks[offset] : BitConverter.ToUInt16(blocks, offset);
    }

    foreach (var spawn in WaitingArenaInitiator.SpawnPositions)
    {
        var x = (int)MathF.Floor(spawn.X);
        var y = (int)MathF.Floor(spawn.Y);
        var z = (int)MathF.Floor(spawn.Z);
        Assert(BlockId(x, y, z) == 0, $"spawn {spawn} has open player space");
        var floor = BlockId(x, y - 1, z);
        Assert(floor is not 0 and not 59, $"spawn {spawn} has solid non-locked support above lava");
    }
}

MemoryStream Inflate(byte[] bytes)
{
    using var input = new MemoryStream(bytes);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    var output = new MemoryStream();
    zlib.CopyTo(output);
    output.Position = 0;
    return output;
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
