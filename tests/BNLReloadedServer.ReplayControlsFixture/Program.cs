using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.ServerTypes;

var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".bnlreplay");
var readyPath = path + ".waiting";
int checks=0;
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
        Record(3,100,[2,6,70]);
        for(int i=0;i<1500;i++) Record(3,200,[2,6,72]);
        Record(3,1000,Phase(ZonePhaseType.Assault));
        Record(3,1500,[2,6,71]);
        Record(3,2000,Phase(ZonePhaseType.Build2));
        Record(3,3000,Phase(ZonePhaseType.Assault2));
        Record(255,4000,Encoding.UTF8.GetBytes("{}"));
    }
    var timeline=new ReplayTimeline(path);
    Check(timeline.DurationMs==4000 && timeline.NextCombat(0)==1000,"existing packets provide duration and first skip-build boundary");
    Check(timeline.NextCombat(1200)==-1 && timeline.NextCombat(2100)==3000,"skip build respects both phases and is unavailable in combat");
    var host=new Host();var replay=new ReplayInstance(host,path,"fixture",7);
    replay.EnableControls(7);replay.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    replay.Control(7,1,0);
    var paused=host.State;
    int sent=host.Packets.Count;
    await Task.Delay(180);
    Check(host.State.Paused && host.State.Position==paused.Position,"pause freezes recorded position");
    Check(!host.Packets.Skip(sent).Any(p=>p.SequenceEqual(new byte[]{2,6,70})),"pause prevents later world packets");
    replay.Control(8,2,0);
    Check(host.State.Paused,"another account cannot resume this replay");
    replay.Control(7,4,0);
    Check(host.State.Seeking && host.Scenes.IsEmpty,"skip build catches up in place without reloading the scene");
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==1000 && host.State.Paused,"skip build reaches combat and preserves pause");
    Check(host.Packets.Any(p=>p.SequenceEqual(new byte[]{2,6,70})),"seeking replays earlier world changes instead of discarding them");
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
    var ackHost=new Host();var ackReplay=new ReplayInstance(ackHost,path,"ack",7);
    ackReplay.EnableControls(7,true);ackReplay.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());
    ackReplay.PlayerEnterScene(7);ackReplay.PlayerZoneReady(7);ackReplay.Control(7,1,0);
    ackReplay.Control(7,3,2200);
    await Until(()=>ackHost.Barrier>=8);
    int barrier=ackHost.Barrier;
    await Task.Delay(80);
    Check(ackHost.State.Seeking,"seek remains pending until the client applies catch-up packets");
    Check(ackHost.Packets.Count<=1026,"large catch-up stops at eight 128-record batches until acknowledged");
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
    await Until(()=>ackHost.Packets.Any(p=>p.SequenceEqual(new byte[]{2,6,70})) && ackHost.State.Position>0);
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
    var readyHost=new Host();var readyReplay=new ReplayInstance(readyHost,readyPath,"ready",7);
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
    var oldHost=new Host();var old=new ReplayInstance(oldHost,path,"old",7);
    old.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());old.PlayerZoneReady(7);old.Control(7,1,0);
    Check(!oldHost.HasState,"old clients receive no unsupported replay control messages");
    old.PlayerLeftInstance(7,KickReason.MatchQuit);
    if(args.Contains("--seek-window-benchmark"))
    {
        foreach(int window in new[]{1,8})
        {
            var measurements=new List<long>();
            for(int sample=0;sample<3;sample++)
            {
                var measuredHost=new Host();var measured=new ReplayInstance(measuredHost,path,"benchmark",7,window);
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
    // Cancellation is cooperative; let the playback reader release its file handle.
    for(int attempt=0;;attempt++) {
        try { File.Delete(path);File.Delete(readyPath);break; }
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
