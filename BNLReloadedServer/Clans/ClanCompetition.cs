using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using SQLite;

namespace BNLReloadedServer.Clans;

public static class ClanCompetition
{
    internal static readonly object Gate = new();
    public static bool IsReserved(uint player) { lock (Gate) return Rooms.Values.Any(r => r.Players.Any(p => p.Id == player)); }
    private static readonly Dictionary<int, ClanFightRoom> Rooms = [];
    private static int nextRoom;
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly Timer Clock = new(_ => Tick(), null, 1000, 1000);
    public static string Handle(uint player, byte command, int roomId, uint key)
    {
        lock (Gate)
        {
            Prune();
            var clan = ClanHub.Store.ClanOf(player);
            var room = Rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.Id == player));
            string error = "";
            switch (command)
            {
                case 0: break;
                case 1:
                    if (clan == null || room != null || !Available(player)) { error = "Leave your current queue or game first, and join a clan."; break; }
                    room = new ClanFightRoom { Id = ++nextRoom, HomeClan = clan.Id, Leader = player };
                    room.Join(player, clan.Id); Rooms.Add(room.Id, room); break;
                case 2:
                    if (clan == null || room != null || !Available(player) || !Rooms.TryGetValue(roomId, out var target) || !target.Join(player, clan.Id))
                        error = "This room is full or reserved for its two clans.";
                    break;
                case 3:
                    if (room != null && room.Phase != "draft") room.Leave(player);
                    break;
                case 4:
                    if (room?.Phase == "room") { var member = room.Players.Single(p => p.Id == player); member.Ready = !member.Ready; }
                    break;
                case 5:
                    if (room?.Phase == "room" && room.Leader == player) { room.Swapped = !room.Swapped; room.ResetReady(); }
                    break;
                case 6:
                    if (room == null || room.Leader != player || !room.CanStart) { error = "Everyone must be ready in a balanced 4v4 or 5v5 room."; break; }
                    room.Maps.Clear(); room.Heroes.Clear();
                    room.Maps.AddRange((CatalogueHelper.MapList.Ranked ?? []).Where(k => Databases.MapDatabase.HasMap(k)).Select(k => k.Hash).Distinct());
                    room.Heroes.AddRange(CatalogueHelper.GetHeroes().Select(k => k.Hash).Distinct());
                    if (!room.Start(player, Now)) error = "Both clans need 4 or 5 players each, with everyone ready.";
                    break;
                case 7:
                    if (room == null || !room.Ban(player, key, Now)) error = "That ban is unavailable or it is not your turn.";
                    break;
                default: error = "Unknown clan fight action."; break;
            }
            Launch();
            return Snapshot(player, error);
        }
    }
    public static string DraftSnapshot(string session,IEnumerable<uint> bannedHeroes) => JsonSerializer.Serialize(new {
        error="",now=Now,roomId=0,rooms=Array.Empty<object>(),draftSession=session,draftBannedHeroes=bannedHeroes.Distinct().ToArray()
    });
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static bool Available(uint player) => Databases.RegionServerDatabase is RegionServerDatabase region && region.CanEnterClanFight(player);
    private static void Prune()
    {
        foreach (var r in Rooms.Values.ToArray())
        {
            if (r.Phase == "draft") continue;
            foreach (var p in r.Players.ToArray())
                if (!Available(p.Id) || ClanHub.Store.ClanOf(p.Id)?.Id != p.Clan) r.Leave(p.Id);
            if (r.Leader == 0 || r.Players.Count == 0) Rooms.Remove(r.Id);
        }
    }
    private static void Tick()
    {
        try { lock (Gate) { Prune(); foreach (var r in Rooms.Values) r.Tick(Now); Launch(); } }
        catch (Exception ex) { Logging.Log.Error(Logging.LogCat.Match, "Clan fight timer failed", ex); }
    }
    private static void Launch()
    {
        foreach (var room in Rooms.Values.Where(r => r.Phase == "draft").ToArray())
        {
            if (Databases.RegionServerDatabase is RegionServerDatabase region && region.StartClanFight(room)) Rooms.Remove(room.Id);
            else { room.Phase = "room"; room.ResetReady(); }
        }
    }
    private static object RoomView(ClanFightRoom r) => new {
        id = r.Id, name = (ClanHub.Store.Clan(r.HomeClan)?.Name ?? "Clan") + " vs " + (ClanHub.Store.Clan(r.AwayClan)?.Name ?? "Waiting for opponent"),
        phase = r.Phase, leader = r.Leader, captain = r.Captain, deadline = r.Deadline, canStart = r.CanStart,
        players = r.Players.Select(p => new { id = p.Id, name = Databases.PlayerDatabase.GetPlayerName(p.Id),
            team = (p.Clan == r.HomeClan) != r.Swapped ? 1 : 2, ready = p.Ready }),
        maps = r.Maps, heroes = r.Heroes, bannedMaps = r.BannedMaps, bannedHeroes = r.BannedHeroes
    };
    private static string Snapshot(uint player, string error) => JsonSerializer.Serialize(new {
        error, now = Now, rooms = ClanHub.Store.ClanOf(player) == null ? [] : Rooms.Values.Select(RoomView).ToArray(),
        roomId = Rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.Id == player))?.Id ?? 0
    }, Json);
}
