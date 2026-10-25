using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using Moserware.Skills;
using SQLite;

namespace BNLReloadedServer.Database;

[Table("MmrInbox")]
public sealed class MmrInbox
{
    [PrimaryKey] public string MatchId {get;set;}="";
    public byte[] Payload {get;set;}=[];
}

[Table("MmrMatches")]
public sealed class MmrMatchSettlement
{
    [PrimaryKey] public string MatchId {get;set;}="";
    public int Version {get;set;}
    public string References {get;set;}="";
    public byte[] Payload {get;set;}=[];
}

public static class MmrLedger
{
    public static void CreateTables(SQLiteConnection db)
    {
        db.CreateTable<MmrSettlement>(); db.CreateTable<MmrMatchSettlement>();
        db.CreateTable<MmrQueuePenalty>(); db.CreateTable<MmrInbox>();
    }
    public static byte[] Encode(CompletedMatchRecord match)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        match.Write(writer); return stream.ToArray();
    }
    public static CompletedMatchRecord Decode(byte[] bytes)
    {
        using var reader=new BinaryReader(new MemoryStream(bytes)); return CompletedMatchRecord.ReadRecord(reader);
    }
    // Called inside the same transaction as the archive. Never overwrite a newer user row
    // with an old serialized PlayerData object; only update rating columns here.
    public static List<MmrAward> Apply(SQLiteConnection db, CompletedMatchRecord match, long now,
        IReadOnlyList<MmrReference>? references=null)
    {
        if(match.RatingVersion!=PersonalMmr.Version || !PersonalMmr.Public(match.GameModeKey))return [];
        if(db.Find<MmrMatchSettlement>(match.MatchId)!=null)return [];
        if(db.Find<ArchivedMatchRecord>(match.MatchId)!=null)
            throw new InvalidOperationException("An existing legacy archive cannot be rated again");
        var ratings=new Dictionary<uint,Rating>();
        foreach(var p in match.Players)
        {
            var row=db.Find<PlayerRecord>(p.PlayerId)??throw new InvalidOperationException("Missing player in rating settlement");
            if(db.Table<MmrSettlement>().Where(s=>s.PlayerId==p.PlayerId).ToList().Any(s=>s.EndedAt>(long)match.StartedAt))
                throw new InvalidOperationException("Out-of-order match requires chronological correction");
            ratings[p.PlayerId]=new Rating(row.RatingMean,row.RatingDeviation);
        }
        var awards=PersonalMmr.Calculate(match,ratings,references);
        foreach(var a in awards)
        {
            var old=ratings[a.PlayerId];
            db.Insert(new MmrSettlement {Id=$"{match.MatchId}/{a.PlayerId}",MatchId=match.MatchId,PlayerId=a.PlayerId,
                EndedAt=(long)match.EndedAt,OldMean=old.Mean,NewMean=old.Mean+a.Delta,
                OldDeviation=old.StandardDeviation,NewDeviation=a.Deviation,Base=a.Base,Performance=a.Performance,
                Shortage=a.Shortage,Reason=a.Reason,Abandoned=a.Abandoned});
            db.Execute("UPDATE Users SET rating_mean=?, rating_dev=? WHERE id=?",old.Mean+a.Delta,a.Deviation,a.PlayerId);
            var player=match.Players.Single(p=>p.PlayerId==a.PlayerId);
            player.RatingDelta=a.Delta; player.RatingDeviationDelta=a.Deviation-old.StandardDeviation;
            if(a.Abandoned)
            {
                var previous=db.Table<MmrSettlement>().Where(s=>s.PlayerId==a.PlayerId && s.Abandoned).ToList()
                    .Count(s=>s.MatchId!=match.MatchId && s.EndedAt>(long)match.EndedAt-86400000);
                var until=Math.Max(now,(long)match.EndedAt)+MmrQueueGuard.CooldownMinutes(previous)*60000L;
                var ban=db.Find<MmrQueuePenalty>(a.PlayerId);
                db.InsertOrReplace(new MmrQueuePenalty {PlayerId=a.PlayerId,Until=Math.Max(until,ban?.Until??0)});
            }
        }
        if(match.GameModeKey==new Key("game_mode_ranked") && LeagueSeasons.Current(db) is {} season)
            LeagueSeasons.RecordPersonalMatch(db,season,match,ratings,references??[]);
        db.Insert(new MmrMatchSettlement {MatchId=match.MatchId,Version=PersonalMmr.Version,Payload=Encode(match),
            References=JsonSerializer.Serialize((references??[]).Select(r=>r.Version).Distinct())});
        // Master acknowledges the inbox only after refreshing caches and publishing the committed result.
        return awards;
    }

}
