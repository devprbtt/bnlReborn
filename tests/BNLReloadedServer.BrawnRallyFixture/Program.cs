// Runs Brawn Rally Point eligibility, combat timing, native spawn packets and actual respawn ticks.
// All zone mutations execute on its action queue; the combat clock is controlled without sleeping.
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
var gear = new CardGear { Id = "fixture_spawn_gear", Tools = [new ToolShot()] };
var brawn = new CardHeroClass { Id = "fixture_brawn", Type = HeroClassType.Brawn };
var hero = new CardUnit { Id = "fixture_spawn_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom };
var device = new CardUnit
{
    Id = "fixture_spawn_device", Data = new UnitDataCommon(), Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center,
    Labels = [UnitLabel.RespawnPoint], SpawnPoint = new UnitSpawnPoint { SideShift = 0 }
};
var match = new CardMatch { Id = "fixture_spawn_match", Data = new MatchDataShieldCapture() };
var mode = new CardGameMode { Id = "game_mode_friendly" };
catalogue.Replicate([
    brawn, gear, hero, device, match, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardBlock { Id = "fixture_air", BlockId = AirId, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardBlock { Id = "fixture_floor", BlockId = FloorId, Passable = BlockPassableType.None, Solid = true, Grounded = true }
]);

((UnitDataPlayer)hero.Data!).Class = brawn.Key;
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

var zone = new GameZone(Stub<IServiceZone>(), Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), mapData,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);

T Field<T>(string name) => (T)typeof(GameZone).GetField(name, Any)!.GetValue(zone)!;
object? Call(string name, params object?[] args) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, args);
void Tick(ulong n) => ((Action)Call("OnTick", n)!)();

// Everything runs on the zone's own action queue, exactly as live ticks do.
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}

var zoneData = Field<ZoneData>("_zoneData");
var deviceSpawnId = OnZone(() => Field<Dictionary<uint, Unit>>("_playerSpawnPoints").Keys.Single());
var baseSpawnId = OnZone(() => zoneData.SpawnPoints.Keys.Single(k => k != deviceSpawnId));
var units = OnZone(() => new uint[] { 1, 2, 3 }.Select(id => (Unit)Call("CreatePlayerUnit", id, Stub<IServiceZone>())!).ToList());
foreach (var u in units) u.ZoneService = Stub<IServiceZone>(); // as SendLoadZone does for a joined player
Check(units.All(u => u.PlayerId is not null && !u.IsDead), "three live player units created through the zone");


long clock = 10000;
typeof(GameZone).GetField("_rallyClock", Any)!.SetValue(zone, (Func<long>)(() => clock));
Field<Dictionary<uint,long>>("_rallyCombatUntil").Clear();
var host = units[0]; var guest = units[1]; var second = units[2];
void Move(Unit unit, Vector3 pos) {
    unit.Transform.Position=pos;
    Call("UnitMoved", unit, 0UL, unit.Transform, pos);
}
SpawnPointLockType State(Unit? arriving = null) {
    object?[] args=[host, arriving, Vector3.Zero];
    return (SpawnPointLockType)typeof(GameZone).GetMethod("RallyState", Any)!.Invoke(zone,args)!;
}
OnZone(() => {
    Move(host,new Vector3(8.5f,FloorTop+.08f,8.5f));
    Move(guest,new Vector3(2.5f,FloorTop+.08f,2.5f));
    Move(second,new Vector3(3.5f,FloorTop+.08f,2.5f));
    Check(State()==SpawnPointLockType.Free,"Brawn carrier is eligible on open ground");
    var tuned=new CardPerk { Id=ClassPerkCatalogue.BrawnId, SlotType=PerkSlotType.Class, ClassPerk=new ClassPerkBalance { OutOfCombatSeconds=2 } };
    catalogue.Replicate(catalogue.All.Append(tuned).ToList());
    Call("MarkRallyCombat",host);
    clock+=1999;Check(State()==SpawnPointLockType.ServerBlocked,"CDB Brawn quiet period blocks early spawn");
    clock++;Check(State()==SpawnPointLockType.Free,"CDB Brawn two second quiet period expires");
    catalogue.Replicate(catalogue.All.Where(c=>c.Id!=tuned.Id).ToList());
    clock+=5000;

    Check(State(host)==SpawnPointLockType.ServerBlocked,"cannot spawn on yourself");
    host.Transform.IsJump=true;Check(State()==SpawnPointLockType.ServerBlocked,"jumping carrier blocked");host.Transform.IsJump=false;
    host.Transform.SetLocalVelocity(new Vector3(0,2,0));Check(State()==SpawnPointLockType.ServerBlocked,"vertical motion blocked");host.Transform.SetLocalVelocity(Vector3.Zero);
    host.Transform.SetLocalVelocity(new Vector3(0,-1,0));Check(State()==SpawnPointLockType.Free,"standing client stick-to-ground velocity is eligible");
    host.Transform.SetLocalVelocity(new Vector3(0,-7.5f,6.5f));Check(State()==SpawnPointLockType.Free,"walking client stick-to-ground velocity is eligible");host.Transform.SetLocalVelocity(Vector3.Zero);
    Move(host,new Vector3(8.5f,FloorTop+3,8.5f));Check(State()==SpawnPointLockType.ServerBlocked,"airborne at jump apex blocked without flag");
    Move(host,new Vector3(.2f,FloorTop+.08f,8.5f));Check(State()==SpawnPointLockType.ServerBlocked,"unsupported footprint at map edge blocked");
    Move(host,new Vector3(8.5f,FloorTop+.08f,8.5f));
    host.IsDead=true;Check(State()==SpawnPointLockType.ServerBlocked,"dead carrier blocked");host.IsDead=false;
    host.IsActive=false;Check(State()==SpawnPointLockType.ServerBlocked,"disconnected carrier blocked");host.IsActive=true;
    guest.Team=TeamType.Team2;Check(State(guest)==SpawnPointLockType.ServerBlocked,"opponent cannot use rally point");guest.Team=TeamType.Team1;
    brawn.Type=HeroClassType.Brains;Check(State()==SpawnPointLockType.ServerBlocked,"non-Brawn hero has no perk");brawn.Type=HeroClassType.Brawn;
    var map=zoneData.BlocksData;
    void SetBlock(int x,int y,int z,ushort id) { var block=map[x,y,z];block.Id=id; }
    // A low roof fits a crouched carrier but not an arriving standing hero.
    for(int x=6;x<=10;x++)for(int z=6;z<=10;z++)SetBlock(x,FloorTop+1,z,FloorId);
    Check(State()==SpawnPointLockType.WorldBlocked,"low ceiling blocks standing spawn volume");
    for(int x=6;x<=10;x++)for(int z=6;z<=10;z++)SetBlock(x,FloorTop+1,z,AirId);
    for(int x=7;x<=9;x++)for(int z=7;z<=9;z++)if(x!=8 || z!=8)SetBlock(x,FloorTop,z,FloorId);
    Check(State()==SpawnPointLockType.WorldBlocked,"one-person pocket cannot spawn a second player or through walls");
    for(int x=7;x<=9;x++)for(int z=7;z<=9;z++)if(x!=8 || z!=8)SetBlock(x,FloorTop,z,AirId);
    for(int x=6;x<=10;x++)for(int z=6;z<=10;z++)if(x!=8 || z!=8)SetBlock(x,FloorTop-1,z,AirId);
    Check(State()==SpawnPointLockType.WorldBlocked,"single supported block with surrounding drops cannot spawn a guest");
    for(int x=6;x<=10;x++)for(int z=6;z<=10;z++)SetBlock(x,FloorTop-1,z,FloorId);
    host.SetGear(gear.Key);
    zone.ReceivedCastRequest(host.PlayerId!.Value,new CastData {ToolIndex=0,ShotPos=host.Transform.Position,Shots=[new ShotData{TargetPos=Vector3.Zero}]});
    Check(State()==SpawnPointLockType.ServerBlocked,"firing even a missed shot starts combat timer");
    clock+=4999;Check(State()==SpawnPointLockType.ServerBlocked,"4999ms is still in combat");
    clock++;Check(State()==SpawnPointLockType.Free,"exactly five seconds allows spawning");
    Call("UnitIsDamaged",host,1f,new ImpactData{CasterPlayerId=guest.PlayerId,CasterUnitId=guest.Id});
    Check(State()==SpawnPointLockType.Free,"friendly damage does not start enemy-combat timer");
    guest.Team=TeamType.Team2;
    Call("UnitIsDamaged",host,1f,new ImpactData{CasterPlayerId=guest.PlayerId,CasterUnitId=guest.Id});
    Check(State()==SpawnPointLockType.ServerBlocked,"enemy damage starts combat timer");
    clock+=4000;Call("UnitIsDamaged",host,1f,new ImpactData{CasterPlayerId=guest.PlayerId,CasterUnitId=guest.Id});
    clock+=1000;Check(State()==SpawnPointLockType.ServerBlocked,"later damage resets the full five seconds");clock+=4000;guest.Team=TeamType.Team1;
    Check(State()==SpawnPointLockType.Free,"quiet period after last damage expires");
    Call("RefreshRallySpawns");
    return 0;
});
var rallyId=Field<Dictionary<uint,uint>>("_rallyPlayers").Single(p=>p.Value==host.PlayerId).Key;
OnZone(() => {
    Check((rallyId&0x80000000u)!=0,"mobile marker has distinct protocol ID");
    Check(zoneData.SpawnPoints[rallyId].Pos==host.Transform.Position,"minimap marker starts at carrier");
    Move(host,host.Transform.Position+Vector3.UnitX);clock+=100;Call("RefreshRallySpawns");
    Check(zoneData.SpawnPoints[rallyId].Pos==host.Transform.Position,"minimap marker follows movement");
    using(var wire=new MemoryStream()) {
        zoneData.SpawnPoints[rallyId].Write(new BinaryWriter(wire));wire.Position=0;
        var decoded=SpawnPoint.ReadRecord(new BinaryReader(wire));
        Check(decoded.Id==rallyId && decoded.Owner==host.PlayerId && decoded.Pos==host.Transform.Position,"existing spawn protocol roundtrips mobile ID, owner and position");
    }
    guest.Team=TeamType.Team2;var before=zoneData.PlayerSpawnPoints.GetValueOrDefault(guest.PlayerId!.Value);
    zone.ReceivedSelectSpawnPoint(guest.PlayerId.Value,rallyId);
    Check(zoneData.PlayerSpawnPoints.GetValueOrDefault(guest.PlayerId.Value)==before,"forged enemy selection rejected");guest.Team=TeamType.Team1;
    // Real two-dead-player tick tests the narrow window between marker selection and respawn.
    foreach(var u in new[]{guest,second}) {u.IsDead=true;u.IsDropped=true;u.RespawnTime=DateTimeOffset.Now.AddSeconds(-1);zone.ReceivedSelectSpawnPoint(u.PlayerId!.Value,rallyId);}
    host.Transform.IsJump=true;Tick(1);
    Check(guest.IsDead && second.IsDead,"carrier jump after selection blocks actual respawn");host.Transform.IsJump=false;
    guest.RespawnTime=DateTimeOffset.Now.AddHours(1);second.RespawnTime=DateTimeOffset.Now.AddHours(1);clock+=100;Tick(2);
    Check(guest.IsDead && second.IsDead,"rally point does not bypass normal respawn timers");
    guest.RespawnTime=DateTimeOffset.Now.AddSeconds(-1);second.RespawnTime=DateTimeOffset.Now.AddSeconds(-1);
    Call("MarkRallyCombat",host);Tick(1);
    Check(guest.IsDead && second.IsDead,"combat after selection blocks actual respawn");
    clock+=5000;Tick(2);
    Check(!guest.IsDead && !second.IsDead,"both guests can spawn when two distinct nearby spots are free");
    Check(Vector3.Distance(guest.Transform.Position,second.Transform.Position)>.9f,"simultaneous guests never overlap");
    Check(Vector3.Distance(guest.Transform.Position,host.Transform.Position)>.9f,"guest never overlaps the carrier");
    Check(Vector3.Distance(guest.Transform.Position,host.Transform.Position)<1.3f,"guest spawns beside current carrier position");
    return 0;
});
zone.Stop();
Console.WriteLine($"Brawn rally fixture passed: {checks} checks.");

static T Stub<T>(Dictionary<string, object?>? returns = null) where T : class
{
    var proxy = DispatchProxy.Create<T, StubProxy>();
    ((StubProxy)(object)proxy).Returns = returns ?? [];
    return proxy;
}

public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Returns = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        if (Returns.TryGetValue(method.Name, out var value)) return value;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(bool)) return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
