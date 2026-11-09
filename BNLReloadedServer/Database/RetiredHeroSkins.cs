using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

// Retain card identities/assets for historical references, but remove current availability.
public static class RetiredHeroSkins
{
    public static readonly string[] Ids = ["skin_ai3d_abe_bear", "skin_ai3d_cogwheel_prime", "skin_ai3d_roly_gold", "skin_ai3d_magnus_steampunk"];
    private static readonly HashSet<Key> Keys = Ids.Select(Catalogue.Key).ToHashSet();
    public static bool IsRetired(Key key) => Keys.Contains(key);

    public static void Apply(Card card)
    {
        if (card is CardSkin && card.Id is { } id && IsRetired(Catalogue.Key(id)))
            card.Scope = ScopeType.Private;
        if (card is CardUnit { Data: UnitDataPlayer hero })
            hero.Skins?.RemoveAll(IsRetired);
        if (card is CardShopItem shop && shop.Items?.Any(IsRetired) == true)
            shop.AvailableInShop = false;
    }
}
