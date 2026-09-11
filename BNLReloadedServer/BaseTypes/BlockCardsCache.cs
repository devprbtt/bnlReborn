using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.BaseTypes;

public static class BlockCardsCache
{
    private static readonly HashSet<ushort> ReportedUnknown = [];

    // A map outlives the catalogue that built it, so an id can arrive with no card behind it.
    // BlockBinary reads Card unguarded from Solid, Passable, Grounded and a dozen other places,
    // so returning null here turns a stale map into a NullReferenceException somewhere far away.
    // Air is the one substitute that cannot break stability or collision.
    public static CardBlock GetCard(ushort blockId)
    {
        var catalogue = (ServerCatalogue)Databases.Catalogue;
        var card = catalogue.GetBlockCard(blockId);
        if (card != null) return card;

        lock (ReportedUnknown)
        {
            if (ReportedUnknown.Add(blockId))
                Log.Warn(LogCat.Map, $"Block id {blockId} has no card in the catalogue — substituting air");
        }

        return catalogue.GetBlockCard(0)!;
    }
}
