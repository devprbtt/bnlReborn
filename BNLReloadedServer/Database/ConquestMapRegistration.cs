using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.Database;

public static class ConquestMapRegistration
{
    public const string BeachBaseOriginalId = "map_sr2_beach_base_new";
    public const string BeachBaseMapId = "map_sr2_beach_base_new_conquest";
    private static readonly (string Original, string Id, string Name)[] Variants =
    [
        (SkyBridgeConquest.OriginalMapId, SkyBridgeConquest.MapId, "Sky Bridge Don Edit - Conquest"),
        (BeachBaseOriginalId, BeachBaseMapId, "Beach Base Mid Edit - Conquest")
    ];
    public static bool IsConquest(Key? key) => key.HasValue && Variants.Any(v => key.Value == new Key(v.Id));
    public static ConquestLogic DefaultRules(Key key) => key == new Key(BeachBaseMapId)
        ? new ConquestLogic { ZoneDepthBelow = 5 } // Lowest capture floor stays above Beach Base's water/kill plane.
        : new ConquestLogic();
    public static IEnumerable<Vector3> Centers(MapData data) => data.Units
        .Where(u => u.UnitKey.GetCard<CardUnit>()?.Labels?.Contains(UnitLabel.DropPointBlockbuster) == true)
        .Select(u => u.Position).OrderBy(p => p.Z).ThenBy(p => p.X);

    public static void Register(List<Card> cards)
    {
        foreach (var variant in Variants)
        {
            var original = cards.OfType<CardMap>().FirstOrDefault(c => c.Id == variant.Original);
            if (original == null) continue;
            var key = Catalogue.Key(variant.Id);
            if (!Databases.MapDatabase.HasMap(key)) continue;
            if (!cards.Any(c => c.Id == variant.Id))
                cards.Add(new CardMap { Id = variant.Id, Key = key, Scope = original.Scope,
                    Name = new LocalizedString { Text = variant.Name, Data = [] },
                    Description = new LocalizedString { Text = "Capture zones to earn a Block Buster attack.", Data = [] },
                    Image = original.Image, LargeImage = original.LargeImage, Data = original.Data,
                    Conquest = DefaultRules(key) });
            // Only custom matches; retain existing catalogue overrides and matchmaking pools.
            foreach (var list in cards.OfType<CardMapList>())
            {
                list.Custom ??= [];
                if (!list.Custom.Contains(key)) list.Custom.Add(key);
            }
        }
    }
}
