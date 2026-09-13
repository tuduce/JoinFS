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
    }
}
