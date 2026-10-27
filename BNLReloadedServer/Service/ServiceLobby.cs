using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Service;

public class ServiceLobby(ISender sender) : IServiceLobby
{
    private enum ServiceLobbyId : byte
    {
        MessageLobbyUpdate = 0,
        MessageClearLobby = 1,
        MessageSwitchHero = 2,
        MessageAddDevice = 3,
        MessageRemoveDevice = 4,
        MessageSwitchDevice = 5,
        MessageSetDefaultDevices = 6,
        MessageSelectPerk = 7,
        MessageDeselectPerk = 8,
        MessageSelectSkin = 9,
        MessageSelectRole = 10,
        MessageVoteMap = 11,
        MessagePlayerReady = 12,
        MessageReceiveMatchLoadingProgress = 13,
        MessageSendMatchLoadingProgress = 14,
        MessageRequeueAsIs = 15,
        MessageRequeueAsTeam = 16,
        MessageExitToMenu = 17,
        MessageExitToCustomGame = 18,
        MessageExitToMenuAsSquad = 19,
        MessageVoteHeroBan = 20,
        MessageRandomHeroAndReady = 21
    }

    private readonly IRegionServerDatabase _serverDatabase = Databases.RegionServerDatabase;
    private IGameInstance? GameInstance => Databases.RegionServerDatabase.GetGameInstance(sender.AssociatedPlayerId);

    private static BinaryWriter CreateWriter()
    {
        var memStream = new MemoryStream();
        var writer = new BinaryWriter(memStream);
        writer.Write((byte)ServiceId.ServiceLobby);
        return writer;
    }

    public void SendLobbyUpdate(LobbyUpdate update)
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServiceLobbyId.MessageLobbyUpdate);
        LobbyUpdate.WriteRecord(writer, update);
        sender.Send(writer);
    }

    public void SendClearLobby()
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServiceLobbyId.MessageClearLobby);
        sender.Send(writer);
    }

    private void ReceiveSwitchHero(BinaryReader reader)
    {
        var heroKey = Key.ReadRecord(reader);
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.SwapHero(sender.AssociatedPlayerId.Value, heroKey);
    }

    private void ReceiveAddDevice(BinaryReader reader)
    {
        var deviceKey = Key.ReadRecord(reader);
        var slot = reader.ReadInt32();
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.UpdateDeviceSlot(sender.AssociatedPlayerId.Value, slot, deviceKey);
    }

    private void ReceiveRemoveDevice(BinaryReader reader)
    {
        var slot = reader.ReadInt32();
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.UpdateDeviceSlot(sender.AssociatedPlayerId.Value, slot, null);
    }

    private void ReceiveSwitchDevice(BinaryReader reader)
    {
        var slot1 = reader.ReadInt32();
        var slot2 = reader.ReadInt32();
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.SwapDevices(sender.AssociatedPlayerId.Value, slot1, slot2);
    }

    private void ReceiveSetDefaultDevices(BinaryReader reader)
    {
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.ResetToDefaultDevices(sender.AssociatedPlayerId.Value);
    }

    private void ReceiveSelectPerk(BinaryReader reader)
    {
        var perkKey = Key.ReadRecord(reader);
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.SelectPerk(sender.AssociatedPlayerId.Value, perkKey);
    }

    private void ReceiveDeselectPerk(BinaryReader reader)
    {
        var perkKey = Key.ReadRecord(reader);
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.DeselectPerk(sender.AssociatedPlayerId.Value, perkKey);
    }

    private void ReceiveSelectSkin(BinaryReader reader)
    {
        var skinKey = Key.ReadRecord(reader);
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.SelectSkin(sender.AssociatedPlayerId.Value, skinKey);
    }

    private void ReceiveSelectRole(BinaryReader reader)
    {
        var role = reader.ReadByteEnum<PlayerRoleType>();
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.SelectRole(sender.AssociatedPlayerId.Value, role);
    }

    private void ReceiveVoteMap(BinaryReader reader)
    {
        var mapKey = Key.ReadRecord(reader);
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.VoteForMap(sender.AssociatedPlayerId.Value, mapKey);
    }

    private void ReceivePlayerReady(BinaryReader reader)
    {
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.PlayerReady(sender.AssociatedPlayerId.Value);
    }

    private void ReceiveMatchLoadingProgress(BinaryReader reader)
    {
        var progress = reader.ReadSingle();
        if (sender.AssociatedPlayerId == null) return;
        GameInstance?.LoadProgressUpdate(sender.AssociatedPlayerId.Value, progress);
    }

    public void SendMatchLoadingProgress(Dictionary<uint, float> playersProgress)
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServiceLobbyId.MessageSendMatchLoadingProgress);
        writer.WriteMap(playersProgress, writer.Write, writer.Write);
        sender.Send(writer);
    }

    private void ReceiveRequeueAsIs(BinaryReader reader)
    {

    }

    private void ReceiveRequeueAsTeam(BinaryReader reader)
    {

    }

    private void ReceiveExitToMenu(BinaryReader reader)
    {
        if (sender.AssociatedPlayerId.HasValue)
        {
            var playerId = sender.AssociatedPlayerId.Value;
            var gameInstance = GameInstance;
            gameInstance?.PlayerLeftInstance(playerId, KickReason.MatchQuit);
            _serverDatabase.RemoveFromCustomGame(playerId);
        }
    }

    private async Task ReceiveExitToCustomGame(BinaryReader reader)
    {
        if (sender.AssociatedPlayerId.HasValue)
        {
            GameInstance?.NotifyExitToCustom();
            await Task.Delay(5000);
            GameInstance?.RemoveAllPlayers();
        }
    }

    private async Task ReceiveExitToMenuAsSquad(BinaryReader reader)
    {
        if (sender.AssociatedPlayerId.HasValue)
        {
            var squad = _serverDatabase.GetSquadId(sender.AssociatedPlayerId.Value);
            // Taking the whole squad out of a match is the leader's call: any member could
            // otherwise end the match for everyone they are queued with.
            if (squad != null && _serverDatabase.IsSquadLeader(sender.AssociatedPlayerId.Value))
            {
                await Task.Delay(5000);
                GameInstance?.RemoveAllFromSquad(squad.Value);
            }
        }
    }

    public bool Receive(BinaryReader reader)
    {
        var serviceLobbyId = reader.ReadByte();
        ServiceLobbyId? lobbyEnum = null;
        if (Enum.IsDefined(typeof(ServiceLobbyId), serviceLobbyId))
        {
            lobbyEnum = (ServiceLobbyId)serviceLobbyId;
        }

        Log.Debug(LogCat.Net, $"Service Lobby ID: {Log.EnumName(lobbyEnum, serviceLobbyId)}");

        switch (lobbyEnum)
        {
            case ServiceLobbyId.MessageRandomHeroAndReady:
                if (sender.AssociatedPlayerId is { } randomPlayer && GameInstance is GameInstance randomInstance && randomInstance.Lobby is { } randomLobby)
                    randomLobby.EnqueueAction(() => randomLobby.RandomHeroAndReady(randomPlayer));
                break;
            case ServiceLobbyId.MessageVoteHeroBan:
                var phase = reader.ReadInt32();
                var hero = Key.ReadRecord(reader);
                if (sender.AssociatedPlayerId is { } voter && GameInstance is GameInstance instance && instance.Lobby is { } lobby)
                    lobby.EnqueueAction(() => lobby.VoteHeroBan(voter, phase, hero));
                break;
            case ServiceLobbyId.MessageSwitchHero:
                ReceiveSwitchHero(reader);
                break;
            case ServiceLobbyId.MessageAddDevice:
                ReceiveAddDevice(reader);
                break;
            case ServiceLobbyId.MessageRemoveDevice:
                ReceiveRemoveDevice(reader);
                break;
            case ServiceLobbyId.MessageSwitchDevice:
                ReceiveSwitchDevice(reader);
                break;
            case ServiceLobbyId.MessageSetDefaultDevices:
                ReceiveSetDefaultDevices(reader);
                break;
            case ServiceLobbyId.MessageSelectPerk:
                ReceiveSelectPerk(reader);
                break;
            case ServiceLobbyId.MessageDeselectPerk:
                ReceiveDeselectPerk(reader);
                break;
            case ServiceLobbyId.MessageSelectSkin:
                ReceiveSelectSkin(reader);
                break;
            case ServiceLobbyId.MessageSelectRole:
                ReceiveSelectRole(reader);
                break;
            case ServiceLobbyId.MessageVoteMap:
                ReceiveVoteMap(reader);
                break;
            case ServiceLobbyId.MessagePlayerReady:
                ReceivePlayerReady(reader);
                break;
            case ServiceLobbyId.MessageReceiveMatchLoadingProgress:
                ReceiveMatchLoadingProgress(reader);
                break;
            case ServiceLobbyId.MessageRequeueAsIs:
                ReceiveRequeueAsIs(reader);
                break;
            case ServiceLobbyId.MessageRequeueAsTeam:
                ReceiveRequeueAsTeam(reader);
                break;
            case ServiceLobbyId.MessageExitToMenu:
                ReceiveExitToMenu(reader);
                break;
            case ServiceLobbyId.MessageExitToCustomGame:
                ReceiveExitToCustomGame(reader).ObserveFailure(LogCat.Net,
                    "Failed to move match players back to their custom game");
                break;
            case ServiceLobbyId.MessageExitToMenuAsSquad:
                ReceiveExitToMenuAsSquad(reader).ObserveFailure(LogCat.Net,
                    "Failed to move a squad from the match to the menu");
                break;
            default:
                Log.Warn(LogCat.Net, $"Unknown service lobby id {Log.EnumName(lobbyEnum, serviceLobbyId)}");
                return false;
        }

        return true;
    }
}
