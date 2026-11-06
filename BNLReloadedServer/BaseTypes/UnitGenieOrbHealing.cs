using System.Diagnostics;
using BNLReloadedServer.Database;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    private static readonly Key GenieHealingAura = new("effect_hero_djinn_heavy_orb_heal_aura");
    // Recipient-owned: different owners and normal/Speed Ball projectiles share this cadence.
    private uint? _lastGenieHealingOrb;
    private long _lastGenieHealingTick;

    private void ApplyAuraInterval(Key effectKey, float interval, IEnumerable<Unit> targets,
        InstEffect effect, long timestamp)
    {
        if (effectKey == GenieHealingAura && effect is InstEffectHeal)
        {
            // Station regen owns this healing group while present. Keep orb cadence
            // untouched while suppressed, so leaving the station can resume healing.
            targets = targets.Distinct().Where(target => !target.IsDead && !target.HasHealthStationRegen() &&
                target.AcceptGenieOrbHeal(Id, interval, timestamp)).ToArray();
        }
        var impact = CreateImpactData().AsPeriodicEffect();
        _updater.OnApplyInstEffect(GetSelfSource(impact), targets, effect, impact);
    }

    private bool HasHealthStationRegen() =>
        ActiveEffects.Any(effect => effect.Key == CatalogueHelper.GenericRegenKey) &&
        _effectSources.TryGetValue(CatalogueHelper.GenericRegenKey, out var sources) &&
        sources.Any(source => source is UnitSource { Unit.UnitCard.Labels: { } labels } &&
                              labels.Contains(UnitLabel.HealthSupply));

    private bool AcceptGenieOrbHeal(uint orbId, float interval, long timestamp)
    {
        // Keep the chosen orb's original timer cadence (including normal timer jitter).
        // A replacement orb may take over after one interval, never add an extra tick within it.
        if (_lastGenieHealingOrb.HasValue && _lastGenieHealingOrb != orbId &&
            Stopwatch.GetElapsedTime(_lastGenieHealingTick, timestamp) < TimeSpan.FromSeconds(interval))
            return false;

        _lastGenieHealingOrb = orbId;
        _lastGenieHealingTick = timestamp;
        return true;
    }
}
