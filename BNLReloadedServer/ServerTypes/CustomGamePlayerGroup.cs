using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.Service;
using Moserware.Skills;

namespace BNLReloadedServer.ServerTypes;

// State is only touched under _lock, and every member finishes its work before returning.
// This was an Updater; queueing mutations meant AddPlayer returned before the player
// existed, and the join path raced its own insert reading the roster back.
// Sends stay inside the lock so clients see updates in the order the changes happened —
// SendPacket only queues to a per-session channel. Anything that blocks or re-enters this
// class (CloseCustomGame, map loading, disk) must stay outside.
public class CustomGamePlayerGroup(IServiceMatchmaker matchService) : IGameInitiator
{
    private readonly Lock _lock = new();

    public required string Password { get; init; }

    // Written only under _lock. Fields stay readable from outside for the lobby browser,
    // where a momentarily stale value is harmless — an update follows every change.
    public required CustomGameInfo GameInfo
    {
        get;
        init
        {
            field = value;
            Send(BuildUpdate(gameName: value.GameName, pass: Password, mapInfo: value.MapInfo, buildTime: value.BuildTime,
                respawnMod: value.RespawnTimeMod, heroSwitch: value.HeroSwitch, superSupply: value.SuperSupply,
                allowBackfilling: value.AllowBackfilling, resourceCap: value.ResourceCap, initResources: value.InitResource,
                players: [], status: value.Status, forceThirdPerson: value.ForceThirdPerson));
        }
    }

    private readonly List<CustomGamePlayer> _players = [];
    private readonly List<uint> _spectators = [];

    private string? _gameInstanceId;

    public string? GameInstanceId
    {
        get { lock (_lock) return _gameInstanceId; }
        set { lock (_lock) _gameInstanceId = value; }
    }

    public required ChatRoom ChatRoom { get; init; }

    private readonly Queue<CustomGamePlayer> _changeTeamRequestsTeam1 = new();
    private readonly Queue<CustomGamePlayer> _changeTeamRequestsTeam2 = new();

    private readonly CustomGameLogic _customLogic = CatalogueHelper.GlobalLogic.CustomGame!;

    // A copy of the roster. The caller owns it; the lobby never mutates it again.
    public List<CustomGamePlayer> PlayersSnapshot()
    {
        lock (_lock) return [.. _players];
    }

    private TeamType GetBalancedTeam()
    {
        var team1Count = _players.Count(p => p.Team == TeamType.Team1);
        var team2Count = _players.Count(p => p.Team == TeamType.Team2);

        return team1Count <= team2Count ? TeamType.Team1 : TeamType.Team2;
    }

    // The capacity check lives here so checking and adding are one atomic step — split
    // across the caller it let two simultaneous joins past a full lobby.
    public CustomGameJoinResult AddPlayer(uint playerId, bool isOwner, ProfileData player)
    {
        lock (_lock)
        {
            if (_players.Any(p => p.Id == playerId))
                return CustomGameJoinResult.Accepted;
            if (_players.Count >= GameInfo.MaxPlayers)
                return CustomGameJoinResult.FullTeams;

            _players.Add(new CustomGamePlayer
            {
                Id = playerId,
                SteamId = player.SteamId,
                Nickname = player.Nickname,
                PlayerLevel = player.Progression!.PlayerProgress.Level,
                SelectedBadges = player.SelectedBadges,
                Owner = isOwner,
                Team = GetBalancedTeam(),
                SwitchTeamRequest = false
            });
            GameInfo.Players = _players.Count;

            Send(BuildUpdate(players: [.. _players]));
        }
        return CustomGameJoinResult.Accepted;
    }

    public bool KickPlayer(uint playerId, uint kickerId)
    {
        lock (_lock)
        {
            var kicker = _players.FirstOrDefault(p => p.Id == kickerId);
            if (kicker is not { Owner: true }) return false;
        }
        RemovePlayer(playerId);
        matchService.SendCustomGamePlayerKicked(playerId);
        return true;
    }

    public bool RemovePlayer(uint playerId)
    {
        bool closing;
        lock (_lock)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
                return false;

            _players.Remove(player);
            GameInfo.Players = _players.Count;

            closing = _players.Count <= 0;
            if (!closing)
            {
                if (player.Owner)
                    _players[0].Owner = true;

                // Their seat just freed up, so whoever is queued to move the other way can go.
                var waiting = player.Team switch
                {
                    TeamType.Team1 => _changeTeamRequestsTeam2,
                    TeamType.Team2 => _changeTeamRequestsTeam1,
                    _ => null
                };

                // A queued request can name someone who has since left, in which case the
                // swap is a no-op — the departure still has to reach everyone.
                Send((waiting is { Count: > 0 } ? SwapTeamLocked(waiting.Dequeue().Id) : null)
                     ?? BuildUpdate(players: [.. _players]));
            }
        }

        // Outside the lock: closing reaches back into the database, which calls in again.
        if (closing)
            CloseCustomGame();
        return true;
    }

    public void CloseCustomGame()
    {
        Databases.RegionServerDatabase.RemoveCustomGame(GameInfo.Id);
        ChatRoom.ClearRoom();
    }

    public void SwapTeam(uint playerId)
    {
        lock (_lock) Send(SwapTeamLocked(playerId));
    }

    private CustomGameUpdate? SwapTeamLocked(uint playerId)
    {
        var player = _players.FirstOrDefault(p => p.Id == playerId);

        if (player == null)
            return null;

        CustomGamePlayer? swapPlayer = null;
        switch (player.Team)
        {
            case TeamType.Team1:
                if (_changeTeamRequestsTeam2.Count > 0) swapPlayer = _changeTeamRequestsTeam2.Dequeue();
                break;
            case TeamType.Team2:
                if (_changeTeamRequestsTeam1.Count > 0) swapPlayer = _changeTeamRequestsTeam1.Dequeue();
                break;
            case TeamType.Neutral:
            default:
                swapPlayer = null;
                break;
        }

        if (swapPlayer != null)
        {
            var myTeam = player.Team;
            var otherTeam = swapPlayer.Team;
            player.Team = otherTeam;
            swapPlayer.Team = myTeam;
            player.SwitchTeamRequest = false;
            swapPlayer.SwitchTeamRequest = false;
        }
        else
        {
            var enemyTeamCount = _players.Count(p => p.Team != player.Team);
            if (enemyTeamCount >= GameInfo.MaxPlayers / 2)
            {
                player.SwitchTeamRequest = true;
                switch (player.Team)
                {
                    case TeamType.Neutral:
                        break;
                    case TeamType.Team1:
                        _changeTeamRequestsTeam1.Enqueue(player);
                        break;
                    case TeamType.Team2:
                        _changeTeamRequestsTeam2.Enqueue(player);
                        break;
                }
            }
            else
            {
                switch (player.Team)
                {
                    case TeamType.Neutral:
                        return null;
                    case TeamType.Team1:
                        player.Team = TeamType.Team2;
                        player.SwitchTeamRequest = false;
                        break;
                    case TeamType.Team2:
                        player.Team = TeamType.Team1;
                        player.SwitchTeamRequest = false;
                        break;
                }
            }
        }
        return BuildUpdate(players: [.. _players]);
    }

    public void UpdateSettings(uint playerId, CustomGameSettings settings)
    {
        lock (_lock)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is not { Owner: true })
                return;

            if (settings.BuildTime.HasValue)
                settings.BuildTime = float.Clamp(settings.BuildTime.Value, _customLogic.MinBuildTime, _customLogic.MaxBuildTime);
            if (settings.RespawnTimeMod.HasValue)
                settings.RespawnTimeMod = float.Clamp(settings.RespawnTimeMod.Value, _customLogic.MinRespawnTimeMod, _customLogic.MaxRespawnTimeMod);
            if (settings.ResourceCap.HasValue)
                settings.ResourceCap = float.Clamp(settings.ResourceCap.Value, _customLogic.MinResourceCap, _customLogic.MaxResourceCap);
            if (settings.InitResource.HasValue)
                settings.InitResource = float.Clamp(settings.InitResource.Value, _customLogic.MinInitResource, _customLogic.MaxInitResource);

            GameInfo.MapInfo = settings.MapInfo ?? GameInfo.MapInfo;
            GameInfo.BuildTime = settings.BuildTime ?? GameInfo.BuildTime;
            GameInfo.RespawnTimeMod = settings.RespawnTimeMod ?? GameInfo.RespawnTimeMod;
            GameInfo.HeroSwitch = settings.HeroSwitch ?? GameInfo.HeroSwitch;
            GameInfo.SuperSupply = settings.SuperSupply ?? GameInfo.SuperSupply;
            GameInfo.AllowBackfilling = settings.AllowBackfilling ?? GameInfo.AllowBackfilling;
            GameInfo.ResourceCap = settings.ResourceCap ?? GameInfo.ResourceCap;
            GameInfo.InitResource = settings.InitResource ?? GameInfo.InitResource;

            if (GameInfo.InitResource > GameInfo.ResourceCap)
            {
                GameInfo.InitResource = GameInfo.ResourceCap;
                settings.InitResource = GameInfo.InitResource;
            }

            Send(BuildUpdate(mapInfo: settings.MapInfo, buildTime: settings.BuildTime, respawnMod: settings.RespawnTimeMod,
                heroSwitch: settings.HeroSwitch, superSupply: settings.SuperSupply, allowBackfilling: settings.AllowBackfilling,
                resourceCap: settings.ResourceCap, initResources: settings.InitResource));
        }
    }

    public void UpdateThirdPerson(uint playerId, bool enabled)
    {
        lock (_lock)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is not { Owner: true })
                return;

            GameInfo.ForceThirdPerson = enabled;
            Send(BuildUpdate(forceThirdPerson: enabled));
        }
    }

    public bool IsMaxSpectators()
    {
        lock (_lock) return _spectators.Count >= _customLogic.MaxSpectatorsPerMatch;
    }

    public bool AddSpectator(uint playerId)
    {
        lock (_lock)
        {
            if (_spectators.Count >= _customLogic.MaxSpectatorsPerMatch)
                return false;
            _spectators.Add(playerId);
            return true;
        }
    }

    public void RemoveSpectator(uint playerId)
    {
        lock (_lock) _spectators.Remove(playerId);
    }

    public bool StartIntoLobby(uint playerId)
    {
        lock (_lock)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is not { Owner: true }) return false;
            GameInfo.Status = CustomGameStatus.Lobby;
            Send(BuildUpdate(status: GameInfo.Status));
        }
        return true;
    }

    public void StartIntoMatch()
    {
        lock (_lock)
        {
            GameInfo.Status = CustomGameStatus.Match;
            Send(BuildUpdate(status: GameInfo.Status));
        }
    }

    public void ClearInstance(string? instanceId)
    {
        lock (_lock)
        {
            if (instanceId != _gameInstanceId)
                return;

            _gameInstanceId = null;
            GameInfo.Status = CustomGameStatus.Preparing;
            Send(BuildUpdate(status: GameInfo.Status));
        }
    }

    public TeamType GetTeamForPlayer(uint playerId)
    {
        lock (_lock) return _players.FirstOrDefault(p => p.Id == playerId)?.Team ?? TeamType.Team1;
    }

    public bool IsPlayerSpectator(uint playerId)
    {
        lock (_lock) return _spectators.Contains(playerId);
    }

    public bool IsPlayerBackfill(uint playerId) => false;

    public Key GetGameMode() => CatalogueHelper.ModeCustom.Key;

    public bool CanSwitchHero() => GameInfo.HeroSwitch;

    public bool IsThirdPersonForced() => GameInfo.ForceThirdPerson;

    public bool IsMapEditor() => false;

    public float GetResourceCap() => GameInfo.ResourceCap;

    public float GetResourceAmount() => GameInfo.InitResource;

    public long? GetBuildPhaseEndTime(DateTimeOffset startTime) =>
        startTime.AddSeconds((long)GameInfo.BuildTime).ToUnixTimeMilliseconds();

    public float GetRespawnMultiplier() => GameInfo.RespawnTimeMod;

    public bool IsSuperSupplies() => GameInfo.SuperSupply;
    public bool NeedsBackfill() => false;

    public void SetBackfillReady(bool backfillReady)
    {
    }

    public (Dictionary<uint, Rating> team1, Dictionary<uint, Rating> team2) GetTeamRatings() =>
        (new Dictionary<uint, Rating>(), new Dictionary<uint, Rating>());

    public CustomGameUpdate GetCustomGameUpdate()
    {
        lock (_lock)
        {
            var settings = new CustomGameSettings
            {
                MapInfo = GameInfo.MapInfo,
                BuildTime = GameInfo.BuildTime,
                RespawnTimeMod = GameInfo.RespawnTimeMod,
                HeroSwitch = GameInfo.HeroSwitch,
                SuperSupply = GameInfo.SuperSupply,
                AllowBackfilling = GameInfo.AllowBackfilling,
                ResourceCap = GameInfo.ResourceCap,
                InitResource = GameInfo.InitResource
            };

            return new CustomGameUpdate
            {
                GameName = GameInfo.GameName,
                Password = Password,
                Settings = settings,
                Players = [.. _players],
                Status = GameInfo.Status,
                ForceThirdPerson = GameInfo.ForceThirdPerson
            };
        }
    }

    // Pure — reads no lobby state, so it is safe to call with or without the lock held.
    private static CustomGameUpdate BuildUpdate(string? gameName = null, string? pass = null,
        MapInfo? mapInfo = null, float? buildTime = null, float? respawnMod = null, bool? heroSwitch = null,
        bool? superSupply = null, bool? allowBackfilling = null, float? resourceCap = null, float? initResources = null,
        List<CustomGamePlayer>? players = null, CustomGameStatus? status = null, bool? forceThirdPerson = null)
    {
        CustomGameSettings? settings = null;
        if (mapInfo != null || buildTime != null || respawnMod != null || heroSwitch != null || superSupply != null ||
            allowBackfilling != null || resourceCap != null || initResources != null)
            settings = new CustomGameSettings
            {
                MapInfo = mapInfo,
                BuildTime = buildTime,
                RespawnTimeMod = respawnMod,
                HeroSwitch = heroSwitch,
                SuperSupply = superSupply,
                AllowBackfilling = allowBackfilling,
                ResourceCap = resourceCap,
                InitResource = initResources
            };

        return new CustomGameUpdate
        {
            GameName = gameName,
            Password = pass,
            Settings = settings,
            Players = players,
            Status = status,
            ForceThirdPerson = forceThirdPerson
        };
    }

    private void Send(CustomGameUpdate? update)
    {
        if (update == null) return;
        matchService.SendUpdateCustomGame(update);
    }
}
