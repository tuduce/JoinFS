using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
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
        public void DefaultProfile_CarriesFlightPlanWithThisCodec()
        {
            ClassDescriptor<FlightPlanUpdate> messageClass = Jfp2Profile.Default.ForKind<FlightPlanUpdate>(MessageKind.FlightPlan);
            Assert.Equal(MessageClasses.FlightPlan, messageClass.MessageClass);
            Assert.IsType<FlightPlanV1Codec>(messageClass.Codec(1));
        }

        /// <summary>
        /// Every string is cut to its limit at a character boundary (docs/jfp2-wire-design.md §4.6), so
        /// with every field at its limit the message is the largest FlightPlanUpdate payload, 958 bytes.
        /// </summary>
        [Fact]
        public void LongStrings_AreCutToTheirLimits()
        {
            var codec = new FlightPlanV1Codec();
            static FlightPlanUpdate With(Func<int, string> text) => new()
            {
                ObjectId = 7,
                IcaoType = text(FlightPlanV1Codec.IcaoTypeLimit),
                Departure = text(FlightPlanV1Codec.DepartureLimit),
                Destination = text(FlightPlanV1Codec.DestinationLimit),
                Rules = text(FlightPlanV1Codec.RulesLimit),
                Route = text(FlightPlanV1Codec.RouteLimit),
                Remarks = text(FlightPlanV1Codec.RemarksLimit),
                Alternate = text(FlightPlanV1Codec.AlternateLimit),
                Speed = text(FlightPlanV1Codec.SpeedLimit),
                Altitude = text(FlightPlanV1Codec.AltitudeLimit),
                Callsign = text(FlightPlanV1Codec.CallsignLimit),
                Registration = text(FlightPlanV1Codec.RegistrationLimit),
                IcaoAirline = text(FlightPlanV1Codec.IcaoAirlineLimit),
                FlightNumber = text(FlightPlanV1Codec.FlightNumberLimit),
            };
            byte[] buffer = new byte[4096];

            Assert.Equal(958, FlightPlanV1Codec.MaxSize);
            Assert.Equal(FlightPlanV1Codec.MaxSize, codec.Encode(With(LongText.Ascii), buffer));

            FlightPlanUpdate sent = With(LongText.Over);
            int written = codec.Encode(sent, buffer);
            FlightPlanUpdate back = codec.Decode(buffer.AsSpan(0, written));

            LongText.AssertCut(sent.IcaoType, FlightPlanV1Codec.IcaoTypeLimit, back.IcaoType);
            LongText.AssertCut(sent.Departure, FlightPlanV1Codec.DepartureLimit, back.Departure);
            LongText.AssertCut(sent.Destination, FlightPlanV1Codec.DestinationLimit, back.Destination);
            LongText.AssertCut(sent.Rules, FlightPlanV1Codec.RulesLimit, back.Rules);
            LongText.AssertCut(sent.Route, FlightPlanV1Codec.RouteLimit, back.Route);
            LongText.AssertCut(sent.Remarks, FlightPlanV1Codec.RemarksLimit, back.Remarks);
            LongText.AssertCut(sent.Alternate, FlightPlanV1Codec.AlternateLimit, back.Alternate);
            LongText.AssertCut(sent.Speed, FlightPlanV1Codec.SpeedLimit, back.Speed);
            LongText.AssertCut(sent.Altitude, FlightPlanV1Codec.AltitudeLimit, back.Altitude);
            LongText.AssertCut(sent.Callsign, FlightPlanV1Codec.CallsignLimit, back.Callsign);
            LongText.AssertCut(sent.Registration, FlightPlanV1Codec.RegistrationLimit, back.Registration);
            LongText.AssertCut(sent.IcaoAirline, FlightPlanV1Codec.IcaoAirlineLimit, back.IcaoAirline);
            LongText.AssertCut(sent.FlightNumber, FlightPlanV1Codec.FlightNumberLimit, back.FlightNumber);
        }
    }
}
