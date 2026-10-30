using System.Globalization;
using System.Text;
using System.Text.Json;
using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Authentication;

// Account-scoped chat restrictions, independent of login and matchmaking bans.
// Operators replace Configs/chat_bans.json atomically; existing restrictions survive a bad edit.
public sealed class ChatBanList(string path)
{
    public sealed record Ban(uint PlayerId, DateTimeOffset UntilUtc, string Reason)
    {
        public string Warning => $"Chat suspended until {UntilUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC. {Reason} You may still play. Do not impersonate other players.";
    }
    public static ChatBanList Shared { get; } = new(Path.Combine(Databases.ConfigsFolderPath, "chat_bans.json"));
    readonly object gate = new();
    DateTime stamp = DateTime.MinValue;
    Dictionary<uint, Ban> bans = [];
    public Ban? Active(uint playerId, DateTimeOffset now)
    {
        lock (gate)
        {
            try
            {
                var changed = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (changed != stamp)
                {
                    var entries = changed == DateTime.MinValue ? [] : JsonSerializer.Deserialize<Ban[]>(File.ReadAllText(path))
                        ?? throw new JsonException("Expected a ban array");
                    var next = new Dictionary<uint, Ban>();
                    foreach (var entry in entries)
                    {
                        if (entry is null || entry.PlayerId == 0 || string.IsNullOrWhiteSpace(entry.Reason) || entry.UntilUtc == default)
                            throw new JsonException("Invalid chat ban");
                        next.Add(entry.PlayerId, entry);
                    }
                    bans = next; stamp = changed;
                    Log.Info(LogCat.Player, $"Loaded {bans.Count} timed chat restrictions");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Do not silently lift a restriction because of a malformed/partial operator write.
                Log.Warn(LogCat.Player, $"Could not reload chat restrictions: {e.GetType().Name}");
            }
            return bans.TryGetValue(playerId, out var ban) && now < ban.UntilUtc ? ban : null;
        }
    }
}

public static class PlayerChatText
{
    // The legacy player's alert marker suppresses the authenticated sender prefix. Never allow
    // player text to reach that parser intact; also neutralize rich text and fake extra chat lines.
    public static string Plain(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            var category = char.GetUnicodeCategory(ch);
            output.Append(ch switch
            {
                '<' => '‹', '>' => '›', '{' => '(', '}' => ')',
                _ when char.IsControl(ch) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => ' ',
                _ => ch
            });
        }
        return output.ToString();
    }
}
