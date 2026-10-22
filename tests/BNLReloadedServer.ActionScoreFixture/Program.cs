using System.Diagnostics;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks=0;
void Check(bool ok,string message) { if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message); }
var rules=new ActionScoreRules();rules.Validate();var ledger=new ActionScoreLedger(rules);
ledger.Grant(1,"Kill",50,false);ledger.Grant(1,"Headshot",10,false);
Check(ledger.Total(1)==60,"kill plus headshot is 60");
ledger.Grant(1,"Assist",25,true);Check(ledger.Total(1)==91,"one objective bonus is added");
ledger.Accumulate(2,"Healing",49,10,false);Check(ledger.Total(2)==0,"small healing ticks bank below threshold");
ledger.Accumulate(2,"Healing",1,10,false);Check(ledger.Total(2)==5,"actual healing accumulates into chunks");
ledger.Accumulate(2,"Healing",float.NaN,10,false);Check(ledger.Total(2)==5,"invalid continuous amount rejected");
ledger.Accumulate(2,"Device damage",100,20,true);Check(ledger.Total(2)==11,"device damage chunk includes objective bonus");
Check(ledger.DeviceValue(20)==5 && ledger.DeviceValue(400)==20 && ledger.DeviceValue(9999)==50,"device score scales with cost and clamps");
Check(ledger.Ready(1,"spot:9",0,15) && !ledger.Ready(1,"spot:9",14.9,15) && ledger.Ready(1,"spot:9",15,15),"spot cooldown boundary");
Check(ledger.Ready(2,"spot:9",1,15),"spot cooldown is per spotter");
using(var doc=JsonDocument.Parse(ledger.Snapshot(true)))Check(doc.RootElement.GetProperty("awards").GetArrayLength()==0,"join baseline does not replay old awards");
for(int i=0;i<400;i++)ledger.Grant(3,"Kill",50,false);
using(var doc=JsonDocument.Parse(ledger.Snapshot(false)))Check(doc.RootElement.GetProperty("awards").GetArrayLength()==256 && ledger.Total(3)==20000,"bounded event history retains complete totals");
foreach(string? json in new string?[]{null,ledger.Snapshot(false)})
{
 using var stream=new MemoryStream();new ZoneUpdate{ActionScoresJson=json,WeatherStartTime=123,ConquestStateJson="{}"}.Write(new BinaryWriter(stream));stream.Position=0;
 var received=ZoneUpdate.ReadRecord(new BinaryReader(stream));Check(received.ActionScoresJson==json && received.WeatherStartTime==123 && stream.Position==stream.Length,"score snapshot wire roundtrip "+(json==null?"absent":"present"));
}

ledger.Published();
using(var doc=JsonDocument.Parse(ledger.Snapshot(false)))Check(doc.RootElement.GetProperty("awards").GetArrayLength()==0 && ledger.Total(3)==20000,"published awards clear without losing totals");
var trackerType=typeof(GameZone).Assembly.GetType("BNLReloadedServer.ServerTypes.MatchParticipationTracker")!;
var tracker=Activator.CreateInstance(trackerType,true)!;
var participant=new CompletedMatchPlayer { PlayerId=1 };
var tracked=(Dictionary<uint,CompletedMatchPlayer>)trackerType.GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tracker)!;tracked[1]=participant;
foreach(int cumulative in new[]{50,75,75})trackerType.GetMethod("SetResult")!.Invoke(tracker,new object?[]{1u,false,new Dictionary<PlayerMatchStatType,int>{{PlayerMatchStatType.ActionScore,cumulative},{PlayerMatchStatType.Kill,1}},cumulative,null,null,null,0});
Check(participant.TotalScore==75 && participant.Stats[PlayerMatchStatType.ActionScore]==75 && participant.Stats[PlayerMatchStatType.Kill]==3,"archive reconnect replaces cumulative score while retaining session statistics");
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
void Set(string name,object value)=>typeof(GameZone).GetField(name,flags)!.SetValue(zone,value);
object? Call(string name,params object?[] values)=>typeof(GameZone).GetMethod(name,flags)!.Invoke(zone,name=="ScoreKill" && values.Length==5?values.Concat(new object?[]{null}).ToArray():values);
var data=(ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData));data.Phase=new ZonePhase{PhaseType=ZonePhaseType.Assault};
Set("_zoneData",data);Set("_gameInitiator",DispatchProxy.Create<IGameInitiator,EmptyInitiator>());Set("_actionScoreRules",rules);Set("_scoreClock",Stopwatch.StartNew());
var actual=new ActionScoreLedger(rules);Set("_actionScores",actual);Set("_cubeContributors",new Dictionary<uint,Dictionary<uint,double>>());
var units=new Dictionary<uint,Unit>();var players=new Dictionary<uint,Unit>();var ids=new Dictionary<uint,uint>();Set("_units",units);Set("_playerUnits",players);Set("_playerIdToUnitId",ids);
var ctor=typeof(UnitUpdater).GetConstructors().Single(c=>c.GetParameters().Length==20);
var callbacks=ctor.GetParameters().Select(p=> {var invoke=p.ParameterType.GetMethod("Invoke")!;return (object)Expression.Lambda(p.ParameterType,Expression.Default(invoke.ReturnType),invoke.GetParameters().Select(a=>Expression.Parameter(a.ParameterType,a.Name))).Compile();}).ToArray();
var updater=(UnitUpdater)ctor.Invoke(callbacks);
updater=updater with { GetPlayerFromPlayerId=id=>players.GetValueOrDefault(ids.GetValueOrDefault(id)), OnActionScore=(p,r,a,pos,t)=>Call("AwardAction",p,r,a,pos,t) };
var db=(ServerCatalogue)Databases.Catalogue;
var playerCard=new CardUnit {Id="score_player",Data=new UnitDataCommon(),Health=new UnitHealth{Health=new Health{MaxHealth=100,HealthType=HealthType.Player}}};
var cubeCard=new CardUnit {Id="score_cube",Labels=[UnitLabel.Objective],Data=new UnitDataCommon(),Health=new UnitHealth{Health=new Health{MaxHealth=100,HealthType=HealthType.Objective}}};
var deviceCard=new CardUnit {Id="score_device",DeviceType=DeviceType.Device,BaseCost=400,Data=new UnitDataCommon(),Health=new UnitHealth{Health=new Health{MaxHealth=100,HealthType=HealthType.World}}};
db.Replicate([playerCard,cubeCard,deviceCard]);
Unit Make(uint id,CardUnit card,TeamType team,uint? player=null)
{
 var unit=new Unit(id,new UnitInit{Key=card.Key,Team=team,PlayerId=player,OwnerId=player},updater);units[id]=unit;
 if(player.HasValue){players[id]=unit;ids[player.Value]=id;}
 return unit;
}
var a=Make(1,playerCard,TeamType.Team1,1);var ally=Make(2,playerCard,TeamType.Team1,2);var enemy=Make(3,playerCard,TeamType.Team2,3);
Call("ScoreKill",enemy,a,true,true,new uint[]{2,2});Check(actual.Total(1)==60 && actual.Total(2)==25,"server kill routes headshot and distinct assists");
enemy.IsActive=false;Call("ScoreKill",enemy,a,true,true,new uint[]{2});Check(actual.Total(1)==60 && actual.Total(2)==25,"disconnect cleanup cannot award kills or assists");enemy.IsActive=true;
Call("ScoreKill",ally,a,false,true,Array.Empty<uint>());Check(actual.Total(1)==60,"team kill has no kill or headshot award");
Call("AwardAction",a,"Supply pickup",1f,Vector3.Zero,4u);Call("AwardAction",a,"Blockbuster pickup",1f,Vector3.Zero,5u);
Check(actual.Total(1)==185,"supply and blockbuster values");
Call("AwardAction",a,"Device built",1f,Vector3.Zero,null);Call("AwardAction",a,"Device built",1f,Vector3.Zero,null);
Check(actual.Total(1)==195,"immediate rebuild at same cell does not farm score");
ally.SendHealAttribution(a,50,null);Check(actual.Total(1)==200,"real ally heal attribution awards score");
a.SendHealAttribution(a,50,null);enemy.SendHealAttribution(a,50,null);Check(actual.Total(1)==200,"self and enemy healing excluded");
var device=Make(4,deviceCard,TeamType.Team2);Call("ScoreKill",device,a,true,false,Array.Empty<uint>());Check(actual.Total(1)==220,"enemy device destruction uses device base cost");
Call("ScoreDamage",enemy,100f,new ImpactData(),a,device);Check(actual.Total(1)==225,"confirmed device damage on opponent scores");
Call("ScoreDamage",ally,100f,new ImpactData(),a,device);Check(actual.Total(1)==225,"friendly device damage excluded");
Call("AwardAction",a,"Radar spot",1f,Vector3.Zero,enemy.Id);Call("AwardAction",a,"Ability spot",1f,Vector3.Zero,enemy.Id);
Check(actual.Total(1)==230,"radar and ability share repeat-target cooldown");
Call("AwardAction",a,"Ability spot",1f,Vector3.Zero,99u);Check(actual.Total(1)==240,"ability spotting has its own starting value");
var cube=Make(5,cubeCard,TeamType.Team2);Call("ScoreDamage",cube,10f,new ImpactData(),ally,ally);
Call("ScoreKill",cube,a,true,false,Array.Empty<uint>());Check(actual.Total(1)==490 && actual.Total(2)==275,"cube destroy grants exactly 250 to destroyer and nearby contributor without duplicate assist");
var result=(Dictionary<PlayerMatchStatType,int>)Call("ScoreResult",1u,null)!;Check(result[PlayerMatchStatType.ActionScore]==490,"results persist authoritative total");
data.MatchEnded=true;Call("AwardAction",a,"Kill",1f,Vector3.Zero,null);Check(actual.Total(1)==490,"ended match cannot score");
data.MatchEnded=false;data.Phase.PhaseType=ZonePhaseType.Waiting;Call("AwardAction",a,"Kill",1f,Vector3.Zero,null);Check(actual.Total(1)==490,"waiting phase cannot score");
data.Phase.PhaseType=ZonePhaseType.Assault;
var spotted=Make(6,playerCard,TeamType.Team2,6);
var markCard=new CardEffect {Id="score_vision_mark",Duration=3,Effect=new ConstEffectBuff{Buffs=new(){{BuffType.VisionMark,1}}}};
db.Replicate([playerCard,cubeCard,deviceCard,markCard]);
device.OwnerPlayerId=a.PlayerId;device.Team=a.Team;
int beforeSpot=actual.Total(1);
spotted.AddEffects([new ConstEffectInfo(markCard.Key)],a.Team,device.GetSelfSource(device.CreateImpactData()));
Check(actual.Total(1)==beforeSpot+6,"real radar mark attributes owner and objective bonus");
var abilityVictim=Make(7,playerCard,TeamType.Team2,7);beforeSpot=actual.Total(1);
abilityVictim.AddEffects([new ConstEffectInfo(markCard.Key)],a.Team,a.GetSelfSource(a.CreateImpactData()));
Check(actual.Total(1)==beforeSpot+12,"real hero ability mark awards spotter");
// Drive the same authoritative accolade registration used by UnitIsKilled.
data.MatchEnded=false;data.Phase.PhaseType=ZonePhaseType.Assault;units.Clear();units[a.Id]=a;units[enemy.Id]=enemy;
Set("_killAccolades",new KillAccoladeTracker());Set("_accoladeClock",Stopwatch.StartNew());
foreach(var expected in new[]{("First kill",60),("Double kill",75),("Multi kill",100),("Ultra kill",125),("Fantastic",150),("Unbelievable",175),("Unbelievable",175)})
{
 actual.Published();int before=actual.Total(1);
 var accolade=Call("RecordKillAccolades",enemy,a,true,false);
 Call("ScoreKill",enemy,a,true,false,Array.Empty<uint>(),accolade);
 using var doc=JsonDocument.Parse(actual.Snapshot(false));var awards=doc.RootElement.GetProperty("awards");
 Check(actual.Total(1)-before==expected.Item2 && awards.GetArrayLength()==1 && awards[0].GetProperty("reason").GetString()==expected.Item1,"accolade replaces kill: "+expected.Item1+" +"+expected.Item2);
}
actual.Published();int prior=actual.Total(1);Call("ScoreKill",enemy,a,true,true,Array.Empty<uint>(),KillAccolade.DoubleKill);
Check(actual.Total(1)-prior==85,"double kill and headshot stack once (75 + 10)");
// Shared cube rewards: actual player positions, radius edge, team and life eligibility.
units[cube.Id]=cube;a.Transform.Position=new Vector3(50,0,0);
var nearby=Make(20,playerCard,TeamType.Team1,20);
var edge=Make(21,playerCard,TeamType.Team1,21);edge.Transform.Position=new Vector3(12,0,0);
var outside=Make(22,playerCard,TeamType.Team1,22);outside.Transform.Position=new Vector3(12.01f,0,0);
var dead=Make(23,playerCard,TeamType.Team1,23);dead.IsDead=true;
var inactive=Make(24,playerCard,TeamType.Team1,24);inactive.IsActive=false;
var defender=Make(25,playerCard,TeamType.Team2,25);
var remoteContributor=Make(26,playerCard,TeamType.Team1,26);remoteContributor.Transform.Position=new Vector3(20,0,0);
Call("ScoreDamage",cube,10f,new ImpactData(),remoteContributor,remoteContributor);
int killerBefore=actual.Total(1);Call("ScoreKill",cube,a,true,false,Array.Empty<uint>());
Check(actual.Total(1)-killerBefore==250,"remote destroyer retains full cube reward");
Check(actual.Total(20)==250 && actual.Total(21)==250,"nearby teammates including exact radius boundary receive full reward without damage");
Check(actual.Total(22)==0,"outside radius receives no shared cube reward");
Check(actual.Total(23)==0 && actual.Total(24)==0 && actual.Total(25)==0,"dead inactive and defending players excluded from shared reward");
Check(actual.Total(26)==93,"remote recent contributor retains 75 assist plus existing objective bonus");
// CDB snake_case round-trip, catalogue changes, validation and legacy binary compatibility.
var configured=new ActionScoreRules();
foreach(var property in typeof(ActionScoreRules).GetProperties())
{
 double changed=Convert.ToDouble(property.GetValue(configured))+1;
 property.SetValue(configured,Convert.ChangeType(changed,property.PropertyType));
}
var global=new CardGlobalLogic { Id="global_logic", ActionScores=configured };
var globalJson=JsonSerializer.Serialize(global,JsonHelper.DefaultSerializerSettings);
var parsed=(CardGlobalLogic)JsonSerializer.Deserialize<Card>(globalJson,JsonHelper.DefaultSerializerSettings)!;
Check(globalJson.Contains("action_scores") && globalJson.Contains("cube_assist_window") && typeof(ActionScoreRules).GetProperties().All(p=>Equals(p.GetValue(configured),p.GetValue(parsed.ActionScores))),"all CDB score settings round-trip using catalogue snake_case JSON");
db.Replicate([parsed]);var frozen=ActionScoreRules.Load();
Check(frozen.Cube==251 && frozen.ObjectiveRadius==13 && frozen.Kill==51,"new match loads edited CDB values");
parsed.ActionScores!.Cube=300;
Check(frozen.Cube==251 && ActionScoreRules.Load().Cube==300,"running match keeps independent score snapshot after catalogue edit");
var invalid=new CardGlobalLogic { Id="global_logic",ActionScores=new ActionScoreRules { HealPerPoint=0 } };
bool rejected=false;try{db.Replicate([invalid]);}catch(InvalidDataException){rejected=true;}
Check(rejected && ActionScoreRules.Load().Cube==300,"invalid full CDB replication leaves previous settings active");
rejected=false;try{db.UpdateCard(invalid);}catch(InvalidDataException){rejected=true;}
Check(rejected && ActionScoreRules.Load().Cube==300,"invalid incremental CDB update leaves previous settings active");
var validationCards=Enumerable.Range(0,999).Select(i=>(Card)new CardEffect{Id="score_validate_"+i}).Append(invalid).ToList();
Check(CatalogueValidator.Validate(validationCards).Any(p=>p.Contains("global_logic.action_scores")),"CDB fetch validation reports invalid score field");
byte[] Binary(CardGlobalLogic card){using var mem=new MemoryStream();card.Write(new BinaryWriter(mem));return mem.ToArray();}
var withScores=Binary(parsed);parsed.ActionScores=null;
Check(withScores.SequenceEqual(Binary(parsed)),"server-only score settings do not change legacy client binary cards");
db.Replicate([parsed]);Check(ActionScoreRules.Load().Cube==250,"older CDB without action_scores keeps defaults");
var partial=JsonSerializer.Deserialize<CardGlobalLogic>("{\"_id\":\"global_logic\",\"action_scores\":{\"cube\":400}}",JsonHelper.DefaultSerializerSettings)!;
db.Replicate([partial]);Check(ActionScoreRules.Load().Cube==400 && ActionScoreRules.Load().Assist==25,"partial CDB settings preserve unspecified defaults");
// Hero blocks use the same production placement gate and destruction path as devices.
data.MatchEnded=false;data.Phase.PhaseType=ZonePhaseType.Assault;units.Clear();units[a.Id]=a;
var heroBlockCard=new CardUnit {Id="score_hero_block",DeviceType=DeviceType.Hero,BaseCost=400,Data=new UnitDataCommon(),Health=new UnitHealth{Health=new Health{MaxHealth=100,HealthType=HealthType.World}}};
db.Replicate([playerCard,heroBlockCard,deviceCard]);
var buildMethod=typeof(GameZone).GetMethod("ScoreDeviceBuilt",BindingFlags.Static|BindingFlags.NonPublic)!;
int beforeHero=actual.Total(1);
buildMethod.Invoke(null,new object[]{a,heroBlockCard.DeviceType,new Vector3(90,0,0)});
Check(actual.Total(1)==beforeHero+rules.Build,"hero block placement earns configured build points");
buildMethod.Invoke(null,new object[]{a,heroBlockCard.DeviceType,new Vector3(90,0,0)});
Check(actual.Total(1)==beforeHero+rules.Build,"hero block repeated placement retains rebuild cooldown");
foreach(var type in new[]{DeviceType.None,DeviceType.World,DeviceType.Block})
 buildMethod.Invoke(null,new object[]{a,type,new Vector3(95,0,0)});
Check(actual.Total(1)==beforeHero+rules.Build,"ordinary terrain blocks remain excluded");
var heroBlock=Make(90,heroBlockCard,TeamType.Team2);
Call("ScoreKill",heroBlock,a,true,false,Array.Empty<uint>());
Check(actual.Total(1)==beforeHero+rules.Build+20,"enemy hero block destruction scores by cost");
heroBlock.Team=TeamType.Team1;Call("ScoreKill",heroBlock,a,false,false,Array.Empty<uint>());
heroBlock.Team=TeamType.Neutral;Call("ScoreKill",heroBlock,a,true,false,Array.Empty<uint>());
Check(actual.Total(1)==beforeHero+rules.Build+20,"friendly and neutral hero blocks give no destruction score");
Console.WriteLine($"PASS action scoring: {checks} checks");
public class EmptyInitiator : DispatchProxy
{
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
}



