using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JoinFS.Properties;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The Aircraft tab on the real traffic: the aircraft the old AircraftForm listed, with the details of its expanded line and the
    /// rules of its context menu. Reads the sim's snapshot (Sim.View) under the app's lock, as the form did; everything that changes an
    /// aircraft is posted to the sim thread through its Source, never done from here.
    ///
    /// The Objects tab is not live yet: its rows come from <paramref name="objects"/>.
    /// </summary>
    class LiveTrafficSource : ITrafficSource
    {
        readonly Main main;
        readonly ITrafficSource objects;

        // the variables an aircraft's radios and transponder are read from
        readonly uint vuidCom1 = VariableMgr.CreateVuid("com active frequency:1");
        readonly uint vuidCom2 = VariableMgr.CreateVuid("com active frequency:2");
        readonly uint vuidSquawk = VariableMgr.CreateVuid("transponder code:1");

        // the aircraft of the last read, by the id a row carries, so an action finds the same one the user clicked
        Dictionary<string, Sim.Aircraft> lastRead = [];

        public LiveTrafficSource(Main main, ITrafficSource objects)
        {
            this.main = main;
            this.objects = objects;
        }

        public IReadOnlyList<ObjectInfo> GetObjects() => objects.GetObjects();

        // ---- the list filters, which are the old window's settings ----

        public bool IncludeHubAircraft
        {
            get => Settings.Default.IncludeGlobalAircraft;
            set
            {
                Settings.Default.IncludeGlobalAircraft = value;
                Settings.Default.Save();
            }
        }

        public bool IncludeSimulatorAircraft
        {
            get => Settings.Default.IncludeSimulatorAircraft;
            set
            {
                Settings.Default.IncludeSimulatorAircraft = value;
                Settings.Default.Save();
            }
        }

        public bool InCockpit => main.sim?.View.EnteredAircraft != null;

        public bool IsTracking => main.sim != null && (main.sim.View.TrackHeadingObject != null || main.sim.View.TrackBearingObject != null);

        // ---- reading ----

        public IReadOnlyList<AircraftInfo> GetAircraft()
        {
            List<AircraftInfo> list = [];
            Dictionary<string, Sim.Aircraft> read = [];
            HashSet<Guid> listedUsers = [];

            lock (main.conch)
            {
                if (main.sim != null)
                {
                    SimSnapshot view = main.sim.View;
                    IReadOnlyList<Sim.Obj> all = view.Objects;
                    Sim.Pos userPosition = view.UserAircraft?.Position;

                    void Add(Sim.Aircraft aircraft)
                    {
                        string id = IdOf(aircraft);
                        // a network aircraft is listed once
                        if (read.ContainsKey(id))
                        {
                            return;
                        }
                        read[id] = aircraft;
                        listedUsers.Add(main.network.Peers.GetNodeGuid(aircraft.ownerNuid));
                        list.Add(Describe(aircraft, view, userPosition));
                    }

                    // the same order as the old list: you, the network, the recorder, then the simulator's own
                    foreach (Sim.Obj obj in all)
                    {
                        if (obj.owner == Sim.Obj.Owner.Me && obj is Sim.Aircraft mine)
                        {
                            Add(mine);
                        }
                    }
                    foreach (Sim.Obj obj in all)
                    {
                        if (obj is Sim.Aircraft aircraft && obj.owner == Sim.Obj.Owner.Network)
                        {
                            Add(aircraft);
                        }
                    }
                    foreach (Sim.Obj obj in all)
                    {
                        if (obj is Sim.Aircraft aircraft && obj.owner == Sim.Obj.Owner.Recorder)
                        {
                            Add(aircraft);
                        }
                    }
                    if (IncludeSimulatorAircraft)
                    {
                        foreach (Sim.Obj obj in all)
                        {
                            if (obj is Sim.Aircraft aircraft && obj.owner == Sim.Obj.Owner.Sim)
                            {
                                Add(aircraft);
                            }
                        }
                    }

                    if (IncludeHubAircraft)
                    {
                        // the users of the other hubs, who are not in the session
                        foreach (var hub in main.network.Hubs.List)
                        {
                            if (hub.endPoint.Equals(main.network.joinEndPoint))
                            {
                                continue;
                            }
                            foreach (var user in hub.userList)
                            {
                                if (listedUsers.Add(user.guid))
                                {
                                    list.Add(Describe(user, userPosition));
                                }
                            }
                        }
                    }
                }
            }

            lastRead = read;
            return list;
        }

        /// <summary>
        /// What names an aircraft to the UI, stable for as long as the aircraft is there: its owner and net id on the network, its sim id otherwise.
        /// </summary>
        static string IdOf(Sim.Aircraft aircraft) =>
            aircraft.ownerNuid.Valid() ? "net:" + aircraft.ownerNuid + "/" + aircraft.netId : "sim:" + aircraft.simId;

        AircraftInfo Describe(Sim.Aircraft aircraft, SimSnapshot view, Sim.Pos userPosition)
        {
            Sim.Pos position = aircraft.Position;

            double? distance = null;
            int? bearing = null;
            int? heading = null;
            int? altitude = null;
            if (position != null)
            {
                heading = Compass((int)(position.angles.y * 180.0 / Math.PI));
                altitude = (int)Math.Round(position.geo.y * Sim.FEET_PER_METRE);
                if (userPosition != null)
                {
                    distance = Vector.GeodesicDistance(position.geo.x, position.geo.z, userPosition.geo.x, userPosition.geo.z) * 0.00053995680346;
                    bearing = Compass((int)(Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, position.geo.x, position.geo.z) * 180.0 / Math.PI));
                }
            }

            double speed = Math.Sqrt(aircraft.netVelocity.linear.x * aircraft.netVelocity.linear.x + aircraft.netVelocity.linear.z * aircraft.netVelocity.linear.z) * 1.9438444925;

            // the owner, marked when the aircraft is not a person's: recorded (R) or AI (A)
            string owner = main.network.Peers.GetNodeName(aircraft.ownerNuid);
            if (aircraft.user == false)
            {
                owner += aircraft.owner == Sim.Obj.Owner.Recorder ? " (R)" : " (A)";
            }

            // com, squawk
            string com1 = "-", com2 = "-", squawk = "-";
            if (aircraft.variableSet != null)
            {
                com1 = aircraft.variableSet.GetFrequency(vuidCom1).ToString("F3", CultureInfo.InvariantCulture);
                com2 = aircraft.variableSet.GetFrequency(vuidCom2).ToString("F3", CultureInfo.InvariantCulture);
                squawk = aircraft.variableSet.GetInteger(vuidSquawk).ToString(CultureInfo.InvariantCulture);
            }

            bool ignored = aircraft.owner == Sim.Obj.Owner.Network
                ? main.log.IgnoreNode(aircraft.ownerNuid)
                : main.log.IgnoreName(aircraft.flightPlan.callsign);

            AircraftLinkState link = aircraft.Created ? AircraftLinkState.Created : aircraft.failed ? AircraftLinkState.Failed : AircraftLinkState.Pending;

            return new AircraftInfo(
                IdOf(aircraft), aircraft.flightPlan.callsign, owner, distance, heading, altitude, speed, ModelText(aircraft, view),
                bearing, squawk, com1, com2, main.network.Peers.GetNodeSimulator(aircraft.ownerNuid), aircraft.ownerModel,
                FlightPlanText(aircraft.flightPlan), Dash(aircraft.flightPlan.remarks), link,
                Recording: aircraft.record, Ignored: ignored, Tracked: IsTracked(aircraft, view), Can: Can(aircraft, view));
        }

        /// <summary>
        /// A user of another hub: no aircraft here, so no actions, and nothing the simulator could tell about it.
        /// </summary>
        AircraftInfo Describe(HubDirectory.HubUser user, Sim.Pos userPosition)
        {
            double? distance = null;
            int? bearing = null;
            if (userPosition != null)
            {
                double lon = user.longitude * (Math.PI / 180.0), lat = user.latitude * (Math.PI / 180.0);
                distance = Vector.GeodesicDistance(lon, lat, userPosition.geo.x, userPosition.geo.z) * 0.00053995680346;
                bearing = Compass((int)(Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, lon, lat) * 180.0 / Math.PI));
            }

            return new AircraftInfo(
                "hub:" + user.guid, user.flightPlan.callsign, user.nickname, distance, (int)user.heading, (int)user.altitude, user.speed,
                user.flightPlan.icaoType, bearing, user.squawk.ToString(CultureInfo.InvariantCulture), "-", "-", "", user.flightPlan.icaoType,
                FlightPlanText(user.flightPlan), Dash(user.flightPlan.remarks), AircraftLinkState.Created,
                Recording: false, Ignored: false, Tracked: false, Can: AircraftActions.None);
        }

        static int Compass(int degrees) => ((degrees % 360) + 360) % 360;

        static string Dash(string text) => string.IsNullOrWhiteSpace(text) ? "-" : text;

        /// <summary>
        /// The flight plan on one line: type, route end points, rules, then whatever else was filed.
        /// </summary>
        static string FlightPlanText(Sim.FlightPlan plan)
        {
            List<string> parts = [];
            void Add(string label, string value)
            {
                if (!string.IsNullOrWhiteSpace(value) && value != "-")
                {
                    parts.Add(label.Length > 0 ? label + " " + value : value);
                }
            }
            Add("", plan.icaoType);
            if (!string.IsNullOrWhiteSpace(plan.departure) || !string.IsNullOrWhiteSpace(plan.destination))
            {
                parts.Add(Dash(plan.departure) + " → " + Dash(plan.destination));
            }
            Add("", plan.rules);
            Add("alt", plan.alternate);
            Add("cruise", plan.speed);
            Add("level", plan.altitude);
            Add("", plan.route);
            return parts.Count > 0 ? string.Join(" · ", parts) : "No flight plan filed";
        }

        /// <summary>
        /// The model as the old list showed it: its title, its livery in FS2024, and how it was chosen as (S)ubstitute, (A)uto, (D)efault or (AI).
        /// Until the simulator is connected there is no model yet, only the type that was filed.
        /// </summary>
        static string ModelText(Sim.Aircraft aircraft, SimSnapshot view)
        {
            if (!view.Connected)
            {
                return aircraft.flightPlan.icaoType;
            }

            string model = aircraft.ModelTitle;
#if FS2024
            if (aircraft.ModelLivery.Length > 0)
            {
                model = aircraft.ModelTitle + " (" + aircraft.ModelLivery + ")";
            }
#endif
            return aircraft.subType switch
            {
                Substitution.Type.Substitute => model + " (S)",
                Substitution.Type.Auto => model + " (A)",
                Substitution.Type.Default => model + " (D)",
                Substitution.Type.AI => model + " (AI)",
                _ => model,
            };
        }

        /// <summary>
        /// Two snapshot copies of the same live object
        /// </summary>
        static bool Same(Sim.Obj a, Sim.Obj b) => a != null && b != null && a.Source == b.Source;

        static bool IsTracked(Sim.Aircraft aircraft, SimSnapshot view) =>
            Same(view.TrackHeadingObject, aircraft) || Same(view.TrackBearingObject, aircraft);

        /// <summary>
        /// What the old context menu enabled for this aircraft. The substitution and flight plan items are left off until those tabs are live.
        /// </summary>
        static AircraftActions Can(Sim.Aircraft aircraft, SimSnapshot view)
        {
            AircraftActions can = AircraftActions.None;

            // an aircraft the recorder plays cannot be recorded again
            if (aircraft.owner != Sim.Obj.Owner.Recorder)
            {
                can |= AircraftActions.Record;
            }

            // you cannot ignore yourself or the recorder, only other users and the simulator's own aircraft
            if (aircraft.owner == Sim.Obj.Owner.Network || aircraft.owner == Sim.Obj.Owner.Sim)
            {
                can |= AircraftActions.Ignore;
            }

            // nothing to do with another aircraft while in another cockpit
            if (view.Connected && view.EnteredAircraft == null)
            {
                if (aircraft.owner == Sim.Obj.Owner.Network && aircraft.metar.Length > 0)
                {
                    can |= AircraftActions.CopyWeather;
                }
                if (aircraft.owner != Sim.Obj.Owner.Me)
                {
                    can |= AircraftActions.Follow | AircraftActions.EnterCockpit | AircraftActions.Track;
                }
            }

            return can;
        }

        // ---- changing ----

        Sim.Aircraft Find(string aircraftId) => lastRead.TryGetValue(aircraftId, out Sim.Aircraft aircraft) ? aircraft : null;

        public void SetRecording(string aircraftId, bool recording)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft == null || aircraft.owner == Sim.Obj.Owner.Recorder)
            {
                return;
            }
            Sim.Obj live = aircraft.Source;
            main.SimCommand(sim => live.record = recording);
        }

        public void SetIgnored(string aircraftId, bool ignored)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft == null)
            {
                return;
            }

            lock (main.conch)
            {
                switch (aircraft.owner)
                {
                    case Sim.Obj.Owner.Network when aircraft.ownerNuid.Valid():
                        if (ignored) main.log.AddIgnoreNode(aircraft.ownerNuid); else main.log.RemoveIgnoreNode(aircraft.ownerNuid);
                        break;

                    case Sim.Obj.Owner.Sim:
                        if (ignored) main.log.AddIgnoreName(aircraft.flightPlan.callsign); else main.log.RemoveIgnoreName(aircraft.flightPlan.callsign);
                        break;
                }
            }
        }

        public void Follow(string aircraftId)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft != null)
            {
                Sim.Obj live = aircraft.Source;
                main.SimCommand(sim => { if (sim.IsLive(live)) sim.ScheduleFollow(live as Sim.Aircraft); });
            }
        }

        public void EnterCockpit(string aircraftId)
        {
            lock (main.conch)
            {
                // in another cockpit already: this leaves it
                if (main.sim != null && main.sim.View.EnteredAircraft != null)
                {
                    main.sim.ScheduleLeave();
                    return;
                }

                Sim.Aircraft aircraft = Find(aircraftId);
                if (aircraft == null)
                {
                    return;
                }
                if (aircraft.owner == Sim.Obj.Owner.Network && aircraft.CockpitShared == false)
                {
                    main.ShowMessage(Resources.Strings.NoPermissionCockpit);
                }
                else if (aircraft.owner != Sim.Obj.Owner.Me)
                {
                    Sim.Obj live = aircraft.Source;
                    main.SimCommand(sim => { if (sim.IsLive(live)) sim.ScheduleEnterAircraft(live as Sim.Aircraft); });
                }
            }
        }

        public void TrackHeading(string aircraftId)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (main.sim != null && aircraft != null)
            {
                Sim.Obj live = aircraft.Source;
                main.SimCommand(sim =>
                {
                    sim.trackHeadingObject = sim.IsLive(live) ? live : null;
                    sim.trackBearingObject = null;
                });
            }
        }

        public void TrackBearing(string aircraftId)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (main.sim != null && aircraft != null)
            {
                Sim.Obj live = aircraft.Source;
                main.SimCommand(sim =>
                {
                    sim.trackBearingObject = sim.IsLive(live) ? live : null;
                    sim.trackHeadingObject = null;
                });
            }
        }

        public void StopTracking()
        {
            if (main.sim != null)
            {
                main.SimCommand(sim =>
                {
                    sim.trackHeadingObject = null;
                    sim.trackBearingObject = null;
                });
            }
        }

        public void CopyWeather(string aircraftId)
        {
            lock (main.conch)
            {
                Sim.Aircraft aircraft = Find(aircraftId);
                if (aircraft != null)
                {
                    main.sim?.SetWeatherObservation(aircraft.metar);
                }
            }
        }
    }
}
