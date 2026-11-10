// Every heal on a player with a player behind it (hero, projectile, their station or block) sends a card-less
// impact naming that player, with the amount in ShotPos.X, so clients can show who is healing them and how much,
// several healers at once. Drives GameZone.ApplyInstEffect and Unit.ApplyBuffEffects with real units and
// records SendImpact.
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
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

var gear = new CardGear { Id = "fixture_ha_gear" };
var hero = new CardUnit
{
    Id = "fixture_ha_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } }
};
var orb = new CardUnit
{
    Id = "fixture_ha_orb", Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center, Lifetime = 60,
    Health = new UnitHealth { Health = new Health { MaxHealth = 50, HealthType = HealthType.World } },
    Data = new UnitDataProjectile { MaxSpeed = 0, Acceleration = 0, TriggerRadius = 0.1f, CollideWith = RelativeTeamType.Friendly }
};
var sprayImpact = new CardImpact { Id = "fixture_ha_spray" };
CardEffect Regen(string id, float rate) => new()
{
    Id = id, Positive = true,
    Effect = new ConstEffectBuff { Buffs = new Dictionary<BuffType, float> { [BuffType.HealthRegen] = rate } }
};
var stationRegen = Regen("fixture_ha_station_regen", 10f);
var staffRegen = Regen("fixture_ha_staff_regen", 5f);
var mode = new CardGameMode { Id = "game_mode_friendly" };
((ServerCatalogue)Databases.Catalogue).Replicate([
    gear, hero, orb, sprayImpact, stationRegen, staffRegen, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_ha_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Gears = [gear.Key];

var sourceCards = System.Text.Json.JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
var relevant = sourceCards.Where(c => c.Id is "effect_perk_fast_restock_pos" or "effect_perk_fast_restock_neg" or "effect_perk_health_scavenger_pos" or "effect_perk_health_scavenger_neg" or "perk_def_fast_restock" or "shop_item_perk_def_fast_restock").ToList();
var catalogue = (ServerCatalogue)Databases.Catalogue;
relevant.Add(new CardEffect { Id="fixture_resource_healing", Effect=new ConstEffectBuff { Buffs=new() { [BuffType.MiningHealthRefill]=1, [BuffType.SupplyHealth]=1 } } });
relevant.Add(new CardEffect { Id="fixture_healing_reduction", Effect=new ConstEffectBuff { Buffs=new() { [BuffType.HealthGain]=-.6f } } });
catalogue.Replicate(catalogue.All.Concat(relevant).ToList());
var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team1), (3u, TeamType.Team1) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero.Key, Nickname = $"p{id}" };
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(16, 16, 16),
    BlocksData = new byte[16 * 16 * 16 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(4, 4, 4) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(12, 4, 12) }]
};
var service = Stub<IServiceZone>();
var zone = new GameZone(service, Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] args) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, args);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
var units = (IDictionary<uint, Unit>)typeof(GameZone).GetField("_units", Any)!.GetValue(zone)!;
var (healer, teammate) = OnZone(() => ((Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!, (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!));
var thirdHealer = OnZone(() => (Unit)Call("CreatePlayerUnit", 3u, Stub<IServiceZone>())!);
var calls = ((StubProxy)(object)service).Calls;

// Applies one heal tick and returns the impacts it sent.
List<ImpactData> Heal(EffectSource source, Unit target, float amount, Key? impact = null, float startHealth = 40f)
{
    return OnZone(() =>
    {
        target.UpdateData(new UnitUpdate { Health = startHealth });
        calls.Clear();
        var effect = new InstEffectHeal { PlayerHeal = amount, Impact = impact };
        var impactData = new ImpactData { CasterPlayerId = healer.PlayerId, CasterUnitId = healer.Id, SourceKey = gear.Key, HitUnits = [] };
        Call("ApplyInstEffect", source, new[] { target }, effect, impactData, null, null, null, true);
        return calls.Where(c => c.Name == "SendImpact").Select(c => (ImpactData)c.Args[0]!).ToList();
    });
}
bool Near(float a, float b) => Math.Abs(a - b) < .01f;
float HP(Unit u) => u.HealthPercentage * u.UnitMaxHealth(100);
var updater = (UnitUpdater)typeof(GameZone).GetField("_defaultUnitUpdater", Any)!.GetValue(zone)!;
uint next = 100;
Unit Source(string id, UnitLabel? label = null, UnitData? data = null)
{
    var card = new CardUnit { Id = id, Data = data, Labels = label.HasValue ? [label.Value] : [] };
    catalogue.Replicate(catalogue.All.Append(card).ToList());
    return new Unit(next++, new UnitInit { Key = card.Key, Team = TeamType.Team1 }, updater);
}
var station = Source("fixture_station", UnitLabel.HealthSupply);
var ammoStation = Source("fixture_ammo", UnitLabel.AmmmoSupply);
var medkit = Source("unit_pickup_medikit_heal_station", data: new UnitDataPickup());
var enemyMedkit = Source("unit_pickup_medikit_heal_station_enemy", data: new UnitDataPickup());
var lamp = Source("unit_pickup_medikit_djinn_lamp", data: new UnitDataPickup());
var dropped = Source("fixture_dropped", data: new UnitDataPickup());
var sources = new (string Name, EffectSource Source, bool Station)[] {
    ("station", new UnitSource(station), true), ("ammo station", new UnitSource(ammoStation), true),
    ("station medkit", new UnitSource(medkit), true), ("enemy station medkit", new UnitSource(enemyMedkit), true),
    ("lamp pickup", new UnitSource(lamp), false), ("dropped health", new UnitSource(dropped), false),
    ("hero ability/tool", healer.GetSelfSource(healer.CreateImpactData()), false), ("self", new UnitSource(teammate), false),
    ("block", new BlockSource(new Vector3s(1,1,1), new Block()), false) };
void Reset(bool fast, bool scavenger)
{
    teammate.UpdateData(new UnitUpdate { Effects = new Dictionary<Key,ulong?>(), Health = 20 });
    ((System.Collections.IDictionary)typeof(Unit).GetField("_effectSources",Any)!.GetValue(teammate)!).Clear();
    var ids = new List<string>();
    if (fast) ids.AddRange(["effect_perk_fast_restock_pos", "effect_perk_fast_restock_neg"]);
    if (scavenger) ids.AddRange(["effect_perk_health_scavenger_pos", "effect_perk_health_scavenger_neg"]);
    teammate.AddEffects(ids.Select(id => new ConstEffectInfo(Catalogue.Key(id))).ToList(), TeamType.Team1, teammate.GetSelfSource());
}
foreach (var (fast, scavenger) in new[] { (false,false), (true,false), (false,true), (true,true) })
{
    foreach (var (name, source, isStation) in sources)
    {
        OnZone(() => { Reset(fast, scavenger); return true; });
        Heal(source, teammate, 10, startHealth:20);
        Check(Near(HP(teammate), 20 + 10 * (1 + (scavenger ? .37f : 0) + (fast && isStation ? .15f : 0))), $"instant {name} fast={fast} scavenger={scavenger}");
    }
    OnZone(() => {
        Reset(fast, scavenger);
        Check(Near(teammate.GetBuff(BuffType.HealthGain), scavenger ? .37f : 0), "global gain excludes Fast Restock");
        Check(Near(teammate.GetBuff(BuffType.AmmoGain), fast ? .15f : 0), "ammo bonus retained");
        Check(Near(teammate.GetBuff(BuffType.ResourceBonus), fast ? -.15f : 0), "brick penalty retained");
        Check(Near(teammate.UnitMaxHealth(100), scavenger ? 90 : 100), "Scavenger health penalty retained");
        Check(Near(teammate.AddHealth(10), 10 * (scavenger ? 1.37f : 1)), "source-less recovery/lifesteal common path");
        Reset(fast, scavenger);
        teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)], TeamType.Team1, new UnitSource(station));
        teammate.AddEffects([new ConstEffectInfo(staffRegen.Key)], TeamType.Team1, healer.GetSelfSource(healer.CreateImpactData()));
        calls.Clear();
        teammate.ApplyBuffEffects(1);
        float expected = 15 * (scavenger ? 1.37f : 1) + (fast ? 1.5f : 0);
        Check(Near(HP(teammate),20 + expected), $"mixed station/hero regen scales only station contribution actual={HP(teammate)} expected={20+expected}");
        var impacts = calls.Where(c=>c.Name=="SendImpact").Select(c=>(ImpactData)c.Args[0]!).ToList();
        Check(Near(impacts.Sum(i=>i.ShotPos.X),expected), "mixed regen attribution sums to actual healing");
        Check(Near(impacts.Single(i=>i.CasterUnitId==station.Id).ShotPos.X, 10 * (1 + (scavenger ? .37f : 0) + (fast ? .15f : 0))), "station attribution uses boosted share");
        Reset(fast, scavenger);
        // One shared effect key supplied by a station AND a hero must retain distinct origins.
        teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)], TeamType.Team1, new UnitSource(station));
        teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)], TeamType.Team1, healer.GetSelfSource(healer.CreateImpactData()));
        teammate.ApplyBuffEffects(1);
        Check(Near(HP(teammate),20 + 10 * (scavenger ? 1.37f : 1) + (fast ? .75f : 0)), "shared regen key does not boost hero half");
        Reset(fast, scavenger);
        teammate.AddEffects([new ConstEffectInfo(Catalogue.Key("fixture_resource_healing"))],TeamType.Team1,teammate.GetSelfSource());
        teammate.AddResource(10, ResourceType.Mining);
        Check(Near(HP(teammate),20 + 10 * (fast ? .85f : 1) * (scavenger ? 1.37f : 1)), "mining healing gets global gain, retains brick penalty");
        teammate.UpdateData(new UnitUpdate { Health=20 });
        teammate.AddSupplies(10);
        Check(Near(HP(teammate),20 + 10 * (scavenger ? 1.37f : 1)), "supply healing gets global gain only");
        Reset(fast, scavenger);
        teammate.AddEffects([new ConstEffectInfo(Catalogue.Key("fixture_healing_reduction"))],TeamType.Team1,teammate.GetSelfSource());
        Check(Near(teammate.AddHealth(10),10 * (.4f + (scavenger ? .37f : 0))), "healing reduction affects non-station gain");
        Check(Near(teammate.AddHealth(10,out _,new UnitSource(station)),10 * (.4f + (scavenger ? .37f : 0) + (fast ? .15f : 0))), "healing reduction and station gain combine once");
        Reset(fast, scavenger);
        teammate.UpdateData(new UnitUpdate { Health = teammate.UnitMaxHealth(100)-1 });
        Check(Near(teammate.AddHealth(100,out _,new UnitSource(station)),1), "overheal capped");
        return true;
    });
}
OnZone(() => {
    Reset(true,false);
    teammate.RemoveEffects([new ConstEffectInfo(Catalogue.Key("effect_perk_fast_restock_pos"))],TeamType.Team1,teammate.GetSelfSource());
    Check(Near(teammate.AddHealth(10,out _,new UnitSource(station)),10), "unequipped perk no longer boosts station");
    return true;
});
zone.Stop();
catalogue.Replicate(sourceCards);
foreach (var id in new[] { "perk_def_fast_restock", "shop_item_perk_def_fast_restock" })
{
    var incoming=System.Text.Json.JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!.Single(c=>c.Id==id); catalogue.UpdateCard(incoming);
    var text = incoming is CardPerk p ? p.Description!.Text : ((CardShopItem)incoming).Description!.Text;
    Check(text!.Contains("healing from health and ammo stations"), "corrected tooltip survives catalogue refresh");
}
Console.WriteLine($"HEALING_PERK_SOURCES PASS {checks} checks");

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

