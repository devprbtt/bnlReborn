using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Servers;

namespace BNLReloadedServer.ServerTypes;

// Folds the recorded zone traffic a replay seek skips over into the state it
// leaves behind. Replaying that history through the client's gameplay handlers
// is what made seeks slow: one build phase is ~400k packets, nearly all of them
// superseded health/ammo/resource values. Here unit, block and zone updates
// collapse to their final values (with the client's own apply rules), movement
// and aim keep only the newest sample, units and projectiles that appear and
// vanish inside the window are elided, superseded build ghosts and tool-loop
// toggles are dropped, and packets that only spawn effects or sounds are
// skipped. Every other packet is kept in recorded order. The output is ordinary
// zone packets, so it needs nothing a live spectator client does not have.
public sealed class ReplayCatchUp
{
    private const byte ZoneService = 6;
    private const byte BlockUpdates = 8, UpdateZone = 9, UnitCreate = 10, UnitUpdateId = 11, UnitDrop = 12, UnitMove = 13;
    private const byte DoStartBuild = 30, DoCancelBuild = 31, CreateProjectile = 56, MoveProjectile = 57, DropProjectile = 58;
    private const byte BroadcastZoneEvent = 63, Kill = 67, DeviceBuilt = 69, AimLook = 108;

    // Effects, sounds and notifications whose client handlers keep no state a
    // later packet depends on: casts, impacts, damage numbers, block-mined and
    // pickup feedback, resource-bonus toast, portal flashes, turret/mortar/
    // tesla/drill attack visuals, pings, emotes and build previews.
    private static readonly bool[] EffectOnly = Table(18, 19, 68, 70, 71, 72, 74, 87, 88, 90, 92, 94, 100, 102, 104);

    // Messages whose first field is the unit they act on; for a unit elided
    // from the window they would only reach a missing unit.
    private static readonly bool[] UnitScoped = Table(14, 15, 16, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 52, 77, 78, 79);

    private readonly List<byte[]?> _ordered = [];
    private readonly Dictionary<Vector3s, BlockUpdate> _blocks = new();
    private ZoneUpdate? _zone;
    private readonly Dictionary<uint, UnitUpdate> _updates = new();
    private readonly Dictionary<uint, byte[]> _moves = new();
    private readonly Dictionary<uint, byte[]> _looks = new();
    private readonly Dictionary<uint, List<int>> _bornUnits = new();
    private readonly Dictionary<ulong, List<int>> _bornShots = new();
    private readonly Dictionary<uint, uint> _playerUnits = new();
    private readonly Dictionary<(bool Player, uint Id), int> _buildEvents = new();
    private readonly Dictionary<(ZoneEventType, uint, byte), int> _unitEvents = new();

    public int InputPackets { get; private set; }
    public long InputBytes { get; private set; }
    public int OutputPackets { get; private set; }
    public long OutputBytes { get; private set; }

    private static bool[] Table(params byte[] ids)
    {
        var table = new bool[256];
        foreach (var id in ids) table[id] = true;
        return table;
    }

    // One recorded stream record: concatenated 7-bit length-prefixed packets.
    public void Add(byte[] record)
    {
        int offset = 0;
        while (offset < record.Length)
        {
            int start = offset, length = 0, shift = 0;
            byte b;
            do
            {
                if (offset >= record.Length || shift > 28) { Keep(record[start..]); return; }
                b = record[offset++];
                length |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            if (length < 0 || offset + length > record.Length) { Keep(record[start..]); return; }
            InputPackets++;
            InputBytes += offset - start + length;
            Packet(record, start, offset, length);
            offset += length;
        }
    }

    private void Packet(byte[] record, int frameStart, int bodyStart, int length)
    {
        byte[] Frame() => record[frameStart..(bodyStart + length)];
        if (length < 2 || record[bodyStart] != ZoneService) { Keep(Frame()); return; }
        byte message = record[bodyStart + 1];
        if (EffectOnly[message]) return;
        int start = bodyStart + 2, size = length - 2;
        var body = new ReadOnlySpan<byte>(record, start, size);
        switch (message)
        {
            case UnitUpdateId when size >= 4:
                MergeUnitUpdate(record, start, size, Frame);
                return;
            case UnitMove when size >= 4:
                _moves[BitConverter.ToUInt32(body)] = Frame();
                return;
            case AimLook when size >= 4:
                _looks[BitConverter.ToUInt32(body)] = Frame();
                return;
            case BlockUpdates:
                if (!MergeBlocks(record, start, size)) Keep(Frame());
                return;
            case UpdateZone:
                if (!MergeZone(record, start, size)) Keep(Frame());
                return;
            case UnitCreate when size >= 4:
            {
                uint id = BitConverter.ToUInt32(body);
                Forget(id);
                if (Parse(record, start + 4, size - 4, UnitInit.ReadRecord) is { PlayerId: { } player }) _playerUnits[player] = id;
                _bornUnits[id] = [_ordered.Count];
                Keep(Frame());
                return;
            }
            case UnitDrop when size >= 4:
            {
                uint id = BitConverter.ToUInt32(body);
                Forget(id);
                _buildEvents.Remove((false, id));
                if (_bornUnits.Remove(id, out var born)) Elide(born);
                else Keep(Frame());
                return;
            }
            case DoStartBuild or DoCancelBuild when size >= 4:
            {
                uint id = BitConverter.ToUInt32(body);
                Track(_bornUnits, id);
                KeepLatestBuild((false, id), Frame());
                return;
            }
            case DeviceBuilt when size >= 4:
            {
                // Keyed by builder player; resolved to the unit when its create was seen.
                uint player = BitConverter.ToUInt32(body);
                if (_playerUnits.TryGetValue(player, out var builder))
                {
                    Track(_bornUnits, builder);
                    KeepLatestBuild((false, builder), Frame());
                }
                else KeepLatestBuild((true, player), Frame());
                return;
            }
            case Kill:
                // The client ignores a kill whose unit it does not hold.
                if (Parse(record, start, size, KillInfo.ReadRecord) is { } kill) Track(_bornUnits, kill.DeadUnitId);
                Keep(Frame());
                return;
            case BroadcastZoneEvent when size >= 1:
                ZoneEventPacket(record, start, size, Frame);
                return;
            case CreateProjectile when size >= 8:
                _bornShots[BitConverter.ToUInt64(body)] = [_ordered.Count];
                Keep(Frame());
                return;
            case MoveProjectile when size >= 8:
                Track(_bornShots, BitConverter.ToUInt64(body));
                Keep(Frame());
                return;
            case DropProjectile when size >= 8:
                if (_bornShots.Remove(BitConverter.ToUInt64(body), out var shot)) Elide(shot);
                else Keep(Frame());
                return;
        }
        if (UnitScoped[message] && size >= 4) Track(_bornUnits, BitConverter.ToUInt32(body));
        Keep(Frame());
    }

    private void ZoneEventPacket(byte[] record, int start, int size, Func<byte[]> frame)
    {
        switch ((ZoneEventType)record[start])
        {
            // Pad bounces, double jumps and single shots only animate.
            case ZoneEventType.SpeedPadUsed or ZoneEventType.JumpPadUsed or ZoneEventType.DoubleJump
                or ZoneEventType.ToolFire or ZoneEventType.TurretFire:
                return;
            // Landings (which settle falling devices and supply crates), forced
            // falls and loop/hold toggles leave state, but only each unit's
            // (and tool's) newest event matters.
            case ZoneEventType.UnitCommonLand or ZoneEventType.ForceFall or ZoneEventType.ToolFireLoop or ZoneEventType.ToolHold:
            {
                var zoneEvent = Parse(record, start, size, ZoneEvent.ReadVariant);
                (uint, byte)? key = zoneEvent switch
                {
                    ZoneEventUnitCommonLand land => (land.UnitId, 0),
                    ZoneEventForceFall fall => (fall.UnitId, 0),
                    ZoneEventToolFireLoop loop => (loop.UnitId, loop.ToolIndex),
                    ZoneEventToolHold hold => (hold.UnitId, hold.ToolIndex),
                    _ => null
                };
                if (key is var (unit, tool))
                {
                    if (_unitEvents.TryGetValue((zoneEvent!.Type, unit, tool), out var previous)) _ordered[previous] = null;
                    _unitEvents[(zoneEvent.Type, unit, tool)] = _ordered.Count;
                    Track(_bornUnits, unit);
                }
                Keep(frame());
                return;
            }
            default:
                Keep(frame());
                return;
        }
    }

    private void Keep(byte[] frame) => _ordered.Add(frame);

    private void Track<TKey>(Dictionary<TKey, List<int>> born, TKey id) where TKey : notnull
    {
        if (born.TryGetValue(id, out var frames)) frames.Add(_ordered.Count);
    }

    private void Elide(List<int> frames)
    {
        foreach (var index in frames) _ordered[index] = null;
    }

    // A start spawns the builder's ghost/brackets (replacing any earlier one);
    // a cancel or a built device removes it. Each fully decides that state, so
    // only a builder's newest build event matters, and keeping it also clears
    // a ghost that started before the window.
    private void KeepLatestBuild((bool Player, uint Id) builder, byte[] frame)
    {
        if (_buildEvents.TryGetValue(builder, out var previous)) _ordered[previous] = null;
        _buildEvents[builder] = _ordered.Count;
        Keep(frame);
    }

    // A dropped unit's pending state must not be applied after the drop, or to
    // a later unit that reuses its id.
    private void Forget(uint id)
    {
        _updates.Remove(id);
        _moves.Remove(id);
    }

    private static T? Parse<T>(byte[] record, int start, int size, Func<BinaryReader, T> read) where T : class
    {
        try
        {
            using var input = new BinaryReader(new MemoryStream(record, start, size, false));
            var value = read(input);
            return input.BaseStream.Position == size ? value : null;
        }
        catch (Exception)
        {
            // Any malformed packet is passed through unfolded instead.
            return null;
        }
    }

    private void MergeUnitUpdate(byte[] record, int start, int size, Func<byte[]> frame)
    {
        uint id = BitConverter.ToUInt32(record, start);
        var update = Parse(record, start + 4, size - 4, UnitUpdate.ReadRecord);
        if (update == null)
        {
            // Unparsed fields cannot be merged; keep the packet and everything
            // pending for that unit in their recorded order.
            if (_updates.Remove(id, out var earlier)) Keep(UnitUpdatePacket(id, earlier));
            Keep(frame());
            return;
        }
        if (_updates.TryGetValue(id, out var pending)) Merge(pending, update);
        else _updates[id] = update;
    }

    // Mirrors the client's Unit.UpdateData: ammo and devices merge per key,
    // every other present field replaces the previous value.
    public static void Merge(UnitUpdate into, UnitUpdate from)
    {
        if (from.Team.HasValue) into.Team = from.Team;
        if (from.Health.HasValue) into.Health = from.Health;
        if (from.Forcefield.HasValue) into.Forcefield = from.Forcefield;
        if (from.Shield.HasValue) into.Shield = from.Shield;
        if (from.CapturePoints.HasValue) into.CapturePoints = from.CapturePoints;
        if (from.MovementActive.HasValue) into.MovementActive = from.MovementActive;
        if (from.Ammo != null)
        {
            if (into.Ammo == null) into.Ammo = new Dictionary<Key, List<Ammo>>(from.Ammo);
            else foreach (var (key, value) in from.Ammo) into.Ammo[key] = value;
        }
        if (from.CurrentGear.HasValue) into.CurrentGear = from.CurrentGear;
        if (from.Ability.HasValue) into.Ability = from.Ability;
        if (from.AbilityCharges.HasValue) into.AbilityCharges = from.AbilityCharges;
        if (from.AbilityChargeCooldownEnd.HasValue) into.AbilityChargeCooldownEnd = from.AbilityChargeCooldownEnd;
        if (from.Resource.HasValue) into.Resource = from.Resource;
        if (from.Effects != null) into.Effects = from.Effects;
        if (from.Buffs != null) into.Buffs = from.Buffs;
        if (from.Devices != null)
        {
            if (into.Devices == null) into.Devices = new Dictionary<int, DeviceData>(from.Devices);
            else foreach (var (key, value) in from.Devices) into.Devices[key] = value;
        }
        if (from.TurretTargetId.HasValue) into.TurretTargetId = from.TurretTargetId;
        if (from.CloudAffectedBlocks != null) into.CloudAffectedBlocks = from.CloudAffectedBlocks;
        if (from.ProjectileInitSpeed.HasValue) into.ProjectileInitSpeed = from.ProjectileInitSpeed;
        if (from.BombTimeoutEnd.HasValue) into.BombTimeoutEnd = from.BombTimeoutEnd;
        if (from.DamageCapturers != null) into.DamageCapturers = from.DamageCapturers;
        if (from.PortalLink != null) into.PortalLink = from.PortalLink;
        if (from.TeslaCharge.HasValue) into.TeslaCharge = from.TeslaCharge;
    }

    private bool MergeZone(byte[] record, int start, int size)
    {
        var update = Parse(record, start, size, ZoneUpdate.ReadRecord);
        if (update == null)
        {
            // Keep order with whatever was pending before the unparsed packet.
            if (_zone != null) { Keep(ZoneUpdatePacket(_zone)); _zone = null; }
            return false;
        }
        if (_zone == null) _zone = update;
        else Merge(_zone, update);
        return true;
    }

    // Mirrors the client's ZoneData.UpdateData: per-player stats and player
    // info merge per player (a stat entry keeps its team when one omits it),
    // every other present field replaces the previous value. The phase
    // callback then sees one transition from the pre-seek phase to the final.
    public static void Merge(ZoneUpdate into, ZoneUpdate from)
    {
        if (from.Phase != null) into.Phase = from.Phase;
        if (from.Statistics != null)
        {
            if (into.Statistics == null) into.Statistics = new MatchStats();
            var stats = into.Statistics;
            if (from.Statistics.PlayerStats != null)
            {
                stats.PlayerStats = stats.PlayerStats == null ? new() : new(stats.PlayerStats);
                foreach (var (player, value) in from.Statistics.PlayerStats)
                {
                    var team = value.Team ?? (stats.PlayerStats.TryGetValue(player, out var old) ? old.Team : null);
                    stats.PlayerStats[player] = new MatchPlayerStats
                        { Team = team, Kills = value.Kills, Deaths = value.Deaths, Assists = value.Assists };
                }
            }
            if (from.Statistics.Team1Stats != null) stats.Team1Stats = from.Statistics.Team1Stats;
            if (from.Statistics.Team2Stats != null) stats.Team2Stats = from.Statistics.Team2Stats;
        }
        if (from.SpawnPoints != null) into.SpawnPoints = from.SpawnPoints;
        if (from.PlayerSpawnPoints != null) into.PlayerSpawnPoints = from.PlayerSpawnPoints;
        if (from.RespawnInfo != null) into.RespawnInfo = from.RespawnInfo;
        if (from.PlayerInfo != null)
        {
            into.PlayerInfo = into.PlayerInfo == null ? new() : new(into.PlayerInfo);
            foreach (var (player, info) in from.PlayerInfo) into.PlayerInfo[player] = info;
        }
        if (from.SupplyInfo != null) into.SupplyInfo = from.SupplyInfo;
        if (from.Objectives != null) into.Objectives = from.Objectives;
        if (from.ResourceCap.HasValue) into.ResourceCap = from.ResourceCap;
        if (from.ConquestStateJson != null) into.ConquestStateJson = from.ConquestStateJson;
        if (from.BlockOwnersJson != null) into.BlockOwnersJson = from.BlockOwnersJson;
        if (from.WeatherStartTime.HasValue) into.WeatherStartTime = from.WeatherStartTime;
        if (from.ActionScoresJson != null) into.ActionScoresJson = from.ActionScoresJson;
        if (from.WeaponSkinsJson != null) into.WeaponSkinsJson = from.WeaponSkinsJson;
    }

    private bool MergeBlocks(byte[] record, int start, int size)
    {
        var updates = Parse(record, start, size, input =>
            input.ReadMap<Vector3s, BlockUpdate, Dictionary<Vector3s, BlockUpdate>>(input.ReadVector3s, BlockUpdate.ReadRecord));
        if (updates == null) return false;
        foreach (var (position, update) in updates) _blocks[position] = update;
        return true;
    }

    public bool HasBlock(Vector3s position) => _blocks.ContainsKey(position);

    // Positions the client changed that the window never set go back to these.
    public void RestoreBlocks(Dictionary<Vector3s, BlockUpdate> keyframe)
    {
        foreach (var (position, update) in keyframe) _blocks.TryAdd(position, update);
    }

    // On air, Vdata selects the destroy effect (and falling/splash debris);
    // folded state must not replay a destruction, as the late-join snapshot
    // does not either.
    private static BlockUpdate Settled(BlockUpdate update) => update.Id == 0 && update.Vdata != 0
        ? new BlockUpdate { Id = 0, Damage = update.Damage, Vdata = 0, Ldata = update.Ldata }
        : update;

    // Order: final blocks and zone state first, as a joining spectator gets the
    // map and zone before its units; kept packets in recorded order (unit
    // creates among them); then each surviving unit's state, movement and aim.
    public List<byte[]> Build(int chunkBytes = 32 * 1024)
    {
        var chunks = new List<byte[]>();
        var chunk = new MemoryStream();
        void Emit(byte[] frame)
        {
            if (chunk.Length > 0 && chunk.Length + frame.Length > chunkBytes)
            {
                chunks.Add(chunk.ToArray());
                chunk.SetLength(0);
            }
            chunk.Write(frame);
            OutputPackets++;
            OutputBytes += frame.Length;
        }
        // Bounded packets keep each one well inside the client's receive buffer.
        foreach (var part in _blocks.Chunk(2048))
            Emit(BlockUpdatesPacket(part.ToDictionary(p => p.Key, p => Settled(p.Value))));
        if (_zone != null) Emit(ZoneUpdatePacket(_zone));
        foreach (var frame in _ordered)
            if (frame != null) Emit(frame);
        foreach (var (id, update) in _updates) Emit(UnitUpdatePacket(id, update));
        foreach (var frame in _moves.Values) Emit(frame);
        foreach (var frame in _looks.Values) Emit(frame);
        if (chunk.Length > 0) chunks.Add(chunk.ToArray());
        return chunks;
    }

    private static BinaryWriter ZoneWriter(byte message)
    {
        var writer = new BinaryWriter(new MemoryStream());
        writer.Write(ZoneService);
        writer.Write(message);
        return writer;
    }

    public static byte[] UnitUpdatePacket(uint id, UnitUpdate update)
    {
        using var writer = ZoneWriter(UnitUpdateId);
        writer.Write(id);
        UnitUpdate.WriteRecord(writer, update);
        return RecordingSender.Frame(writer);
    }

    public static byte[] ZoneUpdatePacket(ZoneUpdate update)
    {
        using var writer = ZoneWriter(UpdateZone);
        ZoneUpdate.WriteRecord(writer, update);
        return RecordingSender.Frame(writer);
    }

    public static byte[] BlockUpdatesPacket(Dictionary<Vector3s, BlockUpdate> updates)
    {
        using var writer = ZoneWriter(BlockUpdates);
        writer.WriteMap(updates, writer.Write, BlockUpdate.WriteRecord);
        return RecordingSender.Frame(writer);
    }
}
