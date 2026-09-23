using System.Text.Json;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Every canonical message kind the legacy protocol carries survives encode on one node and
    /// decode on another unchanged (compared field by field via JSON, which includes list contents).
    /// </summary>
    public class LegacyRoundTripTests
    {
        static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

        static (TestMesh Mesh, TestNode A, TestNode B) Pair()
        {
            var mesh = new TestMesh();
            TestNode a = mesh.Add("198.51.100.2");
            TestNode b = mesh.Add("192.0.2.3");
            a.Core.Mesh.Create(false, 0, false, "");
            b.Core.Mesh.Join(a.EndPoint, 0);
            mesh.Run(3);
            return (mesh, a, b);
        }

        static void AssertRoundTrip<T>(T message, bool guaranteed = false) where T : struct, IMessage
        {
            var (mesh, a, b) = Pair();
            a.Core.SendTo(b.Id, message, guaranteed);
            mesh.Run(0.1);
            T received = Assert.Single(b.Messages<T>());
            Assert.Equal(JsonSerializer.Serialize(message, Json), JsonSerializer.Serialize(received, Json));
        }

        [Fact]
        public void AircraftPosition_SplitsIntoIdentityAndPosition()
        {
            var (mesh, a, b) = Pair();
            var identity = new IdentityUpdate
            {
                ObjectId = 5, IsAircraft = true, IsPlane = true, Callsign = "DLH1", Model = "A320", Livery = "L", IcaoType = "A320",
                IcaoAirline = "DLH", Registration = "D-AIAA", FlightNumber = "1", ClassCode = "L2J", Wtc = "M", ClassCodeConfirmed = true, TypeRole = 2,
            };
            var position = new PositionUpdate
            {
                ObjectId = 5, NetTime = 123.25, Latitude = 50.1, Longitude = 8.6, Altitude = 300, Pitch = 1, Bank = 2, Heading = 3,
                VelocityX = 4, VelocityY = 5, VelocityZ = 6, AngularVelocityX = 7, AngularVelocityY = 8, AngularVelocityZ = 9,
                AccelerationX = 10, AccelerationY = 11, AccelerationZ = 12, Rudder = 0.5f, Elevator = -0.5f, Aileron = 0.25f,
                BrakeLeft = 1, BrakeRight = 0, Elevation = 100, StaticCgToGround = 5.5f,
                StateFlags = PositionStateFlags.OnGround | PositionStateFlags.UserControlled | PositionStateFlags.Paused | PositionStateFlags.ElevationCorrection,
            };
            a.Core.Objects.SetIdentity(a.Id, identity);
            a.Core.SendTo(b.Id, position, false);
            mesh.Run(0.1);

            Assert.Equal(JsonSerializer.Serialize(identity, Json), JsonSerializer.Serialize(b.Messages<IdentityUpdate>().Single(), Json));
            Assert.Equal(JsonSerializer.Serialize(position, Json), JsonSerializer.Serialize(b.Messages<PositionUpdate>().Single(), Json));
        }

        [Fact]
        public void ObjectPosition_SplitsIntoIdentityAndPosition()
        {
            var (mesh, a, b) = Pair();
            var identity = new IdentityUpdate
            {
                ObjectId = 9, IsAircraft = false, Callsign = "", Model = "Ship", Livery = "Red", IcaoType = "", IcaoAirline = "",
                Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 4,
            };
            var position = new ObjectPositionUpdate
            {
                ObjectId = 9, NetTime = 2, Latitude = 1, Longitude = 2, Altitude = 3, Height = 1.5f, VelocityZ = 3,
                StateFlags = PositionStateFlags.OnGround | PositionStateFlags.Paused,
            };
            a.Core.Objects.SetIdentity(a.Id, identity);
            a.Core.SendTo(b.Id, position, false);
            mesh.Run(0.1);

            Assert.Equal(JsonSerializer.Serialize(identity, Json), JsonSerializer.Serialize(b.Messages<IdentityUpdate>().Single(), Json));
            Assert.Equal(JsonSerializer.Serialize(position, Json), JsonSerializer.Serialize(b.Messages<ObjectPositionUpdate>().Single(), Json));
        }

        [Fact]
        public void Variables_OneKindPerMessage()
        {
            var (mesh, a, b) = Pair();
            var m = new VariableSyncUpdate
            {
                Owner = a.Id, ObjectId = 3,
                Entries =
                [
                    new VariableEntry { Vuid = 1, Kind = VariableKind.Int32, IntValue = -5 },
                    new VariableEntry { Vuid = 2, Kind = VariableKind.Float32, FloatValue = 2.5f },
                    new VariableEntry { Vuid = 3, Kind = VariableKind.String8, StringValue = "abc" },
                ],
            };
            a.Core.SendTo(b.Id, m, false);
            mesh.Run(0.1);

            var received = b.Messages<VariableSyncUpdate>().ToList();
            Assert.Equal(3, received.Count);
            Assert.Equal(-5, received[0].Entries.Single().IntValue);
            Assert.Equal(2.5f, received[1].Entries.Single().FloatValue);
            Assert.Equal("abc", received[2].Entries.Single().StringValue);
            Assert.All(received, r => Assert.Equal(a.Id, r.Owner));
        }

        [Fact] public void Event() => AssertRoundTrip(new EventUpdate { ObjectId = 1, EventId = 2, Data = 3 }, true);
        [Fact] public void Remove() => AssertRoundTrip(new RemoveObject { ObjectId = 4 }, true);
        [Fact] public void Radar() => AssertRoundTrip(new ShowOnRadar { Owner = new NodeId(1, 2, 3), ObjectId = 4, Show = true }, true);

        [Fact]
        public void FlightPlan() => AssertRoundTrip(new FlightPlanUpdate
        {
            Owner = new NodeId(9, 8, 7), ObjectId = 6, FormatVersion = 1, IcaoType = "B738", Departure = "EDDF", Destination = "EGLL",
            Rules = "IFR", Route = "R", Remarks = "RMK", Alternate = "EGKK", Speed = "N0450", Altitude = "F350", Callsign = "DLH2",
            Registration = "D-X", IcaoAirline = "DLH", FlightNumber = "2",
        });

        [Fact]
        public void Peer() => AssertRoundTrip(new PeerInfo
        {
            Share = ShareCockpitFlags.Cockpit | ShareCockpitFlags.NavControls, Nickname = "N", Guid = Guid.NewGuid(), Hub = true, Atc = true,
            SimulatorConnected = true, AtcAirport = "EDDF", AtcLevel = 4, AtcFrequency = 12180, ActivityCircle = 20, Version = "26.6", Simulator = "MSFS",
        });

        [Fact] public void StatusRequest() => AssertRoundTrip(new StatusRequestUpdate { HubEnabled = true, HubListRequested = true, Uuid = 77 });

        [Fact]
        public void Status() => AssertRoundTrip(new StatusUpdate
        {
            Guid = Guid.NewGuid(), AppVersion = "26.6", Users = 3, AtcCount = 1, AtcAirport = "EDDF", AtcLevel = 5, Planes = 1, Helicopters = 2,
            Boats = 3, Vehicles = 4, HubEnabled = true, Address = "hub.example", Name = "Hub", About = "A", Voip = "V", NextEvent = "E",
            Airport = "EDDF", ActivityCircle = 80, GlobalSession = true, PasswordRequired = true,
        });

        [Fact] public void WeatherRequest() => AssertRoundTrip(new WeatherRequest { ObjectId = 5 }, true);
        [Fact] public void WeatherReply() => AssertRoundTrip(new WeatherReply { Metar = "M1" }, true);
        [Fact] public void WeatherUpdate() => AssertRoundTrip(new WeatherUpdate { Metar = "M2" });
        [Fact] public void Hubs() => AssertRoundTrip(new HubList { Hubs = [new HubAddress { Node = new NodeId(5, 6, 7), Port = 8 }] });
        [Fact] public void UserListRequest() => AssertRoundTrip(new UserListRequest());
        [Fact] public void UserPositionsRequest() => AssertRoundTrip(new UserPositionsRequest());
        [Fact] public void Online() => AssertRoundTrip(new OnlineAnnouncement { Uuid = 99 });
        [Fact] public void NuidRequest() => AssertRoundTrip(new UserNuidRequest { Uuid = 98 }, true);
        [Fact] public void NuidReply() => AssertRoundTrip(new UserNuidReply { Uuid = 97, Node = new NodeId(1, 1, 1), Port = 2 }, true);
        [Fact] public void Comms() => AssertRoundTrip(new CommsRequest { Scope = CommsScope.Session }, true);

        [Fact]
        public void HubUser() => AssertRoundTrip(new HubUserUpdate
        {
            Guid = Guid.NewGuid(), Atc = true, Ifr = true, Callsign = "C", Nickname = "N", Frequency = 1, Latitude = 2, Longitude = 3,
            Altitude = 4, Speed = 5, Squawk = 6, Level = 7, Range = 8, Heading = 9, IcaoType = "T", Departure = "D", Destination = "E",
            Rules = "R", Route = "RT", Remarks = "RM", Alternate = "A", FlightSpeed = "S", FlightAltitude = "AL", Registration = "REG",
            IcaoAirline = "AIR", FlightNumber = "FN",
        });

        [Fact]
        public void Positions() => AssertRoundTrip(new UserPositions
        {
            Users = [new UserPosition { Guid = Guid.NewGuid(), Latitude = 1, Longitude = 2, Altitude = 3, Speed = 4, Squawk = 5, Heading = 6 }],
        });

        [Fact]
        public void Notes() => AssertRoundTrip(new NotesBundle
        {
            Scope = CommsScope.All,
            Users =
            [
                new NotesUser { Guid = Guid.NewGuid(), Nickname = "A", Callsign = "B", Notes = [new CommsNote { NoteId = 1, Age = 2, Channel = 3, Text = "t" }] },
                new NotesUser { Guid = Guid.NewGuid(), Nickname = "C", Callsign = "D", Notes = [] },
            ],
        }, true);
    }
}
