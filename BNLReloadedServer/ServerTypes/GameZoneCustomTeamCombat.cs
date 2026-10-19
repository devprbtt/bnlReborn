using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    public bool AllowsCustomTeamCombat => _gameInitiator is CustomGamePlayerGroup custom && custom.GameInfo.TeamKilling;

    private bool IsCustomFriendlyDamageTarget(Unit target, Unit? source, TeamType sourceTeam, InstEffect effect) =>
        AllowsCustomTeamCombat && effect is (InstEffectDamage or InstEffectSplashDamage) &&
        effect.Targeting?.AffectedTeam == RelativeTeamType.Opponent && target.PlayerId.HasValue &&
        target != source && target.Team == sourceTeam && sourceTeam is (TeamType.Team1 or TeamType.Team2);

    public bool HasCustomTeamParticipant(uint playerId) => _gameInitiator is CustomGamePlayerGroup custom && custom.GameInfo.TeamSwitching && !HasEnded &&
        _playerLobbyInfo.ContainsKey(playerId) && _playerIdToUnitId.ContainsKey(playerId) && !_gameInitiator.IsPlayerSpectator(playerId);

    // On the zone queue. Re-entering the zone rebuilds the local team-dependent HUD,
    // spawn selection, controlled unit and team chat using the existing scene flow.
    public void PrepareCustomTeamSwitch(uint playerId, TeamType team)
    {
        PreparePlayerHeroChange(playerId);
        var blocks = new Dictionary<Vector3s, BlockUpdate>();
        foreach (var block in MapBinary.OwnedBlocks.Where(b => b.Value.OwnerPlayerId == playerId &&
                     MapBinary[b.Key].Card.DeviceType == DeviceType.Device).ToList())
            foreach (var update in MapBinary.RemoveBlock(block.Key)) blocks[update.Key] = update.Value;
        if (blocks.Count > 0) DoBlockUpdate(blocks);
        if (_playerLobbyInfo.TryGetValue(playerId, out var old))
        {
            var next = old.Clone(); next.Team = team; _playerLobbyInfo[playerId] = next;
        }
        if (_zoneData.PlayerStats.TryGetValue(playerId, out var stats)) stats.Team = team;
        SendRosterUpdate();
    }
}
