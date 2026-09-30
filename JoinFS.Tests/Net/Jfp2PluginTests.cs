using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
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

        /// <summary>A hub and a node A, both JFP2, with their session negotiated and verified.</summary>
        static (TestMesh Mesh, TestNode Hub, TestNode A) TwoNegotiated()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            return (mesh, hub, a);
        }

        /// <summary>A hand-built, unforwarded JFP2 datagram from <paramref name="from"/> to its neighbor <paramref name="to"/>, in their session.</summary>
        static void SendRaw(TestNode from, TestNode to, EnvelopeFlags flags, byte messageClass, ReadOnlySpan<byte> payload, ushort guaranteedId = 0)
        {
            Assert.True(Jfp2Of(from).TryGetHopIds(to.Id, out ushort local, out ushort remote));
            bool guaranteed = (flags & EnvelopeFlags.Guaranteed) != 0;
            var envelope = new Envelope(flags, local, remote, messageClass, guaranteedId, 0, (byte)(guaranteed ? 1 : 0));
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram.AsSpan(header));
            from.Core.Transport.Send(to.EndPoint, datagram);
        }

        [Fact]
        public void Jfp2Guaranteed_SurvivesLoss()
        {
            var (mesh, hub, a) = TwoNegotiated();

            int dropped = 0;
            mesh.Network.Filter = (from, to, data) =>
                !(from.Equals(a.EndPoint) && IsGuaranteedApplication(data) && dropped++ < 2);
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(8);

            Assert.Equal(3, dropped); // two attempts lost, the third let through
            Assert.Equal("hi", Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
        }

        /// <summary>
        /// Pins a known limit (docs/reference/jfp2-protocol.md §4.4): a guaranteed message longer than
        /// one segment still goes out as a single datagram. When segmentation is implemented this
        /// should expect ceil(size / GuaranteedSegmentSize) segments instead, as
        /// LegacyPluginGoldenTests.GuaranteedSegmentation does for legacy.
        /// </summary>
        [Fact]
        public void Jfp2Guaranteed_LongerThanOneSegment_IsSentUnsegmented()
        {
            var (mesh, hub, a) = TwoNegotiated();

            string text = new('x', 2500);
            mesh.Network.Log.Clear();
            a.Core.SendTo(hub.Id, Note(text), true);
            mesh.Run(1);

            Assert.Equal(text, Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            byte[] datagram = Assert.Single(mesh.Network.Log, d => d.From.Equals(a.EndPoint) && IsGuaranteedApplication(d.Data)).Data;
            Envelope envelope = Envelope.ReadFrom(datagram, out int header);
            Assert.Equal(0, envelope.GuaranteedIndex);
            Assert.Equal(1, envelope.GuaranteedCount);
            Assert.True(datagram.Length - header > Jfp2Reliability.GuaranteedSegmentSize);
        }

        /// <summary>Earlier JFP2 builds ack with the id alone (2 bytes); that still acknowledges segment 0.</summary>
        [Fact]
        public void Jfp2Guaranteed_AckWithoutSegmentIndex_StopsRetransmission()
        {
            var (mesh, hub, a) = TwoNegotiated();

            // drop the hub's own (3-byte) acks
            mesh.Network.Filter = (from, to, data) => !(from.Equals(hub.EndPoint) && IsGuaranteedDone(data));
            mesh.Network.Log.Clear();
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(0.1);
            byte[] sent = Assert.Single(mesh.Network.Log, d => d.From.Equals(a.EndPoint) && IsGuaranteedApplication(d.Data)).Data;
            ushort id = Envelope.ReadFrom(sent, out _).GuaranteedId;
            // from here a retry would be acked normally, so any retransmission shows the short ack was ignored
            mesh.Network.Filter = null;

            byte[] shortAck = new byte[2];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(shortAck, id);
            SendRaw(hub, a, EnvelopeFlags.Internal, MessageClasses.GuaranteedDone, shortAck);
            mesh.Run(8);

            Assert.Equal(1, mesh.Network.Log.Count(d => d.From.Equals(a.EndPoint) && IsGuaranteedApplication(d.Data)));
        }

        /// <summary>
        /// Longer than the session timeout: the session is demoted to legacy meanwhile, and the message
        /// waits for JFP2 to reach the hub again instead of being given up (or handed to legacy).
        /// </summary>
        [Fact]
        public void Jfp2Guaranteed_SurvivesOutageLongerThanTheSessionTimeout()
        {
            var (mesh, hub, a) = TwoNegotiated();

            bool down = true;
            mesh.Network.Filter = (from, to, data) => !(down && from.Equals(a.EndPoint));
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(25);
            Assert.Contains(a.Logs, l => l.Contains("no longer verified"));
            Assert.Empty(hub.Messages<NotesBundle>());

            // unanswered Hellos meanwhile put the hub on the 30 s legacy-only cooldown: JFP2 comes back after it
            down = false;
            mesh.Run(45);

            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("hi", Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
        }

        /// <summary>
        /// A peer that restarts with a build offering fewer classes: routes cached for the old agreement
        /// are dropped, or messages of a class it no longer takes would be routed to JFP2 and go nowhere.
        /// </summary>
        [Fact]
        public void Handshake_WithFewerOffers_RefreshesCachedRoutes()
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);

            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort aId, out _));
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor, ProtoMajorMax = Envelope.ProtoMajor, SelfAssignedId = aId,
                Node = new RelayNuid(a.Id.ip, a.Id.port, a.Id.local),
                Offers = [new SchemaOffer(false, MessageClasses.Position, 1, 1)],
            };
            SendRaw(a, hub, EnvelopeFlags.Internal, MessageClasses.Hello, hello.Serialize());
            mesh.Run(0.1);

            Assert.Equal("Legacy", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Position)!.Name);
        }

        /// <summary>A peer must not take a guaranteed message we cannot read (a class never agreed) as delivered.</summary>
        [Fact]
        public void Jfp2Guaranteed_ClassNeverAgreed_IsNotAcknowledged()
        {
            var (mesh, hub, a) = TwoNegotiated();
            const byte unknownClass = 200; // offered by no build

            mesh.Network.Log.Clear();
            SendRaw(a, hub, EnvelopeFlags.Guaranteed, unknownClass, [1, 2, 3, 4], guaranteedId: 77);
            mesh.Run(1);

            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
        }

        /// <summary>Variables go out in datagrams that fit a safe UDP MTU, like legacy's, however many an object has.</summary>
        [Fact]
        public void VariableSync_ManyVariables_SplitIntoDatagramsUnderTheMtu()
        {
            var (mesh, hub, a) = TwoNegotiated();
            var entries = new List<VariableEntry>();
            for (uint vuid = 1; vuid <= 300; vuid++) entries.Add(new VariableEntry { Vuid = vuid, Kind = VariableKind.Int32, IntValue = (int)vuid });

            mesh.Network.Log.Clear();
            a.Core.SendTo(hub.Id, new VariableSyncUpdate { ObjectId = 3, Entries = entries }, false);
            mesh.Run(0.1);

            var datagrams = mesh.Network.Log.Where(d => d.From.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic
                && (d.Data[2] & (byte)EnvelopeFlags.Internal) == 0 && d.Data[7] == MessageClasses.VariableSync).ToList();
            Assert.True(datagrams.Count > 1);
            Assert.All(datagrams, d => Assert.True(d.Data.Length <= 1024, d.Data.Length + " bytes"));
            Assert.Equal(300, hub.Messages<VariableSyncUpdate>().Sum(m => m.Entries.Count));
        }

        /// <summary>
        /// Objects come and go; what was sent about one that is no longer sent is forgotten, and if it
        /// comes back its identity goes out first again.
        /// </summary>
        [Fact]
        public void Identity_OfAnObjectNoLongerSent_IsForgottenAndResentFirst()
        {
            var (mesh, hub, a) = TwoNegotiated();
            SendPosition(a, hub, 3, "ID1", 1);
            mesh.Run(0.1);
            Assert.Equal(1, Jfp2Of(a).IdentitySentCount);

            mesh.Run(61);
            Assert.Equal(0, Jfp2Of(a).IdentitySentCount);

            hub.Received.Clear();
            SendPosition(a, hub, 3, "ID1", 2);
            mesh.Run(0.1);
            Assert.Equal("ID1", Assert.Single(hub.Messages<IdentityUpdate>()).Callsign);
            Assert.Equal(2, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
        }

        static bool IsGuaranteedApplication(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Guaranteed) != 0 && (data[2] & (byte)EnvelopeFlags.Internal) == 0;

        static bool IsGuaranteedDone(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Internal) != 0 && data[7] == MessageClasses.GuaranteedDone;

        static NotesBundle Note(string text) => new()
        {
            Scope = CommsScope.Single,
            Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "n", Callsign = "c", Notes = [new CommsNote { NoteId = 5, Channel = 1, Text = text }] }],
        };

        /// <summary>
        /// A JFP2 sender relaying through a JFP2 hub to a legacy-only target (what builds from this
        /// branch before the rewrite did): the hub translates. Position must be credited to the true
        /// sender (the old Tier 3 credited the hub), and guaranteed classes must arrive (the old Tier 3
        /// dropped them).
        /// </summary>
        [Fact]
        public void RelayedJfp2_IsTranslatedForLegacyTarget()
        {
            var t = new HubBetweenJfp2AndLegacy();
            byte[] buffer = new byte[512];
            int n = new IdentityV1Codec().Encode(Identity(8, "RLY1"), buffer);
            t.Relayed(MessageClasses.Identity, buffer.AsSpan(0, n), false, 0);
            n = new PositionV1Codec().Encode(new PositionUpdate { ObjectId = 8, NetTime = 2, Latitude = 33, StateFlags = PositionStateFlags.UserControlled }, buffer);
            t.Relayed(MessageClasses.Position, buffer.AsSpan(0, n), false, 0);
            n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, buffer);
            t.Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            t.Mesh.Run(1);

            var (meta, position) = Assert.Single(t.B.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(t.A.Id, meta.Sender);
            Assert.Equal(33, position.Latitude);
            Assert.Equal("RLY1", Assert.Single(t.B.Messages<IdentityUpdate>()).Callsign);
            var (eventMeta, evt) = Assert.Single(t.B.MessagesWithMeta<EventUpdate>());
            Assert.Equal(t.A.Id, eventMeta.Sender);
            Assert.Equal(1234u, evt.EventId);

            // the hub's legacy delivery was acknowledged (and the ack consumed, not relayed back to a)
            t.Mesh.Run(3);
            var legacy = t.Hub.Core.Plugins.OfType<LegacyPlugin>().Single();
            Assert.Equal(0, legacy.GuaranteedOutCount);
            Assert.Single(t.B.Messages<EventUpdate>());
        }

        /// <summary>
        /// The hub acks upstream as soon as it has translated. If that ack is lost the sender
        /// retransmits: the hub must ack again, but not deliver the message downstream a second time.
        /// </summary>
        [Fact]
        public void RelayedJfp2_RetransmittedGuaranteed_IsTranslatedOnce()
        {
            var t = new HubBetweenJfp2AndLegacy();
            byte[] buffer = new byte[64];
            int n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, buffer);
            t.Mesh.Network.Log.Clear();
            t.Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            t.Mesh.Run(1);
            t.Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            t.Mesh.Run(3);

            Assert.Single(t.B.Messages<EventUpdate>());
            Assert.Equal(2, t.Mesh.Network.Log.Count(d => d.From.Equals(t.Hub.EndPoint) && d.To.Equals(t.A.EndPoint) && IsGuaranteedDone(d.Data)));
        }

        /// <summary>
        /// A JFP2 node A and a legacy-only node B that reach each other only through a JFP2 hub, and
        /// A's hand-built Forwarded envelopes for B.
        /// </summary>
        sealed class HubBetweenJfp2AndLegacy
        {
            public readonly TestMesh Mesh = new();
            public readonly TestNode Hub, A, B;
            readonly ushort hopLocal, hopRemote;

            public HubBetweenJfp2AndLegacy()
            {
                Hub = Jfp2Node(Mesh, "203.0.113.1");
                A = Jfp2Node(Mesh, "198.51.100.2");
                B = LegacyNode(Mesh, "192.0.2.3");
                Mesh.Partition(A, B);
                Hub.Core.Mesh.Create(false, 0, false, "");
                A.Core.Mesh.Join(Hub.EndPoint, 0);
                B.Core.Mesh.Join(Hub.EndPoint, 0);
                Mesh.Run(15);
                Assert.True(Jfp2Of(Hub).IsNegotiated(A.Id));
                Assert.False(Jfp2Of(Hub).IsNegotiated(B.Id));
                Assert.True(Jfp2Of(A).TryGetHopIds(Hub.Id, out hopLocal, out hopRemote));
            }

            /// <summary>Send from A to the hub, addressed to B: hop ids of A's session with the hub, end-to-end Origin/Target.</summary>
            public void Relayed(byte messageClass, ReadOnlySpan<byte> payload, bool guaranteed, ushort id)
            {
                var flags = EnvelopeFlags.Forwarded | (guaranteed ? EnvelopeFlags.Guaranteed : 0);
                var envelope = new Envelope(flags, hopLocal, hopRemote, messageClass, id, 0, (byte)(guaranteed ? 1 : 0),
                    new RelayNuid(A.Id.ip, A.Id.port, A.Id.local), new RelayNuid(B.Id.ip, B.Id.port, B.Id.local));
                byte[] data = new byte[envelope.WireSize + payload.Length];
                int header = envelope.WriteTo(data);
                payload.CopyTo(data.AsSpan(header));
                A.Core.Transport.Send(Hub.EndPoint, data);
            }
        }
    }
}
