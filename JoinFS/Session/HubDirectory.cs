using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace JoinFS
{
    /// <summary>
    /// The hubs this node knows: discovering and verifying them (status requests), keeping them in
    /// hubs.dat, and the user lists they publish. This node's own role as a hub is
    /// <see cref="HubHost"/>.
    /// </summary>
    public sealed class HubDirectory
    {
        const string HUB_LIST_FILE = "hubs.dat";
        // 10007 adds, per hub, its name as the network knows it (a hub with no IPv4 address has no legacy id) and the endpoints it can be reached at
        const ushort HUB_LIST_VERSION = 10007;

        /// <summary>At most this many hubs per IP address.</summary>
        public const int MAX_IP_HUBS = 4;

        /// <summary>Remove hubs not seen for this number of days.</summary>
        public const int DEAD_HUB_DURATION = 7;

        /// <summary>A hub (or user) not heard from for this long is offline.</summary>
        public const double OFFLINE_TIME = 300.0;

        const float PENDING_HUB_EXPIRE_TIME = 172800.0f;

        /// <summary>A user as a hub publishes it (also this hub's own users, see <see cref="HubHost"/>).</summary>
        public class HubUser
        {
            public double expireTime;
            public Guid guid;
            public bool atc;
            public string nickname;
            public ushort frequency;
            public float latitude;
            public float longitude;
            public ushort altitude;
            public ushort speed;
            public Sim.FlightPlan flightPlan;
            public ushort squawk;
            public byte level;
            public byte range;
            public bool ifr;
            public ushort heading;

            public HubUser(Guid guid)
            {
                this.guid = guid;
                flightPlan = new Sim.FlightPlan();
            }

            public HubUser(Guid guid, bool atc, string nickname, int frequency, double latitude, double longitude, double altitude, Sim.Vel velocity, Sim.FlightPlan flightPlan, int squawk, int level, int range, bool ifr, double heading)
            {
                this.guid = guid;
                this.atc = atc;
                this.nickname = nickname;
                this.frequency = (ushort)frequency;
                this.latitude = (float)latitude;
                this.longitude = (float)longitude;
                this.altitude = (ushort)Math.Max(0.0, Math.Min(20000.0, altitude));
                speed = 0;
                if (velocity != null)
                {
                    // knots
                    speed = (ushort)(Math.Sqrt(velocity.linear.x * velocity.linear.x + velocity.linear.z * velocity.linear.z) * 1.9438444925);
                }
                this.flightPlan = flightPlan;
                this.squawk = (ushort)squawk;
                this.level = (byte)level;
                this.range = (byte)range;
                this.ifr = ifr;
                this.heading = (ushort)Vector.HeadingDegrees(heading);
            }
        }

        /// <summary>A known hub.</summary>
        public class Hub
        {
            public NodeId nuid = new();
            public IPEndPoint endPoint = new(0, 0);
            public string addressText = "";
            public ushort port = Network.DEFAULT_PORT;
            public DateTime dateTime;
            public Guid guid = Guid.Empty;
            public string appVersion = "0.0.0";
            public string name = "";
            public string about = "";
            public string voip = "";
            public string nextEvent = "";
            public string airport = "";
            public int activityCircle = 40;
            public bool online = false;
            public double offlineTime = 0.0;
            public ushort users = 0;
            public ushort atcCount = 0;
            public string atcAirport = "";
            public int atcLevel = 2;
            public ushort planes = 0;
            public ushort helicopters = 0;
            public ushort boats = 0;
            public ushort vehicles = 0;
            public bool globalSession = false;
            public bool password = false;

            /// <summary>
            /// Where the hub can be reached: the endpoints it answered at and those a hub list named for it, best first (global IPv6, then IPv4).
            /// <see cref="endPoint"/> is the one it is polled and joined at. A hub with no endpoint this node can use is unreachable for it.
            /// </summary>
            public List<IPEndPoint> candidates = [];

            /// <summary>Which usable candidate an offline hub is polled at next.</summary>
            public int pollIndex;

            /// <summary>The users this hub published.</summary>
            public List<HubUser> userList = [];
        }

        /// <summary>Known hubs.</summary>
        public readonly List<Hub> List = [];

        /// <summary>A hub running the global session answered for the first time while we are in it.</summary>
        public event Action<IPEndPoint> GlobalHubFound;

        readonly INetworkOutbox outbox;
        readonly ISessionState session;
        readonly ILocalProfile profile;
        readonly IPeerPolicy policy;
        readonly IEndPointResolver endPoints;
        readonly ISessionUi ui;
        readonly IClock clock;
        readonly ISessionLog log;

        readonly Timer hubsTimer = new(5.0);
        readonly Timer pendingHubsTimer = new(1800.0);
        readonly Timer userListTimer = new(2.0);

        /// <summary>Hubs waiting to answer a status request, and when to give up on them.</summary>
        readonly Dictionary<IPEndPoint, float> pendingHubs = [];

        /// <summary>The endpoints a hub list named for a hub, by the endpoint chosen to poll it, until the hub answers.</summary>
        readonly Dictionary<IPEndPoint, List<IPEndPoint>> announcedCandidates = [];
        readonly List<Hub> tempHubs = [];
        readonly List<IPEndPoint> tempEndPoints = [];

        bool changed = false;
        int statusRequestCount = 0;
        int userListRequestCount = 0;
        IPEndPoint submitHub;

        public HubDirectory(INetworkOutbox outbox, ISessionState session, ILocalProfile profile, IPeerPolicy policy, IEndPointResolver endPoints, ISessionUi ui, IClock clock, ISessionLog log)
        {
            this.outbox = outbox;
            this.session = session;
            this.profile = profile;
            this.policy = policy;
            this.endPoints = endPoints;
            this.ui = ui;
            this.clock = clock;
            this.log = log;
            // first user list request after one interval
            userListTimer.Elapsed(clock.Now);
        }

        /// <summary>Total users over all hubs.</summary>
        public int HubUserCount
        {
            get
            {
                int count = 0;
                foreach (var hub in List) count += hub.userList.Count;
                return count;
            }
        }

        // ------------------------------------------------------------------ where a hub is

        /// <summary>This node can send to the endpoint: an IPv6 one needs a global IPv6 address of its own, and a node with no IPv4 address uses no IPv4 one.</summary>
        public bool CanUse(IPEndPoint endPoint)
        {
            NetworkSnapshot snapshot = session.Snapshot;
            return endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? snapshot.Ipv6Addresses.Count > 0 : !snapshot.NoIPv4;
        }

        /// <summary>This node can reach the hub at one of its endpoints at least (an IPv4-only hub is not reachable for a node with no IPv4 address).</summary>
        public bool Reachable(Hub hub) => CanUse(hub.endPoint) || hub.candidates.Any(CanUse);

        /// <summary>Global IPv6 first, then IPv4, each in the order given.</summary>
        internal static List<IPEndPoint> InFamilyOrder(IEnumerable<IPEndPoint> endPoints) =>
            [.. endPoints.Where(e => e.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6), .. endPoints.Where(e => e.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)];

        /// <summary>
        /// The endpoint to ask the hub at: the one it answers at while online, else its usable endpoints in turn, so that a hub whose IPv6 broke
        /// is found again on IPv4. Null when this node can use none of them.
        /// </summary>
        public IPEndPoint PollEndPoint(Hub hub)
        {
            if (hub.online && CanUse(hub.endPoint))
            {
                // now and then the preferred endpoint (IPv6) when the hub is polled at another: if it answers there, the hub moves to it
                IPEndPoint best = hub.candidates.Find(CanUse);
                return best != null && !best.Equals(hub.endPoint) && ++hub.pollIndex % 4 == 0 ? best : hub.endPoint;
            }
            List<IPEndPoint> usable = hub.candidates.FindAll(CanUse);
            if (usable.Count == 0)
            {
                return CanUse(hub.endPoint) ? hub.endPoint : null;
            }
            return usable[hub.pollIndex++ % usable.Count];
        }

        /// <summary>At most this many endpoints are kept for a hub (the Hello's bound, and the one it answered at).</summary>
        const int MaxHubEndPoints = 5;

        /// <summary>The hub answered at <paramref name="endPoint"/>: that is where it is, and a way to reach it, with those a hub list named.</summary>
        void Learn(Hub hub, IPEndPoint endPoint, IReadOnlyList<IPEndPoint> announced)
        {
            var all = new List<IPEndPoint> { endPoint };
            if (announced != null)
            {
                all.AddRange(announced.Where(Peer.IsUsableCandidate));
            }
            all.AddRange(hub.candidates);
            List<IPEndPoint> merged = InFamilyOrder(all.Distinct()).Take(MaxHubEndPoints).ToList();
            if (!merged.SequenceEqual(hub.candidates))
            {
                hub.candidates = merged;
                changed = true;
            }
            // IPv6 is preferred: a hub that answers on both is polled and joined on IPv6
            bool v6 = endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            IPEndPoint previous = hub.endPoint;
            if (hub.endPoint.Port == 0 || !CanUse(hub.endPoint) || !hub.candidates.Contains(hub.endPoint) || (v6 && hub.endPoint.AddressFamily != endPoint.AddressFamily && CanUse(endPoint)))
            {
                hub.endPoint = endPoint;
            }
            hub.port = (ushort)hub.endPoint.Port;
            if (previous.Port != 0 && !previous.Equals(hub.endPoint))
            {
                log.Event("Hub '" + hub.name + "' is now asked at " + hub.endPoint + " (" + FamilyName(hub.endPoint) + "), was " + previous + " (" + FamilyName(previous) + "): it answers there and IPv6 is preferred");
            }
            endPoints.NoteCandidates(hub.endPoint, hub.candidates);
        }

        static string FamilyName(IPEndPoint endPoint) => endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";

        // ------------------------------------------------------------------ discovery

        /// <summary>Submit a hub on the next hubs tick.</summary>
        public void ScheduleSubmitHub(IPEndPoint endPoint)
        {
#if !NO_HUBS
            submitHub ??= endPoint;
#endif
        }

        /// <summary>A possible hub: ask it for its status (it's added once it answers as a hub).</summary>
        public void SubmitHub(IPEndPoint endPoint) => SubmitHub(endPoint, null);

        /// <summary>A possible hub that a hub list named at several endpoints: <paramref name="endPoint"/> is asked, the others are kept with the hub once it answers.</summary>
        void SubmitHub(IPEndPoint endPoint, List<IPEndPoint> announced)
        {
            if (announced != null)
            {
                if (announcedCandidates.Count >= 64) announcedCandidates.Clear(); // bounded: a hub list is somebody else's word
                announcedCandidates[endPoint] = announced;
            }
            int count = 0;
            foreach (var pending in pendingHubs)
            {
                if (pending.Key.Address.Equals(endPoint.Address)) count++;
            }
            if (pendingHubs.ContainsKey(endPoint) == false && count < MAX_IP_HUBS)
            {
                pendingHubs.Add(endPoint, (float)clock.Now + PENDING_HUB_EXPIRE_TIME);
                SendStatusRequest(endPoint, true);
            }
        }

        public StatusRequestUpdate BuildStatusRequest(bool requestHubList) => new()
        {
            HubEnabled = profile.Hub,
            HubListRequested = requestHubList,
            Uuid = profile.Uuid,
        };

        /// <summary>Ask a node or hub for its status.</summary>
        public void SendStatusRequest(IPEndPoint endPoint, bool requestHubList) => outbox.Ask(endPoint, BuildStatusRequest(requestHubList));

        /// <summary>Ask a hub for its status, at the endpoint it is polled at; nothing when this node can use none of its endpoints.</summary>
        void Poll<T>(Hub hub, in T message) where T : struct, IMessage
        {
            if (PollEndPoint(hub) is { } endPoint)
            {
                outbox.Ask(endPoint, message);
            }
        }

        /// <summary>Poll hubs' status, drop dead hubs, save changes, retry pending hubs.</summary>
        public void DoWork()
        {
            if (!session.Ready)
            {
                return;
            }
            double now = clock.Now;

            if (hubsTimer.Elapsed(now))
            {
                if (submitHub != null)
                {
                    SubmitHub(submitHub);
                    submitHub = null;
                }

                StatusRequestUpdate request = BuildStatusRequest((statusRequestCount & 7) == 0);
                statusRequestCount++;

                if (statusRequestCount <= 3)
                {
                    // first few ticks since launch: every known offline hub
                    foreach (var hub in List)
                    {
                        if (hub.nuid.Valid() && hub.online == false)
                        {
                            Poll(hub, request);
                        }
                    }
                }
                else if (List.Count > 0)
                {
                    // then one hub per tick, in turn
                    int index = statusRequestCount % List.Count;
                    if (List[index].nuid.Valid())
                    {
                        Poll(List[index], request);
                    }
                }

                foreach (var hub in List)
                {
                    // not seen for days (once we've been running a while)
                    if (now > 120.0 && (DateTime.Now - hub.dateTime).Days > DEAD_HUB_DURATION)
                    {
                        tempHubs.Add(hub);
                    }
                    if (now > hub.offlineTime)
                    {
                        hub.online = false;
                    }
                }

                foreach (var hub in tempHubs)
                {
                    log.Event("Removed hub '" + hub.name + "' - '" + UserDirectory.UuidToString(UserDirectory.MakeUuid(hub.guid)) + "'");
                    List.Remove(hub);
                    changed = true;
                }
                tempHubs.Clear();

                if (changed)
                {
                    Save();
                    changed = false;
                }
            }

            if (pendingHubsTimer.Elapsed(now))
            {
                foreach (var pending in pendingHubs)
                {
                    outbox.Ask(pending.Key, BuildStatusRequest(true));
                    if (pending.Value < now)
                    {
                        tempEndPoints.Add(pending.Key);
                    }
                }
                foreach (var endPoint in tempEndPoints)
                {
                    pendingHubs.Remove(endPoint);
                }
                tempEndPoints.Clear();
            }
        }

        public void Handle(in MessageMeta meta, in HubList list)
        {
            foreach (HubAddress address in list.Hubs)
            {
                // where the hub is: its legacy id says (our LAN address when it shares our NAT), and a JFP2 list adds the endpoints it can be reached at
                var found = new List<IPEndPoint>();
                if (address.Node.TryGetLegacy(out LegacyNodeId legacyHub))
                {
                    found.Add(endPoints.MakeEndPoint(legacyHub, address.Port));
                }
                found.AddRange((address.Candidates ?? []).Where(Peer.IsUsableCandidate));
                found = InFamilyOrder(found.Distinct()).Take(MaxHubEndPoints).ToList();
                if (found.Count == 0)
                {
                    continue;
                }
                Hub known = List.Find(h => h.nuid == address.Node || found.Contains(h.endPoint));
                if (known != null)
                {
                    Learn(known, known.endPoint.Port != 0 ? known.endPoint : found[0], found);
                    continue;
                }
                SubmitOrKeep(address.Node, found);
            }
        }

        /// <summary>
        /// A seed hub: a host name with an A and an AAAA record gives both, and the hub is asked at the first this node can use. A seed that is an IPv4
        /// address only is kept as unreachable by a node with no IPv4 address, like a hub named by a hub list.
        /// </summary>
        public void SubmitSeed(IPEndPoint seed, IReadOnlyList<IPEndPoint> addresses)
        {
            var found = InFamilyOrder(addresses.Distinct()).Take(MaxHubEndPoints).ToList();
            if (List.Find(h => found.Contains(h.endPoint)) != null)
            {
                return;
            }
            // a seed says no name: an IPv4 one is named as the legacy id of its address, which is what a hub list would call it
            NodeId name = seed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? NodeId.FromLegacy(seed, 0) : default;
            SubmitOrKeep(name, found);
        }

        /// <summary>Ask the hub at the first endpoint this node can use; one it can use none of is kept and shown as unreachable, not hidden.</summary>
        void SubmitOrKeep(NodeId name, List<IPEndPoint> found)
        {
            IPEndPoint ask = found.Find(CanUse);
            if (ask != null)
            {
                SubmitHub(ask, found);
            }
            else if (name.Valid())
            {
                // a hub nobody can poll from here, say an IPv4-only hub for a node with no IPv4 address
                var hub = new Hub { nuid = name, endPoint = found[0], port = (ushort)found[0].Port, addressText = found[0].Address.ToString(), dateTime = DateTime.Now, candidates = found };
                List.Add(hub);
                changed = true;
                log.Event("Hub " + found[0] + " is unreachable from this node: it has no address in a family this node uses");
            }
        }

        /// <summary>A node's or hub's status: add, update or drop it as a hub.</summary>
        public void Handle(in MessageMeta meta, in StatusUpdate status)
        {
            IPEndPoint endPoint = meta.EndPoint;
            NodeId nuid = meta.Sender;

            if (status.HubEnabled)
            {
                pendingHubs.Remove(endPoint);

                // not too many at one IP, and not this hub
                if (HubCount_IP(endPoint.Address) < MAX_IP_HUBS && nuid != session.LocalId)
                {
                    Hub hub = FindHub(nuid, endPoint);
                    if (hub == null)
                    {
                        hub = new Hub();
                        List.Add(hub);
                        changed = true;
                        log.Event("Added new hub '" + status.Name + "' - '" + UserDirectory.UuidToString(UserDirectory.MakeUuid(status.Guid)) + "'");
                    }
                    else if (
                        hub.guid.Equals(status.Guid) == false ||
                        hub.appVersion.Equals(status.AppVersion) == false ||
                        hub.addressText.Equals(status.Address) == false ||
                        hub.name.Equals(status.Name) == false ||
                        hub.about.Equals(status.About) == false ||
                        hub.voip.Equals(status.Voip) == false ||
                        hub.nextEvent.Equals(status.NextEvent) == false ||
                        hub.airport.Equals(status.Airport) == false ||
                        hub.activityCircle != status.ActivityCircle ||
                        hub.globalSession != status.GlobalSession ||
                        hub.password != status.PasswordRequired
                        )
                    {
                        changed = true;
                    }

                    // first contact with a global hub while we're in the global session
                    if (session.Snapshot.GlobalSession && hub.globalSession == false && status.GlobalSession)
                    {
                        GlobalHubFound?.Invoke(endPoint);
                    }

                    hub.nuid = nuid;
                    announcedCandidates.Remove(endPoint, out List<IPEndPoint> announced);
                    Learn(hub, endPoint, announced);
                    hub.dateTime = DateTime.Now;
                    hub.guid = status.Guid;
                    hub.appVersion = status.AppVersion;
                    hub.online = true;
                    hub.offlineTime = clock.Now + OFFLINE_TIME;
                    hub.users = status.Users;
                    hub.atcCount = status.AtcCount;
                    hub.atcAirport = status.AtcAirport;
                    hub.atcLevel = status.AtcLevel;
                    hub.planes = status.Planes;
                    hub.helicopters = status.Helicopters;
                    hub.boats = status.Boats;
                    hub.vehicles = status.Vehicles;
                    hub.addressText = status.Address;
                    hub.name = status.Name;
                    hub.about = status.About;
                    hub.voip = status.Voip;
                    hub.nextEvent = status.NextEvent;
                    hub.airport = status.Airport;
                    hub.activityCircle = status.ActivityCircle;
                    hub.globalSession = status.GlobalSession;
                    hub.password = status.PasswordRequired;
                }
            }
            else
            {
                // no longer a hub
                Hub hub = FindHub(nuid, endPoint);
                if (hub != null)
                {
                    log.Event("Removed hub '" + hub.name + "' - '" + UserDirectory.UuidToString(UserDirectory.MakeUuid(status.Guid)) + "'");
                    List.Remove(hub);
                    changed = true;
                }
            }
        }

        /// <summary>The hub with this name, or the one known at this endpoint.</summary>
        Hub FindHub(NodeId nuid, IPEndPoint endPoint) => List.Find(h => h.nuid == nuid) ?? List.Find(h => h.endPoint.Equals(endPoint) || h.candidates.Contains(endPoint));

        /// <summary>The hub that has an endpoint at this address (the sender of a user list).</summary>
        Hub FindHubAt(IPEndPoint endPoint) => List.Find(h => h.endPoint.Address.Equals(endPoint.Address) || h.candidates.Exists(c => c.Address.Equals(endPoint.Address)));

        /// <summary>Entries in one HubList message: a JFP2 entry is at most 8 + 2 + 1 + 5 * 20 = 111 bytes (five endpoints are kept for a hub, each a length byte and 19 for IPv6), so 8 of them fit the 1,000 bytes of a message.</summary>
        const int HubListChunk = 8;

        /// <summary>
        /// The online hubs we know (reply to a StatusRequest asking for them), a few to a message. The wire the answer goes by chooses what it can
        /// say: legacy names the hubs that have a legacy id, by it and the port, and JFP2 adds the endpoints (IPv6 ones included) each can be reached at.
        /// </summary>
        public void SendHubList(IPEndPoint endPoint)
        {
            var entries = new List<HubAddress>();
            foreach (var hub in List)
            {
                if (hub.online && policy.IgnoreNode(hub.endPoint.Address) == false && policy.IgnoreNode(ref hub.guid) == false)
                {
                    entries.Add(new HubAddress
                    {
                        Node = hub.nuid,
                        Port = (ushort)hub.endPoint.Port,
                        Candidates = hub.candidates.FindAll(Peer.IsUsableCandidate),
                    });
                }
            }
            for (int start = 0; start < entries.Count; start += HubListChunk)
            {
                outbox.SendToEndPoint(endPoint, new HubList { Hubs = entries.GetRange(start, Math.Min(HubListChunk, entries.Count - start)) });
            }
        }

        int HubCount_IP(IPAddress address)
        {
            int count = 0;
            foreach (var hub in List)
            {
                if (hub.endPoint.Address.Equals(address)) count++;
            }
            return count;
        }

        // ------------------------------------------------------------------ hub user lists

        /// <summary>Expire hub users, and request hubs' user lists while something shows them.</summary>
        public void DoUserLists()
        {
            if (!session.Ready || !userListTimer.Elapsed(clock.Now))
            {
                return;
            }
            double now = clock.Now;
            foreach (var hub in List)
            {
                hub.userList.RemoveAll(u => now > u.expireTime);
            }

            if (profile.PublishWhazzup || ui.ShowsGlobalUsers)
            {
                if (userListRequestCount <= 3)
                {
                    // first few ticks: every online hub we have no users from yet
                    foreach (var hub in List)
                    {
                        if (hub.online && hub.userList.Count == 0)
                        {
                            SendUserListRequest(hub);
                        }
                    }
                }
                else if (List.Count > 0)
                {
                    // then one hub per tick, in turn
                    int index = userListRequestCount % List.Count;
                    if (List[index].online)
                    {
                        SendUserListRequest(List[index]);
                    }
                    if (profile.PublishWhazzup)
                    {
                        foreach (var hub in List)
                        {
                            if (hub.online)
                            {
                                Poll(hub, new UserPositionsRequest());
                            }
                        }
                    }
                }
                userListRequestCount++;
            }
        }

        void SendUserListRequest(Hub hub) => Poll(hub, new UserListRequest());

        public void Handle(in MessageMeta meta, in HubUserUpdate update)
        {
            IPEndPoint endPoint = meta.EndPoint;
            Hub hub = FindHubAt(endPoint);
            if (hub == null || update.Guid.Equals(profile.Guid))
            {
                return;
            }
            Guid guid = update.Guid;
            HubUser user = hub.userList.Find(u => u.guid.Equals(guid));
            if (user == null)
            {
                user = new HubUser(guid);
                hub.userList.Add(user);
            }
            user.expireTime = clock.Now + OFFLINE_TIME;
            user.atc = update.Atc;
            user.ifr = update.Ifr;
            user.flightPlan.callsign = update.Callsign;
            user.nickname = update.Nickname;
            user.frequency = update.Frequency;
            user.latitude = update.Latitude;
            user.longitude = update.Longitude;
            user.altitude = update.Altitude;
            user.speed = update.Speed;
            user.squawk = update.Squawk;
            user.level = update.Level;
            user.range = update.Range;
            user.heading = update.Heading;
            user.flightPlan.icaoType = update.IcaoType;
            user.flightPlan.departure = update.Departure.ToUpperInvariant();
            user.flightPlan.destination = update.Destination.ToUpperInvariant();
            user.flightPlan.rules = update.Rules;
            user.flightPlan.route = update.Route;
            user.flightPlan.remarks = update.Remarks;
            user.flightPlan.alternate = update.Alternate;
            user.flightPlan.speed = update.FlightSpeed;
            user.flightPlan.altitude = update.FlightAltitude;
            user.flightPlan.registration = update.Registration;
            user.flightPlan.icaoAirline = update.IcaoAirline;
            user.flightPlan.flightNumber = update.FlightNumber;
        }

        public void Handle(in MessageMeta meta, in UserPositions positions)
        {
            IPEndPoint endPoint = meta.EndPoint;
            Hub hub = FindHubAt(endPoint);
            if (hub == null)
            {
                return;
            }
            foreach (UserPosition position in positions.Users)
            {
                if (position.Guid.Equals(profile.Guid))
                {
                    continue;
                }
                Guid guid = position.Guid;
                HubUser user = hub.userList.Find(u => u.guid.Equals(guid));
                if (user != null)
                {
                    user.expireTime = clock.Now + OFFLINE_TIME;
                    user.latitude = position.Latitude;
                    user.longitude = position.Longitude;
                    user.altitude = position.Altitude;
                    user.speed = position.Speed;
                    user.squawk = position.Squawk;
                    user.heading = position.Heading;
                }
            }
        }

        // ------------------------------------------------------------------ hubs.dat

        string FilePath => profile.StoragePath + Path.DirectorySeparatorChar + HUB_LIST_FILE;

        void Save()
        {
            try
            {
                using BinaryWriter writer = new(File.Create(FilePath));
                // ignored hubs aren't kept
                foreach (var hub in List)
                {
                    if (hub.nuid.Valid() && policy.IgnoreNode(hub.endPoint.Address) == false && policy.IgnoreNode(ref hub.guid) == false)
                    {
                        tempHubs.Add(hub);
                    }
                }

                writer.Write(HUB_LIST_VERSION);
                writer.Write((ushort)tempHubs.Count);
                foreach (var hub in tempHubs)
                {
                    writer.Write(hub.addressText);
                    // the legacy id of the hub, all zeros when it has none (its name follows at the end of the entry)
                    hub.nuid.TryGetLegacy(out LegacyNodeId legacyNuid);
                    legacyNuid.Write(writer);
                    writer.Write(hub.port);
                    writer.Write(hub.dateTime.ToBinary());
                    writer.Write(hub.guid.ToByteArray());
                    writer.Write(hub.appVersion);
                    writer.Write(hub.name);
                    writer.Write(hub.about);
                    writer.Write(hub.voip);
                    writer.Write(hub.nextEvent);
                    writer.Write(hub.airport);
                    writer.Write(hub.activityCircle);
                    writer.Write(hub.globalSession);
                    writer.Write(hub.password);
                    // version 10007: the hub's name whatever its kind, and where it can be reached
                    Span<byte> name = stackalloc byte[NodeId.WireSize];
                    hub.nuid.WriteTo(name);
                    writer.Write(name);
                    writer.Write((byte)hub.candidates.Count);
                    foreach (IPEndPoint candidate in hub.candidates) writer.Write(candidate.ToString());
                }

                log.Event("Saved " + tempHubs.Count + " hub(s)");
            }
            catch (Exception ex)
            {
                log.Event(ex.Message);
            }
            finally
            {
                tempHubs.Clear();
            }
        }

        /// <summary>Load hubs.dat (retrying, as another instance may be writing it).</summary>
        public void Load()
        {
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        using BinaryReader reader = new(File.Open(FilePath, FileMode.Open));
                        List.Clear();

                        ushort version = reader.ReadUInt16();
                        if (version < 10006 || version > HUB_LIST_VERSION)
                        {
                            log.Event("Invalid hub list version. Recreating the hub list.");
                        }
                        else
                        {
                            ushort count = reader.ReadUInt16();
                            for (int i = 0; i < count; i++)
                            {
                                Hub hub = new()
                                {
                                    addressText = reader.ReadString()
                                };

                                if (version >= 10005)
                                {
                                    hub.nuid = NodeId.FromLegacy(LegacyNodeId.Read(reader));
                                    hub.port = reader.ReadUInt16();
                                }
                                else
                                {
                                    uint ip = version >= 10003 ? reader.ReadUInt32() : 0;
                                    hub.port = reader.ReadUInt16();
                                    byte local = version >= 10002 ? reader.ReadByte() : (byte)0;
                                    hub.nuid = NodeId.FromLegacy(ip, 0, local);
                                }
                                hub.dateTime = DateTime.FromBinary(reader.ReadInt64());
                                hub.guid = new Guid(reader.ReadBytes(16));
                                hub.appVersion = reader.ReadString();
                                hub.name = reader.ReadString();
                                hub.about = reader.ReadString();
                                hub.voip = reader.ReadString();
                                hub.nextEvent = reader.ReadString();
                                hub.airport = reader.ReadString();
                                hub.activityCircle = reader.ReadInt32();
                                hub.globalSession = reader.ReadBoolean();
                                hub.password = version >= 10004 && reader.ReadBoolean();
                                if (version >= 10007)
                                {
                                    hub.nuid = NodeId.ReadFrom(reader.ReadBytes(NodeId.WireSize));
                                    int candidateCount = reader.ReadByte();
                                    for (int c = 0; c < candidateCount; c++)
                                    {
                                        if (IPEndPoint.TryParse(reader.ReadString(), out IPEndPoint candidate))
                                        {
                                            hub.candidates.Add(candidate);
                                        }
                                    }
                                }

                                if (version < 10003)
                                {
                                    endPoints.MakeEndPoint(hub.addressText, hub.port, out hub.endPoint);
                                    hub.nuid.TryGetLegacy(out LegacyNodeId legacyRead);
                                    hub.nuid = NodeId.FromLegacy(hub.endPoint, legacyRead.local);
                                }
                                else
                                {
                                    if (hub.nuid.TryGetLegacy(out LegacyNodeId legacyHubId))
                                    {
                                        hub.endPoint = endPoints.MakeEndPoint(legacyHubId, hub.port);
                                    }
                                    else if (hub.candidates.Count > 0)
                                    {
                                        hub.endPoint = hub.candidates[0]; // a hub with no IPv4 address has no legacy id to say where it is
                                    }
                                }

                                if (hub.endPoint.Port != 0 && !hub.candidates.Contains(hub.endPoint))
                                {
                                    hub.candidates.Insert(0, hub.endPoint);
                                }
                                if (hub.endPoint.Port != 0)
                                {
                                    endPoints.NoteCandidates(hub.endPoint, hub.candidates);
                                }
                                if (hub.nuid.Valid() && HubCount_IP(hub.endPoint.Address) < MAX_IP_HUBS)
                                {
                                    List.Add(hub);
                                }
                            }

                            log.Event(attempt == 1 ? "Loaded " + count + " hub(s)" : "Loaded " + count + " hub(s) on attempt " + attempt);
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    log.Event(ex.Message);
                }

                // wait a random time before trying again
                Random random = new((int)DateTime.Now.Ticks);
                Thread.Sleep(random.Next(10, 100));
            }
        }
    }
}
