using System.Buffers.Binary;
using System.Text;

namespace JoinFS.Recording.Format;

/// <summary>A growable little-endian buffer with the format's primitives (spec 1.1, 1.2).</summary>
internal sealed class ByteWriter
{
    static readonly UTF8Encoding Strict = new(false, true);

    byte[] _buf;
    int _n;

    public ByteWriter(int capacity = 256) => _buf = new byte[Math.Max(capacity, 16)];

    public int Length => _n;
    public byte[] Buffer => _buf;
    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, _n);

    public void Clear() => _n = 0;

    Span<byte> Grow(int count)
    {
        if (_n + count > _buf.Length)
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _n + count));
        var s = _buf.AsSpan(_n, count);
        _n += count;
        return s;
    }

    public void U8(byte v) => Grow(1)[0] = v;
    public void U32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Grow(4), v);
    public void U64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Grow(8), v);
    public void I64(long v) => BinaryPrimitives.WriteInt64LittleEndian(Grow(8), v);
    public void Bytes(ReadOnlySpan<byte> b) => b.CopyTo(Grow(b.Length));

    /// <summary>Unsigned LEB128, shortest form.</summary>
    public void VarInt(ulong v)
    {
        while (v >= 0x80)
        {
            U8((byte)(v | 0x80));
            v >>= 7;
        }
        U8((byte)v);
    }

    public void ZigZag(long v) => VarInt((ulong)((v << 1) ^ (v >> 63)));

    public void String(string s)
    {
        int count = Strict.GetByteCount(s);
        if (count > Jfs2.MaxString)
            throw new ArgumentException($"A string of {count} bytes is longer than {Jfs2.MaxString}.");
        VarInt((ulong)count);
        Strict.GetBytes(s, Grow(count));
    }

    /// <summary>Starts a block; returns the start offset to pass to <see cref="EndBlock"/>.</summary>
    public int BeginBlock(BlockType type)
    {
        int start = _n;
        U8((byte)type);
        U32(0);
        return start;
    }

    /// <summary>Patches the length and appends the CRC over type, length and body (spec 3).</summary>
    public void EndBlock(int start)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(start + 1), (uint)(_n - start - 5));
        U32(Crc32.Compute(_buf.AsSpan(start, _n - start)));
    }
}
