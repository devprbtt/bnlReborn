using BNLReloadedServer.BaseTypes;
using SQLite;

namespace BNLReloadedServer.Database;

public static class PlayerNameSearch
{
    public const int Limit = 50;

    // Read only the public search fields, not inventory/progression or other account blobs.
    // Match in .NET so case folding also works for non-ASCII names (SQLite NOCASE is ASCII-only).
    public static async Task<List<SearchResult>> Read(SQLiteAsyncConnection db, string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term)) return [];
        var rows = await db.QueryAsync<PlayerRecord>("SELECT id, steam_id, username FROM Users");
        return rows.Where(p => p.Username.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => string.Equals(p.Username, term, StringComparison.OrdinalIgnoreCase) ? 0
                : p.Username.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(p => p.Username, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PlayerId)
            .Take(Limit)
            .Select(p => new SearchResult { PlayerId=p.PlayerId, SteamId=p.SteamId, Nickname=p.Username }).ToList();
    }
}
