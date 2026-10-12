using System.Buffers.Binary;
using System.Text;

namespace JoinFS.Recording.Format;

/// <summary>A block body or record header did not parse. Internal: callers turn it into damage or a refusal.</summary>
internal sealed class BodyException : Exception
{
    public BodyException(string message) : base(message) { }
}

/// <summary>Reads the format's primitives from a span. Every failure is a <see cref="BodyException"/>.</summary>
internal ref struct ByteReader
{
    static readonly UTF8Encoding Lenient = new(false, false);

    readonly ReadOnlySpan<byte> _s;
    int _p;

    public ByteReader(ReadOnlySpan<byte> s)
    {
        _s = s;
        _p = 0;
    }

    public int Position => _p;
    public int Remaining => _s.Length - _p;

    public ReadOnlySpan<byte> Bytes(int count)
    {
        if (count < 0 || count > _s.Length - _p)
            throw new BodyException("Truncated.");
        var r = _s.Slice(_p, count);
        _p += count;
        return r;
    }

    public byte U8() => Bytes(1)[0];
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
    public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Bytes(8));

    /// <summary>Unsigned LEB128. Rejects more than 10 bytes and 64-bit overflow (spec 1.1).</summary>
    public ulong VarInt()
    {
        ulong result = 0;
        for (int i = 0; i < 10; i++)
        {
            byte b = U8();
            if (i == 9 && b > 1)
                throw new BodyException("Varint is longer than 10 bytes or overflows 64 bits.");
            result |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
                return result;
        }
        throw new BodyException("Varint is longer than 10 bytes.");
    }

    public long ZigZag()
    {
        ulong v = VarInt();
        return (long)(v >> 1) ^ -(long)(v & 1);
    }

    /// <summary>A varint that must fit a <see cref="uint"/> (kind ids, track ids).</summary>
    public uint VarInt32()
    {
        ulong v = VarInt();
        if (v > uint.MaxValue)
            throw new BodyException("Id does not fit 32 bits.");
        return (uint)v;
    }

    public string String()
    {
        ulong count = VarInt();
        if (count > Jfs2.MaxString)
            throw new BodyException("String is longer than 65,535 bytes.");
        return Lenient.GetString(Bytes((int)count));
    }
}
