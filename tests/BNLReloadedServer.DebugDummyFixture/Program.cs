using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

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
Check(card.Health!.Health!.HealthType == HealthType.Player && card.Data is UnitDataPlayer, "dummy receives player damage and player-target effects");
Check(card.Health.Health.MaxHealth == 1 && card.Health.KillReward == null && card.Loot == null && card.SpawnPoint == null && card.Capturing == null, "dummy has no loot, reward, spawn or capture features");
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
updater = updater with { OnUnitInit=(u,_)=>spawned.Add(u), OnUnitKilled=(_,_,_)=>deaths++ };
var player = new Unit(1,new UnitInit{Key=hero.Key,PlayerId=1,OwnerId=1,Team=TeamType.Team1,Transform=new ZoneTransform{Position=new Vector3(3,2,3)}},updater);
var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Set(string name,object value)=>typeof(GameZone).GetField(name,flags)!.SetValue(zone,value);
var data = (ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData));
data.BlocksData = new MapBinary(3,new byte[12*8*12*6].Zip(0).ToArray(),new Vector3s(12,8,12),-1,new MapUpdater((_,_)=>{},(_,_)=>{},_=>{},_=>true));
Set("_zoneData",data);Set("_defaultUnitUpdater",updater);
Set("_playerIdToUnitId",new Dictionary<uint,uint>{{1,1}});Set("_playerUnits",new Dictionary<uint,Unit>{{1,player}});
var air = new CardBlock{Id="block_air",BlockId=0,Passable=BlockPassableType.Any};
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Append(air).ToList());
spawned.Clear();
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["1000"]);
Check(spawned.Count==1,"real debug command spawns one target");
var dummy=spawned.Single();
Check(dummy.PlayerId==null && dummy.OwnerPlayerId==null && dummy.Team==TeamType.Team2,"dummy is an enemy without player identity");
Check(dummy.GetUpdateData().Health==1000 && dummy.UnitMaxHealth(1)==1000,"chosen current and maximum HP agree");
Check(Vector3.Distance(dummy.Transform.Position,player.Transform.Position)==3,"dummy spawns three metres ahead");
var slow = new CardEffect{Id="dummy_test_slow",Effect=new ConstEffectBuff{Buffs=new(){{BuffType.RunSpeed,-.5f}}}};
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Concat(new Card[]{air,slow}).ToList());
dummy.AddEffects([new ConstEffectInfo(slow.Key)],player.Team,player.GetSelfSource());
Check(dummy.UnitMaxHealth(1)==1000 && dummy.GetUpdateData().Buffs![BuffType.HealthCap]==999,"max HP survives status effect changes and replication");
dummy.TakeDamage(400,player.GetSelfSource());Check(dummy.GetUpdateData().Health==600 && !dummy.IsDead,"damage subtracts from configured HP");
dummy.TakeDamage(600,player.GetSelfSource());Check(dummy.IsDead && deaths==1 && dummy.RespawnTime==null,"lethal damage kills once without scheduling respawn");
Check(spawned.Count==1,"death does not spawn a replacement");
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["10000000"]);
Check(spawned.Count==2 && spawned[1].GetUpdateData().Health==10000000 && spawned[1].UnitMaxHealth(1)==10000000,"manual respawn creates new target at maximum requested HP");
zone.ReceivedDebugCommand(1,"spawn_test_dummy",["NaN"]);zone.ReceivedDebugCommand(999,"spawn_test_dummy",["100"]);
Check(spawned.Count==2,"invalid HP and absent players cannot spawn targets");
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
Console.WriteLine($"PASS debug dummy: {checks} checks");
if (args is ["--export-client-fixture", var output])
{
    using var writer = new BinaryWriter(File.Create(output));
    writer.Write(2);
    Card.WriteVariant(writer, dummyClass!);
    Card.WriteVariant(writer, card);
}
