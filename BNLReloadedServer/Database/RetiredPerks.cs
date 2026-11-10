using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

// Keep historical references resolvable, while preventing selection and gameplay effects.
public static class RetiredPerks
{
    public const string ShinobiTactics = "perk_hero_ninja_shinobi_tactics";
    public const string WalkItOff = "perk_def_walk_it_off";
    public static bool IsRetired(Key key) => key == Catalogue.Key(ShinobiTactics) || key == Catalogue.Key(WalkItOff);

    public static void Apply(Card card)
    {
        if (card is CardPerk perk && perk.Id is { } id && IsRetired(Catalogue.Key(id)))
        {
            perk.Scope = ScopeType.Private;
            perk.PerkMods = [];
        }
        if (card is CardGlobalLogic global && global.Perks is { } lists)
        {
            lists.Offensive?.RemoveAll(IsRetired);
            lists.Defensive?.RemoveAll(IsRetired);
            if (lists.Heroes != null)
                foreach (var perks in lists.Heroes.Values)
                    perks.RemoveAll(IsRetired);
        }
        if (card is CardShopItem shop && shop.Items?.Any(IsRetired) == true)
            shop.AvailableInShop = false;
    }
}
