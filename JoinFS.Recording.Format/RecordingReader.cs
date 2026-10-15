namespace JoinFS.Recording.Format;

/// <summary>
/// Reads a JFS2 file (spec 8). Records are opaque: kind id, track, time, bytes. Not thread-safe: one thread
/// at a time, for example a prefetch thread that owns the reader.
/// </summary>
public sealed class RecordingReader : IDisposable
{
    readonly Stream _stream;
    readonly bool _leaveOpen;
    readonly RecordingModel _model;

    RecordingReader(Stream stream, bool leaveOpen, RecordingModel model)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _model = model;
    }

    /// <summary>Opens a file for reading. It may still be being written, or be unfinished.</summary>
    /// <exception cref="Jfs2FormatException">The file is not JFS2, its header is damaged, or it needs a newer build.</exception>
    public static RecordingReader Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            return Open(stream, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The stream must be readable and seekable.</summary>
    public static RecordingReader Open(Stream stream, bool leaveOpen = true) =>
        new(stream, leaveOpen, RecordingModel.Load(stream));

    public RecordingHeader Header => _model.Header;

    /// <summary>True when the footer and Index were valid. False for a crash, a copy in progress, or a damaged Index (spec 2).</summary>
    public bool IsFinished => _model.IsFinished;

    /// <summary>Everything found wrong so far, in the order it was found; reading a chunk can add to it.</summary>
    public IReadOnlyList<Jfs2Damage> Damage => _model.Damage;

    public IReadOnlyList<ChunkInfo> Chunks => _model.Chunks;
    public IReadOnlyList<TrackInfo> Tracks => _model.Tracks;
    public IReadOnlyCollection<KindBinding> Kinds => _model.Kinds.Values;
    public IReadOnlyList<Attachment> Attachments => _model.Attachments;

    public KindBinding? GetKind(uint id) => _model.Kinds.GetValueOrDefault(id);

    /// <summary>The kind id bound to the name and version, if the file binds it.</summary>
    public uint? FindKind(string name, byte version)
    {
        foreach (var k in _model.Kinds.Values)
            if (k.Name == name && k.Version == version)
                return k.Id;
        return null;
    }

    /// <summary>The newest attachment of that name (spec 3.5).</summary>
    public Attachment? GetAttachment(string name) => _model.Attachments.LastOrDefault(a => a.Name == name);

    /// <summary>Index of the last keyframe chunk with TMin at or before the time, else of the first keyframe chunk; -1 if none (spec 6.3).</summary>
    public int FindKeyframeChunk(long time)
    {
        int found = -1;
        for (int i = 0; i < _model.Chunks.Count; i++)
        {
            var c = _model.Chunks[i];
            if (!c.IsKeyframe)
                continue;
            if (found < 0 || c.TMin <= time)
                found = i;
            else
                break;
        }
        return found;
    }

    /// <summary>
    /// Reads and decodes one chunk. False when it must be skipped (CRC, compression, length: see <see cref="Damage"/>);
    /// after that the state of the tracks is unreliable until the next keyframe chunk (spec 8.2).
    /// </summary>
    public bool TryReadChunk(int index, out RecordChunk? chunk)
    {
        chunk = null;
        var info = _model.Chunks[index];
        var status = BlockCodec.Read(_stream, info.Offset, _stream.Length, out var block);
        if (status != BlockStatus.Ok)
        {
            _model.Damage.Add(new(status == BlockStatus.CrcFail ? DamageKind.BlockCrc : DamageKind.BlockLength, info.Offset,
                "A chunk could not be read."));
            return false;
        }
        if (block!.Type != (byte)BlockType.Chunk)
        {
            _model.Damage.Add(new(DamageKind.UnexpectedBlock, info.Offset, "The Index points at a block that is not a chunk."));
            return false;
        }
        chunk = ChunkCodec.Decode(block, info.Offset, index, _model.Kinds.ContainsKey, _model.Damage, info.PrecededByGap);
        return chunk != null;
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }
}
