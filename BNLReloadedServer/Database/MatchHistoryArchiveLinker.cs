using System.Text;
using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

public static class MatchHistoryArchiveLinker
{
    // Old profile summaries used the delayed result-screen timestamp and an instance
    // ID instead of a round ID. Repair the response identity, never stats or ratings.
    public static string? FindId(MatchHistoryRecord history, IEnumerable<ArchivedMatchRecord> candidates)
    {
        var instance=Encoding.UTF8.GetString(history.MatchId??[]);
        if(string.IsNullOrEmpty(instance)||instance.Contains(':')||history.MatchEndTime>long.MaxValue)return null;
        var time=(long)history.MatchEndTime;
        var matches=candidates.Where(m=>m.Id.StartsWith(instance+":",StringComparison.Ordinal)&&
            m.MapKey==history.MapKey.Hash&&m.GameModeKey==history.GameModeKey.Hash&&m.EndedAt>=m.StartedAt&&
            (history.IsQuit ? time>=m.StartedAt && (decimal)time-m.EndedAt<=30000 : Math.Abs((decimal)m.EndedAt-time)<=30000))
            .Select(m=>m.Id).Distinct().Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }

    public static async Task ResolveAsync(SQLiteAsyncConnection db,uint playerId,IEnumerable<MatchHistoryRecord> history)
    {
        foreach(var row in history)
        {
            var instance=Encoding.UTF8.GetString(row.MatchId??[]);
            if(string.IsNullOrEmpty(instance)||instance.Contains(':'))continue;
            // Membership is mandatory: never attach another player's scoreboard.
            var candidates=await db.QueryAsync<ArchivedMatchRecord>(
                "SELECT m.* FROM Matches m JOIN MatchPlayers p ON p.match_id=m.id WHERE p.player_id=? AND m.id LIKE ? AND m.map_key=? AND m.game_mode_key=?",
                playerId,instance+":%",row.MapKey.Hash,row.GameModeKey.Hash);
            if(FindId(row,candidates) is {} id)row.MatchId=Encoding.UTF8.GetBytes(id);
        }
    }
}
