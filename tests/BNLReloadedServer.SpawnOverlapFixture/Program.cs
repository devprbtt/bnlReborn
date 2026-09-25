// Players who choose the same spawn at the same moment must never be placed inside each other.
// Respawn devices have no side shift, so every spawner used to get the device's exact position; the fixture
// spawns players one after another onto real units, checks each landing is free, standable and apart
// from everyone already there, and pins the octree duplicate that left phantom bodies after a respawn.
using System.Linq.Expressions;
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using Octree;

const ushort AirId = 0, FloorId = 1;
const int SizeX = 16, SizeY = 8, SizeZ = 16, FloorTop = 3;
// Client player CharacterController radius 0.32: two bodies overlap when their centres are closer than this.
const float MinSeparation = 0.64f;

int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

var catalogue = (ServerCatalogue)Databases.Catalogue;
var hero = new CardUnit { Id = "fixture_spawn_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom };
catalogue.Replicate([
    hero,
    new CardBlock { Id = "fixture_air", BlockId = AirId, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardBlock { Id = "fixture_floor", BlockId = FloorId, Passable = BlockPassableType.None, Solid = true, Grounded = true }
]);

var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var updater = (UnitUpdater)constructor.Invoke(constructor.GetParameters().Select(p =>
{
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType),
        invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name))).Compile();
}).ToArray());

MapBinary BuildMap(Func<int, int, int, bool> solid)
{
    var data = new byte[6 + SizeX * SizeY * SizeZ * 6];
    BitConverter.TryWriteBytes(data.AsSpan(0, 2), (ushort)SizeX);
    BitConverter.TryWriteBytes(data.AsSpan(2, 2), (ushort)SizeY);
    BitConverter.TryWriteBytes(data.AsSpan(4, 2), (ushort)SizeZ);
    for (var x = 0; x < SizeX; x++)
    for (var y = 0; y < SizeY; y++)
    for (var z = 0; z < SizeZ; z++)
    {
        var id = y < FloorTop || solid(x, y, z) ? FloorId : AirId;
        BitConverter.TryWriteBytes(data.AsSpan(6 + ((x * SizeY + y) * SizeZ + z) * 6, 2), id);
    }
    return new MapBinary(data.Zip(0).ToArray(), 0f, new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => true));
}

uint nextId = 1;
Unit PlayerAt(Vector3 feet)
{
    var id = nextId++;
    var unit = new Unit(id, new UnitInit { Key = hero.Key, Team = TeamType.Team1, PlayerId = id, OwnerId = id }, updater);
    unit.Transform.Position = feet;
    return unit;
}

bool Standable(MapBinary map, Vector3 feet)
{
    var cell = new Vector3s((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z));
    return map.ContainsBlock(cell) && map[cell].Card.Passable is BlockPassableType.Any &&
           map[cell + new Vector3s(0, 1, 0)].Card.Passable is BlockPassableType.Any;
}

// Spawns `count` players in turn at one point, each seeing the bodies placed before it, as the tick loop does.
List<Vector3> SpawnInTurn(MapBinary map, Vector3 spawnPoint, float radius, int count, int seed)
{
    var rand = new Random(seed);
    var placed = new List<Unit>();
    var landings = new List<Vector3>();
    for (var i = 0; i < count; i++)
    {
        var blocked = map.GetContainedInUnits(placed);
        var feet = radius < 1
            ? GameZone.PickExactSpawnPosition(map, blocked, spawnPoint, rand)
            : GameZone.PickAreaSpawnPosition(map, blocked, spawnPoint, radius, rand);
        landings.Add(feet);
        placed.Add(PlayerAt(feet));
    }
    return landings;
}

float MinPairDistance(List<Vector3> feet) =>
    feet.SelectMany((a, i) => feet.Skip(i + 1).Select(b => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z)))).DefaultIfEmpty(float.MaxValue).Min();

var device = new Vector3(8.5f, FloorTop, 8.5f); // respawn device bottom-centre, as GameZone derives it

// Baseline: the pre-fix rule returned this for every spawner, so any two players shared a body.
var oldRule = device with { Y = device.Y + 0.08f };

var open = BuildMap((_, _, _) => false);
var first = SpawnInTurn(open, device, 0, 1, 1)[0];
Check(first == oldRule, "a lone spawner still lands exactly on the device");

var pair = SpawnInTurn(open, device, 0, 2, 1);
Check(pair[0] == oldRule, "first of two keeps the exact spot");
Check(pair[1] != pair[0], "second of two no longer lands on the first (the reported bug)");
Check(MinPairDistance(pair) >= MinSeparation, $"two spawners are apart (min {MinPairDistance(pair):F2})");
Check(MathF.Abs(pair[1].X - pair[0].X) + MathF.Abs(pair[1].Z - pair[0].Z) == 1, "second takes an adjacent side cell before a diagonal");
Check(pair[1].Y == pair[0].Y, "second spawns at the device's height");

for (var seed = 0; seed < 50; seed++)
{
    var nine = SpawnInTurn(open, device, 0, 9, seed);
    if (nine.Distinct().Count() != 9 || MinPairDistance(nine) < MinSeparation || !nine.All(f => Standable(open, f)))
        Check(false, $"nine spawners on an open device stay apart and standable (seed {seed})");
}
Check(true, "nine spawners on an open device stay apart and standable (50 seeds)");

// Corridor one block wide along x: the only free neighbours are ahead and behind, never inside the walls.
var corridor = BuildMap((_, y, z) => y >= FloorTop && z != 8);
for (var seed = 0; seed < 50; seed++)
{
    var three = SpawnInTurn(corridor, device, 0, 3, seed);
    if (!three.All(f => Standable(corridor, f)) || MinPairDistance(three) < MinSeparation)
        Check(false, $"corridor spawners stay out of the walls (seed {seed})");
}
Check(true, "corridor spawners stay out of the walls and apart (50 seeds)");

// Sealed pocket: no neighbour is standable, so the device's own spot is the only place that is not a wall.
var pocket = BuildMap((x, y, z) => y >= FloorTop && !(x == 8 && z == 8));
var sealedIn = SpawnInTurn(pocket, device, 0, 2, 1);
Check(sealedIn.All(f => f == oldRule), "a walled-in device falls back to its own spot rather than a wall");

// Map spawn areas already avoided bodies; the refactor must keep that for every seed.
var spawnArea = new Vector3(8f, FloorTop, 8f);
for (var seed = 0; seed < 50; seed++)
{
    var group = SpawnInTurn(open, spawnArea, 2, 10, seed);
    if (group.Distinct().Count() != 10 || MinPairDistance(group) < MinSeparation || !group.All(f => Standable(open, f)))
        Check(false, $"map spawn area keeps ten spawners apart (seed {seed})");
}
Check(true, "map spawn area keeps ten spawners apart (50 seeds)");

// The octree stores duplicates and Remove drops one; a respawn that only re-added left the old body behind.
var octree = new BoundsOctreeEx<Unit>(64, Vector3.Zero, 1, 1.2f);
var body = PlayerAt(new Vector3(2.5f, FloorTop, 2.5f));
octree.Add(body, new BoundingBox(new Vector3(2.5f, FloorTop + 1, 2.5f), new Vector3(0.5f, 1.9f, 0.5f)));
octree.Add(body, new BoundingBox(new Vector3(8.5f, FloorTop + 1, 8.5f), new Vector3(0.5f, 1.9f, 0.5f)));
octree.Remove(body);
Check(octree.Count == 1, "premise: one Remove leaves a duplicate behind");
while (octree.Remove(body)) { }
Check(octree.Count == 0, "draining Remove clears every entry for the unit");

Console.WriteLine($"Spawn overlap fixture passed: {checks} checks.");
