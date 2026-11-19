using System.Globalization;
using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private sealed class FunnyHold
    {
        public required Unit Admin;
        public Unit? Target;
        public DateTimeOffset LastUpdate = DateTimeOffset.UtcNow;
        public DateTimeOffset? ChargeStart;
        public DateTimeOffset? BeamStart;
        public DateTimeOffset NextBeam, NextBeamPulse;
        public DateTimeOffset NextFireball;
        public bool WellActive;
        public DateTimeOffset? RepulseStart;
        public DateTimeOffset NextRepulse,NextGravityPulse;
        public readonly HashSet<Unit> GravityTargets=new();
        public Vector3 Direction = Vector3.UnitZ;
        public float Distance = 6;
    }
    private readonly Dictionary<uint, FunnyHold> _funnyModes = new();
    private long _funnyStormEpochMs;
    private static bool FunnyDig(Unit unit) => unit.CurrentGear?.Card.Tools?.Any(t =>
        t is ToolShot { HitEffect: InstEffectDamage { Damage.Mining: true } }) == true;
    private static bool FunnyCapable(Unit unit) => unit.ZoneService is ServiceZone { SupportsAdminFunnyMode: true };
    private static bool FunnySecondary(Unit unit) => !FunnyDig(unit) && unit.CurrentGear?.Index==2;
    private bool FunnyAlive(Unit unit) => unit.IsActive && !unit.IsDead && _playerUnits.ContainsKey(unit.Id);

    private void ReceivedAdminFunny(Unit admin, IReadOnlyList<string> args)
    {
        // Recheck on the world thread: queued commands must not outlive a role revocation.
        if (!AdminFunnyRules.IsAdmin(admin.PlayerId is {} id ? Databases.PlayerDatabase.GetPlayerDataNoWait(id)?.Role : null)
            || !FunnyCapable(admin) || args.Count != 6) return;
        var action = args[0];
        if (action == "off") { RemoveFunny(admin.Id); return; }
        if (!FunnyAlive(admin) || admin.AdminFunnyHeld) return;
        if (action == "on")
        {
            if (!_funnyModes.ContainsKey(admin.Id))
            {
                _funnyStormEpochMs = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _funnyStormEpochMs + 1);
                // Remaining admins retain this epoch even if the newest one later leaves.
                foreach (var existing in _funnyModes.Values) existing.Admin.SetAdminFunnyStormEpoch(_funnyStormEpochMs);
                _funnyModes.Add(admin.Id, new FunnyHold { Admin = admin });
            }
            admin.SetAdminFunnyMode(true, _funnyStormEpochMs);
            SendFunny(_funnyModes[admin.Id], 0);
            return;
        }
        if (!_funnyModes.TryGetValue(admin.Id, out var hold)) return;
        if (!uint.TryParse(args[1], out var targetId) || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)
            || !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return;
        var direction = new Vector3(x,y,z);
        var maxDistance = new Vector3(MapBinary.SizeX, MapBinary.SizeY, MapBinary.SizeZ).Length();
        if (!AdminFunnyRules.ValidAim(direction, distance, maxDistance)) return;
        hold.LastUpdate = DateTimeOffset.UtcNow;
        admin.LastMoveTime = hold.LastUpdate; admin.WasAfkWarned = false;
        hold.Direction = Vector3.Normalize(direction); hold.Distance = distance;
        if(action=="invis_toggle")
        {
            if(admin.ZoneService is ServiceZone {SupportsAdminInvisibility:true})admin.SetAdminInvisible(!admin.AdminInvisible);
            return;
        }
        if(action is "well_on" or "well_off" or "repulse_charge" or "repulse_fire" or "gravity_cancel") {ReceivedAdminGravity(hold,action);return;}
        if (action == "fireball") { LaunchAdminFireball(hold); return; }
        if (action.StartsWith("beam_", StringComparison.Ordinal)) { ReceivedAdminBeam(hold, action); return; }
        if (hold.BeamStart != null && FunnyDig(admin)) CancelAdminBeam(hold);
        if (action == "drop" || !FunnyDig(admin)) { DropFunny(hold); return; }
        if (action == "grab" && hold.Target == null && _playerUnits.TryGetValue(targetId, out var target)
            && target != admin && FunnyAlive(target) && FunnyCapable(target) && !target.AdminFunnyHeld && !_funnyModes.ContainsKey(target.Id))
        {
            var offset = target.GetMidpoint() - admin.GetMidpoint();
            var projected = Vector3.Dot(offset, hold.Direction);
            if (projected < 0 || (offset - hold.Direction * projected).Length() > 2 || !FunnyLineClear(admin.GetMidpoint(), target.GetMidpoint())) return;
            hold.Target = target; target.AdminFunnyHeld = true;
            var channel = target.CurrentChannelData;
            target.CurrentBuildInfo = null; target.CurrentChannelData = null;
            target.DashCharge.Clear(); target.StartChargeTime = null;
            _serviceZone.SendDoCancelBuild(target.Id); if (channel != null) _serviceZone.SendDoEndChannel(target.Id,channel.ToolIndex);
            hold.ChargeStart = null;
        }
        else if (action == "charge" && hold.Target != null) hold.ChargeStart ??= DateTimeOffset.UtcNow;
        else if (action == "throw" && hold.Target != null && hold.ChargeStart != null)
        {
            var velocity = AdminFunnyRules.Launch(hold.Direction, (DateTimeOffset.UtcNow - hold.ChargeStart.Value).TotalSeconds);
            DropFunny(hold, velocity); return;
        }
        SendFunny(hold, 0);
    }

    private bool FunnyLineClear(Vector3 from, Vector3 to)
    {
        var length = Vector3.Distance(from,to);
        for (float d = 0; d <= length; d += 0.25f)
        {
            var cell = (Vector3s)Vector3.Lerp(from,to,length > 0 ? d/length : 0);
            if (MapBinary.ContainsBlock(cell) && !MapBinary[cell].IsPassable) return false;
        }
        return true;
    }
    private void TickAdminFunny()
    {
        foreach (var hold in _funnyModes.Values.ToArray())
        {
            var admin = hold.Admin;
            if (!FunnyAlive(admin) || !AdminFunnyRules.IsAdmin(admin.PlayerId is {} id ? Databases.PlayerDatabase.GetPlayerDataNoWait(id)?.Role : null)
                || (DateTimeOffset.UtcNow-hold.LastUpdate).TotalSeconds > AdminFunnyRules.LeaseSeconds)
            { RemoveFunny(admin.Id); continue; }
            TickAdminBeam(hold);
            TickAdminGravity(hold);
            if (hold.Target is not {} target) continue;
            if (!FunnyAlive(target) || !FunnyDig(admin)) { DropFunny(hold); continue; }
            target.LastMoveTime = DateTimeOffset.UtcNow; target.WasAfkWarned = false;
            var midpoint = admin.GetMidpoint() + hold.Direction * hold.Distance;
            // Sweep the complete standing capsule in short steps. Walls stop the hold; never tunnel through terrain.
            var oldMid = target.Transform.Position + Vector3.UnitY * .95f;
            var length = Vector3.Distance(oldMid, midpoint);
            var steps = Math.Max(1, (int)MathF.Ceiling(length / .2f));
            var safe = oldMid;
            for (var i=1; i<=steps; i++)
            {
                var next = Vector3.Lerp(oldMid,midpoint,(float)i/steps);
                if (next.X < .4f || next.X > MapBinary.SizeX-.4f || next.Z < .4f || next.Z > MapBinary.SizeZ-.4f
                    || next.Y < 1 || next.Y > MapBinary.SizeY-1 || !TeleportPlacement.PlayerFits(MapBinary,next)) break;
                safe = next;
            }
            var transform = new ZoneTransform { Position = safe-Vector3.UnitY*.95f, Rotation = target.Transform.Rotation };
            // Use the synchronized server millisecond clock so observers receive increasing timestamps.
            target.UnitMove(transform, Math.Max(target.LastMoveUpdateTime+1, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            SendFunny(hold, 0);
        }
        TickAdminFireballs();
    }
    private void SendFunny(FunnyHold hold, byte action, Vector3 velocity = default, bool enabled = true)
    {
        var target = hold.Target;
        var charge = hold.ChargeStart is {} start ? AdminFunnyRules.Charge((DateTimeOffset.UtcNow-start).TotalSeconds) : 0;
        (hold.Admin.ZoneService as ServiceZone)?.SendAdminFunny(hold.Admin.Id,target?.Id ?? 0,enabled,action,target?.Transform.Position ?? Vector3.Zero,velocity,charge);
        if (target != null) (target.ZoneService as ServiceZone)?.SendAdminFunny(hold.Admin.Id,target.Id,enabled,action,target.Transform.Position,velocity,charge);
    }
    private void DropFunny(FunnyHold hold, Vector3 velocity = default)
    {
        if (hold.Target is {} target)
        {
            target.AdminFunnyHeld = false;
            SendFunny(hold,velocity == Vector3.Zero ? (byte)1 : (byte)2,velocity);
        }
        hold.Target = null; hold.ChargeStart = null;
        SendFunny(hold,0);
    }
    private void RemoveFunny(uint id)
    {
        if (!_funnyModes.Remove(id,out var hold)) return;
        CancelAdminBeam(hold);
        CancelAdminGravity(hold);
        DropFunny(hold); hold.Admin.SetAdminFunnyMode(false); SendFunny(hold,1,enabled:false);
        if(hold.Admin.AdminSpectatorEntity && hold.Admin.PlayerId is {} playerId)EndAdminSpectator(playerId);
    }
}
