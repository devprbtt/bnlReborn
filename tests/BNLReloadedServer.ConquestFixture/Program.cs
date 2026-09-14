using System.Numerics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
Vector3[] centers = [new(0,10,0),new(0,10,30),new(0,10,60)];
SkyBridgeConquest.Player[] attackers = [new(1,TeamType.Team1,centers[0]),new(2,TeamType.Team1,centers[1])];
var mode = new SkyBridgeConquest(centers);
Check(mode.Zones.All(z=>z.Owner==TeamType.Neutral) && mode.Target==420,"neutral start and seven minute Lite target");
mode.Step(9,attackers); Check(mode.Zones.All(z=>z.Owner==TeamType.Neutral),"capture takes ten seconds");
mode.Step(1,attackers); Check(mode.Zones.Count(z=>z.Owner==TeamType.Team1)==2,"capture at ten seconds");
Check(mode.ZonesCaptured(1)==1 && mode.ZonesCaptured(2)==1 && mode.ZoneTimeSeconds(1)==10 && mode.ZoneTimeSeconds(2)==10,
    "capture contributors receive one zone and ten seconds of zone presence each");
mode.Step(419,attackers); Check(!mode.Attacking && mode.Scores[1]==419,"only ownership time scores");
mode.Step(1,attackers); Check(mode.Attacker==TeamType.Team1 && mode.Tier=="lite","first threshold awards light BB");
var snapshotDue=typeof(GameZone).GetMethod("ConquestSnapshotDue",BindingFlags.NonPublic|BindingFlags.Static)!;
Check((bool)snapshotDue.Invoke(null,[false,true])!,"BB transition bypasses periodic snapshot delay");
Check(!(bool)snapshotDue.Invoke(null,[false,false])!,"ordinary Conquest state retains periodic snapshot cadence");
Check(mode.Shielded(TeamType.Team1) && !mode.Shielded(TeamType.Team2),"only defender cubes exposed");
Check(!mode.ObjectiveShielded(TeamType.Team2,[UnitLabel.Line1],UnitLabel.Line1),"first defender cube is vulnerable during attack");
Check(mode.ObjectiveShielded(TeamType.Team2,[UnitLabel.Line3],UnitLabel.Line1),"later defender cube stays shielded until its turn");
Check(mode.ObjectiveShielded(TeamType.Team2,[UnitLabel.Line1],null),"objectives remain shielded when progression is exhausted");
Check(mode.ObjectiveShielded(TeamType.Team1,[UnitLabel.Line1],UnitLabel.Line1),"attacker objectives remain shielded");
mode.Step(10,[attackers[0]]); Check(mode.Attacking && mode.AttackRemaining==80,"one death does not end attack");
mode.Step(10,attackers); Check(mode.AttackRemaining==70,"respawn does not reset attack duration");
mode.Step(0,[]); Check(!mode.Attacking && mode.Round==1,"last living carrier removed ends attack");
Check(mode.Scores.All(s=>s==0) && mode.Zones.All(z=>z.Owner==TeamType.Neutral),"new capture resets both scores and zones");
Check(mode.Shielded(TeamType.Team1) && mode.Shielded(TeamType.Team2),"both cube shields return");
foreach (var progression in new[] { (Tier: "classic", Target: 300f, NextTarget: 180f),
                                    (Tier: "uber", Target: 180f, NextTarget: 180f),
                                    (Tier: "uber", Target: 180f, NextTarget: 180f) })
{
    Check(mode.Target==progression.Target,"target before BB " + progression.Tier);
    mode.Step(10,attackers); mode.Step(mode.Target,attackers);
    Check(mode.Attacking && mode.Tier==progression.Tier,"BB progression " + mode.Round + " " + progression.Tier);
    mode.Step(90,attackers); Check(!mode.Attacking,"expiry ends attack " + mode.Round);
    Check(mode.Target==progression.NextTarget,"target progression " + mode.Round);
}
var contested = new SkyBridgeConquest(centers);
contested.Step(10,[attackers[0],new(3,TeamType.Team2,centers[0])]);
Check(contested.Zones[0].Owner==TeamType.Neutral,"equal populations pause capture");
contested.Step(10,[attackers[0],new(4,TeamType.Team1,centers[0]),new(3,TeamType.Team2,centers[0])]);
Check(contested.Zones[0].Owner==TeamType.Team1,"majority captures while enemies present");
Check(contested.ZonesCaptured(1)==1 && contested.ZonesCaptured(4)==1 && contested.ZonesCaptured(3)==0,
    "all present majority players share capture credit but the defender does not");
Check(contested.ZoneTimeSeconds(1)==20 && contested.ZoneTimeSeconds(3)==20,
    "contested time still counts as time physically present in a zone");
contested.Step(30,[]); Check(contested.Scores[1]==0 && contested.Zones[0].Owner==TeamType.Team1,"empty zones retain ownership; one zone earns no time");
contested.Step(10,[new(3,TeamType.Team2,centers[0])]); Check(contested.Zones[0].Owner==TeamType.Team2,"enemy recaptures");
var bounds = new SkyBridgeConquest(centers);
bounds.Step(10,[new(1,TeamType.Team1,centers[0]+new Vector3(6,4,6)),new(2,TeamType.Team1,centers[1]+new Vector3(0,4.1f,0))]);
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
var buffMode=new SkyBridgeConquest(centers); buffMode.Step(10,attackers);buffMode.Step(420,attackers);
typeof(GameZone).GetField("_conquest",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(zone,buffMode);
var getResourceCap=typeof(GameZone).GetMethod("GetResourceCap",BindingFlags.NonPublic|BindingFlags.Instance)!;
Check((float)getResourceCap.Invoke(zone,null)! == 3000 && buffMode.Rules.InitialBricks==2000,
    "Conquest defaults provide authoritative initial bricks and cap");
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
var generatedConquest=cards.OfType<CardMap>().Single(c=>c.Id==SkyBridgeConquest.MapId);
Check(generatedConquest.Conquest is { InitialBricks: 2000, BrickCap: 3000, CaptureSeconds: 10 },
    "generated Conquest map exposes default CDB tuning");
var cdbJson=JsonSerializer.Serialize<Card>(generatedConquest,JsonHelper.DefaultSerializerSettings);
var cdbCopy=(CardMap)JsonSerializer.Deserialize<Card>(cdbJson,JsonHelper.DefaultSerializerSettings)!;
Check(cdbCopy.Conquest is { LiteBbSeconds: 420, ClassicBbSeconds: 300, ExtremeBbSeconds: 180 },
    "Conquest tuning round-trips through CDB JSON");
using(var cardBytes=new MemoryStream())
{
    generatedConquest.Write(new BinaryWriter(cardBytes));cardBytes.Position=0;
    var clientCopy=CardMap.ReadRecord(new BinaryReader(cardBytes));
    Check(clientCopy.Conquest==null && cardBytes.Position==cardBytes.Length,
        "server-only Conquest tuning does not change the client card protocol");
}
var customRules=new ConquestLogic {InitialBricks=4500,BrickCap=2500,CaptureSeconds=4,AttackSeconds=12,
    LiteBbSeconds=20,ClassicBbSeconds=15,ExtremeBbSeconds=10,TripleCaptureRate=3,
    ZoneHalfWidth=7,ZoneDepthBelow=9,ZoneHeightAbove=5};
var configured=new SkyBridgeConquest(centers,customRules);
Check(configured.Rules.InitialBricks==2500 && configured.Rules.BrickCap==2500,
    "CDB initial bricks are clamped to the CDB cap");
configured.Step(3,attackers);Check(configured.Zones.All(z=>z.Owner==TeamType.Neutral),"CDB capture time is authoritative before threshold");
configured.Step(1,attackers);Check(configured.Zones.Count(z=>z.Owner==TeamType.Team1)==2,"CDB capture time is authoritative at threshold");
configured.Step(20,attackers);Check(configured.Attacker==TeamType.Team1 && configured.AttackRemaining==12,
    "CDB Lite and attack clocks are authoritative");
configured.Step(12,attackers);Check(configured.Round==1 && configured.Target==15,"CDB Classic clock is authoritative");
var teamTwo=new SkyBridgeConquest(centers);
var defenders=attackers.Select(p=>p with {Team=TeamType.Team2}).ToArray();
teamTwo.Step(10,defenders); teamTwo.Step(420,defenders);
Check(teamTwo.Attacker==TeamType.Team2 && teamTwo.Shielded(TeamType.Team2) && !teamTwo.Shielded(TeamType.Team1),"team two wins and shield direction reverses");
var triple=new SkyBridgeConquest(centers);
var allZones=attackers.Append(new SkyBridgeConquest.Player(3,TeamType.Team1,centers[2])).ToArray();
triple.Step(10,allZones);triple.Step(10,allZones);
Check(triple.Scores[1]==20 && triple.ScoreRate(TeamType.Team1)==2,"triple cap scores twice as fast");
triple.Step(10,[new(9,TeamType.Team2,centers[2])]);
Check(triple.ScoreRate(TeamType.Team1)==1,"losing third zone returns scoring to normal");
var vertical=new SkyBridgeConquest(centers);
vertical.Step(10,[new(1,TeamType.Team1,centers[0]+new Vector3(0,-8,0)),new(2,TeamType.Team2,centers[0]+new Vector3(0,4,0)),new(3,TeamType.Team1,centers[0]+new Vector3(0,-8.01f,0))]);
Check(vertical.Zones[0].Team1Count==1 && vertical.Zones[0].Team2Count==1 && vertical.Zones[0].Contested,"bounded vertical column counts both decks and rejects below floor");
Check(vertical.Zones[0].Owner==TeamType.Neutral,"players on different decks contest one zone");
vertical.Step(0,[]);Check(vertical.Zones.All(z=>z.Team1Count==0 && z.Team2Count==0),"occupancy clears when players leave");
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
// Exercise the result injection and byte-keyed wire record, not only pure counters.
var resultMode=new SkyBridgeConquest(centers);
resultMode.Step(10,[attackers[0]]);resultMode.Step(140,[attackers[0]]);
var resultZone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
typeof(GameZone).GetField("_conquest",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(resultZone,resultMode);
var addResults=typeof(GameZone).GetMethod("AddConquestResultStats",BindingFlags.NonPublic|BindingFlags.Instance)!;
var resultStats=(Dictionary<PlayerMatchStatType,int>)addResults.Invoke(resultZone,[1u,null])!;
Check(resultStats[PlayerMatchStatType.ZonesCaptured]==1 && resultStats[PlayerMatchStatType.ZoneTimeSeconds]==150,"end-match payload includes captures and 150 seconds without a configured stats map");
var existingStats=new Dictionary<PlayerMatchStatType,int>{{PlayerMatchStatType.Kill,7}};
addResults.Invoke(resultZone,[1u,existingStats]);
Check(existingStats[PlayerMatchStatType.Kill]==7 && existingStats[PlayerMatchStatType.ZonesCaptured]==1,"participation augments existing combat stats");
var absentStats=(Dictionary<PlayerMatchStatType,int>)addResults.Invoke(resultZone,[99u,null])!;
Check(absentStats[PlayerMatchStatType.ZonesCaptured]==0 && absentStats[PlayerMatchStatType.ZoneTimeSeconds]==0,"players with no zone participation receive explicit zeros");
using(var wire=new MemoryStream()) {
 new EndMatchPlayerStats {Stats=resultStats,Total=42}.Write(new BinaryWriter(wire));wire.Position=0;
 var decoded=new EndMatchPlayerStats();decoded.Read(new BinaryReader(wire));
 Check(decoded.Stats![(PlayerMatchStatType)9]==1 && decoded.Stats[(PlayerMatchStatType)10]==150 && decoded.Total==42,"conquest byte keys survive result protocol round-trip");
}
typeof(GameZone).GetField("_conquest",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(resultZone,null);
Check(addResults.Invoke(resultZone,[1u,null])==null,"non-Conquest results remain unchanged");
Console.WriteLine($"Conquest suite passed: {checks} checks.");

public class NoopZoneService : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method?.ReturnType==typeof(bool) ? false : null;
}
