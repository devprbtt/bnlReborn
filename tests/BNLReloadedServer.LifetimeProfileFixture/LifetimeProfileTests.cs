using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using SQLite;

public static class LifetimeProfileTests
{
    static void Check(bool ok,string label) {if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
    static byte[] Stats()
    {
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            w.WriteMap(new Dictionary<PlayerMatchStatType,int>{{PlayerMatchStatType.Kill,10},{PlayerMatchStatType.Death,4},{PlayerMatchStatType.Assist,2}},w.WriteByteEnum,w.Write);
        return stream.ToArray();
    }
    static byte[] Raw()
    {
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            w.WriteMap(new Dictionary<ScoreType,float>{{ScoreType.KillPlayerCriticalByHero,3},{ScoreType.KillPlayerByHero,8},{ScoreType.DamagePlayerByHero,100},{ScoreType.DamagePlayerByBlock,20},{ScoreType.DamagePlayerCriticalByHero,50}},w.WriteByteEnum,w.Write);
        return stream.ToArray();
    }
    public static void Run()
    {
        var gate=typeof(BNLReloadedServer.ControlPanel.ControlPanelServer).GetMethod("IsPublicReadRequest",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        Check((bool)gate.Invoke(null,["GET","/api/profile-stats/3"])!,"lifetime profile GET is accessible without an admin session");
        Check(!(bool)gate.Invoke(null,["POST","/api/profile-stats/3"])!,"lifetime profile route does not permit public writes");
        Check(!(bool)gate.Invoke(null,["GET","/api/console"])!,"admin routes remain protected");
        var path=Path.Combine(Path.GetTempPath(),"bnl-lifetime-"+Guid.NewGuid()+".db");
        try
        {
            using(var db=new SQLiteConnection(path))
            {
                db.CreateTable<ArchivedMatchRecord>();db.CreateTable<ArchivedMatchPlayerRecord>();db.CreateTable<ArchivedMatchPresenceRecord>();
                for(int i=0;i<12;i++)
                {
                    var id="round-"+i;
                    db.Insert(new ArchivedMatchRecord{Id=id,StartedAt=1000,EndedAt=101000});
                    db.Insert(new ArchivedMatchPlayerRecord{MatchId=id,PlayerId=3,Stats=Stats(),RawStats=i==0?[]:Raw()});
                    db.Insert(new ArchivedMatchPresenceRecord{MatchId=id,PlayerId=3,HeroKey=123,Sequence=0,JoinedAt=1000,LeftAt=51000});
                    db.Insert(new ArchivedMatchPresenceRecord{MatchId=id,PlayerId=3,HeroKey=i==11?456:123,Sequence=1,JoinedAt=61000,LeftAt=101000});
                }
                var stats=LifetimeProfileStats.Read(db,3);
                Check(stats.Overall.Matches==12&&stats.Overall.Kills==120,"lifetime includes more than ten matches");
                Check(stats.Overall.Seconds==1080,"disconnected intervals excluded from play time");
                Check(stats.Overall.DetailedMatches==11&&stats.Overall.DetailedSeconds==990,"legacy gaps excluded from detailed DPS denominator");
                Check(stats.Overall.Damage==1320,"critical damage not counted twice");
                Check(stats.Overall.HeadshotKills==33&&stats.Overall.DirectKills==88,"headshot kills and direct-kill denominator preserved");
                Check(Math.Abs(stats.Overall.Damage/stats.Overall.DetailedSeconds-4d/3)<.00001,"DPS divides total damage by matching played seconds");
                Check(stats.Heroes.Single().Combat.Matches==11&&stats.UnattributedMatches==1,"mixed hero match counted overall without false attribution");
                Check(LifetimeProfileStats.Read(db,99).Overall.Matches==0,"player statistics are isolated");
                db.InsertOrReplace(new ArchivedMatchRecord{Id="round-0",StartedAt=1000,EndedAt=101000});
                Check(LifetimeProfileStats.Read(db,3).Overall.Matches==12,"archive replacement does not double-count matches");
                var sample=JsonSerializer.Serialize(stats,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                Check(sample.Contains("\"headshot_kills\":33")&&!sample.Contains("nickname"),"public projection exposes counters without personal fields");
                Directory.CreateDirectory("reports/profile-summary");File.WriteAllText("reports/profile-summary/lifetime-fixture.json",sample);
                Check(LifetimeProfileStats.PlayedSeconds(new ArchivedMatchRecord{StartedAt=1000,EndedAt=101000},new[]{new ArchivedMatchPresenceRecord{JoinedAt=0,LeftAt=71000},new ArchivedMatchPresenceRecord{JoinedAt=51000,LeftAt=201000}})==100,"overlaps merged and presence time clamped to round");
            }
            using(var reopened=new SQLiteConnection(path))
                Check(LifetimeProfileStats.Read(reopened,3).Overall.Kills==120,"lifetime counters survive database reopen");
        }
        finally {File.Delete(path);}
    }
}
