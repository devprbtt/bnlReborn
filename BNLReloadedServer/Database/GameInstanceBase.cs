// Generated from IGameInstance: one no-op virtual per IGameInstance member, so a
// presentation-only instance (match replay playback) overrides only what it
// handles. Regenerate when IGameInstance changes.
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using Moserware.Skills;

namespace BNLReloadedServer.Database;

public abstract class GameInstanceBase : IGameInstance
{
    public virtual bool HasLobby() => false;
    public virtual bool IsStarted => false;
    public virtual bool IsOver() => false;
    public virtual MapInfo? GetMapInfo() => null;
    public virtual void LinkGuidToPlayer(uint userId, Guid guid, Guid regionGuid) { }
    public virtual void UserEnteredLobby(uint userId, bool enteringLobby) { }
    public virtual void PlayerDisconnected(uint userId) { }
    public virtual void PlayerLeftInstance(uint userId, KickReason reason) { }
    public virtual void DetachFromZoneStreams(uint userId) { }
    public virtual void RemoveAllFromSquad(ulong squadId, Action<uint>? onRemove = null) { }
    public virtual void RemoveAllPlayers(Action<uint>? onRemove = null) { }
    public virtual void NotifyExitToCustom() { }
    public virtual void SetMap(MapInfo? mapInfo, MapData map) { }
    public virtual bool IsMapNull() => false;
    public virtual void SetMatchKey(Key matchKey) { }
    public virtual void RegisterServices(Guid sessionId, Dictionary<ServiceId, IService> services) { }
    public virtual void RemoveService(Guid sessionId) { }
    public virtual void CreateLobby(Key gameModeKey, MapInfo? mapInfo) { }
    public virtual ChatRoom? GetChatRoom(RoomId roomId) => null;
    public virtual Key GetGameMode() => Key.None;
    public virtual bool IsPlayerSpectator(uint playerId) => false;
    public virtual bool NeedsBackfill() => false;
    public virtual (Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2) GetTeamRatings() => default!;
    public virtual void SendAfkWarning(uint playerId) { }
    public virtual void SwapHero(uint playerId, Key hero) { }
    public virtual void UpdateDeviceSlot(uint playerId, int slot, Key? deviceKey) { }
    public virtual void SwapDevices(uint playerId, int slot1, int slot2) { }
    public virtual void ResetToDefaultDevices(uint playerId) { }
    public virtual void SelectPerk(uint playerId, Key perkKey) { }
    public virtual void DeselectPerk(uint playerId, Key perkKey) { }
    public virtual void SelectSkin(uint playerId, Key skinKey) { }
    public virtual void SelectRole(uint playerId, PlayerRoleType role) { }
    public virtual void VoteForMap(uint playerId, Key mapKey) { }
    public virtual void PlayerReady(uint playerId) { }
    public virtual void LoadProgressUpdate(uint playerId, float progress) { }
    public virtual void StartMatch(ICollection<PlayerLobbyState> playerList, bool restart = false) { }
    public virtual void SendUserToZone(uint playerId) { }
    public virtual bool SendWaitingArenaUserToLobby(uint playerId) => false;
    public virtual void PlayerEnterScene(uint playerId) { }
    public virtual void PlayerZoneReady(uint playerId) { }
    public virtual void UnitMoved(uint unitId, ulong moveTime, ZoneTransform transform) { }
    public virtual void BuildRequest(ushort rpcId, uint playerId, BuildInfo buildInfo, IServiceZone builderService) { }
    public virtual void CancelBuildRequest(uint playerId) { }
    public virtual void EventBroadcast(uint playerId, ZoneEvent zoneEvent) { }
    public virtual void SwitchGear(ushort rpcId, uint playerId, Key gearKey, IServiceZone switcherService) { }
    public virtual void StartReload(ushort rpcId, uint playerId, IServiceZone reloaderService) { }
    public virtual void Reload(ushort rpcId, uint playerId, IServiceZone reloaderService) { }
    public virtual void ReloadEnd(uint playerId) { }
    public virtual void ReloadCancel(uint playerId) { }
    public virtual void CreateProj(uint playerId, ulong shotId, ProjectileInfo projInfo) { }
    public virtual void MoveProj(ulong shotId, ulong time, ZoneTransform transform) { }
    public virtual void DropProj(ulong shotId) { }
    public virtual void CreateChannel(ushort rpcId, uint playerId, ChannelData channelData, IServiceZone channelService) { }
    public virtual void EndChannel(uint playerId) { }
    public virtual void ToolChargeStart(uint playerId, byte toolIndex) { }
    public virtual void ToolChargeEnd(ushort rpcId, uint playerId, byte toolIndex, IServiceZone chargeService) { }
    public virtual void DashChargeStart(uint playerId, byte toolIndex) { }
    public virtual void DashChargeEnd(ushort rpcId, uint playerId, byte toolIndex, IServiceZone dashService) { }
    public virtual void DashCast(uint playerId, byte toolIndex) { }
    public virtual void DashHit(uint playerId, byte toolIndex, HitData hitData) { }
    public virtual void GroundSlamCast(uint playerId, byte toolIndex) { }
    public virtual void GroundSlamHit(uint playerId, byte toolIndex, HitData hitData) { }
    public virtual void AbilityCast(ushort rpcId, uint playerId, AbilityCastData castData, IServiceZone abilityService) { }
    public virtual void UnitProjectileHit(uint unitId, HitData hitData) { }
    public virtual void SkybeamHit(uint unitId, HitData hitData) { }
    public virtual void CastRequest(uint playerId, CastData castData) { }
    public virtual void Hit(ulong time, Dictionary<ulong, HitData> hits) { }
    public virtual void Fall(uint playerId, uint unitId, float height, bool force) { }
    public virtual void Pickup(uint playerId, uint pickupId) { }
    public virtual void SelectSpawnPoint(uint playerId, uint? spawnId) { }
    public virtual void TurretTarget(uint playerId, uint turretId, uint targetId) { }
    public virtual void TurretAttack(uint playerId, uint turretId, Vector3 shotPos, List<ShotData> shots) { }
    public virtual void MortarAttack(uint mortarId, Vector3 shotPos, List<ShotData> shots) { }
    public virtual void DrillAttack(uint drillId, Vector3 shotPos, List<ShotData> shots) { }
    public virtual void UpdateTesla(uint teslaId, uint? targetId, List<uint> teslasInRange) { }
    public virtual void PlayerCommand(uint playerId, Key command) { }
    public virtual void TeamPing(uint playerId, Vector3 position, Vector3 normal) { }
    public virtual void HeroEmote(uint playerId, bool active, int emoteIndex) { }
    public virtual void AimLook(uint playerId, byte[] body) { }
    public virtual void ScoreboardMetadata(uint playerId, IServiceZone requester) { }
    public virtual void StartRecall(uint playerId) { }
    public virtual void Surrender(ushort rpcId, uint playerId, IServiceZone surrenderService) { }
    public virtual void SurrenderVote(uint playerId, bool accept) { }
    public virtual void EditorCommand(uint playerId, MapEditorCommand command, bool force) { }
    public virtual void DebugSpawnSupply(string? blockbusterCardId) { }
    public virtual void DebugZoneCommand(uint playerId, string command, IReadOnlyList<string> args) { }
}
