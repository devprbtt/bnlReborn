using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.Database;

public partial class GameInstance
{
    private readonly Dictionary<uint, DateTimeOffset> _lastTeamSwitch = new();

    public bool SwitchCustomTeam(uint playerId)
    {
        if (GameInitiator is not CustomGamePlayerGroup custom || !custom.GameInfo.TeamSwitching ||
            Zone == null || Lobby == null || !IsStarted || HasEnded == true ||
            !_connectedUsers.TryGetValue(playerId, out var player) || player.LoadStage != ZoneLoadStage.Finished)
            return false;
        Zone.EnqueueAction(() =>
        {
            if (!IsCurrentConnection(playerId, player) || player.LoadStage != ZoneLoadStage.Finished ||
                !Zone.HasCustomTeamParticipant(playerId)) return;
            var now = DateTimeOffset.UtcNow;
            if (_lastTeamSwitch.TryGetValue(playerId, out var last) && (now - last).TotalSeconds < 10) return;
            if (!custom.TrySwitchInMatch(playerId, out var team)) return;
            _lastTeamSwitch[playerId] = now;
            player.LoadStage = ZoneLoadStage.None;
            _heroChangeDisconnects[playerId] = player.Guid;
            Zone.PrepareCustomTeamSwitch(playerId, team);
            _zoneSender.Unsubscribe(player.Guid);
            Lobby.EnqueueAction(() => Lobby.SetCustomPlayerTeam(playerId, team));
            _serverDatabase.UpdateScene(playerId, new SceneZone
            {
                GameMode = GameInitiator.GetGameMode(), MatchKey = MatchKey,
                MyTeam = team, IsSpectator = false, IsMapEditor = false, Restart = true
            }, true);
        });
        return true;
    }
}
