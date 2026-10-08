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

if (args.Length == 1)
{
    using var file = File.OpenRead(args[0]);
    using var unzip = new System.IO.Compression.ZLibStream(file, System.IO.Compression.CompressionMode.Decompress);
    using var reader = new BinaryReader(unzip);
    reader.ReadByte();
    var cards = reader.ReadList<Card, List<Card>>(Card.ReadVariant);
    foreach (var card in cards.OfType<CardUnit>().Where(c => c.Id!.Contains("heal") || c.Id.Contains("health") || c.Id.Contains("globe") || c.Id.Contains("staff")))
        Console.WriteLine($"{card.Id} data={card.Data?.GetType().Name} minimap={card.MinimapType} labels={string.Join(',',card.Labels ?? [])}");
    return;
}

const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var brains = new CardHeroClass { Id = "fixture_brains", Type = HeroClassType.Brains };
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
    brains, gear, hero, orb, sprayImpact, stationRegen, staffRegen, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_ha_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Class = brains.Key;
((UnitDataPlayer)hero.Data!).Gears = [gear.Key];

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
bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;


void Reset(float hp = 20) => OnZone(() => { healer.UpdateData(new UnitUpdate { Health = hp }); return true; });
float HP(Unit u) => u.HealthPercentage * 100;
Reset(); Heal(new UnitSource(healer), teammate, 20);
Check(Near(HP(healer), 30), "direct ally healing returns 50 percent");
foreach (var percent in new[] {25f,75f,50f}) {
    OnZone(()=>{
        var catalogue=(ServerCatalogue)Databases.Catalogue;
        var tuned=new CardPerk { Id=ClassPerkCatalogue.BrainsId, SlotType=PerkSlotType.Class, ClassPerk=new ClassPerkBalance { HealingReturnPercent=percent } };
        catalogue.Replicate(catalogue.All.Where(c=>c.Id!=tuned.Id).Append(tuned).ToList());
        return true;
    });
    Reset(); Heal(new UnitSource(healer), teammate, 20);
    Check(Near(HP(healer),20+20*percent/100),$"CDB healing return {percent} percent");
}

Reset(); Heal(new UnitSource(healer), teammate, 20, startHealth: 95);
Check(Near(HP(healer), 22.5f), "overheal returns only half actual health restored");
Reset(); Heal(new UnitSource(healer), teammate, 20, startHealth: 100);
Check(Near(HP(healer), 20), "full health ally grants nothing");
Reset(99); Heal(new UnitSource(healer), teammate, 20);
Check(Near(HP(healer), 100), "self recovery caps at maximum health");
Heal(new UnitSource(healer), healer, 20, startHealth: 20);
Check(Near(HP(healer), 40), "self healing does not trigger recovery or recurse");
Reset(); Heal(new BlockSource(new Vector3s(4,4,4), new Block()), teammate, 20);
Check(Near(HP(healer), 20), "placed block healing excluded");
Unit Owned() => OnZone(() => {
    var before = units.Keys.ToHashSet(); var pos = new Vector3(6,8,6);
    Call("CreateProjectileUnit", orb.Key, 0f, new ShotData { TargetPos = pos + Vector3.UnitX }, pos, healer);
    return units.Values.Single(u => !before.Contains(u.Id) && u.Key == orb.Key);
});
var projectile = Owned();
Reset(); Heal(new UnitSource(projectile), teammate, 20);
Check(Near(HP(healer),30), "owned healing projectile qualifies");
orb.Labels = [UnitLabel.HealthSupply];
Reset(); Heal(new UnitSource(projectile), teammate, 20);
Check(Near(HP(healer),20), "health station label excluded even on a projectile");
orb.Labels = null; orb.MinimapType = UnitMinimapType.HealthSupply;
Reset(); Heal(new UnitSource(projectile), teammate,20);
Check(Near(HP(healer),20), "health supply minimap classification excluded");
orb.MinimapType = null;
var projectileData = orb.Data; orb.Data = new UnitDataCommon();
Reset(); Heal(new UnitSource(projectile), teammate,20);
Check(Near(HP(healer),30), "non-station ability entities qualify");
orb.Labels = [UnitLabel.HealthSupply];
Reset(); Heal(new UnitSource(projectile), teammate,20);
Check(Near(HP(healer),20), "placed station device excluded");
orb.Data = projectileData; orb.Labels = null;
var savedId = orb.Id; orb.Id="unit_pickup_medikit_heal_station_lvl01";
Reset(); Heal(new UnitSource(projectile),teammate,20);
Check(Near(HP(healer),20), "station medikit without health-supply label is excluded");
orb.Id=savedId;
brains.Type=HeroClassType.Brawn;
Reset(); Heal(new UnitSource(healer), teammate,20);
Check(Near(HP(healer),20), "Brawn does not receive Brains perk");
brains.Type=HeroClassType.Skills;
Reset(); Heal(new UnitSource(healer), teammate,20);
Check(Near(HP(healer),20), "Skill does not receive Brains perk");
brains.Type=HeroClassType.Brains;
Reset(0); healer.IsDead = true; Heal(new UnitSource(projectile), teammate,20);
Check(Near(HP(healer),0), "lingering projectile cannot resurrect its healer");
healer.IsDead = false;
Reset(); Heal(new UnitSource(thirdHealer), teammate,20);
Check(Near(HP(healer),20), "unrelated source cannot credit this healer");
OnZone(() => { teammate.UpdateData(new UnitUpdate { Team=TeamType.Team2 }); return true; });
Reset(); Heal(new UnitSource(healer), teammate,20);
Check(Near(HP(healer),20), "enemy healing excluded");
OnZone(() => { teammate.UpdateData(new UnitUpdate { Team=TeamType.Team1 }); return true; });
Reset(); healer.AssignFreeForAllTeam(1);
Heal(new UnitSource(healer),teammate,20);
Check(Near(HP(healer),20), "FFA players are not teammates even with equal team colors");
healer.AssignFreeForAllTeam(null);
healer.IsActive=false; Heal(new UnitSource(healer),teammate,20);
Check(Near(HP(healer),20), "inactive healer cannot receive recovery");
healer.IsActive=true;
Reset();
// Shared regen effect applied by both a station and hero: split actual gain by origin, not owner.
OnZone(() => {
    teammate.UpdateData(new UnitUpdate { Health=90 });
    teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)],TeamType.Team1,
        new BlockSource(new Vector3s(4,4,4),new Block(),healer.CreateImpactData(casterPlayerId:healer.PlayerId)));
    teammate.AddEffects([new ConstEffectInfo(stationRegen.Key)],TeamType.Team1,
        new UnitSource(healer,healer.CreateImpactData(casterPlayerId:healer.PlayerId)));
    teammate.ApplyBuffEffects(1f);return true;
});
Check(Near(HP(teammate),100), "shared regeneration restores actual missing health");
Check(Near(HP(healer),22.5f), "same-owner station and hero regen split fairly, station share excluded");
zone.Stop();
Console.WriteLine($"BNL_BRAINS_RECOVERY_FIXTURE_OK checks={checks}");
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
