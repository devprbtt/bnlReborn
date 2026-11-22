using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private readonly Dictionary<uint, long> _skillRecoveryFrom = new();
    private Func<long> _skillRecoveryClock = () => Environment.TickCount64;

    private static bool HasSkillRecovery(Unit unit) => unit.PlayerId.HasValue &&
        unit.ActiveClassPerk == ClassPerkCatalogue.SkillId;

    // Shared event entry point keeps both class perks on the same accepted attack/damage rules.
    private void MarkClassPerkCombat(Unit unit)
    {
        MarkRallyCombat(unit);
        if (HasSkillRecovery(unit)) _skillRecoveryFrom[unit.PlayerId!.Value] = _skillRecoveryClock() + ClassPerkCatalogue.QuietMilliseconds(ClassPerkCatalogue.SkillId);
    }

    private void TickSkillRecovery()
    {
        var now = _skillRecoveryClock();
        foreach (var unit in _playerUnits.Values)
        {
            if (!HasSkillRecovery(unit)) continue;
            var id = unit.PlayerId!.Value;
            if (unit.IsDead || !unit.IsActive || unit.IsDropped || unit.CurrentChannelData != null ||
                SkillRecoveryThreatened(unit))
            {
                _skillRecoveryFrom[id] = now + ClassPerkCatalogue.QuietMilliseconds(ClassPerkCatalogue.SkillId);
                continue;
            }
            if (!_skillRecoveryFrom.TryGetValue(id, out var from))
            {
                _skillRecoveryFrom[id] = now + ClassPerkCatalogue.QuietMilliseconds(ClassPerkCatalogue.SkillId);
                continue;
            }
            // Accrue only time after the quiet period. Small updates avoid per-frame heal packets;
            // capped elapsed time prevents a pause from banking a large burst of recovery.
            var elapsed = now - from;
            if (elapsed < 250) continue;
            _skillRecoveryFrom[id] = now;
            if (unit.UnitCard?.Health?.Health is not { } health) continue;
            var gained = unit.AddHealth(unit.UnitMaxHealth(health.MaxHealth) * (ClassPerkCatalogue.Balance(ClassPerkCatalogue.SkillId).RegenMaxHealthPercentPerSecond / 100f) * Math.Min(elapsed, 1000) / 1000f);
            if (gained <= 0) continue;
            unit.SendHealAttribution(unit, gained, unit.Key);
            // The timed CDB effect only carries the icon; each heal refreshes it, so it lapses once healing stops.
            if (ClassPerkCatalogue.Balance(ClassPerkCatalogue.SkillId).RegenEffect is { } effectId &&
                Databases.Catalogue.GetCard<CardEffect>(effectId) != null)
                unit.AddEffect(new ConstEffectInfo(Catalogue.Key(effectId)), unit.Team, unit.GetSelfSource(unit.CreateImpactData()));
        }
    }

    private bool SkillRecoveryThreatened(Unit unit)
    {
        // VisionMark is the authoritative reveal used by radar and spotting
        // abilities. Recheck continuously so healing cannot accrue while marked.
        if (unit.GetBuff(BuffType.VisionMark) > 0) return true;
        var radius = ClassPerkCatalogue.Balance(ClassPerkCatalogue.SkillId).EnemyProximityRadius;
        return radius > 0 && _playerUnits.Values.Any(enemy => enemy.Id != unit.Id &&
            enemy.PlayerId != null && enemy.IsActive && !enemy.IsDead && !enemy.IsDropped &&
            AreOpponents(unit, enemy) && Vector3.DistanceSquared(unit.GetMidpoint(), enemy.GetMidpoint()) <= radius * radius);
    }
}
