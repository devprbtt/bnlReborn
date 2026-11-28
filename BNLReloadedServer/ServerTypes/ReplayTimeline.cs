using System.Text.RegularExpressions;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

public sealed record ReplayEvent(uint At, byte Kind, string Label, uint? Attacker = null, uint? Victim = null);

// Index event metadata only; packet payloads are never retained.
public sealed class ReplayTimeline
{
    public List<ReplayEvent> Events { get; } = [];
    private readonly Dictionary<uint, UnitInit> _units = [];
    private readonly Dictionary<uint, TeamType> _teams = [];
    private readonly Dictionary<uint, string> _names = [];
    private readonly HashSet<uint> _dead = [];
    private readonly KillAccoladeTracker _accolades = new();
    private readonly Func<Key, CardUnit?> _card;
    public uint DurationMs { get; private set; }
    public long FirstPlayableOffset => Phases.FirstOrDefault(p =>
        p.Phase is not (ZonePhaseType.Waiting or ZonePhaseType.TutorialInit)).At;
    public List<(uint At, ZonePhaseType Phase)> Phases { get; } = [];

    public ReplayTimeline(string path, Func<Key, CardUnit?>? card = null)
    {
        _card = card ?? (key => key.GetCard<CardUnit>());
        using var reader = new ReplayReader(path);
        while (reader.Next() is { } record)
        {
            DurationMs = Math.Max(DurationMs, record.OffsetMs);
            if (record.Kind is MatchRecorder.KindLoad or MatchRecorder.KindStream)
                ReadPhases(record);
        }
        _units.Clear(); _teams.Clear(); _names.Clear(); _dead.Clear();
    }

    private void ReadPhases(ReplayRecord record)
    {
        using var input = new BinaryReader(new MemoryStream(record.Data));
        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            var length = input.Read7BitEncodedInt();
            var end = input.BaseStream.Position + length;
            if (length < 2 || end > input.BaseStream.Length) return;
            var service = input.ReadByte();
            var message = input.ReadByte();
            if (service == 6 && message is 9 or 10 or 12 or 67)
            {
                var start = input.BaseStream.Position;
                // Bound optional event decoding to this frame, including older recordings.
                using var packet = new BinaryReader(new MemoryStream(record.Data, (int)start, (int)(end-start), false));
                try { ReadEvent(message, packet, record.OffsetMs, record.Kind == MatchRecorder.KindLoad); }
                catch (Exception e) when (e is IOException or ArgumentException or OverflowException or IndexOutOfRangeException) { }
            }
            if (service == 6 && message == 9)
            {
                // Phase is the first optional member; ignore newer trailing fields.
                var fields = new BitField(12);
                fields.Read(input);
                if (fields[0])
                {
                    var phase = ZonePhase.ReadRecord(input).PhaseType;
                    if (Phases.Count == 0 || Phases[^1].Phase != phase)
                        Phases.Add((record.OffsetMs, phase));
                }
            }
            input.BaseStream.Position = end;
        }
    }

    private string Name(uint id) => _names.GetValueOrDefault(id, "Player " + id);

    private void ReadEvent(byte message, BinaryReader input, uint at, bool snapshot)
    {
        if (message == 9)
        {
            var update = ZoneUpdate.ReadRecord(input);
            if (update.PlayerInfo != null)
                foreach (var (id, info) in update.PlayerInfo)
                    if (!string.IsNullOrWhiteSpace(info.Nickname))
                    {
                        var name = Regex.Replace(info.Nickname, "<[^>]*>", "");
                        _names[id] = name[..Math.Min(64, name.Length)];
                    }
        }
        else if (message == 10)
        {
            var id = input.ReadUInt32();
            var unit = UnitInit.ReadRecord(input);
            bool fresh = !_units.ContainsKey(id);
            _units[id] = unit;
            _dead.Remove(id);
            if (unit.PlayerId is { } player) _teams[player] = unit.Team;
            var labels = _card(unit.Key)?.Labels;
            if (fresh && !snapshot && labels != null)
            {
                if (labels.Contains(UnitLabel.SupplyBlockbuster)) Events.Add(new(at, 2, "BB drop"));
                else if (labels.Contains(UnitLabel.SupplyResource)) Events.Add(new(at, 1, "Supply drop"));
            }
        }
        else if (message == 12) _units.Remove(input.ReadUInt32());
        else if (message == 67 && !snapshot)
        {
            var kill = KillInfo.ReadRecord(input);
            if (!_dead.Add(kill.DeadUnitId)) return;
            _units.TryGetValue(kill.DeadUnitId, out var victim);
            if (victim != null && _card(victim.Key)?.IsObjective == true)
                Events.Add(new(at, 3, "Team " + (byte)victim.Team + " cube destroyed"));
            if ((kill.Dead ?? victim?.PlayerId) is not { } player) return;
            var killer = kill.Killer;
            if (killer is { } attacker && (!_teams.TryGetValue(attacker, out var attackTeam) ||
                !_teams.TryGetValue(player, out var victimTeam) || attackTeam == TeamType.Neutral || victimTeam == TeamType.Neutral || attackTeam == victimTeam)) killer = null;
            foreach (var accolade in _accolades.Register(player, killer, kill.Crit, at/1000.0).Accolades)
                Events.Add(new(at, 0, Name(killer!.Value) + ": " + AccoladeName(accolade), killer, player));
        }
    }

    private static string AccoladeName(KillAccolade value) => value switch
    {
        KillAccolade.FirstKill => "First Kill", KillAccolade.Headshot => "Headshot",
        KillAccolade.DoubleKill => "Double Kill", KillAccolade.MultiKill => "Multi Kill",
        KillAccolade.UltraKill => "Ultra Kill", _ => value.ToString()
    };

    public long NextCombat(long position)
    {
        var current = Phases.LastOrDefault(p => p.At <= position);
        if (current.Phase is ZonePhaseType.Assault or ZonePhaseType.Assault2 or ZonePhaseType.SuddenDeath) return -1;
        foreach (var phase in Phases)
            if (phase.At > position && phase.Phase is ZonePhaseType.Assault or ZonePhaseType.Assault2 or ZonePhaseType.SuddenDeath)
                return phase.At;
        return -1;
    }
}
