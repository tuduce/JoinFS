using System;
using System.Collections.Generic;
using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// The exact bytes of the candidates of edition 2 (docs/network/jfp2/protocol.md §5.14, Appendix A.13 and A.14): the Hello that lists
    /// them and the MemberAnnounce of schema version 2. They sit next to <see cref="AdmissionGoldenTests"/>; the Hello of 26.6
    /// (<see cref="HandshakeGoldenTests"/>) is untouched, since a node with no candidates writes no tag. NEVER change these bytes to make a
    /// test pass.
    /// </summary>
    public class CandidatesGoldenTests
    {
        // the Hello of AdmissionGoldenTests without its Admission tag, with the Candidates tag at the end (A.13)
        const string HelloHex =
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
            "05 00 1C 00" +
            "13 06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17" +
            "07 04 CB 00 71 01 E0 17";

        // A.14
        const string AnnounceHex =
            "FA 02 08 78 56 21 43 05" +
            "01" +
            "00 01 71 00 CB E0 17 14" +
            "07 04 CB 00 71 01 E0 17" +
            "02" +
            "13 06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17" +
            "07 04 CB 00 71 01 E0 17";

        static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

        static readonly IPEndPoint[] Candidates = [IPEndPoint.Parse("[2001:db8::1]:6112"), IPEndPoint.Parse("203.0.113.1:6112")];

        static byte[] Datagram(EnvelopeFlags flags, ushort sender, ushort recipient, byte messageClass, byte[] payload)
        {
            var envelope = new Envelope(flags, sender, recipient, messageClass);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram, header);
            return datagram;
        }

        static HandshakeMessage Hello() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, SelfAssignedId = 0x1234,
            Offers = new List<SchemaOffer> { new(false, MessageClasses.Position, 1, 1), new(false, MessageClasses.Status, 1, 1) },
            Names = [NodeId.FromLegacy(0xCB007101, 6112, 20)],
            Build = "26.6.0 JoinFS-FS2024",
            Candidates = [.. Candidates],
        };

        [Fact]
        public void HelloWithCandidates_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(HelloHex)), Convert.ToHexString(Datagram(EnvelopeFlags.Internal, 0x1234, 0, MessageClasses.Hello, Hello().Serialize())));

        [Fact]
        public void HelloWithCandidates_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloHex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            HandshakeMessage hello = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal(Candidates, hello.Candidates);
            Assert.Empty(hello.Extensions);
        }

        [Fact]
        public void AHelloWithNoCandidates_IsTheHelloOf26_6()
        {
            HandshakeMessage hello = Hello();
            hello.Candidates = null;
            Assert.Equal(Convert.ToHexString(Bytes(HelloHex)[..^(4 + 28)]), Convert.ToHexString(Datagram(EnvelopeFlags.Internal, 0x1234, 0, MessageClasses.Hello, hello.Serialize())));
        }

        [Fact]
        public void MemberAnnounceVersion2_IsWrittenExactly()
        {
            var entry = new MemberEntry([NodeId.FromLegacy(0xCB007101, 6112, 20)], IPEndPoint.Parse("203.0.113.1:6112"), Candidates);
            byte[] body = new MemberAnnounceBody { Entry = entry }.Serialize(2);
            Assert.Equal(Convert.ToHexString(Bytes(AnnounceHex)), Convert.ToHexString(Datagram(EnvelopeFlags.Internal, 0x5678, 0x4321, MessageClasses.MemberAnnounce, body)));
        }

        [Fact]
        public void MemberAnnounceVersion2_IsReadExactly()
        {
            byte[] datagram = Bytes(AnnounceHex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            Assert.True(MemberAnnounceBody.TryRead(datagram.AsSpan(header), out MemberAnnounceBody announce, 2));
            Assert.Equal(Candidates, announce.Entry.Candidates);
            Assert.Equal("203.0.113.1:6112", announce.Entry.EndPoint.ToString());
        }
    }
}
