using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Service;

namespace BNLReloadedServer.ServerTypes;

public static class AdminBeamRules
{
    public const float ChargeSeconds = 2.5f, Range = 160, CooldownSeconds = 4;
    public static float Charge(double seconds) => Math.Clamp((float)seconds / ChargeSeconds, 0, 1);
    public static float Radius(float charge) => 2 + 5 * charge;
    public static DamageData Damage(float charge) => DamageData.ZeroDamage with
    { EnemyDamage = 250 + 950 * charge, EnemyDeviceDamage = 500 + 4500 * charge,
      EnemyObjectiveDamage = 250 + 950 * charge, BlockDamage = 5000 + 20000 * charge };
    public static float SegmentDistance(Vector3 point, Vector3 from, Vector3 to)
    {
        var delta = to-from;
        float along = delta.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(point-from,delta)/delta.LengthSquared(),0,1) : 0;
        return Vector3.Distance(point, from+along*delta);
    }
}

public partial class GameZone
{
    // MatchConnection services own negotiated capabilities; the shared buffered
    // service intentionally has none and cannot relay capability-gated VFX.
    public Action<uint,byte,long,Vector3,Vector3,float>? AdminBeamSender { get; set; }
    public Action<uint,uint,byte,Vector3,Vector3>? AdminFireballSender { get; set; }
    private void ReceivedAdminBeam(FunnyHold hold, string action)
    {
        if (hold.Admin.ZoneService is not ServiceZone { SupportsAdminBeam: true }) return;
        if (action == "beam_cancel") { CancelAdminBeam(hold); return; }
        if (FunnyDig(hold.Admin) || FunnySecondary(hold.Admin) || hold.Admin.CurrentGear == null || hold.Target != null) { CancelAdminBeam(hold); return; }
        var now = DateTimeOffset.UtcNow;
        if (action == "beam_charge")
        {
            if (hold.BeamStart != null || now < hold.NextBeam) return;
            hold.BeamStart = now; hold.NextBeamPulse = now;
            TickAdminBeam(hold);
        }
        else if (action == "beam_fire" && hold.BeamStart is {} started)
        {
            float charge = AdminBeamRules.Charge((now-started).TotalSeconds);
            hold.BeamStart = null; hold.NextBeam = now.AddSeconds(AdminBeamRules.CooldownSeconds);
            FireAdminBeam(hold, charge, now.ToUnixTimeMilliseconds());
        }
    }

    private void TickAdminBeam(FunnyHold hold)
    {
        if (hold.BeamStart == null) return;
        if (FunnyDig(hold.Admin) || FunnySecondary(hold.Admin) || hold.Admin.CurrentGear == null || hold.Target != null) { CancelAdminBeam(hold); return; }
        var now = DateTimeOffset.UtcNow;
        if (now < hold.NextBeamPulse) return;
        hold.NextBeamPulse = now.AddMilliseconds(100);
        var from = hold.Admin.GetMidpoint();
        AdminBeamSender?.Invoke(hold.Admin.Id, 0, hold.BeamStart.Value.ToUnixTimeMilliseconds(), from,
            from+hold.Direction*3, AdminBeamRules.Charge((now-hold.BeamStart.Value).TotalSeconds));
    }

    private void CancelAdminBeam(FunnyHold hold)
    {
        if (hold.BeamStart == null) return;
        hold.BeamStart = null;
        AdminBeamSender?.Invoke(hold.Admin.Id, 2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Vector3.Zero, Vector3.Zero, 0);
    }

    private void FireAdminBeam(FunnyHold hold, float charge, long time)
    {
        var admin = hold.Admin; var from = admin.GetMidpoint(); var end = from;
        // First solid terrain intercepts the beam. The blast still uses native
        // block damage so locked/indestructible map geometry remains authoritative.
        for (float distance = 0; distance <= AdminBeamRules.Range; distance += .2f)
        {
            var point = from+hold.Direction*distance; var cell = (Vector3s)point;
            if (!MapBinary.ContainsBlock(cell)) break;
            end = point;
            if (!MapBinary[cell].IsPassable) break;
        }
        var damage = AdminBeamRules.Damage(charge); float radius = AdminBeamRules.Radius(charge);
        ApplyEntityDamage(admin, from, end, damage, radius, .65f+charge);
        AdminBeamSender?.Invoke(admin.Id, 1, time, from, end, charge);
    }
    private void ApplyEntityDamage(Unit admin, Vector3 from, Vector3 end, DamageData damage, float radius, float beamRadius)
    {
        var impact = new ImpactData { CasterUnitId = admin.Id, CasterPlayerId = admin.PlayerId,
            ShotPos = from, InsidePoint = end, SourceKey = admin.CurrentGear?.Card.Key ?? admin.Key,
            DamageCredit = admin.DamageCredit };
        // Resolve units once, before terrain removal. Solid terrain blocks line of sight.
        foreach (var unit in _units.Values.ToArray())
        {
            if (unit == admin || unit.IsDead || !unit.IsActive) continue;
            var middle = unit.GetMidpoint();
            bool inBeam = beamRadius > 0 && AdminBeamRules.SegmentDistance(middle,from,end) <= beamRadius;
            bool inBlast = Vector3.Distance(middle,end) <= radius;
            Vector3 direction=Vector3.Normalize(end-from);
            if (!AdminFunnyRules.Finite(direction)) direction=Vector3.Zero;
            if ((inBeam && FunnyLineClear(from,middle)) || (inBlast && FunnyLineClear(end-direction*.25f,middle)))
                unit.TakeDamage(damage, impact, false, admin, admin.Team);
        }
        var updates = new Dictionary<Vector3s,BlockUpdate>();
        for (int x=(int)MathF.Floor(end.X-radius); x<=MathF.Ceiling(end.X+radius); x++)
        for (int y=(int)MathF.Floor(end.Y-radius); y<=MathF.Ceiling(end.Y+radius); y++)
        for (int z=(int)MathF.Floor(end.Z-radius); z<=MathF.Ceiling(end.Z+radius); z++)
        {
            var cell = new Vector3s((short)x,(short)y,(short)z);
            if (!MapBinary.ContainsBlock(cell) || Vector3.Distance(new Vector3(x+.5f,y+.5f,z+.5f),end)>radius) continue;
            foreach (var update in MapBinary.DamageBlock(cell, damage, admin, scoreSource: impact.SourceKey)) updates[update.Key]=update.Value;
        }
        if (updates.Count > 0) DoBlockUpdate(updates);
    }
}
