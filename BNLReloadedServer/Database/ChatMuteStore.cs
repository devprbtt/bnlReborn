using SQLite;

namespace BNLReloadedServer.Database;

internal static class ChatMuteStore
{
    internal static Task EnsureSchema(SQLiteAsyncConnection database) =>
        database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS PlayerChatMutes (
                player_id INTEGER NOT NULL,
                target_id INTEGER NOT NULL,
                PRIMARY KEY (player_id, target_id)
            )
            """);

    internal static async Task<List<uint>> Read(SQLiteAsyncConnection database, uint playerId)
    {
        var targets = await database.QueryScalarsAsync<long>(
            "SELECT target_id FROM PlayerChatMutes WHERE player_id = ? ORDER BY target_id", playerId);
        return targets.Select(id => checked((uint)id)).ToList();
    }

    internal static Task Write(SQLiteAsyncConnection database, uint playerId, uint targetId, bool muted) =>
        muted
            ? database.ExecuteAsync("INSERT OR IGNORE INTO PlayerChatMutes (player_id, target_id) VALUES (?, ?)", playerId, targetId)
            : database.ExecuteAsync("DELETE FROM PlayerChatMutes WHERE player_id = ? AND target_id = ?", playerId, targetId);
}
