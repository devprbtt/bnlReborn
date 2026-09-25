using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Database;

public static class PlayerDataSanitizer
{
    public static bool SanitizeAgainstCatalogue(this PlayerData player)
    {
        if (!Databases.Catalogue.All.Any()) return false;

        var stripped = new List<string>();

        SanitizeBadges(player, stripped);
        SanitizeLastPlayedHero(player, stripped);
        SanitizeLoadouts(player, stripped);
        SanitizeHeroStats(player, stripped);
        SanitizeMatchHistory(player, stripped);
        SanitizeTimeTrial(player, stripped);
        SanitizeProgression(player, stripped);

        if (stripped.Count == 0) return false;

        Log.Warn(LogCat.Player,
            $"Player {player.PlayerId} ({player.Nickname}): removed stale card references: {string.Join(", ", stripped)}");
        return true;
    }

    private static bool IsMissing<T>(Key key) where T : Card => key != Key.None && key.GetCard<T>() == null;

    private static void SanitizeBadges(PlayerData player, List<string> stripped)
    {
        foreach (var (badgeType, badges) in player.Badges)
        {
            var removed = badges.Where(b => IsMissing<CardBadge>(b)).ToList();
            if (removed.Count == 0) continue;
            badges.RemoveAll(b => IsMissing<CardBadge>(b));
            stripped.Add($"badges {badgeType} [{string.Join(' ', removed)}]");
        }
    }

    private static void SanitizeLastPlayedHero(PlayerData player, List<string> stripped)
    {
        if (player.LastPlayedHero is not { } hero || !IsMissing<CardUnit>(hero)) return;
        player.LastPlayedHero = null;
        stripped.Add($"last played hero {hero}");
    }

    private static void SanitizeLoadouts(PlayerData player, List<string> stripped)
    {
        foreach (var (heroKey, loadout) in player.HeroLoadouts.ToList())
        {
            if (IsMissing<CardUnit>(heroKey) || IsMissing<CardUnit>(loadout.HeroKey) ||
                IsMissing<CardSkin>(loadout.SkinKey) ||
                !PlayerInventory.CanEquipSkin(player.PlayerId, heroKey, loadout.SkinKey))
            {
                player.HeroLoadouts.Remove(heroKey);
                stripped.Add($"loadout {heroKey} (hero {loadout.HeroKey}, skin {loadout.SkinKey})");
                continue;
            }

            if (loadout.Devices != null)
            {
                foreach (var (slot, device) in loadout.Devices.ToList())
                {
                    if (!IsMissing<CardDevice>(device)) continue;
                    loadout.Devices.Remove(slot);
                    stripped.Add($"device {device} in slot {slot} of loadout {heroKey}");
                }

                if (CatalogueHelper.RepairHeroSpecialDevice(heroKey, loadout.Devices,
                        out var replacedDevice, out var defaultDevice))
                    stripped.Add($"invalid special device {replacedDevice} -> {defaultDevice} in loadout {heroKey}");
            }

            if (loadout.Perks == null) continue;
            var removedPerks = loadout.Perks.Where(p => IsMissing<CardPerk>(p)).ToList();
            if (removedPerks.Count == 0) continue;
            loadout.Perks.RemoveAll(p => IsMissing<CardPerk>(p));
            stripped.Add($"perks [{string.Join(' ', removedPerks)}] of loadout {heroKey}");
        }
    }

    private static void SanitizeHeroStats(PlayerData player, List<string> stripped)
    {
        var removed = player.HeroStats.Where(s => IsMissing<CardUnit>(s.Hero)).ToList();
        if (removed.Count == 0) return;
        player.HeroStats.RemoveAll(s => IsMissing<CardUnit>(s.Hero));
        stripped.Add($"hero stats [{string.Join(' ', removed.Select(s => s.Hero))}]");
    }

    private static void SanitizeMatchHistory(PlayerData player, List<string> stripped)
    {
        var removed = player.MatchHistory.Where(IsStaleMatch).ToList();
        if (removed.Count == 0) return;
        player.MatchHistory.RemoveAll(IsStaleMatch);
        stripped.Add($"{removed.Count} match history record(s)");
    }

    private static bool IsStaleMatch(MatchHistoryRecord match) =>
        IsMissing<CardUnit>(match.HeroKey) || IsMissing<CardSkin>(match.SkinKey) ||
        IsMissing<CardMap>(match.MapKey) || IsMissing<CardGameMode>(match.GameModeKey);

    private static void SanitizeTimeTrial(PlayerData player, List<string> stripped)
    {
        if (player.TimeTrial.CompletedGoals is { } goals)
        {
            foreach (var mapKey in goals.Keys.Where(IsMissing<CardMap>).ToList())
            {
                goals.Remove(mapKey);
                stripped.Add($"time trial goals for map {mapKey}");
            }
        }

        if (player.TimeTrial.BestResultTime is not { } bestTimes) return;
        foreach (var mapKey in bestTimes.Keys.Where(IsMissing<CardMap>).ToList())
        {
            bestTimes.Remove(mapKey);
            stripped.Add($"time trial best time for map {mapKey}");
        }
    }

    private static void SanitizeProgression(PlayerData player, List<string> stripped)
    {
        if (player.Progression.HeroesProgress is not { } heroesProgress) return;
        foreach (var heroKey in heroesProgress.Keys.Where(IsMissing<CardUnit>).ToList())
        {
            heroesProgress.Remove(heroKey);
            stripped.Add($"hero progression {heroKey}");
        }
    }
}
