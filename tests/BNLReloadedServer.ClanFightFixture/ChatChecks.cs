using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Clans;
using BNLReloadedServer.Database;
using BNLReloadedServer.Servers;
using BNLReloadedServer.Service;
using SQLite;

static class ChatChecks
{
    public static void Run(Action<bool,string> check)
    {
        var store=new ClanStore(new SQLiteAsyncConnection(Databases.PlayerDatabaseFile),new AllowNames());
        store.Load().GetAwaiter().GetResult();
        var clan=store.Create(1,"Chat Alpha","CA").GetAwaiter().GetResult();
        store.Invite(1,2).GetAwaiter().GetResult(); store.Accept(2,clan.ClanId).GetAwaiter().GetResult();
        store.Create(3,"Chat Beta","CB").GetAwaiter().GetResult();
        typeof(ClanHub).GetField("_store",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,store);
        using (var initial = JsonDocument.Parse(ClanRatings.Leaderboard()))
        {
            var row = initial.RootElement.GetProperty("rows").EnumerateArray().First(r=>r.GetProperty("id").GetInt32()==clan.ClanId);
            check(row.GetProperty("totalMatches").GetInt32()==0 && row.GetProperty("winRate").GetDouble()==0,"unplayed leaderboard has zero matches and win rate");
        }
        int opponent=store.ClanOf(3)!.Id;
        ClanRatings.Record("leaderboard-1",clan.ClanId,opponent,TeamType.Team1);
        ClanRatings.Record("leaderboard-2",clan.ClanId,opponent,TeamType.Team1);
        ClanRatings.Record("leaderboard-3",clan.ClanId,opponent,TeamType.Team2);
        using (var results = JsonDocument.Parse(ClanRatings.Leaderboard()))
        {
            var row = results.RootElement.GetProperty("rows").EnumerateArray().First(r=>r.GetProperty("id").GetInt32()==clan.ClanId);
            check(row.GetProperty("totalMatches").GetInt32()==3 && row.GetProperty("wins").GetInt32()==2 && row.GetProperty("losses").GetInt32()==1,"leaderboard reports matches wins and losses");
            check(Math.Abs(row.GetProperty("winRate").GetDouble()-200.0/3)<.001,"leaderboard win rate uses wins divided by matches");
        }
        var type=typeof(RegionServerDatabase);
        var region=(RegionServerDatabase)RuntimeHelpers.GetUninitializedObject(type);
        var infoType=type.GetNestedType("ConnectionInfo",BindingFlags.NonPublic)!;
        var users=(IDictionary)Activator.CreateInstance(typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(uint),infoType))!;
        var services=new ConcurrentDictionary<Guid,Dictionary<ServiceId,IService>>();
        type.GetField("_connectedUsers",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(region,users);
        type.GetField("_services",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(region,services);
        var senders=new Dictionary<uint,Capture>();
        for(uint id=1;id<=4;id++)
        {
            var guid=Guid.NewGuid();
            var info=Activator.CreateInstance(infoType,guid,new ChatPlayer { PlayerId=id,Nickname="Player "+id },false)!;
            // Clanmates deliberately occupy different scenes: chat must not depend on their match or menu.
            infoType.GetProperty("ActiveScene")!.SetValue(info,id==1?new SceneMainMenu():new SceneLobby());
            users.Add(id,info);
            var sender=new Capture { AssociatedPlayerId=id }; senders[id]=sender;
            var service=new ServiceClan(sender);
            typeof(ServiceClan).GetProperty("SupportsClans")!.SetValue(service,true);
            typeof(ServiceClan).GetProperty("SupportsCompetition")!.SetValue(service,true);
            services[guid]=new() { [ServiceId.ServiceClan]=service };
        }
        void ResetThrottle() => infoType.GetField("LastGlobalMessage")!.SetValue(users[1u],0L);
        check(region.SendClanChat(1,"Hello clan"),"clan chat accepted");
        check(senders[1].Packets.Count==1 && senders[2].Packets.Count==1,"chat delivered across menu and lobby");
        check(senders[3].Packets.Count==0 && senders[4].Packets.Count==0,"other clans and clanless players cannot receive chat");
        using(var reader=new BinaryReader(new MemoryStream(senders[2].Packets.Single())))
        {
            check(reader.ReadByte()==16 && reader.ReadByte()==6,"chat uses capability-gated region extension");
            var json=JsonDocument.Parse(reader.ReadString());
            check(json.RootElement.GetProperty("clanId").GetInt32()==clan.ClanId && json.RootElement.GetProperty("message").GetString()=="Hello clan","chat wire payload preserves clan and text");
        }
        check(!region.SendClanChat(1,"Too soon"),"chat rate limit enforced");
        check(!region.SendClanChat(4,"No clan"),"clanless sender rejected");
        ResetThrottle(); check(!region.SendClanChat(1,new string('x',501)),"oversized chat rejected");
        var ignored=(ConcurrentDictionary<uint,byte>)infoType.GetProperty("Ignored")!.GetValue(users[2u])!; ignored[1]=0;
        check(region.SendClanChat(1,"Muted"),"sender can speak when recipient muted them");
        check(senders[2].Packets.Count==1,"recipient mute is respected");
        ignored.Clear(); store.Leave(2).GetAwaiter().GetResult(); ResetThrottle(); region.SendClanChat(1,"After leave");
        check(senders[2].Packets.Count==1,"former member immediately stops receiving chat");
        var legacy=new Capture(); var legacyService=new ServiceClan(legacy);
        typeof(ServiceClan).GetProperty("SupportsClans")!.SetValue(legacyService,true);
        legacyService.SendExtension(6,"{}");
        check(legacy.Packets.Count==0,"version-one clan clients receive no extension packets");
    }
    private sealed class AllowNames : IOffensiveText { public bool IsOffensive(string text)=>false; }
    private sealed class Capture : ISender
    {
        public uint? AssociatedPlayerId { get; set; }
        public int SenderCount=>1;
        public List<byte[]> Packets { get; }=[];
        public void Send(BinaryWriter writer)=>Packets.Add(((MemoryStream)writer.BaseStream).ToArray());
        public void Send(byte[] buffer)=>Packets.Add(buffer);
        public void SendExcept(BinaryWriter writer,List<Guid> excluded)=>Send(writer);
        public void Subscribe(Guid id) { } public void Unsubscribe(Guid id) { } public void UnsubscribeAll() { }
    }
}
