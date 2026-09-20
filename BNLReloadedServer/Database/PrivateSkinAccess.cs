using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

/// <summary>Private authoring skins are granted only to the verified test Steam account.</summary>
public static class PrivateSkinAccess
{
    public const ulong TestSteamId = 76561197990315750;
    public static readonly Key ArcticWolf = new("skin_hunter_arctic_wolf_private");
    public static readonly Key Demon = new("skin_boxer_demon_private");
    private static readonly Key Hunter = new("unit_hero_hunter");
    private static readonly Key Boxer = new("unit_hero_boxer");

    public static bool IsPrivate(Key skin) => skin == ArcticWolf || skin == Demon;
    public static bool CanUse(ulong? steamId, Key skin) => !IsPrivate(skin) || steamId == TestSteamId;
    public static bool CanEquip(ulong? steamId, Key hero, Key skin) => CanUse(steamId, skin) &&
        (skin != ArcticWolf || hero == Hunter) && (skin != Demon || hero == Boxer);
    public static Key SafeSkin(ulong? steamId, Key hero, Key skin)
    {
        if (CanEquip(steamId, hero, skin)) return skin;
        return (hero.GetCard<CardUnit>()?.Data as UnitDataPlayer)?.Skins?
            .FirstOrDefault(candidate => !IsPrivate(candidate)) ?? Key.None;
    }
}
