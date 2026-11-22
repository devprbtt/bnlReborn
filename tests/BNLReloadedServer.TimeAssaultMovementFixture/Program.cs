using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using BNLReloadedServer.Servers;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

int checks = 0, movementCallbacks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
((ServerCatalogue)Databases.Catalogue).Replicate(File.ReadAllText(args[0]).Deserialize<List<Card>>()!);
var match = Databases.Catalogue.All.OfType<CardMatch>().First(c => c.Data?.Type == MatchType.TimeTrial);
var ordinary = Databases.Catalogue.All.OfType<CardMatch>().First(c => c.Data?.Type == MatchType.Tutorial);
foreach (var course in CatalogueHelper.GlobalLogic.TimeTrial!.Courses!)
{
    Check(TimeAssaultCourses.TryLoad(course.Map, out _, out var map), "actual course loads");
    var start = map!.SpawnPoints.First(p => p.Team == TeamType.Team1 && p.Label == SpawnPointLabel.Base).Position + new Vector3(0,.08f,0);
    var terrain = new MapBinary(map.Schema, map.BlocksData!, map.Size, map.Properties?.PlanePosition ?? 0,
        new MapUpdater((_,_)=>{},(_,_)=>{},_=>{},_=>true));
    for (int restart=0; restart<3; restart++)
    {
        var sender = new CaptureSender(); var immediate = new ServiceZone(sender);
        var data = new ZoneData(new ZoneUpdater(_=>{})) { MatchKey=match.Key, GameModeKey=CatalogueHelper.ModeTimeTrial.Key,
            MapData=map, BlocksData=terrain, Phase=new ZonePhase { PhaseType=ZonePhaseType.TutorialInit, StartTime=1000 } };
        var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
        typeof(GameZone).GetField("_zoneData",fields)!.SetValue(zone,data);
        typeof(GameZone).GetField("_serviceZone",fields)!.SetValue(zone,immediate);
        typeof(GameZone).GetField("_unbufferedZone",fields)!.SetValue(zone,immediate);
        var updater = (UnitUpdater)RuntimeHelpers.GetUninitializedObject(typeof(UnitUpdater));
        updater = updater with { OnUnitInit=(_,_)=>{}, OnUnitMove=(_,_,_,_)=>movementCallbacks++ };
        var unit = new Unit(7,new UnitInit { Key=course.Hero, PlayerId=42, Team=TeamType.Team1,
            Transform=new ZoneTransform { Position=start } },updater);
        typeof(GameZone).GetField("_units",fields)!.SetValue(zone,new Dictionary<uint,Unit>{{7,unit}});
        ulong timestamp=1000;
        foreach (var phase in new[]{ZonePhaseType.TutorialInit,ZonePhaseType.Waiting,ZonePhaseType.Build})
        {
            data.Phase.PhaseType=phase;
            for (int packet=0; packet<25; packet++)
            {
                var requested = new ZoneTransform { Position=start+new Vector3(.1f+packet,0,0), LocalVelocity=new Vector3s(0,0,900),
                    Rotation=new Vector3s(0,packet,0), IsSprint=true,IsJump=true,IsCrouch=true,IsDash=true,IsWallClimb=true,IsGroundSlam=true };
                int callbacks=movementCallbacks;
                zone.ReceivedMoveRequest(7,++timestamp,requested);
                Check(unit.Transform.Position==start,"held Shift+W cannot move exact authored spawn during "+phase);
                Check(unit.Transform.LocalVelocity==Vector3s.Zero && !unit.Transform.IsSprint && !unit.Transform.IsJump &&
                    !unit.Transform.IsCrouch && !unit.Transform.IsDash && !unit.Transform.IsWallClimb && !unit.Transform.IsGroundSlam,"all locomotion flags/velocity neutralized");
                Check(unit.Transform.Rotation==requested.Rotation && unit.LastMoveUpdateTime==timestamp,"fresh look/heartbeat retained");
                Check(movementCallbacks==callbacks,"rejected move cannot run trap/pickup/terrain callback");
                using var reader=new BinaryReader(new MemoryStream(sender.Messages[^1]));
                reader.ReadByte(); reader.ReadByte();
                Check(reader.ReadUInt32()==7 && Maneuver.ReadVariant(reader) is ManeuverTeleport teleport && teleport.Position==start && reader.BaseStream.Position==reader.BaseStream.Length,
                    "immediate native teleport corrects existing clients before buffer loop starts");
            }
        }
        int messages=sender.Messages.Count;
        zone.ReceivedMoveRequest(7,timestamp-1,new ZoneTransform{Position=start,Rotation=new Vector3s(1,1,1)});
        Check(sender.Messages.Count==messages && unit.LastMoveUpdateTime==timestamp && unit.Transform.Rotation!=new Vector3s(1,1,1),"old stationary heartbeat cannot rewind aim/time");
        data.Phase=new ZonePhase { PhaseType=ZonePhaseType.Assault, StartTime=2000 };
        zone.ReceivedMoveRequest(7,1999,new ZoneTransform {Position=start+Vector3.UnitX,IsSprint=true});
        Check(unit.Transform.Position==start && unit.LastMoveUpdateTime==timestamp,"packet sampled before GO cannot sneak in after phase change");
        var allowed=new ZoneTransform{Position=start+new Vector3(.1f,0,0),LocalVelocity=new Vector3s(0,0,900),IsSprint=true};
        int before=movementCallbacks;
        zone.ReceivedMoveRequest(7,2001,allowed);
        Check(unit.Transform==allowed && movementCallbacks==before+1,"first actual post-GO movement accepted immediately");
        zone.ReceivedMoveRequest(7,1998,new ZoneTransform { Position=start+Vector3.UnitX*10 });
        Check(unit.Transform==allowed && unit.LastMoveUpdateTime==2001,"late countdown packet after genuine movement cannot rewind the runner");
        foreach(var phase in new[]{ZonePhaseType.TutorialInit,ZonePhaseType.Waiting,ZonePhaseType.Build})
        {
            data.MatchKey=ordinary.Key;data.Phase.PhaseType=phase;
            var normal=new ZoneTransform{Position=start,IsSprint=true};
            zone.ReceivedMoveRequest(7,++timestamp+3000,normal);
            Check(unit.Transform==normal,"ordinary-mode movement preserved in "+phase);
        }
        data.MatchKey=match.Key;data.Phase.PhaseType=ZonePhaseType.Build;unit.PlayerId=null;
        var deviceMove=new ZoneTransform { Position=start };
        zone.ReceivedMoveRequest(7,++timestamp+4000,deviceMove);
        Check(unit.Transform==deviceMove,"non-player unit movement remains outside TA runner lock");
    }
    Console.WriteLine("COURSE "+course.Map.GetCard<CardMap>()!.Id+": initial load and two restarts locked until GO");
}
Console.WriteLine($"TIME_ASSAULT_MOVEMENT PASS {checks} checks");

public class CaptureSender : ISender
{
    public List<byte[]> Messages=new(); public uint? AssociatedPlayerId{get;set;} public int SenderCount=>1;
    public void Send(BinaryWriter writer)=>Messages.Add(((MemoryStream)writer.BaseStream).ToArray());
    public void Send(byte[] bytes)=>Messages.Add(bytes);
    public void SendExcept(BinaryWriter writer,List<Guid> excluded)=>Send(writer);
    public void Subscribe(Guid id){} public void Unsubscribe(Guid id){} public void UnsubscribeAll(){}
}
