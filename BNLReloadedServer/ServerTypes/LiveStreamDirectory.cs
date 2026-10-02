using System.Net.Http.Headers;
using System.Text.Json;
using BNLReloadedServer.Logging;

namespace BNLReloadedServer.ServerTypes;

public record LiveStream(string Platform, string Channel, string Title, int Viewers, string Url, string Thumbnail,
    long StartedAt);

public record LiveStreamSnapshot(long UpdatedAt, LiveStream[] Streams);

// Who is streaming the game right now, for the client Home page. Twitch is read from the game's category, YouTube
// from live-video search filtered to titles that name the game. Credentials come from the environment and never
// leave the server: BNL_TWITCH_CLIENT_ID + BNL_TWITCH_CLIENT_SECRET, BNL_YOUTUBE_API_KEY. A platform without
// credentials is skipped; a failed poll keeps the last good list for that platform.
public static class LiveStreamDirectory
{
    private const string TwitchGameName = "Block N Load";
    private static readonly TimeSpan TwitchInterval = TimeSpan.FromMinutes(3);
    // search.list costs 100 of the default 10,000 daily quota units; every 15 minutes uses 9,600 with videos.list.
    private static readonly TimeSpan YouTubeInterval = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static LiveStream[] _twitch = [], _youtube = [];
    private static long _updatedAt;
    private static string? _twitchToken, _twitchGameId;
    private static DateTimeOffset _twitchTokenExpires;

    public static LiveStreamSnapshot Snapshot => new(Interlocked.Read(ref _updatedAt),
        Volatile.Read(ref _twitch).Concat(Volatile.Read(ref _youtube)).OrderByDescending(s => s.Viewers).ToArray());

    public static void Start(CancellationToken stop)
    {
        var twitchId = Environment.GetEnvironmentVariable("BNL_TWITCH_CLIENT_ID");
        var twitchSecret = Environment.GetEnvironmentVariable("BNL_TWITCH_CLIENT_SECRET");
        var youtubeKey = Environment.GetEnvironmentVariable("BNL_YOUTUBE_API_KEY");
        if (!string.IsNullOrEmpty(twitchId) && !string.IsNullOrEmpty(twitchSecret))
            _ = Poll("Twitch", TwitchInterval, ct => PollTwitch(twitchId, twitchSecret, ct), s => Volatile.Write(ref _twitch, s), stop);
        if (!string.IsNullOrEmpty(youtubeKey))
            _ = Poll("YouTube", YouTubeInterval, ct => PollYouTube(youtubeKey, ct), s => Volatile.Write(ref _youtube, s), stop);
        Log.Info(LogCat.Panel, $"Live stream directory: Twitch {(twitchId is { Length: > 0 } ? "on" : "off")}, " +
                               $"YouTube {(youtubeKey is { Length: > 0 } ? "on" : "off")}");
    }

    private static async Task Poll(string name, TimeSpan interval, Func<CancellationToken, Task<LiveStream[]>> fetch,
        Action<LiveStream[]> store, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                store(await fetch(stop));
                Interlocked.Exchange(ref _updatedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e) { Log.Warn(LogCat.Panel, $"{name} live stream poll failed: {e.Message}"); }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task<LiveStream[]> PollTwitch(string clientId, string secret, CancellationToken ct)
    {
        if (_twitchToken == null || DateTimeOffset.UtcNow >= _twitchTokenExpires)
        {
            using var tokenResponse = await Http.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["client_id"] = clientId, ["client_secret"] = secret, ["grant_type"] = "client_credentials" }), ct);
            tokenResponse.EnsureSuccessStatusCode();
            using var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
            _twitchToken = token.RootElement.GetProperty("access_token").GetString();
            _twitchTokenExpires = DateTimeOffset.UtcNow.AddSeconds(token.RootElement.GetProperty("expires_in").GetInt32() - 300);
        }
        _twitchGameId ??= ParseTwitchGameId(await TwitchGet($"https://api.twitch.tv/helix/games?name={Uri.EscapeDataString(TwitchGameName)}", clientId, ct))
                          ?? throw new InvalidOperationException($"Twitch has no category named {TwitchGameName}");
        return ParseTwitchStreams(await TwitchGet($"https://api.twitch.tv/helix/streams?game_id={_twitchGameId}&first=20", clientId, ct));
    }

    private static async Task<string> TwitchGet(string url, string clientId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _twitchToken);
        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) _twitchToken = null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static async Task<LiveStream[]> PollYouTube(string key, CancellationToken ct)
    {
        var query = Uri.EscapeDataString("\"Block N Load\" | \"BNL Reborn\"");
        var search = await Http.GetStringAsync("https://www.googleapis.com/youtube/v3/search?part=snippet&eventType=live" +
                                               $"&type=video&maxResults=15&q={query}&key={key}", ct);
        var streams = ParseYouTubeSearch(search);
        if (streams.Length == 0) return streams;
        var ids = string.Join(",", streams.Select(s => s.Url[(s.Url.LastIndexOf('=') + 1)..]));
        var details = await Http.GetStringAsync("https://www.googleapis.com/youtube/v3/videos?part=liveStreamingDetails" +
                                                $"&id={ids}&key={key}", ct);
        return ApplyYouTubeDetails(streams, details);
    }

    internal static string? ParseTwitchGameId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        return data.GetArrayLength() > 0 ? data[0].GetProperty("id").GetString() : null;
    }

    internal static LiveStream[] ParseTwitchStreams(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Where(s => s.GetProperty("type").GetString() == "live")
            .Select(s => new LiveStream("twitch", s.GetProperty("user_name").GetString() ?? "",
                s.GetProperty("title").GetString() ?? "", s.GetProperty("viewer_count").GetInt32(),
                "https://www.twitch.tv/" + s.GetProperty("user_login").GetString(),
                (s.GetProperty("thumbnail_url").GetString() ?? "").Replace("{width}", "320").Replace("{height}", "180"),
                DateTimeOffset.Parse(s.GetProperty("started_at").GetString()!).ToUnixTimeSeconds()))
            .ToArray();
    }

    // Search matches loosely; keep only streams whose title or channel actually names the game.
    internal static bool NamesGame(string text)
    {
        var t = text.ToLowerInvariant();
        return t.Contains("block n load") || t.Contains("blocknload") || t.Contains("block'n'load") ||
               t.Contains("bnl reborn") || t.Contains("block n' load");
    }

    internal static LiveStream[] ParseYouTubeSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return [];
        return items.EnumerateArray().Select(item =>
            {
                var snippet = item.GetProperty("snippet");
                var title = snippet.GetProperty("title").GetString() ?? "";
                var channel = snippet.GetProperty("channelTitle").GetString() ?? "";
                var thumbnail = snippet.TryGetProperty("thumbnails", out var t) && t.TryGetProperty("medium", out var m)
                    ? m.GetProperty("url").GetString() ?? "" : "";
                return new LiveStream("youtube", channel, System.Net.WebUtility.HtmlDecode(title), 0,
                    "https://www.youtube.com/watch?v=" + item.GetProperty("id").GetProperty("videoId").GetString(), thumbnail,
                    DateTimeOffset.Parse(snippet.GetProperty("publishedAt").GetString()!).ToUnixTimeSeconds());
            })
            .Where(s => NamesGame(s.Title) || NamesGame(s.Channel))
            .ToArray();
    }

    internal static LiveStream[] ApplyYouTubeDetails(LiveStream[] streams, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var details = new Dictionary<string, (int Viewers, long? Started)>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("liveStreamingDetails", out var live)) continue;
            var viewers = live.TryGetProperty("concurrentViewers", out var v) && int.TryParse(v.GetString(), out var n) ? n : 0;
            long? started = live.TryGetProperty("actualStartTime", out var s)
                ? DateTimeOffset.Parse(s.GetString()!).ToUnixTimeSeconds() : null;
            details[item.GetProperty("id").GetString()!] = (viewers, started);
        }
        return streams.Select(s =>
        {
            var id = s.Url[(s.Url.LastIndexOf('=') + 1)..];
            return details.TryGetValue(id, out var d) ? s with { Viewers = d.Viewers, StartedAt = d.Started ?? s.StartedAt } : s;
        }).ToArray();
    }
}
