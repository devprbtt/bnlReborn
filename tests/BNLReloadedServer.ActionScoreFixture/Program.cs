using System.Diagnostics;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
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
object? Call(string name,params object?[] values)=>typeof(GameZone).GetMethod(name,flags)!.Invoke(zone,values);
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
Call("ScoreKill",cube,a,true,false,Array.Empty<uint>());Check(actual.Total(1)==490 && actual.Total(2)==118,"cube destroy and contributor assist each get one proximity bonus");
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
Console.WriteLine($"PASS action scoring: {checks} checks");
public class EmptyInitiator : DispatchProxy
{
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
}



