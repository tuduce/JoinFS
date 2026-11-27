using System;
using System.Linq;
using System.Text;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    // The codecs of AtcStation (23) and AtcRelay (24): docs/network/jfp2/protocol.md §9.9, plan stage 6 of atc-fsd.
    public class AtcCodecTests
    {
        static AtcStation Station() => new()
        {
            Slot = 1, Online = true, Facility = 5, Rating = 8, Visibility = 150, Frequency = 118_700,
            Latitude = 48.353783, Longitude = 11.786086, Callsign = "EDDM_N_APP", Name = "Carla Controller",
        };

        [Fact]
        public void AtcStation_RoundTrips_AllFields()
        {
            var codec = new AtcStationV1Codec();
            byte[] buffer = new byte[AtcStationV1Codec.MaxSize];
            int written = codec.Encode(Station(), buffer);
            AtcStation back = codec.Decode(buffer.AsSpan(0, written));

            Assert.Equal(Station().Callsign, back.Callsign);
            Assert.Equal(Station().Name, back.Name);
            Assert.Equal((1, true, 5, 8, 150, 118_700u), ((int)back.Slot, back.Online, (int)back.Facility, (int)back.Rating, (int)back.Visibility, back.Frequency));
            Assert.Equal((48.353783, 11.786086), (back.Latitude, back.Longitude));
        }

        [Fact]
        public void AtcStation_Offline_AndEmptyStrings_RoundTrip()
        {
            var codec = new AtcStationV1Codec();
            byte[] buffer = new byte[AtcStationV1Codec.MaxSize];
            var logoff = new AtcStation { Slot = 0, Online = false, Callsign = "EDDM_TWR", Name = "" };
            AtcStation back = codec.Decode(buffer.AsSpan(0, codec.Encode(logoff, buffer)));

            Assert.False(back.Online);
            Assert.Equal("EDDM_TWR", back.Callsign);
            Assert.Equal("", back.Name);
            Assert.Equal(0u, back.Frequency);
        }

        /// <summary>A frequency above 132.767 MHz survives: it is u32 kHz here, where legacy's PeerInfo field is a signed 16 bit one (plan R8).</summary>
        [Fact]
        public void AtcStation_CarriesFrequenciesAbovePeerInfosRange()
        {
            var codec = new AtcStationV1Codec();
            byte[] buffer = new byte[AtcStationV1Codec.MaxSize];
            var station = Station();
            station.Frequency = 136_975;
            Assert.Equal(136_975u, codec.Decode(buffer.AsSpan(0, codec.Encode(station, buffer))).Frequency);
        }

        /// <summary>The bytes of one station: pinned, so that the layout of §9.9 cannot move unnoticed.</summary>
        [Fact]
        public void AtcStation_HasTheLayoutOfTheSpecification()
        {
            var codec = new AtcStationV1Codec();
            byte[] buffer = new byte[AtcStationV1Codec.MaxSize];
            var station = new AtcStation
            {
                Slot = 1, Online = true, Facility = 4, Rating = 9, Visibility = 0x0102, Frequency = 118_380 /* 0x0001CE6C */,
                Latitude = 1.5, Longitude = -2.0, Callsign = "EDDM_TWR", Name = "Al",
            };
            int written = codec.Encode(station, buffer);

            Assert.Equal(
                "01 01 04 09 0201 6CCE0100 000000000000F83F 00000000000000C0 0800 4544444D5F545752 0200 416C".Replace(" ", ""),
                Convert.ToHexString(buffer.AsSpan(0, written)));
        }

        /// <summary>Only bit 0 of Flags is assigned; a node of a later build may set others, which are not an error (§10.1).</summary>
        [Fact]
        public void AtcStation_IgnoresUnassignedFlagBits()
        {
            var codec = new AtcStationV1Codec();
            byte[] buffer = new byte[AtcStationV1Codec.MaxSize];
            int written = codec.Encode(Station(), buffer);
            buffer[1] = 0xFE; // Online clear, every other bit set
            Assert.False(codec.Decode(buffer.AsSpan(0, written)).Online);
            buffer[1] = 0xFF;
            Assert.True(codec.Decode(buffer.AsSpan(0, written)).Online);
        }

        [Fact]
        public void AtcStation_LongStrings_AreCutToTheirLimits()
        {
            var codec = new AtcStationV1Codec();
            static AtcStation With(Func<int, string> text) => new()
            {
                Online = true, Callsign = text(AtcStationV1Codec.CallsignLimit), Name = text(AtcStationV1Codec.NameLimit),
            };
            byte[] buffer = new byte[4096];

            Assert.Equal(74, AtcStationV1Codec.MaxSize);
            Assert.Equal(AtcStationV1Codec.MaxSize, codec.Encode(With(LongText.Ascii), buffer));

            AtcStation sent = With(LongText.Over);
            AtcStation back = codec.Decode(buffer.AsSpan(0, codec.Encode(sent, buffer)));

            LongText.AssertCut(sent.Callsign, AtcStationV1Codec.CallsignLimit, back.Callsign);
            LongText.AssertCut(sent.Name, AtcStationV1Codec.NameLimit, back.Name);
        }

        [Fact]
        public void AtcRelay_RoundTrips_ALine()
        {
            var codec = new AtcRelayV1Codec();
            byte[] buffer = new byte[AtcRelayV1Codec.MaxSize];
            const string line = "$HOEDDM_TWR:EDDM_APP:DLH4TK";
            int written = codec.Encode(new AtcRelay { Line = line }, buffer);

            Assert.Equal(2 + line.Length, written);
            Assert.Equal(line, codec.Decode(buffer.AsSpan(0, written)).Line);
        }

        [Fact]
        public void AtcRelay_KeepsColonsAndNonAsciiText()
        {
            var codec = new AtcRelayV1Codec();
            byte[] buffer = new byte[AtcRelayV1Codec.MaxSize];
            const string line = "#TMEDDM_TWR:@49999:Grüße: hold at 5000 ✈";
            Assert.Equal(line, codec.Decode(buffer.AsSpan(0, codec.Encode(new AtcRelay { Line = line }, buffer))).Line);
        }

        [Fact]
        public void AtcRelay_LongLine_IsCutToItsLimit()
        {
            var codec = new AtcRelayV1Codec();
            byte[] buffer = new byte[4096];

            Assert.Equal(1002, AtcRelayV1Codec.MaxSize);
            Assert.Equal(AtcRelayV1Codec.MaxSize, codec.Encode(new AtcRelay { Line = LongText.Ascii(AtcRelayV1Codec.LineLimit) }, buffer));

            var sent = new AtcRelay { Line = LongText.Over(AtcRelayV1Codec.LineLimit) };
            AtcRelay back = codec.Decode(buffer.AsSpan(0, codec.Encode(sent, buffer)));
            LongText.AssertCut(sent.Line, AtcRelayV1Codec.LineLimit, back.Line);
            Assert.True(Encoding.UTF8.GetByteCount(back.Line) <= AtcRelayV1Codec.LineLimit);
        }

        [Fact]
        public void DefaultProfile_CarriesTheAtcClassesWithTheseCodecs_LastAndAsPlainClasses()
        {
            ClassDescriptor<AtcStation> station = Jfp2Profile.Default.ForKind<AtcStation>(MessageKind.AtcStation);
            ClassDescriptor<AtcRelay> relay = Jfp2Profile.Default.ForKind<AtcRelay>(MessageKind.AtcRelay);

            Assert.Equal((MessageClasses.AtcStation, false, true), (station.MessageClass, station.Guaranteed, station.SentAsIs));
            Assert.Equal((MessageClasses.AtcRelay, true, true), (relay.MessageClass, relay.Guaranteed, relay.SentAsIs));
            Assert.IsType<AtcStationV1Codec>(station.Codec(1));
            Assert.IsType<AtcRelayV1Codec>(relay.Codec(1));
            Assert.Equal((23, 24), (MessageClasses.AtcStation, MessageClasses.AtcRelay));
            Assert.Equal([MessageClasses.AtcStation, MessageClasses.AtcRelay], Jfp2Profile.Default.Classes.TakeLast(2).Select(c => c.MessageClass));
        }
    }
}
