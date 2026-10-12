using System.IO.Compression;

namespace JoinFS.Recording.Format;

/// <summary>Turns a Chunk block into records and back (spec 3.3, 4).</summary>
internal static class ChunkCodec
{
    /// <summary>
    /// Decodes a CRC-checked Chunk block. Returns null (with damage) when the chunk as a whole must be skipped;
    /// a chunk whose records stop parsing comes back with <see cref="RecordChunk.IsComplete"/> false.
    /// </summary>
    public static RecordChunk? Decode(Block block, long offset, int chunkIndex, Func<uint, bool> isBound,
        List<Jfs2Damage> damage, bool precededByGap = false)
    {
        var body = block.Body;
        if (body.Length < Jfs2.ChunkHeaderLength)
        {
            damage.Add(new(DamageKind.ChunkData, offset, "Chunk block is shorter than its fixed part."));
            return null;
        }
        var h = BlockCodec.ParseChunkHeader(body);
        var stored = block.BodyMemory.Slice(Jfs2.ChunkHeaderLength);

        ReadOnlyMemory<byte> payload;
        if (h.UncompressedLength > Jfs2.MaxUncompressedChunk)
        {
            damage.Add(new(DamageKind.ChunkData, offset, $"Chunk claims {h.UncompressedLength} uncompressed bytes."));
            return null;
        }
        switch ((ChunkCompression)h.Compression)
        {
            case ChunkCompression.None:
                if (stored.Length != h.UncompressedLength)
                {
                    damage.Add(new(DamageKind.ChunkData, offset, "Stored chunk length differs from UncompressedLength."));
                    return null;
                }
                payload = stored;
                break;
            case ChunkCompression.Deflate:
                var inflated = Inflate(stored, (int)h.UncompressedLength);
                if (inflated == null)
                {
                    damage.Add(new(DamageKind.ChunkData, offset, "Chunk data does not inflate to UncompressedLength."));
                    return null;
                }
                payload = inflated;
                break;
            default:
                damage.Add(new(DamageKind.ChunkData, offset, $"Unknown chunk compression {h.Compression}."));
                return null;
        }

        var info = new ChunkInfo(offset, h.TMin, h.TMax, h.IsKeyframe) { PrecededByGap = precededByGap };
        var records = ParseRecords(payload, h, isBound, out bool complete, out string? problem);
        if (!complete)
            damage.Add(new(DamageKind.ChunkRecords, offset, problem!));
        return new RecordChunk(chunkIndex, info, records, (int)Math.Min(h.SnapshotCount, (uint)records.Count), complete);
    }

    static byte[]? Inflate(ReadOnlyMemory<byte> stored, int expected)
    {
        try
        {
            var result = new byte[expected];
            using var input = new MemoryStream(stored.ToArray(), false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            deflate.ReadExactly(result);
            return deflate.ReadByte() == -1 ? result : null;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    static List<RecordEntry> ParseRecords(ReadOnlyMemory<byte> payload, ChunkHeader h, Func<uint, bool> isBound,
        out bool complete, out string? problem)
    {
        // A record is at least four bytes, so a larger count cannot fit; this bounds the allocation.
        ulong total = (ulong)h.SnapshotCount + h.RecordCount;
        var records = new List<RecordEntry>((int)Math.Min(total, (ulong)payload.Length / 4));
        var r = new ByteReader(payload.Span);
        long time = h.TMin;
        complete = true;
        problem = null;
        try
        {
            for (ulong i = 0; i < total; i++)
            {
                if (i == h.SnapshotCount)
                    time = h.TMin;
                uint kind = r.VarInt32();
                uint track = r.VarInt32();
                time += r.ZigZag();
                ulong length = r.VarInt();
                if (!isBound(kind))
                    throw new BodyException($"Record {i} uses kind {kind}, which no Schema binds.");
                if (length > (ulong)r.Remaining)
                    throw new BodyException($"Record {i} does not fit the chunk.");
                int start = r.Position;
                r.Bytes((int)length);
                records.Add(new RecordEntry(kind, track, time, payload.Slice(start, (int)length), i < h.SnapshotCount));
            }
        }
        catch (BodyException e)
        {
            complete = false;
            problem = e.Message;
        }
        return records;
    }

    /// <summary>One record of a chunk being built.</summary>
    internal readonly record struct Pending(uint Kind, uint Track, long Time, int BodyStart, int BodyLength);

    /// <summary>Encodes the payload of a chunk: the snapshot records, then the others, deltas from the previous record of the run.</summary>
    public static void EncodePayload(ByteWriter w, long tmin, List<Pending> snapshot, List<Pending> records, byte[] bodies)
    {
        Run(w, tmin, snapshot, bodies);
        Run(w, tmin, records, bodies);
    }

    static void Run(ByteWriter w, long time, List<Pending> run, byte[] bodies)
    {
        foreach (var p in run)
        {
            w.VarInt(p.Kind);
            w.VarInt(p.Track);
            w.ZigZag(p.Time - time);
            w.VarInt((ulong)p.BodyLength);
            w.Bytes(bodies.AsSpan(p.BodyStart, p.BodyLength));
            time = p.Time;
        }
    }

    public static byte[] Deflate(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw);
        return output.ToArray();
    }
}
