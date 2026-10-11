using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Authentication;

/// <summary>
/// Permanent account bans, enforced at the Reborn master login. Configs/banned_players.txt holds one ban per
/// line: a SteamID64, then optionally the message that banned player is shown instead of the default.
/// Blank lines and lines starting with '#' are ignored. The file is re-read when it changes, so a ban takes
/// effect at the player's next login without a restart. No file, or an empty one, bans nobody.
/// </summary>
public sealed class PlayerBanList(string path)
{
    public const string DefaultMessage = "This account is permanently banned from BNL Reborn.";

    public static PlayerBanList Shared { get; } =
        new(Path.Combine(Databases.ConfigsFolderPath, "banned_players.txt"));

    private readonly object _lock = new();
    private DateTime _stamp = DateTime.MinValue;
    private Dictionary<ulong, string> _bans = [];

    /// <summary>The message to refuse this account with, or null when it is not banned.</summary>
    public string? Refusal(ulong steamId) => Bans().GetValueOrDefault(steamId);

    private Dictionary<ulong, string> Bans()
    {
        lock (_lock)
        {
            try
            {
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (stamp == _stamp) return _bans;
                var bans = new Dictionary<ulong, string>();
                var lines = stamp == DateTime.MinValue ? [] : File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var split = line.IndexOfAny([' ', '\t']);
                    var id = split < 0 ? line : line[..split];
                    if (!ulong.TryParse(id, out var steamId))
                    {
                        // A typo must not drop the other bans: skip only the unreadable line.
                        Log.Warn(LogCat.Conn, $"Ignoring unreadable ban on line {i + 1} of {path}");
                        continue;
                    }
                    var message = split < 0 ? "" : line[split..].Trim();
                    bans[steamId] = message.Length == 0 ? DefaultMessage : message;
                }
                _stamp = stamp;
                _bans = bans;
                Log.Info(LogCat.Conn, $"Banned players: {_bans.Count}");
                return _bans;
            }
            catch (IOException exception)
            {
                Log.Warn(LogCat.Conn, $"Could not read {path}: {exception.Message}");
                return _bans;
            }
            catch (UnauthorizedAccessException exception)
            {
                Log.Warn(LogCat.Conn, $"Could not read {path}: {exception.Message}");
                return _bans;
            }
        }
    }
}
