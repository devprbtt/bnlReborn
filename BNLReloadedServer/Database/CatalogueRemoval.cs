using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Database;

public static class CatalogueRemoval
{
    // Inventories first: a client must never hold a card it has been told no longer exists.
    public static bool Apply(ServerCatalogue catalogue, string id, Action<string> broadcast)
    {
        if (!catalogue.RemoveCard(id)) return false;
        try
        {
            Databases.PlayerDatabase.ReconcileRemovedCard(id);
        }
        catch (Exception ex)
        {
            Log.Error(LogCat.Catalogue, $"Could not refresh players after removing {id}", ex);
        }
        broadcast(id);
        return true;
    }
}
