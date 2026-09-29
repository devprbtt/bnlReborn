using System.Text.RegularExpressions;
using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.Authentication;

/// <summary>
/// Reborn clients report the release the launcher actually installed, as a suffix on the login protocol
/// id: "bnl-reborn-v1;client=0.2.0-beta.43". Logins older than Configs/minimum_client_version.txt are
/// refused with an update message. The file is re-read when it changes, so the minimum can be raised the
/// moment a release is published, without a restart. No file, or an empty one, enforces nothing.
/// </summary>
public sealed partial class ClientVersionGate(string path)
{
    public const string ClientSuffix = ";client=";

    public static ClientVersionGate Shared { get; } =
        new(Path.Combine(Databases.ConfigsFolderPath, "minimum_client_version.txt"));

    private readonly object _lock = new();
    private DateTime _stamp = DateTime.MinValue;
    private string? _minimum;

    /// <summary>Splits "bnl-reborn-v1;client=X" into the protocol id and the reported version (null if absent).</summary>
    public static string SplitProtocol(string id, out string? clientVersion)
    {
        var index = id.IndexOf(ClientSuffix, StringComparison.Ordinal);
        if (index < 0)
        {
            clientVersion = null;
            return id;
        }
        clientVersion = id[(index + ClientSuffix.Length)..];
        return id[..index];
    }

    /// <summary>The refusal message for this client, or null when it may log in.</summary>
    public string? Refusal(string? clientVersion)
    {
        var minimum = Minimum();
        if (minimum is null || Release(minimum) is not { } required) return null;
        if (Release(clientVersion) is { } reported && reported >= required) return null;
        var installed = string.IsNullOrWhiteSpace(clientVersion) ? "an older version" : clientVersion;
        return $"Your game is out of date ({installed}). Close the game and restart the BNL Reborn launcher " +
               $"to update to {minimum} or newer.";
    }

    /// <summary>The beta number of a "0.2.0-beta.N" release, or null for anything else.</summary>
    internal static int? Release(string? version) =>
        version is not null && ReleasePattern().Match(version.Trim()) is { Success: true } match &&
        int.TryParse(match.Groups[1].Value, out var number) ? number : null;

    private string? Minimum()
    {
        lock (_lock)
        {
            try
            {
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (stamp == _stamp) return _minimum;
                var text = stamp == DateTime.MinValue ? null : File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(text) && Release(text) is null)
                {
                    // A typo must not lock every player out: keep the last good minimum.
                    Log.Warn(LogCat.Conn, $"Ignoring invalid minimum client version '{text}' in {path}");
                    _stamp = stamp;
                    return _minimum;
                }
                _stamp = stamp;
                _minimum = string.IsNullOrEmpty(text) ? null : text;
                Log.Info(LogCat.Conn, _minimum is null ? "Minimum client version: none" : $"Minimum client version: {_minimum}");
                return _minimum;
            }
            catch (IOException exception)
            {
                Log.Warn(LogCat.Conn, $"Could not read {path}: {exception.Message}");
                return _minimum;
            }
            catch (UnauthorizedAccessException exception)
            {
                Log.Warn(LogCat.Conn, $"Could not read {path}: {exception.Message}");
                return _minimum;
            }
        }
    }

    [GeneratedRegex(@"^0\.2\.0-beta\.(\d{1,6})$")]
    private static partial Regex ReleasePattern();
}
