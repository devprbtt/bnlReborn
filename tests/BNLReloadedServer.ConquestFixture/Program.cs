using System.Numerics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
Vector3[] centers = [new(0,10,0),new(0,10,30),new(0,10,60)];
SkyBridgeConquest.Player[] attackers = [new(1,TeamType.Team1,centers[0]),new(2,TeamType.Team1,centers[1])];
var mode = new SkyBridgeConquest(centers);
Check(mode.Zones.All(z=>z.Owner==TeamType.Neutral) && mode.Target==600,"neutral start and ten minute target");
mode.Step(9,attackers); Check(mode.Zones.All(z=>z.Owner==TeamType.Neutral),"capture takes ten seconds");
mode.Step(1,attackers); Check(mode.Zones.Count(z=>z.Owner==TeamType.Team1)==2,"capture at ten seconds");
mode.Step(599,attackers); Check(!mode.Attacking && mode.Scores[1]==599,"only ownership time scores");
mode.Step(1,attackers); Check(mode.Attacker==TeamType.Team1 && mode.Tier=="lite","first threshold awards light BB");
Check(mode.Shielded(TeamType.Team1) && !mode.Shielded(TeamType.Team2),"only defender cubes exposed");
mode.Step(10,[attackers[0]]); Check(mode.Attacking && mode.AttackRemaining==80,"one death does not end attack");
mode.Step(10,attackers); Check(mode.AttackRemaining==70,"respawn does not reset attack duration");
mode.Step(0,[]); Check(!mode.Attacking && mode.Round==1,"last living carrier removed ends attack");
Check(mode.Scores.All(s=>s==0) && mode.Zones.All(z=>z.Owner==TeamType.Neutral),"new capture resets both scores and zones");
Check(mode.Shielded(TeamType.Team1) && mode.Shielded(TeamType.Team2),"both cube shields return");
foreach (string tier in new[] {"classic","uber","uber"})
{
    mode.Step(10,attackers); mode.Step(mode.Target,attackers);
    Check(mode.Attacking && mode.Tier==tier,"BB progression " + mode.Round + " " + tier);
    mode.Step(90,attackers); Check(!mode.Attacking,"expiry ends attack " + mode.Round);
    Check(mode.Target==(mode.Round>=3?180:600),"target progression " + mode.Round);
}
var contested = new SkyBridgeConquest(centers);
contested.Step(10,[attackers[0],new(3,TeamType.Team2,centers[0])]);
Check(contested.Zones[0].Owner==TeamType.Neutral,"equal populations pause capture");
contested.Step(10,[attackers[0],new(4,TeamType.Team1,centers[0]),new(3,TeamType.Team2,centers[0])]);
Check(contested.Zones[0].Owner==TeamType.Team1,"majority captures while enemies present");
contested.Step(30,[]); Check(contested.Scores[1]==0 && contested.Zones[0].Owner==TeamType.Team1,"empty zones retain ownership; one zone earns no time");
contested.Step(10,[new(3,TeamType.Team2,centers[0])]); Check(contested.Zones[0].Owner==TeamType.Team2,"enemy recaptures");
var bounds = new SkyBridgeConquest(centers);
bounds.Step(10,[new(1,TeamType.Team1,centers[0]+new Vector3(6,3,6)),new(2,TeamType.Team1,centers[1]+new Vector3(0,3.1f,0))]);
Check(bounds.Zones[0].Owner==TeamType.Team1 && bounds.Zones[1].Owner==TeamType.Neutral,"square corners included and separate elevations excluded");
foreach (string? payload in new string?[] {null,"{\"round\":1,\"zones\":[]}"})
{
    using var stream = new MemoryStream();
    new ZoneUpdate {ResourceCap=100,ConquestStateJson=payload}.Write(new BinaryWriter(stream));
    stream.Position=0; var received=ZoneUpdate.ReadRecord(new BinaryReader(stream));
    Check(received.ResourceCap==100 && received.ConquestStateJson==payload && stream.Position==stream.Length,"optional wire snapshot " +(payload==null?"legacy":"conquest"));
}
var ctor=typeof(UnitUpdater).GetConstructors().Single(c=>c.GetParameters().Length==20);
var callbacks=ctor.GetParameters().Select(p=> {
    var invoke=p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType,Expression.Default(invoke.ReturnType),invoke.GetParameters().Select(a=>Expression.Parameter(a.ParameterType,a.Name))).Compile();
}).ToArray();
var updater=(UnitUpdater)ctor.Invoke(callbacks);
var db=(ServerCatalogue)Databases.Catalogue;
var cubeCard=new CardUnit {Id="fixture_cube",Data=new UnitDataCommon(),Labels=[UnitLabel.Objective],Health=new UnitHealth {Health=new Health {MaxHealth=100,HealthType=HealthType.Objective}}};
var effectCard=new CardEffect {Id="effect_blockbuster_lite_buff",Duration=90,Effect=new ConstEffectBuff {Buffs=new() {{BuffType.WorldDamage,.5f}}}};
db.Replicate([cubeCard,effectCard]);
var cube=new Unit(20,new UnitInit {Key=cubeCard.Key,Team=TeamType.Team2},updater);
typeof(Unit).GetField("_health",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(cube,100f);
cube.ConquestDamageBlocked=()=>true;
cube.TakeDamage(20,null);
cube.TakeDamage(new DamageData(20,20,20,20,20,20,20,20,false,false,true,true),new ImpactData(),true,null,TeamType.Team1);
Check(cube.GetUpdateData().Health==100,"cube guard blocks direct and splash damage even with ignore-defences flags");
// Exercise the real GameZone buff application on a respawned unit with the same deadline.
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var buffMode=new SkyBridgeConquest(centers); buffMode.Step(10,attackers);buffMode.Step(600,attackers);
typeof(GameZone).GetField("_conquest",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(zone,buffMode);
ulong deadline=(ulong)DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeMilliseconds();
typeof(GameZone).GetField("_conquestBuffDeadline",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(zone,deadline);
var apply=typeof(GameZone).GetMethod("ApplyConquestUnit",BindingFlags.NonPublic|BindingFlags.Instance)!;
var hero=new Unit(21,new UnitInit {Key=cubeCard.Key,Team=TeamType.Team1,PlayerId=1},updater);
// Use a non-objective fixture unit for the player.
cubeCard.Labels=[];
apply.Invoke(zone,[hero]);
Check(hero.ActiveEffects.Single(e=>e.Key==effectCard.Key).TimestampEnd==deadline,"BB receives match deadline");
hero.ActiveEffects=hero.ActiveEffects.Clear(); apply.Invoke(zone,[hero]);
Check(hero.ActiveEffects.Single(e=>e.Key==effectCard.Key).TimestampEnd==deadline,"respawn restores BB without renewing timer");
buffMode.Step(90,attackers); apply.Invoke(zone,[hero]);
Check(hero.ActiveEffects.All(e=>e.Key!=effectCard.Key),"attack end removes BB effects");
Check(SkyBridgeConquest.MapId!=SkyBridgeConquest.OriginalMapId,"original and experimental identities separate");
var originalMap=Databases.MapDatabase.LoadMapData(new(SkyBridgeConquest.OriginalMapId))!;
var experimentMap=Databases.MapDatabase.LoadMapData(new(SkyBridgeConquest.MapId))!;
Check(originalMap!=null && experimentMap!=null,"both map payloads load independently");
Check(originalMap!.BlocksData!.SequenceEqual(experimentMap!.BlocksData!) && originalMap.Units.Count==experimentMap.Units.Count,"separate map preserves blocks and map units");
var drops=experimentMap.Units.Where(u=>u.UnitKey==new Key("unit_special_drop_point_blockbuster")).ToArray();
Check(drops.Length==3 && drops.Select(u=>u.Position).Distinct().Count()==3,"real map has three distinct capture centers");
var originalCard=new CardMap {Id=SkyBridgeConquest.OriginalMapId,Key=new(SkyBridgeConquest.OriginalMapId),Name=new LocalizedString {Text="Sky Bridge Don Edit",Data=[]},Data=originalMap};
var pool=new CardMapList {Custom=[originalCard.Key],Friendly=[originalCard.Key],Ranked=[originalCard.Key]};
List<Card> cards=[originalCard,pool];
ConquestMapRegistration.Register(cards); ConquestMapRegistration.Register(cards);
Check(cards.OfType<CardMap>().Count()==2 && pool.Custom.Count==2,"idempotent registration retains two custom map choices");
Check(originalCard.Name.Text=="Sky Bridge Don Edit" && pool.Ranked.Count==1 && pool.Friendly.Count==1,"original card and matchmaking pools unchanged");
var teamTwo=new SkyBridgeConquest(centers);
var defenders=attackers.Select(p=>p with {Team=TeamType.Team2}).ToArray();
teamTwo.Step(10,defenders); teamTwo.Step(600,defenders);
Check(teamTwo.Attacker==TeamType.Team2 && teamTwo.Shielded(TeamType.Team2) && !teamTwo.Shielded(TeamType.Team1),"team two wins and shield direction reverses");
// Actual Unit.Respawn sends its health reset unbuffered after OnRespawn.
// Deliver the Conquest spawn packet last, as happens when the zone buffer flushes.
var spawnSnapshot=typeof(GameZone).GetMethod("ConquestSpawnUpdate",BindingFlags.NonPublic|BindingFlags.Static)!;
var spawnCard=new CardUnit {Id="fixture_spawn",Data=new UnitDataCommon(),Health=new UnitHealth {Health=new Health {MaxHealth=110,HealthType=HealthType.Player}}};
db.Replicate(db.All.Append(spawnCard).Append(new CardGlobalLogic {Id="global_logic"}).ToList());
List<UnitUpdate> delayed=[]; List<UnitUpdate> immediate=[];
UnitUpdate WireCopy(UnitUpdate packet)
{
    using var bytes=new MemoryStream();packet.Write(new BinaryWriter(bytes));bytes.Position=0;
    return UnitUpdate.ReadRecord(new BinaryReader(bytes));
}
var spawnUpdater=updater with {
    GetTeamEffects=_=>[], OnChangeId=u=>u.Id+1,
    OnRespawn=(u,_,_)=>delayed.Add(WireCopy((UnitUpdate)spawnSnapshot.Invoke(null,[u])!)),
    OnUnitUpdate=(_,packet,unbuffered)=> { if(unbuffered) immediate.Add(WireCopy(packet)); }
};
var spawned=new Unit(50,new UnitInit {Key=spawnCard.Key,Team=TeamType.Team1,PlayerId=50},spawnUpdater);
spawned.ZoneService=DispatchProxy.Create<BNLReloadedServer.Service.IServiceZone,NoopZoneService>();
foreach (var label in new[] {"first spawn","death and respawn"})
{
    immediate.Clear();delayed.Clear();spawned.IsDead=true;
    typeof(Unit).GetField("_health",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(spawned,0f);
    Check(spawned.Respawn(Vector3.Zero,Quaternion.Identity),label+" executes real respawn");
    Check(spawned.GetUpdateData().Health==110,label+" initializes server health");
    float clientHealth=0;
    foreach(var packet in immediate.Concat(delayed)) if(packet.Health.HasValue) clientHealth=packet.Health.Value;
    Check(clientHealth==110,label+" stays full health after delayed Conquest packet");
    Check(delayed.All(p=>p.Health==null && p.Forcefield==null && p.Ammo==null),label+" Conquest packet excludes uninitialized combat state");
}
Console.WriteLine($"Conquest suite passed: {checks} checks.");

public class NoopZoneService : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method?.ReturnType==typeof(bool) ? false : null;
}
