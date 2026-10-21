using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>The bodies of the directory classes (docs/network/jfp2/protocol.md §5.16): round trips, sizes inside the datagram, and the bytes of the two with endpoints.</summary>
    public class DirectoryCodecTests
    {
        static readonly IPEndPoint V4 = new(IPAddress.Parse("203.0.113.10"), 6112);
        static readonly IPEndPoint V6 = new(IPAddress.Parse("2001:db8::1"), 6113);

        static byte[] Encode<T>(ICodec<T> codec, in T value)
        {
            byte[] buffer = new byte[Jfp2Limits.MaxPayloadSize];
            int length = codec.Encode(value, buffer);
            return buffer[..length];
        }

        [Fact]
        public void AHubList_RoundTrips_WithNamesPortsAndCandidates()
        {
            NodeId legacy = NodeId.FromLegacy(0xCB00710A, 6112, 1);
            NodeId key = NodeKey.Generate().Id;
            var codec = new HubListV1Codec();

            HubList decoded = codec.Decode(Encode(codec, new HubList { Hubs = [new HubAddress { Node = legacy, Port = 6112 }, new HubAddress { Node = key, Port = 6113, Candidates = [V6, V4] }] }));

            Assert.Equal(2, decoded.Hubs.Count);
            Assert.Equal(legacy, decoded.Hubs[0].Node);
            Assert.Equal((ushort)6112, decoded.Hubs[0].Port);
            Assert.Empty(decoded.Hubs[0].Candidates);
            Assert.Equal(key, decoded.Hubs[1].Node);
            Assert.Equal([V6, V4], decoded.Hubs[1].Candidates);
        }

        [Fact]
        public void AHubList_HasTheBytesOfTheSpecification()
        {
            // count 1 | name (kind 0 id, little-endian) | port | one candidate: length 7, family 4, 203.0.113.10, port 6112
            NodeId legacy = NodeId.FromLegacy(0xCB00710A, 6112, 1);
            byte[] bytes = Encode(new HubListV1Codec(), new HubList { Hubs = [new HubAddress { Node = legacy, Port = 6112, Candidates = [V4] }] });

            byte[] name = new byte[NodeId.WireSize];
            legacy.WriteTo(name);
            byte[] expected = [1, .. name, 0xE0, 0x17, 1, 7, 4, 203, 0, 113, 10, 0xE0, 0x17];
            Assert.Equal(expected, bytes);
        }

        [Fact]
        public void AHubList_ThatIsTooLong_CarriesTheEntriesThatFit_NotAMalformedMessage()
        {
            var hubs = new List<HubAddress>();
            for (int n = 0; n < 40; n++)
            {
                hubs.Add(new HubAddress { Node = NodeId.FromLegacy(0xCB000000u + (uint)n, 6112, (byte)n), Port = 6112, Candidates = [V6, V6, V6, V6, V6] });
            }
            var codec = new HubListV1Codec();
            byte[] bytes = Encode(codec, new HubList { Hubs = hubs });

            Assert.True(bytes.Length <= HubListV1Codec.Budget);
            HubList decoded = codec.Decode(bytes);
            Assert.Equal(bytes[0], decoded.Hubs.Count);
            Assert.InRange(decoded.Hubs.Count, 8, 39);   // eight always fit: the sender sends eight to a message
        }

        [Fact]
        public void AHubUser_RoundTrips_AndTheLargestOneFitsADatagram()
        {
            var codec = new HubUserV1Codec();
            var user = new HubUserUpdate
            {
                Guid = Guid.NewGuid(), Atc = true, Ifr = true, Nickname = "Pilot", Callsign = "N123AB", Frequency = 12280, Latitude = 51.47f, Longitude = -0.45f,
                Altitude = 1500, Speed = 120, Squawk = 1200, Level = 3, Range = 50, Heading = 270, IcaoType = "C172", Departure = "EGLL", Destination = "EGKK", Rules = "I",
                Route = "DCT", Remarks = "RMK", Alternate = "EGSS", FlightSpeed = "N0120", FlightAltitude = "A050", Registration = "G-ABCD", IcaoAirline = "BAW", FlightNumber = "BA1",
            };

            HubUserUpdate decoded = codec.Decode(Encode(codec, user));

            Assert.Equal(user.Guid, decoded.Guid);
            Assert.True(decoded.Atc);
            Assert.True(decoded.Ifr);
            Assert.Equal("Pilot", decoded.Nickname);
            Assert.Equal(12280, decoded.Frequency);
            Assert.Equal(51.47f, decoded.Latitude);
            Assert.Equal(-0.45f, decoded.Longitude);
            Assert.Equal(1500, decoded.Altitude);
            Assert.Equal(270, decoded.Heading);
            Assert.Equal("EGLL", decoded.Departure);
            Assert.Equal("BA1", decoded.FlightNumber);

            string longText = new('x', 2000);
            var largest = new HubUserUpdate
            {
                Nickname = longText, Callsign = longText, IcaoType = longText, Departure = longText, Destination = longText, Rules = longText, Route = longText, Remarks = longText,
                Alternate = longText, FlightSpeed = longText, FlightAltitude = longText, Registration = longText, IcaoAirline = longText, FlightNumber = longText,
            };
            Assert.True(Encode(codec, largest).Length <= HubUserV1Codec.MaxSize);
            Assert.True(HubUserV1Codec.MaxSize <= Jfp2Limits.MaxPayloadSize);
        }

        [Fact]
        public void UserPositions_RoundTrip_AndALongListIsCutAtTheDatagram()
        {
            var codec = new UserPositionsV1Codec();
            var users = Enumerable.Range(0, 50).Select(n => new UserPosition { Guid = Guid.NewGuid(), Latitude = n, Longitude = -n, Altitude = (ushort)n, Speed = 100, Squawk = 1200, Heading = 90 }).ToList();

            byte[] bytes = Encode(codec, new UserPositions { Users = users });
            UserPositions decoded = codec.Decode(bytes);

            Assert.True(bytes.Length <= Jfp2Limits.MaxPayloadSize);
            Assert.Equal(UserPositionsV1Codec.MaxUsers, decoded.Users.Count);
            Assert.Equal(users[3].Guid, decoded.Users[3].Guid);
            Assert.Equal(-3f, decoded.Users[3].Longitude);
        }

        [Fact]
        public void OnlineLookupAndTheAnswer_RoundTrip()
        {
            Assert.Equal(0x12345678u, new OnlineV1Codec().Decode(Encode(new OnlineV1Codec(), new OnlineAnnouncement { Uuid = 0x12345678 })).Uuid);
            Assert.Equal(0x12345678u, new UserLookupV1Codec().Decode(Encode(new UserLookupV1Codec(), new UserNuidRequest { Uuid = 0x12345678 })).Uuid);

            NodeId key = NodeKey.Generate().Id;
            var answer = new UserLookupAnswerV1Codec();
            UserNuidReply decoded = answer.Decode(Encode(answer, new UserNuidReply { Uuid = 7, Node = key, Port = 6112, Candidates = [V6, V4] }));

            Assert.Equal(7u, decoded.Uuid);
            Assert.Equal(key, decoded.Node);
            Assert.Equal((ushort)6112, decoded.Port);
            Assert.Equal([V6, V4], decoded.Candidates);
            Assert.Empty(answer.Decode(Encode(answer, new UserNuidReply { Uuid = 7, Node = key, Port = 6112 })).Candidates);
        }

        [Fact]
        public void AUserLookupAnswer_HasTheBytesOfAppendixA17()
        {
            NodeId keyName = NodeId.ReadFrom([0x02, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]);
            byte[] bytes = Encode(new UserLookupAnswerV1Codec(), new UserNuidReply { Uuid = 7, Node = keyName, Port = 6112, Candidates = [new IPEndPoint(IPAddress.Parse("2001:db8:3::3"), 6112)] });

            Assert.Equal(Convert.FromHexString("07000000" + "0211223344556677" + "E017" + "01" + "13" + "0620010DB8000300000000000000000003E017"), bytes);
        }

        [Fact]
        public void TheRequests_AreOneReservedByte()
        {
            Assert.Equal([0], Encode(new UserListRequestV1Codec(), new UserListRequest()));
            Assert.Equal([0], Encode(new UserPositionsRequestV1Codec(), new UserPositionsRequest()));
        }

        [Fact]
        public void TheProfile_OffersTheDirectoryClasses_AndOnlyTheDirectoryCarriesOnNonMemberSessions()
        {
            Jfp2Profile profile = Jfp2Profile.Default;
            byte[] classes = [MessageClasses.HubList, MessageClasses.UserListRequest, MessageClasses.HubUser, MessageClasses.UserPositionsRequest,
                MessageClasses.UserPositions, MessageClasses.Online, MessageClasses.UserLookup, MessageClasses.UserLookupAnswer];

            Assert.Equal(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, classes);
            foreach (byte messageClass in classes)
            {
                Assert.Contains(profile.Offers, o => !o.Internal && o.MessageClass == messageClass && o.MinVersion == 1 && o.MaxVersion == 1);
                Assert.False(profile.ForClass(messageClass).Guaranteed);
            }
            Assert.Equal(MessageKind.HubList, profile.ForClass(MessageClasses.HubList).Kind);
            Assert.Equal(MessageKind.UserNuid, profile.ForClass(MessageClasses.UserLookupAnswer).Kind);
        }
    }
}
