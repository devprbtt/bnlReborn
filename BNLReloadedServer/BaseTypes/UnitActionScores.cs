using System.Numerics;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    public void ReportActionScore(string reason, float amount, Vector3 position, uint? target = null) =>
        _updater.OnActionScore?.Invoke(this, reason, amount, position, target);
    private void ReportScoreSpots()
    {
        if (PlayerId == null || IsDead) return;
        foreach (var effect in ActiveEffects)
        {
            if (effect.Card?.Effect is not ConstEffectBuff { Buffs: { } buffs } ||
                !buffs.TryGetValue(BuffType.VisionMark, out var mark) || mark <= 0 ||
                !_effectSources.TryGetValue(effect.Key, out var sources)) continue;
            foreach (var source in sources)
            {
                var id = source.Impact?.CasterPlayerId;
                if (id == null || _updater.GetPlayerFromPlayerId(id.Value) is not { } player ||
                    !DamageAccounting.IsEnemyPlayerDamage(Team, player.Team)) continue;
                bool radar = source is UnitSource unit && unit.Unit.PlayerId == null &&
                    unit.Unit.UnitCard?.DeviceType is DeviceType.Device or DeviceType.Block;
                player.ReportActionScore(radar ? "Radar spot" : "Ability spot", 1, GetMidpoint(), Id);
            }
        }
    }
}
