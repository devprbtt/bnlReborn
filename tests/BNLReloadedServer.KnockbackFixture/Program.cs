using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
var json = File.ReadAllText(args[0]);
var cards = json.Deserialize<List<Card>>()!;
var probeImpact = new Key("impact_knockback_fixture");
cards.Add(new CardImpact { Id = "impact_knockback_fixture", Key = probeImpact });
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
var effects = new List<(string Card, InstEffectKnockback Effect)>();
void Walk(JsonNode? node, string card)
{
    if (node is JsonObject obj)
    {
        if (obj["type"]?.ToString() == "knockback")
            effects.Add((card, obj.ToJsonString().Deserialize<InstEffectKnockback>()!));
        foreach (var property in obj) Walk(property.Value, card);
    }
    else if (node is JsonArray array) foreach (var child in array) Walk(child, card);
}
foreach (var card in JsonNode.Parse(json)!.AsArray()) Walk(card, card!["_id"]!.ToString());
Check(effects.Count > 0, "catalogue contains knockback effects");
foreach (var name in new[] { "saucer", "rocket", "kreepy" })
    Check(effects.Any(e => e.Card.Contains(name)), "catalogue covers " + name);
Console.WriteLine("CATALOGUE " + effects.Count + " knockback definitions in " + effects.Select(e => e.Card).Distinct().Count() + " cards");
foreach (var id in effects.Select(e => e.Card).Distinct().Where(id => id.Contains("saucer") || id.Contains("rocket") || id.Contains("kreepy")))
    Console.WriteLine("WEAPON " + id);

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
for (uint id = 1; id <= 4; id++) players[id] = new PlayerLobbyState { PlayerId = id,
    Team = id == 3 ? TeamType.Team2 : TeamType.Team1, Hero = new Key("unit_hero_hunter"), Nickname = "fixture" + id };
var map = new MapData { Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(64, 32, 64),
    BlocksData = new byte[64 * 32 * 64 * 6].Zip(0).ToArray(), SpawnPoints = [
        new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(20, 4, 20) },
        new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(50, 4, 50) }] };
var service = Stub<IServiceZone>();
var calls = ((StubProxy)(object)service).Calls;
var zone = new GameZone(service, Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = new Key("game_mode_friendly"), ["get_GameInstanceId"] = "knockback-fixture" }), players);
object? Call(string name, params object?[] values) => typeof(GameZone).GetMethod(name, Hidden)!.Invoke(zone, values);
T OnZone<T>(Func<T> action)
{
    var result = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { result.SetResult(action()); } catch (Exception e) { result.SetException(e); } });
    return result.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
}
try
{
    OnZone(() =>
    {
        var units = players.Keys.Order().Select(id => (Unit)Call("CreatePlayerUnit", id, Stub<IServiceZone>())!).ToArray();
        var caster = units[0]; var ally = units[1]; var enemy = units[2]; var distant = units[3];
        var data = (ZoneData)typeof(GameZone).GetField("_zoneData", Hidden)!.GetValue(zone)!;
        data.Phase = new ZonePhase { PhaseType = ZonePhaseType.Assault };
        var origin = new Vector3(20, 4, 20);
        var octree = typeof(GameZone).GetField("_unitOctree", Hidden)!.GetValue(zone)!;
        void Place(Unit unit, Vector3 position)
        {
            octree.GetType().GetMethod("Remove", [typeof(Unit)])!.Invoke(octree, [unit]);
            unit.Transform.Position = position; Call("AddUnitToOctree", unit, unit.Transform);
        }
        Place(caster, origin); Place(ally, origin); Place(enemy, origin); Place(distant, origin + new Vector3(30, 0, 0));
        void Apply(InstEffectKnockback effect, EffectSource? source = null)
        {
            calls.Clear();
            Call("ApplyInstEffect", source ?? new UnitSource(caster), units, effect,
                new ImpactData { InsidePoint = origin }, null, null, null, true);
        }
        uint[] Hit() => calls.Where(c => c.Name == "SendUnitManeuver" && c.Args[1] is ManeuverKnockback).Select(c => (uint)c.Args[0]!).Order().ToArray();
        foreach (var (card, effect) in effects)
        {
            Apply(effect);
            Check(!Hit().Contains(ally.Id), card + ": never knocks back teammate");
            Check(!Hit().Contains(distant.Id), card + ": never knocks back distant teammate");
        }
        var probe = new InstEffectKnockback { EffectRange = 5, Force = 20, MidairForce = 12, AffectCaster = true,
            Impact = probeImpact, Interrupt = new EffectInterrupt { Recall = true } };
        caster.IsRecall = ally.IsRecall = enemy.IsRecall = true;
        Apply(probe);
        Check(Hit().SequenceEqual(new[] { caster.Id, enemy.Id }.Order()), "unrestricted splash affects enemy and self only");
        Check(ally.IsRecall && !enemy.IsRecall, "ally recall is preserved; enemy recall interrupted");
        Check(!calls.Any(c => c.Name == "SendDoCancelRecall" && (uint)c.Args[0]! == ally.Id), "no ally cancel-recall packet");
        var impact = (ImpactData)calls.Single(c => c.Name == "SendImpact").Args[0]!;
        Check(impact.HitUnits!.Order().SequenceEqual(Hit()), "impact recipients match knockback recipients without allies");
        var maneuver = (ManeuverKnockback)calls.First(c => c.Name == "SendUnitManeuver").Args[1]!;
        Check(maneuver.Force == 20 && maneuver.MidairForce == 12 && maneuver.Origin == origin, "force and origin unchanged");
        probe.AffectCaster = false; caster.IsRecall = true; Apply(probe);
        Check(Hit().SequenceEqual(new[] { enemy.Id }) && caster.IsRecall, "AffectCaster=false excludes self before interruption");
        probe.AffectCaster = true;
        var projectile = (Unit)RuntimeHelpers.GetUninitializedObject(typeof(Unit));
        projectile.Id = 500; projectile.OwnerPlayerId = caster.PlayerId; projectile.Team = caster.Team;
        Apply(probe, new UnitSource(projectile));
        Check(Hit().Contains(caster.Id) && !Hit().Contains(ally.Id), "owned projectile preserves rocket jump, protects allies");
        probe.AffectCaster = false; Apply(probe, new UnitSource(projectile));
        Check(Hit().SequenceEqual(new[] { enemy.Id }), "owned projectile respects AffectCaster=false");
        probe.AffectCaster = true;
        var block = new Block { Ldata = (byte)TeamType.Team1 };
        Apply(probe, new BlockSource(new Vector3s(3, 3, 3), block));
        Check(Hit().SequenceEqual(new[] { enemy.Id }), "unowned block uses authoritative block team");
        ally.Team = TeamType.Team2; enemy.Team = TeamType.Team1;
        Apply(probe);
        Check(Hit().Contains(ally.Id) && !Hit().Contains(enemy.Id), "team switches are evaluated at impact");
        ally.Team = TeamType.Team1; enemy.Team = TeamType.Team2;
        var buffs = (Dictionary<BuffType, float>)typeof(Unit).GetField("_buffs", Hidden)!.GetValue(enemy)!;
        foreach (var buff in new[] { BuffType.Root, BuffType.KnockbackIgnore })
        {
            buffs[buff] = 1; Apply(probe); Check(!Hit().Contains(enemy.Id), buff + " immunity preserved"); buffs.Remove(buff);
        }
        probe.Targeting = new EffectTargeting { AffectedTeam = RelativeTeamType.Friendly }; Apply(probe);
        Check(Hit().SequenceEqual(new[] { caster.Id }), "friendly-only definition cannot bypass ally protection");
        probe.Targeting = new EffectTargeting { AffectedTeam = RelativeTeamType.Opponent }; Apply(probe);
        Check(Hit().SequenceEqual(new[] { enemy.Id }), "opponent-only definition keeps original targeting");

        probe.Targeting = null;
        var initiatorField = typeof(GameZone).GetField("_gameInitiator", Hidden)!;
        var originalInitiator = initiatorField.GetValue(zone);
        var custom = (CustomGamePlayerGroup)RuntimeHelpers.GetUninitializedObject(typeof(CustomGamePlayerGroup));
        typeof(CustomGamePlayerGroup).GetField("<GameInfo>k__BackingField", Hidden)!.SetValue(custom,
            new CustomGameInfo { TeamKilling = true });
        initiatorField.SetValue(zone, custom);
        Check(zone.AllowsCustomTeamCombat, "friendly-fire-enabled custom fixture is active");
        Apply(probe);
        Check(Hit().Contains(enemy.Id) && Hit().Contains(caster.Id) && !Hit().Contains(ally.Id),
            "friendly fire never enables teammate knockback");
        initiatorField.SetValue(zone, originalInitiator);

        // Waiting arena uses per-player combat sides although all wire teams are neutral.
        var ffa = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
        typeof(GameZone).GetField("_gameInitiator", Hidden)!.SetValue(ffa,
            RuntimeHelpers.GetUninitializedObject(typeof(WaitingArenaInitiator)));
        caster.Team = ally.Team = TeamType.Neutral; caster.AssignFreeForAllTeam(1); ally.AssignFreeForAllTeam(2);
        bool Can(Unit target, Unit? source, TeamType team, bool self) => (bool)typeof(GameZone).GetMethod("CanKnockback", Hidden)!.Invoke(ffa, [target, source, team, self])!;
        Check(Can(ally, caster, caster.Team, false), "different waiting-arena players remain opponents");
        Check(Can(caster, caster, caster.Team, true) && !Can(caster, caster, caster.Team, false), "waiting-arena self-knockback follows weapon flag");
        projectile.AssignFreeForAllTeam(1); projectile.Team = TeamType.Neutral;
        Check(Can(ally, projectile, projectile.Team, false), "owned projectile respects waiting-arena combat sides");
        Check(Can(caster, projectile, projectile.Team, true), "waiting-arena owned projectile self-knockback preserved");
        return true;
    });
}
finally { zone.Stop(); }
Console.WriteLine("KNOCKBACK PASS " + checks + " checks");

static T Stub<T>(Dictionary<string, object?>? returns = null) where T : class
{
    var proxy = DispatchProxy.Create<T, StubProxy>(); ((StubProxy)(object)proxy).Returns = returns ?? []; return proxy;
}
public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Returns = [];
    public List<(string Name, object?[] Args)> Calls = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Add((method!.Name, args?.ToArray() ?? []));
        if (Returns.TryGetValue(method.Name, out var value)) return value;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(bool)) return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
