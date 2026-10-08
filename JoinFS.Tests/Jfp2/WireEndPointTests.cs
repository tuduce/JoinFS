using System.Net;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// WireEndPoint (docs/jfp2/protocol.md §4.4): family u8 (4 or 6), the address in network byte
    /// order, the port u16 little-endian; 7 or 19 bytes. The bytes here come from that table.
    /// </summary>
    public class WireEndPointTests
    {
        static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

        [Fact]
        public void IPv4_IsSevenBytes()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 40001);
            byte[] expected = Bytes("04 CB 00 71 09 41 9C"); // family 4, 203.0.113.9, port 40001 = 0x9C41 LE

            Assert.Equal(WireEndPoint.IPv4Size, WireEndPoint.SizeOf(endPoint));
            Assert.Equal(expected, WireEndPoint.ToBytes(endPoint));
            Assert.True(WireEndPoint.TryReadFrom(expected, out IPEndPoint back));
            Assert.Equal(endPoint, back);
        }

        [Fact]
        public void IPv6_IsNineteenBytes()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6112);
            byte[] expected = Bytes("06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17"); // family 6, 2001:db8::1, port 6112 = 0x17E0 LE

            Assert.Equal(WireEndPoint.IPv6Size, WireEndPoint.SizeOf(endPoint));
            Assert.Equal(expected, WireEndPoint.ToBytes(endPoint));
            Assert.True(WireEndPoint.TryReadFrom(expected, out IPEndPoint back));
            Assert.Equal(endPoint, back);
        }

        /// <summary>Families other than 4 and 6 are unassigned: the value is ignored, whatever follows.</summary>
        [Theory]
        [InlineData("00 CB 00 71 09 41 9C")]
        [InlineData("05 CB 00 71 09 41 9C")]
        [InlineData("FF 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17")]
        public void UnknownFamily_IsIgnored(string hex)
        {
            Assert.False(WireEndPoint.TryReadFrom(Bytes(hex), out IPEndPoint endPoint));
            Assert.Null(endPoint);
        }

        /// <summary>A value shorter than its family needs is ignored (§4.4).</summary>
        [Theory]
        [InlineData("")]
        [InlineData("04 CB 00 71 09 41")]
        [InlineData("06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0")]
        public void ShortValue_IsIgnored(string hex) => Assert.False(WireEndPoint.TryReadFrom(Bytes(hex), out _));

        /// <summary>A longer value is read up to its family's size: a later build may append to it (§4.4).</summary>
        [Fact]
        public void LongValue_IsReadUpToTheFamilysSize()
        {
            Assert.True(WireEndPoint.TryReadFrom(Bytes("04 CB 00 71 09 41 9C AA BB"), out IPEndPoint endPoint));
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.9"), 40001), endPoint);
        }

        /// <summary>
        /// What a dual-mode socket reports for an IPv4 peer is that IPv4 endpoint: written as family 4,
        /// and equal to it after <see cref="WireEndPoint.Normalize"/>, so comparisons hold.
        /// </summary>
        [Fact]
        public void IPv4MappedIPv6_IsFamily4()
        {
            var mapped = new IPEndPoint(IPAddress.Parse("::ffff:203.0.113.9"), 40001);
            var plain = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 40001);

            Assert.Equal(Bytes("04 CB 00 71 09 41 9C"), WireEndPoint.ToBytes(mapped));
            Assert.Equal(plain, WireEndPoint.Normalize(mapped));
            Assert.Same(plain, WireEndPoint.Normalize(plain)); // anything else as it is, not copied
            Assert.True(WireEndPoint.TryReadFrom(Bytes("06 00 00 00 00 00 00 00 00 00 00 FF FF CB 00 71 09 41 9C"), out IPEndPoint read));
            Assert.Equal(plain, read); // a peer that wrote one mapped anyway
        }
    }
}
