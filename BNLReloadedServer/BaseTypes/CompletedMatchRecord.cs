using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.BaseTypes;

public enum MatchJoinKind : byte { Initial, Backfill, Reconnect }
public enum MatchLeaveKind : byte { MatchEnded, Disconnect, Quit, Inactivity, Kicked }
public enum MatchEndReason : byte { Unknown, ObjectivesDestroyed, Surrender, ObjectivesCompleted, Abandoned }

public sealed class CompletedMatchPresence
{
    public int Sequence { get; set; }
    // A persistent display slot within the team. A backfiller inherits an available slot so
    // a future scoreboard can show the people who occupied the same place together.
    public int TeamSlot { get; set; }
    public ulong JoinedAt { get; set; }
    public ulong? LeftAt { get; set; }
    public MatchJoinKind JoinKind { get; set; }
    public MatchLeaveKind? LeaveKind { get; set; }
    public TeamType Team { get; set; }
    public Key HeroKey { get; set; }
    public Key SkinKey { get; set; }
    public Dictionary<int, Key> Devices { get; set; } = [];
    public List<Key> Perks { get; set; } = [];
    public Dictionary<Key, int> DeviceLevels { get; set; } = [];
}

public sealed class CompletedMatchPlayer
{
    public uint PlayerId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public ulong? SquadId { get; set; }
    public bool WasInitial { get; set; }
    public bool WasBackfiller { get; set; }
    public bool IsWinner { get; set; }
    // Rating means are stored in their native TrueSkill scale.  The control panel formats them
    // in display MMR points, just as it does everywhere else.
    public double? StartingRatingMean { get; set; }
    // TrueSkill standard deviation: the uncertainty that determines how much MMR can move.
    public double? StartingRatingDeviation { get; set; }
    public double? RatingDelta { get; set; }
    public double? RatingDeviationDelta { get; set; }
    public Dictionary<PlayerMatchStatType, int> Stats { get; set; } = [];
    // Keep the unprojected server counters as well as the client-facing score categories.
    // The latter cannot faithfully reconstruct damage, healing, or detailed build breakdowns.
    public Dictionary<ScoreType, float> RawStats { get; set; } = [];
    public Dictionary<Key, CompletedMatchDeviceStats> DeviceStats { get; set; } = [];
    // Kill accolade counts (KillAccoladeTracker). Null means the match predates accolade tracking or came from a
    // region that does not record it, which the profile shows as unknown rather than zero.
    public Dictionary<KillAccolade, int>? Accolades { get; set; }
    public int BestStreak { get; set; }
    public double? EffectiveAllyHealing { get; set; }
    public int TotalScore { get; set; }
    public List<CompletedMatchPresence> Presences { get; set; } = [];
}

public sealed class CompletedMatchDeviceStats
{
    public int Placed { get; set; }
    public int Destroyed { get; set; }
}

public sealed class CompletedMatchTeam
{
    public TeamType Team { get; set; }
    public bool IsWinner { get; set; }
    public int CubesAtStart { get; set; }
    public int CubesRemaining { get; set; }
    public bool BaseDestroyed { get; set; }
}

public sealed class CompletedMatchRecord
{
    private const byte ArchiveExtensionVersion = 3;
    private const byte LegacyArchiveExtensionVersion = 1;
    private const byte RatingDeviationExtensionVersion = 2;
    // A separate block after the version 3 extension: readers that predate it stop at the end of that extension,
    // so regions and the master can be upgraded in either order.
    private const byte AccoladeBlockVersion = 1;
    public MatchType? MatchType { get; set; }
    public int RatingVersion { get; set; }
    public ulong? ActiveStartedAt { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public Key MapKey { get; set; }
    public Key GameModeKey { get; set; }
    public ulong StartedAt { get; set; }
    public ulong EndedAt { get; set; }
    public TeamType Winner { get; set; }
    public MatchEndReason EndReason { get; set; }
    public List<CompletedMatchTeam> Teams { get; set; } = [];
    public List<CompletedMatchPlayer> Players { get; set; } = [];

    public void Write(BinaryWriter writer)
    {
        writer.Write(MatchId);
        MapKey.Write(writer);
        GameModeKey.Write(writer);
        writer.Write(StartedAt);
        writer.Write(EndedAt);
        writer.WriteByteEnum(Winner);
        writer.WriteList(Teams, (w, t) =>
        {
            w.WriteByteEnum(t.Team); w.Write(t.IsWinner); w.Write(t.CubesAtStart);
            w.Write(t.CubesRemaining); w.Write(t.BaseDestroyed);
        });
        writer.WriteList(Players, (w, p) =>
        {
            w.Write(p.PlayerId); w.Write(p.Nickname); w.Write(p.SquadId.HasValue);
            if (p.SquadId.HasValue) w.Write(p.SquadId.Value);
            w.Write(p.WasInitial); w.Write(p.WasBackfiller); w.Write(p.IsWinner);
            w.WriteMap(p.Stats, w.WriteByteEnum, w.Write); w.Write(p.TotalScore);
            w.WriteList(p.Presences, (pw, presence) =>
            {
                pw.Write(presence.Sequence); pw.Write(presence.JoinedAt); pw.Write(presence.LeftAt.HasValue);
                if (presence.LeftAt.HasValue) pw.Write(presence.LeftAt.Value);
                pw.WriteByteEnum(presence.JoinKind); pw.Write(presence.LeaveKind.HasValue);
                if (presence.LeaveKind.HasValue) pw.WriteByteEnum(presence.LeaveKind.Value);
                pw.WriteByteEnum(presence.Team); presence.HeroKey.Write(pw); presence.SkinKey.Write(pw);
                pw.WriteMap(presence.Devices, pw.Write, Key.WriteRecord);
                pw.WriteList(presence.Perks, Key.WriteRecord);
                pw.WriteMap(presence.DeviceLevels, Key.WriteRecord, pw.Write);
            });
        });
        // Appended so an old master can safely ignore it and a new master can still accept an
        // archive sent by an older region during a rolling restart.
        writer.WriteMap(Players.Where(player => player.StartingRatingMean.HasValue)
            .ToDictionary(player => player.PlayerId, player => player.StartingRatingMean!.Value), writer.Write, writer.Write);
        writer.WriteByteEnum(EndReason);
        // Keep additions after the old terminal field. Old masters read EndReason and safely
        // ignore this tail during a rolling upgrade.
        writer.Write(ArchiveExtensionVersion);
        writer.WriteList(Players, (w, player) =>
        {
            w.Write(player.PlayerId);
            w.Write(player.StartingRatingDeviation.HasValue);
            if (player.StartingRatingDeviation.HasValue) w.Write(player.StartingRatingDeviation.Value);
            w.WriteMap(player.RawStats, w.WriteByteEnum, w.Write);
            w.WriteMap(player.DeviceStats, Key.WriteRecord, (pw, stats) =>
            {
                pw.Write(stats.Placed);
                pw.Write(stats.Destroyed);
            });
            w.WriteList(player.Presences, (pw, presence) =>
            {
                pw.Write(presence.Sequence);
                pw.Write(presence.TeamSlot);
            });
        });
        writer.Write(AccoladeBlockVersion);
        writer.WriteList(Players.Where(player => player.Accolades is not null).ToList(), (w, player) =>
        {
            w.Write(player.PlayerId);
            w.Write(player.BestStreak);
            w.WriteMap(player.Accolades!, w.WriteByteEnum, w.Write);
        });
        if (RatingVersion > 0)
        {
            writer.Write((byte)2); // personal MMR trailer, separate from accolades
            writer.Write(RatingVersion);
            writer.Write(ActiveStartedAt.HasValue);
            if (ActiveStartedAt.HasValue) writer.Write(ActiveStartedAt.Value);
            writer.Write(MatchType.HasValue);
            if (MatchType.HasValue) writer.Write((byte)MatchType.Value);
            writer.WriteList(Players, (w,p) => { w.Write(p.PlayerId); w.Write(p.EffectiveAllyHealing.HasValue); if(p.EffectiveAllyHealing.HasValue) w.Write(p.EffectiveAllyHealing.Value); });
        }
    }

    public static CompletedMatchRecord ReadRecord(BinaryReader reader)
    {
        var result = new CompletedMatchRecord
        {
            MatchId = reader.ReadString(),
            MapKey = Key.ReadRecord(reader),
            GameModeKey = Key.ReadRecord(reader),
            StartedAt = reader.ReadUInt64(),
            EndedAt = reader.ReadUInt64(),
            Winner = reader.ReadByteEnum<TeamType>()
        };
        result.Teams = reader.ReadList<CompletedMatchTeam, List<CompletedMatchTeam>>(() => new CompletedMatchTeam
        {
            Team = reader.ReadByteEnum<TeamType>(),
            IsWinner = reader.ReadBoolean(),
            CubesAtStart = reader.ReadInt32(),
            CubesRemaining = reader.ReadInt32(),
            BaseDestroyed = reader.ReadBoolean()
        });
        result.Players = reader.ReadList<CompletedMatchPlayer, List<CompletedMatchPlayer>>(() =>
        {
            var player = new CompletedMatchPlayer { PlayerId = reader.ReadUInt32(), Nickname = reader.ReadString() };
            if (reader.ReadBoolean()) player.SquadId = reader.ReadUInt64();
            player.WasInitial = reader.ReadBoolean(); player.WasBackfiller = reader.ReadBoolean();
            player.IsWinner = reader.ReadBoolean();
            player.Stats = reader.ReadMap<PlayerMatchStatType, int, Dictionary<PlayerMatchStatType, int>>(
                reader.ReadByteEnum<PlayerMatchStatType>, reader.ReadInt32);
            player.TotalScore = reader.ReadInt32();
            player.Presences = reader.ReadList<CompletedMatchPresence, List<CompletedMatchPresence>>(() =>
            {
                var presence = new CompletedMatchPresence { Sequence = reader.ReadInt32(), JoinedAt = reader.ReadUInt64() };
                if (reader.ReadBoolean()) presence.LeftAt = reader.ReadUInt64();
                presence.JoinKind = reader.ReadByteEnum<MatchJoinKind>();
                if (reader.ReadBoolean()) presence.LeaveKind = reader.ReadByteEnum<MatchLeaveKind>();
                presence.Team = reader.ReadByteEnum<TeamType>(); presence.HeroKey = Key.ReadRecord(reader);
                presence.SkinKey = Key.ReadRecord(reader);
                presence.Devices = reader.ReadMap<int, Key, Dictionary<int, Key>>(reader.ReadInt32, Key.ReadRecord);
                presence.Perks = reader.ReadList<Key, List<Key>>(Key.ReadRecord);
                presence.DeviceLevels = reader.ReadMap<Key, int, Dictionary<Key, int>>(Key.ReadRecord, reader.ReadInt32);
                return presence;
            });
            return player;
        });
        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var startingRatings = reader.ReadMap<uint, double, Dictionary<uint, double>>(
                reader.ReadUInt32, reader.ReadDouble);
            foreach (var player in result.Players)
                if (startingRatings.TryGetValue(player.PlayerId, out var rating)) player.StartingRatingMean = rating;
        }
        if (reader.BaseStream.Position < reader.BaseStream.Length)
            result.EndReason = reader.ReadByteEnum<MatchEndReason>();
        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var extensionVersion = reader.ReadByte();
            if (extensionVersion is not (LegacyArchiveExtensionVersion or RatingDeviationExtensionVersion or ArchiveExtensionVersion)) return result;
            var playersById = result.Players.ToDictionary(player => player.PlayerId);
            foreach (var extension in reader.ReadList<(uint playerId, double? deviation,
                         Dictionary<ScoreType, float> rawStats, Dictionary<Key, CompletedMatchDeviceStats> deviceStats, Dictionary<int, int> slots),
                     List<(uint playerId, double? deviation, Dictionary<ScoreType, float> rawStats, Dictionary<Key, CompletedMatchDeviceStats> deviceStats, Dictionary<int, int> slots)>>(() =>
                     {
                         var playerId = reader.ReadUInt32();
                         double? deviation = reader.ReadBoolean() ? reader.ReadDouble() : null;
                         var rawStats = reader.ReadMap<ScoreType, float, Dictionary<ScoreType, float>>(
                             reader.ReadByteEnum<ScoreType>, reader.ReadSingle);
                         var deviceStats = extensionVersion == ArchiveExtensionVersion
                             ? reader.ReadMap<Key, CompletedMatchDeviceStats, Dictionary<Key, CompletedMatchDeviceStats>>(
                                 Key.ReadRecord, () => new CompletedMatchDeviceStats { Placed = reader.ReadInt32(), Destroyed = reader.ReadInt32() })
                             : [];
                         var slots = reader.ReadList<(int sequence, int slot), List<(int sequence, int slot)>>(() =>
                             (reader.ReadInt32(), reader.ReadInt32())).ToDictionary(slot => slot.sequence, slot => slot.slot);
                         return (playerId, deviation, rawStats, deviceStats, slots);
                     }))
            {
                if (!playersById.TryGetValue(extension.playerId, out var player)) continue;
                // Version 1 contained the unrelated Glicko volatility constant. Retain none of
                // it; version 2 records the TrueSkill deviation used for MMR movement instead.
                if (extensionVersion is RatingDeviationExtensionVersion or ArchiveExtensionVersion)
                    player.StartingRatingDeviation = extension.deviation;
                player.RawStats = extension.rawStats;
                player.DeviceStats = extension.deviceStats;
                foreach (var presence in player.Presences)
                    if (extension.slots.TryGetValue(presence.Sequence, out var slot)) presence.TeamSlot = slot;
            }
            if (reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadByte() == AccoladeBlockVersion)
            {
                foreach (var accolades in reader.ReadList<(uint playerId, int bestStreak, Dictionary<KillAccolade, int> counts),
                             List<(uint playerId, int bestStreak, Dictionary<KillAccolade, int> counts)>>(() =>
                             (reader.ReadUInt32(), reader.ReadInt32(),
                                 reader.ReadMap<KillAccolade, int, Dictionary<KillAccolade, int>>(reader.ReadByteEnum<KillAccolade>, reader.ReadInt32))))
                {
                    if (!playersById.TryGetValue(accolades.playerId, out var player)) continue;
                    player.Accolades = accolades.counts;
                    player.BestStreak = accolades.bestStreak;
                }
            }
        }
        if (reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadByte() == 2)
        {
            result.RatingVersion = reader.ReadInt32();
            result.ActiveStartedAt = reader.ReadBoolean() ? reader.ReadUInt64() : null;
            result.MatchType = reader.ReadBoolean() ? (MatchType)reader.ReadByte() : null;
            foreach(var entry in reader.ReadList<(uint id,double? heal),List<(uint id,double? heal)>>(() => (reader.ReadUInt32(),reader.ReadBoolean()?reader.ReadDouble():null)))
                if(result.Players.FirstOrDefault(p=>p.PlayerId==entry.id) is {} p) p.EffectiveAllyHealing=entry.heal;
        }
        return result;
    }
}
