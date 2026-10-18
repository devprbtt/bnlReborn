// Kira's kill-thrill regen must not stack with health pickups or heal stations; other healing still stacks.
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
catalogue.Replicate(JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!);
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
ConstEffectInfo Effect(string id) => new(new Key(id));
var killThrill = Effect("effect_hero_kira_kill_thrill");
var pickup = Effect("effect_status_regen_health_5s");
var generic = Effect("effect_status_regen_health");
var elizaHeal = Effect("effect_status_regen_health_doc_eliza_others");
float Regen() => kira.GetBuff(BuffType.HealthRegen);
void Expire(Unit unit) => typeof(Unit).GetMethod("RemoveExpiredEffects", Any)!.Invoke(unit, null);

OnZone(() =>
{
    Check(Regen() == 0, "no regen at start");
    // On a kill, the on-kill effect adds the kill thrill to Kira herself.
    kira.AddEffects([killThrill], kira.Team, kira.GetSelfSource());
    Check(Regen() == 5, "kill thrill alone heals 5/s");
    Check(kira.ActiveEffects.Any(e => e.Key == new Key("effect_status_shielded_kira")), "kill thrill shield is untouched");

    // Health pickup (hero loot / medkit): its wrapper grants the generic 10/s regen.
    // Timed like a real pickup, but already past its end so the real expiry path can remove it below.
    kira.AddEffects([new ConstEffectInfo(pickup.Key, (ulong?)1)], kira.Team, kira.GetSelfSource());
    Check(kira.ActiveEffects.Any(e => e.Key == generic.Key), "pickup grants the generic regen");
    Check(Regen() == 10, "kill thrill does not stack with a health pickup (10, not 15)");
    Expire(kira);
    Check(!kira.ActiveEffects.Any(e => e.Key == pickup.Key || e.Key == generic.Key), "pickup regen expired");
    Check(Regen() == 5, "kill thrill resumes when the pickup ends");

    // Heal station aura: generic regen whose source is the health_supply station.
    kira.AddEffects([generic], station.Team, station.GetSelfSource());
    Check(Regen() == 10, "kill thrill does not stack with a heal station (10, not 15)");
    kira.RemoveEffects([generic], station.Team, station.GetSelfSource());
    Check(Regen() == 5, "kill thrill resumes on leaving the station");

    // Eliza's heal is hero healing, not a pickup or station: unchanged, still additive.
    kira.AddEffects([new ConstEffectInfo(elizaHeal.Key, (ulong?)1)], eliza.Team, eliza.GetSelfSource());
    Check(Regen() == 15, "kill thrill still stacks with Doc Eliza's heal");
    Expire(kira);
    Check(Regen() == 5, "Eliza's heal expired");

    // Without the kill thrill, pickups and stations are unchanged (Eliza never had it).
    eliza.AddEffects([generic], station.Team, station.GetSelfSource());
    Check(eliza.GetBuff(BuffType.HealthRegen) == 10, "station alone still heals 10/s");
    eliza.RemoveEffects([generic], station.Team, station.GetSelfSource());
    eliza.AddEffects([new ConstEffectInfo(pickup.Key, (ulong?)1)], eliza.Team, eliza.GetSelfSource());
    Check(eliza.GetBuff(BuffType.HealthRegen) == 10, "a pickup alone still heals 10/s");
    return true;
});
zone.Stop();
Console.WriteLine($"BNL_KIRA_KILL_REGEN_FIXTURE_OK checks={checks}");

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
