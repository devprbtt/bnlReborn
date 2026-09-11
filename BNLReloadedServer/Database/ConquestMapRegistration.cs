using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.Database;

public static class ConquestMapRegistration
{
    public static void Register(List<Card> cards)
    {
        var original = cards.OfType<CardMap>().FirstOrDefault(c => c.Id == SkyBridgeConquest.OriginalMapId);
        if (original == null) return;
        var key = Catalogue.Key(SkyBridgeConquest.MapId);
        if (!Databases.MapDatabase.HasMap(key)) return;
        if (!cards.Any(c => c.Id == SkyBridgeConquest.MapId))
            cards.Add(new CardMap { Id = SkyBridgeConquest.MapId, Key = key, Scope = original.Scope,
                Name = new LocalizedString { Text = "Sky Bridge Don Edit - Conquest", Data = [] },
                Description = new LocalizedString { Text = "Experimental conquest: capture zones to earn a Block Buster attack.", Data = [] },
                Image = original.Image, LargeImage = original.LargeImage, Data = original.Data });
        // Only custom matches: preserve the original map and all matchmaking pools.
        foreach (var list in cards.OfType<CardMapList>())
        {
            list.Custom ??= [];
            if (!list.Custom.Contains(key)) list.Custom.Add(key);
        }
    }
}
