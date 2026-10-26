using System.Numerics;
using BNLReloadedServer.ServerTypes;

namespace BNLReloadedServer.BaseTypes;

public partial class Unit
{
    private readonly RepairDamageCredit repairDamageCredit = new();
    private void RecordRepairDamage(float damage, TeamType? sourceTeam)
    {
        if (PlayerId is not null || UnitCard?.DeviceType is DeviceType.Device or DeviceType.Hero)
            repairDamageCredit.Record(damage, Team, sourceTeam);
    }

    // Score-only provenance: collecting loot must not change its gameplay ownership.
    public uint? ScoreLampOwner { get; set; }
    public static string ScoreSourceId(Key? key) => key?.GetCard<Card>()?.Id ?? "";
    public static bool ScoreCardFamily(string id, string family) =>
        id == family || id.StartsWith(family + "_", StringComparison.Ordinal);
    private Unit? ScoreSourcePlayer(EffectSource? source)
    {
        var owner = source?.Impact?.CasterPlayerId ?? (source is UnitSource u ? u.Unit.OwnerPlayerId : null);
        return owner is { } id ? _updater.GetPlayerFromPlayerId(id) : null;
    }
    public void ReportHeroBoostAction(string effectPrefix, string reason, Vector3 position, uint? target = null)
    {
        var credited = new HashSet<uint>();
        foreach (var effect in ActiveEffects)
        {
            if (effect.Card?.Id?.StartsWith(effectPrefix, StringComparison.Ordinal) != true ||
                effect.Card.Effect is not ConstEffectBuff { Buffs.Count: > 0 } ||
                !_effectSources.TryGetValue(effect.Key, out var sources)) continue;
            foreach (var source in sources)
                if (ScoreSourcePlayer(source) is { PlayerId: { } id } player && id != (PlayerId ?? OwnerPlayerId) &&
                    Team == player.Team && Team != TeamType.Neutral && credited.Add(id))
                    player.ReportActionScore(reason, 1, position, target);
        }
    }
    private void ReportHeroEffectScores(IEnumerable<ConstEffectInfo> effects, EffectSource? source)
    {
        if (ScoreSourcePlayer(source) is not { PlayerId: { } owner } player || owner == PlayerId || Team == TeamType.Neutral) return;
        bool enemy = player.Team != TeamType.Neutral && DamageAccounting.IsEnemyPlayerDamage(Team, player.Team);
        bool device = UnitCard?.DeviceType is DeviceType.Device or DeviceType.Hero;
        string origin = ScoreSourceId(source?.Impact?.SourceKey);
        var reasons = new HashSet<string>();
        foreach (var effect in effects)
        {
            string id = effect.Card?.Id ?? "";
            string? reason = null;
            if (enemy)
            {
                if ((PlayerId != null || device) && id.StartsWith("effect_hero_djinn_jinx_", StringComparison.Ordinal)) reason = "Jinx";
                else if (device && id.StartsWith("effect_hero_boxer_power_surge", StringComparison.Ordinal) && !id.Contains("immunity")) reason = "EMP affected";
                else if (PlayerId != null)
                {
                    if (id == "effect_status_rooted_bear_trap" && ScoreCardFamily(origin, "block_bear_trap")) reason = "Bear trap affected";
                    else if (id == "effect_status_bleed_caltrops" && ScoreCardFamily(origin, "block_caltrops")) reason = "Caltrops affected";
                    else if (id == "effect_status_slow_glue" && ScoreCardFamily(origin, "block_glue")) reason = "Glue affected";
                    else if (id == "effect_status_poison_thorn_bush" && ScoreCardFamily(origin, "block_thorn_bush")) reason = "Bush affected";
                    else if (id == "effect_status_burning_fire_trap" && (ScoreCardFamily(origin, "unit_device_generic_fire_trap") || origin == "block_fire_from_fire_trap")) reason = "Fire trap affected";
                    else if (id == "effect_hero_magnus_lightning_cage_hitting") reason = "Cage affected";
                    else if (id is "effect_abe_avalanche_area_slow" or "effect_hero_abe_avalanche_slow") reason = "Avalanche affected";
                    else if (id.Contains("toxic_waste", StringComparison.Ordinal) || id == "effect_status_poison_device_toxicwaste") reason = "Goo affected";
                    else if (id.StartsWith("effect_hero_kreepy_killer_klown_debuff", StringComparison.Ordinal)) reason = "Enemy frightened";
                }
            }
            else if (PlayerId != null && Team == player.Team)
            {
                if (id.StartsWith("effect_hero_kira_jpop_star_reload_buff", StringComparison.Ordinal)) reason = "Reload boost";
                else if (origin.StartsWith("ability_astro_boost", StringComparison.Ordinal) && id.StartsWith("effect_status_boost", StringComparison.Ordinal)) reason = "Ally boosted";
            }
            if (reason != null && reasons.Add(reason)) player.ReportActionScore(reason, 1, GetMidpoint(), Id);
        }
    }
}
