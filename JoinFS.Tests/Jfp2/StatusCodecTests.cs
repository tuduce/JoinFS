using System;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // Round-trip coverage for the first real application-partition codecs
    // (docs/jfp2/history/protocol-v2-implementation-plan.md Phase 2, docs/jfp2/protocol.md §9.4's "remaining
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
        public void DefaultProfile_CarriesBothStatusClassesWithTheseCodecs()
        {
            ClassDescriptor<StatusRequestUpdate> request = Jfp2Profile.Default.ForKind<StatusRequestUpdate>(MessageKind.StatusRequest);
            ClassDescriptor<StatusUpdate> status = Jfp2Profile.Default.ForKind<StatusUpdate>(MessageKind.Status);

            Assert.Equal(MessageClasses.StatusRequest, request.MessageClass);
            Assert.Equal(MessageClasses.Status, status.MessageClass);
            Assert.IsType<StatusRequestV1Codec>(request.Codec(1));
            Assert.IsType<StatusV1Codec>(status.Codec(1));
        }

        [Fact]
        public void DefaultProfile_StatusSpeaksVersion1Only()
        {
            ClassDescriptor status = Jfp2Profile.Default.ForKind(MessageKind.Status);
            Assert.Equal(1, status.MinVersion);
            Assert.Equal(1, status.MaxVersion);
        }

        [Fact]
        public void StatusAndStatusRequest_UseDistinctMessageClasses()
        {
            // docs/jfp2/protocol.md's catalog only reserved one slot ("Status") for this
            // exchange; Phase 2 appended StatusRequest at the next free slot rather than overloading
            // one class - assert the two never collide.
            Assert.NotEqual(MessageClasses.Status, MessageClasses.StatusRequest);
        }

        /// <summary>
        /// Every string is cut to its limit at a character boundary (docs/jfp2/protocol.md §9.7), so
        /// with every field at its limit the message is the largest StatusUpdate payload, 1058 bytes.
        /// </summary>
        [Fact]
        public void LongStrings_AreCutToTheirLimits()
        {
            var codec = new StatusV1Codec();
            static StatusUpdate With(Func<int, string> text) => new()
            {
                HubEnabled = true,
                AppVersion = text(StatusV1Codec.AppVersionLimit),
                AtcAirport = text(StatusV1Codec.AtcAirportLimit),
                Address = text(StatusV1Codec.AddressLimit),
                Name = text(StatusV1Codec.NameLimit),
                About = text(StatusV1Codec.AboutLimit),
                Voip = text(StatusV1Codec.VoipLimit),
                NextEvent = text(StatusV1Codec.NextEventLimit),
                Airport = text(StatusV1Codec.AirportLimit),
            };
            byte[] buffer = new byte[4096];

            Assert.Equal(1058, StatusV1Codec.MaxSize);
            Assert.Equal(StatusV1Codec.MaxSize, codec.Encode(With(LongText.Ascii), buffer));

            StatusUpdate sent = With(LongText.Over);
            int written = codec.Encode(sent, buffer);
            StatusUpdate back = codec.Decode(buffer.AsSpan(0, written));

            LongText.AssertCut(sent.AppVersion, StatusV1Codec.AppVersionLimit, back.AppVersion);
            LongText.AssertCut(sent.AtcAirport, StatusV1Codec.AtcAirportLimit, back.AtcAirport);
            LongText.AssertCut(sent.Address, StatusV1Codec.AddressLimit, back.Address);
            LongText.AssertCut(sent.Name, StatusV1Codec.NameLimit, back.Name);
            LongText.AssertCut(sent.About, StatusV1Codec.AboutLimit, back.About);
            LongText.AssertCut(sent.Voip, StatusV1Codec.VoipLimit, back.Voip);
            LongText.AssertCut(sent.NextEvent, StatusV1Codec.NextEventLimit, back.NextEvent);
            LongText.AssertCut(sent.Airport, StatusV1Codec.AirportLimit, back.Airport);
        }
    }
}
