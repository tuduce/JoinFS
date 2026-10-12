namespace JoinFS.Recording.Format.Tests;

public class PrimitivesTests
{
    [Fact]
    public void Crc32_check_value()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
        Assert.Equal(0u, Crc32.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData(0UL, "00")]
    [InlineData(127UL, "7F")]
    [InlineData(128UL, "8001")]
    [InlineData(300UL, "AC02")]
    [InlineData(ulong.MaxValue, "FFFFFFFFFFFFFFFFFF01")]
    public void VarInt_round_trip_and_shortest_form(ulong value, string hex)
    {
        var w = new ByteWriter();
        w.VarInt(value);
        Assert.Equal(hex, Convert.ToHexString(w.Span));
        var r = new ByteReader(w.Span);
        Assert.Equal(value, r.VarInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void VarInt_rejects_eleven_bytes_and_overflow()
    {
        Assert.Throws<BodyException>(() => new ByteReader(Convert.FromHexString("FFFFFFFFFFFFFFFFFFFF01")).VarInt());
        Assert.Throws<BodyException>(() => new ByteReader(Convert.FromHexString("FFFFFFFFFFFFFFFFFF02")).VarInt());
        Assert.Throws<BodyException>(() => new ByteReader(Convert.FromHexString("80")).VarInt());
    }

    [Theory]
    [InlineData(0L, 0UL)]
    [InlineData(-1L, 1UL)]
    [InlineData(1L, 2UL)]
    [InlineData(-2L, 3UL)]
    [InlineData(long.MinValue, ulong.MaxValue)]
    [InlineData(long.MaxValue, ulong.MaxValue - 1)]
    public void ZigZag_matches_the_spec(long value, ulong encoded)
    {
        var w = new ByteWriter();
        w.ZigZag(value);
        var r = new ByteReader(w.Span);
        Assert.Equal(encoded, new ByteReader(w.Span).VarInt());
        Assert.Equal(value, r.ZigZag());
    }

    [Fact]
    public void String_round_trip_and_limits()
    {
        var w = new ByteWriter();
        w.String("Grüße €");
        Assert.Equal("Grüße €", new ByteReader(w.Span).String());
        Assert.Throws<ArgumentException>(() => w.String(new string('x', 65536)));
        Assert.ThrowsAny<ArgumentException>(() => w.String("\uD800")); // a writer must not produce invalid UTF-8
        // A reader accepts invalid UTF-8 without failing.
        Assert.Equal("�", new ByteReader(new byte[] { 1, 0xFF }).String());
        // ... but not a count that runs past the data.
        Assert.Throws<BodyException>(() => new ByteReader(new byte[] { 5, 0x41 }).String());
    }
}
