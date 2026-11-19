using System.Numerics;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Service;
namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private sealed class EntityFireball
    {
        public uint Id;
        public required Unit Owner;
        public Unit? Target;
        public Vector3 Position, Direction;
        public DateTimeOffset Born=DateTimeOffset.UtcNow, Last=DateTimeOffset.UtcNow, NextPulse;
    }
    private readonly List<EntityFireball> _entityFireballs=new();
    private uint _entityFireballId;
    private void LaunchAdminFireball(FunnyHold hold)
    {
        var now=DateTimeOffset.UtcNow;
        if(hold.Admin.ZoneService is not ServiceZone {SupportsAdminBeam:true} || FunnyDig(hold.Admin) ||
            hold.Admin.CurrentGear==null || hold.BeamStart!=null || now<hold.NextFireball || _entityFireballs.Count(b=>b.Owner==hold.Admin)>=8) return;
        hold.NextFireball=now.AddSeconds(.8);
        var origin=hold.Admin.GetMidpoint(); Unit? target=null; float best=float.NegativeInfinity;
        foreach(var candidate in _playerUnits.Values)
        {
            if(candidate==hold.Admin || !FunnyAlive(candidate) || candidate.Team==hold.Admin.Team || candidate.Team==TeamType.Neutral) continue;
            var offset=candidate.GetMidpoint()-origin; float distance=offset.Length(); if(distance<.1f || distance>80) continue;
            float aim=Vector3.Dot(offset/distance,hold.Direction);
            if(aim<.5f || aim-distance*.001f<=best || !FunnyLineClear(origin,candidate.GetMidpoint())) continue;
            best=aim-distance*.001f;target=candidate;
        }
        var ball=new EntityFireball {Id=++_entityFireballId,Owner=hold.Admin,Target=target,Position=origin,Direction=hold.Direction};
        _entityFireballs.Add(ball); AdminFireballSender?.Invoke(ball.Id,ball.Owner.Id,0,ball.Position,ball.Direction*24);
    }
    private void TickAdminFireballs()
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var ball in _entityFireballs.ToArray())
        {
            if(!_funnyModes.ContainsKey(ball.Owner.Id) || (now-ball.Born).TotalSeconds>5)
            { _entityFireballs.Remove(ball);AdminFireballSender?.Invoke(ball.Id,ball.Owner.Id,2,ball.Position,Vector3.Zero);continue; }
            float delta=Math.Clamp((float)(now-ball.Last).TotalSeconds,0,.1f);ball.Last=now;
            if(ball.Target!=null && FunnyAlive(ball.Target))
            {
                var offset=ball.Target.GetMidpoint()-ball.Position;
                if(offset.LengthSquared()>.001f) ball.Direction=Vector3.Normalize(Vector3.Lerp(ball.Direction,Vector3.Normalize(offset),Math.Clamp(delta*3.5f,0,1)));
            }
            var end=ball.Position+ball.Direction*(24*delta); bool hit=false;
            int steps=Math.Max(1,(int)MathF.Ceiling(Vector3.Distance(ball.Position,end)/.15f));var from=ball.Position;
            for(int i=1;i<=steps;i++)
            {
                var point=Vector3.Lerp(from,end,(float)i/steps);var cell=(Vector3s)point;
                if(!MapBinary.ContainsBlock(cell)) {hit=true;break;}
                ball.Position=point;
                if(!MapBinary[cell].IsPassable) {hit=true;break;}
                if(_playerUnits.Values.Any(u=>u!=ball.Owner && FunnyAlive(u) && u.Team!=ball.Owner.Team && u.Team!=TeamType.Neutral && Vector3.Distance(u.GetMidpoint(),point)<.8f)) {hit=true;break;}
            }
            if(hit)
            {
                var damage=DamageData.ZeroDamage with {EnemyDamage=180,EnemyDeviceDamage=400,EnemyObjectiveDamage=100,BlockDamage=1500};
                ApplyEntityDamage(ball.Owner,from,ball.Position,damage,2.5f,0);
                _entityFireballs.Remove(ball);AdminFireballSender?.Invoke(ball.Id,ball.Owner.Id,1,ball.Position,ball.Direction*24);continue;
            }
            if(now>=ball.NextPulse) {AdminFireballSender?.Invoke(ball.Id,ball.Owner.Id,0,ball.Position,ball.Direction*24);ball.NextPulse=now.AddMilliseconds(100);}
        }
    }
}
