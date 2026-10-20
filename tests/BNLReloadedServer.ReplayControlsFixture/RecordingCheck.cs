using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;
using System.Diagnostics;


// Checks catch-up folding against real recordings (opt-in: --recordings <file>...):
// exact protocol round-trips for every unit/block/zone update, folded state
// against an independent oracle of the client's apply rules, the keyframe map
// index against first-touch block hits, and the size and server time of
// representative seeks and in-place rewinds.
static class RecordingCheck
{
    static int failures;

    public static int Run(IEnumerable<string> paths)
    {
        failures = 0;
        foreach (var path in paths)
        {
            var tl = new ReplayTimeline(path);
            Console.WriteLine($"== {Path.GetFileName(path)} duration {tl.DurationMs / 1000}s phases {string.Join(", ", tl.Phases.Select(p => $"{p.Phase}@{p.At / 1000}s"))}");
            RoundTrip(path);
            long build = tl.NextCombat(tl.FirstPlayableOffset);
            var windows = new List<(string, long, long)>();
            if (build > 0) windows.Add(("skip build from start", 0, build));
            foreach (var t in new long[] { 30_000, 300_000, 600_000 }) if (t + 10_000 < tl.DurationMs) windows.Add(($"+10s at {t / 1000}s", t, t + 10_000));
            foreach (var t in new long[] { 60_000, 300_000, 600_000, tl.DurationMs - 10_000 }) if (t < tl.DurationMs) windows.Add(($"rewind rebuild 0->{t / 1000}s", 0, t));
            foreach (var (name, from, to) in windows) Simulate(path, name, from, to);
            KeyframeCheck(path);
            foreach (var p in new long[] { 120_000, 300_000, 600_000, tl.DurationMs - 5_000 }) if (p < tl.DurationMs && p > 10_000) Rewind(path, p, p - 10_000);
        }
        Console.WriteLine(failures == 0 ? "RECORDING_CHECK_OK" : $"RECORDING_CHECK_FAILURES {failures}");
        return failures;
    }

    static void Simulate(string path, string name, long from, long to)
    {
        using var r = new ReplayReader(path);
        r.Next(); r.Next();
        ReplayRecord? rec;
        var raw = new Dictionary<int, int>();
        while ((rec = r.Next()) != null && rec.OffsetMs <= from) { }
        var sw = Stopwatch.StartNew();
        var c = new ReplayCatchUp();
        long records = 0;
        // window semantics in ReplayInstance: records with offset <= target
        while (rec != null && rec.Kind != MatchRecorder.KindEnd && rec.OffsetMs <= to)
        {
            if (rec.Kind == MatchRecorder.KindStream) { c.Add(rec.Data); records++; }
            rec = r.Next();
        }
        var chunks = c.Build();
        sw.Stop();
        var outMix = new Dictionary<int, int>();
        foreach (var chunk in chunks) foreach (var (m, _) in Frames(chunk)) outMix[m] = outMix.GetValueOrDefault(m) + 1;
        Console.WriteLine($"  {name,-26} {records,7} records  {c.InputPackets,8} -> {c.OutputPackets,6} packets ({100.0 * c.OutputPackets / Math.Max(1, c.InputPackets):F1}%)  {c.InputBytes / 1024,6} -> {c.OutputBytes / 1024,5} KiB  {chunks.Count,4} chunks  {sw.ElapsedMilliseconds,5} ms");
        Console.WriteLine("      out mix: " + string.Join(" ", outMix.OrderByDescending(k => k.Value).Take(10).Select(k => $"{k.Key}:{k.Value}")));
    }

    static IEnumerable<(int msg, byte[] body)> Frames(byte[] data)
    {
        var input = new BinaryReader(new MemoryStream(data));
        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            int len = input.Read7BitEncodedInt();
            var body = input.ReadBytes(len);
            yield return (body.Length > 1 ? body[1] : -1, body);
        }
    }

    static void RoundTrip(string path)
    {
        using var r = new ReplayReader(path);
        r.Next(); r.Next();
        ReplayRecord? rec;
        long units = 0, blocks = 0, zones = 0, mismatches = 0, oracleUnits = 0;
        var zoneSeq = new Dictionary<string, string>(); ZoneUpdate? zoneMerged = null;
        var sequential = new Dictionary<uint, Dictionary<string, string>>();
        var merged = new Dictionary<uint, UnitUpdate>();
        while ((rec = r.Next()) != null)
        {
            if (rec.Kind != MatchRecorder.KindStream) continue;
            foreach (var (m, body) in Frames(rec.Data))
            {
                if (body[0] != 6) continue;
                if (m == 11)
                {
                    units++;
                    var input = new BinaryReader(new MemoryStream(body, 2, body.Length - 2));
                    uint id = input.ReadUInt32();
                    var u = UnitUpdate.ReadRecord(input);
                    var again = ReplayCatchUp.UnitUpdatePacket(id, u);
                    var expect = Frame(body);
                    if (!again.AsSpan().SequenceEqual(expect)) { if (mismatches++ < 3) Console.WriteLine($"   unit update round-trip mismatch id {id}"); }
                    Apply(sequential.TryGetValue(id, out var st) ? st : sequential[id] = new(), u);
                    var copy = UnitUpdate.ReadRecord(new BinaryReader(new MemoryStream(body, 6, body.Length - 6)));
                    if (merged.TryGetValue(id, out var pending)) ReplayCatchUp.Merge(pending, copy); else merged[id] = copy;
                }
                else if (m == 9)
                {
                    zones++;
                    var input = new BinaryReader(new MemoryStream(body, 2, body.Length - 2));
                    var z = ZoneUpdate.ReadRecord(input);
                    if (input.BaseStream.Position != input.BaseStream.Length || !ReplayCatchUp.ZoneUpdatePacket(z).AsSpan().SequenceEqual(Frame(body)))
                    { if (mismatches++ < 3) Console.WriteLine("   zone update round-trip mismatch"); }
                    ApplyZone(zoneSeq, z);
                    var copy = ZoneUpdate.ReadRecord(new BinaryReader(new MemoryStream(body, 2, body.Length - 2)));
                    if (zoneMerged == null) zoneMerged = copy; else ReplayCatchUp.Merge(zoneMerged, copy);
                }
                else if (m == 8)
                {
                    blocks++;
                    var input = new BinaryReader(new MemoryStream(body, 2, body.Length - 2));
                    var map = input.ReadMap<Vector3s, BlockUpdate, Dictionary<Vector3s, BlockUpdate>>(input.ReadVector3s, BlockUpdate.ReadRecord);
                    if (input.BaseStream.Position != input.BaseStream.Length || !ReplayCatchUp.BlockUpdatesPacket(map).AsSpan().SequenceEqual(Frame(body)))
                    { if (mismatches++ < 3) Console.WriteLine("   block update round-trip mismatch"); }
                }
            }
        }
        foreach (var (id, u) in merged)
        {
            var one = new Dictionary<string, string>();
            Apply(one, u);
            oracleUnits++;
            if (!one.OrderBy(k => k.Key).SequenceEqual(sequential[id].OrderBy(k => k.Key))) { if (mismatches++ < 3) Console.WriteLine($"   merge != sequential for unit {id}"); }
        }
        if (zoneMerged != null)
        {
            var one = new Dictionary<string, string>(); ApplyZone(one, zoneMerged);
            if (!one.OrderBy(k => k.Key).SequenceEqual(zoneSeq.OrderBy(k => k.Key))) { mismatches++; Console.WriteLine("   zone merge != sequential"); }
        }
        failures += (int)Math.Min(int.MaxValue, mismatches);
        Console.WriteLine($"   round-trip: {units} unit updates, {blocks} block updates, {zones} zone updates, merge oracle (+zone) over {oracleUnits} units: {(mismatches == 0 ? "exact" : mismatches + " mismatches")}");
    }

    static byte[] Frame(byte[] body)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write7BitEncodedInt(body.Length); w.Write(body); return ms.ToArray();
    }

    // Independent oracle of the client's Unit.UpdateData: Ammo/Devices merge per key, other fields replace.
    // Each field value is encoded alone through the protocol writer.
    static void Apply(Dictionary<string, string> state, UnitUpdate u)
    {
        foreach (var p in typeof(UnitUpdate).GetProperties())
        {
            var v = p.GetValue(u);
            if (v == null) continue;
            if (p.Name is "Ammo" or "Devices")
                foreach (System.Collections.DictionaryEntry e in (System.Collections.IDictionary)v)
                {
                    var single = (System.Collections.IDictionary)Activator.CreateInstance(v.GetType())!;
                    single[e.Key] = e.Value;
                    var one = new UnitUpdate(); p.SetValue(one, single);
                    var keyOnly = (System.Collections.IDictionary)Activator.CreateInstance(v.GetType())!;
                    keyOnly[e.Key] = e.Value;
                    state[p.Name + "[" + Hex(KeyBytes(p, e.Key)) + "]"] = Hex(Encode(one));
                }
            else { var one = new UnitUpdate(); p.SetValue(one, v); state[p.Name] = Hex(Encode(one)); }
        }
    }
    static byte[] Encode(UnitUpdate u) { var ms = new MemoryStream(); UnitUpdate.WriteRecord(new BinaryWriter(ms), u); return ms.ToArray(); }
    static byte[] KeyBytes(System.Reflection.PropertyInfo p, object key)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        if (key is Key k) Key.WriteRecord(w, k); else w.Write((int)key);
        return ms.ToArray();
    }
    static string Hex(byte[] b) => Convert.ToHexString(b);

    // Oracle of the client's ZoneData.UpdateData.
    static void ApplyZone(Dictionary<string, string> state, ZoneUpdate z)
    {
        foreach (var p in typeof(ZoneUpdate).GetProperties())
        {
            var v = p.GetValue(z);
            if (v == null) continue;
            if (p.Name == "PlayerInfo")
                foreach (var (k, info) in (Dictionary<uint, ZonePlayerInfo>)v)
                    state["PlayerInfo[" + k + "]"] = Hex(EncodeZone(new ZoneUpdate { PlayerInfo = new() { [k] = info } }));
            else if (p.Name == "Statistics")
            {
                var st = (MatchStats)v;
                if (st.PlayerStats != null)
                    foreach (var (k, ps) in st.PlayerStats)
                    {
                        if (ps.Team.HasValue) state["PS[" + k + "].team"] = ps.Team.ToString()!;
                        state["PS[" + k + "].kda"] = $"{ps.Kills}/{ps.Deaths}/{ps.Assists}";
                    }
                if (st.Team1Stats != null) state["T1"] = Hex(EncodeZone(new ZoneUpdate { Statistics = new MatchStats { Team1Stats = st.Team1Stats } }));
                if (st.Team2Stats != null) state["T2"] = Hex(EncodeZone(new ZoneUpdate { Statistics = new MatchStats { Team2Stats = st.Team2Stats } }));
            }
            else { var one = new ZoneUpdate(); p.SetValue(one, v); state[p.Name] = Hex(EncodeZone(one)); }
        }
    }
    static byte[] EncodeZone(ZoneUpdate z) { var ms = new MemoryStream(); ZoneUpdate.WriteRecord(new BinaryWriter(ms), z); return ms.ToArray(); }

    static void KeyframeCheck(string path)
    {
        using var r = new ReplayReader(path);
        var init = r.Next()!; r.Next();
        var first = new Dictionary<Vector3s, BlockUpdate>();
        ReplayRecord? rec;
        while ((rec = r.Next()) != null)
        {
            if (rec.Kind != MatchRecorder.KindStream) continue;
            foreach (var (m, body) in Frames(rec.Data))
            {
                if (body[0] != 6 || m != 8) continue;
                var input = new BinaryReader(new MemoryStream(body, 2, body.Length - 2));
                foreach (var (pos, b) in input.ReadMap<Vector3s, BlockUpdate, Dictionary<Vector3s, BlockUpdate>>(input.ReadVector3s, BlockUpdate.ReadRecord))
                    first.TryAdd(pos, b);
            }
        }
        var damaged = first.Where(kv => kv.Value.Id != 0 && kv.Value.Damage > 0).Select(kv => kv.Key).ToList();
        var sw = Stopwatch.StartNew();
        var key = ReplayWorld.KeyframeBlocks(init.Data, first.Keys);
        sw.Stop();
        int agree = damaged.Count(p => key.TryGetValue(p, out var k) && k.Id == first[p].Id);
        Console.WriteLine($"   keyframe map: {first.Count} touched positions looked up in {sw.ElapsedMilliseconds} ms; first-touch damage hits whose keyframe id matches: {agree}/{damaged.Count}");
        if (damaged.Count > 0 && agree < damaged.Count * 0.98) { failures++; Console.WriteLine("   KEYFRAME INDEX MISMATCH"); }
    }

    static void Rewind(string path, long at, long target)
    {
        // Play to `at` exactly as the viewer received it, tracking the client world.
        var world = new ReplayWorld();
        using (var r = new ReplayReader(path))
        {
            r.Next(); world.Observe(r.Next()!.Data);
            ReplayRecord? rec;
            while ((rec = r.Next()) != null && rec.OffsetMs <= at) if (rec.Kind == MatchRecorder.KindStream) world.Observe(rec.Data);
        }
        var sw = Stopwatch.StartNew();
        using var reader = new ReplayReader(path);
        var init = reader.Next()!; var load = reader.Next()!;
        var c = new ReplayCatchUp();
        c.Add(world.RemovalFrames());
        c.Add(load.Data);
        ReplayRecord? next;
        while ((next = reader.Next()) != null && next.Kind != MatchRecorder.KindEnd && next.OffsetMs <= target)
            if (next.Kind == MatchRecorder.KindStream) c.Add(next.Data);
        var touched = world.Blocks.Where(p => !c.HasBlock(p)).ToList();
        if (touched.Count > 0) c.RestoreBlocks(ReplayWorld.KeyframeBlocks(init.Data, touched));
        var chunks = c.Build();
        sw.Stop();
        var mix = new Dictionary<int, int>();
        foreach (var chunk in chunks) foreach (var (m, _) in Frames(chunk)) mix[m] = mix.GetValueOrDefault(m) + 1;
        Console.WriteLine($"  in-place rewind {at / 1000}s -> {target / 1000}s: client held {world.Units.Count} units, {world.Shots.Count} shots, {world.Blocks.Count} touched blocks ({touched.Count} back to keyframe); out {c.OutputPackets} packets {c.OutputBytes / 1024} KiB in {chunks.Count} chunks; server {sw.ElapsedMilliseconds} ms");
        Console.WriteLine("      out mix: " + string.Join(" ", mix.OrderByDescending(k => k.Value).Take(10).Select(k => $"{k.Key}:{k.Value}")));
    }
}
