namespace JoinFS.Recording.Format;

/// <summary>The Header block (spec 3.1). A writer always writes edition 1.0 with no feature bits.</summary>
public sealed class RecordingHeader
{
    public byte FormatMajor { get; init; } = Jfs2.FormatMajor;
    public byte FormatMinor { get; init; } = Jfs2.FormatMinor;
    public uint RequiredFeatures { get; init; }
    public uint OptionalFeatures { get; init; }
    /// <summary>Microseconds since 1970-01-01T00:00:00Z when recording time 0 happened.</summary>
    public long StartUtcMicroseconds { get; init; }
    /// <summary>Keys are lower-case ASCII and unique. Defined: app, sim, title, note.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Metadata { get; init; } = Array.Empty<KeyValuePair<string, string>>();

    public string? GetMetadata(string key)
    {
        foreach (var kv in Metadata)
            if (kv.Key == key)
                return kv.Value;
        return null;
    }
}

/// <summary>A file-local kind id bound to a (name, version) pair by a Schema block (spec 3.2).</summary>
public sealed record KindBinding(uint Id, string Name, byte Version);

/// <summary>Where a chunk is and what time it covers. The same facts as an Index entry (spec 3.4).</summary>
public sealed record ChunkInfo(long Offset, long TMin, long TMax, bool IsKeyframe)
{
    /// <summary>
    /// A scan of an unfinished file skipped damaged blocks just before this chunk, so the track state at
    /// this chunk is unreliable until the next keyframe (spec 8.2). In a finished file gaps show up when
    /// <see cref="RecordingReader.TryReadChunk"/> fails instead.
    /// </summary>
    public bool PrecededByGap { get; init; }
}

/// <summary>A track as the Index lists it (spec 3.4). Callsign and Model are empty when unknown.</summary>
public sealed record TrackInfo(uint Id, long FirstTime, long LastTime, string Callsign, string Model)
{
    /// <summary>True when a scan saw no <c>track-close</c> for it. Always false for tracks read from an Index.</summary>
    public bool IsOpen { get; init; }
}

public sealed record Attachment(string Name, string MediaType, byte[] Bytes);

/// <summary>One record of a chunk: opaque body, authoritative track and time (spec 4, 5).</summary>
public readonly record struct RecordEntry(uint Kind, uint Track, long Time, ReadOnlyMemory<byte> Body, bool IsSnapshot);

/// <summary>A decoded chunk. The record bodies point into one buffer owned by this object.</summary>
public sealed class RecordChunk
{
    public RecordChunk(int index, ChunkInfo info, IReadOnlyList<RecordEntry> records, int snapshotCount, bool isComplete)
    {
        Index = index;
        Info = info;
        Records = records;
        SnapshotCount = snapshotCount;
        IsComplete = isComplete;
    }

    /// <summary>Position in <see cref="RecordingReader.Chunks"/>.</summary>
    public int Index { get; }
    public ChunkInfo Info { get; }
    /// <summary>The snapshot records first (<see cref="SnapshotCount"/> of them), then the chunk's own records, in file order.</summary>
    public IReadOnlyList<RecordEntry> Records { get; }
    public int SnapshotCount { get; }
    /// <summary>False when the records stopped parsing; <see cref="Records"/> holds those before the damage (spec 8.2).</summary>
    public bool IsComplete { get; }
}

public enum DamageKind
{
    /// <summary>A block failed its CRC and was skipped.</summary>
    BlockCrc,
    /// <summary>A block length is implausible; reading blocks stopped there.</summary>
    BlockLength,
    /// <summary>A chunk could not be decoded (unknown compression, bad data, wrong length) and was skipped.</summary>
    ChunkData,
    /// <summary>The records of a chunk stopped parsing; the rest of the chunk was skipped.</summary>
    ChunkRecords,
    /// <summary>A block that must not be there (a second Header), or a Schema that contradicts an earlier one.</summary>
    UnexpectedBlock,
    /// <summary>The Index disagrees with the chunks; the chunks win.</summary>
    IndexMismatch,
}

public sealed record Jfs2Damage(DamageKind Kind, long Offset, string Message);

public enum Jfs2Failure
{
    NotJfs2,
    HeaderDamaged,
    UnsupportedMajor,
    UnsupportedFeatures,
}

/// <summary>The file cannot be read at all (spec 8.1 steps 1 and 2, 3.1). Damage further in is reported, not thrown.</summary>
public sealed class Jfs2FormatException : Exception
{
    public Jfs2FormatException(Jfs2Failure reason, string message) : base(message) => Reason = reason;

    public Jfs2Failure Reason { get; }
}
