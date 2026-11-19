using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Service;
namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private readonly Dictionary<uint,ServiceZone> _adminSpectatorServices=[];
    // Membership remains spectator throughout; no lobby/roster/stat/participation entry is created.
    public void ReceivedAdminSpectator(uint playerId,IReadOnlyList<string> args,ServiceZone service)
    {
        if(!_gameInitiator.IsPlayerSpectator(playerId) || !service.SupportsAdminSpectator || args.Count!=6 ||
           !AdminFunnyRules.IsAdmin(Databases.PlayerDatabase.GetPlayerDataNoWait(playerId)?.Role))return;
        if(args[0]=="off"){EndAdminSpectator(playerId);return;}
        if(_playerIdToUnitId.TryGetValue(playerId,out var existing) && _playerUnits.TryGetValue(existing,out var body))
        {if(body.AdminSpectatorEntity)ReceivedAdminFunny(body,args);return;}
        if(args[0]!="on" || HasEnded || _zoneData.MatchEnded || _gameInitiator.IsMapEditor())return;
        var profile=Databases.PlayerDatabase.GetPlayerDataNoWait(playerId);
        var hero=profile?.LastPlayedHero?.GetCard<CardUnit>();
        if(hero?.Data is not UnitDataPlayer)hero=CatalogueHelper.GetHeroes().Select(k=>k.GetCard<CardUnit>()).FirstOrDefault(c=>c?.Data is UnitDataPlayer);
        if(hero?.Data is not UnitDataPlayer { Skins.Count: >0 } heroData || heroData.Gears is not {Count:>0})return;
        var skin=heroData.Skins.FirstOrDefault(k=>k.GetCard<CardSkin>()!=null);
        if(skin==default)return;
        var spawn=_mapSpawnPoints.Values.FirstOrDefault(p=>p.Team is TeamType.Team1 or TeamType.Team2);
        if(spawn==null)return;
        var info=new PlayerLobbyState {PlayerId=playerId,Nickname="",Hero=hero.Key,SkinKey=skin,Team=TeamType.Neutral};
        _adminSpectatorServices.Add(playerId,service);
        service.SendAdminSpectator(true); // Ordered before owned UnitCreate, enabling native player control.
        try
        {
            var unit=CatalogueFactory.CreatePlayerUnit(NewUnitId(),playerId,
                ZoneTransformHelper.ToZoneTransform(GetSpawnPosition(spawn.Position,2),Quaternion.Identity),info,_gameInitiator,_zoneData.MatchCard,
                _defaultUnitUpdater with {OnUnitInit=(u,init)=>{u.AdminSpectatorEntity=true;UnitCreated(u,init,service);}},0);
            if(unit==null){EndAdminSpectator(playerId);return;}
            unit.ZoneService=service;
            // Build/wait phases normally defer ownership; this transient body is usable immediately.
            _createOnStart.RemoveAll(c=>c.Item1==unit.Id);
            ReceivedAdminFunny(unit,args);
            if(!_funnyModes.ContainsKey(unit.Id))EndAdminSpectator(playerId);
        }
        catch{EndAdminSpectator(playerId);throw;}
    }
    private void EndAdminSpectator(uint playerId)
    {
        if(!_adminSpectatorServices.Remove(playerId,out var service))return;
        if(_playerIdToUnitId.TryGetValue(playerId,out var id) && _playerUnits.TryGetValue(id,out var unit) && unit.AdminSpectatorEntity)
        {
            RemoveFunny(id);unit.IsActive=false;unit.RespawnTime=null;
            _unitsToDrop.Remove(unit);DropUnit(unit);_playerUnits.Remove(id);_playerIdToUnitId.Remove(playerId);
            _createOnStart.RemoveAll(c=>c.Item1==id);_updateOnStart.RemoveAll(c=>c.Item1==id);
            RemoveUnit(id,"admin-spectator-return");
        }
        _zoneData.RespawnInfo.Remove(playerId);_zoneData.PlayerSpawnPoints.Remove(playerId);
        service.SendAdminSpectator(false);
    }
}
