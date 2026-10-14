using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private Func<DateTimeOffset> _respawnClock = () => DateTimeOffset.Now;

    /// <summary>
    /// Class perks that react to an opposing hero's death. The killer's own on_kill effects have already run.
    /// </summary>
    private void ApplyClassKillPerks(Unit killerPlayer, IEnumerable<uint> assistPlayerIds)
    {
        // Skill II: an assist triggers the assister's on_kill effects (Killer Shield, Killer Ammo, Rapid Reset...)
        // exactly as a kill would. Assists already exclude the killer and are opponents of the victim.
        foreach (var playerId in assistPlayerIds.Distinct())
        {
            if (GetPlayerFromPlayerId(playerId) is not { IsDead: false } assister ||
                assister.ActiveClassPerk != ClassPerkCatalogue.Skill2Id)
                continue;
            var assistImpact = assister.CreateImpactData();
            foreach (var effect in assister.ActiveEffects.GetEffectsOfType<ConstEffectOnKill>().Select(kil => kil.Effect).OfType<InstEffect>())
                ApplyInstEffect(assister.GetSelfSource(assistImpact), [assister], effect, assistImpact);
        }

        // Skill III: the kill removes a share of every active allied respawn timer, the killer's included.
        if (killerPlayer.ActiveClassPerk == ClassPerkCatalogue.Skill3Id)
            ReduceAlliedRespawnTimers(killerPlayer,
                ClassPerkCatalogue.Balance(ClassPerkCatalogue.Skill3Id).RespawnReductionPercent / 100f);
    }

    private void ReduceAlliedRespawnTimers(Unit killerPlayer, float share)
    {
        if (share <= 0) return;
        var now = _respawnClock();
        foreach (var unit in _playerUnits.Values)
        {
            if (unit.PlayerId is not { } playerId || !unit.IsDead || unit.RespawnTime is not { } respawnAt || respawnAt <= now)
                continue;
            // Free-for-all players have no teammates; only the killer's own timer qualifies there.
            if (unit.Id != killerPlayer.Id &&
                (killerPlayer.FreeForAllPlayer || unit.FreeForAllPlayer || unit.Team != killerPlayer.Team))
                continue;
            unit.RespawnTime = now + (respawnAt - now) * (1 - share);
            _zoneData.UpdateSpawnTime(playerId, (ulong)unit.RespawnTime.Value.ToUnixTimeMilliseconds());
        }
    }
}
