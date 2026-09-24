// Ninja's katana alt-fire (teleport_to) must never place the caster where a force field overlaps the body.
// A synthetic map crosses force-field walls over a floor; the fixture casts from many standing and crouched
// positions across a sweep of aim angles, resolves each landing through the production placement code,
// and checks the landed body against both the server's own fit rule and the client's character capsule.
using System.Linq.Expressions;
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

const ushort AirId = 0, FloorId = 1, FieldId = 44;
const int SizeX = 24, SizeY = 16, SizeZ = 24, FloorTop = 3;
const float Range = 13f;

// Client NinjaPlayer CharacterController: center y 0.7, height 1.8, radius 0.32; CameraRoot at y 1.8.
const float ClientRadius = 0.32f, ClientBottom = -0.2f, ClientTop = 1.6f, CameraHeight = 1.8f, CrouchCameraHeight = 1.0f;

var catalogue = (ServerCatalogue)Databases.Catalogue;
var hero = new CardUnit
{
    Id = "fixture_teleport_ninja", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom
};
catalogue.Replicate([
    hero,
    new CardBlock { Id = "fixture_air", BlockId = AirId, Passable = BlockPassableType.Any, Transparent = true, LightTransparent = true, SkylightTransparent = true },
    new CardBlock { Id = "fixture_floor", BlockId = FloorId, Passable = BlockPassableType.None, Solid = true, Grounded = true },
    new CardBlock { Id = "fixture_force_field", BlockId = FieldId, Passable = BlockPassableType.Ally, Solid = true, HasTeam = true, Grounded = true }
]);

var fields = new HashSet<(int, int, int)>();
bool IsFieldLayout(int x, int y, int z) =>
    y >= FloorTop && y <= 9 && (
        x == 12 || z == 12 ||                                   // crossing walls meeting at (12, *, 12)
        (x is >= 5 and <= 6 && z is >= 5 and <= 6) ||           // 2x2 column
        (x == 18 && z is >= 16 and <= 20) || (z == 16 && x is >= 18 and <= 21) || // L corner
        (y == 9 && x is >= 16 and <= 21 && z is >= 3 and <= 8)); // overhead slab
var data = new byte[6 + SizeX * SizeY * SizeZ * 6];
BitConverter.TryWriteBytes(data.AsSpan(0, 2), (ushort)SizeX);
BitConverter.TryWriteBytes(data.AsSpan(2, 2), (ushort)SizeY);
BitConverter.TryWriteBytes(data.AsSpan(4, 2), (ushort)SizeZ);
for (var x = 0; x < SizeX; x++)
for (var y = 0; y < SizeY; y++)
for (var z = 0; z < SizeZ; z++)
{
    var id = y < FloorTop ? FloorId : IsFieldLayout(x, y, z) ? FieldId : AirId;
    if (id == FieldId) fields.Add((x, y, z));
    var offset = 6 + ((x * SizeY + y) * SizeZ + z) * 6;
    BitConverter.TryWriteBytes(data.AsSpan(offset, 2), id);
    if (id == FieldId) data[offset + 5] = 2; // Team2 field
}
var zipped = data.Zip(0).ToArray();
var map = new MapBinary(zipped, 0f, new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => true));

var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var updater = (UnitUpdater)constructor.Invoke(constructor.GetParameters().Select(p =>
{
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType),
        invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name))).Compile();
}).ToArray());
var unit = new Unit(1, new UnitInit { Key = hero.Key, Team = TeamType.Team1, PlayerId = 1, OwnerId = 1 }, updater);

bool Field(int x, int y, int z) => fields.Contains((x, y, z));
bool Blocked(int x, int y, int z) =>
    x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ && (y < FloorTop || Field(x, y, z));

// Voxel DDA; returns the first entry point into a blocked cell and the face-derived shift toward the shooter.
(Vector3 point, BlockShift shift) Cast(Vector3 origin, Vector3 dir)
{
    int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
    int sx = MathF.Sign(dir.X), sy = MathF.Sign(dir.Y), sz = MathF.Sign(dir.Z);
    float Next(float o, int c, int s, float d) => d == 0 ? float.PositiveInfinity : ((s > 0 ? c + 1 : c) - o) / d;
    float tx = Next(origin.X, x, sx, dir.X), ty = Next(origin.Y, y, sy, dir.Y), tz = Next(origin.Z, z, sz, dir.Z);
    float dx = dir.X == 0 ? float.PositiveInfinity : sx / dir.X, dy = dir.Y == 0 ? float.PositiveInfinity : sy / dir.Y,
        dz = dir.Z == 0 ? float.PositiveInfinity : sz / dir.Z;
    while (true)
    {
        float t; BlockShift shift;
        if (tx <= ty && tx <= tz) { t = tx; x += sx; tx += dx; shift = sx > 0 ? BlockShift.Left : BlockShift.Right; }
        else if (ty <= tz) { t = ty; y += sy; ty += dy; shift = sy > 0 ? BlockShift.Bottom : BlockShift.Top; }
        else { t = tz; z += sz; tz += dz; shift = sz > 0 ? BlockShift.Back : BlockShift.Front; }
        if (t > Range) return (origin + dir * Range, BlockShift.None);
        if (Blocked(x, y, z)) return (origin + dir * t, shift);
    }
}

bool ClientCapsuleTouchesField(Vector3 feet)
{
    // Sample the capsule's cylinder on a fine grid; the rounded caps only shrink its footprint.
    for (var a = 0; a < 16; a++)
    for (var h = 0; h <= 8; h++)
    for (var r = 0; r <= 2; r++)
    {
        var ang = a * MathF.Tau / 16; var rad = ClientRadius * r / 2f;
        var p = feet + new Vector3(MathF.Cos(ang) * rad, ClientBottom + (ClientTop - ClientBottom) * h / 8f + (h == 0 ? 0.33f : h == 8 ? -0.33f : 0), MathF.Sin(ang) * rad);
        if (Field((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z))) return true;
    }
    return false;
}

int casts = 0, landed = 0, serverViolations = 0, clientOverlaps = 0, cameraInside = 0;
var byStance = new Dictionary<string, int>();
double totalShortfall = 0; int farShort = 0;
void Tally(string k) => byStance[k] = byStance.GetValueOrDefault(k) + 1;
var samples = new List<string>();
foreach (var crouch in new[] { false, true })
for (var fx = 1.5f; fx < SizeX - 1; fx += 1.37f)
for (var fz = 1.5f; fz < SizeZ - 1; fz += 1.37f)
{
    var feet = new Vector3(fx, FloorTop, fz);
    unit.Transform.Position = feet;
    unit.Transform.IsCrouch = crouch;
    if (!map.GetCanFit(unit, unit.GetMidpoint())) continue; // caster must start in open space
    var eye = feet + new Vector3(0, crouch ? CrouchCameraHeight : CameraHeight, 0);
    for (var yaw = 0; yaw < 360; yaw += 7)
    for (var pitch = -40; pitch <= 50; pitch += 6)
    {
        var dir = Vector3.Normalize(new Vector3(MathF.Cos(yaw * MathF.PI / 180) * MathF.Cos(pitch * MathF.PI / 180),
            MathF.Sin(pitch * MathF.PI / 180), MathF.Sin(yaw * MathF.PI / 180) * MathF.Cos(pitch * MathF.PI / 180)));
        var (impact, shift) = Cast(eye, dir);
        casts++;
        var result = TeleportPlacement.FindTeleportTo(map, unit, impact, unit.GetMidpoint(), shift, 0f, _ => true, (_, _) => true);
        if (result is not { } landedFeet) continue;
        landed++;
        var shortfall = Vector3.Distance(impact, landedFeet with { Y = landedFeet.Y + 0.95f });
        totalShortfall += shortfall; if (shortfall > 1.5f) farShort++;
        // After landing the client stands up if it can, so judge the standing body.
        unit.Transform.IsCrouch = false;
        var serverOk = map.GetCanFit(unit, unit.GetMidpoint(landedFeet));
        unit.Transform.IsCrouch = crouch;
        var clientHit = ClientCapsuleTouchesField(landedFeet);
        var cam = landedFeet + new Vector3(0, CameraHeight, 0);
        var camHit = Field((int)MathF.Floor(cam.X), (int)MathF.Floor(cam.Y), (int)MathF.Floor(cam.Z));
        var stance = crouch ? "crouched" : "standing";
        if (!serverOk) Tally(stance + ".serverRule");
        if (clientHit) Tally(stance + ".clientOverlap");
        if (camHit) Tally(stance + ".cameraInField");
        if (!serverOk) serverViolations++;
        if (clientHit) clientOverlaps++;
        if (camHit) cameraInside++;
        if (Environment.GetEnvironmentVariable("SAMPLE") switch { "cam" => camHit, "server" => !serverOk, _ => !serverOk || clientHit } && samples.Count < 14)
            samples.Add($"crouch={crouch} from={feet} impact={impact} shift={shift} -> feet={landedFeet} serverOk={serverOk} clientOverlap={clientHit} cameraInField={camHit}");
    }
}

Console.WriteLine($"casts={casts} landed={landed} serverRuleViolations={serverViolations} clientCapsuleOverlaps={clientOverlaps} cameraInField={cameraInside}");
Console.WriteLine($"meanImpactToLanding={totalShortfall / Math.Max(1, landed):F3} landingsOver1.5FromImpact={farShort}");
Console.WriteLine(string.Join(" ", byStance.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
samples.ForEach(Console.WriteLine);
if (landed == 0) throw new Exception("no teleport landed; the sweep did not exercise placement");
if (serverViolations != 0 || clientOverlaps != 0) { Console.WriteLine("FAIL"); Environment.Exit(1); }
Console.WriteLine("PASS");
