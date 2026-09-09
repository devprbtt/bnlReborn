using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Service;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.Database;

public partial class GameInstance
{
    public void BuildPreview(uint playerId,uint generation,BuildInfo info) =>
        Zone?.EnqueueAction(() =>
        {
            if(Zone!=null && Zone.TryAcceptBuildPreview(playerId,generation,info,out var unitId))
                BroadcastBuildPreview(unitId,generation,false,info);
        });

    private void BroadcastBuildPreview(uint unitId,uint generation,bool initial,BuildInfo info)
    {
        foreach(var (_,player) in _connectedUsers)
        {
            if(player.LoadStage==ZoneLoadStage.Finished && _services.TryGetValue(player.Guid,out var services)
                && services.TryGetValue(ServiceId.ServiceZone,out var service) && service is ServiceZone zoneService)
                zoneService.SendBuildPreview(unitId,generation,initial,info);
        }
    }
}
