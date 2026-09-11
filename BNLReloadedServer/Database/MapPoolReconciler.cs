using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Database;

public static class MapPoolReconciler
{
    public static void Reconcile(List<Card> cards)
    {
        ConquestMapRegistration.Register(cards);
        var mapList = cards.OfType<CardMapList>().FirstOrDefault();
        var mapCards = cards.OfType<CardMap>().ToList();
        var mapDatabase = Databases.MapDatabase;
        var mapIds = mapDatabase.GetMapIds();

        // A Key carries only a CRC32, so a name has to come from something that still holds the
        // string: the card ids, or the file names on disk for maps the catalogue never declared.
        var names = new Dictionary<Key, string>(KeyEqualityComparer.Instance);
        foreach (var card in mapCards.Where(c => c.Id != null))
        {
            names[card.Key] = card.Id!;
        }
        foreach (var id in mapIds)
        {
            names.TryAdd(Catalogue.Key(id), id);
        }

        var mapCardKeys = new HashSet<Key>(mapCards.Select(c => c.Key), KeyEqualityComparer.Instance);
        string Name(Key key) => names.TryGetValue(key, out var name) ? name : $"<no name, hash {key.Hash}>";

        List<string> problems = [];
        var poolsChecked = 0;

        // Every map card should have a payload. Cards without one are not an error on their own —
        // they simply cannot be played — but they are how a pool ends up empty, so name them.
        var cardsWithoutFile = mapCards
            .Where(c => !mapDatabase.HasMap(c.Key))
            .Select(c => c.Id ?? Name(c.Key))
            .Order()
            .ToList();
        if (cardsWithoutFile.Count > 0)
        {
            problems.Add($"{cardsWithoutFile.Count} map card(s) have no .bnlbin: {Join(cardsWithoutFile)}");
        }

        var filesWithoutCard = mapIds
            .Where(id => !mapCardKeys.Contains(Catalogue.Key(id)))
            .Order()
            .ToList();
        if (filesWithoutCard.Count > 0)
        {
            problems.Add($"{filesWithoutCard.Count} .bnlbin file(s) have no map card, " +
                         $"so they can never be played: {Join(filesWithoutCard)}");
        }

        if (mapList == null)
        {
            problems.Add("no map_list card in the catalogue — every lobby will fall back to the full map card list.");
        }
        else
        {
            mapList.Custom = Prune(mapList.Custom, "custom");
            mapList.Friendly = Prune(mapList.Friendly, "friendly");
            mapList.FriendlyNoob = Prune(mapList.FriendlyNoob, "friendly_noob");
            mapList.Ranked = Prune(mapList.Ranked, "ranked");

            // Tutorial is a single key rather than a pool, so there is nothing to prune to —
            // dropping it would leave the tutorial game mode pointing at Key.None. Report, keep.
            if (mapList.Tutorial != Key.None && !Playable(mapList.Tutorial, out var tutorialReason))
            {
                problems.Add($"tutorial map '{Name(mapList.Tutorial)}' is unplayable ({tutorialReason}) — " +
                             "left in place, fix it in CouchDB.");
            }
        }

        if (problems.Count == 0)
        {
            Log.Info(LogCat.Map, $"{mapCards.Count} map cards, {mapIds.Count} .bnlbin on disk, " +
                                 $"all {poolsChecked} pool(s) aligned");
            return;
        }

        Log.Warn(LogCat.Map, $"{mapCards.Count} map cards in catalogue, {mapIds.Count} .bnlbin files on disk");
        foreach (var problem in problems)
        {
            Log.Warn(LogCat.Map, problem);
        }

        return;

        bool Playable(Key key, out string reason)
        {
            var hasCard = mapCardKeys.Contains(key);
            var hasFile = mapDatabase.HasMap(key);
            reason = (hasCard, hasFile) switch
            {
                (false, false) => "no map card and no .bnlbin",
                (false, true) => "no map card",
                (true, false) => "no .bnlbin",
                _ => string.Empty
            };
            return hasCard && hasFile;
        }

        List<Key>? Prune(List<Key>? pool, string poolName)
        {
            if (pool == null) return null;
            poolsChecked++;

            List<Key> kept = [];
            List<string> dropped = [];
            foreach (var key in pool)
            {
                if (Playable(key, out var reason)) kept.Add(key);
                else dropped.Add($"{Name(key)} ({reason})");
            }

            if (dropped.Count > 0)
            {
                problems.Add($"pool '{poolName}': {pool.Count} listed, {kept.Count} playable, dropped {Join(dropped)}");
            }

            if (kept.Count == 0)
            {
                problems.Add($"POOL EMPTY: '{poolName}' has no playable maps left — every lobby using it " +
                             "will hand out an empty ballot and the match will never start.");
            }

            return kept;
        }
    }

    private static string Join(List<string> items) =>
        items.Count <= 12
            ? string.Join(", ", items)
            : string.Join(", ", items.Take(12)) + $" (+{items.Count - 12} more)";
}
