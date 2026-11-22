using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Octree_Extensions;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

if (args.Length != 2) throw new ArgumentException("<Reborn catalogue JSON> <decoded original catalogue JSON>");
int checks = 0, trapUnits = 0, trapCells = 0;
void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
byte[] Bytes(Card c) { using var m = new MemoryStream(); Card.WriteVariant(new BinaryWriter(m), c); return m.ToArray(); }
bool IsHistorical(Card c) => c.Id?.Contains("_time_assault_original_", StringComparison.Ordinal) == true;
var cards = File.ReadAllText(args[0]).Deserialize<List<Card>>()!;
var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(cards);
var historical = catalogue.All.Where(IsHistorical).ToArray();
Check(historical.Length == 29, "29 complete isolated trap/dependency cards");
var ordinary = catalogue.All.Where(c => !IsHistorical(c)).ToDictionary(c => c.Key, Bytes);
var original = JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray()
    .ToDictionary(c => c!["_id"]!.GetValue<string>(), c => c!);
var names = historical.ToDictionary(c => c.Id!.Replace("_time_assault_original_", "_"), c => c.Id!);
JsonNode? Rewrite(JsonNode? node)
{
    if (node is JsonObject obj) return new JsonObject(obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Rewrite(p.Value))));
    if (node is JsonArray array) return new JsonArray(array.Select(Rewrite).ToArray());
    if (node is JsonValue val && val.TryGetValue<string>(out var s) && names.TryGetValue(s, out var mapped)) return JsonValue.Create(mapped);
    return node?.DeepClone();
}
foreach (var card in historical)
{
    var id = card.Id!.Replace("_time_assault_original_", "_");
    var expected = Rewrite(original[id])!;
    if (card is CardBlock block) expected["block_id"] = block.BlockId;
    Check(Bytes(card).SequenceEqual(Bytes(expected.ToJsonString().Deserialize<Card>()!)), "all original stats/reference edges preserved: " + id);
}
var acid = new Key("unit_time_assault_original_device_eliza_acid_trap").GetCard<CardUnit>()!;
Check(((UnitDataLandmine)acid.Data!).TriggerRadius == 2, "course-authored Acid Trap is radius 2, not anomalous player lvl01 radius 3");
Check(new Key("unit_device_eliza_acid_trap").GetCard<CardUnit>()!.LandmineRadius() == 2.6f, "ordinary Acid Trap retains radius 2.6");
var updater = (UnitUpdater)RuntimeHelpers.GetUninitializedObject(typeof(UnitUpdater));
var created = new List<Unit>();
updater = updater with { OnUnitInit = (u, _) => created.Add(u), OnUnitUpdate = (_, _, _) => {},
    GetTeamEffects = _ => [], DoesObjBuffApply = (_, _) => false, EnqueueAction = _ => true };
const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
foreach (var course in CatalogueHelper.GlobalLogic.TimeTrial!.Courses!)
{
    Check(TimeAssaultCourses.TryLoad(course.Map, out _, out var map), "course loads");
    string before = JsonSerializer.Serialize(map, JsonHelper.DefaultSerializerSettings);
    var terrain = new MapBinary(map!.Schema, map.BlocksData!, map.Size, map.Properties?.PlanePosition ?? 0,
        new MapUpdater((_, _) => {}, (_, _) => {}, _ => {}, _ => true), t => TimeAssaultTraps.ApplyTerrain(t, MatchType.TimeTrial));
    var normalTerrain = new MapBinary(map.Schema, map.BlocksData!, map.Size, map.Properties?.PlanePosition ?? 0,
        new MapUpdater((_, _) => {}, (_, _) => {}, _ => {}, _ => true));
    var roundtrip = new MapBinary(terrain.ToBinary(), map.Properties?.PlanePosition ?? 0,
        new MapUpdater((_, _) => {}, (_, _) => {}, _ => {}, _ => true));
    for (int x=0; x<terrain.Size.x; x++) for (int y=0; y<terrain.Size.y; y++) for (int z=0; z<terrain.Size.z; z++)
    {
        var actual = terrain[x,y,z]; var normal = normalTerrain[x,y,z];
        if (actual.Id != normal.Id)
        {
            Check(actual.Card.Id!.Contains("_time_assault_original_"), "only historical trap cells remapped");
            Check(roundtrip[x,y,z].Id == actual.Id, "native map wire preserves high historical block IDs");
            Check(actual.Damage == normal.Damage && actual.VData == normal.VData && actual.LData == normal.LData, "cell metadata preserved");
            trapCells++;
        }
    }
    created.Clear();
    var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
    var match = catalogue.All.OfType<CardMatch>().First(c => c.Data?.Type == MatchType.TimeTrial);
    var zoneData = new ZoneData(new ZoneUpdater(_ => {})) { MatchKey = match.Key, GameModeKey = CatalogueHelper.ModeTimeTrial.Key, MapData = map, BlocksData = terrain };
    typeof(GameZone).GetField("_zoneData", flags)!.SetValue(zone, zoneData);
    typeof(GameZone).GetField("_defaultUnitUpdater", flags)!.SetValue(zone, updater);
    typeof(GameZone).GetMethod("CreateMapUnits", flags)!.Invoke(zone, null);
    Check(created.Count == map.Units.Count, "real zone initialization creates every map unit");
    for (int i=0; i<map.Units.Count; i++)
    {
        var authored = map.Units[i]; var spawned = created[i];
        var selected = TimeAssaultTraps.SelectMapUnit(authored, MatchType.TimeTrial);
        Check(spawned.Key == selected.UnitKey && spawned.Transform.Position == authored.Position && spawned.Team == authored.Team, "spawn preserves authored placement/team and selects scoped key");
        foreach (var mode in Enum.GetValues<MatchType>().Where(m => m != MatchType.TimeTrial))
            Check(ReferenceEquals(authored, TimeAssaultTraps.SelectMapUnit(authored, mode)), "non-TA unit untouched");
        if (spawned.UnitCard?.Data is UnitDataLandmine data)
        {
            Check(IsHistorical(spawned.UnitCard), "actual course trap uses historical card");
            Check(spawned.LandmineEffect!.Shape is BoundingSphere sphere && sphere.Radius == data.TriggerRadius, "real trigger sphere uses historical radius");
            trapUnits++;
        }
    }
    Check(JsonSerializer.Serialize(map, JsonHelper.DefaultSerializerSettings) == before, "shared authored map unchanged");
    created.Clear();
    zoneData.MatchKey = catalogue.All.OfType<CardMatch>().First(c => c.Data?.Type == MatchType.Tutorial).Key;
    typeof(GameZone).GetMethod("CreateMapUnits", flags)!.Invoke(zone, null);
    Check(created.Count == map.Units.Count && created.Select(u => u.Key).SequenceEqual(map.Units.Select(u=>u.UnitKey)),
        "real non-TA zone initialization keeps every original map-unit key");
    foreach (var mode in Enum.GetValues<MatchType>().Where(m => m != MatchType.TimeTrial))
    {
        var snapshot = normalTerrain.ToBytes();
        TimeAssaultTraps.ApplyTerrain(normalTerrain, mode);
        Check(snapshot.SequenceEqual(normalTerrain.ToBytes()), "non-TA terrain untouched");
    }
    Console.WriteLine($"COURSE {course.Map.GetCard<CardMap>()!.Id}: units={created.Count}");
}
Check(trapUnits > 500 && trapCells > 1000, "actual course coverage includes hundreds of proximity traps and ground traps");
catalogue.Replicate(cards);
Check(catalogue.All.Count(IsHistorical) == historical.Length, "replication is idempotent");
foreach (var (key, bytes) in ordinary) Check(bytes.SequenceEqual(Bytes(catalogue.GetCard(key)!)), "ordinary card unchanged on replication");
var conflicting = catalogue.All.Where(c => !IsHistorical(c)).Append(new CardBlock { Id = "fixture_conflicting_block", BlockId = 64000 }).ToList();
try { TimeAssaultTraps.Register(conflicting); throw new Exception("block-id collision accepted"); }
catch (InvalidDataException) { checks++; }
using (var zipped = new ZLibStream(new MemoryStream(CatalogueBlob.Current.Data), CompressionMode.Decompress))
using (var raw = new MemoryStream())
{
    zipped.CopyTo(raw); raw.Position = 0; using var reader = new BinaryReader(raw);
    Check(reader.ReadByte() == 0, "native CDB protocol version retained");
    var decoded = reader.ReadList<Card, List<Card>>(Card.ReadVariant);
    Check(raw.Position == raw.Length && decoded.Count(IsHistorical) == 29, "native wire catalogue contains complete historical dependency set");
    Check(decoded.Select(c => c.Id).Distinct().Count() == decoded.Count, "no duplicate wire card IDs");
    Check(decoded.OfType<CardBlock>().Select(c=>c.BlockId).Distinct().Count() == decoded.OfType<CardBlock>().Count(), "no duplicate wire block IDs");
}
Console.WriteLine($"TIME_ASSAULT_TRAPS PASS {checks} checks; {trapUnits} trap units; {trapCells} ground trap cells");

static class CardExtensions
{
    public static float LandmineRadius(this CardUnit c) => ((UnitDataLandmine)c.Data!).TriggerRadius;
    public static byte[] ToBytes(this MapBinary map) => map.ToBinary();
}
