using System.Text.RegularExpressions;

namespace BNLReloadedServer.Clans;

public enum ClanRank : byte
{
    Member = 0,
    Officer = 1,
    Leader = 2
}

public enum ClanResult : byte
{
    Ok = 0,
    NotInClan,
    AlreadyInClan,
    ClanFull,
    NoSuchClan,
    NoSuchPlayer,
    NoSuchInvite,
    NotAllowed,
    InvalidName,
    InvalidTag,
    NameTaken,
    TagTaken,
    Offensive,
    AlreadyInvited,
    LeaderMustTransfer,
    RenameCooldown,
    TargetNotInClan,
    TooManyInvites
}

// Every clan rule as a pure function, so the fixture can pin them without a database or network.
public static partial class ClanRules
{
    public const int MaxMembers = 20;
    public const int MinNameLength = 3, MaxNameLength = 24;
    public const int MinTagLength = 2, MaxTagLength = 10;
    // A clan can have this many invites pending at once, so a leader cannot spam the whole server.
    public const int MaxPendingInvites = 30;
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan RenameCooldown = TimeSpan.FromDays(7);
    public static readonly TimeSpan InactiveLeaderAfter = TimeSpan.FromDays(30);

    [GeneratedRegex("^[A-Za-z0-9 _'.-]+$")]
    private static partial Regex NameCharacters();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TagCharacters();

    /// <summary>Case-insensitive identity for uniqueness: "Yeti Squad", "yeti  squad" and "YETI SQUAD" collide.</summary>
    public static string Key(string value) => Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();

    public static string CleanName(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    public static ClanResult ValidateName(string? name, IOffensiveText offensive)
    {
        if (name == null) return ClanResult.InvalidName;
        name = CleanName(name);
        if (name.Length < MinNameLength || name.Length > MaxNameLength || !NameCharacters().IsMatch(name))
            return ClanResult.InvalidName;
        return offensive.IsOffensive(name) ? ClanResult.Offensive : ClanResult.Ok;
    }

    public static ClanResult ValidateTag(string? tag, IOffensiveText offensive)
    {
        if (tag == null) return ClanResult.InvalidTag;
        tag = tag.Trim();
        if (tag.Length < MinTagLength || tag.Length > MaxTagLength || !TagCharacters().IsMatch(tag))
            return ClanResult.InvalidTag;
        return offensive.IsOffensive(tag) ? ClanResult.Offensive : ClanResult.Ok;
    }

    public static bool CanInvite(ClanRank actor) => actor >= ClanRank.Officer;

    public static bool CanRename(ClanRank actor) => actor == ClanRank.Leader;

    public static bool CanDisband(ClanRank actor) => actor == ClanRank.Leader;

    public static bool CanSetTagColor(ClanRank actor) => actor == ClanRank.Leader;

    /// <summary>Marks a stored/sent tag colour as chosen, so 0x000000 (black) differs from "no colour".</summary>
    public const uint ColorSet = 0x1000000;

    /// <summary>Any 24-bit colour, or 0 to clear back to the default.</summary>
    public static bool ValidTagColor(uint color) => color == 0 || (color & ~0xFFFFFFu) == ColorSet;

    /// <summary>Leaders can remove anyone but themselves; officers can remove members only.</summary>
    public static bool CanKick(ClanRank actor, ClanRank target) =>
        actor == ClanRank.Leader ? target != ClanRank.Leader : actor == ClanRank.Officer && target == ClanRank.Member;

    /// <summary>Only the leader promotes or demotes, and only between Member and Officer.</summary>
    public static bool CanSetRank(ClanRank actor, ClanRank target, ClanRank newRank) =>
        actor == ClanRank.Leader && target != ClanRank.Leader && newRank != ClanRank.Leader;

    public static bool RenameAllowed(DateTimeOffset? lastRename, DateTimeOffset now) =>
        lastRename == null || now - lastRename.Value >= RenameCooldown;

    public static bool InviteExpired(DateTimeOffset created, DateTimeOffset now) => now - created >= InviteLifetime;

    /// <summary>
    /// Who takes over from a leader: the longest-serving officer, else the longest-serving member.
    /// Ties fall to the lower player id so the choice is deterministic.
    /// </summary>
    public static uint? Successor(IEnumerable<(uint PlayerId, ClanRank Rank, DateTimeOffset JoinedAt)> members, uint leaving) =>
        members.Where(m => m.PlayerId != leaving)
            .OrderByDescending(m => m.Rank)
            .ThenBy(m => m.JoinedAt)
            .ThenBy(m => m.PlayerId)
            .Select(m => (uint?)m.PlayerId)
            .FirstOrDefault();

    public static bool LeaderInactive(DateTimeOffset? lastOnline, DateTimeOffset now) =>
        lastOnline != null && now - lastOnline.Value >= InactiveLeaderAfter;
}

public interface IOffensiveText
{
    bool IsOffensive(string text);
}
