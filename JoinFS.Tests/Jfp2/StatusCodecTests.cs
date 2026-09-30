using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the first real application-partition codecs
    // (docs/protocol-v2-implementation-plan.md Phase 2, docs/reference/jfp2-protocol.md §6.4's "remaining
    // message classes" - Status is a mechanical one-to-one port with no new wire shape).
    public class StatusCodecTests
    {
        [Fact]
        public void StatusRequestV1_RoundTrips()
        {
            var codec = new StatusRequestV1Codec();
            var request = new StatusRequestUpdate { HubEnabled = true, HubListRequested = true, Uuid = 0xDEADBEEFu };

            Span<byte> buffer = stackalloc byte[StatusRequestV1Codec.Size];
            int written = codec.Encode(request, buffer);
            StatusRequestUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(StatusRequestV1Codec.Size, written);
            Assert.Equal(request.HubEnabled, back.HubEnabled);
            Assert.Equal(request.HubListRequested, back.HubListRequested);
            Assert.Equal(request.Uuid, back.Uuid);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void StatusRequestV1_FlagCombinations_RoundTrip(bool hubEnabled, bool hubListRequested)
        {
            var codec = new StatusRequestV1Codec();
            var request = new StatusRequestUpdate { HubEnabled = hubEnabled, HubListRequested = hubListRequested, Uuid = 42 };

            Span<byte> buffer = stackalloc byte[StatusRequestV1Codec.Size];
            int written = codec.Encode(request, buffer);
            StatusRequestUpdate back = codec.Decode(buffer[..written]);

            Assert.Equal(hubEnabled, back.HubEnabled);
            Assert.Equal(hubListRequested, back.HubListRequested);
        }

        [Fact]
        public void StatusV1_NonHubRoundTrips()
        {
            var codec = new StatusV1Codec();
            var status = new StatusUpdate
            {
                Guid = Guid.NewGuid(),
                AppVersion = "26.6.0",
                Users = 3,
                AtcCount = 0,
                AtcAirport = "",
                AtcLevel = 2,
                Planes = 1,
                Helicopters = 0,
                Boats = 0,
                Vehicles = 0,
                HubEnabled = false,
            };

            byte[] buffer = new byte[512];
            int written = codec.Encode(status, buffer);
            StatusUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(status.Guid, back.Guid);
            Assert.Equal(status.AppVersion, back.AppVersion);
            Assert.Equal(status.Users, back.Users);
            Assert.Equal(status.AtcCount, back.AtcCount);
            Assert.Equal(status.Planes, back.Planes);
            Assert.False(back.HubEnabled);
        }

        [Fact]
        public void StatusV1_HubRoundTrips_WithAllHubFields()
        {
            var codec = new StatusV1Codec();
            var status = new StatusUpdate
            {
                Guid = Guid.NewGuid(),
                AppVersion = "26.6.0",
                Users = 12,
                AtcCount = 1,
                AtcAirport = "KSEA",
                AtcLevel = 3,
                Planes = 5,
                Helicopters = 1,
                Boats = 0,
                Vehicles = 2,
                HubEnabled = true,
                Address = "joinfs.famtuduce.com",
                Name = "CriCri Aviation",
                About = "A community JoinFS hub",
                Voip = "https://voip.example/hub",
                NextEvent = "Fly-in this Saturday",
                Airport = "KSEA",
                ActivityCircle = 40,
                GlobalSession = true,
                PasswordRequired = true,
            };

            byte[] buffer = new byte[512];
            int written = codec.Encode(status, buffer);
            StatusUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(status.Guid, back.Guid);
            Assert.Equal(status.AppVersion, back.AppVersion);
            Assert.Equal(status.AtcAirport, back.AtcAirport);
            Assert.Equal(status.AtcLevel, back.AtcLevel);
            Assert.True(back.HubEnabled);
            Assert.Equal(status.Address, back.Address);
            Assert.Equal(status.Name, back.Name);
            Assert.Equal(status.About, back.About);
            Assert.Equal(status.Voip, back.Voip);
            Assert.Equal(status.NextEvent, back.NextEvent);
            Assert.Equal(status.Airport, back.Airport);
            Assert.Equal(status.ActivityCircle, back.ActivityCircle);
            Assert.True(back.GlobalSession);
            Assert.True(back.PasswordRequired);
        }

        [Fact]
        public void StatusV1_EmptyStrings_RoundTrip()
        {
            // Every hub-block field can legitimately be blank (e.g. no "about" text configured) -
            // make sure the zero-length string path doesn't desync the rest of the message.
            var codec = new StatusV1Codec();
            var status = new StatusUpdate
            {
                Guid = Guid.Empty,
                AppVersion = "",
                HubEnabled = true,
                Address = "",
                Name = "",
                About = "",
                Voip = "",
                NextEvent = "",
                Airport = "",
            };

            byte[] buffer = new byte[512];
            int written = codec.Encode(status, buffer);
            StatusUpdate back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal("", back.AppVersion);
            Assert.Equal("", back.Address);
            Assert.Equal("", back.Name);
        }

        [Fact]
        public void CodecRegistry_ResolvesRegisteredStatusCodecs()
        {
            CodecRegistry.Register(new StatusRequestV1Codec());
            CodecRegistry.Register(new StatusV1Codec());

            ICodec<StatusRequestUpdate> requestCodec = CodecRegistry.Resolve<StatusRequestUpdate>(MessageClasses.StatusRequest, 1);
            ICodec<StatusUpdate> statusCodec = CodecRegistry.Resolve<StatusUpdate>(MessageClasses.Status, 1);

            Assert.Equal(MessageClasses.StatusRequest, requestCodec.MessageClass);
            Assert.Equal(MessageClasses.Status, statusCodec.MessageClass);
        }

        [Fact]
        public void CodecRegistry_UnregisteredVersion_Throws()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() =>
                CodecRegistry.Resolve<StatusUpdate>(MessageClasses.Status, 99));
        }

        [Fact]
        public void StatusAndStatusRequest_UseDistinctMessageClasses()
        {
            // docs/reference/jfp2-protocol.md's catalog only reserved one slot ("Status") for this
            // exchange; Phase 2 appended StatusRequest at the next free slot rather than overloading
            // one class - assert the two never collide.
            Assert.NotEqual(MessageClasses.Status, MessageClasses.StatusRequest);
        }
    }
}
