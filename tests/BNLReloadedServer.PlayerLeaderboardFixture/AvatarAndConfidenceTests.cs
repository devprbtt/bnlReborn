using BNLReloadedServer.Database;
using SQLite;
using System.Text.Json;

public static partial class PlayerLeaderboardTests {
    public static void AvatarAndConfidence() {
        var path=Path.Combine(Path.GetTempPath(),"bnl-leaderboard-identity-"+Guid.NewGuid()+".db");
        try {
            using var db=Open(path);
            Account(db,1,"Atlas",35);Account(db,2,"Nova",30);
            var first=db.Get<PlayerRecord>(1u);first.SteamId=76561198000000001;first.RatingDeviation=2.25;first.RatingVolatility=.06;db.Update(first);
            var second=db.Get<PlayerRecord>(2u);second.SteamId=0;second.RatingDeviation=4.5;db.Update(second);
            Rounds(db,Min,_=>Round(db,1,"Atlas",Casual,Now-Day,10));
            Rounds(db,Min,_=>Round(db,2,"Nova",Casual,Now-Day,5));
            var board=Board(db,PlayerLeaderboardStat.CasualMmr);
            Check(board.Rows[0].SteamId=="76561198000000001","Steam IDs preserve all 64-bit digits as strings");
            Check(board.Rows[0].RatingDeviation==2.25&&board.Rows[1].RatingDeviation==4.5,"casual board exposes current rating uncertainty, not dormant volatility");
            Check(board.Rows[1].SteamId==null,"missing Steam identity remains absent");
            var kills=Board(db,PlayerLeaderboardStat.Kills);
            Check(kills.Rows[0].SteamId==first.SteamId.ToString()&&kills.Rows.All(r=>r.RatingDeviation==null),"all stat boards carry avatar identity; uncertainty only accompanies its rating");
            var options=new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
            var json=JsonSerializer.Serialize(board,options);
            using var doc=JsonDocument.Parse(json);
            Check(doc.RootElement.GetProperty("rows")[0].GetProperty("steam_id").GetString()==first.SteamId.ToString(),"public JSON uses string steam_id");
            Check(doc.RootElement.GetProperty("rows")[0].GetProperty("rating_deviation").GetDouble()==2.25,"public JSON carries rating_deviation");
            var output=Environment.GetEnvironmentVariable("BNL_LEADERBOARD_FIXTURE_OUTPUT");
            if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"casual-avatars.json"),json);}
        } finally {SQLiteAsyncConnection.ResetPool();try{File.Delete(path);}catch(IOException){}}
    }
}
