namespace JoinFS.Recording.Format;

/// <summary>
/// What a file holds, found by opening it (spec 8.1) or scanning it (spec 8.3). Shared by the reader and by
/// repair, which resumes a writer from it.
/// </summary>
internal sealed class RecordingModel
{
    public RecordingHeader Header { get; private set; } = null!;
    public long HeaderEnd { get; private set; }
    public bool IsFinished { get; private set; }
    /// <summary>End of the last valid block that is not an Index. Repair truncates here.</summary>
    public long ValidEnd { get; private set; }
    public Dictionary<uint, KindBinding> Kinds { get; } = new();
    public List<ChunkInfo> Chunks { get; private set; } = new();
    public List<TrackInfo> Tracks { get; private set; } = new();
    public List<Attachment> Attachments { get; } = new();
    public List<Jfs2Damage> Damage { get; } = new();

    sealed class ScanTrack
    {
        public long First, Last;
        public bool Open;
    }

    public static RecordingModel Load(Stream s)
    {
        long length = s.Length;
        Span<byte> magic = stackalloc byte[Jfs2.MagicLength];
        if (length < Jfs2.MagicLength || !BlockCodec.TryReadExactly(s, 0, magic) || !magic.SequenceEqual(Jfs2.Magic))
            throw new Jfs2FormatException(Jfs2Failure.NotJfs2, "Not a JFS2 file.");

        var status = BlockCodec.Read(s, Jfs2.MagicLength, length, out var block);
        if (status != BlockStatus.Ok || block!.Type != (byte)BlockType.Header)
            throw new Jfs2FormatException(Jfs2Failure.HeaderDamaged, "The header block is missing or damaged.");
        RecordingHeader header;
        try
        {
            header = BlockCodec.ParseHeader(block.Body);
        }
        catch (BodyException e)
        {
            throw new Jfs2FormatException(Jfs2Failure.HeaderDamaged, "The header block is damaged: " + e.Message);
        }
        if (header.FormatMajor != Jfs2.FormatMajor)
            throw new Jfs2FormatException(Jfs2Failure.UnsupportedMajor, $"Format major version {header.FormatMajor} is not supported.");
        if ((header.RequiredFeatures & ~Jfs2.KnownRequiredFeatures) != 0)
            throw new Jfs2FormatException(Jfs2Failure.UnsupportedFeatures, "The file requires features this build does not have.");

        var m = new RecordingModel { Header = header, HeaderEnd = Jfs2.MagicLength + block.Size };
        m.ValidEnd = m.HeaderEnd;
        if (!m.TryLoadFinished(s, length))
            m.Scan(s, length);
        return m;
    }

    // ---- Finished file: footer, Index, and a walk over block heads for the Schema blocks ----

    bool TryLoadFinished(Stream s, long length)
    {
        long footerAt = length - Jfs2.FooterLength;
        if (footerAt < HeaderEnd)
            return false;
        Span<byte> footer = stackalloc byte[Jfs2.FooterLength];
        if (!BlockCodec.TryReadExactly(s, footerAt, footer) || !footer.Slice(12).SequenceEqual(Jfs2.EndMagic))
            return false;
        if (Crc32.Compute(footer.Slice(0, 8)) != System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(8)))
            return false;
        long indexAt = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(footer);
        if (indexAt < HeaderEnd || indexAt >= footerAt)
            return false;

        if (BlockCodec.Read(s, indexAt, footerAt, out var indexBlock) != BlockStatus.Ok
            || indexBlock!.Type != (byte)BlockType.Index || indexAt + indexBlock.Size != footerAt)
            return false;
        List<ChunkInfo> indexChunks;
        List<TrackInfo> indexTracks;
        try
        {
            (indexChunks, indexTracks) = BlockCodec.ParseIndex(indexBlock.Body);
        }
        catch (BodyException)
        {
            return false;
        }

        // The Index does not list Schema blocks, so find them by walking block heads up to the Index.
        // Chunk bodies are not read here; each chunk is checked when it is read.
        var kinds = new Dictionary<uint, KindBinding>();
        var chunks = new List<ChunkInfo>();
        var attachments = new List<Attachment>();
        long offset = HeaderEnd;
        Span<byte> fixedPart = stackalloc byte[Jfs2.ChunkHeaderLength];
        while (offset < indexAt)
        {
            if (BlockCodec.PeekHead(s, offset, indexAt, out byte type, out uint blockLength) != BlockStatus.Ok)
                return false;
            switch (type)
            {
                case (byte)BlockType.Header:
                case (byte)BlockType.Index:
                    return false;
                case (byte)BlockType.Schema:
                    {
                        if (BlockCodec.Read(s, offset, indexAt, out var b) != BlockStatus.Ok)
                            return false;
                        if (!TryBind(kinds, b!.Body, offset, null))
                            return false;
                        break;
                    }
                case (byte)BlockType.Chunk:
                    {
                        if (blockLength < Jfs2.ChunkHeaderLength || !BlockCodec.TryReadExactly(s, offset + 5, fixedPart))
                            return false;
                        var h = BlockCodec.ParseChunkHeader(fixedPart);
                        chunks.Add(new ChunkInfo(offset, h.TMin, h.TMax, h.IsKeyframe));
                        break;
                    }
                case (byte)BlockType.Attach:
                    {
                        if (BlockCodec.Read(s, offset, indexAt, out var b) != BlockStatus.Ok)
                            return false;
                        try
                        {
                            attachments.Add(BlockCodec.ParseAttach(b!.Body));
                        }
                        catch (BodyException)
                        {
                            return false;
                        }
                        break;
                    }
            }
            offset += Jfs2.BlockOverhead + blockLength;
        }
        if (offset != indexAt)
            return false;

        foreach (var kv in kinds)
            Kinds[kv.Key] = kv.Value;
        Attachments.AddRange(attachments);
        Chunks = chunks;
        ValidEnd = indexAt;
        IsFinished = true;
        if (chunks.SequenceEqual(indexChunks))
        {
            Tracks = indexTracks;
        }
        else
        {
            Damage.Add(new(DamageKind.IndexMismatch, indexAt, "The Index disagrees with the chunks; the chunks are used."));
            Tracks = new List<TrackInfo>();
            RebuildTracks(s);
        }
        return true;
    }

    /// <summary>Decodes every chunk to find the tracks, as a scan does. Used when the Index cannot be trusted.</summary>
    void RebuildTracks(Stream s)
    {
        var tracks = new Dictionary<uint, ScanTrack>();
        var ignored = new List<Jfs2Damage>();
        for (int i = 0; i < Chunks.Count; i++)
        {
            if (BlockCodec.Read(s, Chunks[i].Offset, s.Length, out var b) != BlockStatus.Ok)
                continue;
            var chunk = ChunkCodec.Decode(b!, Chunks[i].Offset, i, Kinds.ContainsKey, ignored);
            if (chunk != null)
                Accumulate(tracks, chunk);
        }
        Tracks = ToTrackInfos(tracks);
    }

    // ---- Unfinished file or untrusted Index: read every block ----

    void Scan(Stream s, long length)
    {
        // A valid footer is not a block; stop in front of it so it is not reported as damage.
        long limit = length;
        Span<byte> endMagic = stackalloc byte[8];
        if (length - Jfs2.FooterLength >= HeaderEnd && BlockCodec.TryReadExactly(s, length - 8, endMagic)
            && endMagic.SequenceEqual(Jfs2.EndMagic))
            limit = length - Jfs2.FooterLength;

        var tracks = new Dictionary<uint, ScanTrack>();
        bool gap = false;
        long offset = HeaderEnd;
        while (offset < limit)
        {
            var status = BlockCodec.Read(s, offset, limit, out var block);
            if (status == BlockStatus.Truncated)
                break; // the normal end of a file whose writer died (spec 8.3)
            if (status == BlockStatus.TooLong)
            {
                Damage.Add(new(DamageKind.BlockLength, offset, "A block length is implausible; reading stopped."));
                break;
            }
            if (status == BlockStatus.CrcFail)
            {
                Damage.Add(new(DamageKind.BlockCrc, offset, "A block failed its CRC and was skipped."));
                gap = true;
                offset += block!.Size;
                continue;
            }

            switch (block!.Type)
            {
                case (byte)BlockType.Header:
                    Damage.Add(new(DamageKind.UnexpectedBlock, offset, "A second Header block was ignored."));
                    break;
                case (byte)BlockType.Schema:
                    TryBind(Kinds, block.Body, offset, Damage);
                    break;
                case (byte)BlockType.Chunk:
                    {
                        var chunk = ChunkCodec.Decode(block, offset, Chunks.Count, Kinds.ContainsKey, Damage, gap);
                        if (chunk == null)
                        {
                            gap = true;
                            break;
                        }
                        gap = false;
                        Chunks.Add(chunk.Info);
                        Accumulate(tracks, chunk);
                        break;
                    }
                case (byte)BlockType.Attach:
                    try
                    {
                        Attachments.Add(BlockCodec.ParseAttach(block.Body));
                    }
                    catch (BodyException)
                    {
                        Damage.Add(new(DamageKind.UnexpectedBlock, offset, "An Attach block did not parse and was ignored."));
                    }
                    break;
            }
            offset += block.Size;
            // An Index here belongs to a finish that did not complete; repair writes a new one.
            if (block.Type != (byte)BlockType.Index)
                ValidEnd = offset;
        }
        Tracks = ToTrackInfos(tracks);
    }

    static bool TryBind(Dictionary<uint, KindBinding> kinds, ReadOnlySpan<byte> body, long offset, List<Jfs2Damage>? damage)
    {
        KindBinding k;
        try
        {
            k = BlockCodec.ParseSchema(body);
        }
        catch (BodyException)
        {
            damage?.Add(new(DamageKind.UnexpectedBlock, offset, "A Schema block did not parse and was ignored."));
            return damage != null;
        }
        if (k.Id == 0 || (kinds.TryGetValue(k.Id, out var old) && old != k))
        {
            damage?.Add(new(DamageKind.UnexpectedBlock, offset, $"Schema binds kind id {k.Id} again or to 0; ignored."));
            return damage != null;
        }
        kinds[k.Id] = k;
        return true;
    }

    void Accumulate(Dictionary<uint, ScanTrack> tracks, RecordChunk chunk)
    {
        foreach (var rec in chunk.Records)
        {
            if (rec.IsSnapshot || rec.Track == 0)
                continue;
            string name = Kinds[rec.Kind].Name;
            if (name == Jfs2.TrackOpenKind)
            {
                tracks.TryAdd(rec.Track, new ScanTrack { First = rec.Time, Last = rec.Time, Open = true });
                continue;
            }
            if (!tracks.TryGetValue(rec.Track, out var t))
                continue;
            if (rec.Time > t.Last)
                t.Last = rec.Time;
            if (name == Jfs2.TrackCloseKind)
                t.Open = false;
        }
    }

    static List<TrackInfo> ToTrackInfos(Dictionary<uint, ScanTrack> tracks) =>
        tracks.OrderBy(kv => kv.Key)
            .Select(kv => new TrackInfo(kv.Key, kv.Value.First, kv.Value.Last, "", "") { IsOpen = kv.Value.Open })
            .ToList();
}
