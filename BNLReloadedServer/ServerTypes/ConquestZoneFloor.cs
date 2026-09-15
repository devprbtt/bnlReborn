using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.ServerTypes;

internal static class ConquestZoneFloor
{
    internal static void Apply(MapBinary map, Key? mapKey, MapData data)
    {
        if (mapKey != new Key(SkyBridgeConquest.MapId)) return;
        var metal = new Key("block_metal").GetCard<CardBlock>()
            ?? throw new InvalidOperationException("Conquest requires the standard metal block.");
        if (!metal.Solid || metal.Destructible || !metal.CanFloat)
            throw new InvalidOperationException("Conquest floor metal must be solid, indestructible and self-supporting.");
        var rules = (mapKey.Value.GetCard<CardMap>()?.Conquest ?? new ConquestLogic()).Validated();
        var centers = data.Units.Where(u => u.UnitKey.GetCard<CardUnit>()?.Labels?.Contains(UnitLabel.DropPointBlockbuster) == true)
            .Select(u => u.Position);
        foreach (var position in Cells(centers, rules, map.Size))
        {
            var block = map[position];
            block.Id = metal.BlockId;
            block.Damage = 0;
            block.VData = 0;
            block.LData = 0;
        }
    }

    internal static IEnumerable<Vector3s> Cells(IEnumerable<Vector3> centers, ConquestLogic rules, Vector3s size)
    {
        foreach (var center in centers)
        {
            float left = center.X - rules.ZoneHalfWidth, right = center.X + rules.ZoneHalfWidth;
            float back = center.Z - rules.ZoneHalfWidth, front = center.Z + rules.ZoneHalfWidth;
            if (right <= 0 || front <= 0 || left >= size.x || back >= size.z || size.y <= 0) continue;
            // Put the walking surface at or just above the lower capture bound.
            // Fractional drop-point coordinates must not leave feet below it.
            int y = (int)Math.Clamp(Math.Ceiling(center.Y - rules.ZoneDepthBelow) - 1, 0, size.y - 1);
            if (y + 1 > center.Y + rules.ZoneHeightAbove) continue;
            int minX = (int)Math.Clamp(Math.Floor(left), 0, size.x - 1);
            int maxX = (int)Math.Clamp(Math.Ceiling(right) - 1, 0, size.x - 1);
            int minZ = (int)Math.Clamp(Math.Floor(back), 0, size.z - 1);
            int maxZ = (int)Math.Clamp(Math.Ceiling(front) - 1, 0, size.z - 1);
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
                yield return new Vector3s(x, y, z);
        }
    }
}
