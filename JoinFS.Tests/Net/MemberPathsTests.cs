using System.Net;
using System.Net.Sockets;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Paths between members (docs/jfp2/design-future.md §4.1): the node a member joined through, its hub,
    /// tells it where the other members can be reached over IPv6 (PeerCandidates), so that two members open an
    /// IPv6 path to each other and probe each other at the same time, through both stateful firewalls. The pairs
    /// here have no direct IPv4 path (the hub relays them), so no Hello passes between them: the hub's
    /// message is what starts the probing.
    /// </summary>
    public class MemberPathsTests
    {
        static TestNode Dual(TestMesh mesh, string ip4, string ip6) => mesh.AddDualStack(ip4, ip6, 6112, new LegacyPlugin(), new Jfp2Plugin());
        static TestNode V4Only(TestMesh mesh, string ip4) => mesh.Add(ip4, 6112, new LegacyPlugin(), new Jfp2Plugin());
        static TestNode Release1(TestMesh mesh, string ip4) =>
            mesh.Add(ip4, 6112, new LegacyPlugin(), new Jfp2Plugin(profile: Jfp2Profile.Default.WithoutInternal(MessageClasses.PeerCandidates)));

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static Peer PeerOf(TestNode node, TestNode peer) => node.Core.Peers.TryGet(peer.Id, out Peer p) ? p : null!;

        static bool OnIpv6(TestNode node, TestNode peer) => PeerOf(node, peer).EndPoint.AddressFamily == AddressFamily.InterNetworkV6;

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

        /// <summary>A hub with two dual-stack members that cannot reach each other over IPv4, each behind a stateful IPv6 firewall.</summary>
        static (TestMesh Mesh, TestNode Hub, TestNode A, TestNode B) RelayedPair(bool firewalls = true)
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode b = Dual(mesh, "192.0.2.3", "2001:db8::3");
            mesh.Partition(a, b);
            if (firewalls)
            {
                mesh.Firewall(a);
                mesh.Firewall(b);
            }
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            return (mesh, hub, a, b);
        }

        // ------------------------------------------------------------------ what is on the wire

        static bool IsPeerCandidates(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Internal) != 0 && data[7] == MessageClasses.PeerCandidates;

        static IEnumerable<(IPEndPoint From, IPEndPoint To, PeerCandidates Message)> PeerCandidatesSent(TestMesh mesh) =>
            mesh.Network.Log.Where(d => IsPeerCandidates(d.Data)).Select(d =>
            {
                Envelope.TryReadFrom(d.Data, out _, out int header, out _);
                return (d.From, d.To, new PeerCandidatesV1Codec().Decode(d.Data.AsSpan(header)));
            });

        static bool SentTo(TestMesh mesh, TestNode from, IPAddress to) =>
            mesh.Network.Log.Any(d => from.OwnsAddress(d.From) && d.To.Address.Equals(to));

        // ------------------------------------------------------------------ the main case

        [Fact]
        public void TwoMembersWithoutAnIPv4Path_OpenAnIPv6PathThroughBothFirewalls_OnTheHubsAnnouncement()
        {
            var (mesh, hub, a, b) = RelayedPair();

            mesh.Run(0.5);
            Assert.DoesNotContain(mesh.Network.Log, d => a.OwnsAddress(d.From) && d.To.Equals(b.EndPoint6));   // nothing is known of IPv6 yet

            mesh.Run(22);

            Assert.True(OnIpv6(a, b) && OnIpv6(b, a));
            Assert.Equal(b.EndPoint6, PeerOf(a, b).EndPoint);
            Assert.Equal(a.EndPoint6, PeerOf(b, a).EndPoint);
            foreach (Peer peer in new[] { PeerOf(a, b), PeerOf(b, a) })
            {
                Assert.False(peer.Relayed);
                Assert.Same(peer.EndPoint, peer.RouteEndPoint);
                Assert.True(peer.Direct && peer.RouteIsOwnEndPoint);
            }
            Assert.True(a.Knows(b) && b.Knows(a));
            Assert.Contains(PeerCandidatesSent(mesh), m => hub.OwnsAddress(m.From) && a.OwnsAddress(m.To));
            Assert.Contains(a.Logs, l => l.StartsWith("NETWORK: Path upgrade " + b.Id));
            Assert.Contains(b.Logs, l => l.StartsWith("NETWORK: Path upgrade " + a.Id));
        }

        [Fact]
        public void TheMembersPath_CarriesJfp2AndLegacy_AndPositionsFlowBothWays()
        {
            var (mesh, hub, a, b) = RelayedPair();
            mesh.Run(18);
            Assert.True(OnIpv6(a, b));
            mesh.Run(10);                                                         // the Hello goes over the new path
            Assert.Equal(b.Id, Jfp2Of(a).NextHopNode(b.Id));
            Assert.Equal(a.Id, Jfp2Of(b).NextHopNode(a.Id));
            Assert.Equal("JFP2", a.Core.Route(b.Id, MessageKind.Position)!.Name);
            int before = mesh.Network.Log.Count;

            SendPosition(a, b, 3, 12.5);
            SendPosition(b, a, 4, 21.5);
            a.Core.SendTo(b.Id, new RemoveObject { ObjectId = 77 }, true);          // legacy, guaranteed
            mesh.Run(3);

            Assert.Equal(12.5, Assert.Single(b.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(21.5, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(77u, Assert.Single(b.Messages<RemoveObject>()).ObjectId);
            Assert.Equal(0, a.Core.Plugins.OfType<LegacyPlugin>().Single().GuaranteedOutCount);
            // nothing between the two went through the hub or over IPv4 any more
            Assert.DoesNotContain(mesh.Network.Log.Skip(before), d => a.OwnsAddress(d.From) && hub.OwnsAddress(d.To) && d.Data[0] == Envelope.Magic && (d.Data[2] & (byte)EnvelopeFlags.Forwarded) != 0);
            Assert.DoesNotContain(mesh.Network.Log.Skip(before), d => d.From.AddressFamily == AddressFamily.InterNetwork && a.OwnsAddress(d.From) && b.OwnsAddress(d.To));
        }

        [Fact]
        public void IPv6ThatCannotBeReached_LeavesTheRelayedPairWhereItWas_AndNothingExpires()
        {
            var (mesh, hub, a, b) = RelayedPair();
            mesh.CutIpv6(a, b);

            mesh.Run(70);

            Assert.False(OnIpv6(a, b) || OnIpv6(b, a));
            Assert.True(PeerOf(a, b).Relayed && PeerOf(b, a).Relayed);
            Assert.True(a.Knows(b) && b.Knows(a));
            SendPosition(a, b, 3, 5.5);
            mesh.Run(2);
            Assert.Equal(5.5, Assert.Single(b.Messages<PositionUpdate>()).Latitude);
        }

        [Fact]
        public void WhenTheIPv6PathBreaks_TheMembersAreSearchedForAgain_AndDoNotExpire()
        {
            var (mesh, hub, a, b) = RelayedPair();
            mesh.Run(18);
            Assert.True(OnIpv6(a, b) && OnIpv6(b, a));
            mesh.CutIpv6(a, b);

            mesh.Run(15);

            Assert.False(OnIpv6(a, b) || OnIpv6(b, a));
            Assert.Contains(a.Logs, l => l.StartsWith("NETWORK: Path fallback " + b.Id));
            mesh.Run(12);                                                            // their IPv4 endpoints were never reachable: the hub's relay is found
            Assert.True(PeerOf(a, b).Relayed && PeerOf(b, a).Relayed);
            Assert.Equal(hub.Id, PeerOf(a, b).RouteVia);

            SendPosition(a, b, 3, 7.5);
            mesh.Run(40);                                                           // longer than a peer's expiry
            Assert.True(a.Knows(b) && b.Knows(a));
            Assert.Equal(7.5, Assert.Single(b.Messages<PositionUpdate>()).Latitude);
        }

        [Fact]
        public void ARelayedPair_BecomesDirectOverIPv6_AndGoesBackToItsRelayOnAFallback()
        {
            var (mesh, hub, a, b) = RelayedPair();
            Action restore = mesh.CutIpv6(a, b);
            mesh.Run(10);
            Assert.True(PeerOf(a, b).Relayed && PeerOf(b, a).Relayed);              // only the hub connects them
            Assert.Equal(hub.Id, PeerOf(a, b).RouteVia);
            Assert.True(a.Knows(b) && b.Knows(a));
            SendPosition(a, b, 3, 1.5);
            mesh.Run(1);
            Assert.Equal(1.5, Assert.Single(b.Messages<PositionUpdate>()).Latitude);   // through the hub

            restore();
            mesh.Run(12);

            Assert.True(OnIpv6(a, b) && OnIpv6(b, a));
            Assert.False(PeerOf(a, b).Relayed || PeerOf(b, a).Relayed);
            Assert.Contains(a.Logs, l => l.StartsWith("NETWORK: Path upgrade " + b.Id) && l.Contains("was relayed through " + hub.Id));
            Assert.True(a.Knows(b) && b.Knows(a));

            mesh.CutIpv6(a, b);
            mesh.Run(12);

            Peer back = PeerOf(a, b);
            Assert.False(OnIpv6(a, b));
            Assert.True(back.Relayed);
            Assert.Equal(hub.Id, back.RouteVia);
            Assert.Equal(PeerOf(a, hub).RouteEndPoint, back.RouteEndPoint);
            Assert.True(a.Knows(b) && b.Knows(a));
            SendPosition(a, b, 4, 2.5);
            mesh.Run(40);
            Assert.True(a.Knows(b) && b.Knows(a));
            Assert.Equal(2.5, b.Messages<PositionUpdate>().Last().Latitude);
            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b.Id));
        }

        [Fact]
        public void UpgradeInOneDirectionOnly_EachSideUsesWhatWorks()
        {
            var (mesh, hub, a, b) = RelayedPair();
            // A's PulseResponses never reach B: B's probes get no answer, A's probes do
            var previous = mesh.Network.Filter;
            mesh.Network.Filter = (from, to, data) =>
                !(from.Equals(a.EndPoint6) && to.Equals(b.EndPoint6) && IsPulseResponse(data)) && (previous == null || previous(from, to, data));

            mesh.Run(25);

            Assert.True(OnIpv6(a, b));
            Assert.False(OnIpv6(b, a));
            Assert.True(PeerOf(b, a).Relayed);
            Assert.True(a.Knows(b) && b.Knows(a));
            SendPosition(a, b, 3, 1.5);
            SendPosition(b, a, 4, 2.5);
            mesh.Run(3);
            Assert.Equal(1.5, Assert.Single(b.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(2.5, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
        }

        static bool IsPulseResponse(byte[] data) =>
            data.Length > LegacyWire.DataOffset + 1 && data[0] == 0x0B && (data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0
            && BitConverter.ToInt16(data, LegacyWire.DataOffset) == (short)LegacyWire.InternalId.PulseResponse;

        // ------------------------------------------------------------------ mixed sessions

        [Fact]
        public void MixedSession_ABOnIPv6_ACAndBCOnIPv4_PositionsEverywhere()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode b = Dual(mesh, "192.0.2.3", "2001:db8::3");
            TestNode c = V4Only(mesh, "192.0.2.4");
            mesh.Partition(a, b);
            mesh.Firewall(a);
            mesh.Firewall(b);
            hub.Core.Mesh.Create(false, 0, false, "");
            foreach (TestNode member in new[] { a, b, c }) member.Core.Mesh.Join(hub.EndPoint, 0);

            mesh.Run(25);

            Assert.True(OnIpv6(a, b) && OnIpv6(b, a));
            Assert.False(OnIpv6(a, c) || OnIpv6(b, c) || OnIpv6(c, a) || OnIpv6(c, b));
            Assert.Equal(c.EndPoint, PeerOf(a, c).EndPoint);
            Assert.Equal(c.EndPoint, PeerOf(b, c).EndPoint);
            Assert.All(new[] { (a, b), (b, a), (a, c), (c, a), (b, c), (c, b), (a, hub), (b, hub), (c, hub), (hub, a), (hub, b), (hub, c) }, pair => Assert.True(pair.Item1.Knows(pair.Item2)));
            Assert.DoesNotContain(mesh.Network.Log, d => c.OwnsAddress(d.From) && d.To.AddressFamily == AddressFamily.InterNetworkV6);
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.AddressFamily == AddressFamily.InterNetworkV6 && c.OwnsAddress(d.To));
            Assert.DoesNotContain(PeerCandidatesSent(mesh), m => c.OwnsAddress(m.To));      // C did not offer the class

            uint id = 10;
            var nodes = new[] { hub, a, b, c };
            foreach (TestNode from in nodes)
            {
                foreach (TestNode to in nodes.Where(n => n != from))
                {
                    SendPosition(from, to, id, id);
                    id++;
                }
            }
            mesh.Run(3);
            foreach (TestNode to in nodes)
            {
                Assert.Equal(3, to.Messages<PositionUpdate>().Count());
            }
        }

        static IEnumerable<HandshakeMessage> Hellos(TestMesh mesh, TestNode from) =>
            mesh.Network.Log.Where(d => from.OwnsAddress(d.From) && d.Data[0] == Envelope.Magic && Envelope.TryReadFrom(d.Data, out Envelope e, out _, out _)
                && e.RawMessageClass == MessageClasses.Hello && (e.Flags & EnvelopeFlags.Internal) != 0)
                .Select(d => { Envelope.TryReadFrom(d.Data, out _, out int header, out _); return HandshakeMessage.Deserialize(d.Data.AsSpan(header)); });

        [Fact]
        public void OnlyANodeWithAnIPv6Candidate_OffersPeerCandidates_SoTheHelloOfAnIPv4OnlyNodeIsUnchanged()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode c = V4Only(mesh, "192.0.2.4");
            hub.Core.Mesh.Create(false, 0, false, "");
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(5);

            Assert.NotEmpty(Hellos(mesh, hub));
            Assert.All(Hellos(mesh, hub), h => Assert.Contains(h.Offers, o => o.Internal && o.MessageClass == MessageClasses.PeerCandidates && o.MinVersion == 1 && o.MaxVersion == 1));
            Assert.NotEmpty(Hellos(mesh, c));
            Assert.All(Hellos(mesh, c), h => Assert.DoesNotContain(h.Offers, o => o.Internal));
            Assert.All(Hellos(mesh, c), h => Assert.Equal(10, h.Offers.Count));      // the ten application classes of release 1
        }

        // ------------------------------------------------------------------ release-1 builds stay on IPv4

        [Fact]
        public void AMemberWithoutIPv6OrTheClass_IsNeverToldAnyCandidates_AndItsPairsStayOnIPv4()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode r = Release1(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            r.Core.Mesh.Join(hub.EndPoint, 0);

            mesh.Run(20);

            Assert.DoesNotContain(PeerCandidatesSent(mesh), m => r.OwnsAddress(m.To));
            Assert.DoesNotContain(mesh.Network.Log, d => d.To.AddressFamily == AddressFamily.InterNetworkV6 && r.OwnsAddress(d.From));
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.AddressFamily == AddressFamily.InterNetworkV6 && r.OwnsAddress(d.To));
            Assert.False(OnIpv6(a, r) || OnIpv6(hub, r));
            Assert.True(a.Knows(r) && r.Knows(a));
            SendPosition(r, a, 3, 4.5);
            mesh.Run(1);
            Assert.Equal(4.5, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
        }

        [Fact]
        public void AHubWithoutIPv6_NeverAnnouncesCandidates_SoMembersThatCannotReachEachOtherOverIPv4StayRelayed()
        {
            var mesh = new TestMesh();
            TestNode hub = Release1(mesh, "203.0.113.1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode b = Dual(mesh, "192.0.2.3", "2001:db8::3");
            mesh.Partition(a, b);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);

            mesh.Run(40);

            Assert.Empty(PeerCandidatesSent(mesh));
            Assert.DoesNotContain(mesh.Network.Log, d => d.To.AddressFamily == AddressFamily.InterNetworkV6);
            Assert.True(PeerOf(a, b).Relayed && PeerOf(b, a).Relayed);
            Assert.True(a.Knows(b) && b.Knows(a));
        }

        // ------------------------------------------------------------------ who may announce, and about whom

        static readonly IPEndPoint Planted = new(IPAddress.Parse("2001:db8:bad::1"), 6112);

        /// <summary>
        /// A hub, two dual-stack members A and P that are directly connected (IPv4, JFP2 negotiated), and C,
        /// an IPv4-only member whose candidates only the tests announce.
        /// </summary>
        static (TestMesh Mesh, TestNode Hub, TestNode A, TestNode P, TestNode C) Members()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode p = Dual(mesh, "192.0.2.5", "2001:db8::5");
            TestNode c = V4Only(mesh, "192.0.2.4");
            hub.Core.Mesh.Create(false, 0, false, "");
            foreach (TestNode member in new[] { a, p, c }) member.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(15);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id) && Jfp2Of(p).IsNegotiated(a.Id));
            return (mesh, hub, a, p, c);
        }

        static PeerCandidates Entry(NodeId node, IPEndPoint candidate) =>
            new() { Entries = [new PeerCandidateEntry { Node = node, Candidates = [candidate] }] };

        [Fact]
        public void PeerCandidates_FromTheNodeTheMemberJoinedThrough_AreKeptAndProbed()
        {
            var (mesh, hub, a, p, c) = Members();
            hub.Core.SendTo(a.Id, Entry(c.Id, Planted), true);
            mesh.Run(0.3);
            Assert.Equal([Planted], PeerOf(a, c).Candidates);

            mesh.Run(1.5);

            Assert.True(SentTo(mesh, a, Planted.Address));      // the control of the next test
        }

        [Fact]
        public void PeerCandidates_FromAnotherMember_AreIgnored()
        {
            var (mesh, hub, a, p, c) = Members();
            p.Core.SendTo(a.Id, Entry(c.Id, Planted), true);

            mesh.Run(15);

            Assert.Empty(PeerOf(a, c).Candidates);
            Assert.False(SentTo(mesh, a, Planted.Address));
            Assert.Contains(PeerCandidatesSent(mesh), m => p.OwnsAddress(m.From) && a.OwnsAddress(m.To));   // it did arrive
        }

        [Fact]
        public void AnEntryForTheReceiverItself_IsIgnored_AndTheOtherEntriesOfTheMessageAreStillUsed()
        {
            var (mesh, hub, a, p, c) = Members();
            var other = new IPEndPoint(IPAddress.Parse("2001:db8:cafe::1"), 6112);
            hub.Core.SendTo(a.Id, new PeerCandidates
            {
                Entries =
                [
                    new PeerCandidateEntry { Node = a.Id, Candidates = [Planted] },
                    new PeerCandidateEntry { Node = c.Id, Candidates = [other] },
                ],
            }, true);

            mesh.Run(15);

            Assert.False(SentTo(mesh, a, Planted.Address));
            Assert.False(a.Core.Peers.Contains(a.Id));
            Assert.Equal([other], PeerOf(a, c).Candidates);
            Assert.DoesNotContain(a.EventLogs, l => l.StartsWith("ERROR"));
        }

        [Fact]
        public void AnEntryForAMemberNotYetRegistered_IsHeldBriefly_AndAppliedWhenTheMemberArrives()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode c = V4Only(mesh, "192.0.2.4");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(10);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            hub.Core.SendTo(a.Id, Entry(c.Id, Planted), true);
            mesh.Run(2);
            Assert.False(a.Core.Peers.Contains(c.Id));

            c.Core.Mesh.Join(hub.EndPoint, 0);
            for (int i = 0; i < 100 && !a.Core.Peers.Contains(c.Id); i++) mesh.Run(0.05);

            Assert.True(a.Core.Peers.Contains(c.Id));
            Assert.Equal([Planted], PeerOf(a, c).Candidates);
        }

        [Fact]
        public void AnEntryForAMemberThatArrivesTooLate_IsForgotten()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode c = V4Only(mesh, "192.0.2.4");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(10);
            hub.Core.SendTo(a.Id, Entry(c.Id, Planted), true);
            mesh.Run(MeshManager.HeldCandidatesTime + 2);

            c.Core.Mesh.Join(hub.EndPoint, 0);
            for (int i = 0; i < 100 && !a.Core.Peers.Contains(c.Id); i++) mesh.Run(0.05);

            Assert.True(a.Core.Peers.Contains(c.Id));
            Assert.Empty(PeerOf(a, c).Candidates);
            mesh.Run(5);
            Assert.False(SentTo(mesh, a, Planted.Address));
        }

        [Fact]
        public void AnEntryWithNoCandidates_ClearsWhatTheReceiverKnew()
        {
            var (mesh, hub, a, p, c) = Members();
            hub.Core.SendTo(a.Id, Entry(c.Id, Planted), true);
            mesh.Run(0.3);
            Assert.NotEmpty(PeerOf(a, c).Candidates);

            hub.Core.SendTo(a.Id, new PeerCandidates { Entries = [new PeerCandidateEntry { Node = c.Id, Candidates = [] }] }, true);
            mesh.Run(0.3);

            Assert.Empty(PeerOf(a, c).Candidates);
        }

        [Fact]
        public void AnnouncedCandidates_AreHandledLikeReportedOnes_AnObservedSourceIsTakenOnlyInTheirSlash64()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode c = V4Only(mesh, "192.0.2.4");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(10);
            var announced = new IPEndPoint(IPAddress.Parse("2001:db8:5::c"), 6112);
            hub.Core.SendTo(a.Id, Entry(c.Id, announced), true);
            mesh.Run(1);
            Assert.Equal([announced], PeerOf(a, c).Candidates);
            byte[] pulse = mesh.Network.Log.First(d => c.OwnsAddress(d.From) && a.OwnsAddress(d.To) && d.Data[0] == 0x0B
                && (d.Data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0
                && BitConverter.ToInt16(d.Data, LegacyWire.DataOffset) == (short)LegacyWire.InternalId.Pulse).Data;
            var sameNetwork = new IPEndPoint(IPAddress.Parse("2001:db8:5::77"), 6112);
            var otherNetwork = new IPEndPoint(IPAddress.Parse("2001:db8:9::77"), 6112);

            a.Core.OnDatagram(otherNetwork, pulse);
            Assert.Equal([announced], PeerOf(a, c).Candidates);
            a.Core.OnDatagram(sameNetwork, pulse);

            Assert.Equal([sameNetwork, announced], PeerOf(a, c).Candidates);
        }

        // ------------------------------------------------------------------ the hub's side

        [Fact]
        public void TheHubTellsMembersAboutACandidateItObservedLater()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode c = V4Only(mesh, "192.0.2.4");                 // reports no candidate: the hub takes the source of its probes
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(10);
            byte[] pulse = mesh.Network.Log.First(d => c.OwnsAddress(d.From) && hub.OwnsAddress(d.To) && d.Data[0] == 0x0B
                && (d.Data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0
                && BitConverter.ToInt16(d.Data, LegacyWire.DataOffset) == (short)LegacyWire.InternalId.Pulse).Data;
            var observed = new IPEndPoint(IPAddress.Parse("2001:db8::cc"), 6112);
            Assert.Empty(PeerOf(a, c).Candidates);

            hub.Core.OnDatagram(observed, pulse);
            mesh.Run(3);

            Assert.Equal([observed], PeerOf(hub, c).Candidates);
            Assert.Equal([observed], PeerOf(a, c).Candidates);
        }

        [Fact]
        public void ALongListIsSentInSeveralMessages_AndEveryMemberLearnsEveryOtherMember()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            var members = new List<TestNode>();
            for (int n = 0; n < 15; n++)
            {
                members.Add(Dual(mesh, "198.51.100." + (10 + n), "2001:db8::" + (0x100 + n).ToString("x")));
            }
            hub.Core.Mesh.Create(false, 0, false, "");
            foreach (TestNode member in members) member.Core.Mesh.Join(hub.EndPoint, 0);

            mesh.Run(25);

            var toLast = PeerCandidatesSent(mesh).Where(m => hub.OwnsAddress(m.From) && members[^1].OwnsAddress(m.To)).ToList();
            Assert.All(toLast, m => Assert.True(m.Message.Entries.Count <= PeerCandidatesV1Codec.MaxEntries));
            Assert.Equal(14, toLast.SelectMany(m => m.Message.Entries).Select(e => e.Node).Distinct().Count());
            Assert.True(toLast.Count >= 2);
            foreach (TestNode member in members)
            {
                foreach (TestNode other in members.Where(o => o != member))
                {
                    Assert.True(PeerOf(member, other).Candidates.Count > 0, member.EndPoint + " does not know the candidates of " + other.EndPoint);
                }
            }
        }

        [Fact]
        public void TheHubTellsEachMemberOnlyWhatChanged()
        {
            var (mesh, hub, a, p, c) = Members();
            int sent = PeerCandidatesSent(mesh).Count();
            Assert.True(sent > 0);

            mesh.Run(30);

            Assert.Equal(sent, PeerCandidatesSent(mesh).Count());
        }
    }
}
