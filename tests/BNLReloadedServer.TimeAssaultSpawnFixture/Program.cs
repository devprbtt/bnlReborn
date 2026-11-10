using System.Numerics;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Octree_Extensions;

var cards = JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), JsonHelper.DefaultSerializerSettings)!;
((ServerCatalogue)Databases.Catalogue).Replicate(cards);
const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
var positionMethod = typeof(GameZone).GetMethod("GetSpawnPosition", flags)!;
var defaultMethod = typeof(GameZone).GetMethod("GetDefaultOrRandomSpawn", flags)!;
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
foreach (var course in CatalogueHelper.GlobalLogic.TimeTrial!.Courses!)
{
    Check(TimeAssaultCourses.TryLoad(course.Map, out _, out var map), "actual course loads");
    var start = map!.SpawnPoints.First(p => p.Team == TeamType.Team1 && p.Label == SpawnPointLabel.Base);
    Check(map.SpawnPoints.Count == 1, "current course has exactly one authored start");
    var match = cards.OfType<CardMatch>().First(c => c.Data?.Type == MatchType.TimeTrial);
    var blocks = new MapBinary(map.Schema, map.BlocksData!, map.Size, map.Properties?.PlanePosition ?? 0,
        new MapUpdater((_, _) => {}, (_, _) => {}, _ => {}, _ => true));
    var expected = start.Position + new Vector3(0, .08f, 0);
    Check(blocks[(Vector3s)expected].Card.Passable == BlockPassableType.Any &&
        blocks[(Vector3s)(expected + Vector3.UnitY)].Card.Passable == BlockPassableType.Any,
        "authored start has foot and head clearance");
    for (uint player = 1; player <= 16; player++)
    {
        // Reconstruct zone state for separate players/restarts; no random seed or process cache.
        var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
        var data = new ZoneData(new ZoneUpdater(_ => {})) { MatchKey = match.Key, GameModeKey = Key.None, MapData = map, BlocksData = blocks };
        data.PlayerSpawnPoints[player] = 7u;
        typeof(GameZone).GetField("_zoneData", flags)!.SetValue(zone, data);
        var defaults = new uint[Enum.GetValues<TeamType>().Length]; defaults[(int)TeamType.Team1] = 7;
        typeof(GameZone).GetField("_defaultSpawnId", flags)!.SetValue(zone, defaults);
        Check((uint)defaultMethod.Invoke(zone, new object?[] { TeamType.Team1, null })! == 7, "default authored marker retained");
        foreach (float radius in new[] { 0f, 2f, 5f })
            Check((Vector3)positionMethod.Invoke(zone, [start.Position, radius])! == expected, "exact spawn across players/restarts/radii");
        zone.ReceivedSelectSpawnPoint(player, 123);
        zone.ReceivedSelectSpawnPoint(player, null);
        Check(data.PlayerSpawnPoints[player] == 7u, "alternate and deselect requests cannot change course start");
    }
    // Exercise the unchanged non-Time-Assault scatter path on the same terrain.
    var normal = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
    var normalData = new ZoneData(new ZoneUpdater(_ => {})) { MatchKey = cards.OfType<CardMatch>().First(c => c.Data?.Type == MatchType.Tutorial).Key, GameModeKey = Key.None, MapData = map, BlocksData = blocks };
    typeof(GameZone).GetField("_zoneData", flags)!.SetValue(normal, normalData);
    typeof(GameZone).GetField("_unitOctree", flags)!.SetValue(normal, new BoundsOctreeEx<Unit>(256, new Vector3(128), 1, 1.2f));
    var samples = Enumerable.Range(0, 128).Select(_ => (Vector3)positionMethod.Invoke(normal, [start.Position, 2f])!).ToHashSet();
    Check(samples.Count > 1, "non-TA spawn scatter retained");
    Console.WriteLine($"{course.Map.GetCard<CardMap>()!.Id}: fixed {expected}, facing {start.Direction}; other-mode positions {samples.Count}");
}
Console.WriteLine($"TIME_ASSAULT_SPAWN PASS {checks} checks");
