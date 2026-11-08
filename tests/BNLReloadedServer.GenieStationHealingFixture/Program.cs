// Real candidate catalogue: orb/station parity, shared healing, handoff and unrelated heals.
// Real live catalogue (args[0]) and real units; buffs are read back from the unit after each change.
using System.Collections.Concurrent;
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

var catalogue = (ServerCatalogue)Databases.Catalogue;
var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
catalogue.Replicate(cards);
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(16, 8, 16),
    BlocksData = new byte[16 * 8 * 16 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(2, 4, 2) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(13, 4, 13) }]
};
var players = new ConcurrentDictionary<uint, PlayerLobbyState>
{
    [1] = new() { PlayerId = 1, Team = TeamType.Team1, Hero = new Key("unit_hero_kira"), Nickname = "kira" },
    [2] = new() { PlayerId = 2, Team = TeamType.Team1, Hero = new Key("unit_hero_doc_eliza"), Nickname = "eliza" }
};
var zone = new GameZone(Stub<IServiceZone>(), Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = new Key("game_mode_friendly"), ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] a) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, a);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
}
ZoneTransform At(Vector3 pos) => new() { Position = pos, Rotation = Vector3s.Zero, LocalVelocity = Vector3s.Zero };

var (kira, eliza) = OnZone(() => ((Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!, (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!));
var station = OnZone(() => (Unit)Call("CreateUnit", catalogue.GetCard<CardUnit>(new Key("unit_device_generic_heal_station"))!,
    At(new Vector3(5.5f, 4.5f, 5.5f)), eliza, null, false, false)!);

var updater = (UnitUpdater)typeof(GameZone).GetField("_defaultUnitUpdater", Any)!.GetValue(zone)!;
Unit Source(uint id, string key, uint? owner=2) => new(id, new UnitInit { Key=new Key(key), OwnerId=owner, Team=TeamType.Team1 }, updater);
var normal=Source(1001,"unit_projectile_djinn_heavy_orb");
var speed=Source(1002,"unit_projectile_djinn_heavy_orb_speed_ball",1);
var generic=new ConstEffectInfo(CatalogueHelper.GenericRegenKey);
var auraKey=new Key(GenieHealingVisualRegistration.AuraId);
var aura=(ConstEffectAura)catalogue.GetCard<CardEffect>(auraKey)!.Effect!;
var heal=aura.IntervalEffects!.OfType<InstEffectHeal>().Single();
var apply=typeof(Unit).GetMethod("ApplyAuraInterval",Any)!;
long Tick(double seconds)=>(long)Math.Round(seconds*System.Diagnostics.Stopwatch.Frequency);
void Orb(Unit source, double seconds, Key? key=null) => apply.Invoke(source,[key??auraKey,aura.Interval,new[]{kira},heal,Tick(seconds)]);
float Hp()=> (float)typeof(Unit).GetField("_health",Any)!.GetValue(kira)!;
void Set(float hp=10)=>kira.UpdateData(new UnitUpdate { Health=hp });
void HpIs(float hp,string label)=>Check(Math.Abs(Hp()-hp)<.001f,label+" (HP="+Hp()+")");
OnZone(()=> {
    Check(heal.PlayerHeal==2 && aura.Interval==.2f,"real normal/Speed Ball shared aura heals 2 HP every .2 seconds");
    Check(((ConstEffectBuff)catalogue.GetCard<CardEffect>(CatalogueHelper.GenericRegenKey)!.Effect!).Buffs![BuffType.HealthRegen]==10,"station reference is 10 HP/s");
    Set();for(int i=0;i<5;i++){Orb(normal,10+i*.2);Orb(speed,10+i*.2+.05);}
    HpIs(20,"five ticks from overlapping normal/Speed Ball orbs heal 10 HP, not 20");
    Set();kira.AddEffects([generic],station.Team,station.GetSelfSource());
    for(int i=0;i<5;i++){Orb(normal,20+i*.2);Orb(speed,20+i*.2+.05);kira.ApplyBuffEffects(.2f);}
    HpIs(20,"station plus two staggered orbs heals 10 HP/s, not 30");
    kira.RemoveEffects([generic],station.Team,station.GetSelfSource());
    Orb(speed,22);HpIs(22,"remaining Speed Ball orb resumes after leaving station");
    Orb(normal,22.05);HpIs(22,"another owner cannot stack during takeover");
    kira.AddEffects([generic],station.Team,station.GetSelfSource());
    Orb(speed,22.2);HpIs(22,"entering station suppresses previously selected orb");
    kira.RemoveEffects([generic],station.Team,station.GetSelfSource());
    // All cosmetic variants and map-owned stations must have identical suppression.
    int index=0;
    foreach(var card in cards.OfType<CardUnit>().Where(c=>c.Id?.StartsWith("unit_device_generic_heal_station")==true)) {
        var supply=Source((uint)(2000+index),card.Id!,index%2==0?null:2u);Set();
        kira.AddEffects([generic],supply.Team,supply.GetSelfSource());
        Orb(normal,30+index);HpIs(10,"orb suppressed by "+card.Id);
        kira.ApplyBuffEffects(.2f);HpIs(12,"station still heals normally: "+card.Id);
        kira.RemoveEffects([generic],supply.Team,supply.GetSelfSource());index++;
    }
    Check(index>=6,"covered stock, cosmetics and map station cards");
    Set();var mapStation=Source(3000,"unit_device_generic_heal_station",null);
    kira.AddEffects([generic],station.Team,station.GetSelfSource());kira.AddEffects([generic],mapStation.Team,mapStation.GetSelfSource());
    kira.RemoveEffects([generic],station.Team,station.GetSelfSource());Orb(normal,60);HpIs(10,"removing one of two stations keeps suppression");
    kira.ApplyBuffEffects(1);HpIs(20,"multiple stations remain one 10 HP/s regen");
    kira.RemoveEffects([generic],mapStation.Team,mapStation.GetSelfSource());Orb(normal,61);HpIs(22,"orb resumes after final station is gone");
    // Same generic effect with a non-station source must not be mistaken for station healing.
    Set();kira.AddEffects([generic],eliza.Team,eliza.GetSelfSource());Orb(normal,70);kira.ApplyBuffEffects(.2f);HpIs(14,"non-station generic regen still stacks with orb");
    kira.RemoveEffects([generic],eliza.Team,eliza.GetSelfSource());
    var doc=new ConstEffectInfo(new Key("effect_status_regen_health_doc_eliza_others"));
    Set();kira.AddEffects([doc],eliza.Team,eliza.GetSelfSource());Orb(normal,71);kira.ApplyBuffEffects(.2f);HpIs(14,"Doc healing remains additive");kira.RemoveEffects([doc],eliza.Team,eliza.GetSelfSource());
    Set();kira.AddEffects([generic],station.Team,station.GetSelfSource());Orb(normal,80,new Key("fixture_other_aura"));HpIs(12,"station does not suppress unrelated instant healing auras");kira.RemoveEffects([generic],station.Team,station.GetSelfSource());
    float maximum=kira.UnitMaxHealth(kira.UnitCard!.Health!.Health!.MaxHealth);Set(maximum-1);Orb(normal,90);HpIs(maximum,"orb respects health cap");
    return true;
});
zone.Stop();
Console.WriteLine($"BNL_GENIE_STATION_HEALING_FIXTURE_OK checks={checks}");
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
