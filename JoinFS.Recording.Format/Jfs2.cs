namespace JoinFS.Recording.Format;

/// <summary>Constants of the JFS2 format, edition 1 (spec Appendix B).</summary>
public static class Jfs2
{
    public const byte FormatMajor = 1;
    public const byte FormatMinor = 0;

    /// <summary>Feature bits this build understands. None are defined in edition 1.</summary>
    public const uint KnownRequiredFeatures = 0;

    public static ReadOnlySpan<byte> Magic => new byte[] { 0xFA, 0x00, 0x4A, 0x46, 0x53, 0x32, 0x0D, 0x0A };
    public static ReadOnlySpan<byte> EndMagic => "JFS2END\n"u8;

    /// <summary>
    /// The first two bytes of the magic (FA 00) read as a little-endian int16, which is how a reader of the legacy <c>.jfs</c> format sees
    /// its version: below the oldest it reads, so old builds refuse the file, and new ones tell the two formats apart by it (spec 2).
    /// </summary>
    public const short LegacyVersionSignature = 250;

    public const int MagicLength = 8;
    public const int FooterLength = 20;
    /// <summary>Type, length and CRC around a block body.</summary>
    public const int BlockOverhead = 9;
    public const int ChunkHeaderLength = 30;
    public const int MaxBlockLength = 64 * 1024 * 1024;
    public const int MaxUncompressedChunk = 64 * 1024 * 1024;
    public const int MaxString = 65535;

    public const string Extension = ".jfs2";

    /// <summary>Kind names the format itself knows, because the track lifecycle (spec 5) and repair depend on them.</summary>
    public const string TrackOpenKind = "track-open";
    public const string TrackCloseKind = "track-close";
}

public enum BlockType : byte
{
    Header = 1,
    Schema = 2,
    Chunk = 3,
    Index = 4,
    Attach = 5,
}

public enum ChunkCompression : byte
{
    None = 0,
    Deflate = 1,
}
