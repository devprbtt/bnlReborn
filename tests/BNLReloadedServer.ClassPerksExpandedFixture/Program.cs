// Class perks II and III, driven through a real GameZone with the CDB cards from tools/catalogue: selection and
// spawn resolution, Brains II ammo regeneration on healed allies, Brains III build speed, the Brawn II damage
// reduction aura, Brawn III's on-kill demolition buff, Skill II assists and Skill III respawn reduction.
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

var tools = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tools/catalogue"));
List<T> Load<T>(string dir) where T : Card =>
    Directory.GetFiles(Path.Combine(tools, dir), "*.json").Select(p => File.ReadAllText(p).Deserialize<T>()!).ToList();
var perkCards = Load<CardPerk>("class-perks");
var effectCards = Load<CardEffect>("class-perk-effects");
Check(perkCards.Count == 9 && effectCards.Count == 7, "nine class perk and seven effect CDB documents");

var brawn = new CardHeroClass { Id = "fixture_brawn", Type = HeroClassType.Brawn };
var brains = new CardHeroClass { Id = "fixture_brains", Type = HeroClassType.Brains };
var skill = new CardHeroClass { Id = "fixture_skill", Type = HeroClassType.Skills };
var gear = new CardGear
{
    Id = "fixture_cp_gear", Tools = [new ToolShot { Ammo = new ToolAmmo { AmmoIndex = 0, Rate = 1 } }],
    // No natural regeneration: Brains II must restore ammo on its own.
    Ammo = [new AmmoData { MagSize = 10, Pool = new AmmoPool { PoolSize = 100, BaseRegen = 0 } }]
};
CardUnit Hero(string id) => new()
{
    Id = id, Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom,
    Health = new UnitHealth { Health = new Health { MaxHealth = 100, HealthType = HealthType.Player } }
};
var brawnHero = Hero("fixture_cp_brawn");
var brainsHero = Hero("fixture_cp_brains");
var skillHero = Hero("fixture_cp_skill");
// A regular on-kill perk (Killer Shield shape) to observe Skill II assists.
var killerBunch = new CardEffect { Id = "fixture_killer_bunch", Positive = true, Duration = 5, Effect = new ConstEffectBuff { Buffs = new() { [BuffType.Shield] = 0.5f } } };
var killerEffect = new CardEffect
{
    Id = "fixture_killer_on_kill", Positive = true,
    Effect = new ConstEffectOnKill { Effect = new InstEffectBunch { Constant = [], Instant = [] } }
};
var killerPerk = new CardPerk { Id = "fixture_killer_perk", SlotType = PerkSlotType.Offensive };
var mode = new CardGameMode { Id = "game_mode_friendly" };
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate([
    brawn, brains, skill, gear, brawnHero, brainsHero, skillHero, killerBunch, killerEffect, killerPerk, mode,
    new CardGameMode { Id = "game_mode_custom" }, new CardMatch { Id = "fixture_cp_match", Data = new MatchDataShieldCapture() },
    new CardBlock { Id = "fixture_air", BlockId = 0, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardGlobalLogic { Id = "global_logic" }, .. perkCards, .. effectCards
]);
((ConstEffectOnKill)killerEffect.Effect!).Effect = new InstEffectBunch { Constant = [killerBunch.Key], Instant = [] };
killerPerk.PerkMods = [new PerkModEffect { Constant = [killerEffect.Key] }];
foreach (var (hero, cls) in new[] { (brawnHero, brawn), (brainsHero, brains), (skillHero, skill) })
{
    ((UnitDataPlayer)hero.Data!).Class = cls.Key;
    ((UnitDataPlayer)hero.Data!).Gears = [gear.Key];
}
Key K(string id) => Catalogue.Key(id);

// ---- Wire records and selection resolution ----
foreach (var card in perkCards)
{
    ClassPerkCatalogue.Validate(card);
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) card.Write(writer);
    stream.Position = 0;
    var received = CardPerk.ReadRecord(new BinaryReader(stream));
    Check(stream.Position == stream.Length && received.SlotType == PerkSlotType.Class &&
          !received.Description!.Text!.Contains('{') && !received.Label!.Contains('{'), "resolved class wire record: " + card.Id);
}
Check(ClassPerkCatalogue.Resolve(perkCards.Single(c => c.Id == ClassPerkCatalogue.Skill3Id), "{respawn_reduction_percent}%") == "70%",
    "Skill III description resolves its CDB percentage");
var none = ClassPerkCatalogue.EffectivePerks(brainsHero.Key, [], out var noneId);
Check(noneId == ClassPerkCatalogue.BrainsId && none.SequenceEqual([K(ClassPerkCatalogue.BrainsId)]), "no selection plays and applies class perk I");
var chosen = ClassPerkCatalogue.EffectivePerks(brainsHero.Key, [K(ClassPerkCatalogue.Brains3Id), killerPerk.Key], out var chosenId);
Check(chosenId == ClassPerkCatalogue.Brains3Id && chosen.Count == 2 && chosen.Contains(K(ClassPerkCatalogue.Brains3Id)), "selected class perk is kept beside regular perks");
var foreign = ClassPerkCatalogue.EffectivePerks(brainsHero.Key, [K(ClassPerkCatalogue.Brawn2Id)], out var foreignId);
Check(foreignId == ClassPerkCatalogue.BrainsId && !foreign.Contains(K(ClassPerkCatalogue.Brawn2Id)), "another class's perk is stripped and the default applies");

// ---- Zone ----
// 1 Brains healer, 2 Skill ally, 3 Brawn (Bulwark), 4 Brawn (Rally Point), 5 Skill ally, 6 enemy Skill, 7 Brains III,
// 8 second Bulwark Brawn, 9 Skill II assister, 10 Skill I assister, 11 Brawn III, 12 Skill III, 13 dead ally, 14 dead enemy
var roster = new (uint Id, TeamType Team, CardUnit Hero, string[] Perks)[]
{
    (1, TeamType.Team1, brainsHero, [ClassPerkCatalogue.Brains2Id]), (2, TeamType.Team1, skillHero, []),
    (3, TeamType.Team1, brawnHero, [ClassPerkCatalogue.Brawn2Id]), (4, TeamType.Team1, brawnHero, []),
    (5, TeamType.Team1, skillHero, []), (6, TeamType.Team2, skillHero, []),
    (7, TeamType.Team1, brainsHero, [ClassPerkCatalogue.Brains3Id]), (8, TeamType.Team1, brawnHero, [ClassPerkCatalogue.Brawn2Id]),
    (9, TeamType.Team1, skillHero, [ClassPerkCatalogue.Skill2Id, killerPerk.Id!]), (10, TeamType.Team1, skillHero, [killerPerk.Id!]),
    (11, TeamType.Team1, brawnHero, [ClassPerkCatalogue.Brawn3Id]), (12, TeamType.Team1, skillHero, [ClassPerkCatalogue.Skill3Id]),
    (13, TeamType.Team1, skillHero, []), (14, TeamType.Team2, skillHero, [])
};
var players = new ConcurrentDictionary<uint, PlayerLobbyState>();
foreach (var r in roster)
    players[r.Id] = new PlayerLobbyState { PlayerId = r.Id, Team = r.Team, Hero = r.Hero.Key, Nickname = $"p{r.Id}", Perks = r.Perks.Select(K).ToList() };
var map = new MapData
{
    Match = MatchType.ShieldCapture, Properties = new MapDataProps(), Size = new Vector3s(64, 16, 64),
    BlocksData = new byte[64 * 16 * 64 * 6].Zip(0).ToArray(),
    SpawnPoints = [new MapSpawnPoint { Team = TeamType.Team1, Label = SpawnPointLabel.Base, Position = new Vector3(4, 4, 4) },
                   new MapSpawnPoint { Team = TeamType.Team2, Label = SpawnPointLabel.Base, Position = new Vector3(60, 4, 60) }]
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
var u = OnZone(() => roster.ToDictionary(r => r.Id, r => (Unit)Call("CreatePlayerUnit", r.Id, Stub<IServiceZone>())!));
foreach (var unit in u.Values) unit.ZoneService = Stub<IServiceZone>();
Check(u[1].ClassPerkId == ClassPerkCatalogue.Brains2Id && u[4].ClassPerkId == ClassPerkCatalogue.BrawnId &&
      u[12].ClassPerkId == ClassPerkCatalogue.Skill3Id, "spawned heroes carry their selected or default class perk");
Check(u[1].ActiveClassPerk == ClassPerkCatalogue.Brains2Id && u[2].ActiveClassPerk == ClassPerkCatalogue.SkillId, "active class perk follows selection");
ulong moveTime = 1;
void Place(Unit unit, Vector3 pos)
{
    var old = unit.Transform.Position;
    unit.UnitMove(ZoneTransformHelper.ToZoneTransform(pos, Quaternion.Identity), ++moveTime);
    Call("UnitMoved", unit, moveTime, unit.Transform, old);
}
// Park everyone far apart so only deliberate placements share an aura.
OnZone(() => { foreach (var (id, unit) in u) Place(unit, new Vector3(4 + (id % 7) * 8, 4, 4 + (id / 7) * 30)); return true; });
float Pool(Unit unit) => unit.Gears[0].Ammo[0].Pool;
bool At(DateTimeOffset? t, DateTimeOffset expected) => t is { } v && Math.Abs((v - expected).TotalMilliseconds) < 1;
bool Has(Unit unit, string effect) => unit.ActiveEffects.Any(e => e.Key == K(effect));

// ---- Brains II: Supply Line ----
float HP(Unit unit) => unit.HealthPercentage * 100;
void HealFrom(Unit healer, Unit target, float amount, EffectSource? source = null) => OnZone(() =>
{
    var impact = new ImpactData { CasterPlayerId = healer.PlayerId, CasterUnitId = healer.Id, SourceKey = gear.Key, HitUnits = [] };
    Call("ApplyInstEffect", source ?? new UnitSource(healer), new[] { target }, new InstEffectHeal { PlayerHeal = amount }, impact, null, null, null, true);
    return true;
});
OnZone(() =>
{
    u[1].UpdateData(new UnitUpdate { Health = 40 });
    u[2].UpdateData(new UnitUpdate { Health = 40, Ammo = new() { [gear.Key] = [new Ammo { Index = 0, Pool = 0 }] } });
    return true;
});
HealFrom(u[1], u[2], 20);
Check(Has(u[2], "effect_class_brains_2_ammo_regen"), "healed ally receives the Supply Line effect");
Check(Math.Abs(Pool(u[2]) - 2.5f) < 0.01f, "first regeneration tick restores 2.5% of the pool immediately");
Check(Math.Abs(HP(u[1]) - 40) < 0.01f, "Supply Line does not also grant Shared Recovery");
Thread.Sleep(1100);
Check(OnZone(() => Pool(u[2])) >= 7.4f, "ammo keeps regenerating at 5% per second while the effect lasts");
var expiry = u[2].ActiveEffects.Single(e => e.Key == K("effect_class_brains_2_ammo_regen")).ExpirationTime;
Thread.Sleep(20);
HealFrom(u[1], u[2], 10);
Check(u[2].ActiveEffects.Count(e => e.Key == K("effect_class_brains_2_ammo_regen")) == 1 &&
      u[2].ActiveEffects.Single(e => e.Key == K("effect_class_brains_2_ammo_regen")).ExpirationTime > expiry, "another heal refreshes, not stacks, the effect");
OnZone(() => { u[1].UpdateData(new UnitUpdate { Health = 40 }); return true; });
HealFrom(u[1], u[1], 20);
Check(!Has(u[1], "effect_class_brains_2_ammo_regen"), "self-healing does not trigger Supply Line");
HealFrom(u[1], u[6], 20);
Check(!Has(u[6], "effect_class_brains_2_ammo_regen"), "healing an enemy does not trigger Supply Line");
OnZone(() => { u[5].UpdateData(new UnitUpdate { Health = 40 }); return true; });
HealFrom(u[4], u[5], 20);
Check(!Has(u[5], "effect_class_brains_2_ammo_regen"), "a non-Brains healer does not grant Supply Line");

// ---- Brains III: build speed from the class card's perk_mods ----
Check(Math.Abs(u[7].GetBuff(BuffType.BuildSpeed) - 0.3f) < 0.001f && u[1].GetBuff(BuffType.BuildSpeed) == 0, "Brains III builds 30% faster, Brains II does not");

// ---- Brawn II: Bulwark ----
void Tick(ulong n) => ((Action)Call("OnTick", n)!)();
ulong tick = 1000;
void Ticks() { for (var i = 0; i < 4; i++) Tick(tick++); }
var bulwarkCenter = new Vector3(40, 4, 58);
OnZone(() =>
{
    Place(u[3], bulwarkCenter);
    Place(u[5], bulwarkCenter + new Vector3(5, 0, 0));
    Place(u[4], bulwarkCenter + new Vector3(0, 0, 5));
    Place(u[2], bulwarkCenter + new Vector3(12, 0, 0));
    Ticks();
    return true;
});
Check(Math.Abs(u[5].GetBuff(BuffType.Shield) - 0.15f) < 0.001f, "ally within 8m takes 15% less damage");
Check(u[4].GetBuff(BuffType.Shield) == 0 && !Has(u[4], "effect_class_brawn_2_guard"), "another Brawn in range does not receive Bulwark");
Check(u[3].GetBuff(BuffType.Shield) == 0, "the Bulwark Brawn does not protect itself");
Check(u[2].GetBuff(BuffType.Shield) == 0, "ally outside 8m is not protected");
OnZone(() => { Place(u[8], bulwarkCenter + new Vector3(3, 0, 0)); Ticks(); return true; });
Check(u[5].ActiveEffects.Count(e => e.Key == K("effect_class_brawn_2_guard")) == 1 && Math.Abs(u[5].GetBuff(BuffType.Shield) - 0.15f) < 0.001f,
    "two Bulwarks do not stack");
OnZone(() => { Place(u[3], bulwarkCenter + new Vector3(-20, 0, 0)); Ticks(); return true; });
Check(Math.Abs(u[5].GetBuff(BuffType.Shield) - 0.15f) < 0.001f, "protection stays while another Bulwark still covers the ally");
OnZone(() => { Place(u[8], bulwarkCenter + new Vector3(-20, 0, 3)); Ticks(); return true; });
Check(u[5].GetBuff(BuffType.Shield) == 0 && !Has(u[5], "effect_class_brawn_2_guard"), "leaving every aura removes the protection");
OnZone(() => { Place(u[3], bulwarkCenter); Ticks(); return true; });
var damaged = OnZone(() =>
{
    u[5].UpdateData(new UnitUpdate { Health = 100 });
    u[5].TakeDamage(new DamageData(0, 0, 20, 0, 0, 0, 0, 0, false, false, false, false), u[6].CreateImpactData(casterPlayerId: u[6].PlayerId), false, u[6], TeamType.Team2);
    return HP(u[5]);
});
Check(Math.Abs(damaged - 83) < 0.01f, "protected ally loses 17 health from a 20 damage hit");
OnZone(() => { u[5].AddEffect(new ConstEffectInfo(killerBunch.Key), TeamType.Team1, null); return true; });
Check(Math.Abs(u[5].GetBuff(BuffType.Shield) - 0.5f) < 0.001f, "Bulwark does not stack with a stronger shield");

// ---- Kills: Brawn III, Skill II, Skill III ----
var zoneData = (ZoneData)typeof(GameZone).GetField("_zoneData", Any)!.GetValue(zone)!;
OnZone(() => { foreach (var r in roster) zoneData.PlayerStats.TryAdd(r.Id, new MatchPlayerStats { Team = r.Team }); return true; });
void Kill(Unit killer, Unit victim, params Unit[] assisters) => OnZone(() =>
{
    victim.IsDead = false; victim.IsActive = true;
    victim.UpdateData(new UnitUpdate { Health = 100 });
    foreach (var a in assisters) victim.RecentDamagers[a] = DateTimeOffset.Now.AddSeconds(10);
    Call("UnitIsKilled", victim, killer.CreateImpactData(casterPlayerId: killer.PlayerId), false);
    return true;
});
Kill(u[11], u[6]);
Check(Math.Abs(u[11].GetBuff(BuffType.ToolWorldDamage) - 0.5f) < 0.001f && Math.Abs(u[11].GetBuff(BuffType.ObjectiveDamage) - 0.5f) < 0.001f,
    "Demolisher kill grants +50% dig tool and objective damage");
Check(u[11].ActiveEffects.Single(e => e.Key == K("effect_class_brawn_3_demolish")).TimestampEnd is { } end &&
      Math.Abs((long)end - DateTimeOffset.Now.AddSeconds(10).ToUnixTimeMilliseconds()) < 1000, "Demolisher lasts 10 seconds");
Check(u[3].GetBuff(BuffType.ToolWorldDamage) == 0, "other Brawns do not gain Demolisher");

Kill(u[12], u[6], u[9], u[10]);
Check(Math.Abs(u[9].GetBuff(BuffType.Shield) - 0.5f) < 0.001f, "Skill II assist triggers the assister's on-kill perk");
Check(u[10].GetBuff(BuffType.Shield) == 0, "an assist without Skill II triggers nothing");

var reduced = OnZone(() =>
{
    var now = DateTimeOffset.Now;
    u[13].IsDead = true; u[13].RespawnTime = now.AddSeconds(10);
    u[14].IsDead = true; u[14].RespawnTime = now.AddSeconds(10);
    u[2].IsDead = true; u[2].RespawnTime = now.AddSeconds(-1);
    typeof(GameZone).GetField("_respawnClock", Any)!.SetValue(zone, (Func<DateTimeOffset>)(() => now));
    return now;
});
Kill(u[12], u[6]);
Check(At(u[13].RespawnTime, reduced.AddSeconds(3)), "Skill III kill removes 70% of a dead teammate's remaining respawn time");
Check(u[14].RespawnTime is { } enemyAt && enemyAt > reduced.AddSeconds(9), "enemy respawn timers are untouched");
Check(At(u[2].RespawnTime, reduced.AddSeconds(-1)), "expired timers are left alone");
Check(zoneData.RespawnInfo[13] == (ulong)reduced.AddSeconds(3).ToUnixTimeMilliseconds(), "the shortened timer is published to clients");
OnZone(() => { u[12].IsDead = true; u[12].RespawnTime = reduced.AddSeconds(10); return true; });
Kill(u[12], u[6]);
Check(At(u[12].RespawnTime, reduced.AddSeconds(3)) && At(u[13].RespawnTime, reduced.AddSeconds(0.9)), "a posthumous kill shortens the killer's own timer too");
OnZone(() => { u[13].RespawnTime = reduced.AddSeconds(10); return true; });
Kill(u[5], u[6]);
Check(At(u[13].RespawnTime, reduced.AddSeconds(10)), "a kill without Skill III leaves timers alone");
OnZone(() =>
{
    var tuned = perkCards.Single(c => c.Id == ClassPerkCatalogue.Skill3Id);
    tuned.ClassPerk!.RespawnReductionPercent = 50;
    catalogue.Replicate(catalogue.All.Where(c => c.Id != tuned.Id).Append(tuned).ToList());
    u[12].IsDead = false;
    return true;
});
Kill(u[12], u[6]);
Check(At(u[13].RespawnTime, reduced.AddSeconds(5)), "CDB respawn reduction percent is used live");

zone.Stop();
Console.WriteLine($"BNL_CLASS_PERKS_EXPANDED_FIXTURE_OK checks={checks}");

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
