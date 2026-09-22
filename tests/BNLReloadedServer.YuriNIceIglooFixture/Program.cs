using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

var checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}

var center = new Vector3s(20, 10, 30);
var placements = YuriNIceIgloo.Build(center);
var positions = placements.Select(p => p.Position).ToHashSet();

Check(placements.Count == 67 && positions.Count == 67, "igloo has 67 unique snow blocks");

for (var y = 0; y <= 1; y++)
{
    for (var x = -2; x <= 2; x++)
    for (var z = -2; z <= 2; z++)
    {
        var present = positions.Contains(new Vector3s(center.x + x, center.y + y, center.z + z));
        Check(present == (Math.Max(Math.Abs(x), Math.Abs(z)) == 2),
            $"wall/interior layout at y={y}, x={x}, z={z}");
    }
}

for (var x = -2; x <= 2; x++)
for (var z = -2; z <= 2; z++)
    Check(positions.Contains(new Vector3s(center.x + x, center.y + 2, center.z + z)),
        $"sealed roof at x={x}, z={z}");

for (var x = -1; x <= 1; x++)
for (var z = -1; z <= 1; z++)
    Check(positions.Contains(new Vector3s(center.x + x, center.y + 3, center.z + z)),
        $"stepped crown at x={x}, z={z}");

Check(positions.Contains(new Vector3s(center.x, center.y + 4, center.z)), "top cap exists");

var placed = new HashSet<Vector3s>();
foreach (var placement in placements)
{
    Check(placement.AttachTo.y == center.y - 1 || placed.Contains(placement.AttachTo),
        $"{placement.Position} attaches to foundation or an earlier block");
    placed.Add(placement.Position);
}

foreach (var exit in new[]
         {
             new Vector3s(center.x - 2, center.y, center.z),
             new Vector3s(center.x + 2, center.y, center.z),
             new Vector3s(center.x, center.y, center.z - 2),
             new Vector3s(center.x, center.y, center.z + 2)
         })
    Check(positions.Contains(exit) && positions.Contains(exit + Vector3s.Up), $"cardinal exit {exit} is sealed");

var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate([
    new CardBlock { Id = "block_air", BlockId = 0, Replaceable = true },
    new CardBlock { Id = "fixture_ground", BlockId = 1, Solid = true },
    new CardBlock
    {
        Id = YuriNIceIgloo.BlockId,
        BlockId = 60,
        Solid = true,
        Destructible = true,
        Health = new Health { MaxHealth = 10, HealthType = HealthType.World }
    }
]);

var mapSize = new Vector3s(16, 12, 16);
var raw = new byte[mapSize.x * mapSize.y * mapSize.z * BlockBinary.Size];
for (var x = 0; x < mapSize.x; x++)
for (var z = 0; z < mapSize.z; z++)
{
    var index = ((x * mapSize.y) * mapSize.z + z) * BlockBinary.Size;
    raw[index] = 1;
}

var map = new MapBinary(6, raw.Zip(0).ToArray(), mapSize, -1,
    new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => true));
var mapCenter = new Vector3s(8, 1, 8);
var placedUpdates = new Dictionary<Vector3s, BlockUpdate>();
foreach (var placement in YuriNIceIgloo.Build(mapCenter))
foreach (var update in map.AddBlock(YuriNIceIgloo.BlockKey, placement.Position, placement.AttachTo,
             Direction2D.Left, null))
    placedUpdates[update.Key] = update.Value;

Check(placedUpdates.Count == 67, "ordinary stability-checked placement accepts the full igloo");
Check(YuriNIceIgloo.Build(mapCenter).All(p => map[p.Position].Id == 60),
    "all planned cells contain snow after placement");
Check(map[mapCenter].IsAir && map[mapCenter + Vector3s.Up].IsAir,
    "player-height center chamber remains empty");

Console.WriteLine($"Yuri 'n Ice igloo suite passed: {checks} checks.");
