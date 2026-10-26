using System.Diagnostics;
using System.Numerics;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private static bool IsScoreDevice(CardUnit? card) => card?.DeviceType is DeviceType.Device or DeviceType.Hero;
    private static void ScoreDeviceBuilt(Unit builder, DeviceType type, Vector3 position, float cost, string? cardId)
    {
        // Placement itself never earns action score (including construction-boost assists).
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
            case "Device built": case "Build boost assist": return;
            case "Bomb device affected":
                var cell = $"bomb:{MathF.Floor(position.X)}:{MathF.Floor(position.Y)}:{MathF.Floor(position.Z)}";
                if (!ledger.Ready(id, cell, _scoreClock.Elapsed.TotalSeconds, rules.TrapEffectCooldown)) return;
                break;
            case "Radar device":
                if (target == null || !ledger.Once(id, "radar-device:" + target)) return;
                break;
            case "Portal used":
                if (target == null || !ledger.Ready(id, reason + ":" + target, _scoreClock.Elapsed.TotalSeconds, rules.PortalCooldown)) return;
                break;
            case "Ally spawned": case "Ammo supplied":
                if (target == null || !ledger.Ready(id, reason + ":" + target, _scoreClock.Elapsed.TotalSeconds,
                    reason == "Ally spawned" ? rules.SpawnCooldown : rules.AmmoCooldown)) return;
                break;
            case "Turret damage": ledger.AccumulateChunks(id, reason, amount, rules.TurretDamageChunk, rules.TurretDamagePoints, near); return;
            case "Tiki hit": case "Mortar hit":
                if (target == null || !ledger.Ready(id, reason + ":" + target, _scoreClock.Elapsed.TotalSeconds, rules.DeviceHitCooldown)) return;
                break;
            case "Bomb affected": case "C4 hit": case "Bear trap affected": case "Caltrops affected":

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
            "Kill" => rules.Kill, "Assist" => rules.Assist, "Key assist" => rules.KeyAssist, "Headshot" => rules.Headshot,
            "Supply pickup" => rules.Supply, "Blockbuster pickup" => rules.Blockbuster,
            "Cube destroyed" => rules.Cube, "Cube assist" => rules.CubeAssist,
            "Device destroyed" => ledger.DeviceValue(amount),
            "Radar device" => rules.RadarDevice, "Ally spawned" => rules.SpawnUse, "Portal used" => rules.PortalUse,
            "Ammo supplied" => rules.AmmoSupply, "Bomb affected" or "Bomb device affected" => rules.BombAffected, "C4 hit" => rules.SargeC4Hit,
            "C4 destruction" => rules.SargeC4Destroy, "Mortar hit" => rules.MortarHit, "Tiki hit" => rules.TikiHit,
            "Bear trap affected" => rules.BearTrapAffected, "Caltrops affected" => rules.CaltropsAffected,
            "Radar spot" => rules.Radar, "Ability spot" => rules.AbilitySpot, _ => 0 };
        ledger.Grant(id, reason, points, near);
    }
    private void ScoreDamage(Unit target, float damage, ImpactData impact, Unit? player, Unit? attacker)
    {
        if (!CanScore || damage <= 0 || !float.IsFinite(damage) || !target.IsActive || target.Team == TeamType.Neutral || player?.PlayerId is not { } id || player.Team == TeamType.Neutral || !AreOpponents(target, player)) return;
        string origin = Unit.ScoreSourceId(impact.SourceKey);
        if (DeviceScoreReason(origin) == null && attacker != player) origin = Unit.ScoreSourceId(attacker?.Key);
        string? deviceReason = DeviceScoreReason(origin);
        if (deviceReason != null && (target.PlayerId != null ||
            deviceReason == "Bomb affected" && IsScoreDevice(target.UnitCard) && target.UnitCard?.IsObjective != true && !ExcludedDestroyScore(target.UnitCard?.Id)))
            AwardAction(player, deviceReason, deviceReason == "Turret damage" ? damage : 1, target.GetMidpoint(), target.Id);
        if (target.PlayerId != null && Unit.ScoreSourceId(impact.SourceKey).StartsWith("ability_sarge_frag_grenade", StringComparison.Ordinal))
            AwardAction(player, "Grenade hit", 1, target.GetMidpoint(), target.Id);
        if (target.PlayerId != null && (attacker?.Key == new Key("unit_device_roly_toxic_waste") || Unit.ScoreSourceId(impact.SourceKey).Contains("toxic_waste", StringComparison.Ordinal)))
            AwardAction(player, "Goo affected", 1, target.GetMidpoint(), target.Id);
        if (target.UnitCard?.IsObjective == true)
        {
            if (!_cubeContributors.TryGetValue(target.Id, out var contributors)) _cubeContributors[target.Id] = contributors = [];
            contributors[id] = _scoreClock.Elapsed.TotalSeconds;
        }
        if (deviceReason == null && target.PlayerId != null && DamageAccounting.ResolveImpactCredit(impact.DamageCredit,
            attacker == player, attacker?.UnitCard?.DeviceType) == DamageCreditType.Block)
            AwardAction(player, "Device damage", damage, target.GetMidpoint(), target.Id);
    }
    // Ice (Yeti/Abe block_ice and variants) gives no destruction score: it is cheap, temporary and mass-spawned.
    public static bool ExcludedDestroyScore(string? cardId) => cardId != null && Unit.ScoreCardFamily(cardId, "block_ice");
    private void ScoreKill(Unit target, Unit? killer, bool opponent, bool headshot, IEnumerable<uint> assists, KillAccolade? accolade = null,
        IReadOnlySet<uint>? topDamagers = null)
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
            // The assister who dealt the most damage (TopDamagers, the Accomplice rule; ties share) earns a key assist instead.
            foreach (var id in assists.Distinct())
                if (GetPlayerFromPlayerId(id) is { } helper && AreOpponents(target, helper))
                    AwardAction(helper, topDamagers?.Contains(id) == true ? "Key assist" : "Assist", 1, position, target.Id);
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
        else if (killer != null && opponent && target.Team != TeamType.Neutral && IsScoreDevice(target.UnitCard) && !ExcludedDestroyScore(target.UnitCard?.Id))
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
