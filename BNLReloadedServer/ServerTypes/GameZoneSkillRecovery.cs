using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private readonly Dictionary<uint, long> _skillRecoveryFrom = new();
    private Func<long> _skillRecoveryClock = () => Environment.TickCount64;

    private static bool HasSkillRecovery(Unit unit) => unit.PlayerId.HasValue &&
        unit.UnitCard?.Data is UnitDataPlayer data &&
        data.Class.GetCard<CardHeroClass>()?.Type == HeroClassType.Skills;

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
            if (unit.IsDead || !unit.IsActive || unit.IsDropped || unit.CurrentChannelData != null)
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
            if (gained > 0) unit.SendHealAttribution(unit, gained, unit.Key);
        }
    }
}
