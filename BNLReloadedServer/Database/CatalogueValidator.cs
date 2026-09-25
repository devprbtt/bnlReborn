using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class CatalogueValidator
{
    private static readonly (string Id, Type Type)[] RequiredCards =
    [
        ("map_logic", typeof(CardMapLogic)),
        ("map_list", typeof(CardMapList)),
        ("global_logic", typeof(CardGlobalLogic)),
        ("chat_logic", typeof(CardChatLogic)),
        ("settings_logic", typeof(CardSettingsLogic)),
        ("movement_logic", typeof(CardMovementLogic)),
        ("shop_logic", typeof(CardShopLogic)),
        ("rewards_logic", typeof(CardRewardsLogic)),
        ("game_mode_friendly", typeof(CardGameMode)),
        ("game_mode_ranked", typeof(CardGameMode)),
        ("game_mode_custom", typeof(CardGameMode)),
        ("game_mode_mad", typeof(CardGameMode)),
        ("game_mode_tutorial", typeof(CardGameMode))
    ];

    private const int MinimumCards = 1000;

    public static List<string> Validate(List<Card> cards)
    {
        List<string> problems = [];

        if (cards.Count < MinimumCards)
        {
            problems.Add($"only {cards.Count} cards fetched, expected at least {MinimumCards}");
            // Everything below would just restate this, so stop here.
            return problems;
        }

        var byId = new Dictionary<string, Card>();

        // Everything downstream addresses a card by its Key — the CRC32 of the id — and the id
        // string is not carried along (see Key, which is a bare uint). Two ids that hash alike are
        // indistinguishable from that point on: ServerCatalogue.Replicate throws on the duplicate,
        // and so does the client's own Replicate when it inflates the blob, which would break the
        // game for everyone who downloads it. Here is the last place both ids still exist to name.
        var byKey = new Dictionary<uint, string>();

        foreach (var card in cards)
        {
            if (card.Id == null)
            {
                problems.Add("a card has no id");
                continue;
            }

            if (!byId.TryAdd(card.Id, card))
            {
                problems.Add($"duplicate card id '{card.Id}'");
                continue;
            }

            var hash = Catalogue.Key(card.Id).Hash;
            if (!byKey.TryAdd(hash, card.Id))
            {
                problems.Add($"CARD KEY COLLISION: '{byKey[hash]}' and '{card.Id}' both hash to {hash} — " +
                             "rename one of them in CouchDB; nothing downstream can tell them apart");
            }
        }

        // An on_hit effect is only as good as the effects it names: a typo would silently turn a perk like
        // Heal Bane into nothing, so reject it here where the id is still readable.
        foreach (var card in cards.OfType<CardEffect>())
        {
            if (card.Effect is not ConstEffectOnHit onHit) continue;
            if (onHit.Effect == null)
            {
                problems.Add($"on_hit effect '{card.Id}' has no effect to apply");
                continue;
            }

            foreach (var key in ConstantEffects(onHit.Effect).Where(key =>
                         !byKey.TryGetValue(key.Hash, out var id) || byId[id] is not CardEffect))
                problems.Add($"on_hit effect '{card.Id}' applies an effect that is not in the catalogue ({key})");
        }

        foreach (var (id, type) in RequiredCards)
        {
            if (!byId.TryGetValue(id, out var card))
            {
                problems.Add($"required card '{id}' is missing");
            }
            else if (!type.IsInstanceOfType(card))
            {
                problems.Add($"required card '{id}' is {card.GetType().Name}, expected {type.Name}");
            }
        }

        return problems;
    }

    private static IEnumerable<Key> ConstantEffects(InstEffect effect) => effect is InstEffectBunch bunch
        ? (bunch.Constant ?? []).Concat((bunch.Instant ?? []).SelectMany(ConstantEffects))
        : [];
}
