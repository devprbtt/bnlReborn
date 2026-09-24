// Hero emotes must work in queue deathmatch, whose players are all Neutral on the wire, while team
// modes keep refusing Neutral units and every other emote rule still applies in both.
using System.Linq.Expressions;
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var catalogue = (ServerCatalogue)Databases.Catalogue;
var hero = new CardUnit { Id = "fixture_emote_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom };
catalogue.Replicate([hero]);
var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var updater = (UnitUpdater)constructor.Invoke(constructor.GetParameters().Select(p =>
{
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType),
        invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name))).Compile();
}).ToArray());
Unit Player(TeamType team) => new(1, new UnitInit { Key = hero.Key, Team = team, PlayerId = 1, OwnerId = 1 }, updater);

foreach (var ffa in new[] { false, true })
{
    var mode = ffa ? "deathmatch" : "team mode";
    Check(GameZone.CanStartHeroEmote(Player(TeamType.Team1), 0, ffa), $"{mode}: Team1 player can emote");
    Check(GameZone.CanStartHeroEmote(Player(TeamType.Team2), 3, ffa), $"{mode}: Team2 player can emote");
    Check(GameZone.CanStartHeroEmote(Player(TeamType.Neutral), 0, ffa) == ffa,
        $"{mode}: Neutral player {(ffa ? "can" : "cannot")} emote");

    var subject = Player(ffa ? TeamType.Neutral : TeamType.Team1);
    Check(!GameZone.CanStartHeroEmote(subject, -1, ffa) && !GameZone.CanStartHeroEmote(subject, 16, ffa), $"{mode}: index out of range refused");
    subject.IsDead = true;
    Check(!GameZone.CanStartHeroEmote(subject, 0, ffa), $"{mode}: dead player refused");
    subject = Player(ffa ? TeamType.Neutral : TeamType.Team1);
    subject.Transform.IsCrouch = true;
    Check(!GameZone.CanStartHeroEmote(subject, 0, ffa), $"{mode}: crouching player refused");
    subject = Player(ffa ? TeamType.Neutral : TeamType.Team1);
    subject.Transform.SetLocalVelocity(new Vector3(1f, 0f, 0f));
    Check(!GameZone.CanStartHeroEmote(subject, 0, ffa), $"{mode}: moving player refused");
    subject = Player(ffa ? TeamType.Neutral : TeamType.Team1);
    subject.Transform.SetLocalVelocity(new Vector3(0f, -5f, 0f));
    Check(GameZone.CanStartHeroEmote(subject, 0, ffa), $"{mode}: vertical-only velocity allowed");
}

Console.WriteLine($"Hero emote fixture passed: {checks} checks.");
