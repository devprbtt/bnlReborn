// Live catalogue (args[0]): a Ninja carrying the real Heal Bane perk hits an enemy with every Bloody Bleeding
// attack through the same ApplyInstEffect call ReceivedHit makes, then the bleed ticks through the real buff
// loop and zone damage handler. Only the direct hit may apply Heal Bane; bleed ticks must neither refresh it
// nor bring it back after it runs out.
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
catalogue.Replicate(JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!);
var healBane = new Key("effect_heal_bane_debuff");
var bleed = new Key("effect_status_bleed_bloody_bleeding");
var bleedHealCut = new Key("effect_status_bleed_bloody_bleeding_heal");
var perk = catalogue.GetCard<CardPerk>(new Key("perk_off_heal_bane"))!;
var perkEffects = perk.PerkMods!.OfType<PerkModEffect>().SelectMany(m => m.Constant ?? []).ToList();

var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(16, 8, 16),
    BlocksData = new byte[16 * 8 * 16 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(2, 4, 2) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(13, 4, 13) }]
};
var players = new ConcurrentDictionary<uint, PlayerLobbyState>
{
    [1] = new() { PlayerId = 1, Team = TeamType.Team1, Hero = new Key("unit_hero_ninja"), Nickname = "ninja" },
    [2] = new() { PlayerId = 2, Team = TeamType.Team2, Hero = new Key("unit_hero_kira"), Nickname = "kira" }
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

var (ninja, victim) = OnZone(() => ((Unit)Call("CreatePlayerUnit", 1u, Stub<IServiceZone>())!, (Unit)Call("CreatePlayerUnit", 2u, Stub<IServiceZone>())!));
OnZone(() =>
{
    // Equip the perk the way a loadout does: its constant effects are the hero's starting effects.
    foreach (var key in perkEffects)
    {
        ninja.InitialEffects = ninja.InitialEffects.SetItem(key, null);
        ninja.AddEffect(new ConstEffectInfo(key), ninja.Team, ninja.GetSelfSource());
    }
    return 0;
});
Check(perkEffects.Contains(new Key("effect_perk_heal_bane_pos")) && ninja.ActiveEffects.Any(e => e.Key == new Key("effect_perk_heal_bane_pos")),
    "the Ninja carries the live Heal Bane perk's on_hit effect");

float Hp() => (float)typeof(Unit).GetField("_health", Any)!.GetValue(victim)!;
ConstEffectInfo? Find(Key key) => victim.ActiveEffects.FirstOrDefault(e => e.Key == key);
var removeExpired = typeof(Unit).GetMethod("RemoveExpiredEffects", Any)!;

foreach (var (gearId, toolIndex, label) in new[]
{
    ("gear_ninja_katana_and_teleport_bloody_bleeding", 0, "Katana swing"),
    ("gear_ninja_shuriken_bloody_bleeding", 0, "Ninja Star"),
    ("gear_ninja_shuriken_bloody_bleeding", 1, "Ninja Star triple throw"),
})
{
    var gear = catalogue.GetCard<CardGear>(new Key(gearId))!;
    var tool = gear.Tools![toolIndex];
    var hitEffect = (InstEffect)tool.GetType().GetProperty("HitEffect")!.GetValue(tool)!;
    OnZone(() => { victim.PurgeEffects(true, true); victim.UpdateData(new UnitUpdate { Health = 1000f }); return 0; });
    Check(Find(healBane) is null && Find(bleed) is null, label + ": victim starts clean");

    // The call ReceivedHit makes for a gear hit on a player.
    var hpBefore = Hp();
    OnZone(() =>
    {
        var impact = ninja.CreateImpactDataExact(victim.GetMidpoint(), ninja.GetMidpoint(), Vector3s.Zero, false, gear.Key);
        Call("ApplyInstEffect", ninja.GetSelfSource(impact), new[] { victim }, hitEffect, impact, null, null, null, true);
        return 0;
    });
    Check(Hp() < hpBefore, label + ": the direct hit damaged the victim");
    Check(Find(bleed) is not null && Find(bleedHealCut) is not null, label + ": the hit applied Bloody Bleeding and its healing cut");
    var bane = Find(healBane);
    Check(bane?.ExpirationTime is not null, label + ": the direct hit applied Heal Bane");
    // Pull its live end 2 s earlier (still active) so a refresh to the full 3 s would show as a later end.
    // ExpirationTime is the live end; TimestampEnd is the original stamp and never moves.
    var baneEnd = bane!.ExpirationTime!.Value - 2000;
    OnZone(() => { bane.UpdateDuration(baneEnd); return 0; });

    // Bleed ticks while Heal Bane is active: damage lands, Heal Bane is not refreshed.
    var hpTick = Hp();
    OnZone(() => { victim.ApplyBuffEffects(0.5f); victim.ApplyBuffEffects(0.5f); return 0; });
    Check(Hp() < hpTick, label + ": bleed ticked damage while Heal Bane was active (" + (hpTick - Hp()) + " HP)");
    Check(Find(healBane)?.ExpirationTime == baneEnd, label + ": bleed ticks did not refresh Heal Bane");

    // Heal Bane (3 s) runs out before the bleed (5 s); later ticks must not bring it back.
    OnZone(() => { Find(healBane)!.UpdateDuration(1); removeExpired.Invoke(victim, null); return 0; });
    Check(Find(healBane) is null && Find(bleed) is not null, label + ": Heal Bane ran out while the bleed continues");
    hpTick = Hp();
    OnZone(() => { for (var i = 0; i < 4; i++) victim.ApplyBuffEffects(0.5f); return 0; });
    Check(Hp() < hpTick, label + ": bleed kept ticking after Heal Bane ended (" + (hpTick - Hp()) + " HP)");
    Check(Find(healBane) is null, label + ": bleed ticks did not reactivate Heal Bane");
    Check(Math.Abs(victim.HealthGainAmount(10f) - 8f) < 0.01f, label + ": only Bloody Bleeding's 20% healing cut remains");

    // Positive control: the next direct hit does apply it again, so the checks above can see it.
    OnZone(() =>
    {
        var impact = ninja.CreateImpactDataExact(victim.GetMidpoint(), ninja.GetMidpoint(), Vector3s.Zero, false, gear.Key);
        Call("ApplyInstEffect", ninja.GetSelfSource(impact), new[] { victim }, hitEffect, impact, null, null, null, true);
        return 0;
    });
    Check(Find(healBane) is not null, label + ": the next direct hit applies Heal Bane again");
}

Console.WriteLine($"HEAL_BANE_BLEED PASS {checks} checks");

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
