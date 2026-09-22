using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public readonly record struct IglooBlockPlacement(Vector3s Position, Vector3s AttachTo);

public static class YuriNIceIgloo
{
    public const string UnitId = "unit_dummy_abe_avalanche_yuri_n_ice";
    public const string BlockId = "block_snow";

    public static readonly Key UnitKey = new(UnitId);
    public static readonly Key BlockKey = new(BlockId);

    public static IReadOnlyList<IglooBlockPlacement> Build(Vector3s baseCenter)
    {
        var result = new List<IglooBlockPlacement>(67);

        AddRing(result, baseCenter, 0, 2, attachBelow: true);
        AddRing(result, baseCenter, 1, 2, attachBelow: true);

        // Build the roof from the supported outside edge inward. This ordering lets the
        // ordinary block-placement stability rules accept every overhanging snow block.
        AddRing(result, baseCenter, 2, 2, attachBelow: true);
        AddRing(result, baseCenter, 2, 1, attachBelow: false);
        var roofCenter = Offset(baseCenter, 0, 2, 0);
        result.Add(new IglooBlockPlacement(roofCenter, Offset(baseCenter, 1, 2, 0)));

        // A stepped crown gives the otherwise functional 5x5 shell an igloo silhouette.
        for (var x = -1; x <= 1; x++)
        for (var z = -1; z <= 1; z++)
        {
            var position = Offset(baseCenter, x, 3, z);
            result.Add(new IglooBlockPlacement(position, Offset(baseCenter, x, 2, z)));
        }

        var top = Offset(baseCenter, 0, 4, 0);
        result.Add(new IglooBlockPlacement(top, Offset(baseCenter, 0, 3, 0)));
        return result;
    }

    private static void AddRing(List<IglooBlockPlacement> result, Vector3s center, int y, int radius,
        bool attachBelow)
    {
        for (var x = -radius; x <= radius; x++)
        for (var z = -radius; z <= radius; z++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) != radius) continue;

            var position = Offset(center, x, y, z);
            Vector3s attachTo;
            if (attachBelow)
            {
                attachTo = Offset(center, x, y - 1, z);
            }
            else if (x != 0)
            {
                attachTo = Offset(center, x + Math.Sign(x), y, z);
            }
            else
            {
                attachTo = Offset(center, x, y, z + Math.Sign(z));
            }

            result.Add(new IglooBlockPlacement(position, attachTo));
        }
    }

    private static Vector3s Offset(Vector3s center, int x, int y, int z) =>
        new(center.x + x, center.y + y, center.z + z);
}
