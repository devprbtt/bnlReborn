using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public static class DashHitPolicy
{
    /// <summary>
    /// A dash tool with hit_without_collision (Sweet Science's Blitz) reports a hit even when the dash
    /// touched nothing: the client places it 0.4 in front of the camera. Shipped clients sent that point
    /// near the map origin, so the miss never reached anyone; once clients sent the real position, every
    /// air dash showed the Blitz impact and a charged one splashed nearby enemies. A real hit names a unit
    /// or lands inside a block that is not freely passable, so everything else is a miss.
    /// </summary>
    public static bool IsMiss(MapBinary map, HitData hit)
    {
        if (hit.TargetId is not null) return false;
        var cell = (Vector3s)hit.InsidePoint;
        return !map.ContainsBlock(cell) || map[cell].Card.Passable == BlockPassableType.Any;
    }
}
