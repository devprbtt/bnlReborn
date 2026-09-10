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
            w.WriteMap(Enum.GetValues<PlayerMatchStatType>().ToDictionary(k=>k,k=>k==PlayerMatchStatType.Kill?10:k==PlayerMatchStatType.Death?4:k==PlayerMatchStatType.Assist?2:0),w.WriteByteEnum,w.Write);
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
                for(int i=0;i<14;i++)
                {
                    var id="round-"+i;
                    db.Insert(new ArchivedMatchRecord{Id=id,StartedAt=i==0?1:1000,EndedAt=101000,Winner=1});
                    db.Insert(new ArchivedMatchPlayerRecord{MatchId=id,PlayerId=3,IsWinner=i%2==0,Stats=Stats(),RawStats=i==0?[]:Raw()});
                    db.Insert(new ArchivedMatchPresenceRecord{MatchId=id,PlayerId=3,HeroKey=123,Sequence=0,JoinedAt=1000,LeftAt=51000});
                    db.Insert(new ArchivedMatchPresenceRecord{MatchId=id,PlayerId=3,HeroKey=i==13?456:123,Sequence=1,JoinedAt=61000,LeftAt=101000});
                }
                var stats=LifetimeProfileStats.Read(db,3);
                Check(stats.Overall.Matches==12&&stats.Overall.Kills==120,"lifetime includes more than ten matches");
                Check(stats.Overall.Seconds==1080,"disconnected intervals excluded from play time");
                Check(stats.Overall.DetailedMatches==12&&stats.Overall.DetailedSeconds==1080,"all counted matches have complete headshot and DPS coverage");
                Check(stats.Overall.Damage==1440,"critical damage not counted twice");
                Check(stats.Overall.HeadshotKills==36&&stats.Overall.DirectKills==96,"headshot kills and direct-kill denominator preserved");
                Check(Math.Abs(stats.Overall.Damage/stats.Overall.DetailedSeconds-4d/3)<.00001,"DPS divides total damage by matching played seconds");
                Check(stats.Heroes.Single().Combat.Matches==12&&stats.ExcludedMatches==2&&stats.UnattributedMatches==0,"missing raw data and mixed heroes excluded from overall and hero totals");
                Check(LifetimeProfileStats.Read(db,99).Overall.Matches==0,"player statistics are isolated");
                Check(stats.Overall.Wins==6&&stats.Overall.Losses==6&&stats.Overall.Wins+stats.Overall.Losses==stats.Overall.Matches,"career outcomes use exactly the same eligible matches");
                Check(stats.Version==2&&stats.Since==1000,"schema and tracked-since exclude old incomplete records");
                foreach(var bad in new[]{"no-core","bad-raw","no-hero","no-outcome","zero-time"}) {
                    db.Insert(new ArchivedMatchRecord{Id=bad,StartedAt=1000,EndedAt=bad=="zero-time"?1000:101000,Winner=bad=="no-outcome"?0:1});
                    db.Insert(new ArchivedMatchPlayerRecord{MatchId=bad,PlayerId=3,Stats=bad=="no-core"?[]:Stats(),RawStats=bad=="bad-raw"?new byte[]{255}:Raw()});
                    if(bad!="no-hero")db.Insert(new ArchivedMatchPresenceRecord{MatchId=bad,PlayerId=3,HeroKey=123,Sequence=0,JoinedAt=1000,LeftAt=101000});
                }
                var filtered=LifetimeProfileStats.Read(db,3);
                Check(filtered.Overall.Matches==12&&filtered.ExcludedMatches==7,"missing core data, corrupt raw data, missing hero, missing outcome and invalid time excluded");
                db.Insert(new ArchivedMatchPlayerRecord{MatchId="round-1",PlayerId=99,Stats=Stats(),RawStats=[]});
                db.Insert(new ArchivedMatchPresenceRecord{MatchId="round-1",PlayerId=99,HeroKey=123,Sequence=0,JoinedAt=1000,LeftAt=101000});
                Check(LifetimeProfileStats.Read(db,99).Overall.Matches==0&&LifetimeProfileStats.Read(db,99).ExcludedMatches==1,"the same exclusion policy applies to another player");

                db.InsertOrReplace(new ArchivedMatchRecord{Id="round-0",StartedAt=1,EndedAt=101000,Winner=1});
                Check(LifetimeProfileStats.Read(db,3).Overall.Matches==12,"archive replacement does not double-count matches");
                var sample=JsonSerializer.Serialize(stats,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                Check(sample.Contains("\"headshot_kills\":36")&&!sample.Contains("nickname"),"public projection exposes counters without personal fields");
                Directory.CreateDirectory("reports/profile-summary");File.WriteAllText("reports/profile-summary/lifetime-fixture.json",sample);
                Check(LifetimeProfileStats.PlayedSeconds(new ArchivedMatchRecord{StartedAt=1000,EndedAt=101000},new[]{new ArchivedMatchPresenceRecord{JoinedAt=0,LeftAt=71000},new ArchivedMatchPresenceRecord{JoinedAt=51000,LeftAt=201000}})==100,"overlaps merged and presence time clamped to round");
            }
            using(var reopened=new SQLiteConnection(path))
                Check(LifetimeProfileStats.Read(reopened,3).Overall.Kills==120,"lifetime counters survive database reopen");
        }
        finally {File.Delete(path);}
    }
}
