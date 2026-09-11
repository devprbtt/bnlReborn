using System.Numerics;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

// Pure match rules: elapsed time and authoritative living player positions are inputs.
public sealed class SkyBridgeConquest
{
    public const string MapId = "map_sr2_sky_bridge_don_edit_conquest";
    public const string OriginalMapId = "map_sr2_sky_bridge_don_edit";
    public const float HalfWidth = 6, HalfHeight = 3, CaptureSeconds = 10, AttackSeconds = 90;
    public sealed record Player(uint Id, TeamType Team, Vector3 Position);
    public sealed class Zone(Vector3 center)
    {
        public Vector3 Center { get; } = center;
        public TeamType Owner { get; internal set; }
        public TeamType Capturing { get; internal set; }
        public float Progress { get; internal set; }
        public bool Contested { get; internal set; }
    }
    public Zone[] Zones { get; }
    public float[] Scores { get; } = new float[3];
    public TeamType Attacker { get; private set; }
    public float AttackRemaining { get; private set; }
    public int Round { get; private set; }
    public float Target => Round < 3 ? 600 : 180;
    public string Tier => Round == 0 ? "lite" : Round == 1 ? "classic" : "uber";
    public bool Attacking => Attacker != TeamType.Neutral;
    public SkyBridgeConquest(IEnumerable<Vector3> centers)
    {
        Zones = centers.Select(c => new Zone(c)).ToArray();
        if (Zones.Length != 3) throw new ArgumentException("Conquest requires exactly three BB drop points.");
    }
    public bool Shielded(TeamType team) => !Attacking || team == Attacker;
    public void Step(float elapsed, IReadOnlyList<Player> players)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (Attacking)
        {
            AttackRemaining = Math.Max(0, AttackRemaining - elapsed);
            if (AttackRemaining == 0 || !players.Any(p => p.Team == Attacker)) EndAttack();
            return;
        }
        // Score the ownership held during this step, before resolving captures at its end.
        foreach (var team in new[] { TeamType.Team1, TeamType.Team2 })
        {
            if (Zones.Count(z => z.Owner == team) < 2) continue;
            Scores[(int)team] = Math.Min(Target, Scores[(int)team] + elapsed);
            if (Scores[(int)team] >= Target && players.Any(p => p.Team == team))
            {
                Attacker = team; AttackRemaining = AttackSeconds; return;
            }
        }
        foreach (var zone in Zones)
        {
            var inside = players.Where(p => Math.Abs(p.Position.X - zone.Center.X) <= HalfWidth &&
                Math.Abs(p.Position.Z - zone.Center.Z) <= HalfWidth &&
                Math.Abs(p.Position.Y - zone.Center.Y) <= HalfHeight).ToArray();
            var one = inside.Count(p => p.Team == TeamType.Team1);
            var two = inside.Count(p => p.Team == TeamType.Team2);
            zone.Contested = one > 0 && two > 0;
            if (one == two) continue; // Empty/tied zones retain ownership and pause capture.
            var majority = one > two ? TeamType.Team1 : TeamType.Team2;
            if (zone.Owner == majority) { zone.Progress = 0; zone.Capturing = TeamType.Neutral; continue; }
            if (zone.Capturing != majority) { zone.Capturing = majority; zone.Progress = 0; }
            zone.Progress = Math.Min(CaptureSeconds, zone.Progress + elapsed);
            if (zone.Progress >= CaptureSeconds)
            {
                zone.Owner = majority; zone.Capturing = TeamType.Neutral; zone.Progress = 0;
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
