using System.Net;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Service;
using Moserware.Skills;

namespace BNLReloadedServer.Database;

public interface IMasterServerDatabase
{
    public List<RegionInfo> GetRegionServers();
    public bool AddRegionServer(string id, string host, RegionGuiInfo regionGuiInfo);
    public bool RemoveRegionServer(string id);
    public bool SetRegionPlayerCount(string id, int playerCount);
    public int GetRegionPlayerCount(string id);
    public RegionInfo? GetRegionServer(string id);
    public Task<PlayerData> AddPlayer(ulong steamId, string playerName, string region);
    public Task<PlayerData?> GetPlayer(ulong steamId);
    public Task<PlayerData?> GetPlayer(uint playerId);
    public Task RecordPlayerIp(uint playerId, IPAddress? address);
    public Task<DateTimeOffset?> GetLastOnline(uint playerId);
    public Task SaveLastOnline(uint playerId, DateTimeOffset lastOnline);
    public Task<List<PlayerIpRecord>> GetIpsForPlayer(uint playerId);
    public Task<List<PlayerIpRecord>> GetPlayersForIp(string ip);
    public Task<List<string>> GetNicknamesForIp(string ip, int limit);
    public Task<bool> SetRegionForPlayer(uint playerId, string region);
    public Task<bool> SetUsernameForPlayer(uint playerId, string username);
    public Task<bool> SetLookingForFriendsForPlayer(uint playerId, bool lookingForFriends);
    public Task<bool> SetLastPlayedForPlayer(uint playerId, Key hero);
    public Task<bool> SetBadgesForPlayer(uint playerId, Dictionary<BadgeType, List<Key>> badges);
    public Task<bool> SetLoadoutForPlayer(uint playerId, Key hero, LobbyLoadout loadout);
    public Task<bool> SetNewMatchDataForPlayer(EndMatchResults endMatchResults);
    public Task StoreCompletedMatch(CompletedMatchRecord match);
    public Task<List<ArchivedMatchRecord>> GetCompletedMatches(int limit, long? before);
    public Task<ArchivedMatchDetail?> GetCompletedMatch(string matchId);
    public Task<LifetimeProfileStats> GetLifetimeProfileStats(uint playerId);
    public Task<bool> SetNewRatings(List<uint> winners, List<uint> losers, HashSet<uint> excluded);
    public Task<bool> SetFriends(uint receiverId, uint senderId, bool accepted);
    public Task<bool> SetFriendRequest(uint receiverId, uint senderId);
    public Task<ProfileData> GetProfileData(uint playerId);
    public Task<List<SearchResult>> GetSearchResults(string pattern);
    public Task<List<SearchResult>> GetSearchResults(List<uint> playerIds);
    public Task<List<SearchResult>> GetSearchResults(List<ulong> steamIds);
    public Task<List<LeagueLeaderboardRecord>> GetLeaderboard();
    public Task<Dictionary<Key, List<TtLeaderboardRecord>>> GetTimeTrialLeaderboard();
    public Task<List<PlayerData>> GetAllPlayersAsync();
    public Task<bool> UpdatePlayerAsync(uint playerId, PlayerData updated);

}
