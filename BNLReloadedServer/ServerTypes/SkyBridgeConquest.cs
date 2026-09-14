using System.Numerics;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

// Pure match rules: elapsed time and authoritative living player positions are inputs.
public sealed class SkyBridgeConquest
{
    public const string MapId = "map_sr2_sky_bridge_don_edit_conquest";
    public const string OriginalMapId = "map_sr2_sky_bridge_don_edit";
    public ConquestLogic Rules { get; }
    public sealed record Player(uint Id, TeamType Team, Vector3 Position);
    public sealed class Zone(Vector3 center)
    {
        public Vector3 Center { get; } = center;
        public TeamType Owner { get; internal set; }
        public TeamType Capturing { get; internal set; }
        public float Progress { get; internal set; }
        public bool Contested { get; internal set; }
        public int Team1Count { get; internal set; }
        public int Team2Count { get; internal set; }
        internal bool Contains(Vector3 position, ConquestLogic rules) =>
            Math.Abs(position.X - Center.X) <= rules.ZoneHalfWidth &&
            Math.Abs(position.Z - Center.Z) <= rules.ZoneHalfWidth && position.Y >= Center.Y - rules.ZoneDepthBelow &&
            position.Y <= Center.Y + rules.ZoneHeightAbove;
    }
    public Zone[] Zones { get; }
    public float[] Scores { get; } = new float[3];
    private readonly Dictionary<uint, int> _zonesCaptured = [];
    private readonly Dictionary<uint, float> _zoneTimeSeconds = [];
    public TeamType Attacker { get; private set; }
    public float AttackRemaining { get; private set; }
    public int Round { get; private set; }
    public float Target => Round switch
    {
        0 => Rules.LiteBbSeconds,
        1 => Rules.ClassicBbSeconds,
        _ => Rules.ExtremeBbSeconds
    };
    public string Tier => Round == 0 ? "lite" : Round == 1 ? "classic" : "uber";
    public bool Attacking => Attacker != TeamType.Neutral;
    public int ZonesCaptured(uint playerId) => _zonesCaptured.GetValueOrDefault(playerId);
    public int ZoneTimeSeconds(uint playerId) => (int)Math.Floor(_zoneTimeSeconds.GetValueOrDefault(playerId));
    public SkyBridgeConquest(IEnumerable<Vector3> centers, ConquestLogic? rules = null)
    {
        Rules = (rules ?? new ConquestLogic()).Validated();
        Zones = centers.Select(c => new Zone(c)).ToArray();
        if (Zones.Length != 3) throw new ArgumentException("Conquest requires exactly three BB drop points.");
    }
    public bool Shielded(TeamType team) => !Attacking || team == Attacker;
    public bool ObjectiveShielded(TeamType team, IEnumerable<UnitLabel> labels, UnitLabel? currentObjective) =>
        Shielded(team) || currentObjective is null || !labels.Contains(currentObjective.Value);
    public float ScoreRate(TeamType team) => Attacking || team == TeamType.Neutral ? 0 :
        Zones.Count(z => z.Owner == team) switch { 3 => Rules.TripleCaptureRate, 2 => 1, _ => 0 };
    public void Step(float elapsed, IReadOnlyList<Player> players)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        foreach (var zone in Zones)
        {
            zone.Team1Count = players.Count(p => p.Team == TeamType.Team1 && zone.Contains(p.Position, Rules));
            zone.Team2Count = players.Count(p => p.Team == TeamType.Team2 && zone.Contains(p.Position, Rules));
            zone.Contested = zone.Team1Count > 0 && zone.Team2Count > 0;
        }
        if (Attacking)
        {
            AttackRemaining = Math.Max(0, AttackRemaining - elapsed);
            if (AttackRemaining == 0 || !players.Any(p => p.Team == Attacker)) EndAttack();
            return;
        }
        // Presence is credited once per step even if tuned zone bounds overlap.
        foreach (var player in players.Where(player => Zones.Any(zone => zone.Contains(player.Position, Rules))))
            _zoneTimeSeconds[player.Id] = _zoneTimeSeconds.GetValueOrDefault(player.Id) + elapsed;
        // Score the ownership held during this step, before resolving captures at its end.
        foreach (var team in new[] { TeamType.Team1, TeamType.Team2 })
        {
            var rate = ScoreRate(team);
            if (rate == 0) continue;
            Scores[(int)team] = Math.Min(Target, Scores[(int)team] + elapsed * rate);
            if (Scores[(int)team] >= Target && players.Any(p => p.Team == team))
            {
                Attacker = team; AttackRemaining = Rules.AttackSeconds; return;
            }
        }
        foreach (var zone in Zones)
        {
            var one = zone.Team1Count;
            var two = zone.Team2Count;
            if (one == two) continue; // Empty/tied zones retain ownership and pause capture.
            var majority = one > two ? TeamType.Team1 : TeamType.Team2;
            if (zone.Owner == majority) { zone.Progress = 0; zone.Capturing = TeamType.Neutral; continue; }
            if (zone.Capturing != majority) { zone.Capturing = majority; zone.Progress = 0; }
            zone.Progress = Math.Min(Rules.CaptureSeconds, zone.Progress + elapsed);
            if (zone.Progress >= Rules.CaptureSeconds)
            {
                zone.Owner = majority; zone.Capturing = TeamType.Neutral; zone.Progress = 0;
                foreach (var player in players.Where(player => player.Team == majority && zone.Contains(player.Position, Rules)))
                    _zonesCaptured[player.Id] = _zonesCaptured.GetValueOrDefault(player.Id) + 1;
            }
        }
    }
    private void EndAttack()
    {
        Attacker = TeamType.Neutral; AttackRemaining = 0; Round++;
        Array.Clear(Scores);
        foreach (var zone in Zones)
        {
            zone.Owner = zone.Capturing = TeamType.Neutral; zone.Progress = 0; zone.Contested = false;
        }
    }
}
