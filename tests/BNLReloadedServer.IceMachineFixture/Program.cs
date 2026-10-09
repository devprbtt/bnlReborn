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

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

var sandbox=Path.Combine(AppContext.BaseDirectory,"fixture-sandbox");
Directory.CreateDirectory(Path.Combine(sandbox,"Configs"));
File.WriteAllText(Path.Combine(sandbox,"Configs/configs.json"),JsonSerializer.Serialize(new {master_host="127.0.0.1",master_public_host="127.0.0.1",region_name="fixture",region_icon="fixture"}));
Directory.SetCurrentDirectory(sandbox);
using var cardsJson = JsonDocument.Parse(File.ReadAllText(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "cards.json")));
var realCards = cardsJson.RootElement.EnumerateArray()
    .Where(doc => doc.TryGetProperty("category", out _))
    .Select(doc => JsonSerializer.Deserialize<Card>(doc.GetRawText(), JsonHelper.DefaultSerializerSettings)!)
    .ToList();

var gear = new CardGear { Id = "fixture_as_gear" };
var hero = new CardUnit
{
    Id = "fixture_as_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 160, HealthType = HealthType.Player } }
};
var mode = new CardGameMode { Id = "game_mode_friendly" };
((ServerCatalogue)Databases.Catalogue).Replicate([
    .. realCards.Where(c => c.Id is not ("global_logic" or "game_mode_friendly" or "game_mode_custom")), gear, hero, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_as_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" },
    new CardBlock {Id="fixture_stone",BlockId=1,Solid=true,Destructible=true},
    new CardBlock {Id="fixture_ground",BlockId=2,Solid=true,Grounded=true},
    new CardBlock {Id="fixture_locked",BlockId=59,Solid=true,Destructible=true}
]);
((UnitDataPlayer)hero.Data!).Gears = [gear.Key];

var players = new System.Collections.Concurrent.ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team1), (3u, TeamType.Team2), (4u, TeamType.Team2), (5u, TeamType.Team2) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero.Key, Nickname = $"p{id}" };
const int Side = 32;
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(Side, Side, Side),
    BlocksData = new byte[Side * Side * Side * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(6, 4, 6) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(24, 4, 24) }]
};
var service = Stub<IServiceZone>();
var unbuffered = Stub<IServiceZone>();
var zone = new GameZone(service, unbuffered, Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] callArgs) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, callArgs);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
var octree = typeof(GameZone).GetField("_unitOctree", Any)!.GetValue(zone)!;
var octreeRemove = octree.GetType().GetMethod("Remove", [typeof(Unit)])!;

// Marker lands on the floor at (16, 4, 16); the normal lifts the effect centre to y = 5.
var landing = new Vector3(16, 4, 16);
Unit Place(uint playerId, Vector3 feet) => OnZone(() =>
{
    var unit = (Unit)Call("CreatePlayerUnit", playerId, Stub<IServiceZone>())!;
    while ((bool)octreeRemove.Invoke(octree, [unit])!) { }
    unit.Transform = new ZoneTransform { Position = feet };
    Call("AddUnitToOctree", unit, unit.Transform);
    return unit;
});
var caster = Place(1, landing + new Vector3(1, 0, 0));
var ally = Place(2, landing + new Vector3(0, 0, 2));
var enemyNear = Place(3, landing + new Vector3(3, 0, 0));
var enemyEdge = Place(4, landing + new Vector3(0, 0, 4.5f));
var enemyFar = Place(5, landing + new Vector3(8, 0, 0));
var calls = ((StubProxy)(object)service).Calls;

OnZone(() =>
{
    var mapBinary=((ZoneData)typeof(GameZone).GetField("_zoneData",Any)!.GetValue(zone)!).BlocksData;
    var key=new Key("gear_abe_snow_thrower_ice_machine");
    var card=Databases.Catalogue.GetCard<CardGear>(key)!;
    var alt=(ToolShot)card.Tools![1];
    var stock=Databases.Catalogue.GetCard<CardGear>(new Key("gear_abe_snow_thrower"))!;
    var original=(ToolShot)stock.Tools![1];
    Check(Near(((original.Timing!.PreAttackTime??0)+original.Timing!.AttackTime)/
        ((alt.Timing!.PreAttackTime??0)+alt.Timing!.AttackTime),.65f),"secondary fire rate is exactly 65 percent of base");
    Check(JsonSerializer.Serialize(stock.Tools[0],JsonHelper.DefaultSerializerSettings)==
        JsonSerializer.Serialize(card.Tools[0],JsonHelper.DefaultSerializerSettings),"primary fire unchanged");
    Check(new List<Key>{stock.Key}.ConvertGear([new Key("perk_hero_abe_ice_machine")]).Single()==key,"perk selects replacement gear");
    var cell=new Vector3s(16,4,16); var adjacent=new Vector3s(17,4,16);
    void Set(Vector3s p,ushort id,TeamType team=TeamType.Neutral) { var b=mapBinary[p];b.Id=id;b.Team=team;b.Damage=0;b.VData=0; }
    for(int x=14;x<19;x++)for(int z=14;z<19;z++)Set(new Vector3s(x,3,z),2);
    void Shoot(Unit owner,Vector3 point,Vector3s normal,Unit[] targets) {
        var impact=new ImpactData {InsidePoint=point,Normal=normal,ShotPos=owner.Transform.Position,SourceKey=key,
            CasterPlayerId=owner.PlayerId,CasterUnitId=owner.Id,HitUnits=targets.Select(u=>u.Id).ToList()};
        Call("ApplyInstEffect",new UnitSource(owner),targets,alt.HitEffect!,impact,null,null,null,true);
    }
    foreach(var owner in new[]{caster,enemyFar}) {
        Set(cell,1);Set(adjacent,1);
        // Near the face: adding the normal would address the adjacent cell, which must stay stone.
        Shoot(owner,new Vector3(16.99f,4.5f,16.5f),new Vector3s(1,0,0),[]);
        Check(mapBinary[cell].Id==61,"hit cell becomes ice for "+owner.Team);
        Check(mapBinary[adjacent].Id==1,"adjacent cell is not converted");
        Check(mapBinary[cell].Team==owner.Team && mapBinary[cell].Team!=TeamType.Neutral,"ice receives shooter's team");
        Check(mapBinary.OwnedBlocks[cell]==owner,"ice ownership recorded");
        var special=(BlockSpecialSlippery)mapBinary[cell].Card.Special!;
        Check(special.AffectTeam==RelativeTeamType.Opponent && mapBinary[cell].Team==owner.Team,"friendly ice excluded by client's opponent-only movement rule");
    }
    // HitProvider packs the .02-wide inside/outside delta to zero; the face survives separately.
    var shots = (Dictionary<ulong, ShotInfo>)typeof(GameZone).GetField("_shotInfo", Any)!.GetValue(zone)!;
    var iceGear = new GearData(caster, key, 0);
    ulong shotId = 9000;
    foreach (var (face, direction) in new[] {
        (BlockShift.Left,-Vector3.UnitX), (BlockShift.Right,Vector3.UnitX),
        (BlockShift.Bottom,-Vector3.UnitY), (BlockShift.Top,Vector3.UnitY),
        (BlockShift.Back,-Vector3.UnitZ), (BlockShift.Front,Vector3.UnitZ) })
    {
        Set(cell,1);Set(adjacent,1);
        var surface = new Vector3(16.5f,4.5f,16.5f) + direction * .5f;
        var inside = surface - direction * .01f;
        var outside = surface + direction * .01f;
        var delta = outside - inside;
        var packed = new Vector3s((short)MathF.Round(delta.X*10), (short)MathF.Round(delta.Y*10), (short)MathF.Round(delta.Z*10));
        Check(packed==Vector3s.Zero,"client normal quantizes to zero: "+face);
        var hit = new HitData { InsidePoint=inside, Normal=packed, OutsideShift=face };
        using var wire = new MemoryStream();
        using (var writer = new BinaryWriter(wire, System.Text.Encoding.UTF8, true)) hit.Write(writer);
        wire.Position=0;
        using var reader = new BinaryReader(wire);
        hit=HitData.ReadRecord(reader);
        shots[++shotId]=new ShotInfo(shotId,caster,caster.Transform.Position,SourceGear:iceGear,ToolIndex:1);
        zone.ReceivedHit((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),new() { [shotId]=hit });
        Check(mapBinary[cell].Id==61 && mapBinary[cell].Team==caster.Team && mapBinary.OwnedBlocks[cell]==caster,
            "wire terrain hit converts one friendly ice block: "+face);
        Check(mapBinary[adjacent].Id==1,"wire terrain hit leaves adjacent block untouched: "+face);
    }
    foreach (var (id, face, targetId, label) in new[] {
        ((ushort)1, BlockShift.None, (uint?)null, "expiry without a surface face"),
        ((ushort)0, BlockShift.Top, (uint?)null, "air at a reported face"),
        ((ushort)1, BlockShift.Top, (uint?)enemyNear.Id, "direct unit hit with a surface face") })
    {
        Set(cell,id);
        var hit = new HitData { InsidePoint=new Vector3(16.5f,4.99f,16.5f),
            Normal=Vector3s.Zero, OutsideShift=face, TargetId=targetId };
        shots[++shotId]=new ShotInfo(shotId,caster,caster.Transform.Position,SourceGear:iceGear,ToolIndex:1);
        zone.ReceivedHit((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),new() { [shotId]=hit });
        Check(mapBinary[cell].Id==id,"zero-normal hit ignores "+label);
    }
    Set(cell,60);Shoot(caster,new Vector3(16.5f,4.5f,16.5f),new Vector3s(0,1,0),[]);
    Check(mapBinary[cell].Id==61 && mapBinary[cell].Team==caster.Team,"snow can become friendly ice");
    Set(cell,61,TeamType.Neutral);Shoot(caster,new Vector3(16.5f,4.5f,16.5f),new Vector3s(0,1,0),[]);
    Check(mapBinary[cell].Team==caster.Team,"neutral ice becomes friendly ice");
    foreach(var id in new ushort[]{0,2,59}) {
        Set(cell,id);Shoot(caster,new Vector3(16.5f,4.5f,16.5f),new Vector3s(0,1,0),[]);
        Check(mapBinary[cell].Id==id,"air, grounded or locked block protected: "+id);
    }
    Set(cell,1);Shoot(caster,new Vector3(16.5f,4.5f,16.5f),Vector3s.Zero,[]);
    Check(mapBinary[cell].Id==1,"range expiry without a surface hit does not convert");
    Shoot(caster,new Vector3(16.5f,4.5f,16.5f),new Vector3s(1,0,0),[enemyNear]);
    Check(mapBinary[cell].Id==1,"unit hit does not convert a block");
    Check(mapBinary.IceMachineHit(new Vector3(-1,4,16),caster).Count==0,"out-of-bounds hit ignored");
    Check(mapBinary.IceMachineHit(new Vector3(float.NaN,4,16),caster).Count==0,"non-finite hit ignored");
    return true;
});
zone.Stop();
Console.WriteLine($"BNL_ICE_MACHINE_OK checks={checks}");

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
