using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ControlPanel;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Servers;
using BNLReloadedServer.ServerTypes;
using BNLReloadedServer.Service;
using BNLReloadedServer.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moserware.Skills;
using NetCoreServer;

namespace BNLReloadedServer.Database;

public class RegionServerDatabase(AsyncTaskTcpServer server, AsyncTaskTcpServer matchServer) : IRegionServerDatabase
{
    private const int ScheduledNotificationDelayMs = 300;
    private volatile bool _matchmakingEnabled = true;

    private class ConnectionInfo(Guid guid, ChatPlayer chatInfo, bool isAdmin)
    {
        public Guid Guid { get; set; } = guid;

        public Guid? MatchGuid { get; set; }
        public UiId UiId { get; set; } = UiId.Home;
        public float UiDuration { get; set; }
        public ChatPlayer ChatInfo { get; } = chatInfo;
        public Scene? ActiveScene { get; set; }
        public ulong? CustomGameId { get; set; }
        public string? GameInstanceId { get; set; }
        public ulong? SquadId { get; set; }
        public Key? GameModeForSquadInvite { get; set; }
        public bool Online { get; set; } = true;
        public bool IsAdmin { get; } = isAdmin;

        // Chat mutes last for one match only, so they live here rather than in the player database.
        // Set by the muter's session thread, read by whichever thread is relaying a chat message.
        public ConcurrentDictionary<uint, byte> Ignored { get; } = new();

        // Players this session has an unanswered squad invite out to. Written by the inviter's
        // session thread and read by whichever thread finds the invite has become void.
        public ConcurrentDictionary<uint, byte> SentSquadInvites { get; } = new();
    }

    private readonly ConcurrentDictionary<uint, ConnectionInfo> _connectedUsers = new();
    private readonly ConcurrentDictionary<uint, ConcurrentQueue<string>> _scheduledNotifications = new();

    // Session teardown removes from these on arbitrary threads while request threads read them,
    // so the outer map is concurrent. The inner one stays a plain Dictionary: it is filled by the
    // session's service dispatcher constructor and never written again, only read.
    private readonly ConcurrentDictionary<Guid, Dictionary<ServiceId, IService>> _services = new();

    // Temporary container for services until we can connect the match session guid to a player
    private readonly ConcurrentDictionary<Guid, Dictionary<ServiceId, IService>> _matchServices = new();

    private readonly OrderedDictionary<ulong, (CustomGamePlayerGroup custom, ISender customSender)> _customGamePlayerLists = new();

    // Guards the dictionary operation only. CustomGamePlayerGroup holds its own lock and calls
    // back out while doing so, so this must never be held across a call into that class.
    private readonly Lock _customGamesLock = new();

    private readonly ConcurrentDictionary<string, IGameInstance> _gameInstances = new();
    private readonly ConcurrentDictionary<string, MatchmakerInitiator> _matchmakerGames = new();

    // Matchmaking games use the custom browser protocol for discovery, but are not custom games.
    // Keep their opaque browser ids in the upper half of ulong so they cannot collide with the
    // monotonically increasing ids assigned by CatalogueFactory to real custom games.
    private readonly ConcurrentDictionary<ulong, string> _spectatableMatchIds = new();
    private readonly ConcurrentDictionary<string, ulong> _spectatableMatches = new();
    private long _nextSpectatableMatchId = long.MinValue;

    private readonly ChatRoom _globalChatRoom = new(new RoomIdGlobal(), new SessionSender(server));

    private readonly IPlayerDatabase _playerDatabase = Databases.PlayerDatabase;

    private readonly Matchmaker _matchmaker = new(server);

    public bool MatchmakingEnabled => _matchmakingEnabled;

    private readonly ConcurrentDictionary<ulong, SquadData> _squads = new();

    private static ulong _nextSquadId;

    public bool UserConnected(uint userId) => _connectedUsers.ContainsKey(userId);
    private bool UserConnected(uint userId, [MaybeNullWhen(false)] out ConnectionInfo playerInfo) => _connectedUsers.TryGetValue(userId, out playerInfo);

    private bool TryGetCustomGame(ulong gameId, out (CustomGamePlayerGroup custom, ISender customSender) entry)
    {
        lock (_customGamesLock) return _customGamePlayerLists.TryGetValue(gameId, out entry);
    }

    private void AddCustomGameEntry(ulong gameId, (CustomGamePlayerGroup custom, ISender customSender) entry)
    {
        lock (_customGamesLock) _customGamePlayerLists.Add(gameId, entry);
    }

    private bool RemoveCustomGameEntry(ulong gameId)
    {
        lock (_customGamesLock) return _customGamePlayerLists.Remove(gameId);
    }

    private bool GetService<TService>(Guid guid, ServiceId id, [MaybeNullWhen(false)] out TService serviceCaller) where TService : IService
    {
        serviceCaller = default;
        if (!_services.TryGetValue(guid, out var service) ||
            !service.TryGetValue(id, out var serviceInstance)) return false;

        var res = serviceInstance is TService instance;
        if (res)
        {
            serviceCaller = (TService)serviceInstance;
        }
        return res;
    }

    public bool LinkMatchSessionGuidToUser(uint userId, Guid sessionId)
    {
        if (!UserConnected(userId, out var player)) return false;
        if (player.GameInstanceId == null) return false;
        _gameInstances.TryGetValue(player.GameInstanceId, out var gameInstance);
        if (gameInstance == null) return false;
        _matchServices.TryRemove(sessionId, out var matchServices);
        if (matchServices == null) return false;
        var scene = player.ActiveScene;
        if (scene == null || scene.Type == SceneType.MainMenu) return false;

        player.MatchGuid = sessionId;
        gameInstance.RegisterServices(sessionId, matchServices);
        // The match services are already claimed above, so a region session that tore down in the
        // meantime must not abort this — it just leaves nothing to forward chat through.
        _services.TryGetValue(player.Guid, out var regionServices);
        gameInstance.RegisterServices(player.Guid,
            new Dictionary<ServiceId, IService>(regionServices?.Where(s => s.Key == ServiceId.ServiceChat) ?? []));
        gameInstance.LinkGuidToPlayer(userId, sessionId, player.Guid);
        if (scene.Type == SceneType.Lobby || (scene.Type == SceneType.Zone && gameInstance.HasLobby()))
        {
            gameInstance.UserEnteredLobby(userId);
        }

        return true;
    }

    public Guid? GetSessionGuid(uint playerId) => UserConnected(playerId, out var playerInfo) ? playerInfo.Guid : null;

    public IServicePlayer? GetPlayerService(uint playerId) =>
        UserConnected(playerId, out var playerInfo) &&
        GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out var service)
            ? service
            : null;

    public IServiceChat? GetChatService(uint playerId) =>
        UserConnected(playerId, out var playerInfo) &&
        GetService<IServiceChat>(playerInfo.Guid, ServiceId.ServiceChat, out var service)
            ? service
            : null;

    public bool SchedulePlayerNotification(uint playerId, string message)
    {
        if (GetPlayerService(playerId) != null) return false;

        _scheduledNotifications.GetOrAdd(playerId, _ => new ConcurrentQueue<string>()).Enqueue(message);
        QueueScheduledNotificationDelivery(playerId);
        return true;
    }

    public int BroadcastPlayerNotification(string message)
    {
        var sent = 0;
        foreach (var playerId in _connectedUsers.Keys)
        {
            if (GetPlayerService(playerId) is not { } service) continue;
            service.SendNotification(new NotificationZeus { Text = message });
            sent++;
        }

        return sent;
    }

    public void SetMatchmakingEnabled(bool enabled)
    {
        _matchmakingEnabled = enabled;

        foreach (var playerId in _connectedUsers.Keys)
        {
            if (!UserConnected(playerId, out var playerInfo)) continue;

            if (!enabled &&
                GetService<IServiceMatchmaker>(playerInfo.Guid, ServiceId.ServiceMatchmaker, out var matchmaker))
            {
                _matchmaker.RemovePlayer(playerId, matchmaker);
            }

            if (GetService<IServiceScene>(playerInfo.Guid, ServiceId.ServiceScene, out var scene))
            {
                scene.SendServerUpdate(new ServerUpdate
                {
                    PlayButtonEnabled = enabled,
                    FriendlyEnabled = enabled,
                    RankedEnabled = enabled
                });
            }
        }
    }

    private void QueueScheduledNotificationDelivery(uint playerId) =>
        _ = DeliverScheduledNotificationsAfterDelay(playerId);

    private async Task DeliverScheduledNotificationsAfterDelay(uint playerId)
    {
        try
        {
            await Task.Delay(ScheduledNotificationDelayMs);
            DeliverScheduledNotifications(playerId);
        }
        catch (Exception ex)
        {
            Log.Error(LogCat.Net,
                $"Failed to deliver scheduled notification for player {playerId}", ex);
        }
    }

    private void DeliverScheduledNotifications(uint playerId)
    {
        var service = GetPlayerService(playerId);
        if (service == null || !_scheduledNotifications.TryGetValue(playerId, out var messages)) return;

        while (messages.TryDequeue(out var message))
            service.SendNotification(new NotificationZeus { Text = message });

        _scheduledNotifications.TryRemove(new KeyValuePair<uint, ConcurrentQueue<string>>(playerId, messages));
    }

    public bool AddUser(uint userId, Guid sessionId)
    {
        var result = true;
        if (!UserConnected(userId, out var playerInfo))
        {
            var chatPlayer = new ChatPlayer
            {
                Nickname = _playerDatabase.GetPlayerName(userId),
                PlayerId = userId
            };

            var data = _playerDatabase.GetPlayerDataNoWait(userId);

            result = _connectedUsers.TryAdd(userId, new ConnectionInfo(sessionId, chatPlayer, data is { Role: PlayerRole.Admin or PlayerRole.Core }));
        }
        else
        {
            playerInfo.Guid = sessionId;
            playerInfo.Online = true;
            // The squad still holds the session this player had before they dropped, which is dead
            // now. Without this they stay listed for everyone else but never hear from the squad again.
            if (playerInfo.SquadId is { } squadId && _squads.TryGetValue(squadId, out var squad))
            {
                squad.RebindPlayer(userId, sessionId);
            }
        }

        QueueScheduledNotificationDelivery(userId);
        LiveStateChanged();
        return result;
    }

    // sessionId is the session asking to be torn down. A player who reconnected before their old
    // session finished dying is already back under a new one, and tearing that down would log out
    // and unsquad someone who is online.
    public bool RemoveUser(uint userId, Guid? sessionId = null)
    {
        if (!UserConnected(userId, out var playerInfo)) return false;
        if (sessionId is not null && playerInfo.Guid != sessionId) return false;

        playerInfo.Online = false;
        LiveStateChanged();
        _matchmaker.RemovePlayer(userId, null);
        var customId = playerInfo.CustomGameId;
        var gameInstanceId = playerInfo.GameInstanceId;
        if (customId.HasValue && TryGetCustomGame(customId.Value, out var list))
        {
            if (list.custom.GameInfo.Status != CustomGameStatus.Preparing) return false;
            RemoveFromCustomGame(userId);
        }
        else if (gameInstanceId is not null && _gameInstances.TryGetValue(gameInstanceId, out var gameInstance))
        {
            if (gameInstance.HasLobby() && !gameInstance.IsOver()) return false;
            gameInstance.PlayerLeftInstance(userId, KickReason.MatchQuit);
        }

        // Read before the removal, because leaving the squad needs the connection info that is
        // about to go. The paths above that bail out keep the seat on purpose: those players are
        // still in a match and get their session rebound when they come back.
        var playerSquadId = playerInfo.SquadId;
        CancelAllSquadInvites(userId, playerInfo);
        if (!_connectedUsers.TryRemove(userId, out _)) return false;

        LeaveSquadOnDisconnect(userId, playerSquadId);
        return true;
    }

    // A dropped player has to give up their squad seat: nothing can reach them any more, the
    // leader cannot kick a member the server no longer knows, and they come back squadless while
    // everyone else still sees them in the list.
    private void LeaveSquadOnDisconnect(uint playerId, ulong? squadId)
    {
        if (squadId is null || !_squads.TryGetValue(squadId.Value, out var squad)) return;

        RemoveSquadFromQueue(squadId.Value);
        squad.RemovePlayer(playerId);
    }

    public bool UpdateChatName(uint userId, string newName)
    {
        if (!UserConnected(userId, out var playerInfo)) return false;

        playerInfo.ChatInfo.Nickname = newName;
        return true;
    }

    public async Task NotifyFriends(uint playerId)
    {
        foreach (var friend in (await _playerDatabase.GetFriends(playerId)).Select(p => p.PlayerId))
        {
            var info = await _playerDatabase.GetFriends(friend);
            if (UserConnected(friend, out var friendInfo) &&
                GetService<IServicePlayer>(friendInfo.Guid, ServiceId.ServicePlayer, out var servicePlayer))
            {
                servicePlayer.SendPlayerUpdate(new PlayerUpdate
                {
                    Friends = info
                });
            }
        }
    }

    public void NotifyLeague(uint playerId, League league)
    {
        if (UserConnected(playerId, out var playerInfo) &&
            GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out var servicePlayer))
        {
            servicePlayer.SendPlayerUpdate(new PlayerUpdate
            {
                League = league
            });
        }
    }

    public async Task NotifyRequests(uint playerId, bool requestsForMe, bool requestsFromMe)
    {
        if (!UserConnected(playerId, out var playerInfo) ||
            !GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out var servicePlayer)) return;

        servicePlayer.SendPlayerUpdate(new PlayerUpdate
        {
            RequestsFromFriends = requestsForMe ? await _playerDatabase.GetFriendRequestsFor(playerId) : null,
            RequestsFromMe = requestsFromMe ? await _playerDatabase.GetFriendRequestsFrom(playerId) : null
        });
    }

    public void UserUiChanged(uint userId, UiId uiId, float duration)
    {
        if (!UserConnected(userId, out var info)) return;
        info.UiId = uiId;
        info.UiDuration = duration;
    }

    public UiId? GetUiId(uint userId)
    {
        if (!UserConnected(userId, out var info)) return null;
        return info.UiId;
    }

    public bool UpdateScene(uint userId, Scene scene, IServiceScene sceneService, bool enterInstance)
    {
        if (!UserConnected(userId, out var info)) return false;
        info.ActiveScene = scene;
        LiveStateChanged();
        sceneService.SendChangeScene(scene);
        NotifyFriends(userId);
        switch (scene.Type)
        {
            case SceneType.Lobby:
            case SceneType.Zone:
                if (enterInstance)
                    sceneService.SendEnterInstance(Databases.ConfigDatabase.MasterPublicHost(), 28102,
                        _playerDatabase.GetAuthTokenForPlayer(userId));
                break;
            case SceneType.MainMenu when info.GameInstanceId != null:
                if (_gameInstances.TryGetValue(info.GameInstanceId, out var instance))
                {
                    instance.PlayerLeftInstance(userId, KickReason.MatchQuit);
                }
                break;
        }
        return true;
    }

    // forceEnterInstance is for a zone that replaces another one the player never left: the scene
    // does not change from the menu, but the client still has to reconnect to the new instance.
    public bool UpdateScene(uint userId, Scene scene, bool forceEnterInstance = false)
    {
        if (!UserConnected(userId, out var playerInfo)) return false;
        var guid = playerInfo.Guid;
        var oldScene = playerInfo.ActiveScene;
        playerInfo.ActiveScene = scene;
        LiveStateChanged();
        if (scene.Type == SceneType.MainMenu && !playerInfo.Online)
        {
            RemoveUser(userId);
        }
        return GetService<IServiceScene>(guid, ServiceId.ServiceScene, out var sceneService) &&
               UpdateScene(userId, scene, sceneService, forceEnterInstance || oldScene is SceneMainMenu);
    }

    public Scene GetLastScene(uint userId)
    {
        if (!UserConnected(userId, out var playerInfo)) return new SceneMainMenu();
        return playerInfo.GameInstanceId != null ? playerInfo.ActiveScene ?? new SceneMainMenu() : new SceneMainMenu();
    }

    public void UserEnterScene(uint userId)
    {
        if (!UserConnected(userId, out var player)) return;
        var scene = player.ActiveScene;
        if (scene == null) return;
        switch (scene.Type)
        {
            case SceneType.MainMenu:
                // A client that has just reached the menu, after logging back in or leaving a
                // match, may have missed the squad state pushed while it was still loading.
                if (player.SquadId is { } squadId && _squads.TryGetValue(squadId, out var squad))
                {
                    squad.SendUpdateTo(userId);
                }

                break;
            case SceneType.Lobby:
                break;
            case SceneType.Zone:
                if (player.GameInstanceId != null &&
                    _gameInstances.TryGetValue(player.GameInstanceId, out var instance))
                {
                    instance.PlayerEnterScene(userId);
                }

                break;
        }
    }

    public bool RegisterService(Guid sessionId, IService service, ServiceId serviceId) =>
        _services.GetOrAdd(sessionId, _ => new Dictionary<ServiceId, IService>()).TryAdd(serviceId, service);

    public bool RegisterMatchService(Guid sessionId, IService service, ServiceId serviceId) =>
        _matchServices.GetOrAdd(sessionId, _ => new Dictionary<ServiceId, IService>()).TryAdd(serviceId, service);

    public void RemoveServices(Guid sessionId) => _services.TryRemove(sessionId, out _);

    public void RemoveMatchServices(Guid sessionId)
    {
        _matchServices.TryRemove(sessionId, out _);
        var player = _connectedUsers.Values.FirstOrDefault(p => p.MatchGuid == sessionId);
        if (player?.GameInstanceId == null) return;
        _gameInstances.TryGetValue(player.GameInstanceId, out var gameInstance);
        gameInstance?.RemoveService(sessionId);
        gameInstance?.RemoveService(player.Guid);
    }

    public List<CustomGameInfo> GetCustomGames()
    {
        List<CustomGameInfo> games;
        lock (_customGamesLock)
            games = _customGamePlayerLists.Values.Select(x => x.custom.GameInfo).ToList();

        foreach (var (instanceId, initiator) in _matchmakerGames)
        {
            if (!_gameInstances.TryGetValue(instanceId, out var instance) || !instance.IsStarted || instance.IsOver())
                continue;

            var gameMode = initiator.GetGameMode().GetCard<CardGameMode>();
            if (gameMode?.Ranking is not (GameRankingType.Friendly or GameRankingType.Ranked)) continue;
            var mapInfo = instance.GetMapInfo();
            if (mapInfo == null) continue;

            var browserId = _spectatableMatches.GetOrAdd(instanceId,
                _ => unchecked((ulong)Interlocked.Increment(ref _nextSpectatableMatchId)));
            _spectatableMatchIds[browserId] = instanceId;
            var match = gameMode.MatchMode.GetCard<CardMatch>();
            var modeName = gameMode.Ranking == GameRankingType.Ranked
                ? CatalogueHelper.ModeNameRanked
                : CatalogueHelper.ModeNameFriendly;
            games.Add(new CustomGameInfo
            {
                Id = browserId,
                GameName = $"{modeName} (spectate only)",
                StarterNickname = "Matchmaking",
                Players = initiator.PlayerCount,
                MaxPlayers = initiator.MaxPlayers,
                Private = false,
                MapInfo = mapInfo,
                BuildTime = match?.Data switch
                {
                    MatchDataShieldCapture data => data.Build1Time,
                    MatchDataShieldRush2 data => data.Build1Time,
                    _ => 0
                },
                RespawnTimeMod = 0,
                HeroSwitch = false,
                SuperSupply = false,
                AllowBackfilling = false,
                ForceThirdPerson = false,
                ResourceCap = match?.ResourceCap ?? 7500,
                InitResource = match?.InitResource ?? 2000,
                Status = CustomGameStatus.Match
            });
        }

        return games;
    }

    private bool GetCustomGame(uint playerId, [MaybeNullWhen(false)] out CustomGamePlayerGroup custom)
    {
        custom = null;
        if (!UserConnected(playerId, out var info)) return false;
        var playerCustomGame = info.CustomGameId;
        if (playerCustomGame != null && TryGetCustomGame(playerCustomGame.Value, out var cust))
        {
            custom = cust.custom;
        }
        else
        {
            custom = null;
        }

        return custom != null;
    }

    public ulong? AddCustomGame(string name, string password, uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return null;
        var newCustom = CatalogueFactory.CreateCustomGame(name, password,
            Databases.PlayerDatabase.GetPlayerProfile(playerId).Nickname ?? string.Empty);
        var playerGuid = playerInfo.Guid;
        var sender = new SessionSender(server);
        sender.Subscribe(playerGuid);
        var matchService = new ServiceMatchmaker(sender);
        var customRoom = new RoomIdCustomGame
        {
            CustomGameId = newCustom.Id
        };

        var playerGroup = new CustomGamePlayerGroup(matchService)
        {
            Password = password,
            GameInfo = newCustom,
            ChatRoom = new ChatRoom(customRoom, new SessionSender(server))
        };
        playerInfo.CustomGameId = newCustom.Id;
        playerGroup.AddPlayer(playerId, true, _playerDatabase.GetPlayerProfile(playerId));
        if (GetService<IServiceChat>(playerGuid, ServiceId.ServiceChat, out var chatService))
        {
            playerGroup.ChatRoom.AddToRoom(playerGuid, chatService);
        }

        AddCustomGameEntry(newCustom.Id, (playerGroup, sender));
        LiveStateChanged();
        return newCustom.Id;
    }

    public bool RemoveCustomGame(ulong gameId)
    {
        if (!TryGetCustomGame(gameId, out var list)) return false;
        foreach (var playerId in list.custom.PlayersSnapshot().Select(player => player.Id))
        {
            if (UserConnected(playerId, out _))
                RemoveFromCustomGame(playerId);
        }
        return RemoveCustomGameEntry(gameId);
    }

    public CustomGameJoinResult AddToCustomGame(uint playerId, ulong gameId, string password)
    {
        // The client can invoke Join by double-clicking a disabled browser row. Matchmaking
        // browser entries are deliberately spectate-only, so reject that path explicitly.
        if (_spectatableMatchIds.ContainsKey(gameId)) return CustomGameJoinResult.GameStarted;
        if (!UserConnected(playerId, out var playerInfo) || !TryGetCustomGame(gameId, out var customGame)) return CustomGameJoinResult.NoSuchGame;
        var playerGuid = playerInfo.Guid;
        if (password != customGame.custom.Password && !playerInfo.IsAdmin) return CustomGameJoinResult.WrongPassword;
        if (!customGame.custom.GameInfo.AllowBackfilling && !playerInfo.IsAdmin &&
            customGame.custom.GameInfo.Status != CustomGameStatus.Preparing) return CustomGameJoinResult.GameStarted;
        // Subscribed first so the roster broadcast from AddPlayer reaches the joiner too.
        customGame.customSender.Subscribe(playerGuid);
        var joinResult = customGame.custom.AddPlayer(playerId, false, _playerDatabase.GetPlayerProfile(playerId));
        if (joinResult != CustomGameJoinResult.Accepted)
        {
            customGame.customSender.Unsubscribe(playerGuid);
            return joinResult;
        }

        playerInfo.CustomGameId = gameId;
        if (GetService<IServiceChat>(playerGuid, ServiceId.ServiceChat, out var chatService))
        {
            customGame.custom.ChatRoom.AddToRoom(playerGuid, chatService);
        }
        LiveStateChanged();
        return CustomGameJoinResult.Accepted;
    }

    public bool BackfillCustomGame(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return false;
        var gameId = customId.Value;
        if (!TryGetCustomGame(gameId, out var customGame)) return false;
        var playerGuid = playerInfo.Guid;
        playerInfo.GameInstanceId = customGame.custom.GameInstanceId;

        if (!GetService<IServiceScene>(playerGuid, ServiceId.ServiceScene, out var sceneService)) return false;
        var scene = new SceneLobby
        {
            MyTeam = customGame.custom.GetTeamForPlayer(playerId),
            GameMode = CatalogueHelper.ModeCustom.Key
        };
        UpdateScene(playerId, scene, sceneService, true);
        return true;
    }

    public CustomGameSpectateResult CheckSpectateCustomGame(uint playerId, ulong gameId, string password)
    {
        if (_spectatableMatchIds.TryGetValue(gameId, out var instanceId))
        {
            if (!UserConnected(playerId, out _) ||
                !_matchmakerGames.TryGetValue(instanceId, out var initiator) ||
                !_gameInstances.TryGetValue(instanceId, out var instance) || !instance.IsStarted || instance.IsOver())
                return CustomGameSpectateResult.NoSuchGame;
            return initiator.IsMaxSpectators()
                ? CustomGameSpectateResult.TooManySpectators
                : CustomGameSpectateResult.Accepted;
        }

        if (!UserConnected(playerId, out var playerInfo) || !TryGetCustomGame(gameId, out var customGame))
            return CustomGameSpectateResult.NoSuchGame;

        if (password != customGame.custom.Password && !playerInfo.IsAdmin) return CustomGameSpectateResult.WrongPassword;
        if (customGame.custom.IsMaxSpectators()) return CustomGameSpectateResult.TooManySpectators;
        return customGame.custom.GameInfo.Status switch
        {
            CustomGameStatus.Preparing => CustomGameSpectateResult.GameNotStartedYet,
            CustomGameStatus.Lobby => CustomGameSpectateResult.GameInLobbyYet,
            CustomGameStatus.Match => CustomGameSpectateResult.Accepted,
            _ => CustomGameSpectateResult.NoSuchGame
        };
    }

    public bool SpectateCustomGame(uint playerId, ulong gameId)
    {
        if (_spectatableMatchIds.TryGetValue(gameId, out var instanceId))
        {
            if (!UserConnected(playerId, out var info) ||
                !_matchmakerGames.TryGetValue(instanceId, out var initiator) ||
                !_gameInstances.TryGetValue(instanceId, out var match) || !match.IsStarted || match.IsOver() ||
                !initiator.AddSpectator(playerId)) return false;

            info.CustomGameId = null;
            info.GameInstanceId = instanceId;
            match.SendUserToZone(playerId);
            return true;
        }

        if (!UserConnected(playerId, out var playerInfo) || !TryGetCustomGame(gameId, out var customGame) ||
            customGame.custom.GameInstanceId is null ||
            !_gameInstances.TryGetValue(customGame.custom.GameInstanceId, out var instance)) return false;

        if (!customGame.custom.AddSpectator(playerId)) return false;
        playerInfo.CustomGameId = gameId;
        playerInfo.GameInstanceId = customGame.custom.GameInstanceId;
        instance.SendUserToZone(playerId);
        return true;
    }

    public bool RemoveFromCustomGame(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return false;
        var gameId = customId.Value;
        if (!TryGetCustomGame(gameId, out var customGame)) return false;
        var playerGuid = playerInfo.Guid;
        if (GetService<IServiceChat>(playerGuid, ServiceId.ServiceChat, out var chatService))
        {
            customGame.custom.ChatRoom.RemoveFromRoom(playerGuid, chatService);
        }

        customGame.custom.RemoveSpectator(playerId);
        customGame.custom.RemovePlayer(playerId);
        customGame.customSender.Unsubscribe(playerGuid);

        playerInfo.CustomGameId = null;
        GetService<IServiceMatchmaker>(playerGuid, ServiceId.ServiceMatchmaker, out var matchService);
        matchService?.SendExitCustomGame();
        LiveStateChanged();
        return true;
    }

    public bool KickFromCustomGame(uint playerId, uint kickerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return false;
        var gameId = customId.Value;
        if (!TryGetCustomGame(gameId, out var customGame)) return false;
        if (!customGame.custom.KickPlayer(playerId, kickerId)) return false;
        var playerGuid = playerInfo.Guid;
        customGame.customSender.Unsubscribe(playerGuid);
        if (GetService<IServiceChat>(playerGuid, ServiceId.ServiceChat, out var chatService))
        {
            customGame.custom.ChatRoom.RemoveFromRoom(playerGuid, chatService);
        }
        playerInfo.CustomGameId = null;
        GetService<IServiceMatchmaker>(playerGuid, ServiceId.ServiceMatchmaker, out var matchService);
        matchService?.SendExitCustomGame();
        LiveStateChanged();
        return true;
    }

    public bool SwitchTeam(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return false;
        var gameId = customId.Value;
        if (!TryGetCustomGame(gameId, out var customGame)) return false;
        customGame.custom.SwapTeam(playerId);
        return true;
    }

    public bool UpdateCustomSettings(uint playerId, CustomGameSettings settings)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return false;
        var gameId = customId.Value;
        if (!TryGetCustomGame(gameId, out var customGame)) return false;
        customGame.custom.UpdateSettings(playerId, settings);
        return true;
    }

    public bool UpdateCustomThirdPerson(uint playerId, bool enabled)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return false;
        if (!TryGetCustomGame(customId.Value, out var customGame)) return false;
        customGame.custom.UpdateThirdPerson(playerId, enabled);
        return true;
    }

    public CustomGameUpdate? GetFullCustomGameUpdate(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return null;
        var customId = playerInfo.CustomGameId;
        if (!customId.HasValue) return null;
        var gameId = customId.Value;
        return TryGetCustomGame(gameId, out var customGame) ? customGame.custom.GetCustomGameUpdate() : null;
    }

    public bool StartCustomGame(uint playerId, string? signedMap)
    {
        if (!UserConnected(playerId, out var info)) return false;
        var customId = info.CustomGameId;
        if (!customId.HasValue) return false;
        var gameId = customId.Value;
        if (!TryGetCustomGame(gameId, out var customGame)) return false;

        MapData? map = null;
        if (signedMap != null)
        {
            var handler = new JsonWebTokenHandler();
            var jsonWebToken = handler.ReadJsonWebToken(signedMap);

            var rawPayload = jsonWebToken.EncodedPayload;
            var mapJson = Base64UrlEncoder.Decode(rawPayload);

            map = JsonSerializer.Deserialize<MapData>(mapJson, JsonHelper.DefaultSerializerSettings);
        }
        else if (customGame.custom.GameInfo.MapInfo is MapInfoCard mapCard)
        {
            map = Databases.MapDatabase.LoadMapData(mapCard.MapKey);
        }

        if (map == null || customGame.custom.GameInfo.MapInfo == null) return false;
        if (!customGame.custom.StartIntoLobby(playerId)) return false;

        var gameInstance = new GameInstance(matchServer, server, Guid.NewGuid().ToString(), customGame.custom);
        customGame.custom.GameInstanceId = gameInstance.GameInstanceId;

        gameInstance.SetMap(customGame.custom.GameInfo.MapInfo, map);
        gameInstance.CreateLobby(CatalogueHelper.ModeCustom.Key, customGame.custom.GameInfo.MapInfo);
        if (!_gameInstances.TryAdd(gameInstance.GameInstanceId, gameInstance)) return false;
        foreach (var player in customGame.custom.PlayersSnapshot())
        {
            if (!UserConnected(player.Id, out var playerInfo)) continue;
            playerInfo.GameInstanceId = gameInstance.GameInstanceId;
            var guid = playerInfo.Guid;
            if (!GetService<IServiceScene>(guid, ServiceId.ServiceScene, out var sceneService)) continue;
            var scene = new SceneLobby
            {
                MyTeam = player.Team,
                GameMode = CatalogueHelper.ModeCustom.Key
            };
            UpdateScene(player.Id, scene, sceneService, true);
        }
        return true;
    }

    public bool StartMapEditorGame(uint playerId, MapData map, Key heroKey, TeamType team)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var dummyInitiator = new DummyGameInitiator(CatalogueHelper.ModeCustom, map, team, true);
        var gameInstance = new GameInstance(matchServer, server, Guid.NewGuid().ToString(), dummyInitiator);
        dummyInitiator.GameInstanceId = gameInstance.GameInstanceId;
        gameInstance.SetMap(null, map);
        gameInstance.SetMatchKey(dummyInitiator.MatchCard.Key);
        _gameInstances.TryAdd(gameInstance.GameInstanceId, gameInstance);
        playerInfo.GameInstanceId = gameInstance.GameInstanceId;
        gameInstance.StartMatch([Databases.PlayerDatabase.GetDummyPlayerLobbyInfo(playerId, heroKey, team)]);
        LiveStateChanged();
        return true;
    }

    public bool StartTimeTrialGame(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        if (playerInfo.GameInstanceId != null) return false;
        return StartTimeTrialGame(playerId, playerInfo, false);
    }

    public bool RestartTimeTrialGame(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var instance = GetGameInstance(playerId);
        if (instance == null || instance.GetGameMode() != CatalogueHelper.ModeTimeTrial.Key) return false;
        // The client sends this for every frame the recall key is held, so a course that is still
        // loading is a restart already in flight — dropping those is what keeps one keypress from
        // spawning a zone per frame.
        if (!instance.IsStarted) return false;

        // Dropping the instance id first keeps the teardown from pushing the player back to the
        // main menu: the restarted zone replaces the old one directly.
        playerInfo.GameInstanceId = null;
        instance.PlayerLeftInstance(playerId, KickReason.MatchQuit);

        return StartTimeTrialGame(playerId, playerInfo, true);
    }

    private bool StartTimeTrialGame(uint playerId, ConnectionInfo playerInfo, bool restart)
    {
        var course = CatalogueHelper.GlobalLogic.TimeTrial?.Courses?.FirstOrDefault();
        if (course == null) return false;
        var map = Databases.MapDatabase.LoadMapData(course.Map);
        if (map == null) return false;

        var initiator = new DummyGameInitiator(CatalogueHelper.ModeTimeTrial, map, TeamType.Team1, false);
        var gameInstance = new GameInstance(matchServer, server, Guid.NewGuid().ToString(), initiator);
        initiator.GameInstanceId = gameInstance.GameInstanceId;
        // Unlike the map editor the map key has to travel with the instance: the zone resolves the
        // course, and with it the objectives, from the key it is started with.
        gameInstance.SetMap(new MapInfoCard { MapKey = course.Map }, map);
        gameInstance.SetMatchKey(initiator.MatchCard.Key);
        if (!_gameInstances.TryAdd(gameInstance.GameInstanceId, gameInstance)) return false;
        playerInfo.GameInstanceId = gameInstance.GameInstanceId;
        gameInstance.StartMatch(
            [Databases.PlayerDatabase.GetDummyPlayerLobbyInfo(playerId, course.Hero, TeamType.Team1,
                CatalogueHelper.GetCourseDevices(course))], restart);
        LiveStateChanged();
        return true;
    }

    public bool StartGameFromMatchmaker(CardGameMode gameMode, List<PlayerQueueData> team1, List<PlayerQueueData> team2)
    {
        var matchInitiator = new MatchmakerInitiator(gameMode, team1, team2);
        var gameInstance = new GameInstance(matchServer, server, Guid.NewGuid().ToString(), matchInitiator);
        matchInitiator.GameInstanceId = gameInstance.GameInstanceId;
        gameInstance.CreateLobby(gameMode.Key, null);
        if (!_matchmakerGames.TryAdd(gameInstance.GameInstanceId, matchInitiator) ||
            !_gameInstances.TryAdd(gameInstance.GameInstanceId, gameInstance)) return false;

        foreach (var player in team1)
        {
            if (!UserConnected(player.PlayerId, out var playerInfo)) continue;
            playerInfo.GameInstanceId = gameInstance.GameInstanceId;
            var guid = playerInfo.Guid;
            if (!GetService<IServiceScene>(guid, ServiceId.ServiceScene, out var sceneService)) continue;
            var scene = new SceneLobby
            {
                MyTeam = TeamType.Team1,
                GameMode = gameMode.Key
            };
            UpdateScene(player.PlayerId, scene, sceneService, true);
        }

        foreach (var player in team2)
        {
            if (!UserConnected(player.PlayerId, out var playerInfo)) continue;
            playerInfo.GameInstanceId = gameInstance.GameInstanceId;
            var guid = playerInfo.Guid;
            if (!GetService<IServiceScene>(guid, ServiceId.ServiceScene, out var sceneService)) continue;
            var scene = new SceneLobby
            {
                MyTeam = TeamType.Team2,
                GameMode = gameMode.Key
            };
            UpdateScene(player.PlayerId, scene, sceneService, true);
        }

        return true;
    }

    public bool BackfillMatchmakerGame(PlayerQueueData player, TeamType team, string gameInstanceId)
    {
        if (!UserConnected(player.PlayerId, out var playerInfo) || !_matchmakerGames.TryGetValue(gameInstanceId, out var initiator) ||
            !_gameInstances.TryGetValue(gameInstanceId, out var gameInstance) || gameInstance.IsOver()) return false;
        if (!initiator.AddPlayer(player, team)) return false;

        playerInfo.GameInstanceId = gameInstanceId;
        var guid = playerInfo.Guid;

        if (!GetService<IServiceScene>(guid, ServiceId.ServiceScene, out var sceneService))
        {
            initiator.RemovePlayer(player.PlayerId);
            return false;
        }

        var scene = new SceneLobby
        {
            MyTeam = team,
            GameMode = initiator.GetGameMode()
        };

        UpdateScene(player.PlayerId, scene, sceneService, true);
        return true;
    }

    private ChatRoom? GetChatRoom(uint playerId, RoomId roomId)
    {
        if (!UserConnected(playerId, out _)) return null;
        return roomId.Type switch
        {
            RoomIdType.Team => GetGameInstance(playerId)?.GetChatRoom(roomId),
            RoomIdType.Squad => GetSquad(playerId, out var squad)
                ? squad.ChatRoom.RoomId.Equals(roomId)
                    ? squad.ChatRoom
                    : null
                : null,
            RoomIdType.CustomGame => GetCustomGame(playerId, out var custom)
                ? custom.ChatRoom.RoomId.Equals(roomId)
                    ? custom.ChatRoom
                    : null
                : null,
            RoomIdType.Global => _globalChatRoom,
            _ => null
        };
    }

    public bool SendMessage(uint playerId, RoomId roomId, string message)
    {
        var chatRoom = GetChatRoom(playerId, roomId);
        if (chatRoom == null || !UserConnected(playerId, out var playerInfo)) return false;
        chatRoom.SendMessage(playerInfo.ChatInfo, message, IgnorersOf(playerId));
        return true;
    }

    // Sessions of everyone who has muted this speaker. A superset of any single room's members is
    // fine: SendExcept only filters the recipients it was going to send to anyway.
    private List<Guid> IgnorersOf(uint speakerId) =>
        _connectedUsers.Values
            .Where(info => info.Online && info.Ignored.ContainsKey(speakerId))
            .Select(info => info.Guid)
            .ToList();

    public bool SetIgnored(uint playerId, uint targetId, bool ignore)
    {
        if (playerId == targetId || !UserConnected(playerId, out var playerInfo)) return false;
        if (!(ignore ? playerInfo.Ignored.TryAdd(targetId, 0) : playerInfo.Ignored.TryRemove(targetId, out _)))
            return false;
        SendIgnores(playerInfo);
        return true;
    }

    // A player who reconnects mid-match keeps their mutes here but starts with an empty client-side
    // list, so it has to be pushed again once the new session's services are up.
    public void SendIgnoresTo(uint playerId)
    {
        if (UserConnected(playerId, out var playerInfo) && playerInfo.Ignored.Count > 0)
            SendIgnores(playerInfo);
    }

    private void SendIgnores(ConnectionInfo playerInfo)
    {
        if (!GetService<IServiceChat>(playerInfo.Guid, ServiceId.ServiceChat, out var chatService)) return;
        chatService.SendIgnores(playerInfo.Ignored.Keys
            .Select(id => new ChatPlayer { PlayerId = id, Nickname = _playerDatabase.GetPlayerName(id) })
            .ToList());
    }

    public PrivateMessageFailReason? SendMessage(uint playerId, uint receiver, string message)
    {
        if (!UserConnected(playerId, out var me) || !UserConnected(receiver, out var them))
            return PrivateMessageFailReason.Offline;
        if (them.GameInstanceId != null) return PrivateMessageFailReason.Match;
        if (them.Ignored.ContainsKey(playerId)) return PrivateMessageFailReason.Ignor;
        var chatPlayerMe = me.ChatInfo;
        var chatPlayerThem = them.ChatInfo;
        if (!GetService<IServiceChat>(me.Guid, ServiceId.ServiceChat, out var myChatService) ||
            !GetService<IServiceChat>(them.Guid, ServiceId.ServiceChat, out var theirChatService))
            return PrivateMessageFailReason.Offline;

        myChatService.SendPrivateMessage(chatPlayerMe, chatPlayerThem, message);
        theirChatService.SendPrivateMessage(chatPlayerMe, chatPlayerThem, message);
        return null;
    }

    public IGameInstance? GetGameInstance(uint? playerId)
    {
        if (playerId == null || !UserConnected(playerId.Value, out var playerInfo)) return null;
        var playerGameInstance = playerInfo.GameInstanceId;
        if (playerGameInstance == null)
            return null;
        _gameInstances.TryGetValue(playerGameInstance, out var instance);
        return instance;
    }

    public string? GetOnlinePlayerLocation(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo) || !playerInfo.Online) return null;

        if (playerInfo.GameInstanceId != null &&
            _gameInstances.TryGetValue(playerInfo.GameInstanceId, out var instance))
        {
            var location = instance.GetGameMode().GetCard<CardGameMode>()?.Ranking switch
            {
                GameRankingType.Friendly => "Casual game",
                GameRankingType.Ranked => "Ranked game",
                _ => "Custom game"
            };
            return instance.IsPlayerSpectator(playerId) ? "Spectating " + location : location;
        }

        return playerInfo.CustomGameId.HasValue ? "Custom game" : "Menu";
    }

    public bool RemoveGameInstance(string gameInstanceId)
    {
        foreach (var playerId in _connectedUsers.Keys)
        {
            RemoveFromGameInstance(playerId, gameInstanceId);
        }
        if (_spectatableMatches.TryRemove(gameInstanceId, out var browserId))
            _spectatableMatchIds.TryRemove(browserId, out _);
        var removed = _gameInstances.TryRemove(gameInstanceId, out _);
        if (removed) LiveStateChanged();
        return removed;
    }

    public bool RemoveFromGameInstance(uint playerId, string gameInstanceId)
    {
        if (!UserConnected(playerId, out var playerInfo)) return false;
        if (playerInfo.GameInstanceId != gameInstanceId) return false;
        playerInfo.GameInstanceId = null;
        if (playerInfo.Ignored.Count > 0)
        {
            playerInfo.Ignored.Clear();
            SendIgnores(playerInfo);
        }
        UpdateScene(playerId, new SceneMainMenu());
        LiveStateChanged();

        return true;
    }

    public IEnumerable<(Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2, string instanceId)> GetBackfillNeeded(Key gameModeKey)
    {
        var gameInstances = _gameInstances
            .Where(g => g.Value.GetGameMode() == gameModeKey && !g.Value.IsOver() && g.Value.NeedsBackfill()).ToList();
        if (gameInstances.Count == 0) yield break;

        foreach (var (instanceId, instance) in gameInstances)
        {
            var (team1, team2) = instance.GetTeamRatings();
            yield return (team1, team2, instanceId);
        }
    }

    public int GetActiveGamesCount(Key gameModeKey) => _gameInstances.Count(g => g.Value.GetGameMode() == gameModeKey);

    public List<QueueSnapshot> GetQueueSnapshot() => _matchmaker.GetQueueSnapshot();

    private static void LiveStateChanged() => ControlPanelEvents.Publish(
        ControlPanelEvent.Status | ControlPanelEvent.Activity | ControlPanelEvent.Players);

    public PlayerActivity GetPlayerActivity()
    {
        var byMode = new Dictionary<string, (string? Name, int Players)>();
        var online = 0;
        var inMenu = 0;
        var spectating = 0;

        foreach (var (playerId, info) in _connectedUsers)
        {
            if (!info.Online) continue;
            online++;

            if (info.GameInstanceId != null && _gameInstances.TryGetValue(info.GameInstanceId, out var instance))
            {
                if (instance.IsPlayerSpectator(playerId))
                {
                    spectating++;
                    continue;
                }

                var modeKey = instance.GetGameMode();
                var card = modeKey.GetCard<CardGameMode>();
                var id = card?.Id ?? modeKey.ToString();
                var entry = byMode.GetValueOrDefault(id, (Name: card?.Name, Players: 0));
                byMode[id] = (entry.Name, entry.Players + 1);
                continue;
            }

            // Before the host starts the match, a custom lobby has an ID but no game
            // instance. It is still a custom-game player, not someone in the menu.
            if (info.CustomGameId.HasValue)
            {
                var custom = CatalogueHelper.ModeCustom;
                var entry = byMode.GetValueOrDefault(custom.Id, (Name: custom.Name, Players: 0));
                byMode[custom.Id] = (entry.Name, entry.Players + 1);
                continue;
            }

            inMenu++;
        }

        return new PlayerActivity(online, inMenu,
            byMode.Select(kv => new ModePlayerCount(kv.Key, kv.Value.Name, kv.Value.Players))
                  .OrderByDescending(m => m.Players).ToList(),
            spectating);
    }

    public void JoinQueue(uint playerId, Key gameModeKey, IServiceMatchmaker serviceMatchmaker)
    {
        if (!MatchmakingEnabled) return;

        if (!UserConnected(playerId, out var playerInfo) || _playerDatabase.GetPlayerDataNoWait(playerId) is not { } playerData ||
            _playerDatabase.IsBanned(playerId)) return;

        if (playerInfo.SquadId is not null && _squads.TryGetValue(playerInfo.SquadId.Value, out var squad))
        {
            if (!squad.IsOwner(playerId) && !(CatalogueHelper.GlobalLogic.Squad?.MembersCanEnterQueue ?? false)) return;

            // The squad's own mode, not the caller's: a member whose client is out of date must not
            // drag everyone into a different queue.
            if (squad.GameMode.GetCard<CardGameMode>() != null)
            {
                gameModeKey = squad.GameMode;
            }

            foreach (var pId in squad.GetPlayers())
            {
                if (!UserConnected(pId, out var pInfo) || _playerDatabase.GetPlayerDataNoWait(pId) is not { } pData ||
                    _playerDatabase.IsBanned(pId) ||
                    !GetService<IServiceMatchmaker>(pInfo.Guid, ServiceId.ServiceMatchmaker, out var matchmaker))
                    continue;

                _matchmaker.AddPlayer(gameModeKey, pId, pInfo.Guid, pData.Rating, pInfo.SquadId, matchmaker);
            }
        }
        else
        {
            _matchmaker.AddPlayer(gameModeKey, playerId, playerInfo.Guid, playerData.Rating, playerInfo.SquadId, serviceMatchmaker);
        }
    }

    public void LeaveQueue(uint playerId, IServiceMatchmaker serviceMatchmaker)
    {
        if (UserConnected(playerId, out var playerInfo) && playerInfo.SquadId is not null &&
            _squads.TryGetValue(playerInfo.SquadId.Value, out var squad))
        {
            if (!squad.IsOwner(playerId) && !(CatalogueHelper.GlobalLogic.Squad?.MembersCanLeaveQueue ?? false)) return;

            var matchServices = new List<IServiceMatchmaker>();
            foreach (var pId in squad.GetPlayers())
            {
                if (!UserConnected(pId, out var pInfo) ||
                    !GetService<IServiceMatchmaker>(pInfo.Guid, ServiceId.ServiceMatchmaker, out var matchmaker))
                    continue;

                matchServices.Add(matchmaker);
            }
            _matchmaker.RemoveSquad(playerInfo.SquadId.Value, matchServices);
        }
        else
        {
            _matchmaker.RemovePlayer(playerId, serviceMatchmaker);
        }
    }

    public void EnableBackfilling(uint playerId, bool enable) => _matchmaker.SetDoBackfilling(playerId, enable);

    public void ConfirmMatch(uint playerId, bool confirm, IServiceMatchmaker serviceMatchmaker) =>
        _matchmaker.OnPopAccepted(playerId, confirm, serviceMatchmaker);

    public void ForceStartMatch(uint playerId)
    {
        if (!UserConnected(playerId, out _) ||
            _playerDatabase.GetPlayerDataNoWait(playerId)?.Role is not (PlayerRole.Admin or PlayerRole.Core)) return;

        _matchmaker.ForceStartGame(playerId);
    }

    private bool GetSquad(uint playerId, [MaybeNullWhen(false)] out SquadData squad)
    {
        squad = null;
        if (!UserConnected(playerId, out var playerInfo)) return false;
        var squadId = playerInfo.SquadId;

        return squadId.HasValue && _squads.TryGetValue(squadId.Value, out squad);
    }

    public ulong? CreateSquad(uint ownerId, List<uint> players)
    {
        if (!UserConnected(ownerId, out var ownerInfo) || ownerInfo.GameModeForSquadInvite is null) return null;
        var gameMode = ownerInfo.GameModeForSquadInvite.Value;
        var maxPlayers = CatalogueHelper.MaxPlayersInSquad(gameMode);

        // Everyone who can actually be put in the squad, worked out before anything is created so
        // a squad that would end up empty or solo is never left behind.
        var realPlayers = new List<uint>();
        foreach (var playerId in players)
        {
            if (realPlayers.Count >= maxPlayers) break;
            if (realPlayers.Contains(playerId) || !UserConnected(playerId, out var info) ||
                !GetService<IServiceChat>(info.Guid, ServiceId.ServiceChat, out _) ||
                !GetService<IServicePlayer>(info.Guid, ServiceId.ServicePlayer, out _)) continue;

            realPlayers.Add(playerId);
        }

        if (realPlayers.Count < 2) return null;

        var squadId = Interlocked.Increment(ref _nextSquadId);
        var sender = new SessionSender(server);
        var squadUpdater = new ServicePlayer(sender, new ServiceScene(sender), new ServiceTime(sender));

        var squadRoom = new RoomIdSquad
        {
            SquadId = squadId
        };

        var squad = new SquadData(squadId, gameMode, sender, squadUpdater)
        {
            ChatRoom = new ChatRoom(squadRoom, new SessionSender(server))
        };

        ownerInfo.GameModeForSquadInvite = null;
        _squads.TryAdd(squadId, squad);

        foreach (var playerId in realPlayers)
        {
            if (!UserConnected(playerId, out var playerInfo)) continue;

            LeaveSquad(playerId);
            if (GetService<IServiceMatchmaker>(playerInfo.Guid, ServiceId.ServiceMatchmaker, out var serviceMatchmaker))
            {
                _matchmaker.RemovePlayer(playerId, serviceMatchmaker);
            }

            // Set before the squad is told about them: leaving the old squad happens on that
            // squad's own thread and must not clear a membership that has already moved on.
            playerInfo.SquadId = squadId;
        }

        squad.AddPlayers(realPlayers, ownerId);
        CancelVoidedSquadInvites(squad, realPlayers);
        return squadId;
    }

    private void RemoveSquadFromQueue(ulong squadId)
    {
        if (!_squads.TryGetValue(squadId, out var squad)) return;
        var matchmakersServices = new List<IServiceMatchmaker>();
        foreach (var playerId in squad.GetPlayers())
        {
            if (!UserConnected(playerId, out var playerInfo)) continue;
            if (GetService<IServiceMatchmaker>(playerInfo.Guid, ServiceId.ServiceMatchmaker, out var serviceMatchmaker))
            {
                matchmakersServices.Add(serviceMatchmaker);
            }
        }

        _matchmaker.RemoveSquad(squadId, matchmakersServices);
    }

    public bool JoinSquad(uint playerId, ulong squadId)
    {
        if (!UserConnected(playerId, out var playerInfo) || !_squads.TryGetValue(squadId, out var squad) ||
            !GetService<IServiceChat>(playerInfo.Guid, ServiceId.ServiceChat, out _) ||
            !GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out _)) return false;

        if (playerInfo.SquadId == squadId) return true;
        if (squad.PlayerCount >= CatalogueHelper.MaxPlayersInSquad(squad.GameMode)) return false;

        // Whatever squad they were in before has to let go of them, or it keeps a member it can
        // no longer reach and the player ends up subscribed to two squads at once.
        LeaveSquad(playerId);

        if (GetService<IServiceMatchmaker>(playerInfo.Guid, ServiceId.ServiceMatchmaker, out var serviceMatchmaker))
        {
            _matchmaker.RemovePlayer(playerId, serviceMatchmaker);
        }

        RemoveSquadFromQueue(squadId);
        playerInfo.SquadId = squadId;
        squad.AddPlayer(playerId, false);

        // GetPlayers may or may not have the new member yet: the add runs on the squad's thread.
        var members = squad.GetPlayers();
        if (!members.Contains(playerId)) members.Add(playerId);
        CancelVoidedSquadInvites(squad, members);
        return true;
    }

    public bool LeaveSquad(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo) || playerInfo.SquadId is null ||
            !_squads.TryGetValue(playerInfo.SquadId.Value, out var squad)) return false;

        RemoveSquadFromQueue(playerInfo.SquadId.Value);
        squad.RemovePlayer(playerId);

        if (GetService<IServiceMatchmaker>(playerInfo.Guid, ServiceId.ServiceMatchmaker, out var matchmakerService))
        {
            _matchmaker.RemovePlayer(playerId, matchmakerService);
        }
        return true;
    }

    // Keyed off the kicker rather than the target: the target may be a member who dropped, and the
    // leader still has to be able to clear them out.
    public bool KickFromSquad(uint playerId, uint kickerId)
    {
        if (playerId == kickerId || !UserConnected(kickerId, out var kickerInfo) || kickerInfo.SquadId is null ||
            !_squads.TryGetValue(kickerInfo.SquadId.Value, out var squad) || !squad.IsOwner(kickerId) ||
            !squad.Contains(playerId)) return false;

        RemoveSquadFromQueue(kickerInfo.SquadId.Value);
        squad.RemovePlayer(playerId);

        if (!UserConnected(playerId, out var playerInfo)) return true;

        if (GetService<IServiceMatchmaker>(playerInfo.Guid, ServiceId.ServiceMatchmaker, out var matchmakerService))
        {
            _matchmaker.RemovePlayer(playerId, matchmakerService);
        }

        if (GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out var servicePlayer))
        {
            servicePlayer.SendNotifyKickFromSquad();
        }
        return true;
    }

    public bool SendSquadInvite(uint playerId, uint senderId, Key gameModeKey)
    {
        if (playerId == senderId || !UserConnected(playerId, out var playerInfo) ||
            !UserConnected(senderId, out var senderInfo) ||
            !GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out var playerService)) return false;

        if (senderInfo.SquadId is { } senderSquadId && _squads.TryGetValue(senderSquadId, out var squad))
        {
            if (!squad.IsOwner(senderId) && !(CatalogueHelper.GlobalLogic.Squad?.MembersCanInvite ?? false)) return false;
            if (squad.Contains(playerId) || squad.PlayerCount >= CatalogueHelper.MaxPlayersInSquad(squad.GameMode))
                return false;

            // The squad the invitee would be joining already has a mode; the client's is stale.
            gameModeKey = squad.GameMode;
        }

        senderInfo.GameModeForSquadInvite = gameModeKey;
        senderInfo.SentSquadInvites[playerId] = 0;
        playerService.SendNotifySquadInvite(senderId, _playerDatabase.GetPlayerName(senderId));
        return true;
    }

    // The client shows an invite until it is answered or withdrawn, so anything that makes an
    // invite impossible to honour has to withdraw it rather than leave the popup sitting there.
    private void CancelSquadInvite(uint senderId, ConnectionInfo senderInfo, uint receiverId)
    {
        if (!senderInfo.SentSquadInvites.TryRemove(receiverId, out _)) return;

        GetPlayerService(receiverId)?.SendNotifySquadInviteCancel(senderId);
    }

    private void CancelAllSquadInvites(uint senderId, ConnectionInfo senderInfo)
    {
        foreach (var receiverId in senderInfo.SentSquadInvites.Keys)
        {
            CancelSquadInvite(senderId, senderInfo, receiverId);
        }
    }

    // memberIds is the squad as it stands after the change, including anyone whose addition is
    // still queued, so nothing is decided on a stale roster.
    private void CancelVoidedSquadInvites(SquadData squad, List<uint> memberIds)
    {
        var full = memberIds.Count >= CatalogueHelper.MaxPlayersInSquad(squad.GameMode);

        foreach (var memberId in memberIds)
        {
            if (!UserConnected(memberId, out var memberInfo)) continue;

            foreach (var receiverId in memberInfo.SentSquadInvites.Keys)
            {
                // A full squad has nothing left to offer, and an invite to someone who is already
                // in the squad has nowhere to take them.
                if (full || memberIds.Contains(receiverId))
                {
                    CancelSquadInvite(memberId, memberInfo, receiverId);
                }
            }
        }
    }

    // playerId is the player who sent the invite, senderId the one answering it.
    public bool SendSquadInviteReply(uint playerId, uint senderId, SquadInviteReplyType reply)
    {
        if (!UserConnected(playerId, out var playerInfo) ||
            !GetService<IServicePlayer>(playerInfo.Guid, ServiceId.ServicePlayer, out var playerService)) return false;
        playerService.SendNotifySquadInviteReply(senderId, reply, _playerDatabase.GetPlayerName(senderId));

        // Answered, so it no longer needs withdrawing: the client dropped it when it replied.
        playerInfo.SentSquadInvites.TryRemove(senderId, out _);

        if (reply is not SquadInviteReplyType.Accepted) return true;

        if (playerInfo.SquadId is null)
        {
            CreateSquad(playerId, [senderId, playerId]);
        }
        else
        {
            JoinSquad(senderId, playerInfo.SquadId.Value);
        }
        return true;
    }

    public bool SetSquadGamemode(uint playerId, Key gameModeKey)
    {
        if (!UserConnected(playerId, out var playerInfo) || playerInfo.SquadId is null ||
            !_squads.TryGetValue(playerInfo.SquadId.Value, out var squad) || !squad.IsOwner(playerId)) return false;

        // A mode the squad is already too big for would leave it unable to queue at all.
        if (squad.PlayerCount > CatalogueHelper.MaxPlayersInSquad(gameModeKey)) return false;

        squad.ChangeGameMode(gameModeKey);
        return true;
    }

    // Returns whether the player was still in that squad: one that is being torn down must not
    // clear a membership the player has already moved on to.
    public bool ClearSquadId(uint playerId, ulong squadId)
    {
        if (!UserConnected(playerId, out var playerInfo) || playerInfo.SquadId != squadId) return false;
        playerInfo.SquadId = null;
        return true;
    }

    public void CloseSquad(ulong squadId) => _squads.Remove(squadId, out _);

    public ulong? GetSquadId(uint playerId) => UserConnected(playerId, out var playerInfo) ? playerInfo.SquadId : null;

    public bool IsSquadLeader(uint playerId) => GetSquad(playerId, out var squad) && squad.IsOwner(playerId);

    public void SendAfkWarning(uint playerId, string gameInstanceId)
    {
        if (_gameInstances.TryGetValue(gameInstanceId, out var gameInstance))
            gameInstance.SendAfkWarning(playerId);
    }

    public bool IsUserOnline(uint playerId) => UserConnected(playerId, out var playerInfo) && playerInfo.Online;

    public void RemoveOfflineUser(uint playerId)
    {
        if (!UserConnected(playerId, out var playerInfo) || playerInfo.Online) return;
        RemoveFromCustomGame(playerId);
        var playerSquadId = playerInfo.SquadId;
        CancelAllSquadInvites(playerId, playerInfo);
        if (!_connectedUsers.TryRemove(playerId, out _)) return;

        LeaveSquadOnDisconnect(playerId, playerSquadId);
    }

    public void FreeMatchmakerSlot(uint playerId, string gameInstanceId)
    {
        if (_matchmakerGames.TryGetValue(gameInstanceId, out var initiator))
            initiator.RemovePlayer(playerId);
    }

    public void KickForAfk(uint playerId, string gameInstanceId)
    {
        if (_gameInstances.TryGetValue(gameInstanceId, out var gameInstance))
        {
            gameInstance.PlayerLeftInstance(playerId, KickReason.MatchInactivity);
        }
    }
}
