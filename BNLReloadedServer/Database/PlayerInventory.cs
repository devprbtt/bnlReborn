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
