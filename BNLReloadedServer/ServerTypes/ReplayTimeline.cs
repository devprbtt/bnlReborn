using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

// Index only phase boundaries, never retain the match's packet payloads in RAM.
public sealed class ReplayTimeline
{
    public uint DurationMs { get; private set; }
    public List<(uint At, ZonePhaseType Phase)> Phases { get; } = [];

    public ReplayTimeline(string path)
    {
        using var reader = new ReplayReader(path);
        while (reader.Next() is { } record)
        {
            DurationMs = Math.Max(DurationMs, record.OffsetMs);
            if (record.Kind is MatchRecorder.KindLoad or MatchRecorder.KindStream)
                ReadPhases(record);
        }
    }

    private void ReadPhases(ReplayRecord record)
    {
        using var input = new BinaryReader(new MemoryStream(record.Data));
        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            var length = input.Read7BitEncodedInt();
            var end = input.BaseStream.Position + length;
            if (length < 2 || end > input.BaseStream.Length) return;
            if (input.ReadByte() == 6 && input.ReadByte() == 9)
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
