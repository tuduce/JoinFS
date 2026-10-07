using JoinFS.Net;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Formalizes the envelope round-trip and legacy/JFP2 discrimination behavior specified in
    // docs/reference/jfp2-protocol.md §3 (coexistence via the magic byte) and §4.1 (the fixed envelope),
    // now run against the code ported into JoinFS/Jfp2/Envelope.cs
    // (docs/protocol-v2-implementation-plan.md Phase 0/1).
    public class EnvelopeTests
    {
        [Fact]
        public void WriteTo_ReadFrom_RoundTrips()
        {
            var envelope = new Envelope(EnvelopeFlags.Internal, 1001, 2002, MessageClasses.GuaranteedDone);
            byte[] buffer = new byte[Envelope.FixedSize];

            int written = envelope.WriteTo(buffer);
            Envelope back = Envelope.ReadFrom(buffer, out int consumed);

            Assert.Equal(Envelope.FixedSize, written);
            Assert.Equal(Envelope.FixedSize, consumed);
            Assert.Equal(envelope.Flags, back.Flags);
            Assert.Equal(envelope.SenderPeerId, back.SenderPeerId);
            Assert.Equal(envelope.RecipientPeerId, back.RecipientPeerId);
            Assert.Equal(envelope.RawMessageClass, back.RawMessageClass);
        }

        [Fact]
        public void IsInternal_ReflectsInternalFlag()
        {
            var app = new Envelope(EnvelopeFlags.None, 1, 2, MessageClasses.Position);
            var internalMsg = new Envelope(EnvelopeFlags.Internal, 1, 2, MessageClasses.Hello);

            Assert.False(app.IsInternal);
            Assert.True(internalMsg.IsInternal);
        }

        [Fact]
        public void IsGuaranteed_ReflectsGuaranteedFlag()
        {
            var unreliable = new Envelope(EnvelopeFlags.None, 1, 2, MessageClasses.Position);
            var guaranteed = new Envelope(EnvelopeFlags.Guaranteed, 1, 2, MessageClasses.Event);

            Assert.False(unreliable.IsGuaranteed);
            Assert.True(guaranteed.IsGuaranteed);
        }

        [Fact]
        public void LegacyDatagram_DetectedAsLegacyOnly()
        {
            // 0x520B written little-endian, exactly as BinaryWriter.Write((short)0x520B) would.
            byte[] legacyDatagram = new byte[21];
            legacyDatagram[0] = 0x0B;
            legacyDatagram[1] = 0x52;

            Assert.True(Envelope.IsLegacyDatagram(legacyDatagram));
            Assert.False(Envelope.IsJfp2Datagram(legacyDatagram));
        }

        [Fact]
        public void Jfp2Datagram_DetectedAsJfp2Only()
        {
            byte[] jfp2Datagram = new byte[Envelope.FixedSize];
            new Envelope(EnvelopeFlags.None, 1001, 2002, MessageClasses.Position).WriteTo(jfp2Datagram);

            Assert.True(Envelope.IsJfp2Datagram(jfp2Datagram));
            Assert.False(Envelope.IsLegacyDatagram(jfp2Datagram));
        }

        [Fact]
        public void MagicBytes_CanNeverCollide()
        {
            // The whole coexistence strategy (docs/reference/jfp2-protocol.md §3) rests on this never
            // being equal - assert it directly so a future edit to either constant fails loudly.
            Assert.NotEqual(Envelope.Magic, (byte)(Envelope.LegacyVersionConstant & 0xFF));
        }

        [Fact]
        public void FixedSize_Is8Bytes()
        {
            // The 62%-smaller-than-legacy claim in docs/reference/jfp2-protocol.md §4.1 depends on this.
            Assert.Equal(8, Envelope.FixedSize);
        }

        // docs/protocol-v2-implementation-review.md Finding 1: the guaranteed-delivery extension block
        // (§4.4) was defined on the wire since Phase 1 but WriteTo/ReadFrom never actually produced or
        // consumed it. These tests cover the fix.

        [Fact]
        public void WriteTo_ReadFrom_RoundTrips_WithGuaranteedExtension()
        {
            var envelope = new Envelope(EnvelopeFlags.Guaranteed, 1001, 2002, MessageClasses.Event, guaranteedId: 4242, guaranteedIndex: 0, guaranteedCount: 1);
            byte[] buffer = new byte[Envelope.FixedSize + Envelope.GuaranteedExtraSize];

            int written = envelope.WriteTo(buffer);
            Envelope back = Envelope.ReadFrom(buffer, out int consumed);

            Assert.Equal(Envelope.FixedSize + Envelope.GuaranteedExtraSize, written);
            Assert.Equal(Envelope.FixedSize + Envelope.GuaranteedExtraSize, consumed);
            Assert.True(back.IsGuaranteed);
            Assert.Equal(envelope.GuaranteedId, back.GuaranteedId);
            Assert.Equal(envelope.GuaranteedIndex, back.GuaranteedIndex);
            Assert.Equal(envelope.GuaranteedCount, back.GuaranteedCount);
        }

        [Fact]
        public void WireSize_IncludesGuaranteedExtensionOnlyWhenGuaranteed()
        {
            var unreliable = new Envelope(EnvelopeFlags.None, 1, 2, MessageClasses.Position);
            var guaranteed = new Envelope(EnvelopeFlags.Guaranteed, 1, 2, MessageClasses.Event, 1, 0, 1);

            Assert.Equal(Envelope.FixedSize, unreliable.WireSize);
            Assert.Equal(Envelope.FixedSize + Envelope.GuaranteedExtraSize, guaranteed.WireSize);
        }

        [Fact]
        public void ReadFrom_GuaranteedDatagramTooShortForExtension_Throws()
        {
            // A full guaranteed envelope written into a buffer sized only for the fixed 8 bytes gets
            // truncated - the Flags byte still claims Guaranteed, but the 4-byte extension it promises
            // never made it into the datagram. ReadFrom must reject this rather than silently reading
            // past the buffer or into whatever payload bytes happen to follow.
            byte[] full = new byte[Envelope.FixedSize + Envelope.GuaranteedExtraSize];
            new Envelope(EnvelopeFlags.Guaranteed, 1, 2, MessageClasses.Event, 1, 0, 1).WriteTo(full);
            byte[] truncated = full[..Envelope.FixedSize];

            Assert.Throws<System.ArgumentException>(() => Envelope.ReadFrom(truncated, out _));
        }

        [Fact]
        public void GuaranteedEnvelope_DefaultConstructor_HasZeroExtensionFields()
        {
            // The 4-arg constructor (used by every non-guaranteed send site) must never leave
            // GuaranteedId/Index/Count uninitialized/garbage - they're always well-defined zeros.
            var envelope = new Envelope(EnvelopeFlags.None, 1, 2, MessageClasses.Position);

            Assert.Equal(0, envelope.GuaranteedId);
            Assert.Equal(0, envelope.GuaranteedIndex);
            Assert.Equal(0, envelope.GuaranteedCount);
        }

        // Relay-addressing extension (EnvelopeFlags.Forwarded / Origin+Target names) - see
        // docs/reference/jfp2-protocol.md §4.5 and §7.7. Both Origin and Target are
        // always carried (never a single field whose meaning flips by direction) so that any
        // receiving node can decide "consume or relay further" purely by comparing Target to its
        // own name, regardless of whether it's playing hub or final-recipient role for this message.

        static NodeName Legacy(uint ip, ushort port, byte local) => NodeName.FromLegacy(new NodeId(ip, port, local));

        /// <summary>
        /// The Forwarded extension's exact bytes (§4.5, §4.9): after the guaranteed extension, the
        /// origin's name then the target's, 8 bytes each, a kind byte first. A name of a kind this build
        /// does not resolve is still read, and compared as bytes.
        /// </summary>
        [Fact]
        public void Forwarded_CarriesTwoEightByteNames()
        {
            const string hex =
                "FA 02 03 34 12 78 56 03" +   // magic, ProtoMajor 2, Guaranteed | Forwarded, sender 0x1234, recipient 0x5678, class Event
                "E7 03 00 01" +               // GuaranteedId 999, index 0, count 1
                "00 01 71 00 CB E0 17 14" +   // origin: kind 0, legacy id 203.0.113.1 (ip u32 LE), port 6112, local 20
                "01 AA BB CC DD EE FF 11";    // target: kind 1 (a random key), 7 bytes of value
            byte[] expected = System.Convert.FromHexString(hex.Replace(" ", ""));
            NodeName origin = Legacy(0xCB007101, 6112, 20);
            NodeName target = NodeName.ReadFrom(expected.AsSpan(20, 8));

            var envelope = new Envelope(EnvelopeFlags.Guaranteed | EnvelopeFlags.Forwarded, 0x1234, 0x5678, MessageClasses.Event, 999, 0, 1, origin, target);
            byte[] written = new byte[envelope.WireSize];
            Assert.Equal(28, envelope.WriteTo(written));
            Assert.Equal(System.Convert.ToHexString(expected), System.Convert.ToHexString(written));

            Envelope back = Envelope.ReadFrom(expected, out int consumed);
            Assert.Equal(28, consumed);
            Assert.Equal(origin, back.Origin);
            Assert.Equal(0, back.Origin.Kind);
            Assert.True(back.Origin.TryGetLegacy(out NodeId originId));
            Assert.Equal(new NodeId(0xCB007101, 6112, 20), originId);
            Assert.Equal(target, back.Target);
            Assert.Equal(1, back.Target.Kind);
            Assert.False(back.Target.TryGetLegacy(out _));
            Assert.Equal("kind 1 AABBCCDDEEFF11", back.Target.ToString()); // the value in wire order, as the dissector shows it
        }

        [Fact]
        public void WriteTo_ReadFrom_RoundTrips_WithRelayExtension()
        {
            var origin = Legacy(0x0A0B0C0D, 5555, 42);
            var target = Legacy(0x11223344, 7777, 9);
            var envelope = new Envelope(EnvelopeFlags.Forwarded, 0, 0, MessageClasses.Position, 0, 0, 0, origin, target);
            byte[] buffer = new byte[Envelope.FixedSize + NodeName.WireSize * 2];

            int written = envelope.WriteTo(buffer);
            Envelope back = Envelope.ReadFrom(buffer, out int consumed);

            int expectedSize = Envelope.FixedSize + NodeName.WireSize * 2;
            Assert.Equal(expectedSize, written);
            Assert.Equal(expectedSize, consumed);
            Assert.True(back.IsForwarded);
            Assert.Equal(origin, back.Origin);
            Assert.Equal(target, back.Target);
        }

        [Fact]
        public void WriteTo_ReadFrom_RoundTrips_WithGuaranteedAndRelayExtensions()
        {
            // Both extensions present: Guaranteed's 4 bytes must land before Origin/Target's 16,
            // per Envelope.WireSize's documented ordering.
            var origin = Legacy(0x7F000001, 8080, 1);
            var target = Legacy(0x7F000002, 8081, 1);
            var envelope = new Envelope(EnvelopeFlags.Guaranteed | EnvelopeFlags.Forwarded, 0, 0, MessageClasses.Event, 999, 0, 1, origin, target);
            byte[] buffer = new byte[Envelope.FixedSize + Envelope.GuaranteedExtraSize + NodeName.WireSize * 2];

            int written = envelope.WriteTo(buffer);
            Envelope back = Envelope.ReadFrom(buffer, out int consumed);

            int expectedSize = Envelope.FixedSize + Envelope.GuaranteedExtraSize + NodeName.WireSize * 2;
            Assert.Equal(expectedSize, written);
            Assert.Equal(expectedSize, consumed);
            Assert.True(back.IsGuaranteed);
            Assert.True(back.IsForwarded);
            Assert.Equal(envelope.GuaranteedId, back.GuaranteedId);
            Assert.Equal(origin, back.Origin);
            Assert.Equal(target, back.Target);
        }

        [Fact]
        public void WireSize_IncludesRelayExtensionOnlyWhenForwarded()
        {
            var relayFields = (Legacy(1, 2, 3), Legacy(4, 5, 6));
            var notForwarded = new Envelope(EnvelopeFlags.None, 1, 2, MessageClasses.Position);
            var forwarded = new Envelope(EnvelopeFlags.Forwarded, 0, 0, MessageClasses.Position, 0, 0, 0, relayFields.Item1, relayFields.Item2);
            var both = new Envelope(EnvelopeFlags.Guaranteed | EnvelopeFlags.Forwarded, 0, 0, MessageClasses.Event, 1, 0, 1, relayFields.Item1, relayFields.Item2);

            Assert.Equal(Envelope.FixedSize, notForwarded.WireSize);
            Assert.Equal(Envelope.FixedSize + NodeName.WireSize * 2, forwarded.WireSize);
            Assert.Equal(Envelope.FixedSize + Envelope.GuaranteedExtraSize + NodeName.WireSize * 2, both.WireSize);
        }

        [Fact]
        public void ReadFrom_ForwardedDatagramTooShortForExtension_Throws()
        {
            byte[] full = new byte[Envelope.FixedSize + NodeName.WireSize * 2];
            new Envelope(EnvelopeFlags.Forwarded, 0, 0, MessageClasses.Position, 0, 0, 0, Legacy(1, 2, 3), Legacy(4, 5, 6)).WriteTo(full);
            byte[] truncated = full[..(Envelope.FixedSize + NodeName.WireSize)];

            Assert.Throws<System.ArgumentException>(() => Envelope.ReadFrom(truncated, out _));
        }

        // A datagram this build must not read (docs/reference/jfp2-protocol.md §4.1, §4.2): another
        // ProtoMajor, or a flag bit outside SupportedFlags. TryReadFrom says so instead of throwing,
        // so the plugin can drop it quietly.

        static byte[] Datagram(EnvelopeFlags flags, byte messageClass)
        {
            byte[] datagram = new byte[Envelope.FixedSize + 4];
            new Envelope(flags & Envelope.SupportedFlags, 1, 2, messageClass).WriteTo(datagram);
            datagram[2] = (byte)flags;
            return datagram;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(255)]
        public void TryReadFrom_OtherProtoMajor_IsUnsupported(byte major)
        {
            byte[] datagram = Datagram(EnvelopeFlags.None, MessageClasses.Position);
            datagram[1] = major;

            Assert.False(Envelope.TryReadFrom(datagram, out _, out _, out string unsupported));
            Assert.Contains("ProtoMajor " + major, unsupported);
            Assert.Throws<System.InvalidOperationException>(() => Envelope.ReadFrom(datagram, out _));
        }

        /// <summary>A later major version need not have an 8-byte header: byte 1 alone decides.</summary>
        [Fact]
        public void TryReadFrom_OtherProtoMajor_ShorterThanOurHeader_IsUnsupportedNotTruncated()
        {
            Assert.False(Envelope.TryReadFrom(new byte[] { Envelope.Magic, 3 }, out _, out _, out string unsupported));
            Assert.Contains("ProtoMajor 3", unsupported);
        }

        [Theory]
        [InlineData(EnvelopeFlags.Coalesced)]
        [InlineData((EnvelopeFlags)(1 << 4))]
        [InlineData((EnvelopeFlags)(1 << 5))]
        [InlineData((EnvelopeFlags)(1 << 6))]
        [InlineData((EnvelopeFlags)(1 << 7))]
        [InlineData(EnvelopeFlags.Internal | (EnvelopeFlags)(1 << 7))]
        public void TryReadFrom_FlagThisBuildCannotRead_IsUnsupported(EnvelopeFlags flags)
        {
            byte[] datagram = Datagram(flags, MessageClasses.Position);

            Assert.False(Envelope.TryReadFrom(datagram, out _, out _, out string unsupported));
            Assert.Contains("flag bits", unsupported);
        }

        [Fact]
        public void SupportedFlags_AreExactlyGuaranteedForwardedAndInternal()
        {
            Assert.Equal(EnvelopeFlags.Guaranteed | EnvelopeFlags.Forwarded | EnvelopeFlags.Internal, Envelope.SupportedFlags);
        }

        /// <summary>The handshake travels as ProtoMajor 2 forever, whatever ProtoMajor becomes (§5.2).</summary>
        [Fact]
        public void Handshake_IsWrittenWithTheHandshakeProtoMajor()
        {
            Assert.Equal(2, Envelope.HandshakeProtoMajor);
            foreach (byte messageClass in new[] { MessageClasses.Hello, MessageClasses.HelloAck })
            {
                byte[] datagram = new byte[Envelope.FixedSize];
                new Envelope(EnvelopeFlags.Internal, 1, 0, messageClass).WriteTo(datagram);
                Assert.Equal(Envelope.HandshakeProtoMajor, datagram[1]);
                Assert.True(Envelope.TryReadFrom(datagram, out Envelope back, out _, out _));
                Assert.Equal(messageClass, back.RawMessageClass);
            }
        }

        [Fact]
        public void NonForwardedEnvelope_HasDefaultRelayFields()
        {
            var envelope = new Envelope(EnvelopeFlags.None, 1, 2, MessageClasses.Position);

            Assert.False(envelope.IsForwarded);
            Assert.Equal(default, envelope.Origin);
            Assert.Equal(default, envelope.Target);
        }
    }
}
