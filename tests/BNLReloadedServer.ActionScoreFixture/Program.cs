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
Call("AwardAction",a,"Device built",200f,Vector3.Zero,null);Call("AwardAction",a,"Device built",200f,Vector3.Zero,null);
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
buildMethod.Invoke(null,new object[]{a,heroBlockCard.DeviceType,new Vector3(90,0,0),400f});
Check(actual.Total(1)==beforeHero+ledger.BuildValue(400),"hero block placement earns configured build points");
buildMethod.Invoke(null,new object[]{a,heroBlockCard.DeviceType,new Vector3(90,0,0),400f});
Check(actual.Total(1)==beforeHero+ledger.BuildValue(400),"hero block repeated placement retains rebuild cooldown");
foreach(var type in new[]{DeviceType.None,DeviceType.World,DeviceType.Block})
 buildMethod.Invoke(null,new object[]{a,type,new Vector3(95,0,0),400f});
Check(actual.Total(1)==beforeHero+ledger.BuildValue(400),"ordinary terrain blocks remain excluded");
var heroBlock=Make(90,heroBlockCard,TeamType.Team2);
Call("ScoreKill",heroBlock,a,true,false,Array.Empty<uint>());
Check(actual.Total(1)==beforeHero+ledger.BuildValue(400)+20,"enemy hero block destruction scores by cost");
heroBlock.Team=TeamType.Team1;Call("ScoreKill",heroBlock,a,false,false,Array.Empty<uint>());
heroBlock.Team=TeamType.Neutral;Call("ScoreKill",heroBlock,a,true,false,Array.Empty<uint>());
Check(actual.Total(1)==beforeHero+ledger.BuildValue(400)+20,"friendly and neutral hero blocks give no destruction score");

// Exercise admitted real effect applications and the zone ledger, not only reason-to-value mappings.
CardAbility Ability(string id)=>new(){Id=id,Icon="",KillscoreIcon="",Prefab="",Charges=new AbilityCharges(),Behavior=new AbilityBehaviorCast{Application=new AbilityApplicationSelf()}};
var abilityAstro=Ability("ability_astro_boost");
var abilityDoc=Ability("ability_eliza_miracle_cure");
var abilitySarge=Ability("ability_sarge_frag_grenade");
var engineerCard=new CardUnit{Id="unit_hero_engineer",Data=new UnitDataCommon(),Health=playerCard.Health};
var effectCases=new[]{
 ("effect_hero_djinn_jinx_dizzy", "Jinx",rules.GenieJinx,false,false),
 ("effect_hero_djinn_jinx_confused", "Jinx",rules.GenieJinx,true,false),
 ("effect_hero_boxer_power_surge", "EMP affected",rules.SweetScienceEmp,true,false),
 ("effect_hero_magnus_lightning_cage_hitting", "Cage affected",rules.VanderCage,false,false),
 ("effect_abe_avalanche_area_slow", "Avalanche affected",rules.YetiAvalanche,false,false),
 ("effect_status_slow_glue_toxic_waste", "Goo affected",rules.RolyGoo,false,false),
 ("effect_hero_kreepy_killer_klown_debuff", "Enemy frightened",rules.KreepyFear,false,false),
 ("effect_hero_kira_jpop_star_reload_buff", "Reload boost",rules.KiraReloadBoost,false,true),
 ("effect_status_boost_10", "Ally boosted",rules.AstarellaBoost,false,true)};
var heroEffects=effectCases.Select(c=>new CardEffect{Id=c.Item1,Effect=new ConstEffectBuff{Buffs=[]}}).ToList();
var buildBoost=new CardEffect{Id="effect_hero_engineer_bfs_build",Effect=new ConstEffectBuff{Buffs=new(){{BuffType.BuildSpeed,1}}}};
var demolishBoost=new CardEffect{Id="effect_hero_trondson_heigh_ho_buff",Effect=new ConstEffectBuff{Buffs=new(){{BuffType.ToolWorldDamage,2}}}};
List<Card> heroCatalogue=[new CardGlobalLogic{Id="global_logic"},playerCard,deviceCard,heroBlockCard,engineerCard,abilityAstro,abilityDoc,abilitySarge,buildBoost,demolishBoost];heroCatalogue.AddRange(heroEffects);db.Replicate(heroCatalogue);
uint heroTarget=200;
foreach(var (effectId,reason,points,isDevice,friendly) in effectCases)
{
 var target=Make(heroTarget++,isDevice?deviceCard:playerCard,friendly?TeamType.Team1:TeamType.Team2,isDevice?null:heroTarget);
 var source=a.GetSelfSource(a.CreateImpactData(sourceKey:abilityAstro.Key));
 int before=actual.Total(1);target.AddEffects([new ConstEffectInfo(new Key(effectId))],a.Team,source);
 Check(actual.Total(1)==before+points,reason+" credits its real effect source");
 target.AddEffects([new ConstEffectInfo(new Key(effectId))],a.Team,source);
 Check(actual.Total(1)==before+points,reason+" repeated effect application is throttled");
}
int beforeSelf=actual.Total(1);
a.AddEffects([new ConstEffectInfo(new Key("effect_hero_kira_jpop_star_reload_buff"))],a.Team,a.GetSelfSource());
Check(actual.Total(1)==beforeSelf,"self ability benefits do not award support points");
var boosted=Make(240,playerCard,TeamType.Team1,240);
boosted.AddEffects([new ConstEffectInfo(buildBoost.Key),new ConstEffectInfo(demolishBoost.Key)],a.Team,a.GetSelfSource(a.CreateImpactData()));
int beforeSupport=actual.Total(1);
buildMethod.Invoke(null,new object[]{boosted,DeviceType.Block,new Vector3(110,0,0),40f});
Check(actual.Total(1)==beforeSupport+rules.TonyBuildAssist,"Tony is credited when another player completes a boosted build");
var destroyed=Make(241,deviceCard,TeamType.Team2);
Call("ScoreHeroDeviceDestroyed",destroyed,boosted,boosted,boosted.CreateImpactData());
Check(actual.Total(1)==beforeSupport+rules.TonyBuildAssist+rules.TrondsonDemolitionAssist,"Trondson is credited when boosted ally destroys an enemy device");
var repairer=Make(242,engineerCard,TeamType.Team1,242);var friendlyDevice=Make(243,deviceCard,TeamType.Team1);
friendlyDevice.UpdateData(new UnitUpdate{Health=100});
friendlyDevice.OwnerPlayerId=242;
float Repair(Unit target,float amount,Unit? healer)
{
 float restored=target.AddHealth(amount,out var credit);
 target.Healed(restored,healer?.GetSelfSource(),healer,credit);
 return credit;
}
friendlyDevice.TakeDamage(50,repairer.GetSelfSource());
Check(Repair(friendlyDevice,50,repairer)==0 && actual.Total(242)==0,"Tony self-damage and repair grants no XP");
friendlyDevice.TakeDamage(50,ally.GetSelfSource());
Check(Repair(friendlyDevice,50,repairer)==0 && actual.Total(242)==0,"teammate damage and repair grants no XP");
friendlyDevice.TakeDamage(50,null);
Check(Repair(friendlyDevice,50,repairer)==0 && actual.Total(242)==0,"environmental damage and repair grants no XP");
friendlyDevice.TakeDamage(50,enemy.GetSelfSource());
Check(Repair(friendlyDevice,49,repairer)==49 && actual.Total(242)==0,"repair reward banks actual enemy damage below threshold");
Check(Repair(friendlyDevice,100,repairer)==1 && actual.Total(242)==rules.ContinuousChunk,"Tony repairing his own enemy-damaged device earns XP with overheal clamped");
friendlyDevice.TakeDamage(40,enemy.GetSelfSource());
friendlyDevice.TakeDamage(40,repairer.GetSelfSource());
Check(Repair(friendlyDevice,80,repairer)==40,"mixed damage only credits the enemy portion");
friendlyDevice.TakeDamage(50,repairer.GetSelfSource());
Check(Repair(friendlyDevice,50,repairer)==0,"repair credit cannot be recycled by damaging the device again");
friendlyDevice.TakeDamage(50,enemy.GetSelfSource());
friendlyDevice.AddHealth(50); // Passive or unattributed healing must also consume enemy damage.
friendlyDevice.TakeDamage(50,repairer.GetSelfSource());
Check(Repair(friendlyDevice,50,repairer)==0,"passive healing consumes enemy repair credit");
friendlyDevice.TakeDamage(50,enemy.GetSelfSource());
Repair(friendlyDevice,50,ally);
friendlyDevice.TakeDamage(50,repairer.GetSelfSource());
Check(Repair(friendlyDevice,50,repairer)==0,"another healer consumes enemy repair credit");
var repairHero=Make(290,heroBlockCard,TeamType.Team1);repairHero.UpdateData(new UnitUpdate{Health=100});
repairHero.TakeDamage(50,enemy.GetSelfSource());
Check(Repair(repairHero,50,repairer)==50,"hero device units retain legitimate enemy repair credit");
var shieldedCard=new CardUnit{Id="repair_shield",DeviceType=DeviceType.Device,Data=new UnitDataCommon(),Health=deviceCard.Health};
heroCatalogue.Add(shieldedCard);db.Replicate(heroCatalogue);
var shielded=Make(291,shieldedCard,TeamType.Team1);shielded.UpdateData(new UnitUpdate{Health=100,Shield=30});
shielded.TakeDamage(new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){EnemyDeviceDamage=50},enemy.CreateImpactData(),false,enemy,enemy.Team);
Check(Repair(shielded,100,repairer)==20,"shield absorption never grants health repair credit");
var switched=new RepairDamageCredit();switched.Record(50,TeamType.Team1,TeamType.Team2);
Check(switched.Consume(50,TeamType.Team2)==0,"team change discards previous repair credit");
switched.Record(50,TeamType.Team1,TeamType.Neutral);
Check(switched.Consume(50,TeamType.Team1)==0,"neutral source never grants repair credit");

// Exercise the quantized voxel path, including legacy/passive heals and replacement.
var repairBlock=new CardBlock{Id="repair_voxel",BlockId=80,DeviceType=DeviceType.Hero,HasTeam=true,Destructible=true,CanStayInAir=true,Health=new Health{MaxHealth=255}};
heroCatalogue.Add(new CardBlock{Id="block_air",BlockId=0,Replaceable=true});heroCatalogue.Add(repairBlock);db.Replicate(heroCatalogue);
var repairMap=new MapBinary(3,new byte[4*4*4*6].Zip(0).ToArray(),new Vector3s(4,4,4),-1,new MapUpdater((_,_)=>{},(_,_)=>{},_=>{},_=>true));
var repairPos=new Vector3s(1,1,1);
void PlaceRepairBlock(){var block=repairMap[repairPos];block.Id=80;block.Team=TeamType.Team1;block.Damage=0;}
float RepairVoxel(float amount){repairMap.HealBlock(repairPos,amount,out _,out var credit);return credit;}
PlaceRepairBlock();
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},repairer);
Check(RepairVoxel(100)==0,"voxel self-damage grants no repair credit");
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},ally);
Check(RepairVoxel(100)==0,"voxel teammate damage grants no repair credit");
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},null);
Check(RepairVoxel(100)==0,"voxel environmental damage grants no repair credit");
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},enemy);
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},repairer);
Check(RepairVoxel(20)==20 && RepairVoxel(200)==30,"voxel mixed damage and full repair credit only remaining enemy damage");
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},enemy);
repairMap.HealBlock(repairPos,50,out _);
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},repairer);
Check(RepairVoxel(50)==0,"legacy voxel heal consumes enemy credit even without scoring");
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},enemy);
repairMap.RemoveBlock(repairPos);PlaceRepairBlock();
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=50},repairer);
Check(RepairVoxel(100)==0,"replacing a voxel cannot inherit old enemy damage");
repairMap.DamageBlock(repairPos,new DamageData(0,0,0,0,0,0,0,0,false,false,false,false){BlockDamage=.5f},enemy);
Check(RepairVoxel(100)==0,"sub-byte voxel damage cannot create XP");
foreach(string route in new[]{"SplashDamageBlocksRaycast","SplashDamageBlocksFlood"})
{
 foreach(var source in new[]{enemy,repairer})
 {
  PlaceRepairBlock();
  typeof(MapBinary).GetMethod(route,flags)!.Invoke(repairMap,new object?[]{new[]{new Vector3(1.5f,1.5f,1.5f)},new DamageData(0,0,0,0,0,40,0,0,false,false,false,false),source.CreateImpactData(),1f,new List<Unit>(),source,source.Team});
  int removed=repairMap[repairPos].Damage;
  Check(removed>0 && RepairVoxel(255)==(source==enemy?removed:0),route+" only enemy splash contributes repair credit");
 }
}
var docTarget=Make(244,playerCard,TeamType.Team1,244);int beforeDoc=actual.Total(1);
docTarget.SendHealAttribution(a,50,abilityDoc.Key);
Check(actual.Total(1)==beforeDoc+rules.ContinuousChunk,"Doc F healing scores once, without a duplicate generic heal reward");
using(var doc=JsonDocument.Parse(actual.Snapshot(false)))Check(doc.RootElement.GetProperty("awards").EnumerateArray().Last().GetProperty("reason").GetString()=="Miracle cure","Doc F has distinct award text");
var grenadeVictim=Make(245,playerCard,TeamType.Team2,245);int beforeGrenade=actual.Total(1);
Call("ScoreDamage",grenadeVictim,10f,a.CreateImpactData(sourceKey:abilitySarge.Key),a,a);
Call("ScoreDamage",grenadeVictim,10f,a.CreateImpactData(sourceKey:abilitySarge.Key),a,a);
Check(actual.Total(1)==beforeGrenade+2*rules.SargeGrenadeHit,"separate Sarge grenade hits both score");
var mortarCard=new CardUnit{Id="unit_device_cogwheel_mortar",DeviceType=DeviceType.Hero,Data=new UnitDataCommon()};heroCatalogue.Add(mortarCard);db.Replicate(heroCatalogue);
var mortar=Make(246,mortarCard,TeamType.Team1);int beforeMortar=actual.Total(1);
Call("ScoreHeroDeviceDestroyed",destroyed,a,mortar,a.CreateImpactData(sourceKey:mortarCard.Key));
Check(actual.Total(1)==beforeMortar+rules.CogMortarDestroy,"Cog mortar destruction gives its additional award");
var lampPickup=Make(247,deviceCard,TeamType.Neutral);lampPickup.ScoreLampOwner=1;int beforeLamp=actual.Total(1);
Call("ScoreLampPickup",lampPickup,boosted);Call("ScoreLampPickup",lampPickup,boosted);
Check(actual.Total(1)==beforeLamp+rules.GenieLampPickup,"friendly lamp collection credits builder once");
lampPickup.ScoreLampOwner=1;Call("ScoreLampPickup",lampPickup,a);Call("ScoreLampPickup",lampPickup,grenadeVictim);
Check(actual.Total(1)==beforeLamp+rules.GenieLampPickup,"self and enemy lamp collection give no support reward");

// Verify lamp ownership travels through real loot creation, not through the collector's ownership.
var lampCard=new CardUnit{Id="unit_device_djinn_lamp",Data=new UnitDataCommon(),DeviceType=DeviceType.Hero};
var pickupCard=new CardUnit{Id="unit_pickup_medikit_djinn_lamp",Data=new UnitDataPickup()};
heroCatalogue.Add(lampCard);heroCatalogue.Add(pickupCard);db.Replicate(heroCatalogue);
var originalLamp=Make(280,lampCard,TeamType.Team1);originalLamp.OwnerPlayerId=1;
Unit? createdPickup=null;
Set("_defaultUnitUpdater",updater with {OnUnitInit=(u,_)=>createdPickup=u,GetTeamEffects=_=>Array.Empty<ConstEffectInfo>()});
Call("CreateLootUnit",new LootItemUnit{LootUnitKey=pickupCard.Key,KillerRelativeLootTeam=RelativeTeamType.Both},new ZoneTransform(),boosted,originalLamp);
Check(createdPickup?.ScoreLampOwner==1 && createdPickup.OwnerPlayerId==null,"actual lamp loot preserves builder credit without assigning gameplay ownership");
var editedRules=new ActionScoreRules{GenieLampPickup=37,KiraReloadBoost=19,HeroEffectCooldown=2,TonyRepairPerPoint=4};
var roundtripRules=JsonSerializer.Deserialize<ActionScoreRules>(JsonSerializer.Serialize(editedRules,JsonHelper.DefaultSerializerSettings),JsonHelper.DefaultSerializerSettings)!;
Check(roundtripRules.GenieLampPickup==37 && roundtripRules.KiraReloadBoost==19 && roundtripRules.HeroEffectCooldown==2 && roundtripRules.TonyRepairPerPoint==4,"hero score values and thresholds round-trip through CDB JSON");
bool badRepair=false;try{new ActionScoreRules{TonyRepairPerPoint=0}.Validate();}catch(InvalidDataException){badRepair=true;}
Check(badRepair,"zero repair denominator is rejected");
Check(ledger.BuildValue(40)==2 && ledger.BuildValue(200)==10 && ledger.BuildValue(600)==30,"build score scales with actual cost");
Check(ledger.BuildValue(1)==1 && ledger.BuildValue(21)==2 && ledger.BuildValue(10000)==50,"build score rounds up and caps");
Check(ledger.BuildValue(0)==0 && ledger.BuildValue(-1)==0 && ledger.BuildValue(float.NaN)==0 && ledger.BuildValue(float.PositiveInfinity)==0,"free and invalid builds grant no points");
Check(ledger.BuildValue(float.MaxValue)==50,"extreme finite cost safely caps without integer overflow");
var tunedBuild=new ActionScoreRules{BuildCostPerPoint=10,BuildMin=2,BuildMax=25};tunedBuild.Validate();
var tunedLedger=new ActionScoreLedger(tunedBuild);
Check(tunedLedger.BuildValue(1)==2 && tunedLedger.BuildValue(100)==10 && tunedLedger.BuildValue(1000)==25,"build cost scale and bounds are CDB tunable");
var buildJson=JsonSerializer.Serialize(tunedBuild,JsonHelper.DefaultSerializerSettings);
var loadedBuild=JsonSerializer.Deserialize<ActionScoreRules>(buildJson,JsonHelper.DefaultSerializerSettings)!;
Check(loadedBuild.BuildCostPerPoint==10 && loadedBuild.BuildMin==2 && loadedBuild.BuildMax==25,"build settings round-trip in CDB");
var legacyBuild=JsonSerializer.Deserialize<ActionScoreRules>("{\"build\":10}",JsonHelper.DefaultSerializerSettings)!;
Check(new ActionScoreLedger(legacyBuild).BuildValue(40)==2,"legacy flat-build CDB field does not override proportional defaults");
foreach(var invalidBuild in new[]{new ActionScoreRules{BuildCostPerPoint=0},new ActionScoreRules{BuildMin=10,BuildMax=5}})
{
 bool buildRejected=false;try{invalidBuild.Validate();}catch(InvalidDataException){buildRejected=true;}
 Check(buildRejected,"invalid build score denominator or range rejected");
}
int beforeBuilds=actual.Total(1);
buildMethod.Invoke(null,new object[]{a,DeviceType.Device,new Vector3(150,0,0),40f});
buildMethod.Invoke(null,new object[]{a,DeviceType.Hero,new Vector3(151,0,0),600f});
Check(actual.Total(1)==beforeBuilds+32,"confirmed device and hero builds pass actual cost to score ledger");
buildMethod.Invoke(null,new object[]{a,DeviceType.Device,new Vector3(152,0,0),0f});
Check(actual.Total(1)==beforeBuilds+32,"free placement cannot earn build points");
int beforeObjectiveBuild=actual.Total(1);
Call("AwardAction",a,"Device built",200f,Vector3.Zero,null);
// The original cell is still in rebuild cooldown; changing cost must not bypass it.
Check(actual.Total(1)==beforeObjectiveBuild,"cost changes cannot bypass same-cell rebuild cooldown");
var bonusBuild=new ActionScoreLedger(rules);bonusBuild.Grant(1,"Device built",bonusBuild.BuildValue(200),true);
Check(bonusBuild.Total(1)==12,"objective bonus applies after proportional build score");
Console.WriteLine($"PASS action scoring: {checks} checks");
public class EmptyInitiator : DispatchProxy
{
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
}



