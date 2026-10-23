using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>
    /// The directory parts of the session with hubs that have an IPv6 address (plan stage 9, scenario S9): C, a node with no IPv4 address, sees every hub
    /// it can reach, shown with its users and status, and is told why the others are not there; A, with both families, polls a hub on IPv6.
    /// </summary>
    public class DirectoryJfp2Tests
    {
        static readonly IPEndPoint HubV6 = new(IPAddress.Parse("2001:db8:1::10"), 6112);
        static readonly IPEndPoint HubV4 = new(IPAddress.Parse("203.0.113.10"), 6112);

        static StatusUpdate Status(string name) => new()
        {
            Guid = Guid.NewGuid(), AppVersion = "26.6", HubEnabled = true, Address = "", Name = name, About = "", Voip = "",
            NextEvent = "", Airport = "", AtcAirport = "",
        };

        /// <summary>The rig of C: a node whose only address is an IPv6 one, known by the name of its key.</summary>
        static SessionRig RigOfC()
        {
            var rig = new SessionRig();
            NodeId key = NodeKey.Generate().Id;
            rig.Session.LocalId = key;
            rig.Session.Snapshot = new NetworkSnapshot
            {
                Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [], NoIPv4 = true, LocalId = key,
                Ipv6Addresses = [IPAddress.Parse("2001:db8:2::2")],
            };
            return rig;
        }

        static MessageMeta FromHub(NodeId name, IPEndPoint at) => new() { Sender = name, EndPoint = at };

        [Fact]
        public void C_AHubListNamingAnIPv6Hub_AsksItThereAndAddsItWithItsEndpoints()
        {
            SessionRig rig = RigOfC();
            NodeId hubName = NodeKey.Generate().Id;

            rig.Hubs.Handle(new MessageMeta(), new HubList { Hubs = [new HubAddress { Node = hubName, Port = 6112, Candidates = [HubV6] }] });

            Assert.Equal(HubV6, Assert.Single(rig.Outbox.Asked, a => a.Message is StatusRequestUpdate).EndPoint);

            rig.Hubs.Handle(FromHub(hubName, HubV6), Status("Hub six"));
            HubDirectory.Hub hub = Assert.Single(rig.Hubs.List);
            Assert.Equal(HubV6, hub.endPoint);
            Assert.Equal([HubV6], hub.candidates);
            Assert.True(hub.online);
            Assert.True(rig.Hubs.Reachable(hub));
            Assert.Equal([HubV6], rig.EndPoints.Candidates.Keys.Where(k => k.Equals(HubV6)));
        }

        [Fact]
        public void C_AHubWithOnlyAnIPv4Address_IsListedAsUnreachable_NotHidden()
        {
            SessionRig rig = RigOfC();
            NodeId legacyHub = NodeId.FromLegacy(0xCB00710A, 6112, 1);

            rig.Hubs.Handle(new MessageMeta(), new HubList { Hubs = [new HubAddress { Node = legacyHub, Port = 6112 }] });

            HubDirectory.Hub hub = Assert.Single(rig.Hubs.List);
            Assert.False(hub.online);
            Assert.False(rig.Hubs.Reachable(hub));
            Assert.Empty(rig.Outbox.Asked);   // nothing to send: C has no IPv4 route
            Assert.Contains(rig.Log.Events, e => e.Contains("unreachable"));
        }

        [Fact]
        public void ASeedHostWithBothRecords_IsAskedOnIPv6ByANodeThatHasIt_AndOnIPv4ByOneThatDoesNot()
        {
            var both = new SessionRig();
            both.Session.Snapshot = new NetworkSnapshot { Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [], Ipv6Addresses = [IPAddress.Parse("2001:db8:2::2")] };
            both.Hubs.SubmitSeed(HubV4, [HubV4, HubV6]);
            Assert.Equal(HubV6, Assert.Single(both.Outbox.Asked).EndPoint);

            var v4Only = new SessionRig();
            v4Only.Hubs.SubmitSeed(HubV4, [HubV4, HubV6]);
            Assert.Equal(HubV4, Assert.Single(v4Only.Outbox.Asked).EndPoint);
        }

        [Fact]
        public void C_AnIPv4OnlySeed_IsListedAsUnreachable_AndAnIPv6SeedIsAsked()
        {
            SessionRig rig = RigOfC();

            rig.Hubs.SubmitSeed(HubV4, [HubV4]);
            rig.Hubs.SubmitSeed(HubV6, [HubV6]);

            HubDirectory.Hub unreachable = Assert.Single(rig.Hubs.List);
            Assert.Equal(HubV4, unreachable.endPoint);
            Assert.False(rig.Hubs.Reachable(unreachable));
            Assert.Equal(HubV6, Assert.Single(rig.Outbox.Asked).EndPoint);
        }

        [Fact]
        public void A_AHubOnBothFamilies_IsPolledOnIPv6_AndFallsBackWhenItIsSilent()
        {
            var rig = new SessionRig();
            rig.Session.Snapshot = new NetworkSnapshot { Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [], Ipv6Addresses = [IPAddress.Parse("2001:db8:2::2")] };
            NodeId hubName = NodeId.FromLegacy(0xCB00710A, 6112, 1);

            rig.Hubs.Handle(new MessageMeta(), new HubList { Hubs = [new HubAddress { Node = hubName, Port = 6112, Candidates = [HubV6, HubV4] }] });
            Assert.Equal(HubV6, rig.Outbox.Asked.Single(a => a.Message is StatusRequestUpdate).EndPoint);   // IPv6 first

            rig.Hubs.Handle(FromHub(hubName, HubV6), Status("Both"));
            HubDirectory.Hub hub = Assert.Single(rig.Hubs.List);
            Assert.Equal(HubV6, hub.endPoint);
            Assert.Equal([HubV6, HubV4], hub.candidates);
            Assert.Equal(HubV6, rig.Hubs.PollEndPoint(hub));

            // silent for too long: the hub is offline and is asked at each endpoint in turn
            hub.online = false;
            Assert.Equal([HubV6, HubV4], new[] { rig.Hubs.PollEndPoint(hub), rig.Hubs.PollEndPoint(hub) });
        }

        [Fact]
        public void A_HubKnownOnIPv4_IsMovedToIPv6_WhenItAnswersThereToo()
        {
            var rig = new SessionRig();
            rig.Session.Snapshot = new NetworkSnapshot { Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [], Ipv6Addresses = [IPAddress.Parse("2001:db8:2::2")] };
            NodeId hubName = NodeId.FromLegacy(0xCB00710A, 6112, 1);
            rig.Hubs.Handle(FromHub(hubName, HubV4), Status("Both"));
            HubDirectory.Hub hub = Assert.Single(rig.Hubs.List);
            Assert.Equal(HubV4, hub.endPoint);

            // a hub list of another hub names its IPv6 address, and now and then the hub is asked there
            rig.Hubs.Handle(new MessageMeta(), new HubList { Hubs = [new HubAddress { Node = hubName, Port = 6112, Candidates = [HubV6] }] });
            Assert.Contains(HubV6, hub.candidates);
            var polled = Enumerable.Range(0, 8).Select(_ => rig.Hubs.PollEndPoint(hub)).ToList();
            Assert.Contains(HubV6, polled);
            Assert.Contains(HubV4, polled);

            rig.Hubs.Handle(FromHub(hubName, HubV6), Status("Both"));

            Assert.Equal(HubV6, hub.endPoint);
        }

        [Fact]
        public void TheHubList_ASendsToAnotherNode_HasTheCandidatesAndIsSplit()
        {
            var rig = new SessionRig();
            for (int n = 1; n <= 20; n++)
            {
                NodeId name = NodeId.FromLegacy(0xCB007100u + (uint)n, 6112, (byte)n);
                rig.Hubs.Handle(FromHub(name, new IPEndPoint(new IPAddress(new byte[] { 203, 0, 113, (byte)n }), 6112)), Status("Hub" + n));
            }
            rig.Hubs.Handle(FromHub(NodeKey.Generate().Id, HubV6), Status("Six"));
            rig.Hubs.List[^1].candidates = [HubV6];

            rig.Hubs.SendHubList(new IPEndPoint(IPAddress.Parse("198.51.100.9"), 6112));

            List<HubList> lists = rig.Outbox.Messages<HubList>().ToList();
            Assert.Equal(3, lists.Count);   // 21 hubs, 8 to a message
            Assert.All(lists, l => Assert.InRange(l.Hubs.Count, 1, 8));
            HubAddress six = lists.SelectMany(l => l.Hubs).Single(h => h.Candidates.Contains(HubV6));
            Assert.Equal(NodeId.KeyPairKind, six.Node.Kind);
        }

        [Fact]
        public void HubsDat_KeepsTheCandidatesAndAHubWithNoIPv4Address()
        {
            SessionRig first = RigOfC();
            NodeId sixName = NodeKey.Generate().Id;
            first.Hubs.Handle(FromHub(sixName, HubV6), Status("Six"));
            first.Hubs.Handle(new MessageMeta(), new HubList { Hubs = [new HubAddress { Node = NodeId.FromLegacy(0xCB00710A, 6112, 1), Port = 6112 }] });
            first.Clock.Advance(10);
            first.Hubs.DoWork();   // saves what changed

            var second = new SessionRig();
            second.Profile.StoragePath = first.Profile.StoragePath;
            second.Hubs.Load();

            Assert.Equal(2, second.Hubs.List.Count);
            HubDirectory.Hub six = second.Hubs.List.Single(h => h.name == "Six");
            Assert.Equal(sixName, six.nuid);
            Assert.Equal(HubV6, six.endPoint);
            Assert.Equal([HubV6], six.candidates);
            Assert.Contains(second.Hubs.List, h => h.nuid.TryGetLegacy(out _) && h.name == "");
        }

        [Fact]
        public void AnOnlineUserWithNoIPv4Address_IsFoundByItsCandidate()
        {
            SessionRig rig = RigOfC();
            NodeId userNode = NodeKey.Generate().Id;
            IPEndPoint userAt = new(IPAddress.Parse("2001:db8:3::3"), 6112);

            rig.Users.Handle(new MessageMeta { Sender = userNode, EndPoint = userAt }, new OnlineAnnouncement { Uuid = 77 });
            Assert.True(rig.Users.TryGetEndPoint(77, out IPEndPoint found));
            Assert.Equal(userAt, found);

            // asked, the hub answers with the name, the port and where it saw the user
            rig.Users.Handle(new MessageMeta { Sender = NodeKey.Generate().Id, EndPoint = HubV6 }, new UserNuidRequest { Uuid = 77 });
            UserNuidReply reply = rig.Outbox.Messages<UserNuidReply>().Single();
            Assert.Equal(userNode, reply.Node);
            Assert.Equal([userAt], reply.Candidates);

            // a node asking the hub for it learns the same
            SessionRig other = RigOfC();
            other.Users.Handle(FromHub(NodeKey.Generate().Id, HubV6), reply);
            Assert.True(other.Users.TryGetEndPoint(77, out IPEndPoint again));
            Assert.Equal(userAt, again);
        }

        [Fact]
        public void AnOnlineUserOnIPv4_IsFoundByItsLegacyId_AndDialledOnIPv6FirstWhenItHasBoth()
        {
            var rig = new SessionRig();
            rig.Session.Snapshot = new NetworkSnapshot { Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [], Ipv6Addresses = [IPAddress.Parse("2001:db8:2::2")] };
            NodeId user = NodeId.FromLegacy(0xCB000050, 6112, 5);
            IPEndPoint userV6 = new(IPAddress.Parse("2001:db8:3::3"), 6112);

            rig.Users.RegisterOnlineUser(88, user, 6112, [userV6]);

            Assert.True(rig.Users.TryGetEndPoint(88, out IPEndPoint found));
            Assert.Equal(userV6, found);
            IPEndPoint v4 = rig.EndPoints.MakeEndPoint(user.TryGetLegacy(out LegacyNodeId legacy) ? legacy : default, 6112);
            Assert.Equal([userV6, v4], rig.EndPoints.Candidates[found]);
        }
    }
}
