using System.Collections.Concurrent;
using System.Collections.Frozen;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public enum InventoryChange { Granted, Revoked, Unchanged, UnknownPlayer, UnknownItem, PublicItem }

/// <summary>
/// Decides what a player owns. The catalogue only says which items exist: a public card is owned by
/// everyone, a private card only by players holding a persistent grant (the InventoryGrants table).
/// </summary>
public static class PlayerInventory
{
    private static readonly ConcurrentDictionary<uint, FrozenSet<Key>> Grants = new();

    public static bool RequiresGrant(Card card) => card.Scope == ScopeType.Private;

    public static bool Owns(uint playerId, Key item) =>
        item.GetCard<Card>() is { } card &&
        (!RequiresGrant(card) || (Grants.TryGetValue(playerId, out var owned) && owned.Contains(item)));

    /// <summary>A skin is wearable when the player owns it and it belongs to that hero. No skin is always allowed.</summary>
    public static bool CanEquipSkin(uint playerId, Key hero, Key skin) =>
        skin == Key.None || (skin.GetCard<CardSkin>() is { } card && card.HeroKey == hero && Owns(playerId, skin));

    /// <summary>The requested skin if wearable, otherwise the hero's first skin the player owns.</summary>
    public static Key SafeSkin(uint playerId, Key hero, Key skin)
    {
        if (CanEquipSkin(playerId, hero, skin)) return skin;
        return (hero.GetCard<CardUnit>()?.Data as UnitDataPlayer)?.Skins?
            .FirstOrDefault(candidate => CanEquipSkin(playerId, hero, candidate)) ?? Key.None;
    }

    /// <summary>Everything the client is told this player owns: the same list at login, on a grant change and in the panel.</summary>
    public static List<InventoryItem> Build(uint playerId, PlayerRole role)
    {
        var globalLogic = CatalogueHelper.GlobalLogic;

        var inventory = new List<InventoryItem>();
        bool Owned(Card card) => PlayerInventory.Owns(playerId, card.Key);
        var deviceCards = CatalogueHelper.GetCards<CardDevice>(CardCategory.Device).Where(Owned);
        var heroCards = CatalogueHelper.GetHeroes().Select(h => h.GetCard<CardUnit>()).OfType<CardUnit>().Where(Owned);
        var skinCards = CatalogueHelper.GetCards<CardSkin>(CardCategory.Skin).Where(Owned);

        var offPerks = globalLogic.Perks?.Offensive?.Select(p => p.GetCard<CardPerk>()).OfType<CardPerk>() ?? [];
        var defPerks = globalLogic.Perks?.Defensive?.Select(p => p.GetCard<CardPerk>()).OfType<CardPerk>() ?? [];
        var heroPerks = globalLogic.Perks?.Heroes?.SelectMany(p => p.Value.Select(perk => perk.GetCard<CardPerk>())).OfType<CardPerk>() ?? [];

        var perkCards = heroPerks.Union(offPerks.Union(defPerks)).Where(Owned);
        var badgeCards = (globalLogic.AvailableBadges?.Select(b => b.GetCard<CardBadge>()).OfType<CardBadge>() ?? []).Where(Owned);

        if (role is not PlayerRole.Core)
        {
            badgeCards = badgeCards.Where(b => b.Id != "badge_icon_community_representative");
        }

        var purchaseTime = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
        inventory.AddRange(deviceCards.Select(deviceCard => new InventoryItem { Item = deviceCard.Key }).ToList());
        inventory.AddRange(heroCards.Select(heroCard => new InventoryItem { Item = heroCard.Key, PurchaseTime = purchaseTime }).ToList());
        inventory.AddRange(skinCards.Select(skinCard => new InventoryItem { Item = skinCard.Key, PurchaseTime = purchaseTime }).ToList());
        inventory.AddRange(perkCards.Select(perkCard => new InventoryItem { Item = perkCard.Key, PurchaseTime = purchaseTime }).ToList());
        inventory.AddRange(badgeCards.Select(badgeCard => new InventoryItem { Item = badgeCard.Key }).ToList());
        return inventory;
    }

    public static IReadOnlySet<Key> GrantsFor(uint playerId) =>
        Grants.TryGetValue(playerId, out var owned) ? owned : FrozenSet<Key>.Empty;

    public static void Load(IEnumerable<InventoryGrantRecord> records)
    {
        var byPlayer = records.GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.Select(r => Catalogue.Key(r.Item)).ToFrozenSet());
        foreach (var playerId in Grants.Keys.Except(byPlayer.Keys)) Grants.TryRemove(playerId, out _);
        foreach (var (playerId, owned) in byPlayer) Grants[playerId] = owned;
    }

    public static void Set(uint playerId, IEnumerable<string> itemIds)
    {
        var owned = itemIds.Select(Catalogue.Key).ToFrozenSet();
        if (owned.Count == 0) Grants.TryRemove(playerId, out _);
        else Grants[playerId] = owned;
    }
}
