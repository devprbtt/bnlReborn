using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using Octree;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var hero = (CardUnit)JsonSerializer.Deserialize<Card>(File.ReadAllText(Path.Combine(root,"tests/fixtures/debug-dummy-sarge.json")), JsonHelper.DefaultSerializerSettings)!;
string original = JsonSerializer.Serialize(hero, JsonHelper.DefaultSerializerSettings);
List<Card> cards = [hero]; DebugDummyRegistration.Register(cards); DebugDummyRegistration.Register(cards);
Check(cards.Count == 3, "registration and compatibility class are idempotent");
cards.Add(new CardGlobalLogic{Id="global_logic", AvailableHeroes=[new Key(hero.Id!)]});
Check(JsonSerializer.Serialize(hero, JsonHelper.DefaultSerializerSettings) == original, "real Sarge is not mutated");
var card = cards.OfType<CardUnit>().Single(c => c.Id == DebugDummyRegistration.Id);
cards.Add(new CardGear { Id = "gear_sarge_stone_m60", Prefab = "m60" });
Check(card.Health!.Health!.HealthType == HealthType.Player && card.Data is UnitDataPlayer, "dummy receives player damage and player-target effects");
Check(card.Health.Health.MaxHealth == 1 && card.Health.KillReward?.PlayerReward == 50 && card.Health.KillReward?.TeamReward == 50 && card.SpawnPoint == null && card.Capturing == null, "dummy preserves hero kill resources without spawn or capture features");
Check(card.Loot is { SpawnOnEnemyKill: true, SpawnOnUndefinedKill: true, LootItem: LootItemCommon } &&
    ((LootItemCommon)card.Loot.LootItem).Item?.LootUnitKey == new Key("unit_pickup_hero_loot") &&
    ((LootItemCommon)card.Loot.LootItem).OpponentItem?.LootUnitKey == new Key("unit_pickup_hero_loot"),
    "dummy death retains normal hero health pickup for enemy and environmental kills");
Check(!ReferenceEquals(card.Loot, hero.Loot), "dummy loot is an independent copy of hero loot");
Check(card.Movement is UnitMovementStatic && card.InitEffects!.Count == 0 && card.EnabledEffects!.Count == 0, "stationary dummy has no passive regeneration");
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
var dummyData = (UnitDataPlayer)card.Data!;
var dummyClass = dummyData.Class.GetCard<CardHeroClass>();
Check(dummyData.Class != Key.None && dummyClass is not null,
    "shipped Catalogue browser can resolve the dummy class instead of throwing on card zero");
Check(dummyData.SpecialDevices!.Count == 0 && dummyClass!.AvailableDevices is { Count: 0 },
    "dummy contributes no shared or hero blocks to Catalogue availability");
Check(!CatalogueHelper.GetHeroes().Contains(DebugDummyRegistration.Key),
    "dummy remains outside the playable and draft hero roster");
using (var wire = new MemoryStream())
{
    card.Write(new BinaryWriter(wire)); wire.Position = 0;
    var received = new CardUnit(); received.Read(new BinaryReader(wire));
    var receivedData = (UnitDataPlayer)received.Data!;
    Check(receivedData.Class == DebugDummyRegistration.ClassKey &&
        receivedData.SpecialDevices!.Concat(receivedData.Class.GetCard<CardHeroClass>()!.AvailableDevices!).Count() == 0,
        "replicated player card supports the shipped block-browser traversal");
}
foreach (var text in new[]{"1","1000","10000000"}) Check(GameZone.TryParseDummyHealth([text], out _), "accept HP " + text);
foreach (var text in new[]{"0","-1","10000001","NaN","Infinity","1.5","999999999999",""}) Check(!GameZone.TryParseDummyHealth([text],out _), "reject HP " + text);
Check(!GameZone.TryParseDummyHealth([],out _) && !GameZone.TryParseDummyHealth(["1","2"],out _), "reject wrong argument count");
var ctor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var callbacks = ctor.GetParameters().Select(p => { var invoke=p.ParameterType.GetMethod("Invoke")!; return (object)Expression.Lambda(p.ParameterType,Expression.Default(invoke.ReturnType),invoke.GetParameters().Select(a=>Expression.Parameter(a.ParameterType,a.Name))).Compile(); }).ToArray();
var updater = (UnitUpdater)ctor.Invoke(callbacks);
List<Unit> spawned = []; int deaths = 0;
updater = updater with { OnUnitInit=(u,_)=>spawned.Add(u), OnUnitKilled=(_,_,_)=>deaths++, GetResourceCap=()=>10000 };
var player = new Unit(1,new UnitInit{Key=hero.Key,PlayerId=1,OwnerId=1,Team=TeamType.Team1,Transform=new ZoneTransform{Position=new Vector3(3,2,3)}},updater);
var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Set(string name,object value)=>typeof(GameZone).GetField(name,flags)!.SetValue(zone,value);
var data = (ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData));
data.BlocksData = new MapBinary(3,new byte[12*8*12*6].Zip(0).ToArray(),new Vector3s(12,8,12),-1,new MapUpdater((_,_)=>{},(_,_)=>{},_=>{},_=>true));
data.MapData = new MapData { Properties = new MapDataProps() };
Set("_zoneData",data);Set("_defaultUnitUpdater",updater);Set("_livingTestDummies",new Dictionary<TeamType,Unit>());
var motionField = typeof(GameZone).GetField("_testDummyMotion", flags)!;
motionField.SetValue(zone, Activator.CreateInstance(motionField.FieldType));
Set("_playerIdToUnitId",new Dictionary<uint,uint>{{1,1}});Set("_playerUnits",new Dictionary<uint,Unit>{{1,player}});
var air = new CardBlock{Id="block_air",BlockId=0,Passable=BlockPassableType.Any};
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Append(air).ToList());
spawned.Clear();
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["1000"]);
Check(spawned.Count==1,"real debug command spawns one target");
var dummy=spawned.Single();
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["1000"]);
Check(spawned.Count==2,"admin debug spawns are not limited to one living dummy");
spawned[1].TakeDamage(1000,player.GetSelfSource());spawned.RemoveAt(1);
Check(dummy.PlayerId==null && dummy.OwnerPlayerId==null && dummy.Team==TeamType.Team2,"dummy is an enemy without player identity");
Check(!dummy.GetInitData().Controlled && dummy.GetInitData().Gears!.SequenceEqual([DebugDummyRegistration.CombatGear]) &&
    dummy.GetUpdateData().CurrentGear == DebugDummyRegistration.CombatGear && dummy.CurrentGear?.Card.Prefab == "m60",
    "spawn and observer snapshots use Sarge's equipped combat rig and remote movement");
Check(dummy.GetUpdateData().Health==1000 && dummy.UnitMaxHealth(1)==1000,"chosen current and maximum HP agree");
Check(Vector3.Distance(dummy.Transform.Position,player.Transform.Position)==3,"dummy spawns three metres ahead");
var slow = new CardEffect{Id="dummy_test_slow",Effect=new ConstEffectBuff{Buffs=new(){{BuffType.RunSpeed,-.5f}}}};
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Concat(new Card[]{air,slow}).ToList());
dummy.AddEffects([new ConstEffectInfo(slow.Key)],player.Team,player.GetSelfSource());
Check(dummy.UnitMaxHealth(1)==1000 && dummy.GetUpdateData().Buffs![BuffType.HealthCap]==999,"max HP survives status effect changes and replication");
dummy.TakeDamage(400,player.GetSelfSource());Check(dummy.GetUpdateData().Health==600 && !dummy.IsDead,"damage subtracts from configured HP");
dummy.TakeDamage(600,player.GetSelfSource());Check(dummy.IsDead && deaths==2 && dummy.RespawnTime==null,"lethal damage kills once without scheduling respawn");
Check(spawned.Count==1,"death does not spawn a replacement");
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["10000000"]);
Check(spawned.Count==2 && spawned[1].GetUpdateData().Health==10000000 && spawned[1].UnitMaxHealth(1)==10000000,"manual respawn creates new target at maximum requested HP");
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["NaN"]);zone.ReceivedDebugCommand(999,"spawn_test_dummy",["100"]);
Check(spawned.Count==2,"invalid HP and absent players cannot spawn targets");
spawned[1].TakeDamage(10000000,player.GetSelfSource());
data.MatchEnded=true;zone.ReceivedDebugCommand(1,"spawn_test_dummy",["100"]);Check(spawned.Count==2,"finished match rejects spawning");
data.MatchEnded=false;
air.Passable=BlockPassableType.None;
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["100"]);
Check(spawned.Count==2,"blocked placement rejects spawning");
air.Passable=BlockPassableType.Any;
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["1"]);
Check(spawned.Count==3 && spawned[2].UnitMaxHealth(1)==1,"minimum HP creates a valid target");
spawned[2].TakeDamage(1,player.GetSelfSource());
Check(spawned[2].IsDead && spawned[2].RespawnTime==null,"minimum HP target dies without respawn");
// Non-admin custom request authorization and server-owned one-at-a-time limit.
var group=(CustomGamePlayerGroup)RuntimeHelpers.GetUninitializedObject(typeof(CustomGamePlayerGroup));
var groupInfo=new CustomGameInfo { Status=CustomGameStatus.Match };
typeof(CustomGamePlayerGroup).GetField("<GameInfo>k__BackingField",flags)!.SetValue(group,groupInfo);
Set("_gameInitiator",group);
zone.ReceivedCustomTestDummy(1,1000);Check(spawned.Count==3,"custom spawning is off by default");
groupInfo.TestDummies=true;
zone.ReceivedCustomTestDummy(999,1000);zone.ReceivedCustomTestDummy(1,0);zone.ReceivedCustomTestDummy(1,10000001);
Check(spawned.Count==3,"spectators/missing participants and invalid HP cannot spawn");
zone.ReceivedCustomTestDummy(1,1000);Check(spawned.Count==4,"enabled custom participant spawns without admin access");
zone.ReceivedCustomTestDummy(1,1000);Check(spawned.Count==4,"custom request cannot replace a living target");
var other=new Unit(2,new UnitInit{Key=hero.Key,PlayerId=2,OwnerId=2,Team=TeamType.Team2,Transform=player.Transform},updater);
Set("_playerIdToUnitId",new Dictionary<uint,uint>{{1,1},{2,2}});Set("_playerUnits",new Dictionary<uint,Unit>{{1,player},{2,other}});
var teammate1=new Unit(4,new UnitInit{Key=hero.Key,PlayerId=4,OwnerId=4,Team=TeamType.Team1,Transform=player.Transform},updater);
Set("_playerIdToUnitId",new Dictionary<uint,uint>{{1,1},{2,2},{4,4}});Set("_playerUnits",new Dictionary<uint,Unit>{{1,player},{2,other},{4,teammate1}});
int Dummies()=>spawned.Count(u=>u.IsTestDummy);var firstCustom=spawned.Last(u=>u.IsTestDummy);int before=Dummies();
zone.ReceivedCustomTestDummy(4,1000);Check(Dummies()==before,"one-per-team limit rejects a teammate's second dummy");
zone.ReceivedCustomTestDummy(2,1000);var otherDummy=spawned.Last();Check(Dummies()==before+1 && otherDummy.Team==TeamType.Team1,"the other team may keep its own living dummy");
otherDummy.TakeDamage(1000,other.GetSelfSource());firstCustom.TakeDamage(1000,player.GetSelfSource());
zone.ReceivedCustomTestDummy(1,1000);Check(Dummies()==before+2,"a team may spawn again once its dummy is dead");
spawned.Last().TakeDamage(1000,player.GetSelfSource());
Set("_gameInitiator",RuntimeHelpers.GetUninitializedObject(typeof(DummyGameInitiator)));
before=Dummies();zone.ReceivedCustomTestDummy(1,1000);Check(Dummies()==before,"non-custom matches reject requests");
Set("_gameInitiator",group);
// Real damage conversion and application, using the same critical flag sent by the client.
var match=new CardMatch { Id="dummy_fixture_match", FriendlyFire=new FriedlyFire { DirectDamageReduction=1 } };
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Concat(new Card[]{air,slow,match}).ToList());
data.MatchKey=match.Key;
zone.ReceivedCustomTestDummy(1,1000);var damageDummy=spawned.Last();
var convert=typeof(GameZone).GetMethod("ConvertToDamageData",flags)!;
DamageData Damage(bool crit)=>(DamageData)convert.Invoke(zone,new object?[]{new Damage {PlayerDamage=20},Vector3.Zero,Vector3.Zero,null,false,crit,2f,null})!;
damageDummy.TakeDamage(Damage(false),new ImpactData(),false,player,player.Team);
Check(damageDummy.GetUpdateData().Health==980,"body hit applies normal hero damage");
damageDummy.TakeDamage(Damage(true),new ImpactData{Crit=true},false,player,player.Team);
Check(damageDummy.GetUpdateData().Health==940,"headshot applies weapon critical multiplier to dummy HP");
var scoreRules=new ActionScoreRules();var scores=new ActionScoreLedger(scoreRules);
Set("_actionScoreRules",scoreRules);Set("_actionScores",scores);Set("_units",new Dictionary<uint,Unit>());
data.Phase=new ZonePhase{PhaseType=ZonePhaseType.Assault};
var scoreKill=typeof(GameZone).GetMethod("ScoreKill",flags)!;
scoreKill.Invoke(zone,new object?[]{damageDummy,player,true,true,Array.Empty<uint>(),null,null});
Check(scores.Total(1)==scoreRules.Kill+scoreRules.Headshot,"dummy kill awards normal kill and headshot score");
scoreKill.Invoke(zone,new object?[]{damageDummy,other,false,true,Array.Empty<uint>(),null,null});
Check(scores.Total(2)==0,"friendly dummy kill earns no action score");
var teammate=new Unit(3,new UnitInit{Key=hero.Key,PlayerId=3,OwnerId=3,Team=TeamType.Team1,Transform=player.Transform},updater);
Set("_playerUnits",new Dictionary<uint,Unit>{{1,player},{2,other},{3,teammate}});
var resourceKill=typeof(GameZone).GetMethod("AwardKillResources",flags)!;
resourceKill.Invoke(zone,new object?[]{damageDummy,player,true,false});
Check(player.Resource==50 && teammate.Resource==50 && other.Resource==0,"dummy kill pays normal killer and allied brick rewards");
resourceKill.Invoke(zone,new object?[]{damageDummy,other,false,false});
Check(other.Resource==0,"friendly dummy kill pays no bricks");
resourceKill.Invoke(zone,new object?[]{damageDummy,null,false,false});
Check(player.Resource==50 && teammate.Resource==50,"unattributed dummy death pays no rewards");

var terrain = new CardBlock { Id = "dummy_test_floor", BlockId = 1, Passable = BlockPassableType.None };
var bounce = new CardBlock { Id = "dummy_test_bounce", BlockId = 2, Passable = BlockPassableType.Any,
    Special = new BlockSpecialBounce { BounceForce = 6 } };
var stun = new CardEffect { Id = "dummy_test_stun", Duration = 3,
    Effect = new ConstEffectBuff { Buffs = new() { { BuffType.Root, 1 }, { BuffType.Disarm, 1 } } } };
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Concat(new Card[] { air, slow, match, terrain, bounce, stun }).ToList());
var map = data.BlocksData;
void SetBlock(Vector3s cell, CardBlock card)
{
    var block = map[cell];
    block.Id = card.BlockId;
}
for (var x = 0; x < 12; x++)
for (var z = 0; z < 12; z++)
    SetBlock(new Vector3s(x, 0, z), terrain);

var tick = typeof(GameZone).GetMethod("TickTestDummy", flags)!;
void Tick(int count = 1)
{
    for (var i = 0; i < count; i++) tick.Invoke(zone, [damageDummy]);
}
var start = damageDummy.Transform.Position;
Tick();
Check(damageDummy.Transform.IsJump && damageDummy.Transform.Position.Y < start.Y,
    "an unsupported spawn immediately begins falling with the airborne pose");
Tick(60);
var rest = damageDummy.Transform.Position;
Check(!damageDummy.Transform.IsJump && Math.Abs(rest.Y - 1) < 0.01f, "dummy lands on solid floor");
var lastMove = damageDummy.LastMoveUpdateTime;
Tick(30);
Check(rest == damageDummy.Transform.Position && lastMove == damageDummy.LastMoveUpdateTime,
    "idle dummy stays in place without sending movement");
Set("_units", new Dictionary<uint, Unit> { [damageDummy.Id] = damageDummy });
zone.ReceivedMoveRequest(damageDummy.Id, lastMove + 1, new ZoneTransform { Position = rest + Vector3.UnitX });
Check(damageDummy.Transform.Position == rest, "client movement cannot take over the dummy");

var octree = new BoundsOctreeEx<Unit>(12, new Vector3(6), 1, 1.2f);
octree.Add(damageDummy, new BoundingBox(damageDummy.GetMidpoint(), new Vector3(0.5f, 1.9f, 0.5f)));
Set("_unitOctree", octree);
var applyEffect = typeof(GameZone).GetMethod("ApplyInstEffect", flags)!;
void Knockback() => applyEffect.Invoke(zone, [player.GetSelfSource(), Array.Empty<Unit>(),
    new InstEffectKnockback { EffectRange = 5, Force = 8, MidairForce = 4 },
    new ImpactData { InsidePoint = rest - Vector3.UnitX * 2 }, null, null, null, true]);
damageDummy.AddEffects([new ConstEffectInfo(stun.Key)], player.Team, player.GetSelfSource());
Check(damageDummy.GetUpdateData().Effects?.Any(e => e.Key == stun.Key) == true &&
    damageDummy.IsBuff(BuffType.Root) && damageDummy.IsBuff(BuffType.Disarm), "native stun and both buffs replicate");
Knockback();
Tick();
Check(rest == damageDummy.Transform.Position, "root suppresses knockback through normal effect targeting");
damageDummy.PurgeEffects(false, true);
for (var y = 1; y < 8; y++)
for (var z = 0; z < 12; z++)
    SetBlock(new Vector3s(5, y, z), terrain);
Knockback();
Tick(4);
Check(damageDummy.Transform.IsJump && damageDummy.Transform.Position.X > rest.X &&
    damageDummy.Transform.Position.Y > rest.Y, "normal area knockback launches an unstunned dummy");
Tick(100);
rest = damageDummy.Transform.Position;
Check(!damageDummy.Transform.IsJump && rest.X < 5 && Math.Abs(rest.Y - 1) < 0.01f,
    "knocked-back dummy slides down the wall and lands");

var service = DispatchProxy.Create<IServiceZone, DummyZoneProxy>();
Set("_serviceZone", service);
var launchCell = (Vector3s)(rest + Vector3.UnitY * 0.01f);
SetBlock(launchCell, bounce);
Tick(4);
var events = ((DummyZoneProxy)(object)service).Bounces;
Check(damageDummy.Transform.IsJump && damageDummy.Transform.Position.Y > rest.Y && events.Count == 1 &&
    events[0].UnitId == damageDummy.Id, "a bounce block launches the remote dummy and emits the native pad event once");
SetBlock(launchCell, air);
Tick(130);
Check(!damageDummy.Transform.IsJump && Math.Abs(damageDummy.Transform.Position.Y - 1) < 0.01f,
    "gravity returns the dummy to the floor after a bounce");
rest = damageDummy.Transform.Position;
applyEffect.Invoke(zone, [player.GetSelfSource(), new[] { damageDummy },
    new InstEffectSlip { DirectionAngleRandom = 180, DistanceTo = 1.5f, Time = 0.5f, RotationTime = 0.25f },
    new ImpactData(), null, null, null, true]);
Tick(30);
Check(!damageDummy.Transform.IsJump && damageDummy.Transform.Position.Z < rest.Z - 1 &&
    Math.Abs(damageDummy.Transform.Rotation.y) == 1800, "a ground slip moves and turns the dummy");

rest = damageDummy.Transform.Position;
var onPull = typeof(GameZone).GetMethod("OnPull", flags)!;
onPull.Invoke(zone, [damageDummy, new ManeuverPull { Enabled = true, Force = 4, OriginPos = rest - Vector3.UnitX * 2 }]);
Tick(8);
var pulled = damageDummy.Transform.Position;
Check(pulled.X < rest.X - 0.3f, "native pull moves the dummy toward its source");
onPull.Invoke(zone, [damageDummy, new ManeuverPull { Enabled = false }]);
Tick(8);
Check(damageDummy.Transform.Position == pulled, "ending a pull clears its displacement");

var healthBeforeFall = damageDummy.GetUpdateData().Health;
damageDummy.UnitMove(new ZoneTransform { Position = pulled + Vector3.UnitY * 6.5f }, damageDummy.LastMoveUpdateTime + 1);
Tick(90);
Check(!damageDummy.Transform.IsJump && damageDummy.GetUpdateData().Health < healthBeforeFall,
    "a long drop applies ordinary fall damage");
for (var x = 0; x < 12; x++)
for (var z = 0; z < 12; z++)
    SetBlock(new Vector3s(x, 0, z), air);
Tick();
Check(damageDummy.Transform.IsJump && damageDummy.Transform.Position.Y < 1, "destroying support restarts gravity");
Console.WriteLine($"PASS debug dummy: {checks} checks");
if (args is ["--export-client-fixture", var output])
{
    using var writer = new BinaryWriter(File.Create(output));
    writer.Write(2);
    Card.WriteVariant(writer, dummyClass!);
    Card.WriteVariant(writer, card);
}

public class DummyZoneProxy : DispatchProxy
{
    public List<ZoneEventJumpPadUsed> Bounces { get; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (args is [ZoneEventJumpPadUsed bounce]) Bounces.Add(bounce);
        return null;
    }
}
