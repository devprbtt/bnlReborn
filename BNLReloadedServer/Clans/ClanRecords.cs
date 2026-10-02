using SQLite;

namespace BNLReloadedServer.Clans;

[Table("Clans")]
public class ClanRecord
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    // Lower-cased, whitespace-collapsed name and tag: the unique indexes are the final word on collisions.
    [Indexed(Name = "UX_Clans_NameKey", Unique = true)]
    [Column("name_key")]
    public string NameKey { get; set; } = string.Empty;

    [Column("tag")]
    public string Tag { get; set; } = string.Empty;

    [Indexed(Name = "UX_Clans_TagKey", Unique = true)]
    [Column("tag_key")]
    public string TagKey { get; set; } = string.Empty;

    [Column("leader_id")]
    public uint LeaderId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("renamed_at")]
    public DateTimeOffset? RenamedAt { get; set; }
}

[Table("ClanMembers")]
public class ClanMemberRecord
{
    // One row per player: the primary key is what enforces one clan per player.
    [PrimaryKey]
    [Column("player_id")]
    public uint PlayerId { get; set; }

    [Indexed(Name = "IX_ClanMembers_Clan")]
    [Column("clan_id")]
    public int ClanId { get; set; }

    [Column("rank")]
    public ClanRank Rank { get; set; }

    [Column("joined_at")]
    public DateTimeOffset JoinedAt { get; set; }
}

[Table("ClanInvites")]
public class ClanInviteRecord
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Indexed(Name = "UX_ClanInvites_ClanPlayer", Order = 1, Unique = true)]
    [Column("clan_id")]
    public int ClanId { get; set; }

    [Indexed(Name = "UX_ClanInvites_ClanPlayer", Order = 2, Unique = true)]
    [Column("player_id")]
    public uint PlayerId { get; set; }

    [Column("inviter_id")]
    public uint InviterId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
