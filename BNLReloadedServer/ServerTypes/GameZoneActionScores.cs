using System.Diagnostics;
using System.Numerics;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private static bool IsScoreDevice(CardUnit? card) => card?.DeviceType is DeviceType.Device or DeviceType.Hero;
    private static void ScoreDeviceBuilt(Unit builder, DeviceType type, Vector3 position, float cost, string? cardId)
    {
        builder.ReportHeroBoostAction("effect_hero_engineer_bfs_build", "Build boost assist", position);
        if (type is DeviceType.Device or DeviceType.Hero && !ExcludedBuildScore(cardId))
            builder.ReportActionScore("Device built", cost, position);
    }
    private static bool ExcludedBuildScore(string? cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return true;
        string[] families = ["block_fast_movement", "block_bounce", "block_thorn_bush",
            "block_glue", "block_caltrops", "block_bear_trap", "block_force_gate",
            "block_ice", "unit_device_generic_landmine", "unit_device_generic_fire_trap"];
        return families.Any(family => cardId == family || cardId.StartsWith(family + "_", StringComparison.Ordinal));
    }
    private readonly ActionScoreRules _actionScoreRules = ActionScoreRules.Load();
    private ActionScoreLedger? _actionScores;
    private readonly Stopwatch _scoreClock = Stopwatch.StartNew();
    private readonly Dictionary<uint, Dictionary<uint, double>> _cubeContributors = [];
    private ActionScoreLedger ActionScores => _actionScores ??= new(_actionScoreRules)
    {
        HeroOf = id => _playerIdToUnitId.TryGetValue(id, out var unitId) && _playerUnits.TryGetValue(unitId, out var unit) ? unit.Key : null
    };
    private bool CanScore => _matchResultsFinalized == 0 && !_zoneData.MatchEnded && !_gameInitiator.IsMapEditor() &&
        _gameInitiator is not WaitingArenaInitiator && _zoneData.Phase.PhaseType is
            ZonePhaseType.Build or ZonePhaseType.Build2 or ZonePhaseType.Assault or ZonePhaseType.Assault2 or ZonePhaseType.SuddenDeath;
    private bool NearScoreObjective(Vector3 position) => _units.Values.Any(u => !u.IsDead &&
        (u.UnitCard?.IsObjective == true || u.UnitCard?.Labels?.Contains(UnitLabel.DropPointBlockbuster) == true) &&
        Vector3.DistanceSquared(position, u.GetMidpoint()) <= _actionScoreRules.ObjectiveRadius * _actionScoreRules.ObjectiveRadius);
    private void AwardAction(Unit player, string reason, float amount, Vector3 position, uint? target)
    {
        if (!CanScore || player.PlayerId is not { } id || !float.IsFinite(amount) || amount <= 0) return;
        // Cube destruction is already the full shared objective reward.
        bool near = reason != "Cube destroyed" && NearScoreObjective(position);
        var rules = _actionScoreRules; var ledger = ActionScores;
        switch (reason)
        {
            case "Mine affected": case "Fire trap affected": case "Glue affected": case "Bush affected":
                if (target == null || !ledger.Ready(id, reason + ":" + target, _scoreClock.Elapsed.TotalSeconds, rules.TrapEffectCooldown)) return;
                break;
            case "Repairs": ledger.Accumulate(id, reason, amount, rules.TonyRepairPerPoint, near); return;
            case "Miracle cure": ledger.Accumulate(id, reason, amount, rules.DocAbilityHealPerPoint, near); return;
            case "Healing": ledger.Accumulate(id, reason, amount, rules.HealPerPoint, near); return;
            case "Device damage": ledger.Accumulate(id, reason, amount, rules.DeviceDamagePerPoint, near); return;
            case "Radar spot": case "Ability spot":
                if (target == null || !ledger.Ready(id, "spot:" + target, _scoreClock.Elapsed.TotalSeconds, rules.SpotCooldown)) return;
                break;
            case "Build boost assist":
            case "Device built":
                var cell = $"{reason}:{MathF.Floor(position.X)}:{MathF.Floor(position.Y)}:{MathF.Floor(position.Z)}";
                if (!ledger.Ready(id, cell, _scoreClock.Elapsed.TotalSeconds, rules.BuildCooldown)) return;
                break;
        }
        if (reason == "Demolition boost assist" && target == null &&
            !ledger.Ready(id, $"demolish:{MathF.Floor(position.X)}:{MathF.Floor(position.Y)}:{MathF.Floor(position.Z)}", _scoreClock.Elapsed.TotalSeconds, rules.HeroEffectCooldown)) return;
        if ((reason == "Demolition boost assist" && target != null) || reason is "Jinx" or "Cage affected" or "EMP affected" or "Avalanche affected" or "Goo affected" or "Ally boosted" or "Reload boost" or "Enemy frightened")
            if (target == null || !ledger.Ready(id, reason + ":" + target, _scoreClock.Elapsed.TotalSeconds, rules.HeroEffectCooldown)) return;
        int points = reason switch {
            "Mine affected" => rules.MineAffected, "Fire trap affected" => rules.FireTrapAffected,
            "Glue affected" => rules.GlueAffected, "Bush affected" => rules.BushAffected,
            "Build boost assist" => rules.TonyBuildAssist, "Demolition boost assist" => rules.TrondsonDemolitionAssist,
            "Lamp pickup" => rules.GenieLampPickup, "Jinx" => rules.GenieJinx, "Cage affected" => rules.VanderCage,
            "EMP affected" => rules.SweetScienceEmp, "Avalanche affected" => rules.YetiAvalanche,
            "Grenade hit" => rules.SargeGrenadeHit, "Goo affected" => rules.RolyGoo, "Mortar destruction" => rules.CogMortarDestroy,
            "Ally boosted" => rules.AstarellaBoost, "Reload boost" => rules.KiraReloadBoost, "Enemy frightened" => rules.KreepyFear,
            "First kill" => rules.FirstKill, "Double kill" => rules.DoubleKill, "Multi kill" => rules.MultiKill,
            "Ultra kill" => rules.UltraKill, "Fantastic" => rules.Fantastic, "Unbelievable" => rules.Unbelievable,
            "Kill" => rules.Kill, "Assist" => rules.Assist, "Headshot" => rules.Headshot,
            "Supply pickup" => rules.Supply, "Blockbuster pickup" => rules.Blockbuster,
            "Cube destroyed" => rules.Cube, "Cube assist" => rules.CubeAssist,
            "Device built" => ledger.BuildValue(amount), "Device destroyed" => ledger.DeviceValue(amount),
            "Radar spot" => rules.Radar, "Ability spot" => rules.AbilitySpot, _ => 0 };
        ledger.Grant(id, reason, points, near);
    }
    private void ScoreDamage(Unit target, float damage, ImpactData impact, Unit? player, Unit? attacker)
    {
        if (!CanScore || damage <= 0 || player?.PlayerId is not { } id || !AreOpponents(target, player)) return;
        if (target.PlayerId != null && target.Team != TeamType.Neutral && player.Team != TeamType.Neutral)
        {
            string origin = Unit.ScoreSourceId(impact.SourceKey);
            if (origin.Length == 0) origin = Unit.ScoreSourceId(attacker?.Key);
            string? trapReason = Unit.ScoreCardFamily(origin, "unit_device_generic_landmine") ? "Mine affected" :
                Unit.ScoreCardFamily(origin, "unit_device_generic_fire_trap") || origin == "block_fire_from_fire_trap" ? "Fire trap affected" :
                Unit.ScoreCardFamily(origin, "block_thorn_bush") ? "Bush affected" : null;
            if (trapReason != null) AwardAction(player, trapReason, 1, target.GetMidpoint(), target.Id);
        }
        if (target.PlayerId != null && Unit.ScoreSourceId(impact.SourceKey).StartsWith("ability_sarge_frag_grenade", StringComparison.Ordinal))
            AwardAction(player, "Grenade hit", 1, target.GetMidpoint(), target.Id);
        if (target.PlayerId != null && (attacker?.Key == new Key("unit_device_roly_toxic_waste") || Unit.ScoreSourceId(impact.SourceKey).Contains("toxic_waste", StringComparison.Ordinal)))
            AwardAction(player, "Goo affected", 1, target.GetMidpoint(), target.Id);
        if (target.UnitCard?.IsObjective == true)
        {
            if (!_cubeContributors.TryGetValue(target.Id, out var contributors)) _cubeContributors[target.Id] = contributors = [];
            contributors[id] = _scoreClock.Elapsed.TotalSeconds;
        }
        if (target.PlayerId != null && DamageAccounting.ResolveImpactCredit(impact.DamageCredit,
            attacker == player, attacker?.UnitCard?.DeviceType) == DamageCreditType.Block)
            AwardAction(player, "Device damage", damage, target.GetMidpoint(), target.Id);
    }
    private void ScoreKill(Unit target, Unit? killer, bool opponent, bool headshot, IEnumerable<uint> assists, KillAccolade? accolade = null)
    {
        if (!CanScore || !target.IsActive) return;
        var position = target.GetMidpoint();
        if (target.PlayerId != null)
        {
            if (killer != null && opponent)
            {
                string reason = accolade switch {
                    KillAccolade.FirstKill => "First kill", KillAccolade.DoubleKill => "Double kill",
                    KillAccolade.MultiKill => "Multi kill", KillAccolade.UltraKill => "Ultra kill",
                    KillAccolade.Fantastic => "Fantastic", KillAccolade.Unbelievable => "Unbelievable", _ => "Kill" };
                AwardAction(killer, reason, 1, position, target.Id);
                if (headshot) AwardAction(killer, "Headshot", 1, position, target.Id);
            }
            foreach (var id in assists.Distinct())
                if (GetPlayerFromPlayerId(id) is { } helper && AreOpponents(target, helper))
                    AwardAction(helper, "Assist", 1, position, target.Id);
        }
        else if (target.UnitCard?.IsObjective == true)
        {
            var rewarded = new HashSet<uint>();
            if (killer?.PlayerId is { } killerId && opponent && target.Team != TeamType.Neutral)
            {
                rewarded.Add(killerId);
                AwardAction(killer, "Cube destroyed", 1, position, target.Id);
                float radiusSquared = _actionScoreRules.ObjectiveRadius * _actionScoreRules.ObjectiveRadius;
                foreach (var teammate in _playerUnits.Values)
                    if (teammate.PlayerId is { } teammateId && teammate.Team == killer.Team &&
                        teammate.IsActive && !teammate.IsDead &&
                        Vector3.DistanceSquared(position, teammate.GetMidpoint()) <= radiusSquared && rewarded.Add(teammateId))
                        AwardAction(teammate, "Cube destroyed", 1, position, target.Id);
            }
            if (_cubeContributors.Remove(target.Id, out var contributors))
                foreach (var pair in contributors)
                    if (!rewarded.Contains(pair.Key) && _scoreClock.Elapsed.TotalSeconds - pair.Value <= _actionScoreRules.CubeAssistWindow &&
                        GetPlayerFromPlayerId(pair.Key) is { } helper && AreOpponents(target, helper))
                        AwardAction(helper, "Cube assist", 1, position, target.Id);
        }
        else if (killer != null && opponent && target.Team != TeamType.Neutral && IsScoreDevice(target.UnitCard))
            AwardAction(killer, "Device destroyed", Math.Max(1, target.UnitCard?.BaseCost ?? 0), position, target.Id);
    }
    private string ScoreSnapshot()
    {
        foreach (var id in _zoneData.PlayerStats.Keys) ActionScores.Ensure(id);
        return ActionScores.Snapshot(true);
    }
    private void PublishActionScores()
    {
        if (_actionScores?.Dirty != true) return;
        _serviceZone.SendUpdateZone(new ZoneUpdate { ActionScoresJson = _actionScores.Snapshot(false) });
        _actionScores.Published();
    }
    private Dictionary<PlayerMatchStatType, int> ScoreResult(uint id, Dictionary<PlayerMatchStatType, int>? stats)
    {
        stats ??= []; stats[(PlayerMatchStatType)12] = ActionScores.Total(id); return stats;
    }
}
