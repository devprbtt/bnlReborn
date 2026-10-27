using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public static class RankedDraftRegistration
{
    public static void Register(IEnumerable<Card> cards)
    {
        foreach (var mode in cards.OfType<CardGameMode>())
        {
            if (mode.Id == "game_mode_friendly")
            {
                mode.LobbyMode = new LobbyModeDraftPick { WaitTime = 10, SelectionTime = 20,
                    PrestartTime = 5, ReconnectSelectionTime = 60 };
                mode.HeroLimit = new LobbyHeroLimit { LimitOption = LobbyHeroLimitOption.PerClass, Limit = 2 };
            }
            else if (mode.Ranking == GameRankingType.Friendly && mode.LobbyMode is LobbyModeDraftPick draft)
                mode.LobbyMode = new LobbyModeFreePick { SelectionTime = 60, PrestartTime = draft.PrestartTime,
                    ReconnectSelectionTime = draft.ReconnectSelectionTime };
        }
    }
}
