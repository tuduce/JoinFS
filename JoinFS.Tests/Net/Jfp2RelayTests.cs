using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// JFP2 as a per-hop protocol: a node talks JFP2 or legacy to its next hop, and a hub relays or
    /// translates between them. Includes the topology that broke the first field test: a hub and a
    /// client behind one NAT, both port-forwarded/known by the same public IP:port, so a remote node
    /// reaches both at one endpoint and only the node identity in the handshake tells them apart.
    /// </summary>
    public class Jfp2RelayTests
    {
        static readonly IPEndPoint Public = new(IPAddress.Parse("203.0.113.1"), 6112);

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static IdentityUpdate Identity(uint id, string callsign) => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = callsign, Model = "M", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static void SendPosition(TestNode from, TestNode to, uint id, string callsign, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id, callsign));
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        static int Jfp2Datagrams(TestMesh mesh, IPEndPoint from, IPEndPoint to) =>
            mesh.Network.Log.Count(d => d.From.Equals(from) && d.To.Equals(to) && d.Data[0] == Envelope.Magic && (d.Data[2] & (byte)EnvelopeFlags.Internal) == 0);

        static bool IsLan(IPEndPoint endPoint) => endPoint.Address.GetAddressBytes() is [192, 168, 1, _];

        /// <summary>
        /// A (198.51.100.2, on its own) reaches the hub and B at the same public endpoint 203.0.113.1:6112,
        /// where the router forwards to <see cref="Steer"/> (the hub, as configured; B when a test flips it).
        /// The hub and B share a LAN and reach each other directly.
        /// </summary>
        sealed class SharedEndpoint
        {
            public readonly TestMesh Mesh = new();
            public readonly TestNode Hub, A, B;
            public IPEndPoint Steer;

            public SharedEndpoint()
            {
                Hub = Mesh.AddBehindNat("192.168.1.10", "203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
                B = Mesh.AddBehindNat("192.168.1.20", "203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
                A = Mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin());
                Steer = Hub.EndPoint;
                Mesh.Network.Nat = (from, to) =>
                {
                    if (to.Equals(Public)) to = Steer;
                    if (IsLan(from) && !IsLan(to)) from = Public;
                    return (from, to);
                };
                Hub.Core.Mesh.Create(false, 0, false, "");
                A.Core.Mesh.Join(Public, 0);
                B.Core.Mesh.Join(Hub.EndPoint, 0);
            }
        }

        [Fact]
        public void SharedEndpoint_BothLinksAreJfp2_HubRelaysBetweenThem()
        {
            var t = new SharedEndpoint();
            t.Mesh.Run(20);

            // A cannot tell the hub from B by where they answer, but it knows who answered
            Assert.Equal(t.Hub.Id, Jfp2Of(t.A).NextHopNode(t.Hub.Id));
            Assert.Equal(t.Hub.Id, Jfp2Of(t.A).NextHopNode(t.B.Id));
            Assert.Equal(t.B.Id, Jfp2Of(t.Hub).NextHopNode(t.B.Id));
            Assert.Equal(t.A.Id, Jfp2Of(t.Hub).NextHopNode(t.A.Id));
            Assert.True(t.A.Core.Peers.TryGet(t.B.Id, out Peer peerB));
            Assert.Equal(PeerLinkState.Negotiated, Jfp2Of(t.A).DescribeLink(peerB));
            Assert.Equal("JFP2", t.A.Core.Route(t.B.Id, MessageKind.Position)!.Name);

            // A -> B: JFP2 to the hub (at the shared endpoint), forwarded byte for byte to B
            t.Mesh.Network.Log.Clear();
            SendPosition(t.A, t.B, 5, "AB1", 41.5);
            t.Mesh.Run(0.2);
            var (meta, position) = Assert.Single(t.B.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(t.A.Id, meta.Sender);
            Assert.Equal(41.5, position.Latitude);
            Assert.Equal("AB1", Assert.Single(t.B.Messages<IdentityUpdate>()).Callsign);
            Assert.Equal(2, Jfp2Datagrams(t.Mesh, t.A.EndPoint, Public)); // Identity, Position
            Assert.Equal(2, Jfp2Datagrams(t.Mesh, t.Hub.EndPoint, t.B.EndPoint));
            Assert.Empty(t.Hub.Messages<PositionUpdate>()); // it was relayed, not consumed

            // B -> A: B reaches A directly, but A's answers to B's Hello go to the shared endpoint, which
            // this router forwards to the hub - B cannot verify that A hears it over JFP2, so B keeps
            // the legacy wire for A and A still gets the aircraft
            t.Mesh.Network.Log.Clear();
            SendPosition(t.B, t.A, 6, "BA1", -12.25);
            t.Mesh.Run(0.2);
            var (metaA, positionA) = Assert.Single(t.A.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(t.B.Id, metaA.Sender);
            Assert.Equal(-12.25, positionA.Latitude);
            Assert.Equal("Legacy", t.B.Core.Route(t.A.Id, MessageKind.Position)!.Name);
        }

        [Fact]
        public void SharedEndpoint_GuaranteedMessageThroughTheHub_IsAcknowledgedEndToEnd()
        {
            var t = new SharedEndpoint();
            t.Mesh.Run(20);

            t.A.Core.SendTo(t.B.Id, new EventUpdate { ObjectId = 1, EventId = 77, Data = 3 }, true);
            t.Mesh.Run(12);

            var (meta, evt) = Assert.Single(t.B.MessagesWithMeta<EventUpdate>());
            Assert.Equal(t.A.Id, meta.Sender);
            Assert.Equal(77u, evt.EventId);
            // acknowledged by B through the hub: no retransmissions, nothing left pending
            Assert.Equal(1, t.Mesh.Network.Log.Count(d => d.From.Equals(t.A.EndPoint) && d.To.Equals(Public) && d.Data[0] == Envelope.Magic
                && (d.Data[2] & (byte)EnvelopeFlags.Guaranteed) != 0 && (d.Data[2] & (byte)EnvelopeFlags.Internal) == 0));
        }

        [Fact]
        public void SharedEndpoint_RouterSteersToTheOtherNode_TrafficKeepsFlowingThenRecovers()
        {
            var t = new SharedEndpoint();
            t.Mesh.Run(20);

            // the router now delivers the shared endpoint to B instead of the hub
            t.Steer = t.B.EndPoint;
            t.Mesh.Run(40);
            Assert.Contains(t.A.Logs, l => l.Contains("no longer verified") && l.Contains("is now answered by"));
            t.Mesh.Network.Log.Clear();
            t.A.Received.Clear(); t.B.Received.Clear(); t.Hub.Received.Clear();
            SendPosition(t.A, t.B, 5, "AB2", 10);
            SendPosition(t.A, t.Hub, 7, "AH2", 20);
            t.Mesh.Run(1);
            Assert.Equal(10, Assert.Single(t.B.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(20, Assert.Single(t.Hub.Messages<PositionUpdate>()).Latitude);

            // and back: the sessions are re-verified and everything still arrives
            t.Steer = t.Hub.EndPoint;
            t.Mesh.Run(40);
            t.A.Received.Clear(); t.B.Received.Clear(); t.Hub.Received.Clear();
            SendPosition(t.A, t.B, 5, "AB3", 11);
            SendPosition(t.A, t.Hub, 7, "AH3", 21);
            t.Mesh.Run(1);
            Assert.Equal(11, Assert.Single(t.B.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(21, Assert.Single(t.Hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(t.Hub.Id, Jfp2Of(t.A).NextHopNode(t.Hub.Id));
            Assert.Equal(t.Hub.Id, Jfp2Of(t.A).NextHopNode(t.B.Id));
        }

        static (TestMesh Mesh, TestNode Hub, TestNode A, TestNode B) HubWithTwoBehindIt(bool aJfp2, bool bJfp2)
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode a = mesh.Add("198.51.100.2", 6112, aJfp2 ? [new LegacyPlugin(), new Jfp2Plugin()] : [new LegacyPlugin()]);
            TestNode b = mesh.Add("192.0.2.3", 6112, bJfp2 ? [new LegacyPlugin(), new Jfp2Plugin()] : [new LegacyPlugin()]);
            mesh.Partition(a, b); // they can only reach each other through the hub
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);
            return (mesh, hub, a, b);
        }

        [Fact]
        public void Jfp2Origin_LegacyTargetBehindJfp2Hub_HubTranslates()
        {
            var (mesh, hub, a, b) = HubWithTwoBehindIt(aJfp2: true, bJfp2: false);

            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b.Id));
            Assert.Equal("JFP2", a.Core.Route(b.Id, MessageKind.Position)!.Name);
            Assert.True(a.Core.Peers.TryGet(b.Id, out Peer peerB) && peerB.Relayed && peerB.RouteVia == hub.Id);
            Assert.False(peerB.RouteIsOwnEndPoint);

            SendPosition(a, b, 9, "TR1", 33);
            a.Core.SendTo(b.Id, new EventUpdate { ObjectId = 9, EventId = 5, Data = 1 }, true);
            mesh.Run(8);

            var (meta, position) = Assert.Single(b.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(33, position.Latitude);
            Assert.Equal("TR1", Assert.Single(b.Messages<IdentityUpdate>()).Callsign);
            Assert.Equal(5u, Assert.Single(b.Messages<EventUpdate>()).EventId);
            Assert.Empty(hub.Messages<PositionUpdate>());
        }

        [Fact]
        public void LegacyOrigin_Jfp2TargetBehindJfp2Hub_HubRelaysLegacyAndTargetAnswersOverJfp2()
        {
            var (mesh, hub, a, b) = HubWithTwoBehindIt(aJfp2: false, bJfp2: true);

            SendPosition(a, b, 4, "LG2", 15);
            mesh.Run(1);
            var (meta, position) = Assert.Single(b.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(15, position.Latitude);

            // B answers through JFP2 to the hub, which translates for the legacy A
            Assert.Equal(hub.Id, Jfp2Of(b).NextHopNode(a.Id));
            SendPosition(b, a, 8, "JF2", 25);
            mesh.Run(1);
            var (metaA, positionA) = Assert.Single(a.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(b.Id, metaA.Sender);
            Assert.Equal(25, positionA.Latitude);
            Assert.Equal("JF2", Assert.Single(a.Messages<IdentityUpdate>()).Callsign);
        }

        [Fact]
        public void BothJfp2_ReachEachOtherThroughTheHub_WhenTheyCannotConnectDirectly()
        {
            var (mesh, hub, a, b) = HubWithTwoBehindIt(aJfp2: true, bJfp2: true);

            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b.Id));
            Assert.Equal(hub.Id, Jfp2Of(b).NextHopNode(a.Id));
            mesh.Network.Log.Clear();
            SendPosition(a, b, 3, "JJ1", 5);
            mesh.Run(0.5);
            Assert.Equal(5, Assert.Single(b.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(2, Jfp2Datagrams(mesh, a.EndPoint, hub.EndPoint));
            Assert.Equal(2, Jfp2Datagrams(mesh, hub.EndPoint, b.EndPoint));
        }

        [Fact]
        public void HelloWithoutNodeIdentity_IsIgnored()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            // an older JFP2 build's Hello: right handshake, but it never says who it is
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor, ProtoMajorMax = Envelope.ProtoMajor, SelfAssignedId = 77,
                Offers = [new SchemaOffer(false, MessageClasses.Position, 1, 1)],
            };
            byte[] payload = hello.Serialize();
            var envelope = new Envelope(EnvelopeFlags.Internal, 77, 0, MessageClasses.Hello);
            byte[] data = new byte[envelope.WireSize + payload.Length];
            payload.CopyTo(data, envelope.WriteTo(data));
            mesh.Network.Log.Clear();
            a.Core.Transport.Send(hub.EndPoint, data);
            mesh.Run(1);

            Assert.False(Jfp2Of(hub).IsNegotiated(a.Id));
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && d.To.Equals(a.EndPoint)
                && d.Data[0] == Envelope.Magic && d.Data[7] == MessageClasses.HelloAck);
        }

        [Fact]
        public void PeerThatForgetsItsSessions_IsRecoveredByTheKeepalive()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(4);
            Assert.True(Jfp2Of(hub).IsNegotiated(a.Id));

            // the hub's JFP2 state is gone (a restart): a still sends with ids the hub no longer knows
            Jfp2Of(hub).OnSessionReset();
            mesh.Run(12);

            Assert.True(Jfp2Of(hub).IsNegotiated(a.Id));
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            hub.Received.Clear();
            SendPosition(a, hub, 4, "RS1", 3);
            mesh.Run(0.5);
            Assert.Equal(3, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
        }

        [Fact]
        public void SessionThatStopsAnswering_FallsBackToLegacyAndRecovers()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(4);
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);

            // the hub's JFP2 stops reaching a (a router that forgot the mapping, a peer that hung); legacy still flows
            mesh.Network.Filter = (from, to, data) => !(from.Equals(hub.EndPoint) && data[0] == Envelope.Magic);
            mesh.Run(20);
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            SendPosition(a, hub, 2, "FB1", 1);
            mesh.Run(0.5);
            Assert.Equal(1, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);

            mesh.Network.Filter = null;
            mesh.Run(45);
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
        }
    }
}
