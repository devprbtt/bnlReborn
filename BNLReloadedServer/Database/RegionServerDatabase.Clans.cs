using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Clans;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using Moserware.Skills;

namespace BNLReloadedServer.Database;

public partial class RegionServerDatabase
{
    public bool CanEnterClanFight(uint id) => UserConnected(id, out var p) && p.Online &&
        p.ActiveScene?.Type == SceneType.MainMenu && p.GameInstanceId == null && p.CustomGameId == null && !_matchmaker.IsQueued(id) && !_playerDatabase.IsBanned(id);

    public bool StartClanFight(ClanFightRoom room)
    {
        if (room.Players.Any(p => !CanEnterClanFight(p.Id) || ClanHub.Store.ClanOf(p.Id)?.Id != p.Clan)) return false;
        Key key = (CatalogueHelper.MapList.Ranked ?? []).FirstOrDefault(k => k.Hash == room.Map);
        var map = Databases.MapDatabase.LoadMapData(key);
        if (map == null) return false;
        var info = new MapInfoCard { MapKey = key };
        int team1Clan = room.Swapped ? room.AwayClan : room.HomeClan;
        int team2Clan = room.Swapped ? room.HomeClan : room.AwayClan;
        List<PlayerQueueData> Team(int clan) => room.Players.Where(p => p.Clan == clan).Select(p =>
            new PlayerQueueData(p.Id, _connectedUsers[p.Id].Guid, GameInfo.DefaultGameInfo.DefaultRating,
                DateTimeOffset.UtcNow, null, CatalogueHelper.ModeRanked.Key)).ToList();
        var initiator = new ClanFightInitiator(Team(team1Clan), Team(team2Clan), team1Clan, team2Clan);
        var instance = new GameInstance(matchServer, server, Guid.NewGuid().ToString(), initiator);
        initiator.GameInstanceId = instance.GameInstanceId;
        instance.SetMap(info, map);
        instance.CreateLobby(CatalogueHelper.ModeRanked.Key, info);
        if (instance.Lobby == null) return false;
        foreach (var hero in CatalogueHelper.GetHeroes().Where(k => room.BannedHeroes.Contains(k.Hash))) instance.Lobby.BannedHeroes.Add(hero);
        if (!_gameInstances.TryAdd(instance.GameInstanceId, instance)) return false;
        _matchmakerGames[instance.GameInstanceId] = initiator;
        foreach (var member in room.Players)
        {
            var p = _connectedUsers[member.Id]; p.GameInstanceId = instance.GameInstanceId;
            if (GetService<IServiceScene>(p.Guid, ServiceId.ServiceScene, out var scene))
                UpdateScene(member.Id, new SceneLobby { MyTeam = initiator.GetTeamForPlayer(member.Id), GameMode = CatalogueHelper.ModeRanked.Key }, scene, true);
        }
        return true;
    }

    public void SendClanDraft(uint id)
    {
        if (UserConnected(id, out var player) && player.ActiveScene?.Type == SceneType.Lobby &&
            player.GameInstanceId is { } session && _matchmakerGames.TryGetValue(session, out var mode) && mode is ClanFightInitiator &&
            _gameInstances.TryGetValue(session, out var game) && game is GameInstance instance && instance.Lobby is { } lobby)
            GetClanService(id)?.SendExtension(4, ClanCompetition.DraftSnapshot(session, lobby.BannedHeroes.Select(k => k.Hash)));
    }

    public bool SendClanChat(uint id, string message)
    {
        var clan = ClanHub.Store.ClanOf(id);
        if (clan == null || !UserConnected(id, out var speaker) || !speaker.Online || string.IsNullOrWhiteSpace(message) || message.Length > 500) return false;
        long now = Environment.TickCount64, last = Interlocked.Read(ref speaker.LastGlobalMessage);
        if (now - last < 750 || Interlocked.CompareExchange(ref speaker.LastGlobalMessage, now, last) != last) return false;
        var json = JsonSerializer.Serialize(new { clanId = clan.Id, playerId = id, name = speaker.ChatInfo.Nickname, message });
        foreach (var member in clan.Members)
            if (UserConnected(member.PlayerId, out var recipient) && recipient.Online && !recipient.Ignored.ContainsKey(id))
                GetClanService(member.PlayerId)?.SendExtension(6, json);
        return true;
    }
}
