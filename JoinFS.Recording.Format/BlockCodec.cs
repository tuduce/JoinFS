namespace JoinFS.Recording.Format;

/// <summary>The fixed part of a Chunk block body (spec 3.3).</summary>
internal readonly record struct ChunkHeader(
    long TMin, long TMax, byte Flags, byte Compression, uint SnapshotCount, uint RecordCount, uint UncompressedLength)
{
    public bool IsKeyframe => (Flags & 1) != 0;
}

internal enum BlockStatus { Ok, CrcFail, Truncated, TooLong }

/// <summary>A block read whole: type, length, body and CRC contiguous in <see cref="Data"/>.</summary>
internal sealed class Block
{
    public Block(byte type, byte[] data)
    {
        Type = type;
        Data = data;
    }

    public byte Type { get; }
    public byte[] Data { get; }
    public int BodyLength => Data.Length - Jfs2.BlockOverhead;
    public int Size => Data.Length;
    public ReadOnlySpan<byte> Body => Data.AsSpan(5, BodyLength);
    public ReadOnlyMemory<byte> BodyMemory => Data.AsMemory(5, BodyLength);
}

/// <summary>Reading blocks from a stream and encoding or parsing their bodies (spec 3).</summary>
internal static class BlockCodec
{
    public static bool TryReadExactly(Stream s, long offset, Span<byte> buffer)
    {
        s.Position = offset;
        int done = 0;
        while (done < buffer.Length)
        {
            int n = s.Read(buffer.Slice(done));
            if (n <= 0)
                return false;
            done += n;
        }
        return true;
    }

    /// <summary>Reads the 5 bytes in front of a block body and checks the length against the limit.</summary>
    public static BlockStatus PeekHead(Stream s, long offset, long limit, out byte type, out uint length)
    {
        type = 0;
        length = 0;
        Span<byte> h = stackalloc byte[5];
        if (offset + Jfs2.BlockOverhead > limit || !TryReadExactly(s, offset, h))
            return BlockStatus.Truncated;
        type = h[0];
        length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(1));
        if (length > Jfs2.MaxBlockLength)
            return BlockStatus.TooLong;
        if (offset + Jfs2.BlockOverhead + length > limit)
            return BlockStatus.Truncated;
        return BlockStatus.Ok;
    }

    /// <summary>Reads and CRC-checks the block at <paramref name="offset"/>. <paramref name="block"/> is set for Ok and CrcFail.</summary>
    public static BlockStatus Read(Stream s, long offset, long limit, out Block? block)
    {
        block = null;
        var status = PeekHead(s, offset, limit, out byte type, out uint length);
        if (status != BlockStatus.Ok)
            return status;
        var data = new byte[Jfs2.BlockOverhead + (int)length];
        if (!TryReadExactly(s, offset, data))
            return BlockStatus.Truncated;
        block = new Block(type, data);
        uint stored = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
        return Crc32.Compute(data.AsSpan(0, data.Length - 4)) == stored ? BlockStatus.Ok : BlockStatus.CrcFail;
    }

    // ---- Header ----

    public static void WriteHeader(ByteWriter w, RecordingHeader h)
    {
        var seen = new HashSet<string>();
        foreach (var kv in h.Metadata)
        {
            if (kv.Key.Length == 0 || !kv.Key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.'))
                throw new ArgumentException($"Metadata key '{kv.Key}' must be lower-case ASCII.");
            if (!seen.Add(kv.Key))
                throw new ArgumentException($"Metadata key '{kv.Key}' occurs twice.");
        }
        int start = w.BeginBlock(BlockType.Header);
        w.U8(Jfs2.FormatMajor);
        w.U8(Jfs2.FormatMinor);
        w.U32(0);
        w.U32(0);
        w.I64(h.StartUtcMicroseconds);
        w.VarInt((ulong)h.Metadata.Count);
        foreach (var kv in h.Metadata)
        {
            w.String(kv.Key);
            w.String(kv.Value);
        }
        w.EndBlock(start);
    }

    public static RecordingHeader ParseHeader(ReadOnlySpan<byte> body)
    {
        var r = new ByteReader(body);
        byte major = r.U8();
        byte minor = r.U8();
        uint required = r.U32();
        uint optional = r.U32();
        long start = r.I64();
        ulong count = r.VarInt();
        // Each entry is at least two bytes, so a larger count overruns the body (spec 3.1).
        if (count > (ulong)r.Remaining / 2)
            throw new BodyException("Metadata runs past the end of the header.");
        var meta = new List<KeyValuePair<string, string>>((int)count);
        for (ulong i = 0; i < count; i++)
            meta.Add(new(r.String(), r.String()));
        return new RecordingHeader
        {
            FormatMajor = major,
            FormatMinor = minor,
            RequiredFeatures = required,
            OptionalFeatures = optional,
            StartUtcMicroseconds = start,
            Metadata = meta,
        };
    }

    // ---- Schema ----

    public static void WriteSchema(ByteWriter w, KindBinding k)
    {
        int start = w.BeginBlock(BlockType.Schema);
        w.VarInt(k.Id);
        w.String(k.Name);
        w.U8(k.Version);
        w.EndBlock(start);
    }

    public static KindBinding ParseSchema(ReadOnlySpan<byte> body)
    {
        var r = new ByteReader(body);
        uint id = r.VarInt32();
        string name = r.String();
        byte version = r.U8();
        return new KindBinding(id, name, version);
    }

    // ---- Chunk ----

    public static ChunkHeader ParseChunkHeader(ReadOnlySpan<byte> body)
    {
        var r = new ByteReader(body);
        long tmin = r.I64();
        long tmax = r.I64();
        byte flags = r.U8();
        byte compression = r.U8();
        uint snapshot = r.U32();
        uint records = r.U32();
        uint ulen = r.U32();
        return new ChunkHeader(tmin, tmax, flags, compression, snapshot, records, ulen);
    }

    // ---- Index ----

    public static void WriteIndex(ByteWriter w, IReadOnlyList<ChunkInfo> chunks, IReadOnlyList<TrackInfo> tracks)
    {
        int start = w.BeginBlock(BlockType.Index);
        w.VarInt((ulong)chunks.Count);
        foreach (var c in chunks)
        {
            w.U64((ulong)c.Offset);
            w.I64(c.TMin);
            w.I64(c.TMax);
            w.U8((byte)(c.IsKeyframe ? 1 : 0));
        }
        w.VarInt((ulong)tracks.Count);
        foreach (var t in tracks)
        {
            w.VarInt(t.Id);
            w.I64(t.FirstTime);
            w.I64(t.LastTime);
            w.String(t.Callsign);
            w.String(t.Model);
        }
        w.EndBlock(start);
    }

    public static (List<ChunkInfo> Chunks, List<TrackInfo> Tracks) ParseIndex(ReadOnlySpan<byte> body)
    {
        var r = new ByteReader(body);
        ulong chunkCount = r.VarInt();
        if (chunkCount > (ulong)r.Remaining / 25)
            throw new BodyException("Index chunk list runs past the end.");
        var chunks = new List<ChunkInfo>((int)chunkCount);
        for (ulong i = 0; i < chunkCount; i++)
        {
            long offset = (long)r.U64();
            long tmin = r.I64();
            long tmax = r.I64();
            byte flags = r.U8();
            chunks.Add(new ChunkInfo(offset, tmin, tmax, (flags & 1) != 0));
        }
        ulong trackCount = r.VarInt();
        if (trackCount > (ulong)r.Remaining / 19)
            throw new BodyException("Index track list runs past the end.");
        var tracks = new List<TrackInfo>((int)trackCount);
        for (ulong i = 0; i < trackCount; i++)
        {
            uint id = r.VarInt32();
            long first = r.I64();
            long last = r.I64();
            string callsign = r.String();
            string model = r.String();
            tracks.Add(new TrackInfo(id, first, last, callsign, model));
        }
        return (chunks, tracks);
    }

    // ---- Attach ----

    public static void WriteAttach(ByteWriter w, string name, string mediaType, ReadOnlySpan<byte> bytes)
    {
        int start = w.BeginBlock(BlockType.Attach);
        w.String(name);
        w.String(mediaType);
        w.Bytes(bytes);
        w.EndBlock(start);
    }

    public static Attachment ParseAttach(ReadOnlySpan<byte> body)
    {
        var r = new ByteReader(body);
        string name = r.String();
        string mediaType = r.String();
        return new Attachment(name, mediaType, r.Bytes(r.Remaining).ToArray());
    }
}
