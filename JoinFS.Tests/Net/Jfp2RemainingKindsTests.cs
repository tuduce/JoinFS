using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The kinds legacy alone carried (ObjectPosition, RemoveObject, PeerInfo, CommsRequest) and the notes history
    /// in segments (docs/network/jfp2/protocol.md §7.5, §9.4, §9.8; plan stage 10), between nodes that register no legacy plugin.
    /// </summary>
    public class Jfp2RemainingKindsTests
    {
        static TestNode Node(TestMesh mesh, string ip, Jfp2Profile? profile = null) => mesh.Add(ip, 6112, new Jfp2Plugin(profile: profile));

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static TestNode Hub(TestMesh mesh, Jfp2Profile? profile = null)
        {
            TestNode hub = Node(mesh, "203.0.113.1", profile);
            hub.Core.Mesh.Create(false, 0, false, "");
            return hub;
        }

        static TestNode Joined(TestMesh mesh, TestNode hub, string ip, Jfp2Profile? profile = null)
        {
            TestNode node = Node(mesh, ip, profile);
            node.Core.Mesh.JoinOverJfp2(hub.EndPoint, 0);
            mesh.Run(2);
            return node;
        }

        static IdentityUpdate BoatIdentity(uint id) => new()
        {
            ObjectId = id, IsAircraft = false, IsPlane = false, Callsign = "", Model = "Ferry", Livery = "", IcaoType = "",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 0,
        };

        static bool IsHistorySegment(byte[] data) => data[0] == Envelope.Magic && (data[2] & 0x09) == 0x01 && data[7] == MessageClasses.NotesHistory && data[11] > 1;

        /// <summary>Three users with <paramref name="notesEach"/> notes of about 700 bytes each: far more than one datagram.</summary>
        static NotesBundle History(int notesEach) => new()
        {
            Scope = CommsScope.All,
            Users = Enumerable.Range(1, 3).Select(u => new NotesUser
            {
                Guid = new Guid(u, 0, 0, new byte[8]), Nickname = "Pilot " + u, Callsign = "N" + u,
                Notes = Enumerable.Range(0, notesEach).Select(n => new CommsNote
                {
                    NoteId = (uint)(u * 1000 + n), Age = n * 1.5f, Channel = (ushort)(n % 3), Text = $"u{u}n{n} " + new string('x', 700),
                }).ToList(),
            }).ToList(),
        };

        static void AssertSameHistory(NotesBundle expected, NotesBundle actual)
        {
            Assert.Equal(expected.Users.Count, actual.Users.Count);
            for (int u = 0; u < expected.Users.Count; u++)
            {
                Assert.Equal(expected.Users[u].Guid, actual.Users[u].Guid);
                Assert.Equal(expected.Users[u].Nickname, actual.Users[u].Nickname);
                Assert.Equal(expected.Users[u].Callsign, actual.Users[u].Callsign);
                Assert.Equal(expected.Users[u].Notes.Select(n => (n.NoteId, n.Age, n.Channel, n.Text)),
                    actual.Users[u].Notes.Select(n => (n.NoteId, n.Age, n.Channel, n.Text)));
            }
        }

        // ------------------------------------------------------------------ the four kinds

        [Fact]
        public void ABoatAndItsRemoval_APeerInfoAndACommsRequest_CrossAJfp2OnlySession()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Joined(mesh, h, "198.51.100.2");
            Assert.True(Jfp2Of(a).CanCarry(h.Id, MessageKind.ObjectPosition));

            a.Core.Objects.SetIdentity(a.Id, BoatIdentity(7));
            a.Core.SendTo(h.Id, new ObjectPositionUpdate { ObjectId = 7, NetTime = 5, Latitude = 51.5, Longitude = -0.25, Altitude = 3, Heading = 90, Height = 2.5f, StateFlags = PositionStateFlags.ElevationCorrection }, false);
            a.Core.SendTo(h.Id, new PeerInfo { Share = ShareCockpitFlags.Cockpit | ShareCockpitFlags.NavControls, Nickname = "Ann", Guid = new Guid(5, 0, 0, new byte[8]), Hub = true, SimulatorConnected = true, AtcAirport = "EGLL", AtcLevel = 3, AtcFrequency = 1185, ActivityCircle = 4, Version = "26.7", Simulator = "MSFS" }, false);
            a.Core.SendTo(h.Id, new CommsRequest { Scope = CommsScope.Global }, true);
            a.Core.SendTo(h.Id, new RemoveObject { ObjectId = 7 }, true);
            mesh.Run(0.5);

            // the identity of the boat went ahead of its first position, as for an aircraft
            Assert.Equal("Ferry", Assert.Single(h.Messages<IdentityUpdate>()).Model);
            ObjectPositionUpdate boat = Assert.Single(h.Messages<ObjectPositionUpdate>());
            Assert.Equal((7u, 51.5, -0.25, 90f, 2.5f, PositionStateFlags.ElevationCorrection), (boat.ObjectId, boat.Latitude, boat.Longitude, boat.Heading, boat.Height, boat.StateFlags));
            PeerInfo info = Assert.Single(h.Messages<PeerInfo>());
            Assert.Equal(("Ann", true, true, false, "EGLL", (byte)3, (short)1185, (byte)4, "26.7", "MSFS"),
                (info.Nickname, info.Hub, info.SimulatorConnected, info.Atc, info.AtcAirport, info.AtcLevel, info.AtcFrequency, info.ActivityCircle, info.Version, info.Simulator));
            Assert.Equal(ShareCockpitFlags.Cockpit | ShareCockpitFlags.NavControls, info.Share);
            Assert.Equal(CommsScope.Global, Assert.Single(h.Messages<CommsRequest>()).Scope);
            Assert.Equal(7u, Assert.Single(h.Messages<RemoveObject>()).ObjectId);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
        }

        [Fact]
        public void TheFourKinds_RoundTripTheirCodecs_WithinTheirLimits()
        {
            var peerInfo = new PeerInfo
            {
                Share = ShareCockpitFlags.FlightControls, Nickname = new string('n', 100), Guid = Guid.NewGuid(), Atc = true,
                AtcAirport = "KJFK-LONG", AtcLevel = 5, AtcFrequency = -1, ActivityCircle = 9, Version = new string('v', 100), Simulator = new string('s', 100),
            };
            var codec = new PeerInfoV1Codec();
            var buffer = new byte[2000];
            int length = codec.Encode(peerInfo, buffer);
            Assert.True(length <= PeerInfoV1Codec.MaxSize);
            PeerInfo back = codec.Decode(buffer.AsSpan(0, length));
            Assert.Equal((32, 8, 32, 32), (back.Nickname.Length, back.AtcAirport.Length, back.Version.Length, back.Simulator.Length));
            Assert.Equal((peerInfo.Guid, peerInfo.Atc, peerInfo.Hub, peerInfo.SimulatorConnected, peerInfo.AtcFrequency), (back.Guid, back.Atc, back.Hub, back.SimulatorConnected, back.AtcFrequency));

            var boat = new ObjectPositionUpdate
            {
                ObjectId = uint.MaxValue - 1, NetTime = 1.5, Latitude = -33.9, Longitude = 151.2, Altitude = 12, Pitch = 1, Bank = 2, Heading = 3, VelocityX = 4, VelocityY = 5, VelocityZ = 6,
                AngularVelocityX = 7, AngularVelocityY = 8, AngularVelocityZ = 9, AccelerationX = 10, AccelerationY = 11, AccelerationZ = 12, Height = 13, StateFlags = PositionStateFlags.OnGround | PositionStateFlags.Paused,
            };
            var boatCodec = new ObjectPositionUpdateV1Codec();
            Assert.Equal(ObjectPositionUpdateV1Codec.Size, boatCodec.Encode(boat, buffer));
            Assert.Equal(boat, boatCodec.Decode(buffer));
            Assert.Equal(RemoveObjectV1Codec.Size, new RemoveObjectV1Codec().Encode(new RemoveObject { ObjectId = 9 }, buffer));
            Assert.Equal(9u, new RemoveObjectV1Codec().Decode(buffer).ObjectId);
            foreach (CommsScope scope in Enum.GetValues<CommsScope>())
            {
                new CommsRequestV1Codec().Encode(new CommsRequest { Scope = scope }, buffer);
                Assert.Equal(scope, new CommsRequestV1Codec().Decode(buffer).Scope);
            }
        }

        // ------------------------------------------------------------------ the notes history

        [Fact]
        public void ALargeNotesHistory_ReachesALateJoiner_InSegments_AndIsDeliveredOnce()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode late = Joined(mesh, h, "198.51.100.2");
            NotesBundle history = History(40); // 120 notes of about 700 bytes: about 85 KB, some 78 segments

            h.Core.SendTo(late.Id, history, true);
            mesh.Run(3);

            AssertSameHistory(history, Assert.Single(late.Messages<NotesBundle>()));
            var segments = mesh.Network.Log.Where(d => IsHistorySegment(d.Data) && d.From.Equals(h.EndPoint)).ToList();
            byte count = Assert.Single(segments.Select(d => d.Data[11]).Distinct());
            Assert.InRange(count, (byte)70, (byte)90);
            Assert.Equal(count, segments.Select(d => d.Data[10]).Distinct().Count());
            Assert.Equal(0, Jfp2Of(h).GuaranteedPendingCount);
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
        }

        [Fact]
        public void ANotesHistory_SurvivesLostSegments_ByResendingThem()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode late = Joined(mesh, h, "198.51.100.2");
            NotesBundle history = History(10);
            var firstTry = new HashSet<byte>();
            mesh.Network.Filter = (from, to, data) =>
            {
                // every third segment is lost the first time it is sent
                if (IsHistorySegment(data) && data[10] % 3 == 0 && firstTry.Add(data[10])) return false;
                return true;
            };

            h.Core.SendTo(late.Id, history, true);
            mesh.Run(1);
            Assert.Empty(late.Messages<NotesBundle>()); // not complete: segments 0, 3, 6 are missing
            Assert.True(Jfp2Of(h).GuaranteedPendingCount > 0);
            mesh.Run(5);

            AssertSameHistory(history, Assert.Single(late.Messages<NotesBundle>()));
            Assert.Equal(0, Jfp2Of(h).GuaranteedPendingCount);
        }

        [Fact]
        public void ANotesHistory_ToAPeerBehindARelay_ArrivesNoteByNote()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            TestNode b = Node(mesh, "198.51.100.3");
            mesh.Partition(a, b); // a reaches b through h only
            a.Core.Mesh.JoinOverJfp2(h.EndPoint, 0);
            b.Core.Mesh.JoinOverJfp2(h.EndPoint, 0);
            mesh.Run(25);
            Assert.Equal(h.Id, Jfp2Of(a).NextHopNode(b.Id));
            NotesBundle history = History(2);

            a.Core.SendTo(b.Id, history, true);
            mesh.Run(3);

            Assert.DoesNotContain(mesh.Network.Log, d => IsHistorySegment(d.Data));
            Assert.Equal(6, b.Messages<NotesBundle>().Sum(m => m.Users.Sum(u => u.Notes.Count)));
        }

        [Fact]
        public void ANotesHistory_ToABuildWithoutTheCapability_ArrivesNoteByNote()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode old = Joined(mesh, h, "198.51.100.2", Jfp2Profile.Default.WithCapabilities(0));

            h.Core.SendTo(old.Id, History(2), true);
            mesh.Run(3);

            Assert.DoesNotContain(mesh.Network.Log, d => IsHistorySegment(d.Data));
            Assert.Equal(6, old.Messages<NotesBundle>().Sum(m => m.Users.Sum(u => u.Notes.Count)));
        }

        [Fact]
        public void ANotesHistory_BeyondTheSegmentsOfAMessage_IsCutToWholeNotes()
        {
            var codec = new NotesHistoryV1Codec();
            NotesBundle huge = History(500); // 1,500 notes: more than the 255 segments a message can have
            var buffer = new byte[NotesHistoryV1Codec.MaxSize];
            int length = codec.Encode(huge, buffer);
            Assert.True(length <= NotesHistoryV1Codec.MaxSize);
            NotesBundle back = codec.Decode(buffer.AsSpan(0, length));
            int notes = back.Users.Sum(u => u.Notes.Count);
            Assert.InRange(notes, 300, 1499);
            Assert.Equal(huge.Users[0].Notes[0].Text, back.Users[0].Notes[0].Text);
        }
    }
}
