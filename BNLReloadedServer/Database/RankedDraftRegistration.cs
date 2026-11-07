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
                mode.LobbyMode = new LobbyModeFreePick { SelectionTime = 60,
                    PrestartTime = 5, ReconnectSelectionTime = 60 };
                mode.HeroLimit = null; // Casual permits duplicates and simultaneous selection without draft class minimums.
            }
            else if (mode.Ranking == GameRankingType.Friendly && mode.LobbyMode is LobbyModeDraftPick draft)
                mode.LobbyMode = new LobbyModeFreePick { SelectionTime = 60, PrestartTime = draft.PrestartTime,
                    ReconnectSelectionTime = draft.ReconnectSelectionTime };
        }
    }
}
