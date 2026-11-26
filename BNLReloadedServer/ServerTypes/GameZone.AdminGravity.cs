using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Service;
namespace BNLReloadedServer.ServerTypes;

public static class AdminGravityRules
{
    public const float WellRadius=28,PullSpeed=22,ChargeSeconds=2,CooldownSeconds=5;
    public static float Charge(double seconds)=>Math.Clamp((float)seconds/ChargeSeconds,0,1);
    public static float Radius(float charge)=>12+20*charge;
    public static float Force(float charge,float distance)=> (18+57*charge)*(1-.5f*Math.Clamp(distance/Radius(charge),0,1));
}
public partial class GameZone
{
    public Action<uint,byte,long,Vector3,float,float>? AdminGravitySender {get;set;}
    private void GravityState(FunnyHold hold,byte phase,float radius,float strength,long? time=null)
    {AdminGravitySender?.Invoke(hold.Admin.Id,phase,time??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),hold.Admin.GetMidpoint(),radius,strength);}
    private void StopAdminWell(FunnyHold hold)
    {
        hold.WellActive=false;
        foreach(var target in hold.GravityTargets)target.SetAdminGravityPull(hold.Admin,0,false);
        hold.GravityTargets.Clear();
    }
    private void CancelAdminGravity(FunnyHold hold)
    {
        bool active=hold.WellActive || hold.RepulseStart!=null;
        StopAdminWell(hold);hold.RepulseStart=null;
        if(active)GravityState(hold,3,0,0);
    }
    private void ReceivedAdminGravity(FunnyHold hold,string action)
    {
        if(hold.Admin.ZoneService is not ServiceZone {SupportsAdminGravity:true}) return;
        if(action=="gravity_cancel") {CancelAdminGravity(hold);return;}
        if(action=="well_off") {if(hold.WellActive){StopAdminWell(hold);GravityState(hold,3,0,0);}return;}
        if(!FunnySecondary(hold.Admin)) {CancelAdminGravity(hold);return;}
        var now=DateTimeOffset.UtcNow;
        if(action=="well_on")
        {
            if(hold.WellActive) return;
            hold.RepulseStart=null;hold.WellActive=true;hold.NextGravityPulse=DateTimeOffset.MinValue;TickAdminGravity(hold);
        }
        else if(action=="repulse_charge")
        {
            if(hold.RepulseStart!=null || now<hold.NextRepulse) return;
            StopAdminWell(hold);hold.RepulseStart=now;hold.NextGravityPulse=DateTimeOffset.MinValue;TickAdminGravity(hold);
        }
        else if(action=="repulse_fire" && hold.RepulseStart is {} start)
        {
            float charge=AdminGravityRules.Charge((now-start).TotalSeconds),radius=AdminGravityRules.Radius(charge);
            hold.RepulseStart=null;hold.NextRepulse=now.AddSeconds(AdminGravityRules.CooldownSeconds);
            var center=hold.Admin.GetMidpoint();
            foreach(var target in GravityCandidates(hold.Admin,radius))
            {
                if (!CanKnockback(target, hold.Admin, hold.Admin.Team, false)) continue;
                ReleaseGravityGrab(target);
                float force=AdminGravityRules.Force(charge,Vector3.Distance(center,target.GetMidpoint()));
                _serviceZone.SendUnitManeuver(target.Id,new ManeuverKnockback {Origin=center,Force=force,MidairForce=force});
            }
            GravityState(hold,2,radius,charge);
        }
    }
    private IEnumerable<Unit> GravityCandidates(Unit admin,float radius)
    {
        var center=admin.GetMidpoint();
        foreach(var target in _playerUnits.Values)
            if(target!=admin && FunnyAlive(target) && Vector3.Distance(center,target.GetMidpoint())<=radius &&
                FunnyLineClear(center,target.GetMidpoint()))yield return target;
    }
    private void ReleaseGravityGrab(Unit target)
    {foreach(var owner in _funnyModes.Values)if(owner.Target==target)DropFunny(owner);}
    private void TickAdminGravity(FunnyHold hold)
    {
        if(!hold.WellActive && hold.RepulseStart==null)return;
        if(!FunnySecondary(hold.Admin)) {CancelAdminGravity(hold);return;}
        if(hold.WellActive)
        {
            var wanted=GravityCandidates(hold.Admin,AdminGravityRules.WellRadius)
                .Where(target=>Vector3.Distance(target.GetMidpoint(),hold.Admin.GetMidpoint())>2).ToHashSet();
            foreach(var previous in hold.GravityTargets.Where(target=>!wanted.Contains(target)).ToArray())
            {previous.SetAdminGravityPull(hold.Admin,0,false);hold.GravityTargets.Remove(previous);}
            foreach(var target in wanted)
            {ReleaseGravityGrab(target);if(hold.GravityTargets.Add(target))target.SetAdminGravityPull(hold.Admin,AdminGravityRules.PullSpeed,true);}
        }
        var now=DateTimeOffset.UtcNow;if(now<hold.NextGravityPulse)return;hold.NextGravityPulse=now.AddMilliseconds(100);
        if(hold.WellActive) GravityState(hold,0,AdminGravityRules.WellRadius,1);
        else if(hold.RepulseStart is {} start)
        {float charge=AdminGravityRules.Charge((now-start).TotalSeconds);GravityState(hold,1,AdminGravityRules.Radius(charge),charge,start.ToUnixTimeMilliseconds());}
    }
}
