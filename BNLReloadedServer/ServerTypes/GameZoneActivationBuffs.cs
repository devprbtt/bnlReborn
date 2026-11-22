using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private const string KiraThrillsCast = "effect_hero_kira_jpop_star_cast_thrills_for_days";
    private const string KiraThrillsOnKill = "effect_hero_kira_jpop_star_self_thrills_for_days_on_kill";
    private static readonly Dictionary<Key, Key> ActivationBuffs = new()
    {
        [new("ability_engineer_bfs")] = new(Unit.TonyBuildCastEffectId),
        [new("ability_engineer_bfs_wider")] = new(Unit.TonyBuildCastEffectId),
        [new("ability_kira_jpop_star")] = new("effect_hero_kira_jpop_star_cast"),
        [new("ability_kira_jpop_star_idoru")] = new("effect_hero_kira_jpop_star_cast_idoru"),
        [new("ability_kira_jpop_star_thrills_for_days")] = new(KiraThrillsCast),
        [new("ability_trondson_heigh_ho")] = new("effect_hero_trondson_heigh_ho_cast"),
        [new("ability_trondson_heigh_ho_singin_in_the_mine")] = new("effect_hero_trondson_heigh_ho_cast_singin_in_the_mine")
    };
    private sealed record ThrillsRecipients(Unit Caster, uint CasterId, TeamType Team, EffectSource Source,
        (Unit Unit, uint Id)[] Recipients, ulong ExpiresAt);
    private readonly Dictionary<uint, ThrillsRecipients> _thrillsRecipients = [];
    private static ulong ActivationNow => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private bool TryApplyActivationBuff(Unit player, CardAbility ability, AbilityBehaviorCast behavior)
    {
        // A known ability and new catalogue marker are both required: old CDBs
        // keep their original route until the reviewed catalogue update is applied.
        if (!ActivationBuffs.TryGetValue(ability.Key, out var marker) ||
            behavior.Application is not AbilityApplicationSelf ||
            behavior.HitEffect is not InstEffectBunch { Instant: { } instant } ||
            !instant.OfType<InstEffectAllUnitsBunch>().Any(e => e.Constant?.Contains(marker) == true)) return false;
        var impact = player.CreateImpactData(sourceKey: ability.Key);
        impact.TonyBuildRewardRecipients = [];
        if (marker == new Key(KiraThrillsCast))
        {
            impact.ActivationBuffMarker = marker;
            impact.ActivationBuffRecipients = [];
        }
        var source = player.GetSelfSource(impact);
        ApplyInstEffect(source, [player], behavior.HitEffect, impact);
        if (marker == new Key(KiraThrillsCast) && player.PlayerId is { } id)
        {
            var end = player.ActiveEffects.FirstOrDefault(e => e.Key == marker)?.ExpirationTime;
            if (end is { } expires)
                _thrillsRecipients[id] = new(player, player.Id, player.Team, source,
                    impact.ActivationBuffRecipients!.Select(u => (u, u.Id)).ToArray(), expires);
        }
        return true;
    }

    private void PruneThrillsRecipients()
    {
        foreach (var (id, cast) in _thrillsRecipients.ToArray())
            if (cast.ExpiresAt <= ActivationNow || cast.Caster.Id != cast.CasterId || cast.Caster.IsDead ||
                cast.Caster.IsDropped || !cast.Caster.IsActive || cast.Caster.Team != cast.Team ||
                !cast.Caster.ActiveEffects.Any(e => e.Key == new Key(KiraThrillsOnKill) && !e.IsExpired))
                _thrillsRecipients.Remove(id);
    }

    private bool RefreshThrillsRecipients(Unit? caster)
    {
        PruneThrillsRecipients();
        if (caster?.PlayerId is not { } id || !_thrillsRecipients.TryGetValue(id, out var cast) ||
            !ReferenceEquals(caster, cast.Caster)) return false;
        var marker = new Key(KiraThrillsCast);
        if (marker.GetCard<CardEffect>()?.Duration is not { } duration) return false;
        var end = ActivationNow + (ulong)(duration * 1000);
        foreach (var (unit, originalId) in cast.Recipients)
            if (unit.Id == originalId && !unit.IsDead && unit.IsActive && !unit.IsDropped && unit.Team == cast.Team &&
                unit.ActiveEffects.Any(e => e.Key == marker && !e.IsExpired))
                unit.AddEffects([new ConstEffectInfo(marker, (ulong?)end)], cast.Team, cast.Source);
        _thrillsRecipients[id] = cast with { ExpiresAt = end };
        return true;
    }
}
