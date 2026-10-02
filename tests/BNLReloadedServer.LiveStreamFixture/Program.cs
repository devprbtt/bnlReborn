using System.Reflection;
using BNLReloadedServer.ServerTypes;

var type = typeof(LiveStreamDirectory);
T Call<T>(string name, params object[] args) =>
    (T)type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

var checks = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}

const string games = """{"data":[{"id":"491168","name":"Block N Load","box_art_url":"x"}]}""";
Check(Call<string?>("ParseTwitchGameId", games) == "491168", "Twitch category id is read from helix/games");
Check(Call<string?>("ParseTwitchGameId", """{"data":[]}""") == null, "a missing Twitch category yields no id");

const string twitch = """
{"data":[
 {"id":"1","user_id":"9","user_login":"prbtt","user_name":"Prbtt","game_id":"491168","game_name":"Block N Load",
  "type":"live","title":"Christmas Boris games","viewer_count":42,"started_at":"2026-10-01T20:00:00Z",
  "thumbnail_url":"https://static-cdn.jtvnw.net/previews-ttv/live_user_prbtt-{width}x{height}.jpg"},
 {"id":"2","user_login":"other","user_name":"Other","type":"","title":"rerun","viewer_count":3,
  "started_at":"2026-10-01T19:00:00Z","thumbnail_url":"x"}],
 "pagination":{}}
""";
var t = Call<LiveStream[]>("ParseTwitchStreams", twitch);
Check(t.Length == 1, "only live Twitch streams are listed");
Check(t[0] is { Platform: "twitch", Channel: "Prbtt", Viewers: 42, Url: "https://www.twitch.tv/prbtt" },
    "Twitch stream carries channel, viewers and channel URL");
Check(t[0].Thumbnail.EndsWith("prbtt-320x180.jpg"), "Twitch thumbnail size placeholders are filled");
Check(t[0].StartedAt == DateTimeOffset.Parse("2026-10-01T20:00:00Z").ToUnixTimeSeconds(), "Twitch start time is kept");

const string search = """
{"items":[
 {"id":{"kind":"youtube#video","videoId":"abc123"},"snippet":{"publishedAt":"2026-10-01T21:00:00Z",
  "channelTitle":"Yeti Fan","title":"Block N Load is back! &amp; ranked","thumbnails":{"medium":{"url":"https://i.ytimg.com/vi/abc123/mqdefault.jpg"}}}},
 {"id":{"kind":"youtube#video","videoId":"zzz"},"snippet":{"publishedAt":"2026-10-01T21:00:00Z",
  "channelTitle":"Random","title":"Minecraft block building live","thumbnails":{}}}]}
""";
var y = Call<LiveStream[]>("ParseYouTubeSearch", search);
Check(y.Length == 1 && y[0].Url == "https://www.youtube.com/watch?v=abc123", "YouTube search keeps only titles naming the game");
Check(y[0].Title == "Block N Load is back! & ranked", "YouTube titles are HTML-decoded");
Check(Call<LiveStream[]>("ParseYouTubeSearch", """{"error":{}}""").Length == 0, "a YouTube error body lists nothing");

const string videos = """
{"items":[{"id":"abc123","liveStreamingDetails":{"actualStartTime":"2026-10-01T20:30:00Z","concurrentViewers":"17"}}]}
""";
var merged = Call<LiveStream[]>("ApplyYouTubeDetails", y, videos);
Check(merged[0].Viewers == 17 && merged[0].StartedAt == DateTimeOffset.Parse("2026-10-01T20:30:00Z").ToUnixTimeSeconds(),
    "YouTube viewers and actual start time come from videos.list");
Check(Call<bool>("NamesGame", "BNL Reborn tournament"), "BNL Reborn titles count as the game");

Console.WriteLine($"Live stream fixture passed: {checks} checks.");
