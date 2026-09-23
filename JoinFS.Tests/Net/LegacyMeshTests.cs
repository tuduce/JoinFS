using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// End-to-end behaviour of the new network stack (NetworkCore + MeshManager + LegacyPlugin)
    /// with several nodes on an in-memory network: sessions, introductions, liveness, relaying,
    /// guaranteed delivery and departures. Wire bytes are pinned separately by the golden tests;
    /// these check that the ported state machine behaves like a mesh.
    /// </summary>
    public class LegacyMeshTests
    {
        static (TestMesh Mesh, TestNode Hub, TestNode A, TestNode B) Session()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1");
            TestNode a = mesh.Add("198.51.100.2");
            TestNode b = mesh.Add("192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            return (mesh, hub, a, b);
        }

        [Fact]
        public void Join_EstablishesBothWays()
        {
            var (mesh, hub, a, _) = Session();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            Assert.Equal(hub.Core.Mesh.Suid, a.Core.Mesh.Suid);
            Assert.True(a.Knows(hub));
            Assert.True(hub.Knows(a));
            Assert.Contains(a.Events, e => e.Kind == NetworkEventKind.SessionJoined);
            Assert.Contains(a.Events, e => e.Kind == NetworkEventKind.PeerEstablished && e.Node == hub.Id);
            Assert.Contains(hub.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == a.Id);
        }

        [Fact]
        public void ThirdNode_IsIntroducedAndConnectsDirectly()
        {
            var (mesh, hub, a, b) = Session();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(5);

            Assert.True(a.Knows(b));
            Assert.True(b.Knows(a));
            Assert.True(a.Core.Peers.TryGet(b.Id, out Peer peer) && peer.RouteIsOwnEndPoint);
        }

        [Fact]
        public void WrongPassword_IsRejected()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1");
            TestNode a = mesh.Add("198.51.100.2");
            hub.Core.Mesh.Create(false, NetHash.HashPassword("secret"), false, "");
            a.Core.Mesh.Join(hub.EndPoint, NetHash.HashPassword("wrong"));
            mesh.Run(3);

            Assert.False(a.Core.Mesh.Connected);
            Assert.Equal(JoinResult.PasswordRequired, a.Core.Mesh.ActiveJoinResult);
        }

        [Fact]
        public void UnreachablePair_IsRelayedThroughHub()
        {
            var (mesh, hub, a, b) = Session();
            mesh.Partition(a, b);
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            // pathfinder runs every 5 s
            mesh.Run(12);

            Assert.True(a.Core.Peers.TryGet(b.Id, out Peer route));
            Assert.True(route.SendEstablished);
            Assert.Equal(hub.EndPoint, route.RouteEndPoint);

            a.Core.Objects.SetIdentity(a.Id, new IdentityUpdate
            {
                ObjectId = 7, IsAircraft = true, IsPlane = true, Callsign = "A1", Model = "M", Livery = "", IcaoType = "C172", IcaoAirline = "",
                Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
            });
            a.Core.SendTo(b.Id, new PositionUpdate { ObjectId = 7, NetTime = 1, Latitude = 10, StateFlags = PositionStateFlags.UserControlled }, false);
            mesh.Run(0.1);

            var (meta, position) = Assert.Single(b.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.True(meta.Forwarded);
            Assert.Equal(10, position.Latitude);
            Assert.Equal("A1", b.Messages<IdentityUpdate>().Single().Callsign);
        }

        [Fact]
        public void Guaranteed_SurvivesLossAndArrivesOnce()
        {
            var (mesh, hub, a, _) = Session();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            int sent = 0;
            mesh.Network.Filter = (from, to, data) => !(from.Equals(a.EndPoint) && data[0] == 0x0B && data[2] == 0x02 && sent++ < 3);
            a.Core.SendTo(hub.Id, new EventUpdate { ObjectId = 1, EventId = 42, Data = 7 }, true);
            mesh.Run(10);

            // three dropped, one delivered, then acknowledged - no further resends
            Assert.Equal(4, sent);
            Assert.Equal(42u, Assert.Single(hub.Messages<EventUpdate>()).EventId);
        }

        [Fact]
        public void LargeGuaranteed_IsSegmentedAndReassembled()
        {
            var (mesh, hub, a, _) = Session();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            string text = new('n', 3000);
            a.Core.SendTo(hub.Id, new NotesBundle
            {
                Scope = CommsScope.Single,
                Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "a", Callsign = "c", Notes = [new CommsNote { NoteId = 1, Channel = 1, Text = text }] }],
            }, true);
            mesh.Run(1);

            Assert.Equal(text, Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
        }

        [Fact]
        public void Leave_RemovesNodeEverywhere()
        {
            var (mesh, hub, a, b) = Session();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(5);
            a.Core.Mesh.Leave();
            mesh.Run(1);

            Assert.False(a.Core.Mesh.Connected);
            Assert.Equal(0, a.Core.Peers.Count);
            Assert.False(hub.Core.Peers.Contains(a.Id));
            Assert.False(b.Core.Peers.Contains(a.Id));
            Assert.Contains(hub.Events, e => e.Kind == NetworkEventKind.PeerLeft && e.Node == a.Id);
        }

        [Fact]
        public void SilentNode_ExpiresAfterThirtySeconds()
        {
            var (mesh, hub, a, _) = Session();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            mesh.Partition(hub, a);
            mesh.Run(25);
            Assert.True(hub.Core.Peers.Contains(a.Id));
            mesh.Run(10);
            Assert.False(hub.Core.Peers.Contains(a.Id));
        }

        [Fact]
        public void ObjectMessages_AreIgnoredOutsideASession()
        {
            var mesh = new TestMesh();
            TestNode a = mesh.Add("198.51.100.2");
            TestNode b = mesh.Add("192.0.2.3");
            a.Core.SendToEndPoint(b.EndPoint, new WeatherUpdate { Metar = "X" }, false);
            a.Core.SendToEndPoint(b.EndPoint, new StatusUpdate { Guid = Guid.NewGuid(), AppVersion = "1", AtcAirport = "" }, false);
            mesh.Run(0.1);

            Assert.Empty(b.Messages<WeatherUpdate>());
            Assert.Single(b.Messages<StatusUpdate>());
        }
    }
}
