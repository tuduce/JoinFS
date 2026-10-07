using System;
using System.Collections.Generic;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// The exact bytes of a Hello and a HelloAck: envelope, fixed fields, offer list, Node and Build
    /// extensions. The handshake is JFP2's permanent entry point (docs/reference/jfp2-protocol.md §5.2):
    /// every build ever released must be able to start a handshake with every later one, so these
    /// bytes are a frozen spec, like JoinFS.Tests/Legacy/Fixtures. NEVER change them to make a test
    /// pass. If one fails, the code broke the wire; new things go into new extension tags (§5.5),
    /// and a test for them goes next to these, not into them.
    /// </summary>
    public class HandshakeGoldenTests
    {
        // Hello from 203.0.113.1:6112 (local 20), id 0x1234, to a peer whose id it does not know yet;
        // offers Position and Status at [1, 1]; build "26.6.0 JoinFS-FS2024"
        const string HelloHex =
            "FA 02 08 34 12 00 00 00" +              // envelope: magic, ProtoMajor 2, Internal, sender 0x1234, recipient 0, class Hello
            "02 02" +                                // ProtoMajorMin, ProtoMajorMax
            "00 00 00 00 00 00 00 00" +              // Capabilities
            "34 12" +                                // SelfAssignedId
            "00" +                                   // Result
            "02 00" +                                // OfferCount
            "00 00 01 01" +                          // application Position [1, 1]
            "00 07 01 01" +                          // application Status [1, 1]
            "01 00 07 00 01 71 00 CB E0 17 14" +     // TLV Node: 203.0.113.1, port 6112, local 20
            "02 00 14 00 32 36 2E 36 2E 30 20 4A 6F 69 6E 46 53 2D 46 53 32 30 32 34"; // TLV Build: "26.6.0 JoinFS-FS2024"

        // its answer from 198.51.100.2:6112 (local 2), id 0x5678; offers Position at [1, 1]; build "26.6.0 JoinFS-CONSOLE"
        const string HelloAckHex =
            "FA 02 08 78 56 34 12 01" +              // envelope: magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x1234, class HelloAck
            "02 02" +                                // ProtoMajorMin, ProtoMajorMax
            "00 00 00 00 00 00 00 00" +              // Capabilities
            "78 56" +                                // SelfAssignedId
            "00" +                                   // Result: accepted
            "01 00" +                                // OfferCount
            "00 00 01 01" +                          // application Position [1, 1]
            "01 00 07 00 02 64 33 C6 E0 17 02" +     // TLV Node: 198.51.100.2, port 6112, local 2
            "02 00 15 00 32 36 2E 36 2E 30 20 4A 6F 69 6E 46 53 2D 43 4F 4E 53 4F 4C 45"; // TLV Build: "26.6.0 JoinFS-CONSOLE"

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

        static HandshakeMessage Hello() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, Capabilities = 0, SelfAssignedId = 0x1234, Result = 0,
            Offers = new List<SchemaOffer> { new(false, MessageClasses.Position, 1, 1), new(false, MessageClasses.Status, 1, 1) },
            Node = new RelayNuid(0xCB007101, 6112, 20),
            Build = "26.6.0 JoinFS-FS2024",
        };

        static HandshakeMessage HelloAck() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, Capabilities = 0, SelfAssignedId = 0x5678, Result = 0,
            Offers = new List<SchemaOffer> { new(false, MessageClasses.Position, 1, 1) },
            Node = new RelayNuid(0xC6336402, 6112, 2),
            Build = "26.6.0 JoinFS-CONSOLE",
        };

        [Fact]
        public void Hello_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(HelloHex)), Convert.ToHexString(Datagram(MessageClasses.Hello, 0x1234, 0, Hello())));

        [Fact]
        public void HelloAck_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(HelloAckHex)), Convert.ToHexString(Datagram(MessageClasses.HelloAck, 0x5678, 0x1234, HelloAck())));

        [Fact]
        public void Hello_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloHex);
            Assert.True(Envelope.TryReadFrom(datagram, out Envelope envelope, out int header, out _));
            Assert.Equal(EnvelopeFlags.Internal, envelope.Flags);
            Assert.Equal(MessageClasses.Hello, envelope.RawMessageClass);
            Assert.Equal(0x1234, envelope.SenderPeerId);
            Assert.Equal(0, envelope.RecipientPeerId);

            HandshakeMessage hello = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal(2, hello.ProtoMajorMin);
            Assert.Equal(2, hello.ProtoMajorMax);
            Assert.Equal(0UL, hello.Capabilities);
            Assert.Equal(0x1234, hello.SelfAssignedId);
            Assert.Equal(2, hello.Offers.Count);
            Assert.Equal(MessageClasses.Status, hello.Offers[1].MessageClass);
            Assert.Equal(new RelayNuid(0xCB007101, 6112, 20), hello.Node);
            Assert.Equal("26.6.0 JoinFS-FS2024", hello.Build);
            Assert.Empty(hello.Extensions);
        }

        [Fact]
        public void HelloAck_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloAckHex);
            Assert.True(Envelope.TryReadFrom(datagram, out Envelope envelope, out int header, out _));
            Assert.Equal(MessageClasses.HelloAck, envelope.RawMessageClass);
            Assert.Equal(0x5678, envelope.SenderPeerId);
            Assert.Equal(0x1234, envelope.RecipientPeerId);

            HandshakeMessage ack = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal(0, ack.Result);
            Assert.Equal(0x5678, ack.SelfAssignedId);
            Assert.Equal(new SchemaOffer(false, MessageClasses.Position, 1, 1), ack.Offers[0]);
            Assert.Equal(new RelayNuid(0xC6336402, 6112, 2), ack.Node);
            Assert.Equal("26.6.0 JoinFS-CONSOLE", ack.Build);
        }
    }
}
