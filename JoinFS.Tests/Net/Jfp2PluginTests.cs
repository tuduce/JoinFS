using System.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The JFP2 plugin next to the legacy plugin in the new stack: negotiation, per-(peer, kind)
    /// routing, fallback to legacy, and translation of relayed JFP2 for legacy-only targets.
    /// </summary>
    public class Jfp2PluginTests
    {
        static TestNode Jfp2Node(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin());
        static TestNode LegacyNode(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new LegacyPlugin());

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

        static int Jfp2DatagramsBetween(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Count(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint) && d.Data[0] == Envelope.Magic);

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        [Fact]
        public void TwoJfp2Nodes_NegotiateAndSendPositionsOverJfp2()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.RemoveObject)!.Name);

            mesh.Network.Log.Clear();
            SendPosition(a, hub, 3, "JF1", 12.5);
            mesh.Run(0.1);

            Assert.Equal(12.5, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal("JF1", Assert.Single(hub.Messages<IdentityUpdate>()).Callsign);
            Assert.Equal(2, Jfp2DatagramsBetween(mesh, a, hub)); // Identity, then Position

            // identity is only resent on change or heartbeat
            mesh.Network.Log.Clear();
            SendPosition(a, hub, 3, "JF1", 12.6);
            mesh.Run(0.1);
            Assert.Equal(1, Jfp2DatagramsBetween(mesh, a, hub));
        }

        [Fact]
        public void Jfp2Node_FallsBackToLegacyWithLegacyOnlyPeer()
        {
            var mesh = new TestMesh();
            TestNode hub = LegacyNode(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            Assert.False(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal(PeerLinkState.Legacy, Jfp2Of(a).DescribeLink(a.Core.Peers.All.Single()));

            SendPosition(a, hub, 3, "LG1", 7);
            mesh.Run(0.1);
            Assert.Equal(7, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal("LG1", Assert.Single(hub.Messages<IdentityUpdate>()).Callsign);
        }

        [Fact]
        public void Broadcast_ReachesEachPeerInItsOwnProtocol()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode modern = Jfp2Node(mesh, "198.51.100.2");
            TestNode old = LegacyNode(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            modern.Core.Mesh.Join(hub.EndPoint, 0);
            old.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            mesh.Network.Log.Clear();
            hub.Core.Broadcast(new EventUpdate { ObjectId = 1, EventId = 99, Data = 5 }, true);
            mesh.Run(0.5);

            Assert.Equal(99u, Assert.Single(modern.Messages<EventUpdate>()).EventId);
            Assert.Equal(99u, Assert.Single(old.Messages<EventUpdate>()).EventId);
            Assert.True(Jfp2DatagramsBetween(mesh, hub, modern) >= 1);
            Assert.Equal(0, Jfp2DatagramsBetween(mesh, hub, old));
        }

        [Fact]
        public void Jfp2Guaranteed_SurvivesLoss()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            int dropped = 0;
            mesh.Network.Filter = (from, to, data) =>
                !(from.Equals(a.EndPoint) && data[0] == Envelope.Magic && (data[1] & (byte)EnvelopeFlags.Guaranteed) != 0
                  && (data[1] & (byte)EnvelopeFlags.Internal) == 0 && dropped++ < 2);
            a.Core.SendTo(hub.Id, new NotesBundle
            {
                Scope = CommsScope.Single,
                Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "n", Callsign = "c", Notes = [new CommsNote { NoteId = 5, Channel = 1, Text = "hi" }] }],
            }, true);
            mesh.Run(8);

            Assert.Equal("hi", Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
        }

        /// <summary>
        /// A JFP2 sender relaying through a JFP2 hub to a legacy-only target (what builds from this
        /// branch before the rewrite did): the hub translates. Position must be credited to the true
        /// sender (the old Tier 3 credited the hub), and guaranteed classes must arrive (the old Tier 3
        /// dropped them).
        /// </summary>
        [Fact]
        public void RelayedJfp2_IsTranslatedForLegacyTarget()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            TestNode b = LegacyNode(mesh, "192.0.2.3");
            mesh.Partition(a, b);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(15);
            Assert.True(Jfp2Of(hub).IsNegotiated(a.Id));
            Assert.False(Jfp2Of(hub).IsNegotiated(b.Id));

            // hand-built Forwarded envelopes, as an older JFP2 build originates them
            void Relayed(byte messageClass, ReadOnlySpan<byte> payload, bool guaranteed, ushort id)
            {
                var flags = EnvelopeFlags.Forwarded | (guaranteed ? EnvelopeFlags.Guaranteed : 0);
                var envelope = new Envelope(flags, 0, 0, messageClass, id, 0, (byte)(guaranteed ? 1 : 0),
                    new RelayNuid(a.Id.ip, a.Id.port, a.Id.local), new RelayNuid(b.Id.ip, b.Id.port, b.Id.local));
                byte[] data = new byte[envelope.WireSize + payload.Length];
                int header = envelope.WriteTo(data);
                payload.CopyTo(data.AsSpan(header));
                a.Core.Transport.Send(hub.EndPoint, data);
            }
            byte[] buffer = new byte[512];
            int n = new IdentityV1Codec().Encode(Identity(8, "RLY1"), buffer);
            Relayed(MessageClasses.Identity, buffer.AsSpan(0, n), false, 0);
            n = new PositionV1Codec().Encode(new PositionUpdate { ObjectId = 8, NetTime = 2, Latitude = 33, StateFlags = PositionStateFlags.UserControlled }, buffer);
            Relayed(MessageClasses.Position, buffer.AsSpan(0, n), false, 0);
            n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, buffer);
            Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            mesh.Run(1);

            var (meta, position) = Assert.Single(b.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(33, position.Latitude);
            Assert.Equal("RLY1", Assert.Single(b.Messages<IdentityUpdate>()).Callsign);
            var (eventMeta, evt) = Assert.Single(b.MessagesWithMeta<EventUpdate>());
            Assert.Equal(a.Id, eventMeta.Sender);
            Assert.Equal(1234u, evt.EventId);

            // the hub's legacy delivery was acknowledged (and the ack consumed, not relayed back to a)
            mesh.Run(3);
            var legacy = hub.Core.Plugins.OfType<LegacyPlugin>().Single();
            Assert.Equal(0, legacy.GuaranteedOutCount);
            Assert.Single(b.Messages<EventUpdate>());
        }
    }
}
