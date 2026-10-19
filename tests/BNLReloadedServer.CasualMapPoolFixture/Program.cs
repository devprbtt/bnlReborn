using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
var units = new Dictionary<Key, CardUnit>();
foreach (var label in new[] { UnitLabel.Base, UnitLabel.Line1, UnitLabel.Line2, UnitLabel.Line3 })
    units[new Key(label.ToString())] = new CardUnit { Labels = [label] };
MapData Data(int cubes) => new() { Units = new[] { TeamType.Team1, TeamType.Team2 }
    .SelectMany(team => units.Keys.Take(cubes).Select(key => new MapUnit { UnitKey = key, Team = team })).ToList() };
int Count(CardMap map) => CasualMapPool.CountCubes(map.Data, k => units.GetValueOrDefault(k));
var maps = Enumerable.Range(1, 4).Select(n => new CardMap {
    Id = "map_" + n, Key = new Key("map_" + n), Data = Data(n), CasualVoteWeight = n }).ToArray();
for (int i = 0; i < maps.Length; i++) Check(Count(maps[i]) == i + 1, "per-team cube count includes base: " + (i + 1));
var unknown = new MapData { Units = [new() { UnitKey = new Key("unknown"), Team = TeamType.Team1 }] };
Check(CasualMapPool.CountCubes(unknown, k => units.GetValueOrDefault(k)) == 0, "unknown units are not objectives");
Check(CasualMapPool.CountCubes(null, k => units.GetValueOrDefault(k)) == 0, "missing metadata is not misclassified as one cube");
var four = CasualMapPool.Filter(maps, GameRankingType.Friendly, 4, Count).ToArray();
var five = CasualMapPool.Filter(maps, GameRankingType.Friendly, 5, Count).ToArray();
Check(four.SequenceEqual(maps), "4v4 retains every configured map in order");
Check(five.SequenceEqual(maps.Skip(1)), "5v5 excludes only one-cube maps");
Check(five.Select(m => m.CasualVoteWeight).SequenceEqual(new double?[] { 2, 3, 4 }), "weights preserved");
foreach (var ranking in new[] { GameRankingType.Ranked, GameRankingType.None, GameRankingType.Graveyard })
    Check(CasualMapPool.Filter(maps, ranking, 5, Count).SequenceEqual(maps), ranking + " rotation unchanged");
Check(CasualMapPool.Filter([], GameRankingType.Friendly, 5, Count).Count() == 0, "empty configured pool stays empty");
Check(!CasualMapPool.Filter([maps[0]], GameRankingType.Friendly, 5, Count).Any(), "no fallback reintroduces excluded maps");
var random = new Random(42);
for (int i = 0; i < 1000; i++) {
    var ballot = MapVoteSelection.Select(five, 3, GameRankingType.Friendly, random);
    if (ballot.Contains(maps[0].Key) || ballot.Distinct().Count() != 3) throw new Exception("invalid 5v5 ballot");
}
Check(true, "1000 actual weighted ballots contain no one-cube maps or duplicates");
var mode = new CardGameMode { PlayersPerTeam = 4 };
var initiator = new MatchmakerInitiator(mode, [], [], 5);
Check(initiator.PlayersPerTeam == 5, "5v5 team size survives empty/shorthanded roster on 4v4 mode card");
var snapshot = new PublicPlaySnapshot(0, [], [], [new("map_1", "One")], []) { Friendly5v5Maps = [new("map_2", "Two")] };
var json = JsonSerializer.Serialize(snapshot, JsonHelper.DefaultSerializerSettings);
Check(json.Contains("\"friendly5v5_maps\"") && json.Contains("\"friendly_maps\""), "public JSON matches new and legacy client fields");
Console.WriteLine($"PASS {checks} checks");
