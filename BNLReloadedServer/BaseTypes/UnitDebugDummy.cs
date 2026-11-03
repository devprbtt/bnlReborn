using BNLReloadedServer.Database;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    public bool IsTestDummy => Key == DebugDummyRegistration.Key;

    /// <summary>
    /// A practice target that stands in for a hero: the spawned test dummy, or a map-editor test dummy device with
    /// player health (unit_device_generic_test_dummy_player). Player heals and on-hit perks treat it as a player.
    /// </summary>
    public bool IsHeroPracticeTarget => PlayerId == null && (IsTestDummy ||
        (UnitCard?.Id?.StartsWith("unit_device_generic_test_dummy", StringComparison.Ordinal) == true &&
         UnitCard.Health?.Health?.HealthType == HealthType.Player));

    private int? _debugDummyMaxHealth;

    public void SetDebugDummyHealth(int health)
    {
        if (Key != DebugDummyRegistration.Key || health is < 1 or > DebugDummyRegistration.MaxHealth)
            throw new ArgumentOutOfRangeException(nameof(health));
        _debugDummyMaxHealth = health;
        UpdateData(new UnitUpdate { Health = health, Shield = 0, Forcefield = 0,
            Buffs = ExtractBuffs(ActiveEffects.Select(e => e.Key)) });
    }
}
