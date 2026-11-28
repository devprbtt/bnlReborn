using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Servers;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var cards=new Dictionary<Key,CardUnit>();
Key Card(string name,params UnitLabel[] labels){var key=new Key(name);cards[key]=new CardUnit{Key=key,Labels=labels.ToList()};return key;}
var hero=Card("hero"); var cube=Card("cube",UnitLabel.Objective);
var supply=Card("supply",UnitLabel.SupplyResource);var bb=Card("bb",UnitLabel.SupplyBlockbuster);
var spawn=Card("drop",UnitLabel.DropPointResource);
byte[] Packet(byte message,Action<BinaryWriter> body){using var w=new BinaryWriter(new MemoryStream());w.Write((byte)6);w.Write(message);body(w);return RecordingSender.Frame(w);}
byte[] Create(uint id,Key key,uint? player=null,TeamType team=TeamType.Neutral)=>Packet(10,w=>{w.Write(id);new UnitInit{Key=key,PlayerId=player,Team=team}.Write(w);});
byte[] Kill(uint id,uint? killer=null,bool crit=false)=>Packet(67,w=>new KillInfo{DeadUnitId=id,Killer=killer,Crit=crit}.Write(w));
var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".bnlreplay");
using(var file=File.Create(path))using(var zip=new BrotliStream(file,CompressionLevel.Fastest))using(var w=new BinaryWriter(zip)){
 w.Write(MatchRecorder.Magic);w.Write(MatchRecorder.FormatVersion);
 var metadata=Encoding.UTF8.GetBytes("{\"mode_key\":1,\"match_key\":2,\"server_time_at_start\":100000}");w.Write(metadata.Length);w.Write(metadata);
 void Record(byte kind,uint at,byte[] data){w.Write(kind);w.Write(at);w.Write(data.Length);w.Write(data);}
 void Stream(uint at,byte[] data)=>Record(3,at,data);
 Record(1,0,Packet(0,w=>{}));
 var snapshot=Packet(9,w=>new ZoneUpdate{Phase=new ZonePhase{PhaseType=ZonePhaseType.Build},PlayerInfo=new(){[1]=new(){Nickname="<b>Alpha</b>"}}}.Write(w));
 Record(2,0,snapshot.Concat(Create(100,hero,1,(TeamType)1)).Concat(Create(101,hero,2,(TeamType)2)).Concat(Create(102,hero,3,(TeamType)2)).Concat(Create(200,cube,null,(TeamType)2)).Concat(Create(201,cube,null,(TeamType)1)).Concat(Create(300,supply)).ToArray());
 Stream(50,Create(301,spawn));
 Stream(60,Packet(9,w=>new ZoneUpdate{SupplyInfo=new(){NextSupplyDrop=supply,NextSupplyDropTime=999999}}.Write(w)));
 Stream(1000,Kill(101,1,true)); // Dead player field is absent in real live packets.
 Stream(2000,Kill(102,1));
 Stream(3000,Create(302,supply));Stream(3001,Create(302,supply));
 Stream(4000,Create(303,bb));
 Stream(5000,Kill(200,1));Stream(5001,Kill(200,1));
 Stream(6000,Packet(12,w=>w.Write((uint)201))); // A despawn is not destruction.
 Stream(7000,Create(104,hero,4,(TeamType)1));Stream(7100,Kill(104,1,true)); // friendly fire not an accolade
 Stream(8000,Kill(100));Stream(8100,Create(100,hero,1,(TeamType)1));
 Stream(9000,Create(101,hero,2,(TeamType)2));Stream(9100,Kill(101,1)); // death resets chain
 Stream(10000,Create(102,hero,3,(TeamType)2));Stream(20000,Kill(102,1)); // expired chain
 // Enough events to exercise chunk boundaries.
 for(uint i=0;i<130;i++)Stream(21000+i,Create(1000+i,supply));
 Record(255,30000,Encoding.UTF8.GetBytes("{}"));
}
ReplayInstance? replay=null,legacy=null,identified=null;
try{
 var timeline=new ReplayTimeline(path,k=>cards.GetValueOrDefault(k));
 Check(timeline.Events.Count==136,"exact events indexed from serialized replay");
 Check(timeline.Events.Count(e=>e.Kind==0)==3,"only first kill, headshot and double kill; friendly and expired chains excluded");
 Check(timeline.Events.Any(e=>e.At==1000&&e.Label=="Alpha: First Kill"),"recorded nickname is used without markup");
 Check(timeline.Events.Any(e=>e.At==2000&&e.Label.EndsWith("Double Kill")),"multikill timing uses replay offsets");
 Check(timeline.Events.Where(e=>e.At==1000).All(e=>e.Attacker==1&&e.Victim==2),"accolades retain recorded attacker and victim when KillInfo omits the victim");
 Check(timeline.Events.Count(e=>e.Kind==3)==1&&timeline.Events.Single(e=>e.Kind==3).At==5000,"cube kill deduplicated; despawn excluded");
 Check(timeline.Events.Count(e=>e.Kind==1)==131,"snapshot, duplicate create, static markers and scheduled drops excluded");
 Check(timeline.Events.Single(e=>e.Kind==2).At==4000,"BB actual spawn indexed separately");
 Check(timeline.Events.Select(e=>e.At).SequenceEqual(timeline.Events.Select(e=>e.At).Order()),"timeline is chronological");
 var host=new Host();replay=new(host,path,"test",7,unitCard:k=>cards.GetValueOrDefault(k));replay.EnableControls(7,eventTimeline:true);
 replay.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);replay.Control(7,1,0);
 var chunks=host.Packets.Where(p=>p[0]==121).ToArray();
 Check(chunks.Length==3,"event metadata sent in bounded chunks");
 int total=0;for(int i=0;i<chunks.Length;i++){using var r=new BinaryReader(new MemoryStream(chunks[i],1,chunks[i].Length-1));Check(r.ReadBoolean()==(i==0),"only first chunk resets index");int count=r.ReadInt32();Check(count<=64,"chunk count bounded");total+=count;for(int j=0;j<count;j++){r.ReadInt64();r.ReadByte();r.ReadString();}Check(r.BaseStream.Position==r.BaseStream.Length,"chunk fully decoded");}
 Check(total==timeline.Events.Count,"all indexed events transmitted without truncation");
 Check(!host.Packets.Any(p=>p[0]==122),"capability 20 keeps the original marker wire format");
 var actorHost=new Host();identified=new(actorHost,path,"actors",7,unitCard:k=>cards.GetValueOrDefault(k));
 identified.EnableControls(7,eventTimeline:true,timelineActors:true);
 identified.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());identified.PlayerZoneReady(7);identified.Control(7,1,0);
 var actorChunks=actorHost.Packets.Where(p=>p[0]==122).ToArray();
 Check(actorChunks.Length==3&&!actorHost.Packets.Any(p=>p[0]==121),"capability 21 sends actor-aware chunks only");
 var entries=new List<ReplayEvent>();
 foreach(var chunk in actorChunks)
 {
     using var reader=new BinaryReader(new MemoryStream(chunk,1,chunk.Length-1));
     reader.ReadBoolean(); int count=reader.ReadInt32();
     for(int i=0;i<count;i++)
     {
         uint at=(uint)reader.ReadInt64(); byte kind=reader.ReadByte(); string label=reader.ReadString();
         bool hasActors=reader.ReadBoolean();
         entries.Add(new(at,kind,label,hasActors?reader.ReadUInt32():null,hasActors?reader.ReadUInt32():null));
     }
     Check(reader.BaseStream.Position==reader.BaseStream.Length,"actor-aware chunk fully decoded");
 }
 Check(entries.SequenceEqual(timeline.Events),"actor-aware transport preserves all marker times and optional actor IDs");
 replay.Control(7,6,20000);await Until(()=>!host.Seeking&&host.Position==20000);
 Check(host.Paused,"absolute forward seek preserves pause");
 replay.Control(7,6,1000);Check(host.Scenes==1,"absolute rewind uses fresh scene recovery");replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);await Until(()=>!host.Seeking&&host.Position==1000);
 replay.Control(7,6,int.MaxValue);await Until(()=>!host.Seeking&&host.Position==29999);Check(host.Paused,"absolute seek clamps to recording end");
 replay.Control(7,6,-1);replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);await Until(()=>!host.Seeking&&host.Position==0);
 var oldHost=new Host();legacy=new(oldHost,path,"legacy",7);legacy.EnableControls(7);legacy.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());legacy.PlayerZoneReady(7);legacy.Control(7,1,0);legacy.Control(7,6,20000);
 Check(!oldHost.Packets.Any(p=>p[0]==121)&&!oldHost.Seeking,"old clients receive no timeline and cannot use absolute command");
 Console.WriteLine("REPLAY_TIMELINE_OK checks="+checks);
}finally{replay?.PlayerLeftInstance(7,KickReason.MatchQuit);legacy?.PlayerLeftInstance(7,KickReason.MatchQuit);identified?.PlayerLeftInstance(7,KickReason.MatchQuit);for(int i=0;;i++){try{File.Delete(path);break;}catch(IOException)when(i<40){await Task.Delay(50);}}}
async Task Until(Func<bool> ready){var until=DateTime.UtcNow.AddSeconds(5);while(!ready()&&DateTime.UtcNow<until)await Task.Delay(10);Check(ready(),"absolute seek completes");}
sealed class Host:IReplayHost{
 public readonly ConcurrentQueue<byte[]> Packets=new();public int Scenes;public volatile bool Seeking,Paused;public long Position;
 public void UpdateScene(uint id,Scene scene)=>Scenes++;public void SetReplayClock(uint id,long? time){}public void EndReplay(uint id,string instance){}
 public void SendToSession(Guid session,byte[] framed){using var r=new BinaryReader(new MemoryStream(framed));while(r.BaseStream.Position<r.BaseStream.Length){int count=r.Read7BitEncodedInt();var data=r.ReadBytes(count);if(data.Length<2||data[0]!=6)continue;if(data[1]==111){using var state=new BinaryReader(new MemoryStream(data,2,data.Length-2));state.ReadInt64();Position=state.ReadInt64();state.ReadInt64();Paused=state.ReadBoolean();Seeking=state.ReadBoolean();}else Packets.Enqueue(data[1..]);}}
}
