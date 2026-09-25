namespace BNLReloadedServer.BaseTypes;

public enum ConstEffectType
{
    Buff = 1,
    Immunity = 2,
    Aura = 3,
    Self = 4,
    Team = 5,
    OnSprint = 6,
    OnDeath = 7,
    OnFall = 8,
    OnKill = 9,
    OnBuilding = 10,
    OnLowHealth = 11,
    OnLeading = 12,
    OnMatchContext = 13,
    OnDamageTaken = 14,
    OnGearSwitch = 15,
    Pull = 16,
    OnReload = 17,
    Interval = 18,
    OnNearbyBlock = 19,
    // Reborn, server-only: the client has no such type, so ConstEffectOnHit reaches it as an empty buff.
    OnHit = 20
}
