using System;
using JoinFS.Jfp2;
using JoinFS.Jfp2.Codecs;
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
        public void CodecRegistry_ResolvesIdentityCodec()
        {
            CodecRegistry.Register(new IdentityV1Codec());
            ICodec<IdentityUpdate> codec = CodecRegistry.Resolve<IdentityUpdate>(MessageClasses.Identity, 1);
            Assert.Equal(MessageClasses.Identity, codec.MessageClass);
        }
    }
}
