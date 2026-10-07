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
        /// <summary>A handshake payload's fixed fields with no offers (§5.2): majors 2, capabilities 8, id 2, result 1, offer count 2.</summary>
        const int HandshakeFixedSize = 15;

        /// <summary>Two capability bits for the tests. No capability is assigned (§5.4): these stand for any.</summary>
        const ulong CapabilityX = 1UL << 0, CapabilityY = 1UL << 1;

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
            Negotiator.Resolve(peerA, CapabilityX, OffersA(), CapabilityX, OffersB());

            // B caps Position at 1 despite A offering up to 2 - the agreed version is the highest
            // both sides can speak, never higher.
            Assert.Equal(1, peerA.AgreedAppVersion[MessageClasses.Position]);
            Assert.Equal(1, peerA.AgreedAppVersion[MessageClasses.Identity]);
        }

        [Fact]
        public void Resolve_ClassOnlyOneSideKnows_FallsBackToVersionZero()
        {
            var peerA = new PeerSession();
            Negotiator.Resolve(peerA, CapabilityX, OffersA(), CapabilityX, OffersB());

            // B never declared VariableSync as its own message class - version 0 is the documented
            // "don't send this to this peer" baseline (docs/reference/jfp2-protocol.md §5.3).
            Assert.Equal(0, peerA.AgreedAppVersion[MessageClasses.VariableSync]);
        }

        /// <summary>Resolving again starts from scratch, and says whether the agreement changed.</summary>
        [Fact]
        public void Resolve_Again_KeepsNothingFromTheLastAgreement()
        {
            var session = new PeerSession();
            Assert.True(Negotiator.Resolve(session, 0, OffersA(), 0, OffersA()));
            Assert.Equal(1, session.AgreedAppVersion[MessageClasses.VariableSync]);

            // neither side offers VariableSync any more
            Assert.True(Negotiator.Resolve(session, 0, OffersB(), 0, OffersB()));
            Assert.Equal(0, session.AgreedAppVersion[MessageClasses.VariableSync]);

            Assert.False(Negotiator.Resolve(session, 0, OffersB(), 0, OffersB()));
        }

        [Fact]
        public void Resolve_IsSymmetric_BothSidesAgreeOnSameVersions()
        {
            var peerA = new PeerSession();
            var peerB = new PeerSession();
            Negotiator.Resolve(peerA, CapabilityX, OffersA(), CapabilityX, OffersB());
            Negotiator.Resolve(peerB, CapabilityX, OffersB(), CapabilityX, OffersA());

            Assert.Equal(peerA.AgreedAppVersion[MessageClasses.Position], peerB.AgreedAppVersion[MessageClasses.Position]);
            Assert.Equal(peerA.AgreedAppVersion[MessageClasses.Identity], peerB.AgreedAppVersion[MessageClasses.Identity]);
            Assert.Equal(peerA.AgreedAppVersion[MessageClasses.VariableSync], peerB.AgreedAppVersion[MessageClasses.VariableSync]);
        }

        [Fact]
        public void Resolve_CapabilitiesAreBitwiseAnded()
        {
            var peerA = new PeerSession();
            ulong capsA = CapabilityX | CapabilityY;
            ulong capsB = CapabilityX;
            Negotiator.Resolve(peerA, capsA, OffersA(), capsB, OffersB());

            Assert.True((peerA.AgreedCapabilities & CapabilityX) != 0);
            // B never offered Y, so it cannot be agreed even though A offered it.
            Assert.False((peerA.AgreedCapabilities & CapabilityY) != 0);
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
                Capabilities = CapabilityX | CapabilityY,
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
        public void HandshakeMessage_Names_RoundTripAndAreNotLeftInExtensions()
        {
            NodeName key = NodeName.ReadFrom(new byte[] { 1, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11 });
            var hello = new HandshakeMessage { SelfAssignedId = 5, Names = [NodeName.FromLegacy(new JoinFS.Net.NodeId(0xCB007101, 6112, 20)), key] };
            hello.Extensions[0x0100] = new byte[] { 9 };

            HandshakeMessage back = HandshakeMessage.Deserialize(hello.Serialize());

            Assert.Equal(hello.Names, back.Names);
            Assert.DoesNotContain(HandshakeMessage.NamesTag, back.Extensions.Keys);
            Assert.Equal(new byte[] { 9 }, back.Extensions[0x0100]);
        }

        [Fact]
        public void HandshakeMessage_WithoutNames_HasNone()
        {
            Assert.Empty(HandshakeMessage.Deserialize(new HandshakeMessage { SelfAssignedId = 5 }.Serialize()).Names);
        }

        [Fact]
        public void HandshakeMessage_Build_RoundTripsAndIsNotLeftInExtensions()
        {
            var hello = new HandshakeMessage { SelfAssignedId = 5, Build = "26.6.0 JoinFS-FS2024" };

            HandshakeMessage back = HandshakeMessage.Deserialize(hello.Serialize());

            Assert.Equal("26.6.0 JoinFS-FS2024", back.Build);
            Assert.DoesNotContain(HandshakeMessage.BuildTag, back.Extensions.Keys);
        }

        [Fact]
        public void HandshakeMessage_WithoutBuild_HasNone()
        {
            Assert.Null(HandshakeMessage.Deserialize(new HandshakeMessage { SelfAssignedId = 5 }.Serialize()).Build);
            Assert.Null(HandshakeMessage.Deserialize(new HandshakeMessage { SelfAssignedId = 5, Build = "\r\n" }.Serialize()).Build);
        }

        /// <summary>
        /// The build is free text from unauthenticated Hellos that ends up in the log: only printable
        /// ASCII survives (no line breaks, bidi overrides, zero-width or line-separator characters), bounded.
        /// </summary>
        [Fact]
        public void CleanBuild_KeepsPrintableAsciiOnly_AndCaps()
        {
            string dirty = "26.6\r\nERROR: fake\u0007\u202E\u200B\u2028\u00e9" + new string('x', 100);

            Assert.Equal("26.6ERROR: fake" + new string('x', HandshakeMessage.BuildMaxBytes - 15), HandshakeMessage.CleanBuild(dirty));
            Assert.Null(HandshakeMessage.CleanBuild((string)null));
            Assert.Null(HandshakeMessage.CleanBuild("\u00e9\r\n"));
        }

        /// <summary>
        /// Serialize cleans the build itself, so no caller can put control characters or an oversized
        /// value on the wire: the TLV holds exactly the cleaned bytes.
        /// </summary>
        [Fact]
        public void HandshakeMessage_Build_IsCleanedAndCappedOnSend()
        {
            string dirty = "26.6\r\n‮" + new string('x', 70000);
            byte[] payload = new HandshakeMessage { SelfAssignedId = 5, Build = dirty }.Serialize();

            Dictionary<ushort, byte[]> tlvs = Tlv.ReadAll(payload.AsSpan(HandshakeFixedSize));

            Assert.Equal(Encoding.ASCII.GetBytes(HandshakeMessage.CleanBuild(dirty)), tlvs[HandshakeMessage.BuildTag]);
            Assert.Equal(HandshakeMessage.BuildMaxBytes, tlvs[HandshakeMessage.BuildTag].Length);
        }

        /// <summary>
        /// A peer that does not clean or cap its build (another implementation, or a forged Hello) is
        /// cut to BuildMaxBytes before decoding, then cleaned.
        /// </summary>
        [Fact]
        public void HandshakeMessage_Build_IsCleanedAndCappedOnReceive()
        {
            var bytes = new List<byte>(new HandshakeMessage { SelfAssignedId = 5 }.Serialize());
            Tlv.Write(bytes, HandshakeMessage.BuildTag, Encoding.UTF8.GetBytes("x\ny\u202E" + new string('z', 300)));

            HandshakeMessage back = HandshakeMessage.Deserialize(bytes.ToArray());

            // the first 64 bytes: x, LF, y, U+202E (3 bytes), then 58 z
            Assert.Equal("xy" + new string('z', HandshakeMessage.BuildMaxBytes - 6), back.Build);
        }

        // ------------------------------------------------ extension rules (docs/reference/jfp2-protocol.md §5.5)

        /// <summary>A handshake payload with no offers and no extensions, then these TLVs exactly as given.</summary>
        static byte[] WithTlvs(params (ushort Tag, byte[] Value)[] tlvs)
        {
            var bytes = new List<byte>(new HandshakeMessage { SelfAssignedId = 5 }.Serialize());
            Assert.Equal(HandshakeFixedSize, bytes.Count);
            foreach (var (tag, value) in tlvs) Tlv.Write(bytes, tag, value);
            return bytes.ToArray();
        }

        static byte[] LegacyName(byte local) => [0, 0x01, 0x71, 0x00, 0xCB, 0xE0, 0x17, local]; // kind 0, 203.0.113.1:6112

        static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        /// <summary>A list goes inside one value; a tag repeated counts once, by its first copy.</summary>
        [Fact]
        public void RepeatedTag_TheFirstCopyCounts()
        {
            HandshakeMessage back = HandshakeMessage.Deserialize(WithTlvs(
                (HandshakeMessage.BuildTag, Encoding.ASCII.GetBytes("first")),
                (HandshakeMessage.NamesTag, LegacyName(1)),
                (0x7FFF, [1]),
                (HandshakeMessage.BuildTag, Encoding.ASCII.GetBytes("second")),
                (HandshakeMessage.NamesTag, LegacyName(2)),
                (0x7FFF, [2])));

            Assert.Equal("first", back.Build);
            Assert.Equal(1, Assert.Single(back.Names).TryGetLegacy(out JoinFS.Net.NodeId id) ? id.local : -1);
            Assert.Equal(new byte[] { 1 }, back.Extensions[0x7FFF]);
        }

        /// <summary>A value shorter than its tag needs is ignored: a name needs 8 bytes, a build at least one.</summary>
        [Fact]
        public void ShortValue_IsIgnored()
        {
            HandshakeMessage back = HandshakeMessage.Deserialize(WithTlvs(
                (HandshakeMessage.NamesTag, LegacyName(1)[..7]),
                (HandshakeMessage.BuildTag, [])));

            Assert.Empty(back.Names);
            Assert.Null(back.Build);
            Assert.Empty(back.Extensions); // known tags are consumed even when ignored
        }

        /// <summary>
        /// A longer value is read up to the prefix this build knows and the rest skipped: that is how a
        /// later build extends a value. Names are read in whole names, a build up to 64 bytes; what
        /// follows the long value is still read.
        /// </summary>
        [Fact]
        public void LongValue_IsReadUpToTheKnownPrefix()
        {
            HandshakeMessage back = HandshakeMessage.Deserialize(WithTlvs(
                (HandshakeMessage.BuildTag, Encoding.ASCII.GetBytes(new string('b', 64) + "tail")),
                (HandshakeMessage.NamesTag, Concat(LegacyName(1), LegacyName(2), LegacyName(3))),
                (0x7FFF, [7])));

            Assert.Equal(new string('b', 64), back.Build);
            Assert.Equal(3, back.Names.Count);
            Assert.Equal(new byte[] { 7 }, back.Extensions[0x7FFF]);
        }

        /// <summary>Names are read in whole names: a remainder shorter than a name is skipped, not the whole value.</summary>
        [Fact]
        public void Names_WithATrailingRemainder_ReadsTheWholeNames()
        {
            HandshakeMessage back = HandshakeMessage.Deserialize(WithTlvs(
                (HandshakeMessage.NamesTag, Concat(LegacyName(1), [1, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11], [9, 9, 9])),
                (HandshakeMessage.BuildTag, Encoding.ASCII.GetBytes("after"))));

            Assert.Equal(2, back.Names.Count);
            Assert.Equal(0, back.Names[0].Kind);
            Assert.Equal(1, back.Names[1].Kind);
            Assert.Equal("after", back.Build);
        }

        /// <summary>
        /// The partition byte of an offer: 0 application, 1 internal; any other value is another class
        /// space this build does not know, and the offer is skipped (§5.2), the ones after it still read.
        /// </summary>
        [Fact]
        public void Offer_OfAnUnknownPartition_IsSkipped()
        {
            byte[] payload = new HandshakeMessage
            {
                SelfAssignedId = 5,
                Offers = [new(false, MessageClasses.Notes, 1, 1), new(true, MessageClasses.GuaranteedDone, 1, 1), new(false, MessageClasses.Position, 1, 1)],
            }.Serialize();
            payload[HandshakeFixedSize] = 2; // the first offer's partition byte

            HandshakeMessage back = HandshakeMessage.Deserialize(payload);

            Assert.Equal(new[] { new SchemaOffer(true, MessageClasses.GuaranteedDone, 1, 1), new SchemaOffer(false, MessageClasses.Position, 1, 1) }, back.Offers);
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
