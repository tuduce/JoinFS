using System;
using JoinFS.Jfp2;
using JoinFS.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Weather/WeatherReply codecs (docs/protocol-v2-implementation-
    // plan.md Phase 5). WeatherReplyV1Codec and WeatherUpdateV1Codec share the WeatherReport wire
    // shape but are independently negotiated classes (Weather vs WeatherReply) - covered separately
    // so a change to one class's negotiation can never silently mask a break in the other.
    public class WeatherCodecTests
    {
        const string SampleMetar = "METAR KSEA 131753Z 18010KT 10SM FEW250 22/12 A2992";

        [Fact]
        public void WeatherUpdate_RoundTrips()
        {
            var codec = new WeatherUpdateV1Codec();
            var report = new WeatherReport { Metar = SampleMetar };

            Span<byte> buffer = stackalloc byte[512];
            int written = codec.Encode(report, buffer);
            WeatherReport back = codec.Decode(buffer[..written]);

            Assert.Equal(SampleMetar, back.Metar);
        }

        [Fact]
        public void WeatherReply_RoundTrips()
        {
            var codec = new WeatherReplyV1Codec();
            var report = new WeatherReport { Metar = SampleMetar };

            Span<byte> buffer = stackalloc byte[512];
            int written = codec.Encode(report, buffer);
            WeatherReport back = codec.Decode(buffer[..written]);

            Assert.Equal(SampleMetar, back.Metar);
        }

        [Fact]
        public void EmptyMetar_RoundTrips()
        {
            var codec = new WeatherUpdateV1Codec();
            var report = new WeatherReport { Metar = "" };

            Span<byte> buffer = stackalloc byte[512];
            int written = codec.Encode(report, buffer);
            WeatherReport back = codec.Decode(buffer[..written]);

            Assert.Equal("", back.Metar);
        }

        [Fact]
        public void CodecRegistry_ResolvesBothWeatherClassesIndependently()
        {
            CodecRegistry.Register(new WeatherUpdateV1Codec());
            CodecRegistry.Register(new WeatherReplyV1Codec());

            ICodec<WeatherReport> updateCodec = CodecRegistry.Resolve<WeatherReport>(MessageClasses.Weather, 1);
            ICodec<WeatherReport> replyCodec = CodecRegistry.Resolve<WeatherReport>(MessageClasses.WeatherReply, 1);

            Assert.Equal(MessageClasses.Weather, updateCodec.MessageClass);
            Assert.Equal(MessageClasses.WeatherReply, replyCodec.MessageClass);
            Assert.NotEqual(updateCodec.MessageClass, replyCodec.MessageClass);
        }
    }
}
