using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
if(args.Length!=1)throw new ArgumentException("Pass a CDB catalogue JSON export to test incremental updates.");
int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
var maps=Enumerable.Range(0,6).Select(i=>new CardMap{Id="vote_fixture_"+i,Key=new Key("vote_fixture_"+i)}).ToArray();
foreach(double? input in new double?[]{null,0,-1,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
 Check(MapVoteSelection.ValidWeight(input)==1,"missing/invalid weight defaults to one: "+input);
Check(MapVoteSelection.ValidWeight(.5)==.5 && MapVoteSelection.ValidWeight(4)==4,"positive fractional and boosted weights are retained");
var duplicates=maps.Concat(Enumerable.Repeat(maps[0],20));
Check(MapVoteSelection.Select(maps,3,GameRankingType.Friendly,new Random(42)).SequenceEqual(MapVoteSelection.Select(duplicates,3,GameRankingType.Friendly,new Random(42))),"duplicate pool entries confer no advantage or duplicate slots");
Check(MapVoteSelection.Select(maps,20,GameRankingType.Friendly,new Random(2)).Distinct().Count()==6,"short pools return only distinct available maps");
Check(MapVoteSelection.Select([],3,GameRankingType.Friendly,new Random(2)).Count==0 && MapVoteSelection.Select(maps,0,GameRankingType.Friendly,new Random(2)).Count==0,"empty pool and zero selection are safe");
const int trials=20000;
int Count(GameRankingType ranking){var random=new Random(781);int hits=0;for(int i=0;i<trials;i++){var ballot=MapVoteSelection.Select(maps,3,ranking,random);if(ballot.Count!=3 || ballot.Distinct().Count()!=3)throw new Exception("duplicate/incomplete ballot");if(ballot.Contains(maps[0].Key))hits++;}return hits;}
int normal=Count(GameRankingType.Friendly);Check(Math.Abs(normal/(double)trials-.5)<.02,"equal weights give equal three-of-six inclusion chance");
maps[0].CasualVoteWeight=4;
int boosted=Count(GameRankingType.Friendly);
// Independent closed-form probability of missing the boosted map on all three draws.
double expected=1-(5d/9)*(4d/8)*(3d/7);
Check(Math.Abs(boosted/(double)trials-expected)<.02 && boosted>normal,"weight four matches weighted-without-replacement inclusion probability");
Check(Count(GameRankingType.Graveyard)==boosted,"both casual rankings use weights");
Check(Count(GameRankingType.Ranked)==normal && Count(GameRankingType.None)==normal,"ranked and custom automatic selection stay uniform");
maps[0].CasualVoteWeight=double.MaxValue;maps[1].CasualVoteWeight=double.MaxValue;
Check(MapVoteSelection.Select(maps,6,GameRankingType.Friendly,new Random(4)).Distinct().Count()==6,"very large finite weights never overflow or lose remaining maps");
var json="{\"_id\":\"vote_fixture_cdb\",\"category\":\"map\",\"casual_vote_weight\":4}";
var card=JsonSerializer.Deserialize<Card>(json,JsonHelper.DefaultSerializerSettings) as CardMap;
Check(card?.CasualVoteWeight==4,"CDB snake-case property parses through real Card factory");
Check(JsonSerializer.Serialize<Card>(card!,JsonHelper.DefaultSerializerSettings).Contains("casual_vote_weight"),"CDB round trip retains weight");
byte[] Wire(CardMap c){using var stream=new MemoryStream();c.Write(new BinaryWriter(stream));return stream.ToArray();}
var first=Wire(card!);card!.CasualVoteWeight=1;
Check(first.SequenceEqual(Wire(card)),"changing weight leaves legacy client wire bytes identical");
var db=(ServerCatalogue)Databases.Catalogue;
var catalogue=JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]),JsonHelper.DefaultSerializerSettings)!;
catalogue.RemoveAll(c=>c.Id==card.Id);catalogue.Add(card);db.Replicate(catalogue);
var before=MapVoteSelection.Select(db.All.OfType<CardMap>().Where(c=>c.Id=="vote_fixture_cdb"),3,GameRankingType.Friendly,new Random(1));
card=JsonSerializer.Deserialize<Card>(json,JsonHelper.DefaultSerializerSettings) as CardMap;db.UpdateCard(card!);
Check(db.GetCard<CardMap>(new Key("vote_fixture_cdb"))!.CasualVoteWeight==4 && before.Count==1,"catalogue incremental refresh exposes weight to future ballots");
Console.WriteLine($"Map vote fixture passed: {checks} checks; uniform={normal/(double)trials:P2}, boosted={boosted/(double)trials:P2}, expected={expected:P2}");
