using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The directory over JFP2 (docs/network/jfp2/protocol.md §5.16, plan stage 9, scenario S9): a node that is not a member asks a hub for its
    /// status, hub list, users and lookups over a non-member session, and legacy is asked after 2 s of silence. The hubs in the first tests register
    /// only <see cref="Jfp2Plugin"/>, so a legacy datagram between them is impossible.
    /// </summary>
    public class Jfp2DirectoryTests
    {
        static TestNode Jfp2Node(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new Jfp2Plugin());

        static TestNode Hub(TestMesh mesh, string ip = "203.0.113.1")
        {
            TestNode hub = Jfp2Node(mesh, ip);
            hub.Core.Mesh.Create(false, 0, false, "");
            return hub;
        }

        static StatusUpdate SomeStatus(string name = "Hub") => new()
        {
            Guid = new Guid("11111111-2222-3333-4444-555555555555"), AppVersion = "26.6.0", HubEnabled = true, Name = name, Address = "hub.example",
            About = "", Voip = "", NextEvent = "", Airport = "", AtcAirport = "", Users = 3,
        };

        /// <summary>What the hub's app does with a StatusRequest: answer it where it came from.</summary>
        static void AnswerStatusRequests(TestNode hub)
        {
            foreach ((MessageMeta meta, StatusRequestUpdate _) in hub.MessagesWithMeta<StatusRequestUpdate>().ToList())
            {
                hub.Core.SendToEndPoint(meta.EndPoint, SomeStatus(), guaranteed: false);
            }
            hub.Received.RemoveAll(r => r.Message is StatusRequestUpdate);
        }

        [Fact]
        public void AStranger_AsksAHubForItsStatus_OverJfp2_AndIsAnswered()
        {
            var mesh = new TestMesh();
            TestNode hub = Hub(mesh);
            TestNode a = Jfp2Node(mesh, "198.51.100.2");

            a.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate { HubEnabled = false, Uuid = 7 }, guaranteed: false);
            mesh.Run(1);
            (MessageMeta meta, StatusRequestUpdate request) = Assert.Single(hub.MessagesWithMeta<StatusRequestUpdate>());
            Assert.Equal(7u, request.Uuid);
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(a.EndPoint, meta.EndPoint);

            AnswerStatusRequests(hub);
            mesh.Run(1);

            (MessageMeta reply, StatusUpdate status) = Assert.Single(a.MessagesWithMeta<StatusUpdate>());
            Assert.Equal("Hub", status.Name);
            Assert.Equal(3, status.Users);
            Assert.Equal(hub.Id, reply.Sender);
            Assert.Equal(hub.EndPoint, reply.EndPoint);
            Assert.Empty(hub.Core.Peers.All);   // a stranger asking is not a member
            Assert.All(mesh.Network.Log, d => Assert.Equal(Envelope.Magic, d.Data[0]));
        }

        [Fact]
        public void TheSecondRequest_UsesTheOpenLink_WithoutAnotherConversation()
        {
            var mesh = new TestMesh();
            TestNode hub = Hub(mesh);
            TestNode a = Jfp2Node(mesh, "198.51.100.2");

            a.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(1);
            int hellos = mesh.Network.Log.Count(d => d.From.Equals(a.EndPoint) && d.Data[7] == MessageClasses.Hello && (d.Data[2] & 0x08) != 0);

            a.Core.Mesh.Ask(hub.EndPoint, new UserListRequest(), guaranteed: false);
            mesh.Run(1);

            Assert.Equal(2, hub.Messages<StatusRequestUpdate>().Count() + hub.Messages<UserListRequest>().Count());
            Assert.Equal(hellos, mesh.Network.Log.Count(d => d.From.Equals(a.EndPoint) && d.Data[7] == MessageClasses.Hello && (d.Data[2] & 0x08) != 0));
        }

        [Fact]
        public void ALegacyHub_IsAskedOverLegacyAfterTwoSeconds_AndRemembered()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            TestNode a = mesh.Add("198.51.100.2", 6112, new Jfp2Plugin(), new LegacyPlugin());

            a.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(1.5);
            Assert.Empty(hub.Messages<StatusRequestUpdate>());

            mesh.Run(1);
            Assert.Single(hub.Messages<StatusRequestUpdate>());
            Assert.Contains(a.Events, e => e.Kind == NetworkEventKind.LegacyOnly && e.EndPoint!.Equals(hub.EndPoint) && e.Seconds > 0);

            // remembered: the next request goes over legacy at once, and no JFP2 Hello follows it
            int hellos = mesh.Network.Log.Count(d => d.From.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic);
            a.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(0.5);
            Assert.Equal(2, hub.Messages<StatusRequestUpdate>().Count());
            Assert.Equal(hellos, mesh.Network.Log.Count(d => d.From.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic));
        }

        [Fact]
        public void AnAddressRestoredFromAnEarlierRun_IsAskedOverLegacyAtOnce()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            TestNode a = mesh.Add("198.51.100.2", 6112, new Jfp2Plugin(), new LegacyPlugin());
            a.Core.Mesh.RestoreLegacyOnly(hub.EndPoint, 1800);

            a.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(0.5);

            Assert.Single(hub.Messages<StatusRequestUpdate>());
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] == Envelope.Magic);
        }

        // ------------------------------------------------------------------ S9: C, a node with no IPv4 address

        [Fact]
        public void C_AsksADualStackHub_OverIPv6_ForStatusHubsUsersAndLookups()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.AddDualStack("203.0.113.1", "2001:db8:1::1", 6112, new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            TestNode c = mesh.AddIPv6Only("2001:db8:2::2", 6112, new Jfp2Plugin());
            IPEndPoint userAt = new(IPAddress.Parse("2001:db8:3::3"), 6112);

            c.Core.Mesh.Ask(hub.EndPoint6!, new StatusRequestUpdate { HubListRequested = true, Uuid = 9 }, guaranteed: false);
            c.Core.Mesh.Ask(hub.EndPoint6!, new UserListRequest(), guaranteed: false);
            c.Core.Mesh.Ask(hub.EndPoint6!, new UserPositionsRequest(), guaranteed: false);
            c.Core.Mesh.Ask(hub.EndPoint6!, new OnlineAnnouncement { Uuid = 9 }, guaranteed: false);
            c.Core.Mesh.Ask(hub.EndPoint6!, new UserNuidRequest { Uuid = 5 }, guaranteed: true);
            mesh.Run(1);

            (MessageMeta asked, StatusRequestUpdate _) = Assert.Single(hub.MessagesWithMeta<StatusRequestUpdate>());
            Assert.Equal(c.Id, asked.Sender);
            Assert.Equal(c.EndPoint6, asked.EndPoint);
            Assert.Single(hub.Messages<UserListRequest>());
            Assert.Single(hub.Messages<UserPositionsRequest>());
            Assert.Equal(9u, Assert.Single(hub.Messages<OnlineAnnouncement>()).Uuid);
            Assert.Equal(5u, Assert.Single(hub.Messages<UserNuidRequest>()).Uuid);

            // the hub's app answers where each request came from
            IPEndPoint to = asked.EndPoint;
            hub.Core.SendToEndPoint(to, SomeStatus(), guaranteed: false);
            hub.Core.SendToEndPoint(to, new HubList { Hubs = [new HubAddress { Node = hub.Id, Port = 6112, Candidates = [hub.EndPoint6!, hub.EndPoint] }] }, guaranteed: false);
            hub.Core.SendToEndPoint(to, new HubUserUpdate { Guid = Guid.NewGuid(), Nickname = "Pilot", Callsign = "N123", Latitude = 51.5f, Longitude = -0.1f, Route = "DCT", IcaoType = "C172" }, guaranteed: false);
            hub.Core.SendToEndPoint(to, new UserPositions { Users = [new UserPosition { Guid = Guid.NewGuid(), Latitude = 1, Longitude = 2, Altitude = 3, Speed = 4, Squawk = 1200, Heading = 90 }] }, guaranteed: false);
            hub.Core.SendToEndPoint(to, new UserNuidReply { Uuid = 5, Node = NodeKey.Generate().Id, Port = 6112, Candidates = [userAt] }, guaranteed: true);
            mesh.Run(1);

            Assert.Single(c.Messages<StatusUpdate>());
            HubList list = Assert.Single(c.Messages<HubList>());
            Assert.Equal([hub.EndPoint6!, hub.EndPoint], list.Hubs[0].Candidates);
            Assert.Equal("Pilot", Assert.Single(c.Messages<HubUserUpdate>()).Nickname);
            Assert.Equal(1200, Assert.Single(c.Messages<UserPositions>()).Users[0].Squawk);
            Assert.Equal([userAt], Assert.Single(c.Messages<UserNuidReply>()).Candidates);
            Assert.All(mesh.Network.Log, d => Assert.Equal(Envelope.Magic, d.Data[0]));   // no legacy datagram anywhere
        }

        [Fact]
        public void C_OpensTheHubList_AndJoinsTheHubItNames()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.AddDualStack("203.0.113.1", "2001:db8:1::1", 6112, new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            TestNode c = mesh.AddIPv6Only("2001:db8:2::2", 6112, new Jfp2Plugin());

            c.Core.Mesh.Ask(hub.EndPoint6!, new StatusRequestUpdate { HubListRequested = true }, guaranteed: false);
            mesh.Run(1);
            hub.Core.SendToEndPoint(c.EndPoint6!, new HubList { Hubs = [new HubAddress { Node = hub.Id, Port = 6112, Candidates = [hub.EndPoint6!, hub.EndPoint] }] }, guaranteed: false);
            mesh.Run(1);
            HubAddress listed = Assert.Single(Assert.Single(c.Messages<HubList>()).Hubs);

            // the hub list names IPv6 and IPv4 endpoints; C has no IPv4 and joins on the IPv6 one, with the conversation it already has with the hub
            Assert.True(c.Core.Mesh.JoinOverJfp2(listed.Candidates!, 0));
            mesh.Run(5);

            Assert.True(c.Knows(hub));
            Assert.True(hub.Knows(c));
            Assert.True(c.Core.Peers.TryGet(hub.Id, out Peer hubPeer));
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetworkV6, hubPeer.RouteEndPoint.AddressFamily);
            Assert.All(mesh.Network.Log, d => Assert.Equal(Envelope.Magic, d.Data[0]));
        }

        [Fact]
        public void AnIPv6Request_ThatIsNotAnswered_GoesNowhereOverLegacy()
        {
            var mesh = new TestMesh();
            TestNode legacyHub = mesh.AddDualStack("203.0.113.1", "2001:db8:1::1", 6112, new LegacyPlugin());
            legacyHub.Core.Mesh.Create(false, 0, false, "");
            TestNode a = mesh.AddDualStack("198.51.100.2", "2001:db8:2::2", 6112, new Jfp2Plugin(), new LegacyPlugin());

            a.Core.Mesh.Ask(legacyHub.EndPoint6!, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(10);

            Assert.Empty(legacyHub.Messages<StatusRequestUpdate>());
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] == 0x0B);   // legacy never touches an IPv6 address
            Assert.DoesNotContain(a.Events, e => e.Kind == NetworkEventKind.LegacyOnly);
        }

        [Fact]
        public void ANodeWithNoIPv4Address_AsksNothingAtAnIPv4Hub()
        {
            var mesh = new TestMesh();
            TestNode hub = Hub(mesh);
            TestNode c = mesh.AddIPv6Only("2001:db8:2::2", 6112, new Jfp2Plugin(), new LegacyPlugin());

            c.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(5);

            Assert.Empty(mesh.Network.Log);
        }

        // ------------------------------------------------------------------ the hub list on both wires

        [Fact]
        public void AHubList_GoingToALegacyNode_NamesOnlyTheHubsWithALegacyId_AndTheOtherWireNamesThemAll()
        {
            var mesh = new TestMesh();
            TestNode h = mesh.Add("203.0.113.1", 6112, new Jfp2Plugin(), new LegacyPlugin());
            h.Core.Mesh.Create(false, 0, false, "");
            TestNode o = mesh.Add("203.0.113.9", 6112, new LegacyPlugin());
            o.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            NodeId legacyHub = NodeId.FromLegacy(0xCB00710A, 6112, 1);
            NodeId sixHub = NodeKey.Generate().Id;
            IPEndPoint six = new(IPAddress.Parse("2001:db8:5::5"), 6112);
            var list = new HubList { Hubs = [new HubAddress { Node = legacyHub, Port = 6112 }, new HubAddress { Node = sixHub, Port = 6112, Candidates = [six] }] };

            h.Core.SendToEndPoint(o.EndPoint, list, guaranteed: false);   // O is a released hub: the legacy wire
            a.Core.Mesh.Ask(h.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            mesh.Run(1);
            h.Core.SendToEndPoint(a.EndPoint, list, guaranteed: false);   // A has a conversation with H: the JFP2 wire
            mesh.Run(1);

            HubAddress onlyOne = Assert.Single(Assert.Single(o.Messages<HubList>()).Hubs);
            Assert.Equal(legacyHub, onlyOne.Node);
            Assert.Equal(6112, onlyOne.Port);
            Assert.Null(onlyOne.Candidates);
            HubList all = Assert.Single(a.Messages<HubList>());
            Assert.Equal([legacyHub, sixHub], all.Hubs.Select(x => x.Node));
            Assert.Equal([six], all.Hubs[1].Candidates);
        }

        [Fact]
        public void AMemberHub_IsAskedThroughTheMembersLink_NotAStrangersConversation()
        {
            var mesh = new TestMesh();
            TestNode hub = Hub(mesh);
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            Assert.True(a.Core.Mesh.JoinOverJfp2(hub.EndPoint, 0));
            mesh.Run(5);
            Assert.True(a.Knows(hub));

            a.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            a.Core.Mesh.Ask(hub.EndPoint, new UserListRequest(), guaranteed: false);
            mesh.Run(1);

            Assert.Single(hub.Messages<StatusRequestUpdate>());
            Assert.Single(hub.Messages<UserListRequest>());
            Assert.Equal(0, Jfp2Of(a).NonMemberCount);
            Assert.Equal(0, Jfp2Of(a).IntroductionCount);
        }

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        // ------------------------------------------------------------------ the ban list

        [Fact]
        public void ABannedIPv6Address_BansItsWholeNetwork_AndIPv4StaysAsItWas()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.AddDualStack("203.0.113.1", "2001:db8:1::1", 6112, new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            hub.Core.BanIP("2001:db8:2::99/64");
            hub.Core.BanIP("198.51.100.77");
            TestNode banned = mesh.AddIPv6Only("2001:db8:2::2", 6112, new Jfp2Plugin());
            TestNode other = mesh.AddIPv6Only("2001:db8:7::2", 6112, new Jfp2Plugin());
            TestNode bannedV4 = Jfp2Node(mesh, "198.51.100.77");
            TestNode fine = Jfp2Node(mesh, "198.51.100.78");

            foreach (TestNode node in new[] { banned, other })
            {
                node.Core.Mesh.Ask(hub.EndPoint6!, new StatusRequestUpdate(), guaranteed: false);
            }
            foreach (TestNode node in new[] { bannedV4, fine })
            {
                node.Core.Mesh.Ask(hub.EndPoint, new StatusRequestUpdate(), guaranteed: false);
            }
            mesh.Run(1);

            Assert.Equivalent(new[] { other.Id, fine.Id }, hub.MessagesWithMeta<StatusRequestUpdate>().Select(m => m.Meta.Sender));
        }
    }
}
