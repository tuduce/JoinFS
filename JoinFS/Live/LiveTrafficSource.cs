using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
    /// The Objects tab is read the same way, from the old ObjectsForm's list and its checkboxes.
    /// </summary>
    class LiveTrafficSource : ITrafficSource
    {
        readonly Main main;

        // the variables an aircraft's radios and transponder are read from
        readonly uint vuidCom1 = VariableMgr.CreateVuid("com active frequency:1");
        readonly uint vuidCom2 = VariableMgr.CreateVuid("com active frequency:2");
        readonly uint vuidSquawk = VariableMgr.CreateVuid("transponder code:1");

        // the aircraft of the last read, by the id a row carries, so an action finds the same one the user clicked
        Dictionary<string, Sim.Aircraft> lastRead = [];

        public LiveTrafficSource(Main main)
        {
            this.main = main;
        }

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
        /// What the old context menu enabled for this aircraft. Variables is left off until that is live.
        /// </summary>
        static AircraftActions Can(Sim.Aircraft aircraft, SimSnapshot view)
        {
            AircraftActions can = AircraftActions.None;

            // an aircraft the recorder plays cannot be recorded again
            if (aircraft.owner != Sim.Obj.Owner.Recorder)
            {
                can |= AircraftActions.Record;
            }

            if (view.Connected)
            {
                // a model can only be replaced by one the simulator has
                can |= AircraftActions.Substitute;

                // an aircraft that went through the matching can say how; your own never does, it is matched on request, as a preview
                if ((aircraft.subModel != null && aircraft.subTrace != null) || aircraft.owner == Sim.Obj.Owner.Me)
                {
                    can |= AircraftActions.ExplainMatch;
                }

                // only an aircraft JoinFS creates, with a model standing in for its owner's, has a height to adjust
                if (aircraft.Injected && aircraft.subModel != null)
                {
                    can |= AircraftActions.AdjustHeight;
                }
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

        public ModelTarget GetAircraftModel(string aircraftId)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft == null || aircraft.ownerModel.Length == 0)
            {
                return null;
            }

            // What an injected aircraft shows is the match of its owner's model. Your own and the simulator's are not injected:
            // changing the model of those changes what is sent for them, the masquerade.
#if FS2024
            return new ModelTarget(aircraft.ownerModel, aircraft.ownerLivery, aircraft.typerole, IsMasquerade: !aircraft.Injected);
#else
            return new ModelTarget(aircraft.ownerModel, "", aircraft.typerole, IsMasquerade: !aircraft.Injected);
#endif
        }

        public async Task<MatchExplanation> ExplainMatchAsync(string aircraftId)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft == null || main.substitution == null)
            {
                return null;
            }

            if (aircraft.owner == Sim.Obj.Owner.Me)
            {
                // Your own aircraft never goes through Match() for real (Masquerade() drives what is sent), so the real scorer is run now, purely
                // as a preview. It never touches subModel, subType or subTrace, so it cannot change what the others see.
#if FS2024
                var (model, type, trace) = await main.substitution.Match(aircraft.ownerModel, aircraft.ownerLivery, aircraft.ownerIcaoType, aircraft.ownerIcaoAirline, aircraft.ownerClassCode, aircraft.ownerWtc, aircraft.ownerClassCodeConfirmed, aircraft.typerole, aircraft.flightPlan.registration);
#else
                var (model, type, trace) = await main.substitution.Match(aircraft.ownerModel, aircraft.ownerIcaoType, aircraft.ownerIcaoAirline, aircraft.ownerClassCode, aircraft.ownerWtc, aircraft.ownerClassCodeConfirmed, aircraft.typerole, aircraft.flightPlan.registration);
#endif
                return LiveMatchExplanation.Build(main, aircraft, model, type, trace);
            }

            if (aircraft.subTrace != null)
            {
                return LiveMatchExplanation.Build(main, aircraft, aircraft.subModel, aircraft.subType, aircraft.subTrace);
            }
            return null;
        }

        public HeightAdjustment GetHeightAdjustment(string aircraftId)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft == null || main.sim == null || !aircraft.Injected || aircraft.subModel == null)
            {
                return null;
            }

            lock (main.conch)
            {
                return new HeightAdjustment(aircraft.subModel.longType, main.sim.GetHeightAdjustment(aircraft.subModel));
            }
        }

        public void SetHeightAdjustment(string aircraftId, int centimetres)
        {
            Sim.Aircraft aircraft = Find(aircraftId);
            if (aircraft == null || main.sim == null || !aircraft.Injected || aircraft.subModel == null)
            {
                return;
            }

            lock (main.conch)
            {
                // the store is kept per model and read by the sim thread as it places the aircraft
                main.sim.UpdateHeightAdjustment(aircraft.subModel, centimetres);
            }
            main.ScheduleHeightAdjustmentSave();
        }

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

        // ---- objects ----

        // the objects of the last read, by the id a row carries
        Dictionary<string, Sim.Obj> lastObjects = [];

        public bool GroupObjects
        {
            get => Settings.Default.GroupObjects;
            set
            {
                Settings.Default.GroupObjects = value;
                Settings.Default.Save();
            }
        }

        public IReadOnlyList<ObjectInfo> GetObjects()
        {
            List<ObjectInfo> list = [];
            Dictionary<string, Sim.Obj> read = [];

            lock (main.conch)
            {
                if (main.sim != null)
                {
                    SimSnapshot view = main.sim.View;
                    Sim.Pos userPosition = view.UserAircraft?.Position;

                    if (GroupObjects)
                    {
                        // one row per owner and model, with how many there are
                        Dictionary<string, (Sim.Obj First, int Count)> groups = [];
                        foreach (Sim.Obj obj in view.Objects)
                        {
                            if (obj is Sim.Aircraft)
                            {
                                continue;
                            }
                            string key = obj.ownerNuid.ToString() + " " + obj.ownerModel;
                            groups[key] = groups.TryGetValue(key, out var group) ? (group.First, group.Count + 1) : (obj, 1);
                        }
                        foreach (var group in groups)
                        {
                            string id = "group:" + group.Key;
                            read[id] = group.Value.First;
                            list.Add(Describe(id, group.Value.First, group.Value.Count, view, userPosition));
                        }
                    }
                    else
                    {
                        foreach (Sim.Obj obj in view.Objects)
                        {
                            if (obj is Sim.Aircraft)
                            {
                                continue;
                            }
                            string id = IdOf(obj);
                            if (read.TryAdd(id, obj))
                            {
                                list.Add(Describe(id, obj, 1, view, userPosition));
                            }
                        }
                    }
                }
            }

            lastObjects = read;
            return list;
        }

        /// <summary>
        /// What names an object to the UI: its owner and net id on the network, its sim id otherwise.
        /// </summary>
        static string IdOf(Sim.Obj obj) =>
            obj.ownerNuid.Valid() ? "net:" + obj.ownerNuid + "/" + obj.netId : "sim:" + obj.simId;

        /// <summary>
        /// The owner, as the old Objects list named it
        /// </summary>
        string OwnerName(Sim.Obj obj) => obj.owner switch
        {
            Sim.Obj.Owner.Me or Sim.Obj.Owner.Sim => main.settingsNickname,
            Sim.Obj.Owner.Network => main.network.Peers.GetNodeName(obj.ownerNuid),
            Sim.Obj.Owner.Recorder => Resources.Strings.RecorderStr,
            _ => "",
        };

        ObjectInfo Describe(string id, Sim.Obj obj, int count, SimSnapshot view, Sim.Pos userPosition)
        {
            bool network = obj.owner == Sim.Obj.Owner.Network;
            bool ignoreNode = network && main.log.IgnoreNode(obj.ownerNuid);
            bool ignoreModel = network && main.log.IgnoreName(obj.ownerModel);

            // the model, marked with how it was chosen when it is someone else's
            string model = obj.ModelTitle;
            if (obj.ownerNuid.Valid())
            {
                model += obj.subType switch
                {
                    Substitution.Type.Substitute => " (S)",
                    Substitution.Type.Auto => " (A)",
                    Substitution.Type.Default => " (D)",
                    _ => "",
                };
            }

            // where it is, only for a single object
            double? distance = null;
            int? bearing = null;
            Sim.Pos position = obj.Position;
            if (count == 1 && userPosition != null && position != null)
            {
                distance = Vector.GeodesicDistance(position.geo.x, position.geo.z, userPosition.geo.x, userPosition.geo.z) * 0.00053995680346;
                bearing = Compass((int)(Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, position.geo.x, position.geo.z) * 180.0 / Math.PI));
            }

            // a group is broadcast when its model is, or it is a TacPack model and those are
            bool modelBroadcast = main.log.BroadcastName(obj.ownerModel);
            bool broadcast = count == 1 && !GroupObjects
                ? main.sim != null && main.sim.IsBroadcast(obj)
                : modelBroadcast || Settings.Default.BroadcastTacpack && Sim.IsTacpackModel(obj.ownerModel);

            return new ObjectInfo(
                id, OwnerName(obj), model, obj.ownerModel, count, bearing, distance, broadcast, ignoreNode, ignoreModel, modelBroadcast,
                CanBroadcast: !network, CanIgnore: network, CanSubstitute: network);
        }

        Sim.Obj FindObject(string objectId) => lastObjects.TryGetValue(objectId, out Sim.Obj obj) ? obj : null;

        public ModelTarget GetObjectModel(string objectId)
        {
            Sim.Obj obj = FindObject(objectId);
            if (obj == null || obj.ownerModel.Length == 0)
            {
                return null;
            }

            // objects of the network only, as the old window's button: their match is what is changed
#if FS2024
            return new ModelTarget(obj.ownerModel, obj.ownerLivery, obj.typerole);
#else
            return new ModelTarget(obj.ownerModel, "", obj.typerole);
#endif
        }

        public void SetObjectBroadcast(string objectId, bool broadcast)
        {
            Sim.Obj obj = FindObject(objectId);
            // your own objects only, one at a time
            if (obj == null || obj.owner == Sim.Obj.Owner.Network || GroupObjects)
            {
                return;
            }
            Sim.Obj live = obj.Source;
            main.SimCommand(sim => live.broadcast = broadcast);
        }

        public void SetModelBroadcast(string originalModel, bool broadcast)
        {
            lock (main.conch)
            {
                if (broadcast)
                {
                    main.log.AddBroadcastName(originalModel);
                }
                else
                {
                    main.log.RemoveBroadcastName(originalModel);
                }
            }
        }

        public void SetIgnoreOwner(string objectId, bool ignored)
        {
            Sim.Obj obj = FindObject(objectId);
            if (obj == null || obj.owner != Sim.Obj.Owner.Network)
            {
                return;
            }
            lock (main.conch)
            {
                if (ignored) main.log.AddIgnoreNode(obj.ownerNuid); else main.log.RemoveIgnoreNode(obj.ownerNuid);
            }
        }

        public void SetIgnoreModel(string objectId, bool ignored)
        {
            Sim.Obj obj = FindObject(objectId);
            if (obj == null || obj.owner != Sim.Obj.Owner.Network)
            {
                return;
            }
            lock (main.conch)
            {
                if (ignored) main.log.AddIgnoreName(obj.ownerModel); else main.log.RemoveIgnoreName(obj.ownerModel);
            }
        }
    }
}
