using System;
using JoinFS.Jfp2;
using JoinFS.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Event codec (docs/protocol-v2-implementation-plan.md Phase 5).
    public class EventCodecTests
    {
        static EventUpdate Sample() => new()
        {
            ObjectId = 42,
            EventId = 1001,
            Data = 12345,
        };

        [Fact]
        public void RoundTrips_AllFields()
        {
            var codec = new EventV1Codec();
            EventUpdate update = Sample();

            Span<byte> buffer = stackalloc byte[EventV1Codec.Size];
            int written = codec.Encode(update, buffer);
            EventUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(EventV1Codec.Size, written);
            Assert.Equal(update.ObjectId, back.ObjectId);
            Assert.Equal(update.EventId, back.EventId);
            Assert.Equal(update.Data, back.Data);
        }

        [Fact]
        public void SharedCockpitSentinel_ObjectIdMaxValue_RoundTrips()
        {
            var codec = new EventV1Codec();
            EventUpdate update = Sample();
            update.ObjectId = uint.MaxValue;

            Span<byte> buffer = stackalloc byte[EventV1Codec.Size];
            int written = codec.Encode(update, buffer);
            EventUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(uint.MaxValue, back.ObjectId);
        }

        [Fact]
        public void CodecRegistry_ResolvesEventCodec()
        {
            CodecRegistry.Register(new EventV1Codec());
            ICodec<EventUpdate> codec = CodecRegistry.Resolve<EventUpdate>(MessageClasses.Event, 1);
            Assert.Equal(MessageClasses.Event, codec.MessageClass);
        }

        [Fact]
        public void FixedSize_MatchesEncodedLength()
        {
            var codec = new EventV1Codec();
            byte[] buffer = new byte[64];
            int written = codec.Encode(Sample(), buffer);
            Assert.Equal(EventV1Codec.Size, written);
        }
    }
}
