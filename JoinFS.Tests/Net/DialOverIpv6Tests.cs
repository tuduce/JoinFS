using System.Net;
using System.Net.Sockets;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Dialling over IPv6 (docs/jfp2/design-future.md §4.1, "Dial" and "No flipping"): a dual-stack node
    /// joins a dual-stack hub over IPv6 with legacy and JFP2 both on that path, an IPv4-only node shares
    /// the session, and a Join tries its address list in order.
    /// </summary>
    public class DialOverIpv6Tests
    {
        static TestNode Dual(TestMesh mesh, string ip4, string ip6) => mesh.AddDualStack(ip4, ip6, 6112, new LegacyPlugin(), new Jfp2Plugin());
        static TestNode V4Only(TestMesh mesh, string ip4) => mesh.Add(ip4, 6112, new LegacyPlugin(), new Jfp2Plugin());

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static IdentityUpdate Identity(uint id, string callsign) => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = callsign, Model = "M", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static void SendPosition(TestNode from, TestNode to, uint id, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id, "JF" + id));
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        static IPEndPoint EndPointOf(TestNode node, TestNode peer) => node.Core.Peers.TryGet(peer.Id, out Peer p) ? p.EndPoint : null!;

        static bool Between(TestMesh mesh, TestNode a, TestNode b, AddressFamily family) =>
            mesh.Network.Log.Any(d => d.From.AddressFamily == family && d.To.AddressFamily == family
                && (a.OwnsAddress(d.From) && b.OwnsAddress(d.To) || b.OwnsAddress(d.From) && a.OwnsAddress(d.To)));

        [Fact]
        public void DualStackNode_JoinsDualStackHubOverIPv6_WithLegacyAndJfp2OnThatPath()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint6!, 0);
            mesh.Run(3);

            Assert.True(a.Knows(hub));
            Assert.True(hub.Knows(a));
            Assert.Equal(hub.EndPoint6, EndPointOf(a, hub));
            Assert.Equal(a.EndPoint6, EndPointOf(hub, a));
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.RemoveObject)!.Name);

            SendPosition(a, hub, 3, 12.5);
            SendPosition(hub, a, 4, 21.5);
            mesh.Run(0.5);

            Assert.Equal(12.5, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(21.5, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
            Assert.True(Between(mesh, a, hub, AddressFamily.InterNetworkV6));
            Assert.False(Between(mesh, a, hub, AddressFamily.InterNetwork));
        }

        [Fact]
        public void IPv4OnlyNode_InTheSameSession_ExchangesPositionsWithBothOverIPv4()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode c = V4Only(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint6!, 0);
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            Assert.True(a.Knows(hub) && hub.Knows(a));
            Assert.True(c.Knows(hub) && hub.Knows(c));
            Assert.True(a.Knows(c) && c.Knows(a));
            Assert.Equal(hub.EndPoint6, EndPointOf(a, hub));
            Assert.Equal(hub.EndPoint, EndPointOf(c, hub));
            Assert.Equal(c.EndPoint, EndPointOf(a, c));
            Assert.Equal(a.EndPoint, EndPointOf(c, a));

            SendPosition(a, c, 5, 1.5);
            SendPosition(c, a, 6, 2.5);
            SendPosition(c, hub, 7, 3.5);
            SendPosition(hub, a, 8, 4.5);
            mesh.Run(0.5);

            Assert.Equal(1.5, Assert.Single(c.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(2.5, a.Messages<PositionUpdate>().Single(p => p.ObjectId == 6).Latitude);
            Assert.Equal(3.5, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(4.5, a.Messages<PositionUpdate>().Single(p => p.ObjectId == 8).Latitude);
            Assert.False(Between(mesh, a, c, AddressFamily.InterNetworkV6));
        }

        static int Sent(TestMesh mesh, TestNode from, IPEndPoint to) => mesh.Network.Log.Count(d => from.OwnsAddress(d.From) && d.To.Equals(to));

        [Fact]
        public void Join_TriesTheNextAddress_WhenTheFirstGetsNoAnswerWithinTheDialTimeout()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            mesh.CutIpv6(a, hub);
            a.Core.Mesh.Join([hub.EndPoint6!, hub.EndPoint], 0);

            mesh.Run(4);
            Assert.False(a.Core.Mesh.Connected);
            Assert.Equal(0, Sent(mesh, a, hub.EndPoint));       // still waiting on the first address

            mesh.Run(6);
            Assert.True(a.Core.Mesh.Connected);
            Assert.True(a.Knows(hub) && hub.Knows(a));
            Assert.Equal(hub.EndPoint, EndPointOf(a, hub));
            Assert.Equal(a.EndPoint, EndPointOf(hub, a));
            Assert.Contains(a.Logs, l => l.StartsWith("NETWORK: Dial") && l.Contains(hub.EndPoint.ToString()));
        }

        [Fact]
        public void Join_AfterMovingOn_NoLongerRetriesTheAbandonedAddress()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            mesh.CutIpv6(a, hub);
            a.Core.Mesh.Join([hub.EndPoint6!, hub.EndPoint], 0);
            mesh.Run(7);
            Assert.True(a.Core.Mesh.Connected);
            mesh.Run(40);   // the hub's IPv6 candidate is probed for its 30 s window (HubPathUpgradeTests); the Join must not be resent

            int before = Sent(mesh, a, hub.EndPoint6!);
            mesh.Run(10);
            Assert.Equal(before, Sent(mesh, a, hub.EndPoint6!));
        }

        [Fact]
        public void Join_StaysOnTheFirstAddress_WhenItAnswers()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join([hub.EndPoint6!, hub.EndPoint], 0);
            mesh.Run(12);

            Assert.True(a.Knows(hub));
            Assert.Equal(hub.EndPoint6, EndPointOf(a, hub));
            Assert.Equal(0, Sent(mesh, a, hub.EndPoint));
        }

        [Fact]
        public void Join_StopsDialling_WhenTheHubRefusesTheJoin()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, NetHash.HashPassword("secret"), false, "");
            a.Core.Mesh.Join([hub.EndPoint6!, hub.EndPoint], NetHash.HashPassword("wrong"));
            mesh.Run(12);

            Assert.False(a.Core.Mesh.Connected);
            Assert.Equal(JoinResult.PasswordRequired, a.Core.Mesh.ActiveJoinResult);
            Assert.Equal(0, Sent(mesh, a, hub.EndPoint));
        }

        [Fact]
        public void Join_WithOneAddress_BehavesAsBefore()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = V4Only(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join([hub.EndPoint], 0);
            mesh.Run(3);

            Assert.True(a.Knows(hub) && hub.Knows(a));
        }

        [Fact]
        public void DatagramOverTheOtherFamily_DoesNotReplaceThePeersEndPoint_ButCountsAsHeard()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Network.Pump();                       // the hub has heard the Join, and has not been answered yet
            Assert.True(hub.Core.Peers.TryGet(a.Id, out Peer peer));
            Assert.False(peer.SendEstablished);

            mesh.Clock.Advance(1);
            a.Core.SendToEndPoint(hub.EndPoint6!, new Pulse { Suid = hub.Core.Mesh.Suid, Time = 1 }, guaranteed: false);
            mesh.Network.Pump();

            Assert.True(peer.ReceiveEstablished);
            Assert.Equal(a.EndPoint, peer.EndPoint);
            Assert.Equal(a.EndPoint, peer.RouteEndPoint);
            Assert.Contains(mesh.Network.Log, d => d.From.Equals(a.EndPoint6) && d.To.Equals(hub.EndPoint6));
        }

        [Fact]
        public void PeerThatSendsOverBothFamilies_StaysOnItsFirstPath()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint6!, 0);
            mesh.Run(3);

            // stray datagrams over IPv4 from the same node, every 100 ms, while positions flow
            for (int i = 0; i < 40; i++)
            {
                a.Core.SendToEndPoint(hub.EndPoint, new Pulse { Suid = hub.Core.Mesh.Suid, Time = i }, guaranteed: false);
                SendPosition(a, hub, 3, i);
                mesh.Run(0.1);
            }

            Assert.Equal(a.EndPoint6, EndPointOf(hub, a));
            Assert.Equal(hub.EndPoint6, EndPointOf(a, hub));
            Assert.True(hub.Knows(a));
            Assert.Equal(40, hub.Messages<PositionUpdate>().Count());
        }

        [Fact]
        public void BannedAddress_InTheSendersLegacyId_IsDroppedWhateverTheSourceFamily()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode b = Dual(mesh, "192.0.2.3", "2001:db8::3");
            hub.Core.Mesh.Create(false, 0, false, "");
            hub.Core.BanIP("198.51.100.2");
            a.Core.Mesh.Join(hub.EndPoint6!, 0);        // back over IPv6: the id still says the banned address
            b.Core.Mesh.Join(hub.EndPoint6!, 0);
            mesh.Run(6);

            Assert.False(hub.Core.Peers.Contains(a.Id));
            Assert.False(a.Core.Mesh.Connected);
            Assert.True(hub.Knows(b));
        }

        [Fact]
        public void BannedIPv6Address_BansItsWhole64()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8:0:1::2");
            TestNode neighbour = Dual(mesh, "192.0.2.3", "2001:db8:0:1::99");
            TestNode other = Dual(mesh, "192.0.2.5", "2001:db8:0:2::5");
            hub.Core.Mesh.Create(false, 0, false, "");
            hub.Core.BanIP("2001:db8:0:1::abcd");       // any address of the /64 stands for all of it
            a.Core.Mesh.Join(hub.EndPoint6!, 0);
            neighbour.Core.Mesh.Join(hub.EndPoint6!, 0);
            other.Core.Mesh.Join(hub.EndPoint6!, 0);
            mesh.Run(6);

            Assert.False(a.Core.Mesh.Connected);
            Assert.False(neighbour.Core.Mesh.Connected);
            Assert.True(other.Core.Mesh.Connected);
            Assert.True(hub.Knows(other));
        }
    }
}
