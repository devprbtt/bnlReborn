using BNLReloadedServer.Authentication;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BNLReloadedServer.Database;

public partial class RegionServerDatabase
{
    readonly ConcurrentDictionary<uint, long> chatWarningTimes = new();
    private bool PreparePlayerChat(uint id, ref string message)
    {
        if (ChatBanList.Shared.Active(id, DateTimeOffset.UtcNow) is { } ban)
        {
            long now = Environment.TickCount64;
            long last = chatWarningTimes.GetOrAdd(id, now - 60000);
            if (now - last >= 10000 && chatWarningTimes.TryUpdate(id, now, last))
                GetPlayerService(id)?.SendNotification(new NotificationZeus { Text = ban.Warning });
            return false;
        }
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500) return false;
        var plain = PlayerChatText.Plain(message);
        if (plain != message)
            Log.Warn(LogCat.Player, $"Neutralized player chat formatting: playerId={id}, text={JsonSerializer.Serialize(message)}");
        message = plain;
        return !string.IsNullOrWhiteSpace(message);
    }
}
