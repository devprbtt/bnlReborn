// Heal Bane is an ordinary catalogue perk: its effect is an on_hit trigger that names the debuff it applies,
// so its strength and duration are edited on cards like any other perk. Drives the real GameZone damage
// handler with the perk on a player, and checks the rules a hit must meet, that the values come from the
// cards the perk names, that clients get a type they can parse, and that a broken reference is refused.
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

var opponents = new EffectTargeting { AffectedTeam = RelativeTeamType.Opponent, AffectedUnits = [UnitType.Player] };
CardEffect Debuff(string id, float healthGain, float seconds) => new()
{
    Id = id, Positive = false, Duration = seconds,
    Effect = new ConstEffectBuff { Targeting = opponents, Buffs = new() { [BuffType.HealthGain] = healthGain } }
};
CardEffect OnHitPerk(string id, string debuffId) => new()
{
    Id = id, Positive = true,
    Effect = new ConstEffectOnHit { Effect = new InstEffectBunch { Targeting = opponents, Constant = [Catalogue.Key(debuffId)] } }
};
var debuff = Debuff("effect_heal_bane_debuff", -0.6f, 5);
var perk = OnHitPerk("effect_perk_heal_bane_pos", "effect_heal_bane_debuff");
// A second tuning, to show a perk variant is just two cards: no server code knows either id.
var weakDebuff = Debuff("fixture_weak_heal_bane_debuff", -0.3f, 2);
var weakPerk = OnHitPerk("fixture_weak_heal_bane_pos", "fixture_weak_heal_bane_debuff");

var gear = new CardGear { Id = "fixture_hb_gear" };
var device = Catalogue.Key("fixture_hb_turret"); // turrets, devices and abilities stamp their own keys, not gear
var hero = new CardUnit
{
    Id = "fixture_hb_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } }
};
var mode = new CardGameMode { Id = "game_mode_friendly" };
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate([
    gear, hero, debuff, perk, weakDebuff, weakPerk, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_hb_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Gears = [gear.Key]; // keys exist only after Replicate

var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var (id, team) in new[] { (1u, TeamType.Team1), (2u, TeamType.Team2), (3u, TeamType.Team1), (4u, TeamType.Team2) })
    players[id] = new PlayerLobbyState { PlayerId = id, Team = team, Hero = hero.Key, Nickname = $"p{id}" };
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(8, 8, 8),
    BlocksData = new byte[8 * 8 * 8 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(2, 1, 2) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(5, 1, 5) }]
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

var units = OnZone(() => new uint[] { 1, 2, 3, 4 }.Select(id => (Unit)Call("CreatePlayerUnit", id, Stub<IServiceZone>())!).ToArray());
var (attacker, victim, ally, other) = (units[0], units[1], units[2], units[3]);
var asPeriodic = typeof(ImpactData).GetMethod("AsPeriodicEffect", Any)!;

// What the live server does when a unit takes damage: the real handler, on the zone's own queue.
void Hit(Unit target, Key source, bool periodic = false, Unit? by = null) => OnZone(() =>
{
    by ??= attacker;
    var impact = new ImpactData { SourceKey = source, CasterPlayerId = by.PlayerId, CasterUnitId = by.Id };
    if (periodic) impact = (ImpactData)asPeriodic.Invoke(impact, null)!;
    Call("UnitIsDamaged", target, 10f, impact);
    return 0;
});
bool Baned(Unit u, CardEffect d) => u.ActiveEffects.Any(e => e.Key == d.Key);
void Reset(Unit u) { u.PurgeEffects(false, true); u.UpdateData(new UnitUpdate { Health = 50f }); }

Hit(victim, gear.Key);
Check(!Baned(victim, debuff), "without the perk a hit applies nothing");

attacker.AddEffect(new ConstEffectInfo(perk.Key), attacker.Team, attacker.GetSelfSource());
Reset(victim);
Hit(victim, gear.Key);
Check(Baned(victim, debuff), "with the perk a weapon hit applies the debuff its card names");
var first = victim.ActiveEffects.Single(e => e.Key == debuff.Key);
Check(first.TimestampEnd is { } end && Math.Abs(((double)end - DateTimeOffset.Now.ToUnixTimeMilliseconds()) / 1000.0 - 5) < 1,
    "duration is the debuff card's (5 s)");
Check(Math.Abs(victim.HealthGainAmount(10f) - 4f) < 0.01f, "strength is the debuff card's: healing cut by 60%");
Hit(victim, gear.Key);
Check(victim.ActiveEffects.Count(e => e.Key == debuff.Key) == 1, "a second hit refreshes instead of stacking");

Reset(other);
Hit(other, gear.Key, periodic: true);
Check(!Baned(other, debuff), "bleed and burn ticks do not apply it");
Hit(other, device);
Check(!Baned(other, debuff), "ability and device damage does not apply it");
Reset(ally);
Hit(ally, gear.Key);
Check(!Baned(ally, debuff), "hitting an ally does not apply it");

// A teammate running the other variant (player 3 is on the attacker's team).
var secondAttacker = ally;
secondAttacker.AddEffect(new ConstEffectInfo(weakPerk.Key), secondAttacker.Team, secondAttacker.GetSelfSource());
Reset(victim);
Hit(victim, gear.Key, by: secondAttacker);
Check(Baned(victim, weakDebuff) && !Baned(victim, debuff) && Math.Abs(victim.HealthGainAmount(10f) - 7f) < 0.01f,
    "a perk variant pointing at another debuff applies that one's values (30%)");

// The client has no on_hit type and throws on unknown variants; it must be sent something it parses.
using (var stream = new MemoryStream())
{
    var writer = new BinaryWriter(stream);
    ConstEffect.WriteVariant(writer, perk.Effect!);
    stream.Position = 0;
    var tag = (ConstEffectType)stream.ReadByte();
    Check(tag == ConstEffectType.Buff, "clients are sent on_hit as a buff, a type they know");
    stream.Position = 0;
    Check(ConstEffect.ReadVariant(new BinaryReader(stream)) is ConstEffectBuff { Buffs.Count: 0 }, "and it is inert: no buffs");
}

// Catalogue JSON: the migrated perk card parses, and a catalogue still carrying the retired buff loads.
var opts = JsonHelper.DefaultSerializerSettings;
var migrated = JsonSerializer.Deserialize<Card>("""
    {"_id":"effect_perk_heal_bane_pos","category":"effect","scope":"public","positive":true,
     "effect":{"type":"on_hit","targeting":null,"effect":{"type":"bunch","targeting":{"affected_units":["player"],"affected_team":"opponent"},
               "break_on_effect_fail":false,"instant":[],"constant":["effect_heal_bane_debuff"]}}}
    """, opts);
Check(migrated is CardEffect { Effect: ConstEffectOnHit { Effect: InstEffectBunch { Constant: [var named] } } } && named == debuff.Key,
    "the migrated catalogue card parses as on_hit naming effect_heal_bane_debuff");
var legacy = JsonSerializer.Deserialize<Card>("""
    {"_id":"effect_perk_heal_bane_pos","category":"effect","effect":{"type":"buff","targeting":null,"buffs":{"heal_bane":1}}}
    """, opts);
Check(legacy is CardEffect { Effect: ConstEffectBuff }, "a catalogue still carrying the retired heal_bane buff still loads");

// A dangling reference is refused at load rather than silently doing nothing.
List<Card> Padded(params Card[] extra) =>
    [.. Enumerable.Range(0, 1000).Select(i => (Card)new CardBadge { Id = "fixture_pad_" + i }), .. extra];
string[] Problems(List<Card> cards) => CatalogueValidator.Validate(cards).Where(p => p.Contains("on_hit")).ToArray();
Check(Problems(Padded(perk, debuff)).Length == 0, "validator accepts an on_hit naming an existing effect");
Check(Problems(Padded(perk)).Any(p => p.Contains("effect_perk_heal_bane_pos")), "validator refuses an on_hit naming a missing effect");
Check(Problems(Padded(new CardEffect { Id = "fixture_empty", Effect = new ConstEffectOnHit() })).Length == 1,
    "validator refuses an on_hit with nothing to apply");

zone.Stop();
Console.WriteLine($"Heal bane fixture passed: {checks} checks.");

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
