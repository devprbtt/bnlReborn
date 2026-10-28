using System.Collections.Concurrent;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>The weapon skins a player wears with one character skin (card ids, comma-separated).</summary>
[Table("PlayerWeaponSkins")]
public sealed class WeaponSkinSelectionRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed(Name = "UX_PlayerWeaponSkins", Order = 1, Unique = true), Column("player_id")] public uint PlayerId { get; set; }
    [Indexed(Name = "UX_PlayerWeaponSkins", Order = 2, Unique = true), Column("character_skin")] public string CharacterSkin { get; set; } = string.Empty;
    [Column("weapon_skin")] public string WeaponSkin { get; set; } = string.Empty;
}

public enum WeaponSkinChange { Set, Cleared, UnknownSkin, NotWeaponSkin, WrongHero, NotOwned }

/// <summary>
/// Weapon skins are private CardSkin cards named skin_weapon_*: they belong to a hero and recolour its weapons on
/// whichever character skin the player wears. A weapon skin covers one or more of the hero's weapons; each character
/// skin wears a list of them, one per weapon. Which weapons a skin covers is client art data, so the client sends the
/// whole list and keeps one per weapon; the server checks each entry. Entries are only honoured while the player still
/// owns them, so a revoked grant falls back to stock for that weapon alone.
/// </summary>
public static class WeaponSkinSelections
{
    public const string Prefix = "skin_weapon_";
    /// <summary>More than any hero has weapons; bounds a hostile list.</summary>
    public const int MaxPerSkin = 8;
    private static readonly ConcurrentDictionary<uint, Dictionary<Key, Key[]>> Selections = new();

    public static bool IsWeaponSkin(CardSkin? card) => card?.Id?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static WeaponSkinChange Validate(uint playerId, Key characterSkin, Key weaponSkin)
    {
        if (characterSkin.GetCard<CardSkin>() is not { } character || IsWeaponSkin(character)) return WeaponSkinChange.UnknownSkin;
        if (weaponSkin == Key.None) return WeaponSkinChange.Cleared;
        if (weaponSkin.GetCard<CardSkin>() is not { } weapon || !IsWeaponSkin(weapon)) return WeaponSkinChange.NotWeaponSkin;
        if (weapon.HeroKey != character.HeroKey) return WeaponSkinChange.WrongHero;
        return PlayerInventory.Owns(playerId, weaponSkin) ? WeaponSkinChange.Set : WeaponSkinChange.NotOwned;
    }

    /// <summary>A whole list for one character skin: Set when every entry is valid, Cleared when it is empty.</summary>
    public static WeaponSkinChange Validate(uint playerId, Key characterSkin, IReadOnlyCollection<Key> weaponSkins)
    {
        if (weaponSkins.Count > MaxPerSkin) return WeaponSkinChange.NotWeaponSkin;
        if (weaponSkins.Count == 0) return Validate(playerId, characterSkin, Key.None);
        foreach (var weaponSkin in weaponSkins)
        {
            if (weaponSkin == Key.None) return WeaponSkinChange.NotWeaponSkin;
            var change = Validate(playerId, characterSkin, weaponSkin);
            if (change != WeaponSkinChange.Set) return change;
        }
        return WeaponSkinChange.Set;
    }

    /// <summary>The weapon skins to show for a player wearing a character skin, in the order chosen (none: stock).</summary>
    public static Key[] For(uint playerId, Key characterSkin) =>
        Selections.TryGetValue(playerId, out var chosen) && chosen.TryGetValue(characterSkin, out var weapons)
            ? weapons.Where(w => Validate(playerId, characterSkin, w) == WeaponSkinChange.Set).ToArray()
            : [];

    /// <summary>Card ids joined with commas, the form the client reads.</summary>
    public static string Ids(IEnumerable<Key> weaponSkins) =>
        string.Join(",", weaponSkins.Select(w => w.GetCard<CardSkin>()?.Id).OfType<string>());

    /// <summary>Every honoured choice of one player, character skin id to its weapon skin ids.</summary>
    public static Dictionary<string, string> Of(uint playerId)
    {
        var result = new Dictionary<string, string>();
        if (!Selections.TryGetValue(playerId, out var chosen)) return result;
        foreach (var character in chosen.Keys)
            if (Ids(For(playerId, character)) is { Length: > 0 } ids && character.GetCard<CardSkin>()?.Id is { } c)
                result[c] = ids;
        return result;
    }

    public static string Json(uint playerId) => JsonSerializer.Serialize(Of(playerId));

    public static void Load(IEnumerable<WeaponSkinSelectionRecord> records)
    {
        Selections.Clear();
        foreach (var group in records.GroupBy(r => r.PlayerId))
            Selections[group.Key] = group.ToDictionary(r => Catalogue.Key(r.CharacterSkin), r => Parse(r.WeaponSkin));
    }

    private static Key[] Parse(string ids) =>
        ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Catalogue.Key).ToArray();

    public static void Remember(uint playerId, Key characterSkin, IReadOnlyCollection<Key> weaponSkins)
    {
        var chosen = Selections.GetOrAdd(playerId, _ => new Dictionary<Key, Key[]>());
        lock (chosen)
        {
            var copy = new Dictionary<Key, Key[]>(chosen);
            if (weaponSkins.Count == 0) copy.Remove(characterSkin); else copy[characterSkin] = weaponSkins.ToArray();
            Selections[playerId] = copy;
        }
    }
}
