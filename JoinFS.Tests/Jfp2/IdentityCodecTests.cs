using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the Identity codec (docs/protocol-v2-implementation-plan.md Phase 3).
    public class IdentityCodecTests
    {
        static IdentityUpdate SampleAircraft() => new()
        {
            ObjectId = 42,
            IsAircraft = true,
            IsPlane = true,
            Callsign = "JFS123",
            Model = "A320",
            Livery = "generic",
            IcaoType = "A320",
            IcaoAirline = "JFS",
            Registration = "N12345",
            FlightNumber = "1234",
            ClassCode = "L2J",
            Wtc = "M",
            ClassCodeConfirmed = true,
            TypeRole = 3,
        };

        [Fact]
        public void AircraftIdentity_RoundTrips()
        {
            var codec = new IdentityV1Codec();
            IdentityUpdate identity = SampleAircraft();

            byte[] buffer = new byte[256];
            int written = codec.Encode(identity, buffer);
            IdentityUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(identity.ObjectId, back.ObjectId);
            Assert.True(back.IsAircraft);
            Assert.True(back.IsPlane);
            Assert.Equal(identity.Callsign, back.Callsign);
            Assert.Equal(identity.Model, back.Model);
            Assert.Equal(identity.Livery, back.Livery);
            Assert.Equal(identity.IcaoType, back.IcaoType);
            Assert.Equal(identity.IcaoAirline, back.IcaoAirline);
            Assert.Equal(identity.Registration, back.Registration);
            Assert.Equal(identity.FlightNumber, back.FlightNumber);
            Assert.Equal(identity.ClassCode, back.ClassCode);
            Assert.Equal(identity.Wtc, back.Wtc);
            Assert.True(back.ClassCodeConfirmed);
            Assert.Equal(identity.TypeRole, back.TypeRole);
        }

        [Fact]
        public void NonAircraftObjectIdentity_EmptyAircraftFields_RoundTrips()
        {
            // Matches the legacy write path's own quirk (WriteObjectPositionVelocityMessage): a plain
            // (non-Aircraft) Obj never carries callsign/icaoType/icaoAirline/registration - see
            // Network.BuildJfp2IdentityUpdate.
            var codec = new IdentityV1Codec();
            var identity = new IdentityUpdate
            {
                ObjectId = 7,
                IsAircraft = false,
                IsPlane = false,
                Callsign = "",
                Model = "GroundVehicle01",
                Livery = "",
                IcaoType = "",
                IcaoAirline = "",
                Registration = "",
                ClassCode = "",
                Wtc = "",
                ClassCodeConfirmed = false,
                TypeRole = 0,
            };

            byte[] buffer = new byte[256];
            int written = codec.Encode(identity, buffer);
            IdentityUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.False(back.IsAircraft);
            Assert.Equal("", back.Callsign);
            Assert.Equal("", back.IcaoType);
            Assert.Equal(identity.Model, back.Model);
        }

        [Fact]
        public void ObjectId_UsesFullUintRange()
        {
            // Obj.netId (JoinFS/Sim.cs) is a real uint (a raw SimConnect object id), so a value above
            // ushort.MaxValue must survive intact - see the widening deviation note in
            // docs/protocol-v2-implementation-plan.md's Phase 3 writeup.
            var codec = new IdentityV1Codec();
            var identity = new IdentityUpdate { ObjectId = 0xFFFFFFF0u, Callsign = "", Model = "", Livery = "", IcaoType = "", IcaoAirline = "", Registration = "", ClassCode = "", Wtc = "" };

            byte[] buffer = new byte[256];
            int written = codec.Encode(identity, buffer);
            IdentityUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(0xFFFFFFF0u, back.ObjectId);
        }

        [Fact]
        public void DefaultProfile_CarriesIdentityWithThisCodec()
        {
            ClassDescriptor<IdentityUpdate> messageClass = Jfp2Profile.Default.ForKind<IdentityUpdate>(MessageKind.Identity);
            Assert.Equal(MessageClasses.Identity, messageClass.MessageClass);
            Assert.IsType<IdentityV1Codec>(messageClass.Codec(1));
        }

        /// <summary>
        /// Every string is cut to its limit at a character boundary (docs/jfp2-wire-design.md §4.6), so
        /// with every field at its limit the message is the largest IdentityUpdate payload, 656 bytes.
        /// </summary>
        [Fact]
        public void LongStrings_AreCutToTheirLimits()
        {
            var codec = new IdentityV1Codec();
            static IdentityUpdate With(Func<int, string> text) => new()
            {
                ObjectId = 42,
                Callsign = text(IdentityV1Codec.CallsignLimit),
                Model = text(IdentityV1Codec.ModelLimit),
                Livery = text(IdentityV1Codec.LiveryLimit),
                IcaoType = text(IdentityV1Codec.IcaoTypeLimit),
                IcaoAirline = text(IdentityV1Codec.IcaoAirlineLimit),
                Registration = text(IdentityV1Codec.RegistrationLimit),
                FlightNumber = text(IdentityV1Codec.FlightNumberLimit),
                ClassCode = text(IdentityV1Codec.ClassCodeLimit),
                Wtc = text(IdentityV1Codec.WtcLimit),
            };
            byte[] buffer = new byte[4096];

            Assert.Equal(656, IdentityV1Codec.MaxSize);
            Assert.Equal(IdentityV1Codec.MaxSize, codec.Encode(With(LongText.Ascii), buffer));

            IdentityUpdate sent = With(LongText.Over);
            int written = codec.Encode(sent, buffer);
            IdentityUpdate back = codec.Decode(buffer.AsSpan(0, written));

            LongText.AssertCut(sent.Callsign, IdentityV1Codec.CallsignLimit, back.Callsign);
            LongText.AssertCut(sent.Model, IdentityV1Codec.ModelLimit, back.Model);
            LongText.AssertCut(sent.Livery, IdentityV1Codec.LiveryLimit, back.Livery);
            LongText.AssertCut(sent.IcaoType, IdentityV1Codec.IcaoTypeLimit, back.IcaoType);
            LongText.AssertCut(sent.IcaoAirline, IdentityV1Codec.IcaoAirlineLimit, back.IcaoAirline);
            LongText.AssertCut(sent.Registration, IdentityV1Codec.RegistrationLimit, back.Registration);
            LongText.AssertCut(sent.FlightNumber, IdentityV1Codec.FlightNumberLimit, back.FlightNumber);
            LongText.AssertCut(sent.ClassCode, IdentityV1Codec.ClassCodeLimit, back.ClassCode);
            LongText.AssertCut(sent.Wtc, IdentityV1Codec.WtcLimit, back.Wtc);
        }
    }
}
