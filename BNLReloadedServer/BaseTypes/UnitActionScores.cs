using System.Numerics;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    public void ReportActionScore(string reason, float amount, Vector3 position, uint? target = null) =>
        _updater.OnActionScore?.Invoke(this, reason, amount, position, target);
    public void ReportDeviceBlockImpact(CardBlock card, TeamType targetTeam, Vector3s position, bool destroyed, Key? sourceKey)
    {
        if (!card.Destructible || card.DeviceType is not (DeviceType.Device or DeviceType.Hero) ||
            GameZone.ExcludedDestroyScore(card.Id) || targetTeam == TeamType.Neutral ||
            (PlayerId is not null ? this : OwnerPlayerId is { } owner ? _updater.GetPlayerFromPlayerId(owner) : null) is not { } player ||
            player.Team == TeamType.Neutral || player.Team == targetTeam) return;
        string origin = ScoreSourceId(sourceKey);
        string? reason = GameZone.DeviceScoreReason(origin) ?? GameZone.DeviceScoreReason(ScoreSourceId(Key));
        if (reason == "Bomb affected") player.ReportActionScore("Bomb device affected", 1, position.ToVector3());
        if (!destroyed) return;
        player.ReportActionScore("Device destroyed", Math.Max(1, card.BaseCost ?? 0), position.ToVector3());
        player.ReportHeroBoostAction("effect_hero_trondson_heigh_ho_buff", "Demolition boost assist", position.ToVector3());
        if (reason is "C4 hit" or "Mortar hit")
            player.ReportActionScore(reason == "C4 hit" ? "C4 destruction" : "Mortar destruction", 1, position.ToVector3());
    }

    private void ReportAmmoSupply(EffectSource? source, float restored)
    {
        if (restored <= 0 || !float.IsFinite(restored) || PlayerId is not { } recipient || IsDead || !IsActive ||
            source is not UnitSource { Unit: var station } || station.PlayerId != null ||
            !ScoreCardFamily(ScoreSourceId(station.Key), "unit_device_generic_ammo_resupply") ||
            ScoreSourcePlayer(source) is not { PlayerId: { } owner } builder || owner == recipient ||
            Team == TeamType.Neutral || builder.Team != Team || station.Team != Team) return;
        builder.ReportActionScore("Ammo supplied", restored, GetMidpoint(), recipient);
    }

    public void ReportScoreSpots()
    {
        if (IsDead || !IsActive || Team == TeamType.Neutral ||
            PlayerId == null && (UnitCard?.DeviceType is not (DeviceType.Device or DeviceType.Hero) || UnitCard.IsObjective)) return;
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
                    ScoreCardFamily(ScoreSourceId(unit.Unit.Key), "unit_device_generic_radar");
                if (PlayerId == null && !radar) continue;
                player.ReportActionScore(radar ? (PlayerId == null ? "Radar device" : "Radar spot") : "Ability spot", 1, GetMidpoint(), Id);
            }
        }
    }
}
