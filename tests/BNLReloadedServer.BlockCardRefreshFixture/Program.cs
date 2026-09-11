using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
Check(CatalogueValidator.Validate(cards).Count == 0, "fixture catalogue validates");
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(cards);
foreach (string id in new[] { "block_caltrops", "block_caltrops_astro", "block_caltrops_kreepy" })
{
    var original = catalogue.GetCard<CardBlock>(id)!;
    var placed = new Block(original.BlockId);
    Check(ReferenceEquals(placed.Card, original), id + " initial lookup");
    var updated = JsonSerializer.Deserialize<CardBlock>(JsonSerializer.Serialize(original, JsonHelper.DefaultSerializerSettings), JsonHelper.DefaultSerializerSettings)!;
    updated.BaseCost = 123; updated.Health = new Health { MaxHealth = 789, Toughness = 17 }; updated.SplashResistance = 23;
    catalogue.UpdateCard(updated);
    Check(ReferenceEquals(placed.Card, updated) && placed.Card.Health!.MaxHealth == 789 && placed.Card.SplashResistance == 23,
        id + " existing block sees health/resistance update alongside cost");
    Check(catalogue.GetCard<CardBlock>(id)!.BaseCost == placed.Card.BaseCost, id + " key/id lookups agree");
    catalogue.RemoveCard(id);
    Check(placed.Card.BlockId == 0, id + " removal drops stale definition and preserves air fallback");
    catalogue.UpdateCard(original);
    Check(ReferenceEquals(placed.Card, original), id + " re-add refreshes lookup");
}
var oldCaltrops = catalogue.GetCard<CardBlock>("block_caltrops")!;
ushort oldId = oldCaltrops.BlockId;
ushort freeId = Enumerable.Range(1, 65535).Select(i => (ushort)i).First(i => !catalogue.All.OfType<CardBlock>().Any(c => c.BlockId == i));
var moved = JsonSerializer.Deserialize<CardBlock>(JsonSerializer.Serialize(oldCaltrops, JsonHelper.DefaultSerializerSettings), JsonHelper.DefaultSerializerSettings)!;
moved.BlockId = freeId;
catalogue.UpdateCard(moved);
Check(BlockCardsCache.GetCard(oldId).BlockId == 0 && ReferenceEquals(BlockCardsCache.GetCard(freeId), moved), "block-id change removes old index entry");
catalogue.Replicate(cards);
Check(ReferenceEquals(BlockCardsCache.GetCard(oldId), oldCaltrops) && BlockCardsCache.GetCard(freeId).BlockId == 0, "full replication rebuilds index");
var stable = new Block(oldId);
Parallel.Invoke(
    () => { for (int i = 0; i < 10; i++) catalogue.Replicate(cards); },
    () => { for (int i = 0; i < 100000; i++) if (stable.Card.BlockId != oldId) throw new Exception("Reader observed incomplete block index"); });
Check(true, "concurrent reads never observe a partially built index");
