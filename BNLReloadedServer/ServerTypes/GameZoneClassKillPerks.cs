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
    private void ApplyClassKillPerks(Unit killerPlayer, IEnumerable<uint> assistPlayerIds, HashSet<uint> topDamagerIds)
    {
        // Skill II: an assist triggers the assister's on_kill effects (Killer Shield, Killer Ammo, Rapid Reset...)
        // exactly as a kill would, but only when the assister dealt the most damage to the victim (ties count).
        // Assists already exclude the killer and are opponents of the victim.
        foreach (var playerId in assistPlayerIds.Distinct())
        {
            if (!topDamagerIds.Contains(playerId) ||
                GetPlayerFromPlayerId(playerId) is not { IsDead: false } assister ||
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

    /// <summary>
    /// Players who dealt the victim the most enemy damage within the assist window, killing blow included
    /// (damage is recorded before the death is processed). Several players share the top on a tie.
    /// </summary>
    private HashSet<uint> TopDamagers(Unit victim)
    {
        var now = DateTimeOffset.Now;
        var damage = victim.RecentDamageTaken
            .Where(d => d.Key.PlayerId != null && victim.RecentDamagers.GetValueOrDefault(d.Key) > now && AreOpponents(victim, d.Key))
            .ToList();
        if (damage.Count == 0) return [];
        var most = damage.Max(d => d.Value);
        return damage.Where(d => d.Value >= most - 0.001f).Select(d => d.Key.PlayerId!.Value).ToHashSet();
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
