using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.ServerTypes;

var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".bnlreplay");
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
    Check(host.State.Seeking && host.Scenes.Last().Restart,"skip build requests a fresh spectator scene");
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==1000 && host.State.Paused,"skip build reaches combat and preserves pause");
    Check(host.Packets.Any(p=>p.SequenceEqual(new byte[]{2,6,70})),"seeking replays earlier world changes instead of discarding them");
    replay.Control(7,3,-10000);
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==0 && host.State.Paused,"rewind clamps to start and preserves pause");
    replay.Control(7,3,2200);
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
    await Until(()=>!host.State.Seeking);
    Check(host.State.Position==2200 && host.State.NextCombat==3000,"forward seeking reconstructs second build phase");
    replay.Control(7,4,0);
    replay.PlayerEnterScene(7);replay.PlayerZoneReady(7);
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
    var oldHost=new Host();var old=new ReplayInstance(oldHost,path,"old",7);
    old.LinkGuidToPlayer(7,Guid.NewGuid(),Guid.NewGuid());old.PlayerZoneReady(7);old.Control(7,1,0);
    Check(!oldHost.HasState,"old clients receive no unsupported replay control messages");
    old.PlayerLeftInstance(7,KickReason.MatchQuit);
    Console.WriteLine($"REPLAY_CONTROLS_OK checks={checks}");
} finally {
    // Cancellation is cooperative; let the playback reader release its file handle.
    for(int attempt=0;;attempt++) {
        try { File.Delete(path);break; }
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
    public void UpdateScene(uint player,Scene scene)=>Scenes.Enqueue((SceneZone)scene);
    public void SetReplayClock(uint player,long? offset){}
    public void EndReplay(uint player,string instance)=>Interlocked.Increment(ref Ends);
    public void SendToSession(Guid session,byte[] bytes){
        using var r=new BinaryReader(new MemoryStream(bytes));
        int length=r.Read7BitEncodedInt();
        if(length>=2 && r.ReadByte()==6 && r.ReadByte()==111){
            State=new(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadBoolean(),r.ReadBoolean(),r.ReadInt64());HasState=true;
        } else Packets.Enqueue(bytes);
    }
}
