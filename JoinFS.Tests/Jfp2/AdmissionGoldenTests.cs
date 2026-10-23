using System;
using System.Collections.Generic;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// The exact bytes of the admission exchange (docs/network/jfp2/protocol.md §5.9, Appendix A.3 to A.5): the Hello
    /// that asks, the Hello that echoes the cookie, and the HelloAck with Result 2. They sit next to
    /// <see cref="HandshakeGoldenTests"/>, which keeps what a 26.6 node sends and is never changed: the new
    /// things are one new extension tag and one new Result value, nothing else (§5.8). NEVER change these
    /// bytes to make a test pass.
    /// </summary>
    public class AdmissionGoldenTests
    {
        // the cookie is 00 01 02 ... 0F
        const string CookieHex = "00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F";

        // the Hello of HandshakeGoldenTests (203.0.113.1:6112, local 20, id 0x1234, build "26.6.0 JoinFS-FS2024") with the
        // Admission extension empty at the end: "I am a stranger, give me a cookie"
        const string AskHex =
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
            "04 00 00 00";                                // TLV Admission: empty

        // the same Hello echoing the cookie
        const string EchoHex =
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
            "04 00 10 00 " + CookieHex;                   // TLV Admission: the cookie

        // the answer of a node in a session at 198.51.100.2:6112 (local 2) to AskHex, which came from 203.0.113.1:6112:
        // sender 0, recipient 0x1234, Result 2, no offers, no build, the observed endpoint and the cookie
        const string ChallengeHex =
            "FA 02 08 00 00 34 12 01" +
            "02 02" +
            "00 00 00 00 00 00 00 00" +
            "00 00" +
            "02" +                                         // Result: not admitted
            "00 00" +                                      // no offers
            "01 00 08 00 00 02 64 33 C6 E0 17 02" +
            "03 00 07 00 04 CB 00 71 01 E0 17" +
            "04 00 10 00 " + CookieHex;

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

        static HandshakeMessage Hello(byte[] admission) => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, SelfAssignedId = 0x1234,
            Offers = new List<SchemaOffer> { new(false, MessageClasses.Position, 1, 1), new(false, MessageClasses.Status, 1, 1) },
            Names = [NodeId.FromLegacy(0xCB007101, 6112, 20)],
            Build = "26.6.0 JoinFS-FS2024",
            Admission = admission,
        };

        static HandshakeMessage Challenge() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, Result = HandshakeMessage.ResultNotAdmitted,
            Names = [NodeId.FromLegacy(0xC6336402, 6112, 2)],
            ObservedEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("203.0.113.1"), 6112),
            Admission = Bytes(CookieHex),
        };

        static HandshakeMessage Read(string hex)
        {
            byte[] datagram = Bytes(hex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            return HandshakeMessage.Deserialize(datagram.AsSpan(header));
        }

        [Fact]
        public void HelloAskingForAdmission_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(AskHex)), Convert.ToHexString(Datagram(MessageClasses.Hello, 0x1234, 0, Hello([]))));

        [Fact]
        public void HelloEchoingTheCookie_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(EchoHex)), Convert.ToHexString(Datagram(MessageClasses.Hello, 0x1234, 0, Hello(Bytes(CookieHex)))));

        [Fact]
        public void ChallengeHelloAck_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(ChallengeHex)), Convert.ToHexString(Datagram(MessageClasses.HelloAck, 0, 0x1234, Challenge())));

        [Fact]
        public void HelloAskingForAdmission_IsReadExactly()
        {
            HandshakeMessage hello = Read(AskHex);
            Assert.NotNull(hello.Admission);
            Assert.Empty(hello.Admission);
            Assert.Equal(NodeId.FromLegacy(0xCB007101, 6112, 20), Assert.Single(hello.Names));
            Assert.Empty(hello.Extensions);
        }

        [Fact]
        public void HelloEchoingTheCookie_IsReadExactly() => Assert.Equal(Bytes(CookieHex), Read(EchoHex).Admission);

        [Fact]
        public void ChallengeHelloAck_IsReadExactly()
        {
            HandshakeMessage ack = Read(ChallengeHex);
            Assert.Equal(HandshakeMessage.ResultNotAdmitted, ack.Result);
            Assert.Empty(ack.Offers);
            Assert.Equal(Bytes(CookieHex), ack.Admission);
            Assert.Equal("203.0.113.1:6112", ack.ObservedEndPoint?.ToString());
            Assert.Equal(NodeId.FromLegacy(0xC6336402, 6112, 2), Assert.Single(ack.Names));
        }

        [Fact]
        public void TheHelloOf26_6_IsStillWrittenWithoutAdmission()
        {
            // a Hello that asks for nothing is the Hello of HandshakeGoldenTests: no Admission writes no tag
            string withoutTag = AskHex[..^"04 00 00 00".Length];
            Assert.Equal(Convert.ToHexString(Bytes(withoutTag)), Convert.ToHexString(Datagram(MessageClasses.Hello, 0x1234, 0, Hello(null))));
            Assert.Null(Read(withoutTag).Admission);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(15)]
        public void AnAdmissionValueShorterThanACookie_IsIgnored(int length)
        {
            byte[] datagram = Bytes(AskHex[..^"04 00 00 00".Length] + "04 00 " + length.ToString("X2") + " 00 " + string.Join(" ", new string[length].Select(_ => "AA")));
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            Assert.Null(HandshakeMessage.Deserialize(datagram.AsSpan(header)).Admission);
        }

        [Fact]
        public void AnAdmissionValueLongerThanACookie_IsReadAsItsFirstSixteenBytes()
        {
            byte[] datagram = Bytes(EchoHex[..^CookieHex.Length].Replace("04 00 10 00 ", "04 00 14 00 ") + CookieHex + " FF FF FF FF");
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            Assert.Equal(Bytes(CookieHex), HandshakeMessage.Deserialize(datagram.AsSpan(header)).Admission);
        }
    }
}
