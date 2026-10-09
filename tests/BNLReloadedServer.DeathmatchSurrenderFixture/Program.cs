using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;
using Octree;

const ushort AirId = 0, FloorId = 1;
const int SizeX = 16, SizeY = 8, SizeZ = 16, FloorTop = 3;
const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var catalogue = (ServerCatalogue)Databases.Catalogue;
var gear = new CardGear { Id = "fixture_spawn_gear" };
var hero = new CardUnit { Id = "fixture_spawn_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom };
var device = new CardUnit
{
    Id = "fixture_spawn_device", Data = new UnitDataCommon(), Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center,
    Labels = [UnitLabel.RespawnPoint], SpawnPoint = new UnitSpawnPoint { SideShift = 0 }
};
var match = new CardMatch { Id = "fixture_spawn_match", Data = new MatchDataShieldCapture() };
var mode = new CardGameMode { Id = "game_mode_friendly" };
catalogue.Replicate([
    gear, hero, device, match, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardBlock { Id = "fixture_air", BlockId = AirId, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardBlock { Id = "fixture_floor", BlockId = FloorId, Passable = BlockPassableType.None, Solid = true, Grounded = true }
]);

((UnitDataPlayer)hero.Data!).Gears = [gear.Key]; // keys exist only after Replicate

var blocks = new byte[SizeX * SizeY * SizeZ * 6];
for (var x = 0; x < SizeX; x++)
for (var y = 0; y < FloorTop; y++)
for (var z = 0; z < SizeZ; z++)
    BitConverter.TryWriteBytes(blocks.AsSpan(((x * SizeY + y) * SizeZ + z) * 6, 2), FloorId);

var devicePos = new Vector3(8.5f, FloorTop + 0.5f, 8.5f);
var mapData = new MapData
{
    Match = MatchType.ShieldCapture,
    Properties = new MapDataProps(),
    Size = new Vector3s(SizeX, SizeY, SizeZ),
    BlocksData = blocks.Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(3f, FloorTop, 3f) }],
    Units = [new MapUnit { UnitKey = device.Key, Team = TeamType.Team1, Position = devicePos }]
};

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var id in new uint[] { 1, 2, 3 })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = TeamType.Team1, Hero = hero.Key, Nickname = $"p{id}" };

foreach (var deathmatch in new[] { false, true })
{
var service=Stub<IServiceZone>();
IGameInitiator initiator=deathmatch ? new WaitingArenaInitiator(mode,mapData) :
    Stub<IGameInitiator>(new() { ["GetGameMode"]=mode.Key, ["get_GameInstanceId"]="fixture" });
var zone=new GameZone(service,Stub<IServiceZone>(),Stub<IBuffer>(),Stub<ISender>(),mapData,initiator,players);
var done=new TaskCompletionSource();
zone.EnqueueAction(() => {
try {
    var data=(ZoneData)typeof(GameZone).GetField("_zoneData",Any)!.GetValue(zone)!;
    data.Phase.PhaseType=ZonePhaseType.Assault;
    foreach(var id in new uint[]{1,2,3})
        typeof(GameZone).GetMethod("CreatePlayerUnit",Any)!.Invoke(zone,[id,Stub<IServiceZone>()]);
    var reply=Stub<IServiceZone>();
    zone.ReceivedSurrenderRequest(42,1,reply);
    var calls=((StubProxy)(object)reply).Calls;
    Check(calls.Count==1 && calls[0].Name=="SendSurrenderStart" &&
        (SurrenderStartResultType)calls[0].Args[1]! == (deathmatch ? SurrenderStartResultType.Disabled : SurrenderStartResultType.Accepted),
        deathmatch ? "Deathmatch rejects start with Disabled" : "team mode still accepts surrender");
    if(deathmatch) {
        Check(!data.IsSurrenderRequest.Any(v=>v) && data.SurrenderEndTime.All(t=>t==null),"Deathmatch creates no ballot or timer");
        Check(!((StubProxy)(object)service).Calls.Any(c=>c.Name=="SendSurrenderProgress"),"Deathmatch broadcasts no vote");
        // Even a stale or injected active ballot cannot be voted on.
        data.IsSurrenderRequest[(int)TeamType.Team1]=true;
        data.SurrenderVotes[1]=null;
        zone.ReceivedSurrenderVoteRequest(1,true);
        Check(data.SurrenderVotes[1]==null,"Deathmatch ignores yes votes on a stale ballot");
        zone.ReceivedSurrenderVoteRequest(1,false);
        Check(data.SurrenderVotes[1]==null,"Deathmatch ignores no votes on a stale ballot");
    } else {
        Check(data.IsSurrenderRequest[(int)TeamType.Team1],"team mode starts a ballot");
        zone.ReceivedSurrenderVoteRequest(2,false);
        Check(data.SurrenderVotes[2]==false,"team mode still records votes");
    }
    done.SetResult();
} catch(Exception e) { done.SetException(e); }
});
try { done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
finally { zone.Stop(); }
}
Console.WriteLine($"BNL_DEATHMATCH_SURRENDER_OK checks={checks}");

static T Stub<T>(Dictionary<string,object?>? returns=null) where T:class
{
    var p=DispatchProxy.Create<T,StubProxy>();((StubProxy)(object)p).Returns=returns??[];return p;
}
public class StubProxy:DispatchProxy
{
    public Dictionary<string,object?> Returns=[];
    public List<(string Name,object?[] Args)> Calls=[];
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method==null)return null;
        Calls.Add((method.Name,args??[]));
        if(Returns.TryGetValue(method.Name,out var value))return value;
        var type=method.ReturnType;
        if(type==typeof(void))return null;
        if(type==typeof(bool))return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType?Activator.CreateInstance(type):null;
    }
}
