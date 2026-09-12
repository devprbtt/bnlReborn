using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private static UnitUpdate ConquestSpawnUpdate(Unit unit)
    {
        // OnRespawn runs before Unit.Respawn restores health/ammo. Its buffered
        // packet can arrive after that unbuffered reset, so send only mode effects.
        var snapshot = unit.GetUpdateData();
        return new UnitUpdate { Effects = snapshot.Effects, Buffs = snapshot.Buffs };
    }
    private SkyBridgeConquest? _conquest;
    private ulong _conquestBuffDeadline;
    private static readonly Key[] ConquestBuffs = [new("effect_blockbuster_lite_buff"),
        new("effect_blockbuster_classic_buff"), new("effect_blockbuster_uber_buff"), new("effect_status_shielded_bb")];

    private void InitializeConquest()
    {
        if (_zoneData.MapKey != new Key(SkyBridgeConquest.MapId)) return;
        _conquest = new SkyBridgeConquest(_zoneData.MapData.Units
            .Where(u => u.UnitKey.GetCard<CardUnit>()?.Labels?.Contains(UnitLabel.DropPointBlockbuster) == true)
            .Select(u => u.Position));
        foreach (var key in ConquestBuffs.Concat(CatalogueHelper.ObjectiveShieldKeys))
            if (key.GetCard<CardEffect>() == null) throw new InvalidOperationException($"Missing Conquest effect {key}");
    }

    private List<SupplySequenceItem>? ConquestSupplies(List<SupplySequenceItem>? sequence) =>
        _conquest == null ? sequence : sequence?.Where(s =>
            s.DropPointLabel != UnitLabel.DropPointBlockbuster &&
            s.SupplyUnitKey.GetCard<CardUnit>()?.Labels?.Contains(UnitLabel.SupplyBlockbuster) != true).ToList();

    private void TickConquest(float seconds, bool publish, uint? dyingUnitId = null)
    {
        if (_conquest == null || HasEnded) return;
        var startedAttack = false;
        if (_zoneData.Phase.PhaseType is ZonePhaseType.Assault or ZonePhaseType.Assault2 or ZonePhaseType.SuddenDeath)
        {
            var wasAttacking = _conquest.Attacking;
            _conquest.Step(seconds, _playerUnits.Values.Where(u => u.Id != dyingUnitId && !u.IsDead && u.PlayerId.HasValue &&
                _playerLobbyInfo.TryGetValue(u.PlayerId.Value, out var player) && player.Team == u.Team)
                .Select(u => new SkyBridgeConquest.Player(u.PlayerId!.Value, u.Team, u.Transform.Position)).ToArray());
            if (_conquest.Attacking && !wasAttacking)
            {
                startedAttack = true;
                _conquestBuffDeadline = (ulong)DateTimeOffset.UtcNow.AddSeconds(SkyBridgeConquest.AttackSeconds).ToUnixTimeMilliseconds();
            }
        }
        foreach (var unit in _units.Values.ToArray()) ApplyConquestUnit(unit);
        // The periodic snapshot cadence is sufficient for timers, but the attack
        // transition must follow the boost application in this same server tick so
        // clients can play the announcer and show the phase notice immediately.
        if (ConquestSnapshotDue(publish, startedAttack))
            _serviceZone.SendUpdateZone(new ZoneUpdate { ConquestStateJson = ConquestSnapshot() });
    }

    private static bool ConquestSnapshotDue(bool periodicUpdate, bool startedAttack) =>
        periodicUpdate || startedAttack;

    private void ApplyConquestUnit(Unit unit)
    {
        if (_conquest == null) return;
        if (unit.UnitCard?.IsObjective == true)
        {
            unit.ConquestDamageBlocked = () => ConquestObjectiveShielded(unit.Team, unit.UnitCard.Labels ?? []);
            if (ConquestObjectiveShielded(unit.Team, unit.UnitCard.Labels ?? []))
            {
                if (!unit.ActiveEffects.Any(e => CatalogueHelper.ObjectiveShieldKeys.Contains(e.Key)))
                    unit.AddEffect(new ConstEffectInfo(CatalogueHelper.ObjectiveShieldKeys[0], (ulong?)null), unit.Team, null);
            }
            else unit.ActiveEffects = unit.ActiveEffects.RemoveAll(e => CatalogueHelper.ObjectiveShieldKeys.Contains(e.Key));
        }
        if (!unit.PlayerId.HasValue) return;
        var active = _conquest.Attacking && unit.Team == _conquest.Attacker;
        var buff = new Key($"effect_blockbuster_{_conquest.Tier}_buff");
        unit.ActiveEffects = unit.ActiveEffects.RemoveAll(e => ConquestBuffs.Contains(e.Key) &&
            (!active || (e.Key != buff && !(e.Key == ConquestBuffs[3] && _conquest.Round > 0))));
        if (!active || unit.IsDead) return;
        foreach (var key in _conquest.Round == 0 ? new[] { buff } : new[] { buff, ConquestBuffs[3] })
            if (!unit.ActiveEffects.Any(e => e.Key == key))
                unit.AddEffect(new ConstEffectInfo(key, (ulong?)_conquestBuffDeadline), unit.Team, null);
    }

    private bool ConquestObjectiveShielded(TeamType team, IEnumerable<UnitLabel> labels)
    {
        UnitLabel? currentObjective = _objectiveConquest[(int)team].TryPeek(out var current) ? current : null;
        return _conquest?.ObjectiveShielded(team, labels, currentObjective) ?? false;
    }

    private string? ConquestSnapshot() => _conquest == null ? null : JsonSerializer.Serialize(new
    {
        version = 2, round = _conquest.Round, attacker = (int)_conquest.Attacker, attackRemaining = _conquest.AttackRemaining,
        target = _conquest.Target, team1 = _conquest.Scores[1], team2 = _conquest.Scores[2],
        tier = _conquest.Tier, halfWidth = SkyBridgeConquest.HalfWidth,
        depthBelow = SkyBridgeConquest.DepthBelow, heightAbove = SkyBridgeConquest.HeightAbove,
        rate1 = _conquest.ScoreRate(TeamType.Team1), rate2 = _conquest.ScoreRate(TeamType.Team2),
        capturing = _zoneData.Phase.PhaseType is ZonePhaseType.Assault or ZonePhaseType.Assault2 or ZonePhaseType.SuddenDeath,
        zones = _conquest.Zones.Select(z => new { x = z.Center.X, y = z.Center.Y, z = z.Center.Z,
            owner = (int)z.Owner, capturing = (int)z.Capturing, progress = z.Progress / SkyBridgeConquest.CaptureSeconds,
            contested = z.Contested, team1Count = z.Team1Count, team2Count = z.Team2Count }).ToArray()
    });
}
