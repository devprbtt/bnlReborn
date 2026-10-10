using System.Linq.Expressions;
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using Octree;

int checks = 0;
void Check(bool pass, string label) { if (!pass) throw new Exception("FAIL " + label); checks++; Console.WriteLine("PASS " + label); }
var catalogue = (ServerCatalogue)Databases.Catalogue;
var portal = new CardUnit { Id = "fixture_portal", Data = new UnitDataPortal(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.Center };
var hero = new CardUnit { Id = "fixture_hero", Data = new UnitDataPlayer(), Size = new Vector3s(1, 2, 1), PivotType = UnitPivotType.CenterBottom };
var device = new CardUnit { Id = "fixture_device", Data = new UnitDataCommon(), Size = new Vector3s(1, 1, 1), PivotType = UnitPivotType.Center };
var build = new CardBlock { Id = "fixture_build", BlockId = 2, Solid = true, CanStayInAir = true, Passable = BlockPassableType.None };
var brick = new CardBlock { Id = "fixture_brick", BlockId = 3, Solid = true, Passable = BlockPassableType.None };
catalogue.Replicate([portal, hero, device, build, brick,
    new CardBlock { Id = "fixture_air", BlockId = 0, Replaceable = true, Passable = BlockPassableType.Any },
    new CardBlock { Id = "fixture_ground", BlockId = 1, Solid = true, CanStayInAir = true, Passable = BlockPassableType.None }]);
var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var updater = (UnitUpdater)constructor.Invoke(constructor.GetParameters().Select(p => {
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType),
        invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name))).Compile();
}).ToArray());
Unit Make(CardUnit card, Vector3 position, bool player = false) {
    var unit = new Unit(1, new UnitInit { Key = card.Key, Team = TeamType.Team1, PlayerId = player ? 1u : null }, updater);
    unit.Transform = ZoneTransformHelper.ToZoneTransform(position, Quaternion.Identity);
    return unit;
}
MapBinary Map(Unit unit, bool supportGround = false) {
    var bytes = new byte[8 * 8 * 8 * 6];
    if (supportGround) BitConverter.TryWriteBytes(bytes.AsSpan(((4 * 8 + 1) * 8 + 4) * 6, 2), (ushort)1);
    var map = new MapBinary(3, bytes.Zip(0).ToArray(), new Vector3s(8,8,8), -1,
        new MapUpdater((_,_)=>{}, (_,_)=>{}, _=>{}, _=>true));
    map.Units = new BoundsOctreeEx<Unit>(16, new Vector3(4), 1, 1);
    map.Units.Add(unit, new BoundingBox(unit.GetMidpoint(), unit.UnitCard!.Size!.Value.ToVector3() - UnitSizeHelper.ImprecisionVector));
    return map;
}
var portalUnit = Make(portal, new Vector3(4.5f,3.5f,4.5f));
var map = Map(portalUnit);
var support = new Vector3s(4,2,4);
Check(map.GetContainedInUnit(portalUnit).Contains(support), "reproduce centered portal combat bounds overlapping support cell");
Check(map.AddBlock(build.Key, support, support + Vector3s.Down, Direction2D.Left, null).ContainsKey(support),
    "block directly below portal can be placed");
map = Map(portalUnit, supportGround: true);
Check(map.AddBlock(brick.Key, support, support + Vector3s.Down, Direction2D.Left, null).ContainsKey(support),
    "ordinary supported brick beneath portal passes production stability and occupancy checks");
map = Map(portalUnit);
Check(map.AddBlock(brick.Key, support, support + Vector3s.Down, Direction2D.Left, null).Count == 0,
    "unsupported brick is still rejected");
foreach (var cell in new[] { new Vector3s(4,3,4), new Vector3s(4,4,4) }) {
    map = Map(portalUnit);
    Check(map.AddBlock(build.Key, cell, cell + Vector3s.Down, Direction2D.Left, null).Count == 0,
        "portal occupied cell remains blocked: " + cell);
}
foreach (var cell in new[] { new Vector3s(3,3,4), new Vector3s(5,3,4), new Vector3s(4,3,3), new Vector3s(4,3,5) }) {
    map = Map(portalUnit);
    Check(map.AddBlock(build.Key, cell, cell + Vector3s.Down, Direction2D.Left, null).ContainsKey(cell),
        "adjacent portal cell remains buildable: " + cell);
}
var body = new Vector3s(4,2,4);
map = Map(Make(hero, new Vector3(4.5f,2,4.5f), true));
Check(map.AddBlock(build.Key, body, body + Vector3s.Down, Direction2D.Left, null).Count == 0,
    "player self-embedding remains rejected");
map = Map(Make(device, new Vector3(4.5f,2.5f,4.5f)));
Check(map.AddBlock(build.Key, body, body + Vector3s.Down, Direction2D.Left, null).Count == 0,
    "ordinary device occupancy remains rejected");
Console.WriteLine($"PASS {checks} portal placement checks");
