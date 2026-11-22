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

const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; }
((ServerCatalogue)Databases.Catalogue).Replicate(File.ReadAllText(args[0]).Deserialize<List<Card>>()!);
foreach (var rolling in new[] { false, true })
{
    var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
    players[1] = new PlayerLobbyState { PlayerId = 1, Team = TeamType.Team1, Hero = new Key("unit_hero_roly"), Nickname = "roly" };
    var map = new MapData { Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(32, 16, 32),
        BlocksData = new byte[32 * 16 * 32 * 6].Zip(0).ToArray(), SpawnPoints = [
            new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(4, 4, 4) },
            new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(24, 4, 24) }] };
    var service = Stub<IServiceZone>();
    var zone = new GameZone(service, service, Stub<IBuffer>(), Stub<ISender>(new() { ["get_SenderCount"] = 1 }), map,
        Stub<IGameInitiator>(new() { ["GetGameMode"] = new Key("game_mode_friendly"), ["get_GameInstanceId"] = "trigger-death" }), players);
    object? Call(string name, params object?[] args) => typeof(GameZone).GetMethod(name, Hidden)!.Invoke(zone, args);
    T OnZone<T>(Func<T> action)
    {
        var done = new TaskCompletionSource<T>();
        zone.EnqueueAction(() => { try { done.SetResult(action()); } catch (Exception error) { done.SetException(error); } });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }
    try
    {
        OnZone(() =>
        {
            var roly = (Unit)Call("CreatePlayerUnit", 1u, service)!;
            roly.ZoneService = service;
            var data = (ZoneData)typeof(GameZone).GetField("_zoneData", Hidden)!.GetValue(zone)!;
            data.Phase = new ZonePhase { PhaseType = ZonePhaseType.Assault };
            roly.AbilityKey = new Key(rolling ? "ability_roly_emplacement_mode_rolling_wild" : "ability_roly_emplacement_mode");
            roly.AbilityCharges = 1;
            float cooldown = roly.AbilityCard!.Charges!.ChargeCooldown;
            var calls = ((StubProxy)(object)service).Calls;
            zone.ReceivedAbilityCastRequest(1, 1, new AbilityCastData(), service);
            Check(roly.AbilityTriggered && roly.AbilityCharges == 1 && roly.TimeTillNextAbilityCharge == null,
                "native F activates without starting its end-of-toggle cooldown");
            Check(roly.ActiveEffects.Any(e => e.Key == new Key(rolling ? "effect_hero_roly_emplacement_mode_rolling_wild" : "effect_hero_roly_emplacement_mode")), "F effect active before death");
            calls.Clear();
            roly.Killed(roly.CreateBlankImpactData());
            Check(roly.IsDead && !roly.AbilityTriggered && roly.AbilityTriggerTimeEnd == null,
                "death immediately clears the stale F toggle and its duration");
            Check(roly.AbilityCharges == 0 && roly.TimeTillNextAbilityCharge.HasValue, "death immediately consumes F charge");
            var end = roly.TimeTillNextAbilityCharge!.Value;
            Check(Math.Abs((end - DateTimeOffset.Now).TotalSeconds - cooldown) < .5, "cooldown starts at death using native configured duration");
            Check(calls.Where(c => c.Name == "SendUnitUpdate").SelectMany(c => c.Args.OfType<UnitUpdate>()).Any(u =>
                u.AbilityCharges == 0 && u.AbilityChargeCooldownEnd == (ulong)end.ToUnixTimeMilliseconds()),
                "death publishes depleted charge and exact cooldown timestamp to HUD");
            Check(!roly.ActiveEffects.Any(e => e.Key == new Key("effect_pull_immunity")), "trigger effects removed on death");
            roly.Killed(roly.CreateBlankImpactData());
            Check(roly.TimeTillNextAbilityCharge == end && roly.AbilityCharges == 0, "duplicate death cannot restart or double-charge cooldown");
            calls.Clear();
            Check(roly.Respawn(new Vector3(4, 4, 4), Quaternion.Identity), "native immediate custom respawn succeeds");
            Check(calls.Where(c => c.Name == "SendUnitUpdate" && c.Args.OfType<uint>().Contains(roly.Id))
                .SelectMany(c => c.Args.OfType<UnitUpdate>()).Any(u => u.AbilityCharges == 0 &&
                    u.AbilityChargeCooldownEnd == (ulong)end.ToUnixTimeMilliseconds()), "respawn sends cooldown on the new unit id");
            Check(!roly.AbilityTriggered && roly.AbilityCharges == 0 && roly.TimeTillNextAbilityCharge == end,
                "fast respawn preserves the already-running cooldown");
            var snapshot = roly.GetUpdateData();
            Check(snapshot.AbilityCharges == 0 && snapshot.AbilityChargeCooldownEnd == (ulong)end.ToUnixTimeMilliseconds(),
                "respawn and late-join snapshot shows cooldown");
            calls.Clear();
            zone.ReceivedAbilityCastRequest(2, 1, new AbilityCastData(), service);
            Check(!roly.AbilityTriggered && roly.TimeTillNextAbilityCharge == end && roly.AbilityCharges == 0,
                "early F press cannot restart the cooldown or consume a stale toggle");
            Check(calls.Any(c => c.Name == "SendCastAbility" && c.Args.OfType<bool>().Contains(false)), "early F press is rejected");
            roly.TimeTillNextAbilityCharge = DateTimeOffset.Now.AddMilliseconds(-1);
            roly.AbilityChargeGained();
            zone.ReceivedAbilityCastRequest(3, 1, new AbilityCastData(), service);
            Check(roly.AbilityTriggered && roly.AbilityCharges == 1, "F works on first press after recharge");
            zone.ReceivedAbilityCastRequest(4, 1, new AbilityCastData(), service);
            Check(!roly.AbilityTriggered && roly.AbilityCharges == 0, "ordinary manual cancellation still starts cooldown");
            var cancelledEnd = roly.TimeTillNextAbilityCharge;
            roly.Killed(roly.CreateBlankImpactData());
            Check(roly.TimeTillNextAbilityCharge == cancelledEnd, "death with already-cancelled F preserves cooldown");
            return true;
        });
    }
    finally { zone.Stop(); }
}
Console.WriteLine("TRIGGER_DEATH_COOLDOWN PASS " + checks + " checks");

static T Stub<T>(Dictionary<string, object?>? returns = null) where T : class
{ var proxy = DispatchProxy.Create<T, StubProxy>(); ((StubProxy)(object)proxy).Returns = returns ?? []; return proxy; }
public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Returns = [];
    public readonly List<(string Name, object?[] Args)> Calls = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Add((method!.Name, args ?? []));
        if (Returns.TryGetValue(method.Name, out var value)) return value;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(bool)) return method.Name is "UsesPhaseBarriers" or "AllowsTeamCommunication";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
