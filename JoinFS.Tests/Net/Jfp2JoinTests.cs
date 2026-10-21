using System.Net;
using System.Security.Cryptography;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Join over JFP2 (docs/network/jfp2/protocol.md §5.10, plan stage 4, scenario S4 for the join itself). The
    /// nodes register no legacy plugin, so a legacy datagram between them is impossible, and the datagram
    /// log says so too.
    /// </summary>
    public class Jfp2JoinTests
    {
        static TestNode Node(TestMesh mesh, string ip, Jfp2Profile? profile = null) => mesh.Add(ip, 6112, new Jfp2Plugin(profile: profile));

        static TestNode Hub(TestMesh mesh, string ip = "203.0.113.1", uint password = 0, Jfp2Profile? profile = null)
        {
            TestNode hub = Node(mesh, ip, profile);
            hub.Core.Mesh.Create(false, password, false, "");
            return hub;
        }

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        /// <summary>The joiner starts a JFP2 join of the node and the mesh runs for <paramref name="seconds"/>.</summary>
        static bool Join(TestMesh mesh, TestNode joiner, TestNode hub, uint password = 0, double seconds = 2)
        {
            bool started = joiner.Core.Mesh.JoinOverJfp2(hub.EndPoint, password);
            mesh.Run(seconds);
            return started;
        }

        static IEnumerable<(IPEndPoint From, IPEndPoint To, byte[] Data)> Datagrams(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Where(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint));

        static bool IsInternal(byte[] data, byte messageClass) => data[0] == Envelope.Magic && (data[2] & 0x08) != 0 && data[7] == messageClass;

        static IdentityUpdate Identity(uint id) => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = "N1", Model = "M", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static void SendPosition(TestNode from, TestNode to, uint id, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id));
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        // ------------------------------------------------------------------ S4: the join itself

        [Fact]
        public void AJoinsH_WithNoLegacyDatagramBetweenThem()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            Assert.True(Join(mesh, a, h));

            Assert.True(a.Core.Mesh.Connected);
            Assert.Equal(h.Core.Mesh.Suid, a.Core.Mesh.Suid);
            Assert.True(h.Core.Peers.TryGet(a.Id, out Peer? aAtH));
            Assert.True(a.Core.Peers.TryGet(h.Id, out Peer? hAtA));
            Assert.True(aAtH!.SendEstablished && aAtH.ReceiveEstablished && !aAtH.LegacyLink && aAtH.RouteIsOwnEndPoint);
            Assert.True(hAtA!.SendEstablished && !hAtA.LegacyLink && hAtA.RouteIsOwnEndPoint);
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
            Assert.Contains(h.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == a.Id);
            Assert.Contains(a.Events, e => e.Kind == NetworkEventKind.SessionJoined);
            Assert.Contains(a.Events, e => e.Kind == NetworkEventKind.PeerEstablished && e.Node == h.Id);
            Assert.False(Jfp2Of(a).JoinPending);
            Assert.Equal(PeerLinkState.Negotiated, Jfp2Of(h).DescribeLink(aAtH));
            Assert.Contains(h.EventLogs, l => l.Contains("joined over JFP2") && l.Contains("198.51.100.2"));
            Assert.Contains(a.EventLogs, l => l.Contains("joined session") && l.Contains("over JFP2"));
        }

        [Fact]
        public void TheJoin_IsOneJoinOneJoinResultAndNoMemberListWhenAlone()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h);

            Assert.Single(Datagrams(mesh, a, h), d => IsInternal(d.Data, MessageClasses.Join));
            Assert.Single(Datagrams(mesh, h, a), d => IsInternal(d.Data, MessageClasses.JoinResult));
            Assert.DoesNotContain(Datagrams(mesh, h, a), d => IsInternal(d.Data, MessageClasses.MemberList));
        }

        [Fact]
        public void AfterTheJoin_PositionsFlowBothWays_OverJfp2()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            Join(mesh, a, h);

            SendPosition(a, h, 1, 11);
            SendPosition(h, a, 2, 22);
            mesh.Run(0.5);

            Assert.Equal(11, Assert.Single(h.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(22, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
        }

        [Fact]
        public void AMember_StaysAMemberAtBothEnds_ByKeepalive()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            Join(mesh, a, h);

            mesh.Run(90);

            Assert.True(h.Core.Peers.Contains(a.Id));
            Assert.True(a.Core.Peers.Contains(h.Id));
            Assert.DoesNotContain(h.Events, e => e.Kind == NetworkEventKind.PeerLeft);
            Assert.DoesNotContain(a.Events, e => e.Kind == NetworkEventKind.PeerLeft);
        }

        [Fact]
        public void AMemberThatGoesSilent_ExpiresAtTheHub()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            Join(mesh, a, h);

            mesh.Partition(a, h);
            mesh.Run(45);

            Assert.False(h.Core.Peers.Contains(a.Id));
            Assert.Contains(h.Events, e => e.Kind == NetworkEventKind.PeerLeft && e.Node == a.Id);
        }

        // ------------------------------------------------------------------ passwords

        [Fact]
        public void TheRightPassword_Joins_AndTheHashNeverTravels()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh, password: 0xCAFEBABE);
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h, password: 0xCAFEBABE);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(h.Core.Peers.Contains(a.Id));
            byte[] hash = BitConverter.GetBytes(0xCAFEBABE);
            foreach (var d in mesh.Network.Log)
            {
                Assert.False(d.Data.AsSpan().IndexOf(hash) >= 0, "the password hash is on the wire");
            }
        }

        [Fact]
        public void AWrongPassword_IsRefused_AndTheAppReadsPasswordRequired()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh, password: 0xCAFEBABE);
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h, password: 0x12345678);

            Assert.False(a.Core.Mesh.Connected);
            Assert.False(h.Core.Peers.Contains(a.Id));
            Assert.Equal(JoinResult.PasswordRequired, a.Core.Mesh.ActiveJoinResult);
            Assert.False(Jfp2Of(a).JoinPending);
            Assert.Contains(a.EventLogs, l => l.Contains("refused the join") && l.Contains("PasswordRequired"));
        }

        [Fact]
        public void NoPassword_AtAProtectedSession_IsRefused()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh, password: 0xCAFEBABE);
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h);

            Assert.False(a.Core.Mesh.Connected);
            Assert.Equal(JoinResult.PasswordRequired, a.Core.Mesh.ActiveJoinResult);
        }

        [Fact]
        public void APasswordGiven_ToAnOpenSession_JoinsAnyway()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h, password: 0x12345678);

            Assert.True(a.Core.Mesh.Connected);
        }

        [Fact]
        public void AJoinProof_IsBoundToTheCookieAndTheName_NotToTheSession()
        {
            NodeId name = NodeId.FromLegacy(0xC6336402, 6112, 20);
            byte[] cookie = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            byte[] other = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            byte[] label = "JFP2 Join"u8.ToArray();
            byte[] data = [.. label, .. cookie, .. NameBytes(name)];

            byte[] proof = JoinBody.ComputeProof(0xCAFEBABE, cookie, name);

            Assert.Equal(HMACSHA256.HashData(BitConverter.GetBytes(0xCAFEBABE), data), proof);
            Assert.NotEqual(proof, JoinBody.ComputeProof(0xCAFEBABE, other, name));
            Assert.NotEqual(proof, JoinBody.ComputeProof(0xCAFEBABE, cookie, NodeId.FromLegacy(0xC6336403, 6112, 20)));
            Assert.NotEqual(proof, JoinBody.ComputeProof(0xCAFEBAB0, cookie, name));
        }

        static byte[] NameBytes(NodeId name)
        {
            byte[] bytes = new byte[NodeId.WireSize];
            name.WriteTo(bytes);
            return bytes;
        }

        // ------------------------------------------------------------------ the list

        [Fact]
        public void ALateJoiner_GetsTheListOfMembers()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");
            TestNode c = Node(mesh, "198.51.100.4");
            mesh.Partition(b, c); // no direct link: the list is what tells c of b
            Join(mesh, b, h);

            Join(mesh, c, h);

            Assert.True(c.Core.Peers.TryGet(b.Id, out Peer? bAtC));
            Assert.True(c.Core.Peers.TryGet(h.Id, out _));
            Assert.Equal(2, c.Core.Peers.Count);
            Assert.True(bAtC!.Relayed);
            Assert.Equal(h.Id, bAtC.RouteVia);
            Assert.Equal(b.EndPoint, bAtC.EndPoint);
            Assert.Contains(c.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == b.Id);
            Assert.Single(Datagrams(mesh, h, c), d => IsInternal(d.Data, MessageClasses.MemberList));
            Assert.False(Jfp2Of(c).JoinPending);
        }

        [Fact]
        public void TheListIsSplitAcrossDatagrams_AndEveryMemberArrives()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            // 150 members the hub knows already (peers added straight to its table: only the list matters here)
            var members = new List<NodeId>();
            for (int i = 0; i < 150; i++)
            {
                NodeId id = NodeId.FromLegacy(0x0A000001u + (uint)i * 256, 6112, 20);
                h.Core.Peers.Add(id, new IPEndPoint(new IPAddress(0x0A000001u + (uint)i * 256), 6112), true).ExpireTime = double.MaxValue;
                members.Add(id);
            }
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h);

            var lists = Datagrams(mesh, h, a).Where(d => IsInternal(d.Data, MessageClasses.MemberList)).ToList();
            Assert.True(lists.Count >= 2, "150 members do not fit one datagram");
            Assert.All(lists, d => Assert.True(d.Data.Length <= 1200));
            Assert.All(members, id => Assert.True(a.Core.Peers.Contains(id)));
            Assert.Equal(151, a.Core.Peers.Count);
            Assert.False(Jfp2Of(a).JoinPending);
        }

        // ------------------------------------------------------------------ loss and refusals

        [Fact]
        public void ALostJoinResult_IsAnsweredAgainWhenTheJoinIsRepeated()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");
            TestNode a = Node(mesh, "198.51.100.2");
            Join(mesh, b, h);
            int dropped = 0;
            mesh.Network.Filter = (from, to, data) =>
            {
                if (from.Equals(h.EndPoint) && to.Equals(a.EndPoint) && IsInternal(data, MessageClasses.JoinResult) && dropped++ == 0)
                {
                    return false;
                }
                return true;
            };

            Join(mesh, a, h, seconds: 6);

            Assert.Equal(2, Datagrams(mesh, h, a).Count(d => IsInternal(d.Data, MessageClasses.JoinResult)));
            Assert.True(a.Core.Mesh.Connected);
            Assert.True(a.Core.Peers.Contains(b.Id));
            Assert.Equal(2, Datagrams(mesh, a, h).Count(d => IsInternal(d.Data, MessageClasses.Join)));
            Assert.Equal(1, h.Events.Count(e => e.Kind == NetworkEventKind.PeerJoined && e.Node == a.Id));
        }

        [Fact]
        public void ANodeThatTakesNoJoin_FailsTheJoinWithAReason()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh, profile: Jfp2Profile.Default.WithoutInternal(MessageClasses.Join));
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h, seconds: 4);

            Assert.False(a.Core.Mesh.Connected);
            Assert.False(h.Core.Peers.Contains(a.Id));
            Assert.False(Jfp2Of(a).JoinPending);
            Assert.Contains(a.EventLogs, l => l.Contains("join failed") && l.Contains("does not take a join over JFP2"));
        }

        [Fact]
        public void ANodeInNoSession_IgnoresAJoin_AndTheJoinTimesOut()
        {
            var mesh = new TestMesh();
            TestNode idle = Node(mesh, "203.0.113.1");
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, idle, seconds: 20);

            Assert.False(a.Core.Mesh.Connected);
            Assert.False(Jfp2Of(a).JoinPending);
            Assert.Contains(a.EventLogs, l => l.Contains("join failed") && l.Contains("no answer"));
        }

        [Fact]
        public void AJoinFromAnotherEndpoint_IsNotTheAdmittedSessions()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            TestNode thief = Node(mesh, "198.51.100.9");
            // A is admitted as a stranger, then a Join with A's ids arrives from another endpoint
            Jfp2Of(a).Introduce(h.EndPoint);
            mesh.Run(1);
            Assert.True(Jfp2Of(h).HasNonMemberSession(a.Id));
            Jfp2Of(h).TryGetHopIds(a.Id, out ushort hubId, out ushort aId);
            var envelope = new Envelope(EnvelopeFlags.Internal, aId, hubId, MessageClasses.Join);
            byte[] body = new JoinBody().Serialize();
            byte[] datagram = new byte[envelope.WireSize + body.Length];
            envelope.WriteTo(datagram);
            body.CopyTo(datagram, envelope.WireSize);

            thief.Transport.Send(h.EndPoint, datagram);
            mesh.Run(1);

            Assert.False(h.Core.Peers.Contains(a.Id));
            Assert.Contains(h.Logs, l => l.Contains("is not from") && l.Contains("was admitted - ignored"));
        }

        // ------------------------------------------------------------------ a mixed hub

        [Fact]
        public void AJfp2OnlyMember_IsNeverSentALegacyDatagram_ByAHubThatSpeaksBoth()
        {
            var mesh = new TestMesh();
            TestNode h = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            h.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Node(mesh, "198.51.100.2");

            Join(mesh, a, h);
            mesh.Run(30);

            Assert.True(a.Core.Peers.Contains(h.Id));
            Assert.True(h.Core.Peers.Contains(a.Id));
            Assert.DoesNotContain(Datagrams(mesh, h, a), d => d.Data[0] != Envelope.Magic);
            Assert.DoesNotContain(Datagrams(mesh, a, h), d => d.Data[0] != Envelope.Magic);
        }

        // ------------------------------------------------------------------ stage 5: members come and go

        static bool Left(TestNode node, TestNode who) => node.Events.Any(e => e.Kind == NetworkEventKind.PeerLeft && e.Node == who.Id);

        [Fact]
        public void AMemberAlreadyInTheSession_LearnsOfALateJoiner_WithoutAskingTheHub()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");
            TestNode c = Node(mesh, "198.51.100.4");
            mesh.Partition(b, c); // no direct link: the announcement is what tells b of c
            Join(mesh, b, h);

            Join(mesh, c, h);

            Assert.True(b.Core.Peers.TryGet(c.Id, out Peer? cAtB));
            Assert.True(cAtB!.Relayed);
            Assert.Equal(h.Id, cAtB.RouteVia);
            Assert.Equal(c.EndPoint, cAtB.EndPoint);
            Assert.Contains(b.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == c.Id);
            Assert.Single(Datagrams(mesh, h, b), d => IsInternal(d.Data, MessageClasses.MemberAnnounce));
            Assert.DoesNotContain(Datagrams(mesh, h, c), d => IsInternal(d.Data, MessageClasses.MemberAnnounce));
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
        }

        [Fact]
        public void AMemberThatLeaves_IsDroppedByEveryoneAtOnce_NotAtTheExpiry()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");
            TestNode c = Node(mesh, "198.51.100.4");
            Join(mesh, b, h);
            Join(mesh, c, h);
            Assert.True(b.Core.Peers.Contains(c.Id));

            c.Core.Mesh.Leave();
            mesh.Run(1);

            Assert.False(h.Core.Peers.Contains(c.Id));
            Assert.False(b.Core.Peers.Contains(c.Id));
            Assert.True(b.Core.Peers.Contains(h.Id));
            Assert.True(Left(h, c));
            Assert.True(Left(b, c));
            Assert.Single(Datagrams(mesh, c, h), d => IsInternal(d.Data, MessageClasses.MemberLeave));
            Assert.Single(Datagrams(mesh, h, b), d => IsInternal(d.Data, MessageClasses.MemberLeave));
            Assert.Equal(1, b.Events.Count(e => e.Kind == NetworkEventKind.PeerLeft && e.Node == c.Id));
        }

        [Fact]
        public void AMemberThatCrashes_IsGoneFromTheOthersWhenTheHubExpiresIt()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");
            TestNode c = Node(mesh, "198.51.100.4");
            Join(mesh, b, h);
            Join(mesh, c, h);

            mesh.Partition(c, h);
            mesh.Partition(c, b);
            mesh.Run(45);

            // before MemberLeave, b kept c for as long as the hub answered
            Assert.False(h.Core.Peers.Contains(c.Id));
            Assert.False(b.Core.Peers.Contains(c.Id));
            Assert.True(b.Core.Peers.Contains(h.Id));
            Assert.True(Left(b, c));
        }

        [Fact]
        public void AMemberLeave_NamingANodeTheSenderHasNoSayOver_IsIgnored()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");
            TestNode c = Node(mesh, "198.51.100.4");
            Join(mesh, b, h);
            Join(mesh, c, h);
            Jfp2Of(b).TryGetHopIds(h.Id, out ushort bId, out ushort hId);

            // b says c is gone, but c is the hub's neighbour and not reached through b
            b.Transport.Send(h.EndPoint, Datagram(MessageClasses.MemberLeave, bId, hId, new MemberLeaveBody { Name = c.Id }.Serialize()));
            mesh.Run(1);

            Assert.True(h.Core.Peers.Contains(c.Id));
            Assert.False(Left(h, c));
            Assert.Contains(h.Logs, l => l.Contains("none of its business"));
        }

        [Fact]
        public void ANodeThatRestarts_RejoinsAHubThatStillHoldsItAsAMember()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            TestNode b = Node(mesh, "198.51.100.3");
            Join(mesh, a, h);
            Join(mesh, b, h);
            int announcedBefore = Datagrams(mesh, h, b).Count(d => IsInternal(d.Data, MessageClasses.MemberAnnounce));
            // a crashes: the hub never hears it leave
            var previous = mesh.Network.Filter;
            mesh.Network.Filter = (from, to, data) => !(from.Equals(a.EndPoint) && IsInternal(data, MessageClasses.MemberLeave)) && (previous == null || previous(from, to, data));
            a.Core.Mesh.Leave();
            mesh.Run(0.5);
            Assert.True(h.Core.Peers.Contains(a.Id));

            Join(mesh, a, h, seconds: 3);

            Assert.True(a.Core.Mesh.Connected);
            Assert.False(Jfp2Of(a).JoinPending);
            Assert.True(a.Core.Peers.Contains(h.Id));
            Assert.True(a.Core.Peers.Contains(b.Id));
            Assert.Equal(2, h.Core.Peers.Count);
            Assert.Equal(1, h.Events.Count(e => e.Kind == NetworkEventKind.PeerJoined && e.Node == a.Id));
            Assert.Equal(announcedBefore, Datagrams(mesh, h, b).Count(d => IsInternal(d.Data, MessageClasses.MemberAnnounce)));
            Assert.Contains(h.EventLogs, l => l.Contains("joined again over JFP2"));
            SendPosition(h, a, 5, 55);
            mesh.Run(0.5);
            Assert.Equal(55, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
            mesh.Run(60);
            Assert.True(h.Core.Peers.Contains(a.Id) && a.Core.Peers.Contains(h.Id));
        }

        [Fact]
        public void TheGlobalSessionJoinsSeveralHubsAtOnce_AndEveryJoinCompletes()
        {
            var mesh = new TestMesh();
            TestNode h1 = Node(mesh, "203.0.113.1");
            TestNode h2 = Node(mesh, "203.0.113.2");
            h1.Core.Mesh.Create(true, 0, false, "");
            h2.Core.Mesh.Create(true, 0, false, "");
            TestNode a = Node(mesh, "198.51.100.2");
            a.Core.Mesh.Create(true, 0, false, "");

            a.Core.Mesh.JoinOverJfp2(h1.EndPoint, 0);
            a.Core.Mesh.JoinOverJfp2(h2.EndPoint, 0);
            mesh.Run(3);

            Assert.True(a.Core.Peers.Contains(h1.Id));
            Assert.True(a.Core.Peers.Contains(h2.Id));
            Assert.False(Jfp2Of(a).JoinPending);
        }

        // ------------------------------------------------------------------ stage 5: JFP2 first, legacy after silence (D4, S2)

        static IEnumerable<(IPEndPoint From, IPEndPoint To, byte[] Data)> Legacy(TestMesh mesh, TestNode from, TestNode to) =>
            Datagrams(mesh, from, to).Where(d => d.Data[0] != Envelope.Magic);

        /// <summary>A node that speaks both protocols, as the app does.</summary>
        static TestNode Both(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin());

        [Fact]
        public void ALegacyHub_GetsTheLegacyJoinAfterTwoSeconds()
        {
            var mesh = new TestMesh();
            TestNode o = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            o.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Both(mesh, "198.51.100.2");

            a.Core.Mesh.JoinJfp2First(o.EndPoint, 0);
            mesh.Run(1.8);

            // O ignored the stranger's Hello; nothing legacy has been sent yet
            Assert.False(a.Core.Mesh.Connected);
            Assert.NotEmpty(Datagrams(mesh, a, o));
            Assert.Empty(Legacy(mesh, a, o));

            mesh.Run(0.4);
            Assert.NotEmpty(Legacy(mesh, a, o));
            mesh.Run(3);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(a.Core.Peers.TryGet(o.Id, out Peer? oAtA));
            Assert.True(oAtA!.LegacyLink);
            Assert.True(o.Core.Peers.Contains(a.Id));
            Assert.Contains(a.EventLogs, l => l.Contains("takes no join over JFP2") && l.Contains("joining it over legacy"));
            Assert.False(Jfp2Of(a).JoinPending);
            Assert.Equal(0, Jfp2Of(a).IntroductionCount);
        }

        [Fact]
        public void AnAddressThatTookNoJoinOverJfp2_IsJoinedOverLegacyAtOnceNextTime()
        {
            var mesh = new TestMesh();
            TestNode o = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            o.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Both(mesh, "198.51.100.2");
            a.Core.Mesh.JoinJfp2First(o.EndPoint, 0);
            mesh.Run(5);
            Assert.True(a.Core.Mesh.Connected);
            a.Core.Mesh.Leave();
            mesh.Run(1);
            mesh.Network.Log.Clear();

            a.Core.Mesh.JoinJfp2First(o.EndPoint, 0);
            mesh.Run(0.05);

            // the first thing sent is the legacy Join: no stranger's Hello first (a peer's Hello comes later)
            Assert.NotEmpty(Legacy(mesh, a, o));
            Assert.DoesNotContain(Datagrams(mesh, a, o), d => d.Data[0] == Envelope.Magic);
            mesh.Run(2);
            Assert.True(a.Core.Mesh.Connected);
        }

        [Fact]
        public void TheMemoryOfALegacyOnlyAddress_EndsAfterAnHour()
        {
            var mesh = new TestMesh();
            TestNode o = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            o.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Both(mesh, "198.51.100.2");
            a.Core.Mesh.JoinJfp2First(o.EndPoint, 0);
            mesh.Run(5);
            a.Core.Mesh.Leave();
            mesh.Run(3600);
            mesh.Network.Log.Clear();

            a.Core.Mesh.JoinJfp2First(o.EndPoint, 0);
            mesh.Run(0.1);

            Assert.NotEmpty(Datagrams(mesh, a, o));
            Assert.Empty(Legacy(mesh, a, o));
        }

        [Fact]
        public void AJfp2Hub_IsJoinedOverJfp2_AndNoLegacyDatagramPassesBetweenThem()
        {
            var mesh = new TestMesh();
            TestNode h = Both(mesh, "203.0.113.1");
            h.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Both(mesh, "198.51.100.2");

            a.Core.Mesh.JoinJfp2First(h.EndPoint, 0);
            mesh.Run(20);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(a.Core.Peers.TryGet(h.Id, out Peer? hAtA));
            Assert.False(hAtA!.LegacyLink);
            Assert.Empty(Legacy(mesh, a, h));
            Assert.Empty(Legacy(mesh, h, a));
            Assert.Contains(a.EventLogs, l => l.Contains("joining") && l.Contains("over JFP2"));
        }

        [Fact]
        public void ARefusalOverJfp2_IsFinal_AndNoLegacyJoinFollows()
        {
            var mesh = new TestMesh();
            TestNode h = Both(mesh, "203.0.113.1");
            h.Core.Mesh.Create(false, 0xCAFEBABE, false, "");
            TestNode a = Both(mesh, "198.51.100.2");

            a.Core.Mesh.JoinJfp2First(h.EndPoint, 0x12345678);
            mesh.Run(10);

            Assert.False(a.Core.Mesh.Connected);
            Assert.Equal(JoinResult.PasswordRequired, a.Core.Mesh.ActiveJoinResult);
            Assert.Empty(Legacy(mesh, a, h));
            Assert.False(h.Core.Peers.Contains(a.Id));
        }

        [Fact]
        public void AHubThatTakesNoJoinOverJfp2_IsJoinedOverLegacy_AtOnceWhenItSaysSo()
        {
            var mesh = new TestMesh();
            TestNode h = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(profile: Jfp2Profile.Default.WithoutInternal(MessageClasses.Join)));
            h.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Both(mesh, "198.51.100.2");

            a.Core.Mesh.JoinJfp2First(h.EndPoint, 0);
            mesh.Run(4);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(a.Core.Peers.TryGet(h.Id, out Peer? hAtA));
            Assert.True(hAtA!.LegacyLink);
            Assert.NotEmpty(Legacy(mesh, a, h));
        }

        [Fact]
        public void ANodeWithoutAMembershipProtocol_JoinsOverLegacyAtOnce()
        {
            var mesh = new TestMesh();
            TestNode o = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            o.Core.Mesh.Create(false, 0, false, "");
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin());

            a.Core.Mesh.JoinJfp2First(o.EndPoint, 0);
            mesh.Run(1);

            Assert.True(a.Core.Mesh.Connected);
        }

        // ------------------------------------------------------------------ the tracer bullet: nothing but JFP2 (roadmap item 12)

        [Fact]
        public void AJfp2OnlyMesh_CreatesJoinsExchangesPositionsAndLeaves()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            TestNode b = Node(mesh, "198.51.100.3");
            Join(mesh, a, h);
            Join(mesh, b, h);

            // a and b see each other and the hub, and positions cross between them through the hub
            Assert.True(a.Core.Peers.Contains(b.Id) && b.Core.Peers.Contains(a.Id));
            SendPosition(a, b, 1, 11);
            SendPosition(b, a, 2, 22);
            SendPosition(h, a, 3, 33);
            mesh.Run(0.5);
            var atB = Assert.Single(b.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(11, atB.Message.Latitude);
            Assert.Equal(a.Id, atB.Meta.Sender);
            Assert.Equal(22, Assert.Single(a.MessagesWithMeta<PositionUpdate>(), m => m.Meta.Sender == b.Id).Message.Latitude);
            Assert.Equal(33, Assert.Single(a.MessagesWithMeta<PositionUpdate>(), m => m.Meta.Sender == h.Id).Message.Latitude);

            // nothing expires while everyone is there
            mesh.Run(90);
            Assert.Equal(2, h.Core.Peers.Count);
            Assert.Equal(2, a.Core.Peers.Count);
            Assert.Equal(2, b.Core.Peers.Count);
            Assert.All(new[] { h, a, b }, node => Assert.DoesNotContain(node.Events, e => e.Kind == NetworkEventKind.PeerLeft));

            // leaving: the others hear of it
            b.Core.Mesh.Leave();
            mesh.Run(1);
            Assert.False(a.Core.Peers.Contains(b.Id));
            Assert.False(h.Core.Peers.Contains(b.Id));
            a.Core.Mesh.Leave();
            mesh.Run(1);
            Assert.Equal(0, h.Core.Peers.Count);
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
        }

        // ------------------------------------------------------------------ the bodies

        [Fact]
        public void JoinBody_RoundTrips_AndIsFixedSize()
        {
            byte[] proof = Enumerable.Range(0, 32).Select(i => (byte)(0xA0 + i)).ToArray();

            byte[] bytes = new JoinBody { HasProof = true, Proof = proof }.Serialize();

            Assert.Equal(33, bytes.Length);
            Assert.Equal(1, bytes[0]);
            Assert.True(JoinBody.TryRead(bytes, out JoinBody read));
            Assert.True(read.HasProof);
            Assert.Equal(proof, read.Proof);
            Assert.Equal(new byte[33], new JoinBody().Serialize());
            Assert.False(JoinBody.TryRead(bytes.AsSpan(0, 32), out _));
        }

        [Fact]
        public void JoinResultBody_HasTheWireOfSection510()
        {
            byte[] bytes = new JoinResultBody { Result = JoinResult.PasswordRequired, Suid = 0x01020304, ListSegments = 2 }.Serialize();

            Assert.Equal(Convert.FromHexString("010403020102"), bytes);
            Assert.True(JoinResultBody.TryRead(bytes, out JoinResultBody read));
            Assert.Equal(JoinResult.PasswordRequired, read.Result);
            Assert.Equal(0x01020304u, read.Suid);
            Assert.Equal(2, read.ListSegments);
            Assert.False(JoinResultBody.TryRead(bytes.AsSpan(0, 5), out _));
        }

        [Fact]
        public void MemberListBody_HasTheWireOfSection510()
        {
            NodeId legacy = NodeId.FromLegacy(0xCB007101, 6112, 20);
            NodeId key = NodeId.ReadFrom(Convert.FromHexString("0211223344556677"));
            var list = new MemberListBody
            {
                Index = 1,
                Entries = [new MemberEntry([legacy, key], new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112))],
            };

            byte[] bytes = list.Serialize();

            Assert.Equal(Convert.FromHexString(
                "01" + "01" +                                   // index, one entry
                "02" + "00017100CBE01714" + "0211223344556677" + // two names
                "07" + "04CB007101E017"), bytes);                // the endpoint, 7 bytes: family 4, 203.0.113.1, port 6112
            Assert.True(MemberListBody.TryRead(bytes, out MemberListBody read));
            Assert.Equal(1, read.Index);
            MemberEntry entry = Assert.Single(read.Entries);
            Assert.Equal([legacy, key], entry.Names);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112), entry.EndPoint);
        }

        static byte[] Datagram(byte messageClass, ushort sender, ushort recipient, byte[] body)
        {
            var envelope = new Envelope(EnvelopeFlags.Internal, sender, recipient, messageClass);
            byte[] datagram = new byte[envelope.WireSize + body.Length];
            envelope.WriteTo(datagram);
            body.CopyTo(datagram, envelope.WireSize);
            return datagram;
        }

        /// <summary>The datagrams of protocol.md Appendix A.6 to A.8. NEVER change these bytes to make a test pass.</summary>
        [Fact]
        public void TheJoinDatagrams_AreThoseOfAppendixA()
        {
            NodeId member = NodeId.FromLegacy(0xCB007101, 6112, 20);

            Assert.Equal(Convert.FromHexString("FA0208341278560200" + new string('0', 64)), Datagram(MessageClasses.Join, 0x1234, 0x5678, new JoinBody().Serialize()));
            Assert.Equal(Convert.FromHexString("FA02087856341203" + "00" + "04030201" + "01"),
                Datagram(MessageClasses.JoinResult, 0x5678, 0x1234, new JoinResultBody { Result = JoinResult.Accepted, Suid = 0x01020304, ListSegments = 1 }.Serialize()));
            Assert.Equal(Convert.FromHexString("FA02087856341204" + "0001" + "01" + "00017100CBE01714" + "07" + "04CB007101E017"),
                Datagram(MessageClasses.MemberList, 0x5678, 0x1234,
                    new MemberListBody { Entries = [new MemberEntry([member], new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112))] }.Serialize()));
        }

        /// <summary>The datagrams of protocol.md Appendix A.9 and A.10. NEVER change these bytes to make a test pass.</summary>
        [Fact]
        public void TheMemberDatagrams_AreThoseOfAppendixA()
        {
            NodeId member = NodeId.FromLegacy(0xCB007101, 6112, 20);

            byte[] announce = Datagram(MessageClasses.MemberAnnounce, 0x5678, 0x4321,
                new MemberAnnounceBody { Entry = new MemberEntry([member], new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112)) }.Serialize());
            byte[] leave = Datagram(MessageClasses.MemberLeave, 0x5678, 0x4321, new MemberLeaveBody { Name = member }.Serialize());

            Assert.Equal(Convert.FromHexString("FA0208785621430501" + "00017100CBE01714" + "07" + "04CB007101E017"), announce);
            Assert.Equal(Convert.FromHexString("FA0208785621430600017100CBE01714"), leave);
            Assert.True(MemberAnnounceBody.TryRead(announce.AsSpan(8), out MemberAnnounceBody readAnnounce));
            Assert.Equal([member], readAnnounce.Entry.Names);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112), readAnnounce.Entry.EndPoint);
            Assert.True(MemberLeaveBody.TryRead(leave.AsSpan(8), out MemberLeaveBody readLeave));
            Assert.Equal(member, readLeave.Name);
        }

        [Fact]
        public void TheMemberBodies_RefuseWhatTheyCannotUse()
        {
            NodeId member = NodeId.FromLegacy(0xCB007101, 6112, 20);
            byte[] announce = new MemberAnnounceBody { Entry = new MemberEntry([member], new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112)) }.Serialize();

            Assert.False(MemberAnnounceBody.TryRead(announce.AsSpan(0, announce.Length - 1), out _));   // cut short
            Assert.False(MemberAnnounceBody.TryRead([], out _));
            Assert.False(MemberAnnounceBody.TryRead([1, .. NameBytes(member), 7, 9, 0, 0, 0, 0, 0, 0], out _)); // an endpoint of an unassigned family
            Assert.False(MemberLeaveBody.TryRead(new byte[7], out _));
            Assert.False(MemberLeaveBody.TryRead(new byte[8], out _));                                    // not a valid name
        }

        [Fact]
        public void MemberListBody_SkipsAnEntryItCannotUse_AndReadsTheRest()
        {
            NodeId legacy = NodeId.FromLegacy(0xCB007101, 6112, 20);
            byte[] good = new MemberListBody { Entries = [new MemberEntry([legacy], new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112))] }.Serialize();
            // an entry whose endpoint has an unassigned family (9), 7 bytes, then the good entry
            byte[] unusable = [1, .. NameBytes(NodeId.FromLegacy(0xCB007102, 6112, 20)), 7, 9, 0, 0, 0, 0, 0, 0];
            byte[] bytes = [0, 2, .. unusable, .. good[2..]];

            Assert.True(MemberListBody.TryRead(bytes, out MemberListBody read));

            Assert.Equal(legacy, Assert.Single(read.Entries).Names[0]);
        }
    }
}
