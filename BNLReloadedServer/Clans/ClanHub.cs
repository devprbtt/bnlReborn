using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;
using BNLReloadedServer.Service;
using SQLite;

namespace BNLReloadedServer.Clans;

/// <summary>
/// Owns the clan store for the region server: loads it at start, pushes a fresh state to every affected online
/// player after each committed change, and runs the daily inactive-leader check.
/// </summary>
public static class ClanHub
{
    private static ClanStore? _store;
    private static Timer? _inactiveLeaderTimer;

    public static ClanStore Store => _store ?? throw new InvalidOperationException("Clans are not started.");

    public static bool Started => _store != null;

    public static void Start()
    {
        var store = new ClanStore(new SQLiteAsyncConnection(Databases.PlayerDatabaseFile), new CatalogueOffensiveText());
        store.Load().GetAwaiter().GetResult();
        store.ClanChanged += clanId => PushClan(clanId);
        store.PlayerChanged += playerId => Push(playerId);
        _store = store;
        // At start and then daily. A leader already offline for 30 days is replaced the first time the check runs.
        _inactiveLeaderTimer = new Timer(_ => TransferInactiveLeaders(), null, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
        Log.Info(LogCat.Server, "Clans started");
    }

    private static void TransferInactiveLeaders()
    {
        Store.TransferInactiveLeaders(id => Databases.PlayerDatabase.GetPresence(id).LastOnline,
                Databases.RegionServerDatabase.IsUserOnline)
            .ContinueWith(t =>
            {
                if (t.IsFaulted) Log.Error(LogCat.Server, "Inactive clan leader check failed", t.Exception!);
                else if (t.Result > 0) Log.Info(LogCat.Server, $"Transferred leadership of {t.Result} clan(s) from inactive leaders");
            });
    }

    /// <summary>Sends the current clan state to every online member of a clan.</summary>
    public static void PushClan(int clanId)
    {
        var clan = Store.Clan(clanId);
        if (clan == null) return;
        foreach (var member in clan.Members) Push(member.PlayerId);
    }

    /// <summary>Refreshes a player's clanmates (and the player) after they come online or go offline.</summary>
    public static void PushClanOf(uint playerId)
    {
        if (!Started) return;
        if (Store.ClanOf(playerId) is { } clan) PushClan(clan.Id);
        else Push(playerId);
    }

    /// <summary>Sends one player their clan state, if they are online with a clan-capable client.</summary>
    public static void Push(uint playerId)
    {
        if (Databases.RegionServerDatabase.GetClanService(playerId) is not { SupportsClans: true } service) return;
        BuildState(playerId).ContinueWith(t =>
        {
            if (t.IsFaulted) Log.Error(LogCat.Server, $"Failed to build clan state for player {playerId}", t.Exception!);
            else service.SendState(t.Result);
        });
    }

    public static async Task<ClanStateMessage> BuildState(uint playerId)
    {
        var store = Store;
        var clan = store.ClanOf(playerId);
        var invites = store.InvitesFor(playerId);
        var outgoing = clan == null ? [] : store.InvitesFrom(clan.Id);

        var ids = new HashSet<uint>();
        if (clan != null) foreach (var m in clan.Members) ids.Add(m.PlayerId);
        foreach (var i in invites) ids.Add(i.InviterId);
        foreach (var i in outgoing) ids.Add(i.PlayerId);
        var names = await Names(ids);

        ClanStateClan? clanState = null;
        if (clan != null)
        {
            var members = clan.Members.Select(m => new ClanStateMember(m.PlayerId, names.GetValueOrDefault(m.PlayerId, "Player"),
                m.Rank, Databases.RegionServerDatabase.GetOnlinePlayerLocation(m.PlayerId), m.JoinedAt)).ToArray();
            var myRank = clan.Members.First(m => m.PlayerId == playerId).Rank;
            clanState = new ClanStateClan(clan.Id, clan.Name, clan.Tag, clan.LeaderId, myRank, clan.RenamedAt, members,
                outgoing.Select(i => new ClanStateOutgoing(i.PlayerId, names.GetValueOrDefault(i.PlayerId, "Player"), i.CreatedAt)).ToArray());
        }
        var inviteStates = invites.Select(i =>
        {
            var from = store.Clan(i.ClanId);
            return from == null ? null : new ClanStateInvite(i.ClanId, from.Name, from.Tag, names.GetValueOrDefault(i.InviterId, "Player"), i.CreatedAt);
        }).OfType<ClanStateInvite>().ToArray();
        return new ClanStateMessage(clanState, inviteStates);
    }

    private static async Task<Dictionary<uint, string>> Names(IEnumerable<uint> ids)
    {
        var names = new Dictionary<uint, string>();
        var offline = new List<uint>();
        foreach (var id in ids)
        {
            var online = Databases.PlayerDatabase.GetPlayerName(id);
            if (!string.IsNullOrEmpty(online)) names[id] = online;
            else offline.Add(id);
        }
        if (offline.Count > 0)
            foreach (var result in await Databases.MasterServerDatabase.GetSearchResults(offline))
                if (!string.IsNullOrEmpty(result.Nickname)) names[result.PlayerId] = result.Nickname;
        return names;
    }

    /// <summary>Exact, case-insensitive nickname match; null when nobody or more than one player has the name.</summary>
    public static async Task<uint?> FindPlayer(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return null;
        // SQLite's NOCASE folds ASCII only; the final comparison also folds other letters.
        var matches = (await Databases.MasterServerDatabase.FindPlayersByName(name))
            .Where(r => string.Equals(r.Nickname, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0].PlayerId : null;
    }
}

public record ClanStateMember(uint PlayerId, string Name, ClanRank Rank, string? Activity, DateTimeOffset JoinedAt);
public record ClanStateOutgoing(uint PlayerId, string Name, DateTimeOffset CreatedAt);
public record ClanStateClan(int Id, string Name, string Tag, uint LeaderId, ClanRank MyRank, DateTimeOffset? RenamedAt,
    ClanStateMember[] Members, ClanStateOutgoing[] Outgoing);
public record ClanStateInvite(int ClanId, string ClanName, string Tag, string InviterName, DateTimeOffset CreatedAt);
public record ClanStateMessage(ClanStateClan? Clan, ClanStateInvite[] Invites);

/// <summary>The catalogue's offensive-name lists (all locales), as the original client applied them to nicknames.</summary>
public sealed class CatalogueOffensiveText : IOffensiveText
{
    public bool IsOffensive(string text)
    {
        var offensive = CatalogueHelper.ChatLogic.Offensive;
        if (offensive == null) return false;
        var lower = text.ToLowerInvariant();
        foreach (var lists in offensive.Values)
        {
            if (lists.OffensiveNamesContains?.Any(w => w.Length > 0 && lower.Contains(w.ToLowerInvariant())) == true) return true;
            if (lists.OffensiveNamesStartsEnds?.Any(w => w.Length > 0 &&
                    (lower.StartsWith(w.ToLowerInvariant()) || lower.EndsWith(w.ToLowerInvariant()))) == true) return true;
            if (lists.OffensiveWords?.Any(w => w.Length > 0 &&
                    lower.Split(' ', '_', '-', '.').Contains(w.ToLowerInvariant())) == true) return true;
        }
        return false;
    }
}
