using System.Collections.Generic;
using System.Text;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Formalizes the negotiation/handshake behavior specified in docs/reference/jfp2-protocol.md §5 (the
    // Hello/HelloAck handshake, §5.3's per-class resolution algorithm, §5.5's extension TLV), now run
    // against the code ported into JoinFS/Jfp2/Negotiation.cs (docs/protocol-v2-implementation-plan.md
    // Phase 0/1). Scenario: peer A is a fresh build offering Position v1-2/Identity v1/VariableSync
    // v1, peer B is one release behind (Position v1 only, Identity v1, never heard of VariableSync).
    public class NegotiationTests
    {
        static SchemaOffer[] OffersA() => new[]
        {
            new SchemaOffer(false, MessageClasses.Position, 1, 2),
            new SchemaOffer(false, MessageClasses.Identity, 1, 1),
            new SchemaOffer(false, MessageClasses.VariableSync, 1, 1),
        };

        static SchemaOffer[] OffersB() => new[]
        {
            new SchemaOffer(false, MessageClasses.Position, 1, 1),
            new SchemaOffer(false, MessageClasses.Identity, 1, 1),
        };

        [Fact]
        public void Resolve_PicksHighestMutuallySupportedVersion()
        {
            var peerA = new PeerSession();
            Negotiator.Resolve(peerA, (ulong)Capability.Coalescing, OffersA(), (ulong)Capability.Coalescing, OffersB());

            // B caps Position at 1 despite A offering up to 2 - the agreed version is the highest
            // both sides can speak, never higher.
            Assert.Equal(1, peerA.AgreedAppVersion[MessageClasses.Position]);
            Assert.Equal(1, peerA.AgreedAppVersion[MessageClasses.Identity]);
        }

        [Fact]
        public void Resolve_ClassOnlyOneSideKnows_FallsBackToVersionZero()
        {
            var peerA = new PeerSession();
            Negotiator.Resolve(peerA, (ulong)Capability.Coalescing, OffersA(), (ulong)Capability.Coalescing, OffersB());

            // B never declared VariableSync as its own message class - version 0 is the documented
            // "don't send this to this peer" baseline (docs/reference/jfp2-protocol.md §5.3).
            Assert.Equal(0, peerA.AgreedAppVersion[MessageClasses.VariableSync]);
        }

        [Fact]
        public void Resolve_IsSymmetric_BothSidesAgreeOnSameVersions()
        {
            var peerA = new PeerSession();
            var peerB = new PeerSession();
            Negotiator.Resolve(peerA, (ulong)Capability.Coalescing, OffersA(), (ulong)Capability.Coalescing, OffersB());
            Negotiator.Resolve(peerB, (ulong)Capability.Coalescing, OffersB(), (ulong)Capability.Coalescing, OffersA());

            Assert.Equal(peerA.AgreedAppVersion[MessageClasses.Position], peerB.AgreedAppVersion[MessageClasses.Position]);
            Assert.Equal(peerA.AgreedAppVersion[MessageClasses.Identity], peerB.AgreedAppVersion[MessageClasses.Identity]);
            Assert.Equal(peerA.AgreedAppVersion[MessageClasses.VariableSync], peerB.AgreedAppVersion[MessageClasses.VariableSync]);
        }

        [Fact]
        public void Resolve_CapabilitiesAreBitwiseAnded()
        {
            var peerA = new PeerSession();
            ulong capsA = (ulong)(Capability.Coalescing | Capability.QuantizedPosition);
            ulong capsB = (ulong)Capability.Coalescing;
            Negotiator.Resolve(peerA, capsA, OffersA(), capsB, OffersB());

            Assert.True((peerA.AgreedCapabilities & (ulong)Capability.Coalescing) != 0);
            // B never offered QuantizedPosition, so it cannot be agreed even though A offered it.
            Assert.False((peerA.AgreedCapabilities & (ulong)Capability.QuantizedPosition) != 0);
        }

        [Fact]
        public void Resolve_DisjointRanges_ProduceVersionZero()
        {
            var peerA = new PeerSession();
            var offersA = new[] { new SchemaOffer(false, MessageClasses.Status, 3, 4) };
            var offersB = new[] { new SchemaOffer(false, MessageClasses.Status, 1, 2) };

            Negotiator.Resolve(peerA, 0, offersA, 0, offersB);

            Assert.Equal(0, peerA.AgreedAppVersion[MessageClasses.Status]);
        }

        [Fact]
        public void HandshakeMessage_SerializeDeserialize_RoundTrips()
        {
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor,
                ProtoMajorMax = Envelope.ProtoMajor,
                Capabilities = (ulong)(Capability.Coalescing | Capability.QuantizedPosition),
                SelfAssignedId = 1001,
                Offers = new List<SchemaOffer>(OffersA()),
            };
            hello.Extensions[0x0100] = Encoding.UTF8.GetBytes("JoinFS-26.6-dev");

            byte[] wire = hello.Serialize();
            HandshakeMessage back = HandshakeMessage.Deserialize(wire);

            Assert.Equal(hello.ProtoMajorMin, back.ProtoMajorMin);
            Assert.Equal(hello.ProtoMajorMax, back.ProtoMajorMax);
            Assert.Equal(hello.Capabilities, back.Capabilities);
            Assert.Equal(hello.SelfAssignedId, back.SelfAssignedId);
            Assert.Equal(hello.Offers.Count, back.Offers.Count);
            Assert.Equal("JoinFS-26.6-dev", Encoding.UTF8.GetString(back.Extensions[0x0100]));
        }

        [Fact]
        public void HandshakeMessage_NodeIdentity_RoundTripsAndIsNotLeftInExtensions()
        {
            var hello = new HandshakeMessage { SelfAssignedId = 5, Node = new RelayNuid(0xCB007101, 6112, 20) };
            hello.Extensions[0x0100] = new byte[] { 9 };

            HandshakeMessage back = HandshakeMessage.Deserialize(hello.Serialize());

            Assert.Equal(hello.Node, back.Node);
            Assert.DoesNotContain(HandshakeMessage.NodeTag, back.Extensions.Keys);
            Assert.Equal(new byte[] { 9 }, back.Extensions[0x0100]);
        }

        [Fact]
        public void HandshakeMessage_WithoutNodeIdentity_HasNone()
        {
            Assert.Null(HandshakeMessage.Deserialize(new HandshakeMessage { SelfAssignedId = 5 }.Serialize()).Node);
        }

        [Fact]
        public void HandshakeMessage_UnrecognizedExtensionTag_IsSkippedNotThrown()
        {
            var hello = new HandshakeMessage { SelfAssignedId = 1 };
            hello.Extensions[0x00FF] = new byte[] { 1, 2, 3 };
            hello.Extensions[0x0100] = Encoding.UTF8.GetBytes("known");

            byte[] wire = hello.Serialize();
            HandshakeMessage back = HandshakeMessage.Deserialize(wire);

            // A reader that doesn't recognize tag 0x00FF still parses the rest of the message
            // correctly - it just never looks the tag up (docs/reference/jfp2-protocol.md §5.5).
            Assert.Equal(2, back.Extensions.Count);
            Assert.Equal("known", Encoding.UTF8.GetString(back.Extensions[0x0100]));
        }
    }
}
