using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;
public static partial class PlayerLeaderboardTests {
    static void ScoresAndLevels() {
        var path=Path.Combine(Path.GetTempPath(),"bnl-spm-level-"+Guid.NewGuid()+".db");
        try {
            using var db=Open(path);
            foreach(var (id,name,level) in new[]{(1u,"Atlas",50),(2u,"Nova",100),(3u,"Flint",100),(5u,"Newcomer",1)}) {
                Account(db,id,name,30);
                var account=db.Get<PlayerRecord>(id);
                account.Progression=PlayerProgression.WriteByteRecord(new PlayerProgression {PlayerProgress=new XpInfo {Level=level}});
                db.Update(account);
            }
            Account(db,4,"CorruptProgression",30);
            var corrupt=db.Get<PlayerRecord>(4u);corrupt.Progression=[255];db.Update(corrupt);
            Rounds(db,Min,_=>Round(db,1,"OldAtlas",Ranked,Now-Day,kills:1,score:600,seconds:60));
            Round(db,1,"OldAtlas",Casual,Now-40*Day,kills:1,score:0,seconds:600);
            Rounds(db,Min,i=>Round(db,2,"Nova",Casual,Now-Day,kills:1,score:600,seconds:i%2==0?60:180));
            Rounds(db,Min,_=>Round(db,3,"Flint",Custom,Now-Day,kills:1,score:999999));
            Rounds(db,Min-1,_=>Round(db,4,"CorruptProgression",Ranked,Now-Day,kills:1,score:99999));
            Rounds(db,Min,_=>Round(db,7,"InvalidScore",Ranked,Now-Day,kills:1,score:-100));
            var spm=Board(db,PlayerLeaderboardStat.ScorePerMinute);
            Check(spm.Rows.Select(r=>(r.PlayerId,r.Value)).SequenceEqual([(1u,400d),(2u,300d)]),"SPM divides total archived score by total played minutes; custom and negative scores do not enter the board");
            Check(Board(db,PlayerLeaderboardStat.ScorePerMinute,true).Rows.First().Value==600,"30-day SPM excludes old score and time together");
            Check(spm.Rows.All(r=>r.PlayerId!=4)&&spm.MinMatches==Min,"SPM retains the 20 valid counted-round minimum");
            Check(spm.Rows[0].Nickname=="Atlas","SPM uses current account names");
            var levels=Board(db,PlayerLeaderboardStat.PlayerLevel);
            var output=Environment.GetEnvironmentVariable("BNL_LEADERBOARD_FIXTURE_OUTPUT");
            if(!string.IsNullOrEmpty(output)) {
                Directory.CreateDirectory(output);
                var options=new System.Text.Json.JsonSerializerOptions {PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.SnakeCaseLower};
                File.WriteAllText(Path.Combine(output,"score-per-minute.json"),System.Text.Json.JsonSerializer.Serialize(spm,options));
                File.WriteAllText(Path.Combine(output,"player-level.json"),System.Text.Json.JsonSerializer.Serialize(levels,options));
            }
            Check(levels.Rows.Select(r=>(r.PlayerId,r.Value,r.Rank)).SequenceEqual([(2u,100d,1),(3u,100d,1),(1u,50d,3),(5u,1d,4)]),"account levels rank descending, including new accounts, with ties sharing rank");
            Check(levels.MinMatches==0&&levels.Rows.Single(r=>r.PlayerId==5).Matches==0,"player level has no match requirement");
            Check(levels.Rows.All(r=>r.PlayerId!=4),"malformed progression is skipped without inventing a level or breaking the board");
            Check(Board(db,PlayerLeaderboardStat.PlayerLevel,true).Period=="all"&&Board(db,PlayerLeaderboardStat.PlayerLevel,true).Rows.SequenceEqual(levels.Rows),"player level always represents current all-time account progression");
            Check(levels.Page(1,1).Rows.Single().Rank==1,"level paging preserves tied global ranks");
            Check(PlayerStatLeaderboards.StatNames["score_per_minute"]==PlayerLeaderboardStat.ScorePerMinute&&PlayerStatLeaderboards.StatNames["player_level"]==PlayerLeaderboardStat.PlayerLevel,"new public stat names are registered");
            Check(!PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.ScorePerMinute)&&!PlayerStatLeaderboards.IsPerMatchAverage(PlayerLeaderboardStat.PlayerLevel),"SPM and levels are not per-match averages");
        } finally {SQLiteAsyncConnection.ResetPool();try{File.Delete(path);}catch(IOException){}}
    }
}
