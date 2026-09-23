using JoinFS.Net;
using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS
{
    /// <summary>
    /// This node as a directory entry: answering status requests (any node), and when it is a hub,
    /// keeping the list of its session's users and serving it (user lists, user positions, the hubs
    /// it knows, who is online). Other hubs are <see cref="HubDirectory"/>.
    /// </summary>
    public sealed class HubHost
    {
        /// <summary>This hub's users (rebuilt every 10 s while this node is a hub).</summary>
        public readonly List<HubDirectory.HubUser> LocalUsers = [];

        readonly INetworkOutbox outbox;
        readonly ISessionState session;
        readonly ILocalProfile profile;
        readonly ISimView sim;
        readonly PeerTable peers;
        readonly HubDirectory hubs;
        readonly UserDirectory users;
        readonly IClock clock;
        readonly ISessionLog log;

        readonly Timer localUsersTimer = new(10.0);
        readonly uint vuidSquawk = VariableMgr.CreateVuid("transponder code:1");
        readonly uint vuidIfr = VariableMgr.CreateVuid("ai traffic isifr");

        public HubHost(INetworkOutbox outbox, ISessionState session, ILocalProfile profile, ISimView sim, PeerTable peers, HubDirectory hubs, UserDirectory users, IClock clock, ISessionLog log)
        {
            this.outbox = outbox;
            this.session = session;
            this.profile = profile;
            this.sim = sim;
            this.peers = peers;
            this.hubs = hubs;
            this.users = users;
            this.clock = clock;
            this.log = log;
            // first rebuild after one interval
            localUsersTimer.Elapsed(clock.Now);
        }

        // ------------------------------------------------------------------ status

        /// <summary>A node or hub asked for our status (and maybe the hubs we know).</summary>
        public void Handle(in MessageMeta meta, in StatusRequestUpdate request)
        {
            if (!session.Connected)
            {
                return;
            }
            IPEndPoint endPoint = meta.EndPoint;
            if (request.HubEnabled)
            {
                hubs.SubmitHub(endPoint);
            }

            try
            {
                outbox.SendToEndPoint(endPoint, BuildStatus());
            }
            catch (Exception ex)
            {
                log.Event("Failed to write status reply message: " + ex.Message);
            }

            if (profile.Hub)
            {
                if (request.HubListRequested)
                {
                    hubs.SendHubList(endPoint);
                }
                try
                {
                    users.RegisterOnlineUser(request.Uuid, meta.Sender, (ushort)endPoint.Port);
                }
                catch { }
            }
        }

        /// <summary>This node's current status (hub directory information).</summary>
        public StatusUpdate BuildStatus()
        {
            int atcCount = peers.GetMainAtc(out string airport, out int level);
            sim.CountObjects(out ushort planes, out ushort helicopters, out ushort boats, out ushort vehicles);

            var status = new StatusUpdate
            {
                Guid = profile.Guid,
                AppVersion = Main.Version,
                Users = (ushort)LocalUsers.Count,
                AtcCount = (ushort)atcCount,
                AtcAirport = airport,
                AtcLevel = level,
                Planes = planes,
                Helicopters = helicopters,
                Boats = boats,
                Vehicles = vehicles,
                HubEnabled = profile.Hub,
            };

            if (profile.Hub)
            {
                string address = profile.HubDomain;
                if (address.Length == 0)
                {
                    address = profile.MyIp;
                }
                status.Address = address;
                status.Name = profile.HubName;
                status.About = profile.HubAbout;
                status.Voip = profile.HubVoip;
                status.NextEvent = profile.HubEvent;
                status.Airport = profile.Atc ? profile.AtcAirport : "";
                status.ActivityCircle = profile.ActivityCircle;
                status.GlobalSession = session.Snapshot.GlobalSession;
                status.PasswordRequired = session.Snapshot.PasswordProtected;
            }
            return status;
        }

        // ------------------------------------------------------------------ this hub's users

        /// <summary>Every 10 s while this node is a hub: rebuild the list of this session's users.</summary>
        public void DoLocalUsers()
        {
            if (!localUsersTimer.Elapsed(clock.Now) || !profile.Hub)
            {
                return;
            }
            LocalUsers.Clear();

            // this node: as a controller, or as a pilot
            Sim.Aircraft aircraft = sim.UserAircraft;
            if (profile.Atc && profile.AtcAirport.Length > 0)
            {
                AddController(profile.Guid, profile.Nickname, profile.AtcAirport, profile.AtcLevel, profile.AtcFrequency, profile.ActivityCircle);
            }
            else
            {
                AddPilot(profile.Guid, profile.Nickname, aircraft);
            }

            // every node in the session
            foreach (var node in peers.Nodes)
            {
                if (node.Value.atc && node.Value.atcAirport.Length > 0)
                {
                    AddController(node.Value.guid, node.Value.nickname, node.Value.atcAirport, node.Value.atcLevel, node.Value.atcFrequency, node.Value.activityCircle);
                }
                else
                {
                    AddPilot(node.Value.guid, node.Value.nickname, sim.FindUserAircraft(node.Key));
                }
            }
        }

        void AddController(Guid guid, string nickname, string airportCode, int level, int frequency, int range)
        {
            if (sim.TryGetAirport(airportCode, out Main.Airport airport))
            {
                double latitude = Math.Min(90.0, Math.Max(-90.0, airport.latitude));
                double longitude = Math.Min(180.0, Math.Max(-180.0, airport.longitude));
                Sim.FlightPlan flightPlan = new()
                {
                    callsign = Sim.MakeAtcCallsign(airportCode, level),
                    departure = airportCode
                };
                LocalUsers.Add(new HubDirectory.HubUser(guid, true, nickname, frequency, latitude, longitude, 0.0, null, flightPlan, 0, level, range, true, 0));
            }
        }

        void AddPilot(Guid guid, string nickname, Sim.Aircraft aircraft)
        {
            if (aircraft != null && aircraft.Position != null)
            {
                int squawk = aircraft.variableSet != null ? aircraft.variableSet.GetInteger(vuidSquawk) : 0;
                bool ifr = aircraft.variableSet != null && aircraft.variableSet.GetInteger(vuidIfr) != 0;
                LocalUsers.Add(new HubDirectory.HubUser(guid, false, nickname, 0, aircraft.Position.geo.z * (180.0 / Math.PI), aircraft.Position.geo.x * (180.0 / Math.PI), aircraft.Position.geo.y,
                    aircraft.netVelocity, aircraft.flightPlan, squawk, 0, 0, ifr, aircraft.Position.angles.y));
            }
        }

        /// <summary>Another node wants this hub's users, one message each.</summary>
        public void Handle(in MessageMeta meta, in UserListRequest request)
        {
            if (!session.Connected || !profile.Hub)
            {
                return;
            }
            foreach (HubDirectory.HubUser user in LocalUsers)
            {
                outbox.SendToEndPoint(meta.EndPoint, new HubUserUpdate
                {
                    Guid = user.guid,
                    Atc = user.atc,
                    Ifr = user.ifr,
                    Callsign = user.flightPlan.callsign,
                    Nickname = user.nickname,
                    Frequency = user.frequency,
                    Latitude = user.latitude,
                    Longitude = user.longitude,
                    Altitude = user.altitude,
                    Speed = user.speed,
                    Squawk = user.squawk,
                    Level = user.level,
                    Range = user.range,
                    Heading = user.heading,
                    IcaoType = user.flightPlan.icaoType,
                    Departure = user.flightPlan.departure,
                    Destination = user.flightPlan.destination,
                    Rules = user.flightPlan.rules,
                    Route = user.flightPlan.route,
                    Remarks = user.flightPlan.remarks,
                    Alternate = user.flightPlan.alternate,
                    FlightSpeed = user.flightPlan.speed,
                    FlightAltitude = user.flightPlan.altitude,
                    Registration = user.flightPlan.registration,
                    IcaoAirline = user.flightPlan.icaoAirline,
                    FlightNumber = user.flightPlan.flightNumber,
                });
            }
        }

        /// <summary>Another node wants this hub's users' positions.</summary>
        public void Handle(in MessageMeta meta, in UserPositionsRequest request)
        {
            if (!session.Connected || !profile.Hub)
            {
                return;
            }
            var positions = new UserPositions { Users = new List<UserPosition>(LocalUsers.Count) };
            foreach (HubDirectory.HubUser user in LocalUsers)
            {
                positions.Users.Add(new UserPosition
                {
                    Guid = user.guid,
                    Latitude = user.latitude,
                    Longitude = user.longitude,
                    Altitude = user.altitude,
                    Speed = user.speed,
                    Squawk = user.squawk,
                    Heading = user.heading,
                });
            }
            if (positions.Users.Count > 0) outbox.SendToEndPoint(meta.EndPoint, positions);
        }
    }
}
