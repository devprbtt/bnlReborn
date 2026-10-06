using SQLite;

namespace BNLReloadedServer.Clans;

public record ClanMember(uint PlayerId, ClanRank Rank, DateTimeOffset JoinedAt);

public record ClanInvite(int ClanId, uint PlayerId, uint InviterId, DateTimeOffset CreatedAt);

public record ClanView(int Id, string Name, string Tag, uint LeaderId, DateTimeOffset CreatedAt,
    DateTimeOffset? RenamedAt, ClanMember[] Members, uint TagColor = 0);

/// <summary>
/// All clans in memory, persisted to the player database. Clan changes are rare, so one lock serialises every
/// operation: each is checked against memory, written in a database transaction, and only then applied in memory.
/// That makes the 20-member cap, one clan per player and name/tag uniqueness exact under concurrent requests.
/// </summary>
public sealed class ClanStore
{
    private readonly SQLiteAsyncConnection _db;
    private readonly IOffensiveText _offensive;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<int, ClanRecord> _clans = new();
    private readonly Dictionary<uint, ClanMemberRecord> _members = new();
    private readonly List<ClanInviteRecord> _invites = new();

    /// <summary>Raised after a committed change, with the clans whose members should receive a fresh snapshot.</summary>
    public event Action<int>? ClanChanged;
    /// <summary>Raised after a committed change that affects one player's own state (invites, leaving, being kicked).</summary>
    public event Action<uint>? PlayerChanged;

    public ClanStore(SQLiteAsyncConnection db, IOffensiveText offensive, Func<DateTimeOffset>? now = null)
    {
        _db = db;
        _offensive = offensive;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task Load()
    {
        await _db.CreateTableAsync<ClanRecord>();
        await _db.CreateTableAsync<ClanMemberRecord>();
        await _db.CreateTableAsync<ClanInviteRecord>();
        await _gate.WaitAsync();
        try
        {
            _clans.Clear(); _members.Clear(); _invites.Clear();
            foreach (var clan in await _db.Table<ClanRecord>().ToListAsync()) _clans[clan.Id] = clan;
            foreach (var member in await _db.Table<ClanMemberRecord>().ToListAsync()) _members[member.PlayerId] = member;
            _invites.AddRange(await _db.Table<ClanInviteRecord>().ToListAsync());
        }
        finally { _gate.Release(); }
    }

    // ---- Reads (snapshots; never expose the mutable records) ----

    public ClanView? ClanOf(uint playerId)
    {
        lock (_clans)
            return _members.TryGetValue(playerId, out var m) ? View(m.ClanId) : null;
    }

    public ClanView? Clan(int clanId) { lock (_clans) return View(clanId); }

    public ClanView[] Browse(string query)
    {
        query = query.Trim();
        lock (_clans)
            return _clans.Values.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Tag.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id).Take(50)
                .Select(c => View(c.Id)!).ToArray();
    }

    public ClanView[] AllClans() { lock (_clans) return _clans.Keys.Select(id => View(id)!).ToArray(); }

    public ClanView? ClanByTag(string tag)
    {
        var key = ClanRules.Key(tag);
        lock (_clans)
        {
            var clan = _clans.Values.FirstOrDefault(c => c.TagKey == key);
            return clan == null ? null : View(clan.Id);
        }
    }

    public string? TagOf(uint playerId) => TagAndColorOf(playerId)?.Tag;

    public (string Tag, uint Color)? TagAndColorOf(uint playerId)
    {
        lock (_clans)
            return _members.TryGetValue(playerId, out var m) && _clans.TryGetValue(m.ClanId, out var c) ? (c.Tag, c.TagColor) : null;
    }

    public ClanInvite[] InvitesFor(uint playerId)
    {
        var now = _now();
        lock (_clans)
            return _invites.Where(i => i.PlayerId == playerId && !ClanRules.InviteExpired(i.CreatedAt, now))
                .Select(i => new ClanInvite(i.ClanId, i.PlayerId, i.InviterId, i.CreatedAt)).ToArray();
    }

    public ClanInvite[] InvitesFrom(int clanId)
    {
        var now = _now();
        lock (_clans)
            return _invites.Where(i => i.ClanId == clanId && !ClanRules.InviteExpired(i.CreatedAt, now))
                .Select(i => new ClanInvite(i.ClanId, i.PlayerId, i.InviterId, i.CreatedAt)).ToArray();
    }

    private ClanView? View(int clanId)
    {
        if (!_clans.TryGetValue(clanId, out var c)) return null;
        var members = _members.Values.Where(m => m.ClanId == clanId)
            .OrderByDescending(m => m.Rank).ThenBy(m => m.JoinedAt)
            .Select(m => new ClanMember(m.PlayerId, m.Rank, m.JoinedAt)).ToArray();
        return new ClanView(c.Id, c.Name, c.Tag, c.LeaderId, c.CreatedAt, c.RenamedAt, members, c.TagColor);
    }

    // ---- Operations ----

    public async Task<(ClanResult Result, int ClanId)> Create(uint playerId, string name, string tag)
    {
        var nameCheck = ClanRules.ValidateName(name, _offensive);
        if (nameCheck != ClanResult.Ok) return (nameCheck, 0);
        var tagCheck = ClanRules.ValidateTag(tag, _offensive);
        if (tagCheck != ClanResult.Ok) return (tagCheck, 0);
        name = ClanRules.CleanName(name); tag = tag.Trim();

        await _gate.WaitAsync();
        try
        {
            if (_members.ContainsKey(playerId)) return (ClanResult.AlreadyInClan, 0);
            if (_clans.Values.Any(c => c.NameKey == ClanRules.Key(name))) return (ClanResult.NameTaken, 0);
            if (_clans.Values.Any(c => c.TagKey == ClanRules.Key(tag))) return (ClanResult.TagTaken, 0);
            var now = _now();
            var clan = new ClanRecord { Name = name, NameKey = ClanRules.Key(name), Tag = tag, TagKey = ClanRules.Key(tag), LeaderId = playerId, CreatedAt = now };
            var member = new ClanMemberRecord { PlayerId = playerId, Rank = ClanRank.Leader, JoinedAt = now };
            List<ClanInviteRecord> stale = _invites.Where(i => i.PlayerId == playerId).ToList();
            await _db.RunInTransactionAsync(db =>
            {
                db.Insert(clan);
                member.ClanId = clan.Id;
                db.Insert(member);
                // Joining any clan voids the player's other invites.
                db.Execute("DELETE FROM ClanInvites WHERE player_id = ?", playerId);
            });
            Apply(() => { _clans[clan.Id] = clan; _members[playerId] = member; _invites.RemoveAll(stale.Contains); });
            Raise(clan.Id, playerId);
            return (ClanResult.Ok, clan.Id);
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> Invite(uint actorId, uint targetId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!ClanRules.CanInvite(actor.Rank)) return ClanResult.NotAllowed;
            if (_members.ContainsKey(targetId)) return ClanResult.AlreadyInClan;
            if (Count(actor.ClanId) >= ClanRules.MaxMembers) return ClanResult.ClanFull;
            var now = _now();
            var existing = _invites.FirstOrDefault(i => i.ClanId == actor.ClanId && i.PlayerId == targetId);
            if (existing != null && !ClanRules.InviteExpired(existing.CreatedAt, now)) return ClanResult.AlreadyInvited;
            if (_invites.Count(i => i.ClanId == actor.ClanId && !ClanRules.InviteExpired(i.CreatedAt, now)) >= ClanRules.MaxPendingInvites)
                return ClanResult.TooManyInvites;
            var invite = new ClanInviteRecord { ClanId = actor.ClanId, PlayerId = targetId, InviterId = actorId, CreatedAt = now };
            await _db.RunInTransactionAsync(db =>
            {
                db.Execute("DELETE FROM ClanInvites WHERE clan_id = ? AND player_id = ?", actor.ClanId, targetId);
                db.Insert(invite);
            });
            Apply(() => { if (existing != null) _invites.Remove(existing); _invites.Add(invite); });
            Raise(actor.ClanId, targetId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> Accept(uint playerId, int clanId)
    {
        await _gate.WaitAsync();
        try
        {
            var now = _now();
            var invite = _invites.FirstOrDefault(i => i.ClanId == clanId && i.PlayerId == playerId);
            if (invite == null || ClanRules.InviteExpired(invite.CreatedAt, now)) return ClanResult.NoSuchInvite;
            if (!_clans.ContainsKey(clanId)) return ClanResult.NoSuchClan;
            if (_members.ContainsKey(playerId)) return ClanResult.AlreadyInClan;
            if (Count(clanId) >= ClanRules.MaxMembers) return ClanResult.ClanFull;
            var member = new ClanMemberRecord { PlayerId = playerId, ClanId = clanId, Rank = ClanRank.Member, JoinedAt = now };
            await _db.RunInTransactionAsync(db =>
            {
                db.Insert(member);
                db.Execute("DELETE FROM ClanInvites WHERE player_id = ?", playerId);
            });
            Apply(() => { _members[playerId] = member; _invites.RemoveAll(i => i.PlayerId == playerId); });
            Raise(clanId, playerId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> Decline(uint playerId, int clanId)
    {
        await _gate.WaitAsync();
        try
        {
            var invite = _invites.FirstOrDefault(i => i.ClanId == clanId && i.PlayerId == playerId);
            if (invite == null) return ClanResult.NoSuchInvite;
            await _db.ExecuteAsync("DELETE FROM ClanInvites WHERE clan_id = ? AND player_id = ?", clanId, playerId);
            Apply(() => _invites.Remove(invite));
            Raise(clanId, playerId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    /// <summary>A leader with other members must transfer first; a leader alone disbands the clan by leaving.</summary>
    public async Task<ClanResult> Leave(uint playerId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(playerId, out var member)) return ClanResult.NotInClan;
            if (member.Rank == ClanRank.Leader)
            {
                if (Count(member.ClanId) > 1) return ClanResult.LeaderMustTransfer;
                return await DisbandLocked(member.ClanId);
            }
            await _db.ExecuteAsync("DELETE FROM ClanMembers WHERE player_id = ?", playerId);
            Apply(() => _members.Remove(playerId));
            Raise(member.ClanId, playerId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> Kick(uint actorId, uint targetId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!_members.TryGetValue(targetId, out var target) || target.ClanId != actor.ClanId) return ClanResult.TargetNotInClan;
            if (!ClanRules.CanKick(actor.Rank, target.Rank)) return ClanResult.NotAllowed;
            await _db.ExecuteAsync("DELETE FROM ClanMembers WHERE player_id = ?", targetId);
            Apply(() => _members.Remove(targetId));
            Raise(actor.ClanId, targetId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> SetRank(uint actorId, uint targetId, ClanRank rank)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!_members.TryGetValue(targetId, out var target) || target.ClanId != actor.ClanId) return ClanResult.TargetNotInClan;
            if (!ClanRules.CanSetRank(actor.Rank, target.Rank, rank)) return ClanResult.NotAllowed;
            if (target.Rank == rank) return ClanResult.Ok;
            await _db.ExecuteAsync("UPDATE ClanMembers SET rank = ? WHERE player_id = ?", (int)rank, targetId);
            Apply(() => target.Rank = rank);
            Raise(actor.ClanId, targetId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> TransferLeadership(uint actorId, uint targetId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (actor.Rank != ClanRank.Leader) return ClanResult.NotAllowed;
            if (actorId == targetId) return ClanResult.Ok;
            if (!_members.TryGetValue(targetId, out var target) || target.ClanId != actor.ClanId) return ClanResult.TargetNotInClan;
            return await TransferLocked(actor.ClanId, actorId, targetId);
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> Rename(uint actorId, string name, string tag)
    {
        var nameCheck = ClanRules.ValidateName(name, _offensive);
        if (nameCheck != ClanResult.Ok) return nameCheck;
        var tagCheck = ClanRules.ValidateTag(tag, _offensive);
        if (tagCheck != ClanResult.Ok) return tagCheck;
        name = ClanRules.CleanName(name); tag = tag.Trim();
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!ClanRules.CanRename(actor.Rank)) return ClanResult.NotAllowed;
            var clan = _clans[actor.ClanId];
            var now = _now();
            if (!ClanRules.RenameAllowed(clan.RenamedAt, now)) return ClanResult.RenameCooldown;
            if (_clans.Values.Any(c => c.Id != clan.Id && c.NameKey == ClanRules.Key(name))) return ClanResult.NameTaken;
            if (_clans.Values.Any(c => c.Id != clan.Id && c.TagKey == ClanRules.Key(tag))) return ClanResult.TagTaken;
            // Write a copy so memory only changes after the database accepted it (the unique indexes may refuse).
            var renamed = new ClanRecord
            {
                Id = clan.Id, Name = name, NameKey = ClanRules.Key(name), Tag = tag, TagKey = ClanRules.Key(tag),
                LeaderId = clan.LeaderId, CreatedAt = clan.CreatedAt, RenamedAt = now, TagColor = clan.TagColor
            };
            await _db.UpdateAsync(renamed);
            Apply(() => _clans[clan.Id] = renamed);
            Raise(clan.Id, actorId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> SetTagColor(uint actorId, uint color)
    {
        if (!ClanRules.ValidTagColor(color)) return ClanResult.NotAllowed;
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!ClanRules.CanSetTagColor(actor.Rank)) return ClanResult.NotAllowed;
            var clan = _clans[actor.ClanId];
            if (clan.TagColor == color) return ClanResult.Ok;
            await _db.ExecuteAsync("UPDATE Clans SET tag_color = ? WHERE id = ?", (long)color, clan.Id);
            Apply(() => clan.TagColor = color);
            Raise(clan.Id, actorId);
            return ClanResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task<ClanResult> Disband(uint actorId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_members.TryGetValue(actorId, out var actor)) return ClanResult.NotInClan;
            if (!ClanRules.CanDisband(actor.Rank)) return ClanResult.NotAllowed;
            return await DisbandLocked(actor.ClanId);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Hands leadership of every clan whose leader has been offline for 30 days to the longest-serving officer
    /// (or member). Returns how many clans changed. lastOnline returns null for players with no record.
    /// </summary>
    public async Task<int> TransferInactiveLeaders(Func<uint, DateTimeOffset?> lastOnline, Func<uint, bool> isOnline)
    {
        await _gate.WaitAsync();
        try
        {
            var now = _now();
            int changed = 0;
            foreach (var clan in _clans.Values.ToList())
            {
                if (isOnline(clan.LeaderId) || !ClanRules.LeaderInactive(lastOnline(clan.LeaderId), now)) continue;
                var members = _members.Values.Where(m => m.ClanId == clan.Id).Select(m => (m.PlayerId, m.Rank, m.JoinedAt));
                if (ClanRules.Successor(members, clan.LeaderId) is not { } successor) continue;
                if (await TransferLocked(clan.Id, clan.LeaderId, successor) == ClanResult.Ok) changed++;
            }
            return changed;
        }
        finally { _gate.Release(); }
    }

    // ---- Helpers (caller holds _gate) ----

    private int Count(int clanId) => _members.Values.Count(m => m.ClanId == clanId);

    private async Task<ClanResult> TransferLocked(int clanId, uint fromId, uint toId)
    {
        var from = _members[fromId]; var to = _members[toId]; var clan = _clans[clanId];
        await _db.RunInTransactionAsync(db =>
        {
            db.Execute("UPDATE ClanMembers SET rank = ? WHERE player_id = ?", (int)ClanRank.Officer, fromId);
            db.Execute("UPDATE ClanMembers SET rank = ? WHERE player_id = ?", (int)ClanRank.Leader, toId);
            db.Execute("UPDATE Clans SET leader_id = ? WHERE id = ?", toId, clanId);
        });
        Apply(() => { from.Rank = ClanRank.Officer; to.Rank = ClanRank.Leader; clan.LeaderId = toId; });
        Raise(clanId, fromId);
        PlayerChanged?.Invoke(toId);
        return ClanResult.Ok;
    }

    private async Task<ClanResult> DisbandLocked(int clanId)
    {
        var former = _members.Values.Where(m => m.ClanId == clanId).Select(m => m.PlayerId).ToList();
        var invited = _invites.Where(i => i.ClanId == clanId).Select(i => i.PlayerId).ToList();
        await _db.RunInTransactionAsync(db =>
        {
            db.Execute("DELETE FROM ClanMembers WHERE clan_id = ?", clanId);
            db.Execute("DELETE FROM ClanInvites WHERE clan_id = ?", clanId);
            db.Execute("DELETE FROM Clans WHERE id = ?", clanId);
        });
        Apply(() =>
        {
            foreach (var id in former) _members.Remove(id);
            _invites.RemoveAll(i => i.ClanId == clanId);
            _clans.Remove(clanId);
        });
        foreach (var id in former.Concat(invited).Distinct()) PlayerChanged?.Invoke(id);
        return ClanResult.Ok;
    }

    // Readers lock _clans; writers also hold _gate, so reads never see a half-applied change.
    private void Apply(Action change) { lock (_clans) change(); }

    private void Raise(int clanId, uint playerId)
    {
        ClanChanged?.Invoke(clanId);
        PlayerChanged?.Invoke(playerId);
    }
}
