using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.IO;
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
        const ushort HUB_LIST_VERSION = 10006;

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
                this.heading = (ushort)(heading * (180.0 / Math.PI));
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

        // ------------------------------------------------------------------ discovery

        /// <summary>Submit a hub on the next hubs tick.</summary>
        public void ScheduleSubmitHub(IPEndPoint endPoint)
        {
#if !NO_HUBS
            submitHub ??= endPoint;
#endif
        }

        /// <summary>A possible hub: ask it for its status (it's added once it answers as a hub).</summary>
        public void SubmitHub(IPEndPoint endPoint)
        {
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
        public void SendStatusRequest(IPEndPoint endPoint, bool requestHubList) => outbox.SendToEndPoint(endPoint, BuildStatusRequest(requestHubList));

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
                            outbox.SendToEndPoint(hub.endPoint, request);
                        }
                    }
                }
                else if (List.Count > 0)
                {
                    // then one hub per tick, in turn
                    int index = statusRequestCount % List.Count;
                    if (List[index].nuid.Valid())
                    {
                        outbox.SendToEndPoint(List[index].endPoint, request);
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
                    outbox.SendToEndPoint(pending.Key, BuildStatusRequest(true));
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
                NodeId hubNuid = address.Node;
                if (List.Find(h => h.nuid == hubNuid) == null)
                {
                    SubmitHub(endPoints.MakeEndPoint(hubNuid, address.Port));
                }
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
                if (HubCount_IP(nuid) < MAX_IP_HUBS && nuid != session.LocalId)
                {
                    Hub hub = List.Find(h => h.nuid == nuid);
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
                    hub.endPoint = endPoint;
                    hub.port = (ushort)endPoint.Port;
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
                Hub hub = List.Find(h => h.nuid == nuid);
                if (hub != null)
                {
                    log.Event("Removed hub '" + hub.name + "' - '" + UserDirectory.UuidToString(UserDirectory.MakeUuid(status.Guid)) + "'");
                    List.Remove(hub);
                    changed = true;
                }
            }
        }

        /// <summary>The online hubs we know (reply to a StatusRequest asking for them).</summary>
        public void SendHubList(IPEndPoint endPoint)
        {
            var list = new HubList { Hubs = [] };
            foreach (var hub in List)
            {
                if (hub.online && policy.IgnoreNode(hub.endPoint.Address) == false && policy.IgnoreNode(ref hub.guid) == false)
                {
                    list.Hubs.Add(new HubAddress { Node = hub.nuid, Port = (ushort)hub.endPoint.Port });
                }
            }
            if (list.Hubs.Count > 0) outbox.SendToEndPoint(endPoint, list);
        }

        int HubCount_IP(NodeId nuid)
        {
            int count = 0;
            foreach (var hub in List)
            {
                if (hub.nuid.ip == nuid.ip) count++;
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
                            SendUserListRequest(hub.endPoint);
                        }
                    }
                }
                else if (List.Count > 0)
                {
                    // then one hub per tick, in turn
                    int index = userListRequestCount % List.Count;
                    if (List[index].online)
                    {
                        SendUserListRequest(List[index].endPoint);
                    }
                    if (profile.PublishWhazzup)
                    {
                        foreach (var hub in List)
                        {
                            if (hub.online)
                            {
                                outbox.SendToEndPoint(hub.endPoint, new UserPositionsRequest());
                            }
                        }
                    }
                }
                userListRequestCount++;
            }
        }

        void SendUserListRequest(IPEndPoint endPoint) => outbox.SendToEndPoint(endPoint, new UserListRequest());

        public void Handle(in MessageMeta meta, in HubUserUpdate update)
        {
            IPEndPoint endPoint = meta.EndPoint;
            Hub hub = List.Find(h => h.endPoint.Address.Equals(endPoint.Address));
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
            Hub hub = List.Find(h => h.endPoint.Address.Equals(endPoint.Address));
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
                    if (policy.IgnoreNode(hub.endPoint.Address) == false && policy.IgnoreNode(ref hub.guid) == false)
                    {
                        tempHubs.Add(hub);
                    }
                }

                writer.Write(HUB_LIST_VERSION);
                writer.Write((ushort)tempHubs.Count);
                foreach (var hub in tempHubs)
                {
                    writer.Write(hub.addressText);
                    hub.nuid.Write(writer);
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
                                    hub.nuid = NodeId.Read(reader);
                                    hub.port = reader.ReadUInt16();
                                }
                                else
                                {
                                    uint ip = version >= 10003 ? reader.ReadUInt32() : 0;
                                    hub.port = reader.ReadUInt16();
                                    byte local = version >= 10002 ? reader.ReadByte() : (byte)0;
                                    hub.nuid = new NodeId(ip, hub.nuid.port, local);
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

                                if (version < 10003)
                                {
                                    endPoints.MakeEndPoint(hub.addressText, hub.port, out hub.endPoint);
                                    hub.nuid = new NodeId(hub.endPoint, hub.nuid.local);
                                }
                                else
                                {
                                    hub.endPoint = endPoints.MakeEndPoint(hub.nuid, hub.port);
                                }

                                if (hub.nuid.Valid() && HubCount_IP(hub.nuid) < MAX_IP_HUBS)
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
