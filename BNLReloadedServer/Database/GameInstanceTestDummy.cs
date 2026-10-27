using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;
namespace BNLReloadedServer.Database;
public partial class GameInstance
{
    public void SpawnCustomTestDummy(uint playerId, int health)
    {
        if (GameInitiator is not CustomGamePlayerGroup || Zone == null || !IsStarted || HasEnded == true ||
            !_connectedUsers.TryGetValue(playerId, out var player) || player.LoadStage != ZoneLoadStage.Finished) return;
        Zone.EnqueueAction(() => {
            if (IsCurrentConnection(playerId, player) && player.LoadStage == ZoneLoadStage.Finished)
                Zone.ReceivedCustomTestDummy(playerId, health);
        });
    }
}
