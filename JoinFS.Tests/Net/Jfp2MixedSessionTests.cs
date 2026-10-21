using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// A session whose members do not all speak the same protocol (docs/network/jfp2/protocol.md §5.13, §8.4; plan
    /// stage 6, scenario S4 with W present and the mechanism of S7 on IPv4). H is a hub of this build, A and B
    /// joined it over JFP2, W is a build released before this work (legacy for membership; with or without JFP2
    /// for the application classes). A and W are cut off from each other, so H is their only path.
    /// </summary>
    public class Jfp2MixedSessionTests
    {
        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        /// <summary>A node of this build: legacy for the nodes that speak nothing else, JFP2 with the membership classes.</summary>
        static TestNode N(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin());

        /// <summary>A build released before this work: R1 has JFP2 for the application classes, L has legacy only.</summary>
        static TestNode W(TestMesh mesh, string ip, bool r1) =>
            r1 ? mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin(profile: new Jfp2Profile([.. Jfp2Profile.Default.Classes])))
               : mesh.Add(ip, 6112, new LegacyPlugin());

        static TestNode Hub(TestMesh mesh)
        {
            TestNode hub = N(mesh, "203.0.113.1");
            hub.Core.Mesh.Create(false, 0, false, "");
            return hub;
        }

        static void JoinOverJfp2(TestMesh mesh, TestNode joiner, TestNode hub, double seconds = 3)
        {
            Assert.True(joiner.Core.Mesh.JoinOverJfp2(hub.EndPoint, 0));
            mesh.Run(seconds);
        }

        static void JoinOverLegacy(TestMesh mesh, TestNode joiner, TestNode hub, double seconds = 3)
        {
            joiner.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(seconds);
        }

        static IdentityUpdate Identity(uint id) => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = "N" + id, Model = "M", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static void SendPosition(TestNode from, TestNode to, uint id, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id));
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        static bool Left(TestNode node, TestNode who) => node.Events.Any(e => e.Kind == NetworkEventKind.PeerLeft && e.Node == who.Id);

        static IEnumerable<(IPEndPoint From, IPEndPoint To, byte[] Data)> Datagrams(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Where(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint));

        static bool IsLegacy(byte[] data) => data[0] != Envelope.Magic;

        /// <summary>
        /// A joined over JFP2: no legacy datagram passes between it and the hub, whatever W and the hub say to each other. (What W
        /// sends A directly is lost in the cut, which the network's log still shows.)
        /// </summary>
        static void AssertNoLegacyAtA(Session s) =>
            Assert.DoesNotContain(s.Mesh.Network.Log, d => IsLegacy(d.Data)
                && ((d.From.Equals(s.A.EndPoint) && d.To.Equals(s.H.EndPoint)) || (d.From.Equals(s.H.EndPoint) && d.To.Equals(s.A.EndPoint))));

        /// <summary>H, W joined over legacy, A joined over JFP2; A and W cannot reach each other.</summary>
        sealed class Session
        {
            public readonly TestMesh Mesh = new();
            public readonly TestNode H, A, W;

            public Session(bool r1, bool wFirst = true, int budget = MeshManager.DefaultRelayBudget)
            {
                H = Hub(Mesh);
                H.Core.Mesh.RelayBudget = budget;
                A = N(Mesh, "198.51.100.2");
                W = W(Mesh, "198.51.100.9", r1);
                Mesh.Partition(A, W);
                if (wFirst)
                {
                    JoinOverLegacy(Mesh, W, H);
                    JoinOverJfp2(Mesh, A, H);
                }
                else
                {
                    JoinOverJfp2(Mesh, A, H);
                    JoinOverLegacy(Mesh, W, H);
                }
            }
        }

        // ------------------------------------------------------------------ S4 with W present

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void AAndW_ThroughH_SeeEachOther(bool r1, bool wFirst)
        {
            var s = new Session(r1, wFirst);

            s.Mesh.Run(20);

            Assert.True(s.A.Core.Peers.TryGet(s.W.Id, out Peer? wAtA));
            Assert.True(s.W.Core.Peers.TryGet(s.A.Id, out Peer? aAtW));
            Assert.True(wAtA!.SendEstablished && wAtA.Relayed && wAtA.RouteVia == s.H.Id);
            Assert.True(aAtW!.SendEstablished && aAtW.Relayed && aAtW.RouteVia == s.H.Id);
            Assert.Contains(s.A.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == s.W.Id);
            Assert.Contains(s.W.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == s.A.Id);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AAndW_ThroughH_ExchangePositions(bool r1)
        {
            var s = new Session(r1);
            s.Mesh.Run(10);

            SendPosition(s.A, s.W, 1, 11);
            SendPosition(s.W, s.A, 2, 22);
            s.Mesh.Run(1);

            Assert.Equal(11, s.W.Messages<PositionUpdate>().Single(p => p.ObjectId == 1).Latitude);
            Assert.Equal(22, s.A.Messages<PositionUpdate>().Single(p => p.ObjectId == 2).Latitude);
            Assert.Equal("N1", s.W.Messages<IdentityUpdate>().Single(i => i.ObjectId == 1).Callsign);
            Assert.Equal("N2", s.A.Messages<IdentityUpdate>().Single(i => i.ObjectId == 2).Callsign);
            Assert.Empty(s.H.Messages<PositionUpdate>()); // relayed, not consumed
            AssertNoLegacyAtA(s);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AAndW_StayMembers_ForAMinute(bool r1)
        {
            var s = new Session(r1);

            s.Mesh.Run(90);

            Assert.True(s.A.Core.Peers.Contains(s.W.Id));
            Assert.True(s.W.Core.Peers.Contains(s.A.Id));
            Assert.True(s.H.Core.Peers.Contains(s.A.Id) && s.H.Core.Peers.Contains(s.W.Id));
            Assert.False(Left(s.A, s.W));
            Assert.False(Left(s.W, s.A));
        }

        [Fact]
        public void NoLegacyDatagramPasses_BetweenAAndH()
        {
            var s = new Session(r1: true);

            s.Mesh.Run(60);

            Assert.DoesNotContain(Datagrams(s.Mesh, s.A, s.H), d => IsLegacy(d.Data));
            Assert.DoesNotContain(Datagrams(s.Mesh, s.H, s.A), d => IsLegacy(d.Data));
        }

        // ------------------------------------------------------------------ leaving

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void WLeaves_AIsToldAtOnce(bool r1)
        {
            var s = new Session(r1);
            s.Mesh.Run(5);

            s.W.Core.Mesh.Leave();
            s.Mesh.Run(1);

            Assert.False(s.A.Core.Peers.Contains(s.W.Id));
            Assert.True(Left(s.A, s.W));
            Assert.True(s.A.Core.Peers.Contains(s.H.Id));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ALeaves_WIsToldAtOnce(bool r1)
        {
            var s = new Session(r1);
            s.Mesh.Run(5);

            s.A.Core.Mesh.Leave();
            s.Mesh.Run(1);

            Assert.False(s.W.Core.Peers.Contains(s.A.Id));
            Assert.True(Left(s.W, s.A));
            Assert.True(s.W.Core.Peers.Contains(s.H.Id));
        }

        [Fact]
        public void ACrash_IsKnownToWAndToTheHub_WithinTheExpiry()
        {
            var s = new Session(r1: false);
            s.Mesh.Run(5);

            s.Mesh.Partition(s.A, s.H);
            s.Mesh.Run(45);

            Assert.False(s.H.Core.Peers.Contains(s.A.Id));
            Assert.False(s.W.Core.Peers.Contains(s.A.Id));
        }

        // ------------------------------------------------------------------ guaranteed delivery through the bridge

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AGuaranteedEvent_FromWToA_IsAcknowledgedEndToEnd(bool r1)
        {
            var s = new Session(r1);
            s.Mesh.Run(10);

            s.W.Core.SendTo(s.A.Id, new EventUpdate { ObjectId = 1, EventId = 77, Data = 3 }, true);
            s.Mesh.Run(10);

            var (meta, evt) = Assert.Single(s.A.MessagesWithMeta<EventUpdate>());
            Assert.Equal(s.W.Id, meta.Sender);
            Assert.Equal(77u, evt.EventId);
            Assert.Equal(0, Jfp2Of(s.H).GuaranteedPendingCount);
            AssertNoLegacyAtA(s);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AGuaranteedEvent_FromAToW_IsDeliveredOnce(bool r1)
        {
            var s = new Session(r1);
            s.Mesh.Run(10);

            s.A.Core.SendTo(s.W.Id, new EventUpdate { ObjectId = 1, EventId = 78, Data = 3 }, true);
            s.Mesh.Run(10);

            var (meta, evt) = Assert.Single(s.W.MessagesWithMeta<EventUpdate>());
            Assert.Equal(s.A.Id, meta.Sender);
            Assert.Equal(78u, evt.EventId);
            Assert.Equal(0, Jfp2Of(s.A).GuaranteedPendingCount);
            AssertNoLegacyAtA(s);
        }

        // ------------------------------------------------------------------ the bridge's own work

        [Fact]
        public void TheHub_AnswersTheLegacyPulsesForAnAThatCannotAnswerThem()
        {
            var s = new Session(r1: false);
            s.Mesh.Run(5);
            s.Mesh.Network.Log.Clear();

            s.Mesh.Run(10);

            // W pulses A through H, and gets its answers from H, in A's name; nothing legacy reaches A at all
            Assert.Contains(s.W.Logs, l => l.Contains("PulseResponse " + s.A.Id));
            Assert.Contains(s.H.Logs, l => l.Contains("Pulse of " + s.W.Id + " answered for " + s.A.Id));
            AssertNoLegacyAtA(s);
        }

        [Fact]
        public void WIsToldOfAByAddNode_AndAIsToldOfWByMemberAnnounce()
        {
            var s = new Session(r1: false, wFirst: false);

            // W joined last: A heard of it from the hub's MemberAnnounce, W heard of A from the JoinReply
            Assert.Single(Datagrams(s.Mesh, s.H, s.A), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.MemberAnnounce);
            Assert.Contains(s.H.Logs, l => l.Contains(s.W.Id + " announced to"));

            // A joined last: W heard of it by the hub's AddNode
            var t = new Session(r1: false, wFirst: true);
            Assert.Contains(t.H.Logs, l => l.Contains(t.A.Id + " announced to the legacy members"));
            Assert.Contains(t.W.Logs, l => l.Contains("AddNode " + t.H.Id) && l.Contains(t.A.Id.ToString()));
        }

        [Fact]
        public void AMemberWithoutALegacyId_IsLeftOutOfWhatLegacyIsTold_AndNoDatagramIsMalformed()
        {
            var s = new Session(r1: false);
            s.Mesh.Run(30);

            // the hub never names A in a way W cannot read: W holds A once, as a peer reached through the hub
            Assert.Single(s.W.Core.Peers.All, p => p.Id == s.A.Id);
            Assert.DoesNotContain(s.H.Logs, l => l.Contains("not sent"));
        }

        [Fact]
        public void AHubWithoutRelayBudget_CarriesNothingBetweenAAndW()
        {
            var s = new Session(r1: false, budget: 0);
            s.Mesh.Run(10);

            SendPosition(s.W, s.A, 3, 33);
            SendPosition(s.A, s.W, 4, 44);
            s.Mesh.Run(1);

            Assert.DoesNotContain(s.A.Messages<PositionUpdate>(), p => p.ObjectId == 3);
            Assert.DoesNotContain(s.W.Messages<PositionUpdate>(), p => p.ObjectId == 4);
            Assert.Contains(s.H.Logs, l => l.Contains("relay capacity reached"));
        }

        // ------------------------------------------------------------------ RouteQuery and RouteAnswer

        /// <summary>
        /// H; A joined H; G joined A; B joined G. A has H and G as neighbours and B through G. H knows G through A and does not know B.
        /// </summary>
        sealed class Chain
        {
            public readonly TestMesh Mesh = new();
            public readonly TestNode H, A, G, B;

            public Chain()
            {
                H = Hub(Mesh);
                A = N(Mesh, "198.51.100.2");
                G = N(Mesh, "198.51.100.7");
                B = N(Mesh, "198.51.100.3");
                JoinOverJfp2(Mesh, A, H);
                JoinOverJfp2(Mesh, G, A);
                JoinOverJfp2(Mesh, B, G);
                Mesh.Run(2);
            }
        }

        [Fact]
        public void TheChain_IsAsTheTestsAssumeIt()
        {
            var c = new Chain();

            Assert.True(c.A.Core.Peers.TryGet(c.B.Id, out Peer? bAtA));
            Assert.Equal(c.G.Id, bAtA!.RouteVia);
            Assert.True(c.H.Core.Peers.TryGet(c.G.Id, out Peer? gAtH));
            Assert.Equal(c.A.Id, gAtH!.RouteVia);
            Assert.False(c.H.Core.Peers.Contains(c.B.Id));
        }

        [Fact]
        public void ARouteThroughANeighbourThatDoesNotReachTheMember_IsReplacedByOneThatDoes()
        {
            var c = new Chain();
            Assert.True(c.A.Core.Peers.TryGet(c.B.Id, out Peer? bAtA));
            // a MemberList of the hub put B behind the hub, which does not reach it
            bAtA!.RouteVia = c.H.Id;
            bAtA.RouteEndPoint = c.H.EndPoint;
            c.A.Core.LinkChanged(c.B.Id);

            c.Mesh.Run(25);

            Assert.Equal(c.G.Id, bAtA.RouteVia);
            Assert.Equal(c.G.EndPoint, bAtA.RouteEndPoint);
            Assert.True(bAtA.SendEstablished);
            SendPosition(c.A, c.B, 8, 88);
            c.Mesh.Run(1);
            Assert.Equal(88, c.B.Messages<PositionUpdate>().Single(p => p.ObjectId == 8).Latitude);
            Assert.Contains(c.A.Logs, l => l.Contains(c.B.Id + " is reached through " + c.G.Id));
            // the hub was asked and said nothing: it does not reach B
            Assert.DoesNotContain(Datagrams(c.Mesh, c.H, c.A), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.RouteAnswer);
            Assert.Contains(Datagrams(c.Mesh, c.G, c.A), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.RouteAnswer);
        }

        [Fact]
        public void ARouteThatWorks_IsNeverLeftForAnotherThatAlsoAnswers()
        {
            var c = new Chain();
            Assert.True(c.A.Core.Peers.TryGet(c.B.Id, out Peer? bAtA));

            c.Mesh.Run(120);

            Assert.Equal(c.G.Id, bAtA!.RouteVia);
            Assert.Equal(1, c.A.Events.Count(e => e.Kind == NetworkEventKind.PeerJoined && e.Node == c.B.Id));
            Assert.DoesNotContain(c.A.Logs, l => l.Contains("is reached through"));
        }

        [Fact]
        public void WhenTheRelayIsGone_TheMemberIsReachedThroughTheOtherNeighbour()
        {
            var c = new Chain();
            // A reached B through the hub (a MemberList of the hub put it there)
            Assert.True(c.A.Core.Peers.TryGet(c.B.Id, out Peer? bAtA));
            bAtA!.RouteVia = c.H.Id;
            bAtA.RouteEndPoint = c.H.EndPoint;
            bAtA.ExpireTime = c.Mesh.Clock.Now + 1000;
            c.A.Core.LinkChanged(c.B.Id);

            c.Mesh.Partition(c.A, c.H);
            c.Mesh.Run(60);

            Assert.False(c.A.Core.Peers.Contains(c.H.Id));
            Assert.True(c.A.Core.Peers.Contains(c.B.Id));
            Assert.Equal(c.G.Id, bAtA.RouteVia);
            Assert.False(Left(c.A, c.B));
        }

        [Fact]
        public void ANeighbourWithoutRelayBudget_DoesNotAnswer()
        {
            var c = new Chain();
            c.G.Core.Mesh.RelayBudget = 0;
            Assert.True(c.A.Core.Peers.TryGet(c.B.Id, out Peer? bAtA));
            bAtA!.RouteVia = c.H.Id;
            bAtA.RouteEndPoint = c.H.EndPoint;
            c.A.Core.LinkChanged(c.B.Id);
            c.Mesh.Network.Log.Clear();

            c.Mesh.Run(25);

            Assert.Equal(c.H.Id, bAtA.RouteVia);
            Assert.DoesNotContain(Datagrams(c.Mesh, c.G, c.A), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.RouteAnswer);
        }

        [Fact]
        public void ANodeAsksOnlyAboutMembersWithoutALegacyLink_LegacyKeepsThePathfinderForTheOthers()
        {
            var s = new Session(r1: false);

            s.Mesh.Run(30);

            // A holds W as a member without a legacy link, so it asks the hub about it; the hub holds W over legacy, so it asks nobody
            Assert.Contains(Datagrams(s.Mesh, s.A, s.H), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.RouteQuery);
            Assert.DoesNotContain(Datagrams(s.Mesh, s.H, s.A), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.RouteQuery);
            Assert.Contains(Datagrams(s.Mesh, s.H, s.A), d => d.Data[0] == Envelope.Magic && (d.Data[2] & 0x08) != 0 && d.Data[7] == MessageClasses.RouteAnswer);
        }

        // ------------------------------------------------------------------ the wire

        [Fact]
        public void RouteNamesBody_RoundTrips_AndRefusesWhatItCannotRead()
        {
            NodeId one = NodeId.FromLegacy(0xCB007101, 6112, 20);
            NodeId two = NodeId.FromLegacy(0xC6336402, 6112, 2);
            byte[] bytes = new RouteNamesBody { Names = [one, two] }.Serialize();

            Assert.Equal(1 + 2 * NodeId.WireSize, bytes.Length);
            Assert.Equal(2, bytes[0]);
            Assert.True(RouteNamesBody.TryRead(bytes, out RouteNamesBody read));
            Assert.Equal([one, two], read.Names);
            Assert.False(RouteNamesBody.TryRead(bytes.AsSpan(0, bytes.Length - 1), out _));
            Assert.False(RouteNamesBody.TryRead([101], out _));
            Assert.False(RouteNamesBody.TryRead([], out _));
            Assert.True(RouteNamesBody.TryRead([0], out RouteNamesBody none));
            Assert.Empty(none.Names);
        }

        [Fact]
        public void TheRouteDatagrams_AreThoseOfAppendixA()
        {
            NodeId member = NodeId.FromLegacy(0xCB007101, 6112, 20);
            byte[] names = new RouteNamesBody { Names = [member] }.Serialize();

            Assert.Equal("01" + "000171 00CBE01714".Replace(" ", ""), Convert.ToHexString(names));
            var query = new Envelope(EnvelopeFlags.Internal, 0x1234, 0x5678, MessageClasses.RouteQuery, 0, 0, 0, default, default);
            var answer = new Envelope(EnvelopeFlags.Internal, 0x5678, 0x1234, MessageClasses.RouteAnswer, 0, 0, 0, default, default);
            byte[] buffer = new byte[64];
            int size = query.WriteTo(buffer);
            Assert.Equal("FA02083412785607", Convert.ToHexString(buffer.AsSpan(0, size)));
            size = answer.WriteTo(buffer);
            Assert.Equal("FA02087856341208", Convert.ToHexString(buffer.AsSpan(0, size)));
        }

        // ------------------------------------------------------------------ a second node of this build

        [Fact]
        public void AAndB_AndW_AllSeeAllThroughH()
        {
            var s = new Session(r1: true);
            TestNode b = N(s.Mesh, "198.51.100.3");
            s.Mesh.Partition(b, s.W);
            s.Mesh.Partition(b, s.A);
            JoinOverJfp2(s.Mesh, b, s.H);

            s.Mesh.Run(20);

            foreach ((TestNode x, TestNode y) in new[] { (s.A, b), (b, s.A), (b, s.W), (s.W, b), (s.A, s.W), (s.W, s.A) })
            {
                Assert.True(x.Core.Peers.TryGet(y.Id, out Peer? peer), x.Id + " does not know " + y.Id);
                Assert.True(peer!.SendEstablished);
            }
            SendPosition(b, s.W, 5, 55);
            SendPosition(s.W, b, 6, 66);
            SendPosition(b, s.A, 7, 77);
            s.Mesh.Run(1);
            Assert.Equal(55, s.W.Messages<PositionUpdate>().Single(p => p.ObjectId == 5).Latitude);
            Assert.Equal(66, b.Messages<PositionUpdate>().Single(p => p.ObjectId == 6).Latitude);
            Assert.Equal(77, s.A.Messages<PositionUpdate>().Single(p => p.ObjectId == 7).Latitude);
        }
    }
}
