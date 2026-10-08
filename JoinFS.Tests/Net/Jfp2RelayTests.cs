using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
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

            public SharedEndpoint(string hubBuild = null, string bBuild = null)
            {
                Hub = Mesh.AddBehindNat("192.168.1.10", "203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(build: hubBuild));
                B = Mesh.AddBehindNat("192.168.1.20", "203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(build: bBuild));
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

        /// <summary>
        /// The router starts steering the shared endpoint to B, so A's keepalive Hello to the hub is
        /// answered by B: the build in that HelloAck is B's, and must not be taken for the hub's.
        /// </summary>
        [Fact]
        public void SharedEndpoint_HelloAckFromAnotherNode_DoesNotGiveItsBuildToThePeerAskedFor()
        {
            var t = new SharedEndpoint(hubBuild: "26.6.0 JoinFS-CONSOLE", bBuild: "26.6.0 JoinFS-FS2024");
            // Hellos to A would tell A the builds themselves: keep them away, so only HelloAcks speak
            t.Mesh.Network.Filter = (from, to, data) => !(to.Equals(t.A.EndPoint) && IsHello(data));
            t.Mesh.Run(20);
            Assert.Equal("26.6.0 JoinFS-CONSOLE", Jfp2Of(t.A).BuildOf(t.Hub.Id));

            t.Steer = t.B.EndPoint;
            t.Mesh.Run(10);

            Assert.Contains(t.A.Logs, l => l.Contains(t.Hub.Id + " is not the node answering"));
            Assert.Equal("26.6.0 JoinFS-CONSOLE", Jfp2Of(t.A).BuildOf(t.Hub.Id));
        }

        static bool IsHello(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Internal) != 0 && data[7] == MessageClasses.Hello;

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

        /// <summary>
        /// A relayed ack belongs to the origin it is addressed to. B's ack of A's message, relayed by the
        /// hub, must not clear the hub's own message to B that happens to carry the same id (each node
        /// counts its own ids), or the hub never retransmits it.
        /// </summary>
        [Fact]
        public void RelayedAckForAnotherOrigin_DoesNotClearTheHubsOwnMessage()
        {
            var (mesh, hub, a, b) = HubWithTwoBehindIt(aJfp2: true, bJfp2: true);
            int hubToB = 0;
            var partition = mesh.Network.Filter;
            mesh.Network.Filter = (from, to, data) => partition(from, to, data)
                && !(from.Equals(hub.EndPoint) && to.Equals(b.EndPoint) && IsGuaranteedApplication(data) && hubToB++ == 0);
            mesh.Network.Log.Clear();

            hub.Core.SendTo(b.Id, new EventUpdate { ObjectId = 1, EventId = 11 }, true); // lost on the way
            a.Core.SendTo(b.Id, new EventUpdate { ObjectId = 2, EventId = 22 }, true);   // relayed by the hub
            mesh.Run(0.5);
            ushort GuaranteedIdFrom(IPEndPoint from) =>
                Envelope.ReadFrom(mesh.Network.Log.First(d => d.From.Equals(from) && IsGuaranteedApplication(d.Data)).Data, out _).GuaranteedId;
            Assert.Equal(GuaranteedIdFrom(hub.EndPoint), GuaranteedIdFrom(a.EndPoint)); // the collision this test is about

            mesh.Run(10);
            Assert.Contains(b.Messages<EventUpdate>(), e => e.EventId == 11);
            Assert.Contains(b.Messages<EventUpdate>(), e => e.EventId == 22);
            Assert.Equal(0, Jfp2Of(hub).GuaranteedPendingCount);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
        }

        static bool IsGuaranteedApplication(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Guaranteed) != 0 && (data[2] & (byte)EnvelopeFlags.Internal) == 0;

        /// <summary>
        /// A JFP2 node A and two legacy-only targets that it reaches only through a JFP2 hub: one message
        /// of A to both may share an id (spec §4.4). The hub translates each for its own target,
        /// deduplicating per (origin, target, id), so both arrive (keyed by (origin, id), the second was
        /// taken for a duplicate); downstream, legacy delivers each with its own ids. Upstream, the hub
        /// acknowledges each in its target's name, and A's own messages are cleared by those acks.
        /// </summary>
        [Fact]
        public void TranslatingRelay_SameIdToTwoTargets_DeliversBoth()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode b1 = mesh.Add("192.0.2.3", 6112, new LegacyPlugin());
            TestNode b2 = mesh.Add("192.0.2.4", 6112, new LegacyPlugin());
            mesh.Partition(a, b1);
            mesh.Partition(a, b2);
            hub.Core.Mesh.Create(false, 0, false, "");
            foreach (TestNode node in new[] { a, b1, b2 }) node.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);
            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b1.Id));
            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b2.Id));

            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort local, out ushort remote));
            byte[] payload = new byte[EventV1Codec.Size];
            new EventV1Codec().Encode(new EventUpdate { ObjectId = 1, EventId = 4321 }, payload);
            mesh.Network.Log.Clear();
            foreach (TestNode target in new[] { b1, b2 })
            {
                var envelope = new Envelope(EnvelopeFlags.Guaranteed | EnvelopeFlags.Forwarded, local, remote, MessageClasses.Event, 50, 0, 1,
                    NodeName.FromLegacy(a.Id), NodeName.FromLegacy(target.Id));
                byte[] datagram = new byte[envelope.WireSize + payload.Length];
                payload.CopyTo(datagram, envelope.WriteTo(datagram));
                a.Core.Transport.Send(hub.EndPoint, datagram);
            }
            mesh.Run(3);

            foreach (TestNode target in new[] { b1, b2 })
            {
                var (meta, evt) = Assert.Single(target.MessagesWithMeta<EventUpdate>());
                Assert.Equal(a.Id, meta.Sender);
                Assert.Equal(4321u, evt.EventId);
            }
            List<Envelope> acks = mesh.Network.Log.Where(d => d.From.Equals(hub.EndPoint) && d.To.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic)
                .Select(d => Envelope.ReadFrom(d.Data, out _)).Where(e => e.IsInternal && e.RawMessageClass == MessageClasses.GuaranteedDone).ToList();
            Assert.Equal([NodeName.FromLegacy(b1.Id), NodeName.FromLegacy(b2.Id)], acks.Select(e => e.Origin).ToList());
            Assert.All(acks, e => Assert.True(e.IsForwarded && e.Target == NodeName.FromLegacy(a.Id)));
            Assert.Equal(0, hub.Core.Plugins.OfType<LegacyPlugin>().Single().GuaranteedOutCount); // legacy's own acks consumed

            // A's own messages to both: each cleared by the hub's ack in its target's name
            a.Core.SendTo(b1.Id, new EventUpdate { ObjectId = 2, EventId = 1 }, true);
            a.Core.SendTo(b2.Id, new EventUpdate { ObjectId = 2, EventId = 2 }, true);
            mesh.Run(3);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
            Assert.Contains(b1.Messages<EventUpdate>(), e => e.EventId == 1);
            Assert.Contains(b2.Messages<EventUpdate>(), e => e.EventId == 2);
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

        /// <summary>
        /// A node behind a NAT that maps its port 6112 to public port 40001: the hub answers its Hello
        /// with the endpoint the Hello arrived from, the mapped one, and the node's app classifies that as
        /// a translated port (docs/jfp2-wire-design.md §7.3). The public address the node uses is still
        /// the one its HTTP lookup gave.
        /// </summary>
        [Fact]
        public void BehindNat_TheObservedEndPointIsTheMappedOne()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode c = mesh.AddBehindNat("10.0.0.5", "198.51.100.9", 6112, new LegacyPlugin(), new Jfp2Plugin());
            var lan = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 6112);
            var mapped = new IPEndPoint(IPAddress.Parse("198.51.100.9"), 40001);
            mesh.Network.Nat = (from, to) => (from.Equals(lan) ? mapped : from, to.Equals(mapped) ? lan : to);
            hub.Core.Mesh.Create(false, 0, false, "");
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);
            Assert.Equal("JFP2", c.Core.Route(hub.Id, MessageKind.Position)!.Name);

            NetworkEvent observation = Assert.Single(c.Events, e => e.Kind == NetworkEventKind.EndPointObserved);
            Assert.Equal(hub.Id, observation.Node);
            Assert.Equal(mapped, observation.EndPoint);

            var observed = new ObservedEndPoints();
            Assert.True(observed.Observe(observation.Node, observation.EndPoint, c.Core.Identity.LocalAddress, c.Core.Identity.Port));
            Assert.Equal(NatClass.Translated, observed.Class);
            Assert.Equal(IPAddress.Parse("198.51.100.9"), c.Core.Identity.InternetAddress); // unchanged: log only
        }
    }
}
