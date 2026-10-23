using System.Net;
using System.Net.Sockets;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// IPv6 between nodes of this build (docs/network/jfp2/standalone-ipv6-plan.md stage 7; scenarios S1, S3, S6 and S8 of §2.3,
    /// protocol.md §5.14). H is a hub with an IPv4 and an IPv6 address; A, B and D are nodes behind home routers: A and D have
    /// IPv6 behind a stateful firewall, B has IPv4 only. The tests assert what a pilot sees: who is a member, the family of each
    /// link, nothing expiring, and which datagrams went where.
    /// </summary>
    public class Jfp2IPv6Tests
    {
        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static TestNode Node(TestMesh mesh, string ip, string? ip6 = null) =>
            ip6 == null ? mesh.Add(ip, 6112, new Jfp2Plugin()) : mesh.AddDualStack(ip, ip6, 6112, new Jfp2Plugin());

        static TestNode Hub(TestMesh mesh)
        {
            TestNode hub = Node(mesh, "203.0.113.1", "2001:db8::1");
            hub.Core.Mesh.Create(false, 0, false, "");
            return hub;
        }

        /// <summary>The node joins the hub by its host name: the hub's IPv6 address and its IPv4 address, in that order.</summary>
        static void JoinByName(TestMesh mesh, TestNode joiner, TestNode hub, double seconds = 3)
        {
            Assert.True(joiner.Core.Mesh.JoinOverJfp2([hub.EndPoint6!, hub.EndPoint], 0));
            mesh.Run(seconds);
        }

        static Peer PeerOf(TestNode node, TestNode other)
        {
            Assert.True(node.Core.Peers.TryGet(other.Id, out Peer? peer), node.EndPoint + " has no peer " + other.EndPoint);
            return peer!;
        }

        static bool IsV6(IPEndPoint endPoint) => endPoint.AddressFamily == AddressFamily.InterNetworkV6;

        /// <summary>The link from <paramref name="node"/> to <paramref name="other"/> is direct and over IPv6.</summary>
        static bool DirectOverIPv6(TestNode node, TestNode other) =>
            node.Core.Peers.TryGet(other.Id, out Peer? peer) && peer.SendEstablished && !peer.Relayed && IsV6(peer.RouteEndPoint);

        static bool DirectOverIPv4(TestNode node, TestNode other) =>
            node.Core.Peers.TryGet(other.Id, out Peer? peer) && peer.SendEstablished && !peer.Relayed && !IsV6(peer.RouteEndPoint);

        /// <summary>Drop every datagram of one family between two nodes (both directions), while <paramref name="cut"/> says so.</summary>
        sealed class FamilyCut
        {
            public bool Cut = true;

            public FamilyCut(TestMesh mesh, TestNode a, TestNode b, bool ipv6)
            {
                var previous = mesh.Network.Filter;
                IPEndPoint a1 = ipv6 ? a.EndPoint6! : a.EndPoint, b1 = ipv6 ? b.EndPoint6! : b.EndPoint;
                mesh.Network.Filter = (from, to, data) =>
                    !(Cut && ((from.Equals(a1) && to.Equals(b1)) || (from.Equals(b1) && to.Equals(a1))))
                    && (previous == null || previous(from, to, data));
            }
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

        // ------------------------------------------------------------------ S1: joining a host with both addresses

        [Fact]
        public void S1_AJoinsHByName_WithBothAddresses_TalksIPv6AndNothingElse()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");

            JoinByName(mesh, a, h);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(DirectOverIPv6(a, h));
            Assert.True(DirectOverIPv6(h, a));
            // no datagram on IPv4 between them, and none that is legacy
            Assert.DoesNotContain(mesh.Network.Log, d => !IsV6(d.From) || !IsV6(d.To));
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
            Assert.Contains(a.EventLogs, l => l.Contains("joined session") && l.Contains("(IPv6)"));
            Assert.Contains(h.EventLogs, l => l.Contains(" joined over JFP2") && l.Contains("(IPv6)"));
        }

        [Fact]
        public void S1_IPv6ToHIsBroken_TheJoinSucceedsOverIPv4_WithinTheHappyEyeballsDelay()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            _ = new FamilyCut(mesh, a, h, ipv6: true);

            Assert.True(a.Core.Mesh.JoinOverJfp2([h.EndPoint6!, h.EndPoint], 0));
            mesh.Run(1.0);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(DirectOverIPv4(a, h));
            Assert.Contains(a.EventLogs, l => l.Contains("joining through") && l.Contains("(IPv4)") && l.Contains("the first of"));
        }

        [Fact]
        public void S1_IPv6ThatStartsAnsweringLater_TakesTheLinkOver_OnceAndOnlyOnce()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            var cut = new FamilyCut(mesh, a, h, ipv6: true);
            JoinByName(mesh, a, h);
            Assert.True(DirectOverIPv4(a, h));

            cut.Cut = false;
            mesh.Run(130);

            Assert.True(DirectOverIPv6(a, h), "IPv6 answers and is preferred");
            Assert.True(DirectOverIPv6(h, a));
            Assert.Single(a.EventLogs, l => l.Contains("moves from IPv4") && l.Contains("to IPv6"));
            Assert.True(a.Core.Mesh.Connected);
        }

        [Fact]
        public void AnIPv4OnlyNode_JoinsByName_OverIPv4_WithoutTryingIPv6()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = Node(mesh, "198.51.100.3");

            JoinByName(mesh, b, h);

            Assert.True(b.Core.Mesh.Connected);
            Assert.True(DirectOverIPv4(b, h));
            Assert.DoesNotContain(mesh.Network.Log, d => IsV6(d.From) && d.From.Equals(b.EndPoint));
            Assert.DoesNotContain(mesh.Network.Log, d => IsV6(d.To) && d.From.Equals(b.EndPoint));
        }

        // ------------------------------------------------------------------ S3 and S6: two nodes with IPv6 behind firewalls

        [Fact]
        public void S3_AAndD_AreDirectOverIPv6_AndHRelaysNothingForThem()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            TestNode d = Node(mesh, "100.64.0.9", "2001:db8::9");
            mesh.Firewall(a);
            mesh.Firewall(d);
            _ = new FamilyCut(mesh, a, d, ipv6: false); // D is behind CGNAT: nobody reaches its IPv4

            JoinByName(mesh, d, h);
            JoinByName(mesh, a, h, seconds: 12);

            Assert.True(DirectOverIPv6(a, d));
            Assert.True(DirectOverIPv6(d, a));
            mesh.Network.Log.Clear();
            SendPosition(a, d, 7, 51.5);
            mesh.Run(1);
            Assert.Contains(d.Messages<PositionUpdate>(), p => p.ObjectId == 7);
            // the position went from A to D, not through H
            Assert.DoesNotContain(mesh.Network.Log, d2 => d2.From.Equals(a.EndPoint6) && d2.To.Equals(h.EndPoint6) && (d2.Data[2] & (byte)EnvelopeFlags.Forwarded) != 0);
            Assert.Equal(0, h.Core.Mesh.RelayCount);
        }

        [Fact]
        public void S6_AJoinedOverIPv4_AndAnotherNodeOverIPv6_AreDirectOverIPv6()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            TestNode c = Node(mesh, "198.51.100.4", "2001:db8::4");
            mesh.Firewall(a);
            mesh.Firewall(c);

            Assert.True(a.Core.Mesh.JoinOverJfp2(h.EndPoint, 0)); // A joined H over IPv4 alone
            mesh.Run(3);
            JoinByName(mesh, c, h, seconds: 12);

            Assert.True(DirectOverIPv6(a, c), "both try each other at once, which opens both firewalls");
            Assert.True(DirectOverIPv6(c, a));
        }

        [Fact]
        public void AnIPv6Node_AndAnIPv4OnlyNode_AreDirectOverIPv4()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            TestNode b = Node(mesh, "198.51.100.3");

            JoinByName(mesh, a, h);
            JoinByName(mesh, b, h, seconds: 12);

            Assert.True(DirectOverIPv4(a, b));
            Assert.True(DirectOverIPv4(b, a));
            Assert.Contains(PeerOf(b, a).Candidates, c => c.Equals(a.EndPoint6)); // B knows A has IPv6, and has no use for it
        }

        [Fact]
        public void AHubThatAnswersFromAnotherAddressOfItsNetwork_IsStillJoinedOverIPv6()
        {
            // the AAAA record names 2001:db8::10, a second address of the hub's interface, and the hub answers from its preferred one
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            IPEndPoint named = IPEndPoint.Parse("[2001:db8::10]:6112");
            // A's datagrams for the named address reach the hub; its answers come from 2001:db8::1, the address it prefers
            mesh.Network.Nat = (from, to) => (from, to.Equals(named) ? h.EndPoint6! : to);
            Assert.True(a.Core.Mesh.JoinOverJfp2([named, h.EndPoint], 0));
            mesh.Run(4);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(DirectOverIPv6(a, h), "the join went on at the address that answered");
            Assert.Contains(a.EventLogs, l => l.Contains("another address of its network"));
        }

        // ------------------------------------------------------------------ S8: IPv6 breaks mid-flight

        [Fact]
        public void S8_IPv6BreaksBetweenTwoNodes_TheLinkMovesToIPv4_WithinTheKeepaliveTimeout_AndNobodyExpires()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            TestNode b = Node(mesh, "198.51.100.3", "2001:db8::3");
            JoinByName(mesh, a, h);
            JoinByName(mesh, b, h, seconds: 12);
            Assert.True(DirectOverIPv6(a, b));
            var cut = new FamilyCut(mesh, a, b, ipv6: true);

            mesh.Run(25); // the 15 s of silence, and the Hellos at IPv4

            Assert.True(DirectOverIPv4(a, b), "A reaches B over IPv4 now");
            Assert.True(DirectOverIPv4(b, a));
            Assert.Contains(a.EventLogs, l => l.Contains("does not answer on IPv6") && l.Contains("not using IPv6 for"));
            SendPosition(a, b, 9, 52.0);
            mesh.Run(1);
            Assert.Contains(b.Messages<PositionUpdate>(), p => p.ObjectId == 9);

            // IPv6 comes back: held off for 60 s, then tried every 60 s, and the link goes back once, not back and forth
            cut.Cut = false;
            mesh.Run(15);
            Assert.True(DirectOverIPv4(a, b), "still held off");
            mesh.Run(130);
            Assert.True(DirectOverIPv6(a, b));
            Assert.True(DirectOverIPv6(b, a));
            Assert.Single(a.EventLogs, l => l.Contains("moves from IPv4") && l.Contains("to IPv6"));
            Assert.DoesNotContain(a.Events, e => e.Kind == NetworkEventKind.PeerLeft && e.Node == b.Id);
            Assert.DoesNotContain(b.Events, e => e.Kind == NetworkEventKind.PeerLeft && e.Node == a.Id);
        }

        [Fact]
        public void S8_IPv6ToTheHubBreaks_TheLinkToItMovesToIPv4()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            JoinByName(mesh, a, h);
            Assert.True(DirectOverIPv6(a, h));
            _ = new FamilyCut(mesh, a, h, ipv6: true);

            mesh.Run(25);

            Assert.True(a.Core.Mesh.Connected);
            Assert.True(DirectOverIPv4(a, h));
            Assert.True(DirectOverIPv4(h, a));
        }

        // ------------------------------------------------------------------ legacy never sees IPv6

        [Fact]
        public void LegacyIsNeverHandedAnIPv6Endpoint_AndAWSeesAAcrossTheHub()
        {
            var mesh = new TestMesh();
            TestNode h = mesh.AddDualStack("203.0.113.1", "2001:db8::1", 6112, new LegacyPlugin(), new Jfp2Plugin());
            h.Core.Mesh.Create(false, 0, false, "");
            TestNode a = mesh.AddDualStack("198.51.100.2", "2001:db8::2", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode w = mesh.Add("198.51.100.5", 6112, new LegacyPlugin()); // a build released before this work
            mesh.Partition(a, w);

            Assert.True(a.Core.Mesh.JoinOverJfp2([h.EndPoint6!, h.EndPoint], 0));
            mesh.Run(3);
            w.Core.Mesh.Join(h.EndPoint, 0);
            mesh.Run(12);

            Assert.True(DirectOverIPv6(a, h));
            Assert.True(w.Core.Peers.Contains(a.Id), "W sees A");
            Assert.True(a.Core.Peers.Contains(w.Id), "A sees W");
            // every datagram on IPv6 is JFP2, and W never sent or received one
            Assert.DoesNotContain(mesh.Network.Log, d => (IsV6(d.From) || IsV6(d.To)) && d.Data[0] != Envelope.Magic);
            Assert.DoesNotContain(mesh.Network.Log, d => IsV6(d.To) && d.From.Equals(w.EndPoint));
        }

        // ------------------------------------------------------------------ candidates and their wire

        [Fact]
        public void ANodesCandidates_AreItsIPv6AddressesThenItsPublicThenItsLanAddress_OnTheSocketsPort()
        {
            var identity = new LocalIdentity();
            identity.LocalAddress = IPAddress.Parse("192.168.1.20");
            identity.InternetAddress = IPAddress.Parse("203.0.113.7");
            identity.Port = 6112;
            Assert.Equal(["203.0.113.7:6112", "192.168.1.20:6112"], identity.Candidates.Select(c => c.ToString()));

            identity.Ipv6Addresses = [IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2")];
            Assert.Equal(["[2001:db8::1]:6112", "[2001:db8::2]:6112", "203.0.113.7:6112", "192.168.1.20:6112"], identity.Candidates.Select(c => c.ToString()));

            identity.Port = 7000;
            Assert.All(identity.Candidates, c => Assert.Equal(7000, c.Port));
        }

        [Theory]
        [InlineData("2001:db8::1", true)]
        [InlineData("2a02:810d::1", true)]
        [InlineData("fe80::1", false)]
        [InlineData("fd00::1", false)]
        [InlineData("::1", false)]
        [InlineData("ff02::1", false)]
        [InlineData("192.0.2.1", false)]
        public void OnlyAGlobalUnicastIPv6Address_IsACandidate(string address, bool global) =>
            Assert.Equal(global, LocalIdentity.IsGlobalUnicast(IPAddress.Parse(address)));

        [Theory]
        [InlineData("[2001:db8::1]:6112", true)]
        [InlineData("[fe80::1]:6112", false)]
        [InlineData("[::1]:6112", false)]
        [InlineData("203.0.113.1:6112", true)]
        [InlineData("192.168.1.2:6112", true)]
        [InlineData("127.0.0.1:6112", false)]
        [InlineData("0.0.0.0:6112", false)]
        [InlineData("224.0.0.1:6112", false)]
        [InlineData("169.254.1.1:6112", false)]
        [InlineData("203.0.113.1:0", false)]
        public void ACandidateNobodyCouldReach_IsNotKept(string endPoint, bool usable) =>
            Assert.Equal(usable, Peer.IsUsableCandidate(IPEndPoint.Parse(endPoint)));

        [Fact]
        public void TheCandidatesTheMeshKeepsForAMember_LeaveOutTheOnesNobodyCouldReach()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = Node(mesh, "198.51.100.2", "2001:db8::2");
            JoinByName(mesh, a, h);

            ((IMembership)h.Core.Mesh).Advertised(a.Id, [IPEndPoint.Parse("[fe80::1]:6112"), IPEndPoint.Parse("127.0.0.1:6112"), IPEndPoint.Parse("[2001:db8::2]:6112"), IPEndPoint.Parse("[2001:db8::2]:6112")]);

            Assert.Equal(["[2001:db8::2]:6112"], PeerOf(h, a).Candidates.Select(c => c.ToString()));
        }

        [Fact]
        public void HelloCandidates_RoundTrip_AndAnEntryOfAnUnassignedFamilyIsSkipped()
        {
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = 2, ProtoMajorMax = 2, SelfAssignedId = 5, Names = [NodeId.FromLegacy(0xCB007101, 6112, 20)],
                Candidates = [IPEndPoint.Parse("[2001:db8::1]:6112"), IPEndPoint.Parse("203.0.113.1:6112")],
            };
            HandshakeMessage back = HandshakeMessage.Deserialize(hello.Serialize());
            Assert.Equal(hello.Candidates, back.Candidates);

            // an entry of family 9 between two good ones: skipped by its length byte
            byte[] list = [.. HandshakeMessage.WriteCandidates([IPEndPoint.Parse("203.0.113.1:6112")]), 3, 9, 1, 2, .. HandshakeMessage.WriteCandidates([IPEndPoint.Parse("[2001:db8::1]:6112")])];
            Assert.Equal(["203.0.113.1:6112", "[2001:db8::1]:6112"], HandshakeMessage.ReadCandidates(list).Select(c => c.ToString()));
        }

        [Fact]
        public void AHelloWithoutCandidates_ReadsAsNone_AsAReleasedBuildSendsIt()
        {
            var hello = new HandshakeMessage { ProtoMajorMin = 2, ProtoMajorMax = 2, Names = [NodeId.FromLegacy(0xCB007101, 6112, 20)] };
            Assert.Null(HandshakeMessage.Deserialize(hello.Serialize()).Candidates);
        }

        [Fact]
        public void AMemberEntryAtVersion2_CarriesItsCandidates_AndVersion1DoesNot()
        {
            var entry = new MemberEntry([NodeId.FromLegacy(0xCB007101, 6112, 20)], IPEndPoint.Parse("203.0.113.1:6112"),
                [IPEndPoint.Parse("[2001:db8::1]:6112"), IPEndPoint.Parse("203.0.113.1:6112")]);

            var v2 = new MemberListBody { Index = 0, Entries = [entry, entry] };
            Assert.True(MemberListBody.TryRead(v2.Serialize(2), out MemberListBody back2, 2));
            Assert.Equal(2, back2.Entries.Count);
            Assert.Equal(entry.Candidates, back2.Entries[1].Candidates);

            Assert.True(MemberListBody.TryRead(v2.Serialize(1), out MemberListBody back1, 1));
            Assert.Equal(2, back1.Entries.Count);
            Assert.Null(back1.Entries[0].Candidates);

            Assert.True(MemberAnnounceBody.TryRead(new MemberAnnounceBody { Entry = entry }.Serialize(2), out MemberAnnounceBody announce, 2));
            Assert.Equal(entry.Candidates, announce.Entry.Candidates);
        }

        [Fact]
        public void AMemberListCutInsideTheCandidates_EndsWhereItIsCut()
        {
            var entry = new MemberEntry([NodeId.FromLegacy(0xCB007101, 6112, 20)], IPEndPoint.Parse("203.0.113.1:6112"), [IPEndPoint.Parse("[2001:db8::1]:6112")]);
            byte[] bytes = new MemberListBody { Index = 0, Entries = [entry, entry] }.Serialize(2);

            Assert.True(MemberListBody.TryRead(bytes.AsSpan(0, bytes.Length - 5), out MemberListBody cut, 2));
            Assert.Single(cut.Entries);
        }
    }
}
