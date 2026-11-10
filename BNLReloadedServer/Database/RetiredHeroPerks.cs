using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

// Keep historical references resolvable, while preventing selection and gameplay effects.
public static class RetiredHeroPerks
{
    public const string ShinobiTactics = "perk_hero_ninja_shinobi_tactics";
    public static bool IsRetired(Key key) => key == Catalogue.Key(ShinobiTactics);

    public static void Apply(Card card)
    {
        if (card is CardPerk perk && perk.Id == ShinobiTactics)
        {
            perk.Scope = ScopeType.Private;
            perk.PerkMods = [];
        }
        if (card is CardGlobalLogic global && global.Perks?.Heroes != null)
            foreach (var perks in global.Perks.Heroes.Values)
                perks.RemoveAll(IsRetired);
        if (card is CardShopItem shop && shop.Items?.Any(IsRetired) == true)
            shop.AvailableInShop = false;
    }
}
