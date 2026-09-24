// Sweet Science's Blitz (hit_without_collision) must only apply its hit effect, impact visual included,
// when the dash actually touched something. HitData is built with the client's own formulas:
//   miss (PlayerMovementDash.CreateMissHit): InsidePoint = ray.GetPoint(0.3) + dir * 0.1, no target
//   block hit (HitProvider.Create(RaycastInfo)): InsidePoint = face point - normal * 0.01
//   unit hit: TargetId set, InsidePoint on the unit's collider in open air
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

const ushort AirId = 0, FloorId = 1, FieldId = 44, WaterId = 2;
const int SizeX = 20, SizeY = 12, SizeZ = 20, FloorTop = 3;

var catalogue = (ServerCatalogue)Databases.Catalogue;
catalogue.Replicate([
    new CardBlock { Id = "fixture_air", BlockId = AirId, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardBlock { Id = "fixture_floor", BlockId = FloorId, Passable = BlockPassableType.None, Solid = true, Grounded = true },
    new CardBlock { Id = "fixture_water", BlockId = WaterId, Passable = BlockPassableType.Any, Transparent = true, Grounded = true },
    new CardBlock { Id = "fixture_force_field", BlockId = FieldId, Passable = BlockPassableType.Ally, Solid = true, HasTeam = true, Grounded = true }
]);

ushort Layout(int x, int y, int z) =>
    y < FloorTop ? FloorId :
    x == 10 && y <= 8 ? FloorId :                  // solid wall
    z == 14 && y <= 8 ? FieldId :                  // force-field wall
    x is >= 3 and <= 5 && z is >= 3 and <= 5 && y == FloorTop ? WaterId :
    AirId;
var data = new byte[6 + SizeX * SizeY * SizeZ * 6];
BitConverter.TryWriteBytes(data.AsSpan(0, 2), (ushort)SizeX);
BitConverter.TryWriteBytes(data.AsSpan(2, 2), (ushort)SizeY);
BitConverter.TryWriteBytes(data.AsSpan(4, 2), (ushort)SizeZ);
for (var x = 0; x < SizeX; x++)
for (var y = 0; y < SizeY; y++)
for (var z = 0; z < SizeZ; z++)
    BitConverter.TryWriteBytes(data.AsSpan(6 + ((x * SizeY + y) * SizeZ + z) * 6, 2), Layout(x, y, z));
var map = new MapBinary(data.Zip(0).ToArray(), 0f, new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => true));

bool Blocking(int x, int y, int z) =>
    x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ &&
    Layout(x, y, z) is FloorId or FieldId;

// First blocking face along the ray within maxDistance: point on the face and its outward normal.
(Vector3 point, Vector3 normal)? Raycast(Vector3 origin, Vector3 dir, float maxDistance)
{
    for (var t = 0f; t <= maxDistance; t += 0.002f)
    {
        var p = origin + dir * t;
        var c = new[] { (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z) };
        if (!Blocking(c[0], c[1], c[2])) continue;
        var prev = origin + dir * (t - 0.002f);
        var normal = new Vector3(
            MathF.Floor(prev.X) != c[0] ? MathF.Sign(prev.X - p.X) : 0,
            MathF.Floor(prev.Y) != c[1] ? MathF.Sign(prev.Y - p.Y) : 0,
            MathF.Floor(prev.Z) != c[2] ? MathF.Sign(prev.Z - p.Z) : 0);
        return (p, normal == Vector3.Zero ? -dir : Vector3.Normalize(normal));
    }
    return null;
}

int checks = 0, misses = 0, blockHits = 0, fieldHits = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); checks++; }

var rng = new Random(7);
for (var n = 0; n < 200_000; n++)
{
    var camera = new Vector3(0.5f + (float)rng.NextDouble() * 18.9f, FloorTop + 0.2f + (float)rng.NextDouble() * 6f,
        0.5f + (float)rng.NextDouble() * 18.9f);
    if (Blocking((int)camera.X, (int)camera.Y, (int)camera.Z)) continue;
    var dir = Vector3.Normalize(new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 1.4f - 0.7f,
        (float)rng.NextDouble() * 2 - 1));
    // PlayerMovementDash.Update: RaycastHelper.RaycastDash(ray, 2f, 0.3f) ends the dash on a hit.
    var hit = Raycast(camera, dir, 2f);
    if (hit is { } h)
    {
        var inside = h.point - h.normal * 0.01f;
        var isField = Layout((int)MathF.Floor(inside.X), (int)MathF.Floor(inside.Y), (int)MathF.Floor(inside.Z)) == FieldId;
        Check(!DashHitPolicy.IsMiss(map, new HitData { InsidePoint = inside }), $"block hit at {inside} counts");
        if (isField) fieldHits++; else blockHits++;
    }
    else
    {
        var inside = camera + dir * 0.3f + dir * 0.1f;
        Check(DashHitPolicy.IsMiss(map, new HitData { InsidePoint = inside }), $"air dash at {inside} is a miss");
        misses++;
        // The same open-air point with a unit target is a player hit and must still apply.
        Check(!DashHitPolicy.IsMiss(map, new HitData { InsidePoint = inside, TargetId = 42 }), "unit hit counts");
    }
}

// Fixed cases.
Check(DashHitPolicy.IsMiss(map, new HitData { InsidePoint = new Vector3(4.5f, FloorTop + 0.4f, 4.5f) }), "dash ending in water is a miss");
Check(DashHitPolicy.IsMiss(map, new HitData { InsidePoint = new Vector3(-3f, 5f, 4f) }), "point outside the map is a miss");
Check(!DashHitPolicy.IsMiss(map, new HitData { InsidePoint = new Vector3(10.01f, 5f, 7f) }), "wall face point counts");

Console.WriteLine($"misses={misses} blockHits={blockHits} forceFieldHits={fieldHits}");
if (misses == 0 || blockHits == 0 || fieldHits == 0) throw new Exception("sweep did not exercise every hit kind");
Console.WriteLine($"Dash miss fixture passed: {checks} checks.");
