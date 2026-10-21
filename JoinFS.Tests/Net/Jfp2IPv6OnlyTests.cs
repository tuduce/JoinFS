using System.Net;
using System.Net.Sockets;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Nodes with no IPv4 address (docs/network/jfp2/standalone-ipv6-plan.md stage 8; scenarios S5, S7, S13 and S14 of §2.3, protocol.md
    /// §5.15). H is a hub of this build with an IPv4 and an IPv6 address, C has IPv6 only and is known by the name of its key, W is a
    /// build released before this work (R1 with JFP2 for the application classes, or L with legacy only), A and B are nodes of this build
    /// with IPv4 (A also IPv6). The tests assert what a pilot sees: who is a member, by which name, positions arriving, nothing expiring.
    /// </summary>
    public class Jfp2IPv6OnlyTests
    {
        static readonly NodeId None = default;

        /// <summary>A node of this build: legacy for the nodes that speak nothing else, JFP2 with the membership classes.</summary>
        static IProtocolPlugin[] Plugins() => [new LegacyPlugin(), new Jfp2Plugin()];

        static TestNode Hub(TestMesh mesh)
        {
            TestNode hub = mesh.AddDualStack("203.0.113.1", "2001:db8::1", 6112, Plugins());
            hub.Core.Mesh.Create(false, 0, false, "");
            return hub;
        }

        /// <summary>C: no IPv4 at all, joined to the hub by its host name (the hub's IPv6 address and its IPv4 address).</summary>
        static TestNode C(TestMesh mesh, TestNode hub, string ip6 = "2001:db8::c", double seconds = 3)
        {
            TestNode c = mesh.AddIPv6Only(ip6, 6112, Plugins());
            c.Core.Mesh.JoinJfp2First([hub.EndPoint6!, hub.EndPoint], 0);
            mesh.Run(seconds);
            return c;
        }

        /// <summary>A build released before this work: R1 has JFP2 for the application classes, L has legacy only.</summary>
        static TestNode W(TestMesh mesh, string ip, bool r1) =>
            r1 ? mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin(profile: new Jfp2Profile([.. Jfp2Profile.Default.Classes])))
               : mesh.Add(ip, 6112, new LegacyPlugin());

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

        /// <summary>A position from <paramref name="from"/> to the name <paramref name="to"/> (a node knows another by its own name for it).</summary>
        static void SendPosition(TestNode from, NodeId to, uint id, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id));
            from.Core.SendTo(to, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        static bool Left(TestNode node, NodeId who) => node.Events.Any(e => e.Kind == NetworkEventKind.PeerLeft && e.Node == who);

        static bool IsV6(IPEndPoint endPoint) => endPoint.AddressFamily == AddressFamily.InterNetworkV6;

        static bool IsLegacy(byte[] data) => data[0] != Envelope.Magic;

        /// <summary>The name nodes that speak legacy know C by: the alias its admitting node gave it, as C was told.</summary>
        static NodeId AliasOf(TestNode c)
        {
            Assert.True(c.Core.Identity.Alias.Valid(), "C was given no alias");
            return c.Core.Identity.Alias;
        }

        // ------------------------------------------------------------------ the node with no IPv4

        [Fact]
        public void CHasNoIPv4_IsReadyWithItsKeyName_AndJoinsHOverIPv6AndNothingElse()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);

            TestNode c = C(mesh, h);

            Assert.True(c.Core.Identity.Ready);
            Assert.Equal(NodeId.KeyPairKind, c.Id.Kind);
            Assert.Equal(c.Core.Identity.KeyId, c.Id);
            Assert.True(c.Core.Mesh.Connected);
            Assert.True(c.Core.Peers.TryGet(h.Id, out Peer? hubAtC) && hubAtC.SendEstablished && !hubAtC.Relayed && IsV6(hubAtC.RouteEndPoint));
            Assert.True(h.Core.Peers.TryGet(c.Id, out Peer? cAtHub) && cAtHub.SendEstablished && !cAtHub.Relayed && IsV6(cAtHub.RouteEndPoint));
            Assert.Equal(c.Id, cAtHub.Id); // one peer, by the name C stated
            // C has no IPv4 candidate to advertise
            Assert.All(c.Core.Identity.Candidates, candidate => Assert.True(IsV6(candidate)));
            Assert.Contains(h.EventLogs, l => l.Contains("has no IPv4 address: nodes that speak legacy know it as"));
            Assert.Contains(c.EventLogs, l => l.Contains("gave this node the alias"));
        }

        [Fact]
        public void CJoiningAnIPv4OnlyHub_SaysWhyItCannot()
        {
            var mesh = new TestMesh();
            TestNode o = mesh.Add("203.0.113.9", 6112, new LegacyPlugin());
            o.Core.Mesh.Create(false, 0, false, "");
            TestNode c = mesh.AddIPv6Only("2001:db8::c", 6112, Plugins());

            c.Core.Mesh.JoinJfp2First([o.EndPoint], 0);
            mesh.Run(5);

            Assert.False(c.Core.Mesh.Connected);
            Assert.Contains(c.EventLogs, l => l.Contains("cannot join") && l.Contains("no address in a family it uses"));
            Assert.Empty(mesh.Network.Log.Where(d => d.From.Equals(c.EndPoint) || d.To.Equals(c.EndPoint)));
        }

        [Fact]
        public void TheAlias_IsHsAddressAndPort_WithALanOctetHDoesNotUse_AndIsTheSameAfterARejoin()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode c = C(mesh, h);

            NodeId alias = AliasOf(c);

            Assert.True(alias.TryGetLegacy(out LegacyNodeId legacy));
            Assert.Equal(h.Core.Identity.LegacyId.ip, legacy.ip);
            Assert.Equal(h.Core.Identity.LegacyId.port, legacy.port);
            Assert.NotEqual(h.Core.Identity.LegacyId.local, legacy.local);
            // H knows C by both names, as one peer
            Assert.True(h.Core.Peers.TryResolve(alias, out NodeId resolved));
            Assert.Equal(c.Id, resolved);

            c.Core.Mesh.Leave();
            mesh.Run(2);
            c.Core.Identity.Alias = None;
            c.Core.Mesh.JoinJfp2First([h.EndPoint6!, h.EndPoint], 0);
            mesh.Run(3);

            Assert.Equal(alias, AliasOf(c));
        }

        // ------------------------------------------------------------------ S5: C and W through H

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void S5_CAndW_ThroughH_SeeEachOther(bool r1, bool wFirst)
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1);
            TestNode c;
            if (wFirst)
            {
                JoinOverLegacy(mesh, w, h);
                c = C(mesh, h);
            }
            else
            {
                c = C(mesh, h);
                JoinOverLegacy(mesh, w, h);
            }

            mesh.Run(20);

            NodeId alias = AliasOf(c);
            Assert.True(c.Core.Peers.TryGet(w.Id, out Peer? wAtC));
            Assert.True(wAtC.SendEstablished && wAtC.Relayed && wAtC.RouteVia == h.Id, "C reaches W through H");
            Assert.True(w.Core.Peers.TryGet(alias, out Peer? cAtW), "W knows C by its alias");
            Assert.True(cAtW.SendEstablished, "W's pulses to C are answered");
            Assert.Contains(c.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == w.Id);
            Assert.Contains(w.Events, e => e.Kind == NetworkEventKind.PeerJoined && e.Node == alias);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void S5_CAndW_ExchangePositions_AndIdentities(bool r1)
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1);
            JoinOverLegacy(mesh, w, h);
            TestNode c = C(mesh, h);
            mesh.Run(10);
            NodeId alias = AliasOf(c);

            SendPosition(c, w.Id, 1, 11);
            SendPosition(w, alias, 2, 22);
            mesh.Run(1);

            Assert.Equal(11, w.Messages<PositionUpdate>().Single(p => p.ObjectId == 1).Latitude);
            Assert.Equal(22, c.Messages<PositionUpdate>().Single(p => p.ObjectId == 2).Latitude);
            Assert.Equal("N1", w.Messages<IdentityUpdate>().Single(i => i.ObjectId == 1).Callsign);
            Assert.Equal("N2", c.Messages<IdentityUpdate>().Single(i => i.ObjectId == 2).Callsign);
            // W sees the position as coming from the alias, C sees W's from W
            Assert.Contains(w.MessagesWithMeta<PositionUpdate>(), m => m.Message.ObjectId == 1 && m.Meta.Sender == alias);
            Assert.Contains(c.MessagesWithMeta<PositionUpdate>(), m => m.Message.ObjectId == 2 && m.Meta.Sender == w.Id);
            Assert.Empty(h.Messages<PositionUpdate>()); // relayed, not consumed
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void S5_CAndW_StayMembers_ForAMinute_AndNoLegacyDatagramTouchesC(bool r1)
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1);
            JoinOverLegacy(mesh, w, h);
            TestNode c = C(mesh, h);

            mesh.Run(90);

            NodeId alias = AliasOf(c);
            Assert.True(c.Core.Peers.Contains(w.Id));
            Assert.True(w.Core.Peers.Contains(alias));
            Assert.True(h.Core.Peers.Contains(c.Id) && h.Core.Peers.Contains(w.Id));
            Assert.False(Left(c, w.Id));
            Assert.False(Left(w, alias));
            Assert.DoesNotContain(mesh.Network.Log, d => IsLegacy(d.Data) && (d.From.Equals(c.EndPoint) || d.To.Equals(c.EndPoint)));
            // nothing was sent to W from an IPv6 address
            Assert.DoesNotContain(mesh.Network.Log, d => d.To.Equals(w.EndPoint) && IsV6(d.From));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void S5_WLeaves_CIsToldAtOnce(bool r1)
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1);
            JoinOverLegacy(mesh, w, h);
            TestNode c = C(mesh, h);
            mesh.Run(5);

            w.Core.Mesh.Leave();
            mesh.Run(1);

            Assert.False(c.Core.Peers.Contains(w.Id));
            Assert.True(Left(c, w.Id));
            Assert.True(c.Core.Peers.Contains(h.Id));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void S5_CLeaves_WIsToldAtOnce(bool r1)
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1);
            JoinOverLegacy(mesh, w, h);
            TestNode c = C(mesh, h);
            mesh.Run(5);
            NodeId alias = AliasOf(c);

            c.Core.Mesh.Leave();
            mesh.Run(1);

            Assert.False(w.Core.Peers.Contains(alias));
            Assert.True(Left(w, alias));
            Assert.True(w.Core.Peers.Contains(h.Id));
        }

        [Fact]
        public void S5_ACrashedC_IsGoneAtWAndH_WithinTheExpiry()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1: false);
            JoinOverLegacy(mesh, w, h);
            TestNode c = C(mesh, h);
            mesh.Run(5);
            NodeId alias = AliasOf(c);
            mesh.Nodes.Remove(c);
            mesh.Network.Detach(c.EndPoint);

            mesh.Run(45);

            Assert.False(h.Core.Peers.Contains(c.Id));
            Assert.False(w.Core.Peers.Contains(alias));
            Assert.True(Left(w, alias));
        }

        [Fact]
        public void S5_CGuaranteedMessages_ReachW_AndWsReachC()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1: true);
            JoinOverLegacy(mesh, w, h);
            TestNode c = C(mesh, h);
            mesh.Run(10);
            NodeId alias = AliasOf(c);

            c.Core.SendTo(w.Id, new NotesBundle { Scope = CommsScope.Single, Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "c", Callsign = "C", Notes = [new CommsNote { NoteId = 1, Age = 0, Channel = 0, Text = "hello W" }] }] }, true);
            w.Core.SendTo(alias, new NotesBundle { Scope = CommsScope.Single, Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "w", Callsign = "W", Notes = [new CommsNote { NoteId = 2, Age = 0, Channel = 0, Text = "hello C" }] }] }, true);
            mesh.Run(10);

            Assert.Contains(w.Messages<NotesBundle>(), b => b.Users.Any(u => u.Notes.Any(n => n.Text == "hello W")));
            Assert.Contains(c.Messages<NotesBundle>(), b => b.Users.Any(u => u.Notes.Any(n => n.Text == "hello C")));
            Assert.Equal(0, ((Jfp2Plugin)c.Core.Plugins.Single(p => p is Jfp2Plugin)).GuaranteedPendingCount);
            Assert.Equal(0, h.Core.Plugins.OfType<LegacyPlugin>().Single().GuaranteedOutCount);
        }

        // ------------------------------------------------------------------ S13: W on H's LAN

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void S13_WOnHsLan_GuessesALanAddressForTheAlias_AndFindsCThroughH(bool r1)
        {
            var mesh = new TestMesh();
            // H and W share one public address (203.0.113.1), on a LAN
            var h = new TestNode(mesh, "192.168.1.2", "2001:db8::1", 6112, "203.0.113.1", Plugins());
            mesh.Nodes.Add(h);
            h.Core.Mesh.Create(false, 0, false, "");
            TestNode w = r1
                ? mesh.AddBehindNat("192.168.1.9", "203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(profile: new Jfp2Profile([.. Jfp2Profile.Default.Classes])))
                : mesh.AddBehindNat("192.168.1.9", "203.0.113.1", 6112, new LegacyPlugin());
            w.Core.Mesh.Join(h.EndPoint, 0);
            mesh.Run(3);
            TestNode c = mesh.AddIPv6Only("2001:db8::c", 6112, Plugins());
            c.Core.Mesh.JoinJfp2First([h.EndPoint6!], 0);
            mesh.Run(30);

            NodeId alias = AliasOf(c);
            Assert.True(w.Core.Peers.TryGet(alias, out Peer? cAtW));
            // the guess is a LAN address nobody has: W reaches C through H, which is where the traffic has to go
            Assert.True(cAtW.SendEstablished);
            SendPosition(c, w.Id, 1, 11);
            SendPosition(w, alias, 2, 22);
            mesh.Run(1);
            Assert.Equal(11, w.Messages<PositionUpdate>().Single(p => p.ObjectId == 1).Latitude);
            Assert.Equal(22, c.Messages<PositionUpdate>().Single(p => p.ObjectId == 2).Latitude);
            mesh.Run(60);
            Assert.True(w.Core.Peers.Contains(alias));
            Assert.True(c.Core.Peers.Contains(w.Id));
        }

        // ------------------------------------------------------------------ S7: C and B have no family in common

        [Fact]
        public void S7_CAndB_NoFamilyInCommon_HRelays()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode b = mesh.Add("198.51.100.3", 6112, Plugins());
            Assert.True(b.Core.Mesh.JoinOverJfp2(h.EndPoint, 0));
            mesh.Run(3);
            TestNode c = C(mesh, h);

            mesh.Run(30);

            Assert.True(c.Core.Peers.TryGet(b.Id, out Peer? bAtC) && bAtC.SendEstablished && bAtC.Relayed && bAtC.RouteVia == h.Id);
            Assert.True(b.Core.Peers.TryGet(c.Id, out Peer? cAtB) && cAtB.SendEstablished && cAtB.Relayed && cAtB.RouteVia == h.Id);
            SendPosition(c, b.Id, 1, 11);
            SendPosition(b, c.Id, 2, 22);
            mesh.Run(1);
            Assert.Equal(11, b.Messages<PositionUpdate>().Single(p => p.ObjectId == 1).Latitude);
            Assert.Equal(22, c.Messages<PositionUpdate>().Single(p => p.ObjectId == 2).Latitude);
            Assert.True(h.Core.Mesh.RelayCount >= 1);
            // nothing between them that is legacy, and C never sends B an IPv4 datagram
            Assert.DoesNotContain(mesh.Network.Log, d => IsLegacy(d.Data) && (d.From.Equals(c.EndPoint) || d.To.Equals(c.EndPoint)));
            // a minute on, still members of each other's
            mesh.Run(60);
            Assert.True(b.Core.Peers.Contains(c.Id) && c.Core.Peers.Contains(b.Id));
        }

        [Fact]
        public void S7_CAndB_WhenHIsOutOfRelayBudget_TheyDoNotSeeEachOther_ButStayInTheSession()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            h.Core.Mesh.RelayBudget = 0;
            TestNode b = mesh.Add("198.51.100.3", 6112, Plugins());
            Assert.True(b.Core.Mesh.JoinOverJfp2(h.EndPoint, 0));
            mesh.Run(3);
            TestNode c = C(mesh, h);

            mesh.Run(60);

            Assert.True(h.Core.Peers.Contains(c.Id) && h.Core.Peers.Contains(b.Id));
            Assert.True(c.Core.Mesh.Connected && b.Core.Mesh.Connected);
        }

        // ------------------------------------------------------------------ S6 for C: another node with IPv6 is reached directly

        [Fact]
        public void S6_CAndA_BothWithIPv6_AreDirectOverIPv6_AndHRelaysNothing()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = mesh.AddDualStack("198.51.100.2", "2001:db8::2", 6112, Plugins());
            mesh.Firewall(a);
            Assert.True(a.Core.Mesh.JoinOverJfp2(h.EndPoint, 0));
            mesh.Run(3);
            TestNode c = C(mesh, h, seconds: 15);
            mesh.Firewall(c);

            mesh.Run(15);

            Assert.True(c.Core.Peers.TryGet(a.Id, out Peer? aAtC) && aAtC.SendEstablished && !aAtC.Relayed && IsV6(aAtC.RouteEndPoint));
            Assert.True(a.Core.Peers.TryGet(c.Id, out Peer? cAtA) && cAtA.SendEstablished && !cAtA.Relayed && IsV6(cAtA.RouteEndPoint));
            Assert.Equal(0, h.Core.Mesh.RelayCount);
        }

        // ------------------------------------------------------------------ S14: the admitting node leaves

        [Fact]
        public void S14_TheAdmittingNodeLeaves_CStaysAMemberThroughItsOtherLinks()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode a = mesh.AddDualStack("198.51.100.2", "2001:db8::2", 6112, Plugins());
            Assert.True(a.Core.Mesh.JoinOverJfp2([h.EndPoint6!, h.EndPoint], 0));
            mesh.Run(3);
            // C joins through A, not through H
            TestNode c = mesh.AddIPv6Only("2001:db8::c", 6112, Plugins());
            Assert.True(c.Core.Mesh.JoinOverJfp2([a.EndPoint6!], 0));
            mesh.Run(15);
            Assert.True(c.Core.Peers.Contains(h.Id), "C learned of H through A");
            Assert.True(h.Core.Peers.Contains(c.Id), "H learned of C through A");
            NodeId alias = AliasOf(c);
            Assert.Equal(a.Core.Identity.LegacyId.ip, alias.TryGetLegacy(out LegacyNodeId legacy) ? legacy.ip : 0u);

            a.Core.Mesh.Leave();
            mesh.Run(90);

            Assert.True(c.Core.Mesh.Connected);
            Assert.True(h.Core.Peers.TryGet(c.Id, out Peer? cAtH) && cAtH.SendEstablished && !cAtH.Relayed && IsV6(cAtH.RouteEndPoint));
            Assert.True(c.Core.Peers.TryGet(h.Id, out Peer? hAtC) && hAtC.SendEstablished && !hAtC.Relayed && IsV6(hAtC.RouteEndPoint));
            Assert.False(c.Core.Peers.Contains(a.Id));
            SendPosition(c, h.Id, 1, 11);
            mesh.Run(1);
            Assert.Equal(11, h.Messages<PositionUpdate>().Single(p => p.ObjectId == 1).Latitude);
        }

        // ------------------------------------------------------------------ the wire: JoinResult version 2

        [Fact]
        public void JoinResultVersion2_CarriesTheAlias_AndVersion1IsUnchanged()
        {
            var alias = NodeId.FromLegacy(new LegacyNodeId(IPAddress.Parse("203.0.113.1"), 6112, 20));
            var body = new JoinResultBody { Result = JoinResult.Accepted, Suid = 0x01020304, ListSegments = 1, Alias = alias };

            // A.7 of protocol.md is version 1, and stays; A.15 is version 2
            Assert.Equal("000403020101", Convert.ToHexString(body.Serialize(1)).ToLowerInvariant());
            Assert.Equal("00040302010100017100cbe01714", Convert.ToHexString(body.Serialize(2)).ToLowerInvariant());

            Assert.True(JoinResultBody.TryRead(body.Serialize(2), out JoinResultBody read, 2));
            Assert.Equal(alias, read.Alias);
            Assert.Equal(0x01020304u, read.Suid);
            Assert.True(JoinResultBody.TryRead(body.Serialize(1), out read, 1));
            Assert.Equal(None, read.Alias);
            Assert.False(JoinResultBody.TryRead(body.Serialize(1), out _, 2), "a version 1 body is too short for version 2");
            // a version 1 reader takes a longer body as it always did
            Assert.True(JoinResultBody.TryRead(body.Serialize(2), out read, 1));
            Assert.Equal(None, read.Alias);
        }

        [Fact]
        public void AJoinerThatOffersJoinResultVersion1Only_IsAdmitted_AndSeenByW_WithoutKnowingItsAlias()
        {
            var mesh = new TestMesh();
            TestNode h = Hub(mesh);
            TestNode w = W(mesh, "198.51.100.9", r1: false);
            JoinOverLegacy(mesh, w, h);
            Jfp2Profile older = Jfp2Profile.Default.WithoutInternal(MessageClasses.JoinResult)
                .WithInternalOffers([new SchemaOffer(true, MessageClasses.JoinResult, 1, 1)]);
            TestNode c = mesh.AddIPv6Only("2001:db8::c", 6112, new LegacyPlugin(), new Jfp2Plugin(profile: older));
            c.Core.Mesh.JoinJfp2First([h.EndPoint6!], 0);

            mesh.Run(20);

            Assert.True(c.Core.Mesh.Connected);
            Assert.Equal(None, c.Core.Identity.Alias);
            h.Core.Peers.TryGetLegacy(c.Id, out LegacyNodeId legacy);
            Assert.True(w.Core.Peers.TryGet(NodeId.FromLegacy(legacy), out Peer? cAtW) && cAtW.SendEstablished);
        }
    }
}
