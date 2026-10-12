using static JoinFS.Recording.Format.ChunkCodec;

namespace JoinFS.Recording.Format;

public sealed class RecordingWriterOptions
{
    /// <summary>Deflate is used per chunk only when it makes the chunk smaller.</summary>
    public ChunkCompression Compression { get; init; } = ChunkCompression.None;
}

/// <summary>
/// Writes a JFS2 file (spec 9). The file is append-only and valid after every completed chunk. Not thread-safe.
/// The caller decides when a chunk ends (<see cref="EndChunk"/>) and when it is a keyframe
/// (<see cref="StartKeyframeChunk"/> then <see cref="AddSnapshot"/>), because the snapshot needs
/// knowledge of the records that this library does not have.
/// </summary>
/// <remarks>
/// <see cref="Dispose"/> without <see cref="Finish"/> abandons the file as an unfinished one, which
/// <see cref="RecordingRepair"/> can complete; records of the chunk still pending are lost.
/// </remarks>
public sealed class RecordingWriter : IDisposable
{
    sealed class WriterTrack
    {
        public long First, Last;
        public bool Open;
        public string Callsign = "", Model = "";
    }

    // Largest estimate of a payload we accept, so the chunk block stays within MaxBlockLength.
    const int MaxPayload = Jfs2.MaxBlockLength - Jfs2.ChunkHeaderLength;
    const int MaxRecordHeader = 25;

    readonly Stream _stream;
    readonly bool _leaveOpen;
    readonly ChunkCompression _compression;
    readonly ByteWriter _block = new(4096);
    readonly ByteWriter _payload = new(4096);

    readonly Dictionary<uint, KindBinding> _kinds = new();
    readonly Dictionary<(string, byte), uint> _kindIds = new();
    readonly SortedDictionary<uint, WriterTrack> _tracks = new();
    readonly List<ChunkInfo> _chunks = new();
    uint _nextKind = 1;
    long _position;
    long _lastChunkTMin = long.MinValue;
    bool _finished;

    // The chunk being built.
    bool _active;
    bool _keyframe;
    readonly List<Pending> _snapshot = new();
    readonly List<Pending> _records = new();
    byte[] _bodies = new byte[4096];
    int _bodiesLength;
    int _estimate;
    long _pendingTMin = long.MaxValue;
    long _pendingTMax = long.MinValue;

    RecordingWriter(Stream stream, bool leaveOpen, RecordingWriterOptions? options, long position)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _compression = options?.Compression ?? ChunkCompression.None;
        _position = position;
    }

    /// <summary>Starts a file at the stream's current position, which must be its start.</summary>
    public static RecordingWriter Create(Stream stream, RecordingHeader header, RecordingWriterOptions? options = null,
        bool leaveOpen = false)
    {
        var w = new RecordingWriter(stream, leaveOpen, options, 0);
        w._block.Bytes(Jfs2.Magic);
        BlockCodec.WriteHeader(w._block, header);
        w.WriteBlock();
        return w;
    }

    /// <summary>Creates the file, readable by others while it is written.</summary>
    public static RecordingWriter Create(string path, RecordingHeader header, RecordingWriterOptions? options = null)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        try
        {
            return Create(stream, header, options, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Continues an unfinished file after its last valid block (repair). The stream must be readable, writable and seekable.</summary>
    internal static RecordingWriter Resume(Stream stream, RecordingModel model, RecordingWriterOptions? options = null)
    {
        stream.SetLength(model.ValidEnd);
        stream.Position = model.ValidEnd;
        var w = new RecordingWriter(stream, true, options, model.ValidEnd);
        foreach (var k in model.Kinds.Values)
        {
            w._kinds[k.Id] = k;
            w._kindIds[(k.Name, k.Version)] = k.Id;
            w._nextKind = Math.Max(w._nextKind, k.Id + 1);
        }
        w._chunks.AddRange(model.Chunks);
        foreach (var c in model.Chunks)
            w._lastChunkTMin = Math.Max(w._lastChunkTMin, c.TMin);
        foreach (var t in model.Tracks)
            w._tracks[t.Id] = new WriterTrack { First = t.FirstTime, Last = t.LastTime, Open = t.IsOpen, Callsign = t.Callsign, Model = t.Model };
        return w;
    }

    /// <summary>Bytes written so far; the file length.</summary>
    public long Position => _position;
    public int ChunkCount => _chunks.Count;
    public int PendingRecords => _records.Count;
    /// <summary>An estimate of the size of the chunk being built, for the caller's chunking policy.</summary>
    public int PendingBytes => _estimate;
    /// <summary>The smallest record time of the chunk being built, or null if it has none.</summary>
    public long? PendingTMin => _records.Count == 0 ? null : _pendingTMin;
    public long? PendingTMax => _records.Count == 0 ? null : _pendingTMax;

    /// <summary>
    /// Binds a (name, version) to a kind id and writes the Schema block if it is new. Call it before the first record
    /// of the kind; calling it again returns the same id (spec 3.2).
    /// </summary>
    public uint BindKind(string name, byte version)
    {
        CheckUsable();
        if (name.Length == 0 || version == 0)
            throw new ArgumentException("A kind needs a name and a version of at least 1.");
        if (_kindIds.TryGetValue((name, version), out uint id))
            return id;
        var k = new KindBinding(_nextKind, name, version);
        BlockCodec.WriteSchema(_block, k);
        WriteBlock();
        _nextKind++;
        _kinds[k.Id] = k;
        _kindIds[(name, version)] = k.Id;
        return k.Id;
    }

    /// <summary>
    /// Adds a record to the current chunk, starting one if there is none. The track lifecycle is enforced (spec 5):
    /// the first record of a track is its <c>track-open</c>, nothing follows <c>track-close</c>, and times of a track do not decrease.
    /// A record may not be earlier than the TMin of the chunk before.
    /// </summary>
    public void Add(uint kind, uint track, long time, ReadOnlySpan<byte> body)
    {
        CheckUsable();
        string name = NameOf(kind);
        if (time < _lastChunkTMin)
            throw new ArgumentOutOfRangeException(nameof(time), "A record may not be earlier than the TMin of the previous chunk.");
        CheckRoom(body.Length);

        WriterTrack? t = null;
        bool opens = name == Jfs2.TrackOpenKind;
        if (track == 0)
        {
            if (opens || name == Jfs2.TrackCloseKind)
                throw new ArgumentException("track-open and track-close need a track.");
        }
        else if (opens)
        {
            if (_tracks.ContainsKey(track))
                throw new InvalidOperationException($"Track {track} was already opened; track ids are never reused.");
        }
        else
        {
            if (!_tracks.TryGetValue(track, out t) || !t.Open)
                throw new InvalidOperationException($"Track {track} is not open.");
            if (time < t.Last)
                throw new ArgumentOutOfRangeException(nameof(time), $"Times of track {track} must not decrease.");
        }

        EnsureChunk();
        _records.Add(Stash(kind, track, time, body));
        if (time < _pendingTMin) _pendingTMin = time;
        if (time > _pendingTMax) _pendingTMax = time;

        if (opens)
            _tracks[track] = new WriterTrack { First = time, Last = time, Open = true };
        else if (t != null)
        {
            t.Last = time;
            if (name == Jfs2.TrackCloseKind)
                t.Open = false;
        }
    }

    /// <summary>
    /// Ends the current chunk, if any, and starts a keyframe chunk. Call <see cref="AddSnapshot"/> for every open track,
    /// then <see cref="Add"/> (spec 6.1). The first chunk of a file is a keyframe without needing this call.
    /// </summary>
    public void StartKeyframeChunk()
    {
        CheckUsable();
        EndChunk();
        _active = true;
        _keyframe = true;
    }

    /// <summary>Adds a snapshot record to the keyframe chunk just started. It keeps the time of what it summarises (spec 6.2).</summary>
    public void AddSnapshot(uint kind, uint track, long time, ReadOnlySpan<byte> body)
    {
        CheckUsable();
        if (!_active || !_keyframe || _records.Count > 0)
            throw new InvalidOperationException("Snapshot records go first in a chunk begun with StartKeyframeChunk.");
        NameOf(kind);
        if (!_tracks.TryGetValue(track, out var t) || !t.Open)
            throw new InvalidOperationException($"Track {track} is not open, so it has no snapshot.");
        CheckRoom(body.Length);
        _snapshot.Add(Stash(kind, track, time, body));
    }

    /// <summary>
    /// Writes the pending chunk as one block and hands it to the operating system. A chunk without
    /// records is dropped (a snapshot alone says nothing).
    /// </summary>
    public void EndChunk()
    {
        if (!_active)
            return;
        if (_records.Count == 0)
        {
            ResetChunk();
            return;
        }

        long tmin = _pendingTMin, tmax = _pendingTMax;
        _payload.Clear();
        EncodePayload(_payload, tmin, _snapshot, _records, _bodies);
        var raw = _payload.Span;
        var stored = raw;
        byte compression = (byte)ChunkCompression.None;
        if (_compression == ChunkCompression.Deflate)
        {
            var deflated = Deflate(raw);
            if (deflated.Length < raw.Length)
            {
                stored = deflated;
                compression = (byte)ChunkCompression.Deflate;
            }
        }

        long offset = _position;
        int start = _block.BeginBlock(BlockType.Chunk);
        _block.I64(tmin);
        _block.I64(tmax);
        _block.U8((byte)(_keyframe ? 1 : 0));
        _block.U8(compression);
        _block.U32((uint)_snapshot.Count);
        _block.U32((uint)_records.Count);
        _block.U32((uint)raw.Length);
        _block.Bytes(stored);
        _block.EndBlock(start);
        WriteBlock();
        _stream.Flush();

        _chunks.Add(new ChunkInfo(offset, tmin, tmax, _keyframe));
        _lastChunkTMin = tmin;
        ResetChunk();
    }

    /// <summary>Writes an attachment block now (spec 3.5). A later one of the same name replaces an earlier one.</summary>
    public void AddAttachment(string name, string mediaType, ReadOnlySpan<byte> bytes)
    {
        CheckUsable();
        BlockCodec.WriteAttach(_block, name, mediaType, bytes);
        WriteBlock();
    }

    /// <summary>The callsign and model the Index lists for a track, for display and search only (spec 3.4).</summary>
    public void SetTrackLabel(uint track, string callsign, string model)
    {
        CheckUsable();
        if (!_tracks.TryGetValue(track, out var t))
            throw new InvalidOperationException($"Track {track} does not exist.");
        t.Callsign = callsign;
        t.Model = model;
    }

    /// <summary>Flushes to the operating system and, for a file, asks it to reach the disk (spec 9.1).</summary>
    public void Sync()
    {
        _stream.Flush();
        if (_stream is FileStream fs)
            fs.Flush(true);
    }

    /// <summary>
    /// Ends the pending chunk, closes every track still open at its last time, and writes the Index and Footer (spec 9.1).
    /// </summary>
    public void Finish()
    {
        if (_finished)
            return;
        CheckUsable();
        EndChunk();

        var open = _tracks.Where(kv => kv.Value.Open).ToList();
        if (open.Count > 0)
        {
            uint close = _kinds.Values.FirstOrDefault(k => k.Name == Jfs2.TrackCloseKind)?.Id
                ?? BindKind(Jfs2.TrackCloseKind, 1);
            // A track may lag the previous chunk's TMin by the cross-track skew; the record keeps the later time.
            foreach (var kv in open)
                Add(close, kv.Key, Math.Max(kv.Value.Last, _lastChunkTMin), ReadOnlySpan<byte>.Empty);
            EndChunk();
        }

        long indexAt = _position;
        BlockCodec.WriteIndex(_block, _chunks,
            _tracks.Select(kv => new TrackInfo(kv.Key, kv.Value.First, kv.Value.Last, kv.Value.Callsign, kv.Value.Model)).ToList());
        _block.U64((ulong)indexAt);
        _block.U32(Crc32.Compute(_block.Span.Slice(_block.Length - 8)));
        _block.Bytes(Jfs2.EndMagic);
        WriteBlock();
        Sync();
        _finished = true;
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }

    // ---- internals ----

    void CheckUsable()
    {
        if (_finished)
            throw new InvalidOperationException("The file is finished.");
    }

    string NameOf(uint kind) =>
        _kinds.TryGetValue(kind, out var k) ? k.Name : throw new ArgumentException($"Kind {kind} is not bound; call BindKind first.");

    void CheckRoom(int bodyLength)
    {
        if ((long)_estimate + bodyLength + MaxRecordHeader > MaxPayload)
            throw new InvalidOperationException("The chunk is full; call EndChunk.");
    }

    void EnsureChunk()
    {
        if (_active)
            return;
        _active = true;
        _keyframe = _chunks.Count == 0; // the first chunk is always a keyframe (spec 3.3)
    }

    Pending Stash(uint kind, uint track, long time, ReadOnlySpan<byte> body)
    {
        if (_bodiesLength + body.Length > _bodies.Length)
            Array.Resize(ref _bodies, Math.Max(_bodies.Length * 2, _bodiesLength + body.Length));
        body.CopyTo(_bodies.AsSpan(_bodiesLength));
        var p = new Pending(kind, track, time, _bodiesLength, body.Length);
        _bodiesLength += body.Length;
        _estimate += body.Length + MaxRecordHeader;
        return p;
    }

    void ResetChunk()
    {
        _active = false;
        _keyframe = false;
        _snapshot.Clear();
        _records.Clear();
        _bodiesLength = 0;
        _estimate = 0;
        _pendingTMin = long.MaxValue;
        _pendingTMax = long.MinValue;
    }

    /// <summary>One write per block, so a crash leaves a whole block or a truncated one (spec 9.1).</summary>
    void WriteBlock()
    {
        _stream.Write(_block.Buffer, 0, _block.Length);
        _position += _block.Length;
        _block.Clear();
    }
}
