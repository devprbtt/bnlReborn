using SQLite;

namespace BNLReloadedServer.Database;

/// <summary>One item a player owns beyond the public catalogue. Only private-scope cards need a grant.</summary>
[Table("InventoryGrants")]
public class InventoryGrantRecord
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Indexed(Name = "UX_InventoryGrants_PlayerItem", Order = 1, Unique = true)]
    [Column("player_id")]
    public uint PlayerId { get; set; }

    // The card id, not its hash, so the table stays readable and survives key-hash changes.
    [Indexed(Name = "UX_InventoryGrants_PlayerItem", Order = 2, Unique = true)]
    [Column("item")]
    public string Item { get; set; } = string.Empty;

    [Column("granted_at")]
    public DateTimeOffset GrantedAt { get; set; }

    [Column("granted_by")]
    public string GrantedBy { get; set; } = string.Empty;

    [Column("note")]
    public string? Note { get; set; }
}

/// <summary>Marks a one-time data migration as done so it never runs again.</summary>
[Table("AppliedMigrations")]
public class AppliedMigrationRecord
{
    [PrimaryKey]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("applied_at")]
    public DateTimeOffset AppliedAt { get; set; }
}
