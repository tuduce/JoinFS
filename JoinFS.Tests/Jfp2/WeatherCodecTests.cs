using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Weather/WeatherReply codecs (docs/protocol-v2-implementation-
    // plan.md Phase 5). WeatherReplyV1Codec and WeatherUpdateV1Codec share one wire
    // shape but are independently negotiated classes (Weather vs WeatherReply) - covered separately
    // so a change to one class's negotiation can never silently mask a break in the other.
    public class WeatherCodecTests
    {
        const string SampleMetar = "METAR KSEA 131753Z 18010KT 10SM FEW250 22/12 A2992";

        [Fact]
        public void WeatherUpdate_RoundTrips()
        {
            var codec = new WeatherUpdateV1Codec();
            var report = new WeatherUpdate { Metar = SampleMetar };

            Span<byte> buffer = stackalloc byte[512];
            int written = codec.Encode(report, buffer);
            WeatherUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(SampleMetar, back.Metar);
        }

        [Fact]
        public void WeatherReply_RoundTrips()
        {
            var codec = new WeatherReplyV1Codec();
            var report = new WeatherReply { Metar = SampleMetar };

            Span<byte> buffer = stackalloc byte[512];
            int written = codec.Encode(report, buffer);
            WeatherReply back = codec.Decode(buffer[..written]);

            Assert.Equal(SampleMetar, back.Metar);
        }

        [Fact]
        public void EmptyMetar_RoundTrips()
        {
            var codec = new WeatherUpdateV1Codec();
            var report = new WeatherUpdate { Metar = "" };

            Span<byte> buffer = stackalloc byte[512];
            int written = codec.Encode(report, buffer);
            WeatherUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal("", back.Metar);
        }

        [Fact]
        public void DefaultProfile_CarriesBothWeatherClassesIndependently()
        {
            ClassDescriptor<WeatherUpdate> update = Jfp2Profile.Default.ForKind<WeatherUpdate>(MessageKind.WeatherUpdate);
            ClassDescriptor<WeatherReply> reply = Jfp2Profile.Default.ForKind<WeatherReply>(MessageKind.WeatherReply);

            Assert.Equal(MessageClasses.Weather, update.MessageClass);
            Assert.Equal(MessageClasses.WeatherReply, reply.MessageClass);
            Assert.IsType<WeatherUpdateV1Codec>(update.Codec(1));
            Assert.IsType<WeatherReplyV1Codec>(reply.Codec(1));
            Assert.NotEqual(update.Guaranteed, reply.Guaranteed);
        }
    }
}
