using System;
using JoinFS.Jfp2;
using JoinFS.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the FlightPlan codec (docs/protocol-v2-implementation-plan.md Phase 5).
    public class FlightPlanCodecTests
    {
        static FlightPlanUpdate Sample() => new()
        {
            ObjectId = 7,
            IcaoType = "A320",
            Departure = "KSEA",
            Destination = "KPDX",
            Rules = "I",
            Route = "DIRECT",
            Remarks = "TEST FLIGHT",
            Alternate = "KBFI",
            Speed = "0250",
            Altitude = "0350",
            Callsign = "JFS123",
            Registration = "N12345",
            IcaoAirline = "JFS",
            FlightNumber = "1234",
        };

        [Fact]
        public void RoundTrips_AllFields()
        {
            var codec = new FlightPlanV1Codec();
            FlightPlanUpdate update = Sample();

            Span<byte> buffer = stackalloc byte[1024];
            int written = codec.Encode(update, buffer);
            FlightPlanUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(update.ObjectId, back.ObjectId);
            Assert.Equal(update.IcaoType, back.IcaoType);
            Assert.Equal(update.Departure, back.Departure);
            Assert.Equal(update.Destination, back.Destination);
            Assert.Equal(update.Rules, back.Rules);
            Assert.Equal(update.Route, back.Route);
            Assert.Equal(update.Remarks, back.Remarks);
            Assert.Equal(update.Alternate, back.Alternate);
            Assert.Equal(update.Speed, back.Speed);
            Assert.Equal(update.Altitude, back.Altitude);
            Assert.Equal(update.Callsign, back.Callsign);
            Assert.Equal(update.Registration, back.Registration);
            Assert.Equal(update.IcaoAirline, back.IcaoAirline);
            Assert.Equal(update.FlightNumber, back.FlightNumber);
        }

        [Fact]
        public void EmptyStringFields_RoundTrip()
        {
            var codec = new FlightPlanV1Codec();
            var update = new FlightPlanUpdate
            {
                ObjectId = 1,
                IcaoType = "",
                Departure = "",
                Destination = "",
                Rules = "",
                Route = "",
                Remarks = "",
                Alternate = "",
                Speed = "",
                Altitude = "",
                Callsign = "",
                Registration = "",
                IcaoAirline = "",
                FlightNumber = "",
            };

            Span<byte> buffer = stackalloc byte[1024];
            int written = codec.Encode(update, buffer);
            FlightPlanUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal("", back.Route);
            Assert.Equal("", back.FlightNumber);
        }

        [Fact]
        public void CodecRegistry_ResolvesFlightPlanCodec()
        {
            CodecRegistry.Register(new FlightPlanV1Codec());
            ICodec<FlightPlanUpdate> codec = CodecRegistry.Resolve<FlightPlanUpdate>(MessageClasses.FlightPlan, 1);
            Assert.Equal(MessageClasses.FlightPlan, codec.MessageClass);
        }
    }
}
