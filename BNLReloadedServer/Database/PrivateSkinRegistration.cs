using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

/// <summary>Private inventory entries with public rendering metadata and no shop offers.</summary>
public static class PrivateSkinRegistration
{
    public static void Register(List<Card> cards)
    {
        Add(cards, "hunter", "arctic_wolf", "Arctic Wolf", "skin_hunter_s1",
            "LongshotArcticWolfPrivate", "LongshotPlayerArcticWolfPrivate", "fps_longshot_s1");
        Add(cards, "boxer", "demon", "Demon", "skin_boxer_s6",
            "SweetScienceDarklordFinal", "SweetSciencePlayerS7", "fps_sweetscience_s6");
    }

    private static void Add(List<Card> cards, string hero, string variant, string name, string sourceId,
        string prefab, string fpsPrefab, string fpsBundle)
    {
        var source = cards.OfType<CardSkin>().FirstOrDefault(c => c.Id == sourceId);
        var unit = cards.OfType<CardUnit>().FirstOrDefault(c => c.Id == "unit_hero_" + hero);
        if (source == null || unit?.Data is not UnitDataPlayer player) return;
        string id = $"skin_{hero}_{variant}_private";
        var key = new Key(id);
        cards.RemoveAll(c => c.Id == id);
        cards.Add(new CardSkin
        {
            Id = id, Key = key, Scope = source.Scope,
            HeroKey = new Key("unit_hero_" + hero), Bundle = source.Bundle, FpsBundle = fpsBundle,
            Prefab = "Prefabs/Units/" + prefab, FpsPrefab = "Prefabs/Player/" + fpsPrefab,
            IconPortrait = $"portrait_{hero}_{variant}_private",
            IconPortraitProfile = $"shop_{hero}_{variant}_private",
            Name = new LocalizedString { Text = name, Data = [] },
            LearningMusic = source.LearningMusic, LockinMusic = source.LockinMusic,
            DeathMusicSting = source.DeathMusicSting
        });
        player.Skins ??= [];
        player.Skins.RemoveAll(skin => skin == key);
        player.Skins.Add(key);
    }
}
