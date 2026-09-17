using System.Reflection;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

var rows = File.ReadAllLines(args[0]).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => JsonDocument.Parse(l)).ToList();
var cards = rows.Where(d => d.RootElement.TryGetProperty("eligible_card", out _)).Select(d => {
    var r=d.RootElement; var id=r.GetProperty("eligible_card").GetString()!;
    if (!r.GetProperty("installed").GetBoolean()) throw new Exception("Missing map: "+id);
    return new CardMap { Id=id, Key=new Key(id), CasualVoteWeight=r.GetProperty("weight").GetDouble() };
}).ToArray();
if (cards.Length!=18 || cards.Count(c=>c.Id!.EndsWith("_conquest") && c.CasualVoteWeight==3)!=2)
    throw new Exception("Live pool shape changed; recalculate analytical expectation");
var select=typeof(GameInstance).Assembly.GetType("BNLReloadedServer.ServerTypes.MapVoteSelection")!
    .GetMethod("Select",BindingFlags.Static|BindingFlags.NonPublic)!;
var random=new Random(20260917); int hits=0; var counts=cards.ToDictionary(c=>c.Key,c=>0);
for(int i=0;i<100000;i++) {
    var ballot=(List<Key>)select.Invoke(null,new object[]{cards,3,GameRankingType.Friendly,random})!;
    if(ballot.Count!=3 || ballot.Distinct().Count()!=3)throw new Exception("Bad ballot");
    foreach(var key in ballot)counts[key]++;
    if(ballot.Any(k=>cards.Single(c=>c.Key==k).Id!.EndsWith("_conquest")))hits++;
}
double expected=1-(16d/22)*(15d/21)*(14d/20);
if(Math.Abs(hits/100000d-expected)>.01)throw new Exception("Distribution mismatch");
Console.WriteLine($"PASS 100000 three-distinct-map ballots: Conquest offered {hits/100000d:P3}; expected {expected:P3}");
foreach(var c in cards.Where(c=>c.Id!.EndsWith("_conquest")))
 Console.WriteLine($"{c.Id}: weight={c.CasualVoteWeight}, offered={counts[c.Key]}/100000");
Console.WriteLine($"Five independent ballots with neither Conquest map: {Math.Pow(1-expected,5):P3}. This is not the probability that no Conquest match is played.");
