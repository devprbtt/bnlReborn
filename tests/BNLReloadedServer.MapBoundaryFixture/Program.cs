using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Octree_Extensions;
using BNLReloadedServer.ProtocolHelpers;

var checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}

var size = new Vector3s(4, 4, 4);
var binary = new byte[size.x * size.y * size.z * 6].Zip(0).ToArray();
var map = new MapBinary(3, binary, size, -1,
    new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => true));

var topSurface = new BoundingSphere(new Vector3(2.5f, size.y, 2.5f), 1f);
var topCells = map.EnumerateBlocks(topSurface, null).ToArray();
Check(topCells.Contains(new Vector3s(2, size.y - 1, 2)),
    "effect centered on maximum height reaches the top voxel layer");
Check(map.CheckBlocks(topSurface, block => block.Position == new Vector3s(2, size.y - 1, 2)) is not null,
    "nearby-effect lookup centered on maximum height reaches the top voxel layer");

var aboveButOverlapping = new BoundingSphere(new Vector3(1.5f, size.y + 0.9f, 1.5f), 1f);
Check(map.EnumerateBlocks(aboveButOverlapping, null).Any(),
    "player-centered effect above the top surface still reaches intersecting cells");

var fullyOutside = new BoundingSphere(new Vector3(1.5f, size.y + 1.1f, 1.5f), 1f);
Check(!map.EnumerateBlocks(fullyOutside, null).Any() && map.CheckBlocks(fullyOutside, _ => true) is null,
    "effect volume fully outside the map remains rejected");

var sideBoundary = new BoundingSphere(new Vector3(size.x, 1.5f, 1.5f), 0.5f);
Check(map.EnumerateBlocks(sideBoundary, null).Any(p => p.x == size.x - 1),
    "shared boundary traversal also reaches an intersecting side voxel");

Console.WriteLine($"Map boundary suite passed: {checks} checks.");
