using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>
    /// The directory parts of the session: HubDirectory (hubs we know), UserDirectory (users by
    /// uuid, address book) and HubHost (this node's status and, as a hub, its users).
    /// </summary>
    public class DirectoryTests
    {
        static NodeId Node(uint last, ushort port = 6112) => new(0x0A000000 + last, port, (byte)last);

        static StatusUpdate Status(string name, bool hub = true, bool global = false) => new()
        {
            Guid = Guid.NewGuid(), AppVersion = "26.6", HubEnabled = hub, Address = "", Name = name, About = "", Voip = "",
            NextEvent = "", Airport = "", AtcAirport = "", GlobalSession = global,
        };

        static void AddHub(SessionRig rig, NodeId hub, string name, bool global = false) =>
            rig.Hubs.Handle(SessionRig.From(hub), Status(name, global: global));

        // ------------------------------------------------------------------ HubDirectory

        [Fact]
        public void SubmitHub_AsksForStatus_AtMostFourPendingPerAddress()
        {
            var rig = new SessionRig();
            for (ushort port = 1; port <= 6; port++)
            {
                rig.Hubs.SubmitHub(new IPEndPoint(IPAddress.Parse("10.0.0.9"), port));
            }
            rig.Hubs.SubmitHub(new IPEndPoint(IPAddress.Parse("10.0.0.9"), 1));

            var requests = rig.Outbox.Of<StatusRequestUpdate>().ToList();
            Assert.Equal(HubDirectory.MAX_IP_HUBS, requests.Count);
            Assert.All(requests, r => Assert.True(((StatusRequestUpdate)r.Message).HubListRequested));
        }

        [Fact]
        public void Status_AddsHubs_AndDropsNodesThatStopBeingHubs()
        {
            var rig = new SessionRig();
            AddHub(rig, Node(1), "One");
            AddHub(rig, Node(2), "Two");
            Assert.Equal(new[] { "One", "Two" }, rig.Hubs.List.Select(h => h.name));
            Assert.True(rig.Hubs.List[0].online);

            rig.Hubs.Handle(SessionRig.From(Node(1)), Status("One", hub: false));
            Assert.Equal("Two", Assert.Single(rig.Hubs.List).name);

            // never this node itself
            AddHub(rig, rig.Session.LocalId, "Me");
            Assert.Single(rig.Hubs.List);
        }

        [Fact]
        public void Status_KeepsAtMostFourHubsPerAddress()
        {
            var rig = new SessionRig();
            for (ushort port = 1; port <= 6; port++)
            {
                AddHub(rig, Node(1, port), "Hub" + port);
            }
            Assert.Equal(HubDirectory.MAX_IP_HUBS, rig.Hubs.List.Count);
        }

        [Fact]
        public void GlobalHub_IsReported_WhenFirstSeenWhileInTheGlobalSession()
        {
            var rig = new SessionRig();
            var found = new List<IPEndPoint>();
            rig.Hubs.GlobalHubFound += found.Add;

            AddHub(rig, Node(1), "Global", global: true);
            Assert.Empty(found);

            rig.Session.Snapshot = new NetworkSnapshot { GlobalSession = true, Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [] };
            AddHub(rig, Node(2), "Global2", global: true);
            AddHub(rig, Node(2), "Global2", global: true);
            Assert.Equal(Node(2).ToEndPoint(Node(2).port), Assert.Single(found));
        }

        [Fact]
        public void HubUsers_ComeOnlyFromKnownHubs_AndNeverIncludeUs()
        {
            var rig = new SessionRig();
            AddHub(rig, Node(1), "One");
            var user = new HubUserUpdate { Guid = Guid.NewGuid(), Nickname = "Ann", Callsign = "A1", Departure = "eddf", Destination = "lrop" };

            rig.Hubs.Handle(SessionRig.From(Node(1)), user);
            rig.Hubs.Handle(SessionRig.From(Node(1)), user with { Guid = rig.Profile.Guid });
            rig.Hubs.Handle(SessionRig.From(Node(3)), user with { Guid = Guid.NewGuid() });

            HubDirectory.HubUser stored = Assert.Single(rig.Hubs.List[0].userList);
            Assert.Equal(("Ann", "A1", "EDDF", "LROP"), (stored.nickname, stored.flightPlan.callsign, stored.flightPlan.departure, stored.flightPlan.destination));
            Assert.Equal(1, rig.Hubs.HubUserCount);
        }

        [Fact]
        public void UserLists_AreRequestedOnlyWhileSomethingShowsThem()
        {
            var rig = new SessionRig();
            AddHub(rig, Node(1), "One");
            AddHub(rig, Node(2), "Two");
            rig.Outbox.Sent.Clear();

            rig.Clock.Advance(2.1);
            rig.Hubs.DoUserLists();
            Assert.Empty(rig.Outbox.Sent);

            rig.Ui.ShowsGlobalUsers = true;
            rig.Clock.Advance(2.1);
            rig.Hubs.DoUserLists();
            Assert.Equal(2, rig.Outbox.Of<UserListRequest>().Count());
        }

        [Fact]
        public void HubList_SurvivesSaveAndLoad()
        {
            var rig = new SessionRig();
            AddHub(rig, Node(1), "One");
            AddHub(rig, Node(2, 7000), "Two");
            rig.Hubs.DoWork();
            Assert.Contains("Saved 2 hub(s)", rig.Log.Events);

            var loaded = new HubDirectory(new FakeOutbox(), rig.Session, rig.Profile, rig.Policy, rig.EndPoints, rig.Ui, rig.Clock, new FakeLog());
            loaded.Load();
            Assert.Equal(new[] { ("One", Node(1), (ushort)6112), ("Two", Node(2, 7000), (ushort)7000) },
                loaded.List.Select(h => (h.name, h.nuid, h.port)));
        }

        // ------------------------------------------------------------------ UserDirectory

        [Fact]
        public void RequestNuid_AsksFiveOnlineHubs_OneGuaranteedRequestEach()
        {
            var rig = new SessionRig();
            for (uint i = 1; i <= 7; i++) AddHub(rig, Node(i), "Hub" + i);
            rig.Hubs.List[0].online = false;
            rig.Outbox.Sent.Clear();

            rig.Users.RequestNuid(42);

            var requests = rig.Outbox.Of<UserNuidRequest>().ToList();
            Assert.Equal(UserDirectory.REQUEST_NUID_NUM_SAMPLES, requests.Count);
            Assert.All(requests, r => Assert.True(r.Guaranteed));
            Assert.Equal(requests.Count, requests.Select(r => r.EndPoint).Distinct().Count());
            Assert.DoesNotContain(rig.Hubs.List[0].endPoint, requests.Select(r => r.EndPoint));
        }

        [Fact]
        public void OnlineUsers_AreFoundByUuid_AnsweredFor_AndExpire()
        {
            var rig = new SessionRig();
            rig.Users.Handle(SessionRig.From(Node(5)), new OnlineAnnouncement { Uuid = 42 });
            Assert.True(rig.Users.TryGetEndPoint(42, out IPEndPoint endPoint));
            Assert.Equal(Node(5).ToEndPoint(6112), endPoint);

            rig.Users.Handle(SessionRig.From(Node(6)), new UserNuidRequest { Uuid = 42 });
            rig.Users.Handle(SessionRig.From(Node(6)), new UserNuidRequest { Uuid = 43 });
            Sent reply = Assert.Single(rig.Outbox.Of<UserNuidReply>());
            Assert.Equal(Node(5), ((UserNuidReply)reply.Message).Node);

            rig.Users.DoOnlineUsers();
            Assert.Equal(1, rig.Users.OnlineUserCount);
            rig.Clock.Advance(HubDirectory.OFFLINE_TIME + 61);
            rig.Users.DoOnlineUsers();
            Assert.Equal(0, rig.Users.OnlineUserCount);
        }

        [Fact]
        public void OnlineAnnouncement_GoesToOnlineHubs_EveryMinute()
        {
            var rig = new SessionRig();
            AddHub(rig, Node(1), "One");
            AddHub(rig, Node(2), "Two");
            rig.Hubs.List[1].online = false;
            rig.Outbox.Sent.Clear();

            rig.Users.DoOnlineUsers();
            rig.Users.DoOnlineUsers();

            Sent announcement = Assert.Single(rig.Outbox.Of<OnlineAnnouncement>());
            Assert.Equal(rig.Hubs.List[0].endPoint, announcement.EndPoint);
            Assert.Equal(rig.Profile.Uuid, ((OnlineAnnouncement)announcement.Message).Uuid);
        }

        [Fact]
        public void AnyStatus_MarksTheAddressBookEntryOnline()
        {
            var rig = new SessionRig();
            var entry = new AddressBook.AddressBookEntry { endPoint = Node(8).ToEndPoint(6112) };
            rig.Profile.AddressBook.Add(entry);

            rig.Users.Handle(SessionRig.From(Node(8)), Status("", hub: false));
            Assert.True(entry.online);
        }

        [Fact]
        public void Uuid_TextRoundTrips()
        {
            uint uuid = UserDirectory.MakeUuid(Guid.Parse("11111111-2222-3333-4444-555555555555"));
            string text = UserDirectory.UuidToString(uuid);
            Assert.True(UserDirectory.IsUuidFormat(text));
            Assert.Equal(uuid, UserDirectory.MakeUuid(text));
            Assert.Equal(0u, UserDirectory.MakeUuid("not a uuid"));
        }

        // ------------------------------------------------------------------ HubHost

        [Fact]
        public void StatusRequest_IsAnsweredOnlyInASession_AndHubsAlsoListHubsAndRegisterTheUser()
        {
            var rig = new SessionRig();
            AddHub(rig, Node(1), "One");
            rig.Outbox.Sent.Clear();
            var request = new StatusRequestUpdate { HubListRequested = true, Uuid = 77 };

            rig.Session.Connected = false;
            rig.Host.Handle(SessionRig.From(Node(9)), request);
            Assert.Empty(rig.Outbox.Sent);

            rig.Session.Connected = true;
            rig.Host.Handle(SessionRig.From(Node(9)), request);
            StatusUpdate status = Assert.Single(rig.Outbox.Messages<StatusUpdate>());
            Assert.False(status.HubEnabled);
            Assert.Equal(((ushort)1, (ushort)2, (ushort)3, (ushort)4), (status.Planes, status.Helicopters, status.Boats, status.Vehicles));
            Assert.Empty(rig.Outbox.Messages<HubList>());
            Assert.False(rig.Users.IsUserOnline(77));

            rig.Profile.Hub = true;
            rig.Profile.HubName = "My hub";
            rig.Host.Handle(SessionRig.From(Node(9)), request);
            Assert.Equal("My hub", rig.Outbox.Messages<StatusUpdate>().Last().Name);
            Assert.Equal(Node(1), Assert.Single(Assert.Single(rig.Outbox.Messages<HubList>()).Hubs).Node);
            Assert.True(rig.Users.IsUserOnline(77));
        }

        [Fact]
        public void StatusRequest_FromAHub_SubmitsIt()
        {
            var rig = new SessionRig();
            rig.Host.Handle(SessionRig.From(Node(9)), new StatusRequestUpdate { HubEnabled = true });

            // our status reply, and our own status request to check it as a hub
            Assert.Single(rig.Outbox.Messages<StatusUpdate>());
            Assert.Single(rig.Outbox.Messages<StatusRequestUpdate>());
        }
    }
}
