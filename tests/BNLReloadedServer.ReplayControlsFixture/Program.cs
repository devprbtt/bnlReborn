using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.ServerTypes;

if(args.Length>1 && args[0]=="--recordings") { Environment.ExitCode=RecordingCheck.Run(args.Skip(1))==0 ? 0 : 1; return; }
var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".bnlreplay");
var readyPath = path + ".waiting";
var worldPath = path + ".world";
// A kept (stateful) world packet; effect-only packets are dropped from catch-up.
byte[] Marker=[3,6,75,99];
byte[] Big(int i) { using var w=new BinaryWriter(new MemoryStream());w.Write((byte)6);w.Write((byte)75);w.Write(i);w.Write(new byte[296]);return RecordingSender.Frame(w); }
bool Holds(byte[] hay,byte[] needle) => hay.AsSpan().IndexOf(needle)>=0;
// Zone packets inside sent chunks, as (message, body after the message id).
IEnumerable<(byte Message,byte[] Body)> Frames(IEnumerable<byte[]> packets) {
    foreach(var packet in packets) {
        using var r=new BinaryReader(new MemoryStream(packet));
        while(r.BaseStream.Position<r.BaseStream.Length) {
            int length=r.Read7BitEncodedInt();var body=r.ReadBytes(length);
            if(length>=2 && body[0]==6) yield return (body[1],body[2..]);
        }
    }
}
int checks=0;
var live=new List<ReplayInstance>();
ReplayInstance Track(ReplayInstance replay){live.Add(replay);return replay;}
void Check(bool ok,string name) { if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
byte[] Phase(ZonePhaseType phase) {
    using var w=new BinaryWriter(new MemoryStream());w.Write((byte)6);w.Write((byte)9);
    new ZoneUpdate { Phase=new ZonePhase {PhaseType=phase} }.Write(w);
    return RecordingSender.Frame(w);
}
async Task Until(Func<bool> ready) {
    var timeout=DateTime.UtcNow.AddSeconds(5);
    while(!ready() && DateTime.UtcNow<timeout)await Task.Delay(10);
    Check(ready(),"asynchronous operation completes");
}
try {
    using(var file=File.Create(path))
    using(var zipped=new BrotliStream(file,CompressionLevel.Fastest))
    using(var w=new BinaryWriter(zipped)) {
        w.Write(MatchRecorder.Magic);w.Write(MatchRecorder.FormatVersion);
        var meta=Encoding.UTF8.GetBytes("{\"mode_key\":1,\"match_key\":2,\"server_time_at_start\":100000}");
        w.Write(meta.Length);w.Write(meta);
        void Record(byte kind,uint at,byte[] data){w.Write(kind);w.Write(at);w.Write(data.Length);w.Write(data);}
        Record(1,0,[2,6,0]);
        Record(2,0,Phase(ZonePhaseType.Build));
        Record(3,100,Marker);
        for(int i=0;i<1500;i++) Record(3,200,Big(i));
        Record(3,1000,Phase(ZonePhaseType.Assault));
        Record(3,1500,[2,6,71]);
        Record(3,2000,Phase(ZonePhaseType.Build2));
        Record(3,3000,Phase(ZonePhaseType.Assault2));
        Record(255,4000,Encoding.UTF8.GetBytes("{}"));
    }
    var timeline=new ReplayTimeline(path);
    Check(timeline.DurationMs==4000 && timeline.NextCombat(0)==1000,"existing packets provide duration and first skip-build boundary");
    Check(timeline.NextCombat(1200)==-1 && timeline.NextCombat(2100)==3000,"skip build respects both phases and is unavailable in combat");
    var host=new Host();var replay=Track(new ReplayInstance(host,path,"fixture",7));
    replay.EnableControls(7);replay.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    replay.Control(7,1,0);
    var paused=host.State;
    int sent=host.Packets.Count;
    await Task.Delay(180);
    Check(host.State.Paused && host.State.Position==paused.Position,"pause freezes recorded position");
    Check(!host.Packets.Skip(sent).Any(p=>Holds(p,Marker)),"pause prevents later world packets");
    replay.Control(8,2,0);
    Check(host.State.Paused,"another account cannot resume this replay");
    replay.Control(7,4,0);
    Check(host.State.Seeking && host.Scenes.IsEmpty,"skip build catches up in place without reloading the scene");
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==1000 && host.State.Paused,"skip build reaches combat and preserves pause");
    Check(host.Packets.Any(p=>Holds(p,Marker)),"seeking replays earlier world changes instead of discarding them");
    replay.Control(7,3,-10000);
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==0 && host.State.Paused,"rewind clamps to start and preserves pause");
    replay.Control(7,3,2200);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==2200 && host.State.NextCombat==3000,"forward seeking reconstructs second build phase");
    replay.Control(7,4,0);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==3000,"skip-build also handles the second build phase");
    replay.Control(7,2,0);
    await Until(()=>host.State.Position==4000 && host.State.Paused);
    Check(!replay.IsOver(),"end remains available for rewind");
    replay.Control(7,2,0);
    Check(host.State.Paused,"resume at end does not advertise playback with no remaining frames");
    replay.Control(7,3,-1000);
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==3000,"rewind works after replay reaches its end");
    replay.PlayerLeftInstance(7,KickReason.MatchQuit);
    int count=host.Packets.Count;await Task.Delay(80);
    Check(replay.IsOver() && host.Ends==1 && host.Packets.Count==count,"leaving cancels streaming and cleans up once");
    replay.PlayerDisconnected(7);Check(host.Ends==1,"disconnect after leave is idempotent");
    var ackHost=new Host();var ackReplay=Track(new ReplayInstance(ackHost,path,"ack",7));
    ackReplay.EnableControls(7,true);ackReplay.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
    ackReplay.PlayerEnterScene(7);ackReplay.PlayerZoneReady(7);ackReplay.Control(7,1,0);
    int beforeSeek=ackHost.Packets.Count;
    ackReplay.Control(7,3,2200);
    await Until(()=>ackHost.Barrier>=8);
    int barrier=ackHost.Barrier;
    await Task.Delay(80);
    Check(ackHost.State.Seeking,"seek remains pending until the client applies catch-up packets");
    Check(ackHost.Packets.Count-beforeSeek<=8,"large catch-up stops at eight unacknowledged chunks");
    int blockedCount=ackHost.Packets.Count;
    await Task.Delay(80);
    Check(ackHost.Packets.Count==blockedCount,"slow client cannot accumulate an unbounded catch-up queue");
    ackReplay.Control(8,5,barrier);ackReplay.Control(7,5,barrier-1);
    await Task.Delay(80);
    Check(ackHost.State.Seeking,"another viewer and stale acknowledgements cannot release catch-up");
    await Until(()=>{ ackReplay.Control(7,5,ackHost.Barrier); return !ackHost.State.Seeking; });
    barrier=ackHost.Barrier;
    Check(ackHost.State.Position==2200 && ackHost.State.Paused,"acknowledged seek completes at target with pause intent");
    ackReplay.Control(7,3,-2200);ackReplay.PlayerEnterScene(7);ackReplay.PlayerZoneReady(7);
    await Until(()=>ackHost.Barrier>barrier);
    ackReplay.Control(7,5,barrier);
    await Task.Delay(80);
    Check(ackHost.State.Seeking,"previous seek acknowledgement cannot complete a new seek");
    ackReplay.Control(7,5,ackHost.Barrier);
    await Until(()=>!ackHost.State.Seeking);
    ackReplay.Control(7,2,0);
    await Until(()=>ackHost.Packets.Any(p=>p.SequenceEqual(Marker)) && ackHost.State.Position>0);
    Check(!ackHost.State.Paused,"playback resumes after acknowledged rewind");
    ackReplay.Control(7,3,1000);
    await Until(()=>ackHost.State.Seeking);
    ackReplay.PlayerLeftInstance(7,KickReason.MatchQuit);
    Check(ackReplay.IsOver() && ackHost.Ends==1,"leaving during pending catch-up cancels cleanly");
    using(var original=new ReplayReader(path))
    using(var file=File.Create(readyPath))
    using(var zipped=new BrotliStream(file,CompressionLevel.Fastest))
    using(var writer=new BinaryWriter(zipped))
    {
        writer.Write(MatchRecorder.Magic);writer.Write(MatchRecorder.FormatVersion);
        var metadata=Encoding.UTF8.GetBytes(original.Metadata.RootElement.GetRawText());
        writer.Write(metadata.Length);writer.Write(metadata);
        void Record(byte kind,uint at,byte[] data) {writer.Write(kind);writer.Write(at);writer.Write(data.Length);writer.Write(data);}
        while(original.Next() is {} record)
        {
            Record(record.Kind,record.OffsetMs,record.Kind==2?Phase(ZonePhaseType.Waiting):record.Data);
            if(record.Kind==2) Record(3,50,Phase(ZonePhaseType.Build));
        }
    }
    var readyHost=new Host();var readyReplay=Track(new ReplayInstance(readyHost,readyPath,"ready",7));
    readyReplay.EnableControls(7,true,true);readyReplay.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
    readyReplay.PlayerEnterScene(7);readyReplay.PlayerZoneReady(7);
    await Until(()=>readyHost.Barrier<0);
    long loadingPosition=readyHost.State.Position;
    await Task.Delay(250);
    Check(readyHost.State.Seeking && readyHost.State.Position==loadingPosition,"initial playback clock stays stopped until spectator presentation is ready");
    readyReplay.Control(7,5,readyHost.Barrier);
    await Until(()=>!readyHost.State.Seeking);
    Check(readyHost.State.Position==50,"initial ready acknowledgement begins at first playable build transition, not the waiting snapshot");
    readyReplay.PlayerLeftInstance(7,KickReason.MatchQuit);
    var oldHost=new Host();var old=Track(new ReplayInstance(oldHost,path,"old",7));
    old.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());old.PlayerZoneReady(7);old.Control(7,1,0);
    Check(!oldHost.HasState,"old clients receive no unsupported replay control messages");
    old.PlayerLeftInstance(7,KickReason.MatchQuit);
    // Catch-up folding keeps the client's final state while dropping history.
    {
        byte[] Zone(byte message,Action<BinaryWriter> body){ using var w=new BinaryWriter(new MemoryStream());w.Write((byte)6);w.Write(message);body(w);return RecordingSender.Frame(w); }
        byte[] Update(uint id,UnitUpdate u)=>Zone(11,w=>{w.Write(id);UnitUpdate.WriteRecord(w,u);});
        byte[] Blocks(Vector3s at,BlockUpdate b)=>Zone(8,w=>{w.Write7BitEncodedInt(1);w.Write(at.x);w.Write(at.y);w.Write(at.z);BlockUpdate.WriteRecord(w,b);});
        var fold=new ReplayCatchUp();
        foreach(var frame in new[]{
            Update(5,new UnitUpdate{Health=10,Buffs=new(){[BuffType.ResourceBonus]=1f}}),
            Zone(19,w=>w.Write(new byte[16])),
            Update(5,new UnitUpdate{Health=7,Shield=3}),
            Update(6,new UnitUpdate{Health=1}),
            Zone(10,w=>w.Write(9u)),Update(9,new UnitUpdate{Health=4}),Zone(20,w=>w.Write(9u)),Zone(12,w=>w.Write(9u)),
            Zone(12,w=>w.Write(6u)),
            Zone(30,w=>{w.Write(4u);w.Write(new byte[8]);}),Zone(30,w=>{w.Write(4u);w.Write(new byte[8]);}),Zone(31,w=>w.Write(4u)),
            Zone(30,w=>{w.Write(3u);w.Write(new byte[8]);}),
            Blocks(new Vector3s(1,1,1),new BlockUpdate{Id=3}),Blocks(new Vector3s(1,1,1),new BlockUpdate{Id=0,Vdata=1}),
            Zone(9,w=>new ZoneUpdate{Phase=new ZonePhase{PhaseType=ZonePhaseType.Build}}.Write(w)),
            Zone(9,w=>new ZoneUpdate{Phase=new ZonePhase{PhaseType=ZonePhaseType.Assault},ResourceCap=5}.Write(w)),
        }) fold.Add(frame);
        var output=Frames(fold.Build()).ToList();
        var unitUpdates=output.Where(f=>f.Message==11).Select(f=>(Id:BitConverter.ToUInt32(f.Body,0),Update:UnitUpdate.ReadRecord(new BinaryReader(new MemoryStream(f.Body,4,f.Body.Length-4))))).ToList();
        Check(unitUpdates.Count==1 && unitUpdates[0].Id==5 && unitUpdates[0].Update.Health==7 && unitUpdates[0].Update.Shield==3 &&
            unitUpdates[0].Update.Buffs?[BuffType.ResourceBonus]==1f,"unit updates fold to one with the client's last-write state");
        Check(!output.Any(f=>f.Message is 10 or 20 || f.Message==12 && BitConverter.ToUInt32(f.Body,0)==9),"a unit created and dropped inside the window is elided with its packets");
        Check(output.Any(f=>f.Message==12 && BitConverter.ToUInt32(f.Body,0)==6),"an earlier unit's drop is kept and its pending state is not reapplied");
        Check(!output.Any(f=>f.Message==19),"effect-only packets are skipped");
        var builds=output.Where(f=>f.Message is 30 or 31).ToList();
        Check(builds.Count==2 && builds[0].Message==31 && BitConverter.ToUInt32(builds[0].Body,0)==4 && builds[1].Message==30 && BitConverter.ToUInt32(builds[1].Body,0)==3,
            "only each builder's newest build event survives, so ghosts are neither leaked nor replayed");
        var block=output.Single(f=>f.Message==8);
        using(var r=new BinaryReader(new MemoryStream(block.Body))){ r.Read7BitEncodedInt();r.ReadInt16();r.ReadInt16();r.ReadInt16();var b=BlockUpdate.ReadRecord(r);
            Check(b.Id==0 && b.Vdata==0,"a destroyed block folds to settled air without replaying its destruction"); }
        var zone=output.Where(f=>f.Message==9).Select(f=>ZoneUpdate.ReadRecord(new BinaryReader(new MemoryStream(f.Body)))).ToList();
        Check(zone.Count==1 && zone[0].Phase?.PhaseType==ZonePhaseType.Assault && zone[0].ResourceCap==5,"zone updates fold to the final phase and fields");
        Check(output.FindIndex(f=>f.Message==8)<output.FindIndex(f=>f.Message==9) && output.FindIndex(f=>f.Message==9)<output.FindIndex(f=>f.Message==12),
            "map and zone state precede the kept unit packets, as for a joining spectator");
    }
    // Capability 12: rewind rebuilds the state inside the loaded scene.
    {
        byte[] Zone(byte message,Action<BinaryWriter> body){ using var w=new BinaryWriter(new MemoryStream());w.Write((byte)6);w.Write(message);body(w);return RecordingSender.Frame(w); }
        byte[] Blocks(Vector3s at,BlockUpdate b)=>Zone(8,w=>{w.Write7BitEncodedInt(1);w.Write(at.x);w.Write(at.y);w.Write(at.z);BlockUpdate.WriteRecord(w,b);});
        var map=new MemoryStream();
        using(var mw=new BinaryWriter(map,Encoding.UTF8,true)){ mw.Write((ushort)2);mw.Write((ushort)2);mw.Write((ushort)2);
            for(int i=0;i<8;i++){mw.Write((ushort)5);mw.Write((byte)0);mw.Write((ushort)0);mw.Write((byte)0);} }
        var initZone=Zone(0,w=>new ZoneInitData{MapData=BNLReloadedServer.ProtocolHelpers.ZLibHelper.Zip(map.ToArray(),3).ToArray()}.Write(w));
        using(var file=File.Create(worldPath))
        using(var zipped=new BrotliStream(file,CompressionLevel.Fastest))
        using(var w=new BinaryWriter(zipped)) {
            w.Write(MatchRecorder.Magic);w.Write(MatchRecorder.FormatVersion);
            var meta=Encoding.UTF8.GetBytes("{\"mode_key\":1,\"match_key\":2,\"server_time_at_start\":100000}");
            w.Write(meta.Length);w.Write(meta);
            void Record(byte kind,uint at,byte[] data){w.Write(kind);w.Write(at);w.Write(data.Length);w.Write(data);}
            Record(1,0,initZone);
            Record(2,0,[..Phase(ZonePhaseType.Build),..Zone(10,x=>x.Write(50u))]);
            Record(3,100,Blocks(new Vector3s(0,0,0),new BlockUpdate{Id=7}));
            Record(3,200,Zone(10,x=>x.Write(60u)));
            Record(3,300,Blocks(new Vector3s(1,1,1),new BlockUpdate{Id=0,Vdata=1}));
            Record(3,1000,Phase(ZonePhaseType.Assault));
            Record(3,1800,Zone(3,x=>x.Write((byte)1)));
            Record(255,2000,Encoding.UTF8.GetBytes("{}"));
        }
        var worldHost=new Host();var world=Track(new ReplayInstance(worldHost,worldPath,"world",7));
        worldHost.OnBarrier=sequence=>{ _=Task.Run(()=>world.Control(7,5,sequence)); };
        world.EnableControls(7,true,true,true);world.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
        world.PlayerEnterScene(7);world.PlayerZoneReady(7);
        await Until(()=>!worldHost.State.Seeking && worldHost.HasState);
        world.Control(7,1,0);
        await Until(()=>worldHost.State.Paused);
        long start=worldHost.State.Position; // seeks are relative to where the pause landed
        world.Control(7,3,1500);
        await Until(()=>!worldHost.State.Seeking && worldHost.State.Position==start+1500);
        int mark=worldHost.Packets.Count;
        world.Control(7,3,-1300);
        await Until(()=>!worldHost.State.Seeking);
        var rebuilt=Frames(worldHost.Packets.Skip(mark).ToList()).ToList();
        Check(worldHost.Scenes.IsEmpty && worldHost.State.Position==start+200 && worldHost.State.Paused,"rewind rebuilds in the loaded scene at the target with pause intent");
        int Drop(uint id)=>rebuilt.FindIndex(f=>f.Message==12 && BitConverter.ToUInt32(f.Body,0)==id);
        int Create(uint id)=>rebuilt.FindIndex(f=>f.Message==10 && BitConverter.ToUInt32(f.Body,0)==id);
        Check(Drop(50)>=0 && Drop(60)>=0 && Create(50)>Drop(50) && Create(60)>Drop(60),"rewind removes the client's units before recreating the target's");
        var restored=new Dictionary<Vector3s,BlockUpdate>();
        foreach(var f in rebuilt.Where(f=>f.Message==8)) using(var r=new BinaryReader(new MemoryStream(f.Body))){ int n=r.Read7BitEncodedInt();
            for(int i=0;i<n;i++){ var at=new Vector3s(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());restored[at]=BlockUpdate.ReadRecord(r);} }
        Check(restored.TryGetValue(new Vector3s(0,0,0),out var kept) && kept.Id==7 && restored.TryGetValue(new Vector3s(1,1,1),out var original) && original.Id==5,
            "blocks changed after the target return to their keyframe value; earlier changes stay");
        Check(!rebuilt.Any(f=>f.Message==9 && ZoneUpdate.ReadRecord(new BinaryReader(new MemoryStream(f.Body))).Phase?.PhaseType==ZonePhaseType.Assault),"rewound zone phase is the target's");
        world.Control(7,3,1700);
        await Until(()=>!worldHost.State.Seeking && worldHost.State.Position==start+1900);
        world.Control(7,3,-1500);
        await Until(()=>!worldHost.Scenes.IsEmpty);
        Check(worldHost.State.Seeking,"after the match end, rewind falls back to a scene reload");
        world.PlayerLeftInstance(7,KickReason.MatchQuit);
    }
    // Capability 12 at the end of a recording: playback holds at the final
    // frame but must still serve an in-place rewind.
    {
        var endHost=new Host();var atEnd=Track(new ReplayInstance(endHost,path,"end",7));
        endHost.OnBarrier=sequence=>{ _=Task.Run(()=>atEnd.Control(7,5,sequence)); };
        atEnd.EnableControls(7,true,true,true);atEnd.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
        atEnd.PlayerEnterScene(7);atEnd.PlayerZoneReady(7);
        await Until(()=>!endHost.State.Seeking && endHost.HasState);
        atEnd.Control(7,3,60000);
        await Until(()=>endHost.State.Position==4000 && endHost.State.Paused && !endHost.State.Seeking);
        atEnd.Control(7,3,-1000);
        await Until(()=>!endHost.State.Seeking && endHost.State.Position==3000);
        Check(endHost.Scenes.IsEmpty && endHost.State.Paused,"in-place rewind from the final frame completes without a reload");
        atEnd.PlayerLeftInstance(7,KickReason.MatchQuit);
    }
    if(args.Contains("--seek-window-benchmark"))
    {
        foreach(int window in new[]{1,8})
        {
            var measurements=new List<long>();
            for(int sample=0;sample<3;sample++)
            {
                var measuredHost=new Host();var measured=Track(new ReplayInstance(measuredHost,path,"benchmark",7,window));
                measured.EnableControls(7,true);measured.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
                measured.PlayerEnterScene(7);measured.PlayerZoneReady(7);measured.Control(7,1,0);
                measuredHost.OnBarrier = sequence => { _ = Task.Run(async()=> { await Task.Delay(100); measured.Control(7,5,sequence); }); };
                var timer=System.Diagnostics.Stopwatch.StartNew();measured.Control(7,3,2200);
                await Until(()=>!measuredHost.State.Seeking);
                timer.Stop();measurements.Add(timer.ElapsedMilliseconds);
                Check(measuredHost.State.Position==2200,"benchmark reaches identical target");
                measured.PlayerLeftInstance(7,KickReason.MatchQuit);
            }
            Console.WriteLine($"SEEK_WINDOW_BENCHMARK records=1500 ack_delay_ms=100 window={window} samples_ms={string.Join(',',measurements)} median_ms={measurements.Order().ElementAt(1)}");
        }
    }
    Console.WriteLine($"REPLAY_CONTROLS_OK checks={checks}");
} finally {
    // A failed check must not leave playback holding the recordings open.
    foreach(var replay in live) replay.PlayerLeftInstance(7,KickReason.MatchQuit);
    // Cancellation is cooperative; let the playback reader release its file handle.
    for(int attempt=0;;attempt++) {
        try { File.Delete(path);File.Delete(readyPath);File.Delete(worldPath);break; }
        catch(IOException) when(attempt<40) { await Task.Delay(50); }
    }
}

record State(long Origin,long Position,long Duration,bool Paused,bool Seeking,long NextCombat);
sealed class Host:IReplayHost {
    public readonly ConcurrentQueue<byte[]> Packets=new();
    public readonly ConcurrentQueue<SceneZone> Scenes=new();
    public State State=new(0,0,0,false,false,-1);
    public bool HasState;
    public int Ends;
    public volatile int Barrier;
    public Action<int>? OnBarrier;
    public void UpdateScene(uint player,Scene scene)=>Scenes.Enqueue((SceneZone)scene);
    public void SetReplayClock(uint player,long? offset){}
    public void EndReplay(uint player,string instance)=>Interlocked.Increment(ref Ends);
    public void SendToSession(Guid session,byte[] bytes){
        using var r=new BinaryReader(new MemoryStream(bytes));
        int length=r.Read7BitEncodedInt();
        int service=r.ReadByte(), message=r.ReadByte();
        if(service==6 && message==113){Barrier=r.ReadInt32();OnBarrier?.Invoke(Barrier);return;}
        if(length>=2 && service==6 && message==111){
            State=new(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadBoolean(),r.ReadBoolean(),r.ReadInt64());HasState=true;
        } else Packets.Enqueue(bytes);
    }
}
