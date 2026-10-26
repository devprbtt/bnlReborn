using System.Text;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;

int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
const string instance="7b435b3f-5848-402e-945c-22c203b15fb2";
var archive=new ArchivedMatchRecord{Id=instance+":1792945439423",MapKey=2975753642,GameModeKey=2874013687,StartedAt=1792945439423,EndedAt=1792946369549};
MatchHistoryRecord History()=>new(){MatchId=Encoding.UTF8.GetBytes(instance),MapKey=Key.FromHash(2975753642),GameModeKey=Key.FromHash(2874013687),MatchEndTime=1792946376554,MatchSeconds=697.125f,Kill=7,Death=6,Assist=11,ObjectiveDamage=620,BlocksBuilt=14156,Destruction=2178};
Check(MatchHistoryArchiveLinker.FindId(History(),[archive])==archive.Id,"real screenshot resolves despite 7005 ms result delay");
var exact=History();exact.MatchId=Encoding.UTF8.GetBytes(archive.Id);
Check(MatchHistoryArchiveLinker.FindId(exact,[archive])==null,"already stable IDs are untouched");
var wrong=History();wrong.MapKey=new Key("wrong_map");Check(MatchHistoryArchiveLinker.FindId(wrong,[archive])==null,"map must match");
wrong=History();wrong.GameModeKey=new Key("game_mode_ranked");Check(MatchHistoryArchiveLinker.FindId(wrong,[archive])==null,"queue must match");
var far=History();far.MatchEndTime=(ulong)archive.EndedAt+30001;Check(MatchHistoryArchiveLinker.FindId(far,[archive])==null,"unrelated timestamp is rejected");
var edge=History();edge.MatchEndTime=(ulong)archive.EndedAt+30000;Check(MatchHistoryArchiveLinker.FindId(edge,[archive])==archive.Id,"30 second compatibility boundary included");
var other=new ArchivedMatchRecord{Id=instance+":other",MapKey=archive.MapKey,GameModeKey=archive.GameModeKey,StartedAt=archive.StartedAt,EndedAt=archive.EndedAt+1000};
Check(MatchHistoryArchiveLinker.FindId(History(),[archive,other])==null,"ambiguous round never substituted");
var quit=History();quit.IsQuit=true;quit.MatchEndTime=(ulong)archive.StartedAt+10000;
Check(MatchHistoryArchiveLinker.FindId(quit,[archive])==archive.Id,"early quitter resolves containing archived round");
quit.MatchEndTime=(ulong)archive.StartedAt-1;Check(MatchHistoryArchiveLinker.FindId(quit,[archive])==null,"quit before round does not match");
var invalid=History();invalid.MatchEndTime=ulong.MaxValue;Check(MatchHistoryArchiveLinker.FindId(invalid,[archive])==null,"out of range timestamp rejected");
var path=Path.Combine(Path.GetTempPath(),"bnl-history-"+Guid.NewGuid()+".db");
var db=new SQLiteAsyncConnection(path);
try
{
    await db.CreateTableAsync<ArchivedMatchRecord>();await db.CreateTableAsync<ArchivedMatchPlayerRecord>();
    await db.InsertAsync(archive);await db.InsertAsync(new ArchivedMatchPlayerRecord{MatchId=archive.Id,PlayerId=21});
    var row=History();await MatchHistoryArchiveLinker.ResolveAsync(db,21,[row]);
    Check(Encoding.UTF8.GetString(row.MatchId!)==archive.Id,"database join returns exact stable ID for participant");
    Check(row.Kill==7&&row.Death==6&&row.Assist==11&&row.MatchEndTime==1792946376554&&row.MatchSeconds==697.125f,"compatibility repair preserves all summary values");
    var stranger=History();await MatchHistoryArchiveLinker.ResolveAsync(db,22,[stranger]);
    Check(Encoding.UTF8.GetString(stranger.MatchId!)==instance,"nonparticipant cannot inherit another scoreboard");
    Check((await db.Table<ArchivedMatchRecord>().FirstAsync()).EndedAt==archive.EndedAt,"archive timestamp is never rewritten");
    // Verify repaired identity survives the same profile-history wire format the client reads.
    var roundtrip=PlayerData.ReadMatchByteRecord(PlayerData.WriteMatchByteRecord([row]))[0];
    Check(Encoding.UTF8.GetString(roundtrip.MatchId!)==archive.Id&&roundtrip.Kill==7,"stable ID survives profile serialization");
}
finally{await db.CloseAsync();File.Delete(path);}
Console.WriteLine($"MATCH_HISTORY_FIXTURE_OK checks={checks}");
