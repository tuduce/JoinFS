using JoinFS.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Formalizes the envelope round-trip and legacy/JFP2 discrimination behavior specified in
    // docs/protocol-v2-design.md §3 (coexistence via the magic byte) and §4.1 (the fixed envelope),
    // now run against the code ported into JoinFS/Jfp2/Envelope.cs
    // (docs/protocol-v2-implementation-plan.md Phase 0/1).
    public class EnvelopeTests
    {
        [Fact]
        public void WriteTo_ReadFrom_RoundTrips()
        {
            var envelope = new Envelope(EnvelopeFlags.Coalesced, 1001, 2002, MessageClasses.Position);
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
            // The whole coexistence strategy (docs/protocol-v2-design.md §3) rests on this never
            // being equal - assert it directly so a future edit to either constant fails loudly.
            Assert.NotEqual(Envelope.Magic, (byte)(Envelope.LegacyVersionConstant & 0xFF));
        }

        [Fact]
        public void FixedSize_Is8Bytes()
        {
            // The 62%-smaller-than-legacy claim in docs/protocol-v2-design.md §4.1 depends on this.
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
    }
}
