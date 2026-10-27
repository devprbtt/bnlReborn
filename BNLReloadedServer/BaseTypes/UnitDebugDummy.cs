using BNLReloadedServer.Database;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
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
