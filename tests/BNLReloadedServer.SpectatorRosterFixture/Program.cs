// A player who quits a live match and then spectates it must not reappear on their old team's scoreboard,
// and a player who leaves without a living unit must disappear from everyone's scoreboard.
// Real live catalogue (args[0]); real GameZone roster, phase start, leave and join-in-progress paths.
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;
using Rating = Moserware.Skills.Rating;

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!);
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(16, 8, 16),
    BlocksData = new byte[16 * 8 * 16 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(2, 4, 2) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(13, 4, 13) }]
};
var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team2), (3u, TeamType.Team1) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = new Key("unit_hero_kira"), Nickname = $"p{id}" };
var broadcast = Stub<IServiceZone>();
var initiatorReturns = new Dictionary<string, object?>
{
    ["GetGameMode"] = new Key("game_mode_friendly"), ["get_GameInstanceId"] = "fixture", ["IsPlayerSpectator"] = false,
    ["GetTeamRatings"] = (new Dictionary<uint, Rating>(), new Dictionary<uint, Rating>())
};
var zone = new GameZone(broadcast, Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(initiatorReturns), players);
var zoneData = (ZoneData)typeof(GameZone).GetField("_zoneData", Any)!.GetValue(zone)!;
object? Call(string name, params object?[] a) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, a);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
}
var calls = ((StubProxy)(object)broadcast).Calls;
List<ZoneUpdate> RosterBroadcasts() =>
    calls.Where(c => c.Name == "SendUpdateZone").Select(c => (ZoneUpdate)c.Args[0]!)
        .Where(u => u.Statistics?.PlayerStats is not null).ToList();

OnZone(() =>
{
    var units = new[] { 1u, 2u }.Select(id => (Unit)Call("CreatePlayerUnit", id, Stub<IServiceZone>())!).ToList();
    foreach (var u in units) u.ZoneService = Stub<IServiceZone>();
    zone.BeginBuildPhase();
    return true;
});
Check(new[] { 1u, 2u, 3u }.All(zoneData.PlayerStats.ContainsKey), "match starts with all three rostered players");

// Player 3 never spawned (or is dead) and quits.
calls.Clear();
var removed = OnZone(() => zone.PlayerLeft(3, KickReason.MatchQuit));
Check(removed && !zoneData.PlayerStats.ContainsKey(3), "the zone drops the leaver's row");
Check(RosterBroadcasts().Any(u => !u.Statistics!.PlayerStats!.ContainsKey(3)), "the removal is broadcast to every client");

// Player 3 now spectates the same match. Their old roster entry is still in the zone's lobby info.
initiatorReturns["IsPlayerSpectator"] = true;
calls.Clear();
var spectatorService = Stub<IServiceZone>();
OnZone(() => { zone.JoinedInProgress(3, spectatorService); return true; });
Check(!zoneData.PlayerStats.ContainsKey(3) && !zoneData.PlayerInfo.ContainsKey(3), "a spectating former player gets no stats or player-info row");
Check(!RosterBroadcasts().Any(u => u.Statistics!.PlayerStats!.ContainsKey(3)), "no client is told the spectator is a player");
var spectatorCalls = ((StubProxy)(object)spectatorService).Calls;
Check(spectatorCalls.Any(c => c.Name == "SendUpdateZone" && ((ZoneUpdate)c.Args[0]!).Statistics is not null),
    "the spectator still receives the current scoreboard");

// The same player returning as a participant (not a spectator) is still put back on their team.
initiatorReturns["IsPlayerSpectator"] = false;
calls.Clear();
OnZone(() => { zone.JoinedInProgress(3, Stub<IServiceZone>()); return true; });
Check(zoneData.PlayerStats.TryGetValue(3, out var back) && back.Team == TeamType.Team1, "a returning participant rejoins their team");
Check(RosterBroadcasts().Any(u => u.Statistics!.PlayerStats!.ContainsKey(3)), "the returning participant is broadcast");

zone.Stop();
Console.WriteLine($"BNL_SPECTATOR_ROSTER_FIXTURE_OK checks={checks}");

static T Stub<T>(Dictionary<string, object?>? returns = null) where T : class
{
    var proxy = DispatchProxy.Create<T, StubProxy>();
    ((StubProxy)(object)proxy).Returns = returns ?? [];
    return proxy;
}

public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Returns = [];
    public readonly List<(string Name, object?[] Args)> Calls = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        lock (Calls) Calls.Add((method.Name, args ?? []));
        if (Returns.TryGetValue(method.Name, out var value)) return value;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(bool)) return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
