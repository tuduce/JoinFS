using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// The Candidates handshake extension (docs/jfp2/protocol.md §5.4, tag 4, edition 2): the speaker's own
    /// IPv6 endpoint candidates, a u8 length before each WireEndPoint. The NEW golden bytes here sit next to
    /// the frozen ones of HandshakeGoldenTests, which they never touch: a node without an IPv6 candidate
    /// sends no tag, so its handshake stays byte for byte as in 26.6.
    /// </summary>
    public class HandshakeCandidatesTests
    {
        // Appendix A.1's Hello from a node that has the IPv6 candidate [2001:db8::2]:6112
        const string HelloWithCandidatesHex =
            "FA 02 08 34 12 00 00 00" +
            "02 02" +
            "00 00 00 00 00 00 00 00" +
            "34 12" +
            "00" +
            "02 00" +
            "00 00 01 01" +
            "00 07 01 01" +
            "01 00 08 00 00 01 71 00 CB E0 17 14" +
            "02 00 14 00 32 36 2E 36 2E 30 20 4A 6F 69 6E 46 53 2D 46 53 32 30 32 34" +
            "04 00 14 00 13 06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 02 E0 17"; // TLV Candidates: one entry of 19 bytes

        // Appendix A.2's HelloAck (with the observed endpoint) from a node that has [2001:db8::3]:6112 and [2001:db8:1::3]:6113
        const string HelloAckWithCandidatesHex =
            "FA 02 08 78 56 34 12 01" +
            "02 02" +
            "00 00 00 00 00 00 00 00" +
            "78 56" +
            "00" +
            "01 00" +
            "00 00 01 01" +
            "01 00 08 00 00 02 64 33 C6 E0 17 02" +
            "02 00 15 00 32 36 2E 36 2E 30 20 4A 6F 69 6E 46 53 2D 43 4F 4E 53 4F 4C 45" +
            "03 00 07 00 04 CB 00 71 01 E0 17" +
            "04 00 28 00" +
            "13 06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 03 E0 17" +
            "13 06 20 01 0D B8 00 01 00 00 00 00 00 00 00 00 00 03 E1 17";

        static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

        static byte[] Datagram(byte messageClass, ushort sender, ushort recipient, HandshakeMessage message)
        {
            byte[] payload = message.Serialize();
            var envelope = new Envelope(EnvelopeFlags.Internal, sender, recipient, messageClass);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram, header);
            return datagram;
        }

        static IPEndPoint V6(string address, int port) => new(IPAddress.Parse(address), port);

        static HandshakeMessage Hello() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, Capabilities = 0, SelfAssignedId = 0x1234, Result = 0,
            Offers = [new(false, MessageClasses.Position, 1, 1), new(false, MessageClasses.Status, 1, 1)],
            Names = [NodeName.FromLegacy(new NodeId(0xCB007101, 6112, 20))],
            Build = "26.6.0 JoinFS-FS2024",
        };

        static HandshakeMessage HelloAck() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, Capabilities = 0, SelfAssignedId = 0x5678, Result = 0,
            Offers = [new(false, MessageClasses.Position, 1, 1)],
            Names = [NodeName.FromLegacy(new NodeId(0xC6336402, 6112, 2))],
            Build = "26.6.0 JoinFS-CONSOLE",
            ObservedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112),
        };

        [Fact]
        public void HelloWithCandidates_IsWrittenExactly()
        {
            HandshakeMessage hello = Hello();
            hello.Candidates = [V6("2001:db8::2", 6112)];
            Assert.Equal(Convert.ToHexString(Bytes(HelloWithCandidatesHex)), Convert.ToHexString(Datagram(MessageClasses.Hello, 0x1234, 0, hello)));
        }

        [Fact]
        public void HelloAckWithCandidates_IsWrittenExactly()
        {
            HandshakeMessage ack = HelloAck();
            ack.Candidates = [V6("2001:db8::3", 6112), V6("2001:db8:1::3", 6113)];
            Assert.Equal(Convert.ToHexString(Bytes(HelloAckWithCandidatesHex)), Convert.ToHexString(Datagram(MessageClasses.HelloAck, 0x5678, 0x1234, ack)));
        }

        [Fact]
        public void HelloWithCandidates_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloWithCandidatesHex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            HandshakeMessage hello = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal(V6("2001:db8::2", 6112), Assert.Single(hello.Candidates));
            Assert.Equal("26.6.0 JoinFS-FS2024", hello.Build);
            Assert.Empty(hello.Extensions);
        }

        [Fact]
        public void HelloAckWithCandidates_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloAckWithCandidatesHex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            HandshakeMessage ack = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal([V6("2001:db8::3", 6112), V6("2001:db8:1::3", 6113)], ack.Candidates);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112), ack.ObservedEndPoint);
            Assert.Empty(ack.Extensions);
        }

        [Fact]
        public void WithoutCandidates_NoTagIsWritten()
        {
            // a node with no IPv6 candidate writes the 26.6 bytes: the Hello above minus its 24-byte tag
            byte[] datagram = Datagram(MessageClasses.Hello, 0x1234, 0, Hello());
            Assert.Equal(Bytes(HelloWithCandidatesHex).Length - 24, datagram.Length);
            Assert.Empty(HandshakeMessage.Deserialize(datagram.AsSpan(Envelope.FixedSize)).Candidates);
        }

        [Fact]
        public void MoreThanFourCandidates_AreCutToFourOnWriteAndOnRead()
        {
            HandshakeMessage hello = Hello();
            hello.Candidates = [.. Enumerable.Range(1, 6).Select(i => V6("2001:db8::" + i, 6000 + i))];
            byte[] datagram = Datagram(MessageClasses.Hello, 0x1234, 0, hello);
            HandshakeMessage back = HandshakeMessage.Deserialize(datagram.AsSpan(Envelope.FixedSize));
            Assert.Equal(4, back.Candidates.Count);
            Assert.Equal(V6("2001:db8::4", 6004), back.Candidates[3]);

            // a longer list from a later edition is read up to four as well
            var bytes = new List<byte>(Hello().Serialize());
            var entries = new List<byte>();
            foreach (IPEndPoint endPoint in hello.Candidates)
            {
                entries.Add(19);
                entries.AddRange(WireEndPoint.ToBytes(endPoint));
            }
            Tlv.Write(bytes, 4, entries.ToArray());
            Assert.Equal(4, HandshakeMessage.Deserialize(bytes.ToArray()).Candidates.Count);
        }

        [Fact]
        public void AnEntryWithAnUnknownFamilyOrTooShort_IsSkippedByItsLength()
        {
            var value = new List<byte>();
            value.AddRange([5, 9, 1, 2, 3, 4]);          // family 9: unassigned, skipped by its length byte
            value.AddRange([4, 6, 1, 2, 3]);             // family 6 but shorter than 19 bytes: ignored
            value.Add(19);
            value.AddRange(WireEndPoint.ToBytes(V6("2001:db8::7", 7000)));
            value.AddRange([7, 4, 203, 0, 113, 9, 0x41, 0x9C]); // an IPv4 entry is read (the core uses IPv6 only)
            var bytes = new List<byte>(Hello().Serialize());
            Tlv.Write(bytes, 4, value.ToArray());

            HandshakeMessage back = HandshakeMessage.Deserialize(bytes.ToArray());
            Assert.Equal([V6("2001:db8::7", 7000), new IPEndPoint(IPAddress.Parse("203.0.113.9"), 40001)], back.Candidates);
        }

        [Fact]
        public void AnEntryLongerThanItsFamily_IsReadUpToTheFamilysSize()
        {
            var value = new List<byte> { 22 };
            value.AddRange(WireEndPoint.ToBytes(V6("2001:db8::8", 8000)));
            value.AddRange([0xAA, 0xBB, 0xCC]);
            var bytes = new List<byte>(Hello().Serialize());
            Tlv.Write(bytes, 4, value.ToArray());
            Assert.Equal(V6("2001:db8::8", 8000), Assert.Single(HandshakeMessage.Deserialize(bytes.ToArray()).Candidates));
        }

        [Fact]
        public void ATruncatedLastEntry_IsIgnored()
        {
            var value = new List<byte> { 19 };
            value.AddRange(WireEndPoint.ToBytes(V6("2001:db8::9", 9000)));
            value.AddRange([19, 6, 0x20, 0x01]);         // claims 19 bytes, has 2
            var bytes = new List<byte>(Hello().Serialize());
            Tlv.Write(bytes, 4, value.ToArray());
            Assert.Equal(V6("2001:db8::9", 9000), Assert.Single(HandshakeMessage.Deserialize(bytes.ToArray()).Candidates));
        }

        [Fact]
        public void OnlyIPv6Candidates_AreWritten()
        {
            HandshakeMessage hello = Hello();
            hello.Candidates = [new IPEndPoint(IPAddress.Parse("203.0.113.9"), 6112), new IPEndPoint(IPAddress.Parse("::ffff:203.0.113.9"), 6112), V6("2001:db8::2", 6112)];
            HandshakeMessage back = HandshakeMessage.Deserialize(Datagram(MessageClasses.Hello, 0x1234, 0, hello).AsSpan(Envelope.FixedSize));
            Assert.Equal(V6("2001:db8::2", 6112), Assert.Single(back.Candidates));
        }
    }
}
