using SQLite;

namespace BNLReloadedServer.Clans;

[Table("ClanApplications")]
public sealed class ClanApplicationRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed("UX_ClanApplications_PlayerClan", 1, Unique = true)] public uint PlayerId { get; set; }
    [Indexed("UX_ClanApplications_PlayerClan", 2, Unique = true)] public int ClanId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed partial class ClanStore
{
    private readonly List<ClanApplicationRecord> _applications = new();
    public ClanApplicationRecord[] Applications(uint viewer)
    {
        lock (_clans)
        {
            var officerClan = _members.TryGetValue(viewer, out var member) && ClanRules.CanInvite(member.Rank)
                ? member.ClanId : 0;
            return _applications.Where(a => a.PlayerId == viewer || a.ClanId == officerClan)
                .OrderBy(a => a.CreatedAt).Select(a => new ClanApplicationRecord
                { Id = a.Id, PlayerId = a.PlayerId, ClanId = a.ClanId, CreatedAt = a.CreatedAt }).ToArray();
        }
    }

    public async Task<ClanResult> ApplyToClan(uint playerId, int clanId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_members.ContainsKey(playerId)) return ClanResult.AlreadyInClan;
            if (!_clans.ContainsKey(clanId)) return ClanResult.NoSuchClan;
            if (Count(clanId) >= ClanRules.MaxMembers) return ClanResult.ClanFull;
            if (_applications.Any(a => a.PlayerId == playerId && a.ClanId == clanId)) return ClanResult.AlreadyApplied;
            if (_applications.Count(a => a.PlayerId == playerId) >= 5 ||
                _applications.Count(a => a.ClanId == clanId) >= 50) return ClanResult.TooManyApplications;
            var application = new ClanApplicationRecord { PlayerId = playerId, ClanId = clanId, CreatedAt = _now() };
            await _db.InsertAsync(application);
            Apply(() => _applications.Add(application));
            Raise(clanId, playerId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> CancelApplication(uint playerId, int clanId)
    {
        await _gate.WaitAsync();
        try { return await RemoveApplicationLocked(playerId, clanId); }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> ReviewApplication(uint actorId, uint playerId, bool approve)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!ClanRules.CanInvite(actor.Rank)) return ClanResult.NotAllowed;
            if (!_applications.Any(a => a.PlayerId == playerId && a.ClanId == actor.ClanId)) return ClanResult.NoSuchApplication;
            if (!approve) return await RemoveApplicationLocked(playerId, actor.ClanId);
            if (_members.ContainsKey(playerId)) return ClanResult.AlreadyInClan;
            if (Count(actor.ClanId) >= ClanRules.MaxMembers) return ClanResult.ClanFull;
            var affected = _applications.Where(a => a.PlayerId == playerId).Select(a => a.ClanId).ToArray();
            var member = new ClanMemberRecord { PlayerId = playerId, ClanId = actor.ClanId, Rank = ClanRank.Member, JoinedAt = _now() };
            await _db.RunInTransactionAsync(db =>
            {
                db.Insert(member);
                db.Execute("DELETE FROM ClanInvites WHERE player_id = ?", playerId);
                db.Execute("DELETE FROM ClanApplications WHERE PlayerId = ?", playerId);
            });
            Apply(() => { _members[playerId] = member; _invites.RemoveAll(i => i.PlayerId == playerId); _applications.RemoveAll(a => a.PlayerId == playerId); });
            foreach (var id in affected.Distinct()) ClanChanged?.Invoke(id);
            Raise(actor.ClanId, playerId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    private async Task<ClanResult> RemoveApplicationLocked(uint playerId, int clanId)
    {
        if (!_applications.Any(a => a.PlayerId == playerId && a.ClanId == clanId)) return ClanResult.NoSuchApplication;
        await _db.ExecuteAsync("DELETE FROM ClanApplications WHERE PlayerId = ? AND ClanId = ?", playerId, clanId);
        Apply(() => _applications.RemoveAll(a => a.PlayerId == playerId && a.ClanId == clanId));
        Raise(clanId, playerId);
        return ClanResult.Ok;
    }
}
