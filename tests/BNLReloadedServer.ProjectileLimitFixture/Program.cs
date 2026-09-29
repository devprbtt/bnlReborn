// A projectile unit card's count_limit is enforced like a device's: Genie's heavy orb with
// { limit 2, scope owner } keeps each player's two newest orbs and kills the oldest when a third is fired.
// Drives GameZone.CreateProjectileUnit, the path every gear and ability projectile unit takes.
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

CardUnit Orb(string id, UnitCountLimit? limit) => new()
{
    Id = id, Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center, Lifetime = 60, CountLimit = limit,
    Health = new UnitHealth { Health = new Health { MaxHealth = 50, HealthType = HealthType.World } },
    Data = new UnitDataProjectile { MaxSpeed = 0, Acceleration = 0, TriggerRadius = 0.1f, CollideWith = RelativeTeamType.Opponent }
};
var limited = Orb("fixture_limited_orb", new UnitCountLimit { Limit = 2, Scope = UnitLimitScope.Owner, DropLast = true });
var unlimited = Orb("fixture_unlimited_orb", null);
var gear = new CardGear { Id = "fixture_pl_gear" };
var hero = new CardUnit
{
    Id = "fixture_pl_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } }
};
var mode = new CardGameMode { Id = "game_mode_friendly" };
((ServerCatalogue)Databases.Catalogue).Replicate([
    gear, hero, limited, unlimited, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_pl_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Gears = [gear.Key];

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team1) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero.Key, Nickname = $"p{id}" };
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(16, 16, 16),
    BlocksData = new byte[16 * 16 * 16 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(4, 4, 4) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(12, 4, 12) }]
};
var zone = new GameZone(Stub<IServiceZone>(), Stub<IServiceZone>(), Stub<IBuffer>(), Stub<ISender>(), map,
    Stub<IGameInitiator>(new() { ["GetGameMode"] = mode.Key, ["get_GameInstanceId"] = "fixture" }), players);
object? Call(string name, params object?[] args) => typeof(GameZone).GetMethod(name, Any)!.Invoke(zone, args);
T OnZone<T>(Func<T> body)
{
    var done = new TaskCompletionSource<T>();
    zone.EnqueueAction(() => { try { done.SetResult(body()); } catch (Exception e) { done.SetException(e); } });
    return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
var units = (IDictionary<uint, Unit>)typeof(GameZone).GetField("_units", Any)!.GetValue(zone)!;
var (genie, teammate) = OnZone(() => ((Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!, (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!));

// Fires one projectile unit the way gear and abilities do, and returns the new unit.
Unit Fire(CardUnit card, Unit by)
{
    Thread.Sleep(20); // real orbs are seconds apart; keep creation times distinct
    return OnZone(() =>
    {
        var before = units.Keys.ToHashSet();
        var origin = new Vector3(6, 8, 6);
        Call("CreateProjectileUnit", card.Key, 0f, new ShotData { TargetPos = origin + Vector3.UnitX }, origin, by);
        return units.Values.Single(u => !before.Contains(u.Id) && u.Key == card.Key);
    });
}
List<Unit> Alive(CardUnit card, Unit owner) => OnZone(() =>
    units.Values.Where(u => u.Key == card.Key && !u.IsDead && u.OwnerPlayerId == owner.PlayerId).ToList());

var first = Fire(limited, genie);
Check(first.OwnerPlayerId == genie.PlayerId, "a fired orb is owned by the player who fired it");
var second = Fire(limited, genie);
Check(Alive(limited, genie).Count == 2 && !first.IsDead, "two orbs can be out at once");
var third = Fire(limited, genie);
var alive = Alive(limited, genie);
Check(first.IsDead && alive.Count == 2 && alive.Contains(second) && alive.Contains(third), "a third orb removes the oldest, keeping the two newest");
var fourth = Fire(limited, genie);
Check(second.IsDead && !third.IsDead && !fourth.IsDead, "each new orb removes the then-oldest");

var teammateOrb = Fire(limited, teammate);
Fire(limited, teammate);
Check(!teammateOrb.IsDead && !third.IsDead && !fourth.IsDead, "the limit is per player: a teammate's orbs do not remove the Genie's");

for (var i = 0; i < 4; i++) Fire(unlimited, genie);
Check(Alive(unlimited, genie).Count == 4, "a projectile without count_limit is unaffected");

zone.Stop();
Console.WriteLine($"Projectile limit fixture passed: {checks} checks.");

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
