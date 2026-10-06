using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.Clans;

public sealed class ClanFightInitiator(List<PlayerQueueData> home, List<PlayerQueueData> away, int team1Clan, int team2Clan)
    : MatchmakerInitiator(CatalogueHelper.ModeRanked, home, away, home.Count)
{
    public int Team1Clan { get; } = team1Clan;
    public int Team2Clan { get; } = team2Clan;
    public override bool NeedsBackfill() => false;
}
