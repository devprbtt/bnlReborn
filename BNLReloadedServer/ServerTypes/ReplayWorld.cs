using System.IO.Compression;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.ServerTypes;

// What one replay viewer's client currently holds, observed from every packet
// sent to it: live units, live projectiles and every block position changed
// after the keyframe map. An in-place rewind removes these and restores the
// blocks, instead of reloading the whole Zone scene.
public sealed class ReplayWorld
{
    private const byte ZoneService = 6;

    public HashSet<uint> Units { get; } = [];
    public HashSet<ulong> Shots { get; } = [];
    public HashSet<Vector3s> Blocks { get; } = [];
    // The client keeps end-of-match state that no zone packet reverts.
    public bool Ended { get; private set; }

    public void Observe(byte[] frames)
    {
        int offset = 0;
        while (offset < frames.Length)
        {
            int length = 0, shift = 0;
            byte b;
            do
            {
                if (offset >= frames.Length || shift > 28) return;
                b = frames[offset++];
                length |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            if (length < 0 || offset + length > frames.Length) return;
            if (length >= 2 && frames[offset] == ZoneService) Packet(frames, offset + 2, length - 2, frames[offset + 1]);
            offset += length;
        }
    }

    private void Packet(byte[] frames, int start, int size, byte message)
    {
        switch (message)
        {
            case 3 or 4:
                Ended = true;
                break;
            case 10 when size >= 4:
                Units.Add(BitConverter.ToUInt32(frames, start));
                break;
            case 12 when size >= 4:
                Units.Remove(BitConverter.ToUInt32(frames, start));
                break;
            case 56 when size >= 8:
                Shots.Add(BitConverter.ToUInt64(frames, start));
                break;
            case 58 when size >= 8:
                Shots.Remove(BitConverter.ToUInt64(frames, start));
                break;
            case 8:
                try
                {
                    using var input = new BinaryReader(new MemoryStream(frames, start, size, false));
                    int count = input.Read7BitEncodedInt();
                    for (int i = 0; i < count; i++)
                    {
                        Blocks.Add(input.ReadVector3s());
                        BlockUpdate.ReadRecord(input);
                    }
                }
                catch (Exception) { } // keep what parsed before a malformed packet
                break;
        }
    }

    // Drops for everything the client holds, as ordinary zone packets.
    public byte[] RemovalFrames()
    {
        var output = new MemoryStream();
        foreach (var unit in Units) Frame(output, 12, BitConverter.GetBytes(unit));
        foreach (var shot in Shots) Frame(output, 58, BitConverter.GetBytes(shot));
        return output.ToArray();
    }

    private static void Frame(MemoryStream output, byte message, byte[] payload)
    {
        output.WriteByte((byte)(payload.Length + 2));
        output.WriteByte(ZoneService);
        output.WriteByte(message);
        output.Write(payload);
    }

    // The keyframe map's blocks at the given positions, streamed from the
    // recorded InitZone so a viewer never holds a decompressed map copy.
    public static Dictionary<Vector3s, BlockUpdate> KeyframeBlocks(byte[] initZoneFrames, IEnumerable<Vector3s> positions)
    {
        var result = new Dictionary<Vector3s, BlockUpdate>();
        try { ReadKeyframeBlocks(initZoneFrames, positions, result); }
        catch (Exception) { } // a malformed keyframe restores what it could, never ends playback
        return result;
    }

    private static void ReadKeyframeBlocks(byte[] initZoneFrames, IEnumerable<Vector3s> positions,
        Dictionary<Vector3s, BlockUpdate> result)
    {
        using var frame = new BinaryReader(new MemoryStream(initZoneFrames, false));
        frame.Read7BitEncodedInt();
        if (frame.ReadByte() != ZoneService || frame.ReadByte() != 0) return;
        var init = ZoneInitData.ReadRecord(frame);
        if (init.MapData is not { Length: > 0 } map) return;
        using var blocks = new BinaryReader(new ZLibStream(new MemoryStream(map, false), CompressionMode.Decompress));
        int sizeX = blocks.ReadUInt16(), sizeY = blocks.ReadUInt16(), sizeZ = blocks.ReadUInt16();
        var wanted = positions
            .Where(p => p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < sizeX && p.y < sizeY && p.z < sizeZ)
            .Select(p => (Index: ((long)(p.x * sizeY + p.y) * sizeZ + p.z) * BlockBinary.Size, Position: p))
            .OrderBy(p => p.Index);
        long at = 0;
        var skip = new byte[1 << 16];
        foreach (var (index, position) in wanted)
        {
            for (long gap = index - at; gap > 0;)
            {
                int read = blocks.Read(skip, 0, (int)Math.Min(gap, skip.Length));
                if (read <= 0) return;
                gap -= read;
            }
            var block = blocks.ReadBytes(BlockBinary.Size);
            if (block.Length != BlockBinary.Size) return;
            at = index + BlockBinary.Size;
            result[position] = new BlockUpdate
            {
                Id = BitConverter.ToUInt16(block, 0),
                Damage = block[2],
                Vdata = BitConverter.ToUInt16(block, 3),
                Ldata = block[5]
            };
        }
        // Updates recorded beside the map apply on top of it.
        if (init.Updates != null)
            foreach (var position in result.Keys.ToList())
                if (init.Updates.TryGetValue(position, out var update)) result[position] = update;
        return;
    }
}
