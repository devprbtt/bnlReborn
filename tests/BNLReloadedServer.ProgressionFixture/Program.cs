using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using SQLite;

// Score-based progression: the level curve, per-hero score attribution, the region-to-master hero XP record and the
// one-time reset. Curves are the 2026-10-23 catalogue values.
var checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); checks++; Console.WriteLine("PASS " + message); }

var hero = new ExpLogic { FlatCoeff = 400, MultCoeff = 245, PowerCoeff = 0.74f, MaxLevel = 200 };
var player = new ExpLogic { FlatCoeff = 580, MultCoeff = 333, PowerCoeff = 0.85f, MaxLevel = null };
XpInfo Start(ExpLogic logic) => new() { Level = 1, LevelXp = 0, XpForNextLevel = CatalogueHelper.XpStep(logic, 1) };

// ---- Curve ----
Check(CatalogueHelper.XpStep(hero, 1) == 400 && CatalogueHelper.XpStep(player, 1) == 580, "level 1 costs the flat amount (it used to cost 0)");
Check(Math.Abs(CatalogueHelper.XpStep(hero, 2) - 645) < .01, "level 2 costs flat + mult");
var almost = CatalogueHelper.AddXp(Start(hero), 399, hero);
Check(almost is { Level: 1, LevelXp: 399 } && almost.XpForNextLevel == 400, "XP short of a level stays in the bar");
var exact = CatalogueHelper.AddXp(Start(hero), 400, hero);
Check(exact is { Level: 2, LevelXp: 0 } && Math.Abs(exact.XpForNextLevel - CatalogueHelper.XpStep(hero, 2)) < .01, "exact step levels up with an empty bar");
double cumulative = 0; for (var level = 1; level < 10; level++) cumulative += CatalogueHelper.XpStep(hero, level);
var ten = CatalogueHelper.AddXp(Start(hero), (float)cumulative + 5, hero);
Check(ten.Level == 10 && Math.Abs(ten.LevelXp - 5) < .5, "one big award climbs several levels: hero 10 after " + Math.Round(cumulative) + " XP");
Check(Math.Abs(cumulative - 9394) < 10, "hero level 10 costs about 9.4k XP, about 5 median matches");
var split = CatalogueHelper.AddXp(CatalogueHelper.AddXp(Start(hero), 3000, hero), 3000, hero);
var once = CatalogueHelper.AddXp(Start(hero), 6000, hero);
Check(split.Level == once.Level && Math.Abs(split.LevelXp - once.LevelXp) < .5, "two awards reach the same place as one");
var top = CatalogueHelper.AddXp(new XpInfo { Level = 199, LevelXp = 0, XpForNextLevel = CatalogueHelper.XpStep(hero, 199) }, 1e9f, hero);
Check(top.Level == 200 && top.LevelXp == top.XpForNextLevel, "heroes stop at level 200 with a full bar");
Check(CatalogueHelper.AddXp(top, 50000, hero) is { Level: 200 }, "XP past the hero cap is discarded");
Check(CatalogueHelper.AddXp(Start(player), 1e9f, player).Level > 200, "player level has no cap");
Check(CatalogueHelper.AddXp(Start(hero), float.NaN, hero) is { Level: 1, LevelXp: 0 } && CatalogueHelper.AddXp(Start(hero), -50, hero) is { Level: 1, LevelXp: 0 },
    "invalid or negative XP adds nothing");
Check(CatalogueHelper.AddXp(new XpInfo { Level = 0, LevelXp = -3 }, 0, hero) is { Level: 1, LevelXp: 0 }, "a corrupt level 0 record reads as level 1");

// ---- Per-hero attribution ----
Key abe = new("unit_hero_abe"), sarge = new("unit_hero_sarge_stone");
var current = abe;
var ledger = new ActionScoreLedger(new ActionScoreRules()) { HeroOf = id => id == 1 ? current : null };
ledger.Grant(1, "Kill", 50, false);
ledger.Grant(1, "Assist", 25, true);   // plus the objective bonus, earned on the same hero
current = sarge;
ledger.Grant(1, "Kill", 50, false);
ledger.Grant(2, "Kill", 50, false);    // a player whose hero is unknown still scores, without hero XP
var byHero = ledger.HeroTotals(1);
Check(byHero[abe] == 50 + 25 + 6 && byHero[sarge] == 50 && byHero.Values.Sum() == ledger.Total(1),
    "score is credited to the hero it was earned on, including objective bonuses");
Check(ledger.Total(2) == 50 && ledger.HeroTotals(2).Count == 0, "score without a known hero counts for the player only");

// ---- Region to master: XP for heroes switched away from ----
var results = new EndMatchResults { PlayerId = 7, OtherHeroXp = new() { [abe] = 105.3f, [sarge] = 40 } };
using (var stream = new MemoryStream())
{
    results.Write(new BinaryWriter(stream)); stream.Position = 0;
    var read = new EndMatchResults(); read.Read(new BinaryReader(stream));
    Check(read.PlayerId == 7 && read.OtherHeroXp.Count == 2 && Math.Abs(read.OtherHeroXp[abe] - 105.3f) < .001 && read.OtherHeroXp[sarge] == 40
          && stream.Position == stream.Length, "per-hero XP survives the region-to-master record");
}

// ---- One-time reset ----
var file = Path.Combine(Path.GetTempPath(), "bnl-progression-fixture-" + Guid.NewGuid().ToString("N") + ".db");
var db = new SQLiteAsyncConnection(file);
try
{
    await db.CreateTableAsync<PlayerRecord>();
    await db.CreateTableAsync<AppliedMigrationRecord>();
    byte[] legacy = [1, 2, 3], reset = [9, 9];
    for (uint i = 1; i <= 3; i++) await db.InsertAsync(new PlayerRecord { SteamId = 1000 + i, Progression = legacy });
    Check(await MasterServerDatabase.ResetProgression(db, reset) == 3, "the reset rewrites every account");
    Check((await db.Table<PlayerRecord>().ToListAsync()).All(p => p.Progression.SequenceEqual(reset)), "every account holds the level 1 progression");
    await db.ExecuteAsync("UPDATE Users SET progression = ? WHERE id = 1", new byte[] { 4 });
    Check(await MasterServerDatabase.ResetProgression(db, reset) == null, "the reset is recorded and never runs again");
    Check((await db.FindAsync<PlayerRecord>(1u)).Progression.SequenceEqual(new byte[] { 4 }), "progress earned after the reset is kept on later starts");
}
finally { await db.CloseAsync(); File.Delete(file); }

Console.WriteLine($"Progression fixture passed: {checks} checks.");

// Optional: the live catalogue after tools/score_progression_catalogue.py, as the server would load it.
if (args.Length > 0)
{
    var cards = System.Text.Json.JsonSerializer.Deserialize<List<Card>>(await File.ReadAllTextAsync(args[0]),
        BNLReloadedServer.ProtocolHelpers.JsonHelper.DefaultSerializerSettings)!;
    var problems = CatalogueValidator.Validate(cards);
    Check(problems.Count == 0, "the transformed catalogue validates: " + string.Join("; ", problems.Take(5)));
    var global = cards.OfType<CardGlobalLogic>().Single();
    Check(global.XpLogic?.HeroXp is { FlatCoeff: 400, MaxLevel: 200 } && global.XpLogic.PlayerXp is { FlatCoeff: 580, MaxLevel: null }
          && global.XpLogic.HeroXp.LevelRewards is not { Count: > 0 } && global.XpLogic.PlayerXp.EveryLevelRewards is not { Count: > 0 },
        "the server reads the new curves and no level rewards");
    var modes = cards.OfType<CardGameMode>().ToList();
    Check(modes.All(m => m.HeroesReqs == null && m.MinPlayerLevel == null), "no game mode requires a player or hero level");
    Check(modes.Where(m => m.RewardLogic?.Bonuses?.ContainsKey(MatchRewardBonusType.Victory) == true)
          .All(m => Math.Abs(m.RewardLogic!.Bonuses![MatchRewardBonusType.Victory] - 0.3f) < 1e-6), "every victory bonus is 30 %");
    Console.WriteLine($"Progression fixture passed with catalogue: {checks} checks.");
}
