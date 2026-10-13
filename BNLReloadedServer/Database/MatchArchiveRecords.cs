using SQLite;

namespace BNLReloadedServer.Database;

[Table("Matches")]
public sealed class ArchivedMatchRecord
{
    [PrimaryKey, Column("id")] public string Id { get; set; } = string.Empty;
    [Column("map_key")] public long MapKey { get; set; }
    [Column("game_mode_key")] public long GameModeKey { get; set; }
    [Indexed, Column("started_at_ms")] public long StartedAt { get; set; }
    [Indexed, Column("ended_at_ms")] public long EndedAt { get; set; }
    [Column("winner")] public int Winner { get; set; }
    [Column("end_reason")] public int EndReason { get; set; }
}

[Table("MatchTeams")]
public sealed class ArchivedMatchTeamRecord
{
    [PrimaryKey, AutoIncrement, Column("row_id")] public long RowId { get; set; }
    [Indexed(Name = "IX_MatchTeams_MatchTeam", Order = 1, Unique = true), Column("match_id")] public string MatchId { get; set; } = string.Empty;
    [Indexed(Name = "IX_MatchTeams_MatchTeam", Order = 2, Unique = true), Column("team")] public int Team { get; set; }
    [Column("is_winner")] public bool IsWinner { get; set; }
    [Column("cubes_at_start")] public int CubesAtStart { get; set; }
    [Column("cubes_remaining")] public int CubesRemaining { get; set; }
    [Column("base_destroyed")] public bool BaseDestroyed { get; set; }
}

[Table("MatchPlayers")]
public sealed class ArchivedMatchPlayerRecord
{
    [PrimaryKey, AutoIncrement, Column("row_id")] public long RowId { get; set; }
    [Indexed(Name = "IX_MatchPlayers_MatchPlayer", Order = 1, Unique = true), Column("match_id")] public string MatchId { get; set; } = string.Empty;
    [Indexed(Name = "IX_MatchPlayers_MatchPlayer", Order = 2, Unique = true), Column("player_id")] public long PlayerId { get; set; }
    [Column("nickname")] public string Nickname { get; set; } = string.Empty;
    [Column("squad_id")] public string? SquadId { get; set; }
    [Column("was_initial")] public bool WasInitial { get; set; }
    [Column("was_backfiller")] public bool WasBackfiller { get; set; }
    [Column("is_winner")] public bool IsWinner { get; set; }
    [Column("starting_rating_mean")] public double? StartingRatingMean { get; set; }
    [Column("starting_rating_deviation")] public double? StartingRatingDeviation { get; set; }
    [Column("rating_delta")] public double? RatingDelta { get; set; }
    [Column("rating_deviation_delta")] public double? RatingDeviationDelta { get; set; }
    [Column("total_score")] public int TotalScore { get; set; }
    [Column("stats")] public byte[] Stats { get; set; } = [];
    [Column("raw_stats")] public byte[] RawStats { get; set; } = [];
    // ArchivedAccolades blob; null for matches recorded before accolade tracking.
    [Column("accolades")] public byte[]? Accolades { get; set; }
}

[Table("MatchPlayerDevices")]
public sealed class ArchivedMatchPlayerDeviceRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed(Name = "IX_MatchPlayerDevices", Order = 1, Unique = true), Column("match_id")] public string MatchId { get; set; } = string.Empty;
    [Indexed(Name = "IX_MatchPlayerDevices", Order = 2, Unique = true), Column("player_id")] public long PlayerId { get; set; }
    [Indexed(Name = "IX_MatchPlayerDevices", Order = 3, Unique = true), Column("device_key")] public long DeviceKey { get; set; }
    [Column("placed")] public int Placed { get; set; }
    [Column("destroyed")] public int Destroyed { get; set; }
}

[Table("MatchPresences")]
public sealed class ArchivedMatchPresenceRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed(Name = "IX_MatchPresences_Sequence", Order = 1, Unique = true), Column("match_id")] public string MatchId { get; set; } = string.Empty;
    [Indexed(Name = "IX_MatchPresences_Sequence", Order = 2, Unique = true), Column("player_id")] public long PlayerId { get; set; }
    [Indexed(Name = "IX_MatchPresences_Sequence", Order = 3, Unique = true), Column("sequence")] public int Sequence { get; set; }
    [Column("team_slot")] public int TeamSlot { get; set; }
    [Column("joined_at_ms")] public long JoinedAt { get; set; }
    [Column("left_at_ms")] public long? LeftAt { get; set; }
    [Column("join_kind")] public int JoinKind { get; set; }
    [Column("leave_kind")] public int? LeaveKind { get; set; }
    [Column("team")] public int Team { get; set; }
    [Column("hero_key")] public long HeroKey { get; set; }
    [Column("skin_key")] public long SkinKey { get; set; }
}

[Table("MatchPresenceDevices")]
public sealed class ArchivedMatchDeviceRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed, Column("presence_id")] public long PresenceId { get; set; }
    [Column("slot")] public int Slot { get; set; }
    [Column("device_key")] public long DeviceKey { get; set; }
    [Column("device_level")] public int? DeviceLevel { get; set; }
}

[Table("MatchPresencePerks")]
public sealed class ArchivedMatchPerkRecord
{
    [PrimaryKey, AutoIncrement, Column("id")] public long Id { get; set; }
    [Indexed, Column("presence_id")] public long PresenceId { get; set; }
    [Column("slot")] public int Slot { get; set; }
    [Column("perk_key")] public long PerkKey { get; set; }
}

public sealed record ArchivedMatchDetail(
    ArchivedMatchRecord Match,
    List<ArchivedMatchTeamRecord> Teams,
    List<ArchivedMatchPlayerRecord> Players,
    List<ArchivedMatchPlayerDeviceRecord> PlayerDevices,
    List<ArchivedMatchPresenceRecord> Presences,
    List<ArchivedMatchDeviceRecord> Devices,
    List<ArchivedMatchPerkRecord> Perks);
