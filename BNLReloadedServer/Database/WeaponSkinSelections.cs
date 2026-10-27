using System.Collections.Concurrent;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>The weapon skin a player wears with one character skin.</summary>
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
/// whichever character skin the player wears. Each player picks at most one per character skin. Choices are only
/// honoured while the player still owns the weapon skin, so a revoked grant falls back to stock weapons.
/// </summary>
public static class WeaponSkinSelections
{
    public const string Prefix = "skin_weapon_";
    private static readonly ConcurrentDictionary<uint, Dictionary<Key, Key>> Selections = new();

    public static bool IsWeaponSkin(CardSkin? card) => card?.Id?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static WeaponSkinChange Validate(uint playerId, Key characterSkin, Key weaponSkin)
    {
        if (characterSkin.GetCard<CardSkin>() is not { } character || IsWeaponSkin(character)) return WeaponSkinChange.UnknownSkin;
        if (weaponSkin == Key.None) return WeaponSkinChange.Cleared;
        if (weaponSkin.GetCard<CardSkin>() is not { } weapon || !IsWeaponSkin(weapon)) return WeaponSkinChange.NotWeaponSkin;
        if (weapon.HeroKey != character.HeroKey) return WeaponSkinChange.WrongHero;
        return PlayerInventory.Owns(playerId, weaponSkin) ? WeaponSkinChange.Set : WeaponSkinChange.NotOwned;
    }

    /// <summary>The weapon skin to show for a player wearing a character skin, or None.</summary>
    public static Key For(uint playerId, Key characterSkin) =>
        Selections.TryGetValue(playerId, out var chosen) && chosen.TryGetValue(characterSkin, out var weapon) &&
        Validate(playerId, characterSkin, weapon) == WeaponSkinChange.Set ? weapon : Key.None;

    /// <summary>Every honoured choice of one player, character skin id to weapon skin id.</summary>
    public static Dictionary<string, string> Of(uint playerId)
    {
        var result = new Dictionary<string, string>();
        if (!Selections.TryGetValue(playerId, out var chosen)) return result;
        foreach (var (character, weapon) in chosen)
            if (For(playerId, character) != Key.None && character.GetCard<CardSkin>()?.Id is { } c && weapon.GetCard<CardSkin>()?.Id is { } w)
                result[c] = w;
        return result;
    }

    public static string Json(uint playerId) => JsonSerializer.Serialize(Of(playerId));

    public static void Load(IEnumerable<WeaponSkinSelectionRecord> records)
    {
        Selections.Clear();
        foreach (var group in records.GroupBy(r => r.PlayerId))
            Selections[group.Key] = group.ToDictionary(r => Catalogue.Key(r.CharacterSkin), r => Catalogue.Key(r.WeaponSkin));
    }

    public static void Remember(uint playerId, Key characterSkin, Key weaponSkin)
    {
        var chosen = Selections.GetOrAdd(playerId, _ => new Dictionary<Key, Key>());
        lock (chosen)
        {
            var copy = new Dictionary<Key, Key>(chosen);
            if (weaponSkin == Key.None) copy.Remove(characterSkin); else copy[characterSkin] = weaponSkin;
            Selections[playerId] = copy;
        }
    }
}
