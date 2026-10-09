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

var skill = new CardHeroClass { Id="fixture_skill", Type=HeroClassType.Skills };
var gear = new CardGear { Id = "fixture_ha_gear", Tools=[new ToolShot()] };
var hero = new CardUnit
{
    Id = "fixture_ha_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom, FallHitModifier = 1,
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
    skill, gear, hero, orb, sprayImpact, stationRegen, staffRegen, mode, new CardGameMode { Id = "game_mode_custom" },
    new CardMatch { Id = "fixture_ha_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }
]);
((UnitDataPlayer)hero.Data!).Class = skill.Key;
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


long clock=10000;
typeof(GameZone).GetField("_skillRecoveryClock",Any)!.SetValue(zone,(Func<long>)(()=>clock));
float HP() => healer.HealthPercentage * hero.Health!.Health!.MaxHealth;
void Near(float expected,string name) => Check(Math.Abs(HP()-expected)<.01f,name);
void Step(long ms) { clock+=ms;Call("TickSkillRecovery"); }
void Reset(float hp=20) { healer.UpdateData(new UnitUpdate{Health=hp}); Call("MarkClassPerkCombat",healer); }
OnZone(()=>{
    Reset();Step(4999);Near(20,"no regeneration before five seconds");
    Step(1);Near(20,"quiet period cannot be banked as healing");
    Step(250);Near(22.5f,"first quarter-second grants 2.5 percent max HP");
    Step(750);Near(30,"ten percent max HP restored per eligible second");
    Step(0);Near(30,"repeated tick at same time cannot duplicate healing");
    Reset(99);Step(6000);Near(100,"healing caps at maximum health");
    calls.Clear();Step(1000);Check(!calls.Any(c=>c.Name=="SendImpact"),"full health emits no recovery feedback");
    Reset();healer.SetGear(gear.Key);clock+=4900;
    zone.ReceivedCastRequest(healer.PlayerId!.Value,new CastData{ToolIndex=0,ShotPos=healer.Transform.Position,Shots=[new ShotData{TargetPos=Vector3.Zero}]});
    Step(200);Near(20,"accepted missed shot restarts combat delay");
    Step(5050);Near(22.5f,"regeneration resumes after full post-shot quiet period");
    Reset();clock+=4900;teammate.Team=TeamType.Team2;
    Call("UnitIsDamaged",healer,10f,teammate.CreateImpactData(casterPlayerId:teammate.PlayerId));
    Step(200);Near(20,"enemy damage restarts delay");
    Step(5050);Near(22.5f,"enemy-damage delay expires normally");
    teammate.Team=TeamType.Team1;Reset();clock+=4900;
    Call("UnitIsDamaged",healer,10f,teammate.CreateImpactData(casterPlayerId:teammate.PlayerId));
    Step(1100);Near(30,"friendly damage does not restart enemy combat delay");
    Reset();clock+=4900;teammate.Team=TeamType.Team2;
    Call("UnitIsDamaged",healer,0f,teammate.CreateImpactData(casterPlayerId:teammate.PlayerId));
    Step(1100);Near(30,"zero damage does not count as combat");
    teammate.Team=TeamType.Team1;
    ImpactData FallImpact() { var i=healer.CreateImpactData(sourceKey:CatalogueHelper.FallSource);i.CasterPlayerId=null;i.CasterUnitId=null;i.Impact=CatalogueHelper.FallImpact;return i; }
    Reset();clock+=4900;
    Call("UnitIsDamaged",healer,10f,FallImpact());
    Step(1100);Near(20,"fall damage restarts delay");
    Step(4150);Near(22.5f,"fall-damage delay expires normally");
    Reset();clock+=4900;
    Call("UnitIsDamaged",healer,0f,FallImpact());
    Step(1100);Near(30,"zero fall damage does not count as combat");
    Reset(80);clock+=4900;
    healer.OnFall(20f,false,5f,25f,true);
    float landed=HP();
    Check(landed<80,"native fall damage lands");
    Step(1100);Check(Math.Abs(HP()-landed)<.01f,"native fall restarts delay");
    Step(4150);Check(Math.Abs(HP()-(landed+2.5f))<.01f,"regeneration resumes after a fall's quiet period");
    Reset();healer.IsDead=true;Step(6000);Near(20,"dead hero cannot regenerate");
    healer.IsDead=false;Step(1000);Near(20,"death time cannot be banked");
    Reset();healer.IsActive=false;Step(6000);Near(20,"inactive hero cannot regenerate");healer.IsActive=true;
    Reset();healer.IsDropped=true;Step(6000);Near(20,"dropped hero cannot regenerate");healer.IsDropped=false;
    Reset();healer.CurrentChannelData=new ChannelData();Step(6000);Near(20,"ongoing channel prevents regeneration");healer.CurrentChannelData=null;
    Reset();skill.Type=HeroClassType.Brawn;Step(6000);Near(20,"Brawn does not receive Skill regeneration");
    skill.Type=HeroClassType.Brains;Step(1000);Near(20,"Brains does not receive Skill regeneration");skill.Type=HeroClassType.Skills;
    Reset();hero.Health!.Health!.MaxHealth=200;Step(6000);Near(40,"rate scales with maximum health");hero.Health.Health.MaxHealth=100;
    Reset();healer.AssignFreeForAllTeam(1);Step(6000);Near(30,"passive regeneration also works in FFA");healer.AssignFreeForAllTeam(null);
    Reset();Step(60000);Near(30,"server pause cannot bank a large healing burst");
    Reset();clock+=6000;((Action)Call("OnTick",1UL)!)();Near(30,"native zone tick drives regeneration");
    Reset();clock+=6000;calls.Clear();Call("TickSkillRecovery");
    Check(calls.Where(c=>c.Name=="SendImpact").Select(c=>(ImpactData)c.Args[0]!).Any(i=>i.CasterPlayerId==healer.PlayerId && i.HitUnits!.Contains(healer.Id) && Math.Abs(i.ShotPos.X-10)<.01f),"actual restored health uses native self-heal feedback");
    Reset();healer.UpdateData(new UnitUpdate{Buffs=new Dictionary<BuffType,float>{{BuffType.HealthCap,.5f}}});
    Step(6000);Check(Math.Abs(healer.HealthPercentage*150-35f)<.01f,"rate uses buffed maximum health");
    healer.UpdateData(new UnitUpdate{Buffs=new Dictionary<BuffType,float>{{BuffType.HealthCap,0},{BuffType.HealthGain,-.5f}}});
    Reset();Step(6000);Near(25,"normal incoming-healing reductions apply");
    healer.UpdateData(new UnitUpdate{Buffs=new Dictionary<BuffType,float>{{BuffType.HealthGain,0}}});
    healer.ZoneService=Stub<IServiceZone>();healer.IsDead=true;
    Check(healer.Respawn(new Vector3(4,4,4),Quaternion.Identity),"native respawn succeeds");
    healer.UpdateData(new UnitUpdate{Health=20});Step(4999);Near(20,"native respawn starts a fresh five-second delay");
    Step(1001);Near(30,"respawned hero resumes normal regeneration");
    var tuned = new CardPerk { Id=ClassPerkCatalogue.SkillId, SlotType=PerkSlotType.Class, ClassPerk=new ClassPerkBalance { OutOfCombatSeconds=2, RegenMaxHealthPercentPerSecond=4 } };
    var catalogue = (ServerCatalogue)Databases.Catalogue;
    catalogue.Replicate(catalogue.All.Append(tuned).ToList());
    Reset();Step(1999);Near(20,"CDB quiet period prevents early regeneration");
    Step(251);Near(21,"CDB four percent rate is used by existing hero");
    Step(750);Near(24,"CDB rate scales with elapsed time");
    tuned = new CardPerk { Id=ClassPerkCatalogue.SkillId, SlotType=PerkSlotType.Class, ClassPerk=new ClassPerkBalance { OutOfCombatSeconds=2, RegenMaxHealthPercentPerSecond=20 } };
    catalogue.Replicate(catalogue.All.Where(c=>c.Id!=tuned.Id).Append(tuned).ToList());
    Step(1000);Near(44,"replicated CDB rate updates an existing hero");
    return true;
});
zone.Stop();
Console.WriteLine($"BNL_SKILL_RECOVERY_FIXTURE_OK checks={checks}");
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
