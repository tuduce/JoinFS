using System.Net;
using System.Net.Mail;
using JoinFS.Net;
using JoinFS.Net.Legacy;
using PeerState = JoinFS.Net.Peer;

namespace JoinFS.Tests.Legacy
{
    /// <summary>
    /// The LegacyPlugin, driven through NetworkCore on an in-memory network, must put exactly the
    /// bytes on the wire that the pre-rewrite Network.cs/Node.cs implementation did. The fixtures in
    /// Legacy/Fixtures were captured from that implementation (driving its real writers, and
    /// injecting datagrams into a real LocalNode for the mesh replies) before it was deleted; they
    /// are now the frozen specification of the legacy wire. Same fixed identities, ports and inputs
    /// as at capture time.
    /// </summary>
    public class LegacyPluginGoldenTests
    {
        static readonly NodeId Self = new(0xCB007101u, 6112, 42);
        static readonly NodeId Peer = new(0xC6336402u, 6113, 7);
        static readonly NodeId Other = new(0xC0000299u, 6200, 9);
        static readonly IPEndPoint Capture = new(IPAddress.Loopback, 46112);
        static readonly NodeId Loopback = new(IPAddress.Loopback, 46112, 1);
        static readonly IPEndPoint NodeEndPoint = new(IPAddress.Loopback, 50000);
        static readonly Guid MainGuid = new("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
        const uint Suid = 0x0BADF00D;

        sealed class Sink : INetworkSink
        {
            public void Deliver<T>(in MessageMeta meta, in T message) where T : struct, IMessage { }
            public void OnEvent(in NetworkEvent e) { }
        }

        sealed class Stack
        {
            public readonly InMemoryNetwork Network = new();
            public readonly ManualClock Clock = new();
            public readonly NetworkCore Core;
            public readonly InMemoryTransport CaptureTransport;
            public readonly List<byte[]> Captured = [];

            public Stack()
            {
                CaptureTransport = Network.Attach(Capture, (_, data) => Captured.Add(data));
                NetworkCore core = null!;
                InMemoryTransport transport = Network.Attach(NodeEndPoint, (from, data) => core.OnDatagram(from, data));
                Core = core = new NetworkCore(transport, Clock, new Sink()) { IsOpen = true };
                Core.Identity.Set(Self);
                Core.AddPlugin(new LegacyPlugin(firstGuaranteedId: 0x0100));
                Network.Log.Clear();
                AddPeer(Peer, Capture);
            }

            public PeerState AddPeer(NodeId id, IPEndPoint endPoint)
            {
                PeerState peer = Core.Peers.Add(id, endPoint, receiveEstablished: true);
                peer.ExpireTime = 1e9;
                return peer;
            }

            public void Inject(byte[] datagram)
            {
                CaptureTransport.Send(NodeEndPoint, datagram);
                Network.Pump();
            }

            public List<byte[]> Drain()
            {
                Network.Pump();
                var result = new List<byte[]>(Captured);
                Captured.Clear();
                return result;
            }
        }

        static void AssertMatches(string fixture, List<byte[]> actual)
        {
            List<byte[]> expected = GoldenFile.Load(fixture);
            Assert.Equal(expected.Select(Convert.ToHexString), actual.Select(Convert.ToHexString));
        }

        static IdentityUpdate Identity(uint objectId, bool aircraft = true) => new()
        {
            ObjectId = objectId, IsAircraft = aircraft, IsPlane = true, Callsign = "BAW123", Model = "Airbus A320 Neo Asobo",
            Livery = "British Airways", IcaoType = "A320", IcaoAirline = "BAW", Registration = "G-ABCD", FlightNumber = "123",
            ClassCode = "L2J", Wtc = "M", ClassCodeConfirmed = true, TypeRole = 1,
        };

        static PositionUpdate SamplePosition(uint objectId, double netTime, PositionStateFlags flags) => new()
        {
            ObjectId = objectId, NetTime = netTime,
            Latitude = 51.4775, Longitude = -0.4614, Altitude = 1234.5,
            Pitch = -2.5f, Bank = 10.25f, Heading = 271.75f,
            VelocityX = 1.5f, VelocityY = -0.25f, VelocityZ = 120.125f,
            AngularVelocityX = 0.01f, AngularVelocityY = -0.02f, AngularVelocityZ = 0.03f,
            AccelerationX = 0.1f, AccelerationY = 0.2f, AccelerationZ = -0.3f,
            Rudder = 0.5f, Elevator = -0.25f, Aileron = 0.125f, BrakeLeft = 1f, BrakeRight = 0f,
            Elevation = 25.5f, StaticCgToGround = 4.75f,
            StateFlags = flags | PositionStateFlags.ElevationCorrection,
        };

        static FlightPlanUpdate SampleFlightPlan() => new()
        {
            Owner = Self, ObjectId = 0x0102, FormatVersion = 1,
            Callsign = "BAW123", Registration = "G-ABCD", IcaoType = "A320", IcaoAirline = "BAW",
            FlightNumber = "123", Departure = "EGLL", Destination = "LFPG", Rules = "IFR",
            Route = "DVR UL9 KONAN", Remarks = "TCAS", Alternate = "LFPO", Speed = "N0450", Altitude = "FL350",
        };

        static HubUserUpdate SampleHubUser(int i) => new()
        {
            Guid = new Guid(i, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
            Atc = i % 2 == 0, Ifr = true, Nickname = "User" + i, Frequency = (ushort)(12000 + i),
            Latitude = 50f + i, Longitude = -1f - i, Altitude = (ushort)(3000 + i), Speed = (ushort)(200 + i),
            Squawk = 7000, Level = 2, Range = 40, Heading = (ushort)(90 + i),
            Callsign = "BAW123", Registration = "G-ABCD", IcaoType = "A320", IcaoAirline = "BAW", FlightNumber = "123",
            Departure = "EGLL", Destination = "LFPG", Rules = "IFR", Route = "DVR UL9 KONAN", Remarks = "TCAS",
            Alternate = "LFPO", FlightSpeed = "N0450", FlightAltitude = "FL350",
        };

        // ================================================================== mesh requests

        [Fact]
        public void Join()
        {
            var s = new Stack();
            s.Core.Mesh.Join(Capture, 0xCAFEBABE);
            AssertMatches("internal_join", s.Drain());
        }

        [Fact]
        public void Login()
        {
            var s = new Stack();
            s.Core.Mesh.Login(Capture, "pilot@example.org", 0xDEADBEEF, true);
            AssertMatches("internal_login", s.Drain());
        }

        [Fact]
        public void Leave()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(0x55667788);
            s.Core.Mesh.Leave();
            AssertMatches("internal_leave", s.Drain());
        }

        [Fact]
        public void GuaranteedSegmentation()
        {
            var s = new Stack();
            SendCommsNote(s, new string('x', 2500));
            AssertMatches("guaranteed_segmented_comms_note", s.Drain());
        }

        // ================================================================== positions

        [Fact]
        public void AircraftPosition()
        {
            var s = new Stack();
            s.Core.Objects.SetIdentity(Self, Identity(0x0102));
            s.Core.SendTo(Peer, SamplePosition(0x0102, 12345.678, PositionStateFlags.UserControlled), false);
            AssertMatches("app_aircraft_position", s.Drain());
        }

        [Fact]
        public void AircraftPosition_SharedCockpitPaused()
        {
            var s = new Stack();
            s.Core.Objects.SetIdentity(Self, new IdentityUpdate
            {
                ObjectId = uint.MaxValue, IsAircraft = true, IsPlane = false, Callsign = "", Model = "", Livery = "", IcaoType = "",
                IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", ClassCodeConfirmed = false, TypeRole = 0,
            });
            s.Core.SendTo(Peer, SamplePosition(uint.MaxValue, 99.5, PositionStateFlags.Paused | PositionStateFlags.OnGround), false);
            AssertMatches("app_aircraft_position_shared_cockpit_paused", s.Drain());
        }

        static ObjectPositionUpdate SampleObjectPosition(uint id, double netTime, PositionStateFlags extra) => new()
        {
            ObjectId = id, NetTime = netTime,
            Latitude = 45.5, Longitude = 7.25, Altitude = 12.75, Pitch = 0.5f, Bank = -1.5f, Heading = 180f,
            VelocityX = 3f, VelocityY = 0f, VelocityZ = 8.5f, AngularVelocityX = 0f, AngularVelocityY = 0.1f,
            AngularVelocityZ = 0f, AccelerationX = 0f, AccelerationY = 0f, AccelerationZ = 0.5f, Height = 2.5f,
            StateFlags = PositionStateFlags.OnGround | PositionStateFlags.ElevationCorrection | extra,
        };

        [Fact]
        public void ObjectPosition_Boat()
        {
            var s = new Stack();
            s.Core.Objects.SetIdentity(Self, new IdentityUpdate
            {
                ObjectId = 0x0303, IsAircraft = false, Model = "Container Ship", Livery = "Blue", IcaoType = "", IcaoAirline = "",
                ClassCode = "", Wtc = "", ClassCodeConfirmed = false, TypeRole = 5,
            });
            s.Core.SendTo(Peer, SampleObjectPosition(0x0303, 4321.5, PositionStateFlags.Paused), false);
            AssertMatches("app_object_position_boat", s.Drain());
        }

        [Fact]
        public void ObjectPosition_Plane()
        {
            var s = new Stack();
            s.Core.Objects.SetIdentity(Self, new IdentityUpdate
            {
                ObjectId = 0x0404, IsAircraft = true, Model = "Cessna 172", Livery = "White", IcaoType = "C172", IcaoAirline = "",
                ClassCode = "L1P", Wtc = "L", ClassCodeConfirmed = true, TypeRole = 1,
            });
            s.Core.SendTo(Peer, SampleObjectPosition(0x0404, 10.25, 0), false);
            AssertMatches("app_object_position_plane", s.Drain());
        }

        // ================================================================== variables

        [Fact]
        public void IntegerVariables_Chunked()
        {
            var s = new Stack();
            var m = new VariableSyncUpdate { Owner = Self, ObjectId = 0x0102, Entries = [] };
            for (uint i = 0; i < 101; i++) m.Entries.Add(new VariableEntry { Vuid = 0x1000 + i, Kind = VariableKind.Int32, IntValue = (int)(i * 3) - 50 });
            s.Core.SendTo(Peer, m, false);
            AssertMatches("app_integer_variables_chunked", s.Drain());
        }

        [Fact]
        public void FloatVariables_Broadcast()
        {
            var s = new Stack();
            var m = new VariableSyncUpdate
            {
                Owner = Self, ObjectId = 0x0102,
                Entries =
                [
                    new VariableEntry { Vuid = 0xAAAA0001, Kind = VariableKind.Float32, FloatValue = 1.5f },
                    new VariableEntry { Vuid = 0xAAAA0002, Kind = VariableKind.Float32, FloatValue = -0.125f },
                    new VariableEntry { Vuid = 0xAAAA0003, Kind = VariableKind.Float32, FloatValue = 1e6f },
                ],
            };
            s.Core.Broadcast(m, false);
            AssertMatches("app_float_variables_broadcast", s.Drain());
        }

        [Fact]
        public void String8Variables_Chunked()
        {
            var s = new Stack();
            var m = new VariableSyncUpdate { Owner = Self, ObjectId = 0x0102, Entries = [] };
            for (uint i = 0; i < 81; i++) m.Entries.Add(new VariableEntry { Vuid = 0x2000 + i, Kind = VariableKind.String8, StringValue = "S" + i });
            s.Core.SendTo(Peer, m, false);
            AssertMatches("app_string8_variables_chunked", s.Drain());
        }

        // ================================================================== events / weather / objects

        [Fact]
        public void SimEvent()
        {
            var s = new Stack();
            s.Core.SendTo(Peer, new EventUpdate { ObjectId = 0x0102, EventId = 0x00011000, Data = 0xFFFFFFFE }, true);
            AssertMatches("app_sim_event", s.Drain());
        }

        [Fact]
        public void Weather()
        {
            var s = new Stack();
            s.Core.SendTo(Peer, new WeatherRequest { ObjectId = 0x0102 }, true);
            AssertMatches("app_weather_request", s.Drain());
            s = new Stack();
            s.Core.SendTo(Peer, new WeatherReply { Metar = "EGLL 231150Z 24012KT 9999 FEW030 18/09 Q1015" }, true);
            AssertMatches("app_weather_reply", s.Drain());
            s = new Stack();
            s.Core.SendTo(Peer, new WeatherUpdate { Metar = "LFPG 231200Z 22008KT CAVOK 20/10 Q1017" }, false);
            AssertMatches("app_weather_update", s.Drain());
        }

        [Fact]
        public void RemoveObjectAndShowOnRadar()
        {
            var s = new Stack();
            s.Core.Broadcast(new RemoveObject { ObjectId = 0x0102 }, true);
            AssertMatches("app_remove_object", s.Drain());
            s = new Stack();
            s.Core.Broadcast(new ShowOnRadar { Owner = Self, ObjectId = 0x0102, Show = true }, true);
            AssertMatches("app_show_on_radar", s.Drain());
        }

        [Fact]
        public void FlightPlan()
        {
            var s = new Stack();
            s.Core.Broadcast(SampleFlightPlan(), false);
            AssertMatches("app_flight_plan", s.Drain());
        }

        // ================================================================== peer / status

        [Fact]
        public void SharedData()
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
            var s = new Stack();
            s.Core.SendTo(Peer, new PeerInfo
            {
                Share = ShareCockpitFlags.FlightControls, Nickname = "Nick", Guid = MainGuid, Hub = false, Atc = true,
                SimulatorConnected = false, AtcAirport = "EGLL", AtcLevel = 3, AtcFrequency = 12250, ActivityCircle = 40,
                Version = "9.9.9", Simulator = JoinFS.Resources.Strings.NotConnected,
            }, false);
            AssertMatches("app_shared_data", s.Drain());
        }

        [Fact]
        public void StatusRequest()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, new StatusRequestUpdate { HubEnabled = false, HubListRequested = true, Uuid = 0x12345678 }, false);
            AssertMatches("app_status_request", s.Drain());
        }

        [Fact]
        public void Status_Client()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, new StatusUpdate { Guid = MainGuid, AppVersion = "9.9.9", Users = 1, AtcAirport = "", AtcLevel = 2 }, false);
            AssertMatches("app_status_client", s.Drain());
        }

        [Fact]
        public void Status_Hub()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, new StatusUpdate
            {
                Guid = MainGuid, AppVersion = "9.9.9", Users = 0, AtcCount = 1, AtcAirport = "EGKK", AtcLevel = 3, HubEnabled = true,
                Address = "hub.example.org", Name = "Test Hub", About = "About text", Voip = "voip://x", NextEvent = "Friday fly-in",
                Airport = "EGKK", ActivityCircle = 40,
            }, false);
            AssertMatches("app_status_hub", s.Drain());
        }

        [Fact]
        public void Online()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, new OnlineAnnouncement { Uuid = 0x12345678 }, false);
            AssertMatches("app_online", s.Drain());
        }

        // ================================================================== hub directory

        [Fact]
        public void HubList()
        {
            var s = new Stack();
            var m = new HubList { Hubs = [] };
            for (int i = 0; i < 26; i++) m.Hubs.Add(new HubAddress { Node = new NodeId(0x0A000000u + (uint)i, (ushort)(6112 + i), (byte)i), Port = (ushort)(6112 + i) });
            s.Core.SendToEndPoint(Capture, m, false);
            AssertMatches("app_hub_list_chunked", s.Drain());
        }

        [Fact]
        public void UserLists()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, new UserListRequest(), false);
            AssertMatches("app_user_list_request", s.Drain());

            s = new Stack();
            s.Core.SendToEndPoint(Capture, SampleHubUser(1), false);
            s.Core.SendToEndPoint(Capture, SampleHubUser(2), false);
            AssertMatches("app_user_list", s.Drain());

            s = new Stack();
            s.Core.SendToEndPoint(Capture, new UserPositionsRequest(), false);
            AssertMatches("app_user_positions_request", s.Drain());

            s = new Stack();
            var positions = new UserPositions { Users = [] };
            for (int i = 0; i < 21; i++)
            {
                HubUserUpdate u = SampleHubUser(i);
                positions.Users.Add(new UserPosition { Guid = u.Guid, Latitude = u.Latitude, Longitude = u.Longitude, Altitude = u.Altitude, Speed = u.Speed, Squawk = u.Squawk, Heading = u.Heading });
            }
            s.Core.SendToEndPoint(Capture, positions, false);
            AssertMatches("app_user_positions_chunked", s.Drain());
        }

        [Fact]
        public void UserNuid()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, new UserNuidRequest { Uuid = 0x0A0B0C0D }, true);
            AssertMatches("app_user_nuid_request", s.Drain());

            // the fixture starts with the GuaranteedDone for the injected request; the reply is the second datagram
            s = new Stack();
            s.Core.SendToEndPoint(Capture, new UserNuidReply { Uuid = 0x0A0B0C0D, Node = Other, Port = 6201 }, true);
            Assert.Equal(Convert.ToHexString(GoldenFile.Load("app_user_nuid_reply")[1]), Convert.ToHexString(s.Drain().Single()));
        }

        // ================================================================== comms

        static void SendCommsNote(Stack s, string text) =>
            s.Core.Broadcast(new NotesBundle
            {
                Scope = CommsScope.Single,
                Users = [new NotesUser { Guid = MainGuid, Nickname = "Nick", Callsign = "BAW123", Notes = [new CommsNote { NoteId = 77, Age = 1.5f, Channel = 3, Text = text }] }],
            }, true);

        [Fact]
        public void CommsRequests()
        {
            var s = new Stack();
            s.Core.SendTo(Peer, new CommsRequest { Scope = CommsScope.Session }, true);
            AssertMatches("app_session_comms_request", s.Drain());
            s = new Stack();
            s.Core.SendToEndPoint(Capture, new CommsRequest { Scope = CommsScope.Global }, true);
            AssertMatches("app_global_comms_request", s.Drain());
            s = new Stack();
            s.Core.SendToEndPoint(Capture, new CommsRequest { Scope = CommsScope.All }, true);
            AssertMatches("app_comms_listen_request", s.Drain());
        }

        [Fact]
        public void CommsNote()
        {
            var s = new Stack();
            SendCommsNote(s, "Hello, tower.");
            AssertMatches("app_comms_note", s.Drain());
        }

        static NotesBundle SampleNotes(CommsScope scope, Func<ushort, bool> include)
        {
            var alice = new NotesUser { Guid = new Guid("11111111-2222-3333-4444-555555555555"), Nickname = "Alice", Callsign = "AAL1", Notes = [] };
            var bob = new NotesUser { Guid = new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa"), Nickname = "Bob", Callsign = "BOB2", Notes = [] };
            void Add(NotesUser u, uint id, float age, ushort channel, string text)
            {
                if (include(channel)) u.Notes.Add(new CommsNote { NoteId = id, Age = age, Channel = channel, Text = text });
            }
            Add(alice, 1, 2.0f, 3, "hello");
            Add(alice, 2, 4.5f, 0, "global hi");
            Add(bob, 7, 1.0f, 5, "roger");
            return new NotesBundle { Scope = scope, Users = [alice, bob] };
        }

        [Fact]
        public void BulkNotes()
        {
            var s = new Stack();
            s.Core.SendToEndPoint(Capture, SampleNotes(CommsScope.Session, c => c != 0), true);
            AssertMatches("app_session_comms", s.Drain());
            s = new Stack();
            s.Core.SendToEndPoint(Capture, SampleNotes(CommsScope.Global, c => c == 0), true);
            AssertMatches("app_global_comms", s.Drain());
            s = new Stack();
            s.Core.SendToEndPoint(Capture, SampleNotes(CommsScope.All, _ => true), true);
            AssertMatches("app_notes_all", s.Drain());
            AssertMatches("app_notes_type_comms", GoldenFile.Load("app_notes_all"));
        }

        // ================================================================== mesh responses

        static byte[] Internal(NodeId sender, short id, bool guaranteed, ushort gid, Action<BinaryWriter> body) =>
            LegacyDatagrams.Build(sender, Self, true, guaranteed, gid, w => { w.Write(id); body(w); });


        [Fact]
        public void Join_Accepted()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid);
            s.Inject(Internal(Loopback, 0, true, 0x0777, w => w.Write(0u)));
            AssertMatches("mesh_join_accepted", s.Drain());
        }

        [Fact]
        public void Join_Fails()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid, password: 0x11112222);
            s.Inject(Internal(Loopback, 0, true, 0x0778, w => w.Write(0x33334444u)));
            AssertMatches("mesh_join_fail_password", s.Drain());

            s = new Stack();
            s.Core.Mesh.ForceSession(Suid, loginRequired: true);
            s.Inject(Internal(Loopback, 0, true, 0x0779, w => w.Write(0u)));
            AssertMatches("mesh_join_fail_login_required", s.Drain());
        }

        static Stack LoginHub(uint storedHash)
        {
            var s = new Stack();
            var store = new CredentialStore(Path.GetTempPath(), _ => { });
            store.Set(new MailAddress("pilot@example.org"), storedHash);
            s.Core.Mesh.ForceSession(Suid, loginRequired: true, creator: true, store: store);
            return s;
        }

        static byte[] LoginDatagram(string email, uint hash, bool verify) =>
            Internal(Loopback, 11, true, 0x0780, w => { w.Write(email); w.Write(hash); w.Write(verify); });

        [Fact]
        public void Login_Replies()
        {
            var s = LoginHub(0xABCD0001);
            s.Inject(LoginDatagram("pilot@example.org", 0xABCD0001, false));
            AssertMatches("mesh_login_accepted", s.Drain());

            s = LoginHub(0xABCD0001);
            s.Inject(LoginDatagram("stranger@example.org", 0xABCD0001, false));
            AssertMatches("mesh_login_fail_unknown_address", s.Drain());

            s = LoginHub(0);
            s.Inject(LoginDatagram("pilot@example.org", 0xABCD0001, false));
            AssertMatches("mesh_login_fail_verify_password", s.Drain());

            s = LoginHub(0xABCD0001);
            s.Inject(LoginDatagram("pilot@example.org", 0xFFFF0000, false));
            AssertMatches("mesh_login_fail_wrong_password", s.Drain());
        }

        [Fact]
        public void Pulse_Outgoing()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid);
            s.Core.LowBandwidth = true;
            s.Clock.Advance(0.001);
            s.Core.Mesh.DoPulse();
            List<byte[]> datagrams = s.Drain();
            foreach (byte[] d in datagrams) Array.Clear(d, LegacyWire.DataOffset + 6, 8);
            AssertMatches("mesh_pulse_timestamp_zeroed", datagrams);
        }

        [Fact]
        public void Pulse_Incoming()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid);
            s.Inject(Internal(Peer, 4, false, 0, w => { w.Write(Suid); w.Write(0x0102030405060708L); w.Write((byte)1); }));
            AssertMatches("mesh_pulse_response", s.Drain());
        }

        [Fact]
        public void Pathfinder()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid);
            s.Core.Peers.TryGet(Peer, out PeerState peer);
            peer!.SendEstablished = true;
            s.AddPeer(Other, new IPEndPoint(IPAddress.Parse("192.0.2.153"), 6200));
            s.Clock.Advance(0.001);
            s.Core.Mesh.DoRouting();
            // Other isn't reachable in memory; only what reached the capture endpoint counts
            AssertMatches("mesh_pathfinder", s.Drain());
        }

        [Fact]
        public void Pathfinder_Response()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid);
            s.Core.Peers.TryGet(Peer, out PeerState peer);
            peer!.SendEstablished = true;
            s.Inject(Internal(Peer, 8, false, 0, w =>
            {
                w.Write(Suid);
                w.Write((short)3);
                Self.Write(w);
                Peer.Write(w);
                Other.Write(w);
            }));
            AssertMatches("mesh_pathfinder_response", s.Drain());
        }

        [Fact]
        public void Forward()
        {
            var s = new Stack();
            s.Core.Mesh.ForceSession(Suid);
            s.Inject(LegacyDatagrams.Build(Other, Peer, false, false, 0,
                w => { w.Write((short)21008); w.Write((short)12); w.Write("METAR"); }));
            AssertMatches("mesh_forward", s.Drain());
        }
    }
}
