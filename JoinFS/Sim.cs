using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.Net;




#if SIMCONNECT
#if P3D
//using LockheedMartin.Prepar3D.SimConnect;
using Microsoft.FlightSimulator.SimConnect;
#else
using Microsoft.FlightSimulator.SimConnect;
#endif
#endif

namespace JoinFS
{
    public partial class Sim
    {
#if DEBUG
        const float OBJECT_EXPIRE_TIME = 30.0f;
#else
        const float OBJECT_EXPIRE_TIME = 10.0f;
#endif
        const float NEW_OBJECT_EXPIRE_TIME = 60.0f;

        public const double TIME_ERROR_RATE = 0.02;
        public const double FEET_PER_METRE = 3.28084;
        public const double METRES_PER_FOOT = 0.3048;
        /// <summary>How long the sender's raw "SIM ON GROUND" bit must hold its current value before trustingPlatformGround follows it - see Aircraft.pendingGroundFlag.</summary>
        const double GroundTrustDebounceSeconds = 0.3;

        /// <summary>
        /// Positions go out to the network and the recorder at most this often (seconds) - the rate
        /// they were polled at before the simulator fed them every frame
        /// </summary>
        public const double PositionSendInterval = 0.05;

        /// <summary>
        /// Whether a position received now is due out to the network and the recorder. Keeps an
        /// average of one per <see cref="PositionSendInterval"/>, on the first report after each
        /// interval; after a gap it restarts from now instead of catching up.
        /// </summary>
        internal static bool SendDue(Obj obj, double now)
        {
            if (now < obj.nextSendTime)
            {
                return false;
            }
            obj.nextSendTime += PositionSendInterval;
            if (obj.nextSendTime <= now)
            {
                obj.nextSendTime = now + PositionSendInterval;
            }
            return true;
        }

        /// <summary>
        /// Reference to the main form
        /// </summary>
        readonly Main main;

        /// <summary>
        /// List of objects
        /// </summary>
        public List<Obj> objectList = [];

        /// <summary>
        /// Index of the network and recorder objects in <see cref="objectList"/> by (ownerNuid,
        /// netId), the network identity of an object - both fields, and the owner, are set once at
        /// construction and never reassigned, so this stays in sync purely by being updated wherever
        /// objectList is added to/removed from. Avoids an O(N) List.Find (plus a per-call closure
        /// allocation) on the network-message-receive path.
        ///
        /// Simulator objects (Owner.Me/Sim) are not indexed: like recorder objects they have no
        /// owner node, and their netId is the simulator's id, so they could share a recorder
        /// object's key. Nothing looks them up by network identity.
        /// </summary>
        readonly Dictionary<(NodeId, uint), Obj> objectsByOwnerNetId = [];

        /// <summary>
        /// The network or recorder object owned by <paramref name="ownerNuid"/> with network id <paramref name="netId"/>, or null
        /// </summary>
        public Obj FindObject(NodeId ownerNuid, uint netId) => objectsByOwnerNetId.GetValueOrDefault((ownerNuid, netId));

        /// <summary>
        /// Add an object to <see cref="objectList"/>, keeping <see cref="objectsByOwnerNetId"/> in sync.
        /// The only way an object should be added to the list.
        /// </summary>
        void AddObjectToList(Obj obj)
        {
            objectList.Add(obj);
            if (obj.owner == Obj.Owner.Network || obj.owner == Obj.Owner.Recorder)
            {
                objectsByOwnerNetId[(obj.ownerNuid, obj.netId)] = obj;
            }
        }

        /// <summary>
        /// Remove an object from <see cref="objectList"/>, and its own entry (only) from <see cref="objectsByOwnerNetId"/>
        /// </summary>
        void RemoveFromListAndIndex(Obj obj)
        {
            objectList.Remove(obj);
            if (objectsByOwnerNetId.TryGetValue((obj.ownerNuid, obj.netId), out Obj indexed) && indexed == obj)
            {
                objectsByOwnerNetId.Remove((obj.ownerNuid, obj.netId));
            }
        }

        // create remove object list
        readonly List<Obj> removeList = [];

        /// <summary>
        /// Current object being created in simconnect
        /// </summary>
        Obj creatingObject = null;

#if XPLANE || SIMCONNECT || CONSOLE
        /// <summary>
        /// Expire time of new object
        /// </summary>
        double creatingObjectExpireTime = 0.0;
#endif

        /// <summary>
        /// Current user aircraft
        /// </summary>
        public Aircraft userAircraft = null;

        /// <summary>
        /// Currently entered aircraft
        /// </summary>
        public Aircraft enteredAircraft;

        /// <summary>
        /// Scheduled enter
        /// </summary>
        volatile Aircraft enterAircraft;

        /// <summary>
        /// Aircraft used to update the weather from
        /// </summary>
        public Aircraft weatherAircraft;

        /// <summary>
        /// Schedule remove objects
        /// </summary>
        volatile bool scheduleRemoveObjects = false;

        /// <summary>
        /// Schedule remove
        /// </summary>
        volatile string scheduleRemove = null;

        /// <summary>
        /// scheduled follow aircraft
        /// </summary>
        volatile Aircraft followAircraft = null;

        /// <summary>
        /// Scheduled leave
        /// </summary>
        volatile bool leaveAircraft = false;

        /// <summary>
        /// Current weather METAR
        /// </summary>
        public volatile string scheduleMetar = null;

        /// <summary>
        /// Close requested from inside a simulator callback (lost connection, quit)
        /// </summary>
        volatile bool scheduleClose = false;

        /// <summary>
        /// Timers
        /// </summary>
        readonly Timer objectProcessTimer = new(0.1);
        readonly Timer requestInfoTimer = new(2.0);
        readonly Timer requestPositionTimer = new(0.05);
        readonly Timer requestWeatherTimer = new(60.0);
        readonly Timer requestLocalStateTimer = new(20.0);
        readonly Timer trackingTimer = new(1.0);
        readonly Timer variablesTimer = new(0.2);
        readonly Timer flightPlanTimer = new(5.0);

        readonly Timer updateIntervalsTimer = new(5.0);

        // get heading vuid
        readonly uint headingVuid = VariableMgr.CreateVuid("sim/cockpit/autopilot/heading_mag");

        /// <summary>
        /// Object to be tracked
        /// </summary>
        public Obj trackHeadingObject;
        public Obj trackBearingObject;

        /// <summary>
        /// count of DoWork()
        /// </summary>
        int workCount = 0;

        /// <summary>
        /// Session peer ids for this tick, refreshed once at the top of <see cref="DoWork"/>
        /// instead of once per broadcasting aircraft in <see cref="ProcessAircraftPosition"/> -
        /// tolerates being up to one tick stale, same as <see cref="Network.Snapshot"/> itself.
        /// </summary>
        NodeId[] tickPeerIds = [];

        /// <summary>
        /// Each node's user aircraft (the one with <see cref="Aircraft.user"/> set) for this tick,
        /// refreshed once at the top of <see cref="DoWork"/> instead of an O(objectList) Find per
        /// peer per broadcasting aircraft in <see cref="ProcessAircraftPosition"/>.
        /// </summary>
        readonly Dictionary<NodeId, Aircraft> tickUserAircraftByNode = [];

        /// <summary>
        /// Timers
        /// </summary>
        readonly Timer checkConnectionTimer = new(20.0);

        // track previous connected state so we can detect transitions
        bool previousConnected = false;

        // whether we've already attempted auto-network-join for the current simulator connection
        bool autoNetworkJoinAttempted = false;

        /// <summary>
        /// Connection attempts
        /// </summary>
        const int CHECK_CONNECTION_ATTEMPTS = 6;
        int checkConnectionCount = 0;

#if SIMCONNECT
        /// <summary>
        /// SimConnect interface
        /// </summary>
        SimConnectInterface simconnect;
#endif

#if XPLANE || CONSOLE
        /// <summary>
        /// X-Plane interface
        /// </summary>
        public XPlane xplane;
#endif

        /// <summary>
        /// Is a simulator currently connected
        /// </summary>
#if XPLANE || CONSOLE
        public bool Connected { get { return xplane.IsConnected; } }
#elif SIMCONNECT
        public bool Connected { get { return simconnect != null; } }
#else
        public bool Connected { get { return false; } }
#endif

        /// <summary>
        /// Signalled when the simulator has messages waiting (null when there is nothing to wait on)
        /// </summary>
#if SIMCONNECT
        public System.Threading.WaitHandle MessageEvent => simconnect?.MessageEvent;
#else
        public System.Threading.WaitHandle MessageEvent => null;
#endif

        /// <summary>
        /// Periodic work, polled by DoWork
        /// </summary>
        Timer[] scheduledTimers;

        /// <summary>
        /// When DoWork next has timed work to do (ElapsedTime). The sim thread sleeps until then
        /// unless the simulator, a post or a datagram wakes it first.
        /// </summary>
        public double NextDue(double now)
        {
            scheduledTimers ??= [objectProcessTimer, requestInfoTimer, requestPositionTimer, requestWeatherTimer, trackingTimer, variablesTimer, flightPlanTimer, updateIntervalsTimer];
            double next = double.MaxValue;
            foreach (Timer timer in scheduledTimers)
            {
                next = Math.Min(next, timer.Due);
            }
            if (checkConnectionCount < CHECK_CONNECTION_ATTEMPTS)
            {
                next = Math.Min(next, checkConnectionTimer.Due);
            }
            if (creatingObject != null)
            {
                next = Math.Min(next, creatingObjectExpireTime);
            }
#if XPLANE || CONSOLE
            next = Math.Min(next, xplane.NextDue());
#endif
            return next;
        }

        /// <summary>
        /// Run DoWork on the sim thread now, for work scheduled from another thread
        /// </summary>
        void WakeSimThread()
        {
            main.simService?.Wake();
        }

        /// <summary>
        /// Simulator details
        /// </summary>
        string simulatorName = "";
        string simulatorVersion = "0";

        /// <summary>
        /// Get simulator name
        /// </summary>
        /// <returns></returns>
        public string GetSimulatorName()
        {
#if XPLANE || CONSOLE
            return (Connected && main.settingsXplane) ? "X-Plane" : Resources.Strings.NotConnected;
#else
            return (Connected && simulatorName != "") ? simulatorName : Resources.Strings.NotConnected;
#endif
        }

        /// <summary>
        /// Get simulator version
        /// </summary>
        /// <returns></returns>
        public string GetSimulatorVersion()
        {
            return simulatorVersion;
        }

#if !CONSOLE
        [DllImport("winmm")]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        static extern int timeBeginPeriod(uint uPeriod);

        [DllImport("winmm")]
        static extern int timeEndPeriod(uint uPeriod);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
#endif

        /// <summary>
        /// Constructor
        /// </summary>
        public Sim(Main main)
        {
            // set main form
            this.main = main;

            // per-model/per-simulator stores
            heightAdjustmentStore = new HeightAdjustmentStore(main, () => Connected, GetSimulatorName);
            modelVariableStore = new ModelVariableStore(main);

#if XPLANE || CONSOLE
            // create xplane link
            xplane = new XPlane(main)
            {
                modelNotify = XPlaneModelUpdate,
                aircraftPositionNotify = ProcessAircraftPosition,
                connectedNotify = XPlaneConnected,
                removeNotify = XPlaneRemove
            };
#endif

#if !CONSOLE
            int v = timeBeginPeriod(1);
#endif

            // initialize timers
            requestInfoTimer.Elapsed(main.ElapsedTime);
            requestPositionTimer.Elapsed(main.ElapsedTime);
            requestWeatherTimer.Elapsed(main.ElapsedTime);
            trackingTimer.Elapsed(main.ElapsedTime);
            updateIntervalsTimer.Elapsed(main.ElapsedTime);

            // set connection
            checkConnectionCount = main.settingsConnectOnLaunch ? 0 : CHECK_CONNECTION_ATTEMPTS;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~Sim()
        {
#if !CONSOLE
            int v = timeEndPeriod(1);
#endif
        }

        /// <summary>
        /// Process module
        /// </summary>
        public void DoWork()
        {
            RefreshTickCaches();
            ProcessScheduledWork();
            PollConnectionTimer();
            PumpSimMessages();

            var (userLatitude, userLongitude, activityCircle) = ComputeUserLocation();
            UpdateCreatingObject(userLatitude, userLongitude, activityCircle);

            // get elapsed time
            double time = main.ElapsedTime;

            UpdateRemoteControl();
            ProcessObjectList(time, userLatitude, userLongitude, activityCircle);
            ProcessTimedRequests(time);
            RebuildIntervalMasks(time);
            ProcessTracking(time);
            BroadcastObjectVariables(time);
            BroadcastFlightPlans(time);

#if XPLANE || CONSOLE
            // process xplane
            xplane.DoWork();
#endif

            // increment count
            workCount++;

            TryAutoNetworkJoin();
        }

        /// <summary>
        /// Refresh <see cref="tickPeerIds"/> and <see cref="tickUserAircraftByNode"/> for this
        /// tick. Done first, before <see cref="PumpSimMessages"/>/<c>xplane.DoWork</c> can call
        /// <see cref="ProcessAircraftPosition"/> (SimConnect and X-Plane fire it from different
        /// points within DoWork), so both are ready however this tick's position updates arrive.
        /// </summary>
        void RefreshTickCaches()
        {
            tickPeerIds = main.network.PeerIds();

            tickUserAircraftByNode.Clear();
            foreach (var obj in objectList)
            {
                if (obj is Aircraft aircraft && aircraft.user)
                {
                    tickUserAircraftByNode[aircraft.ownerNuid] = aircraft;
                }
            }
        }

        /// <summary>
        /// Scheduled work posted from another thread (ScheduleFollow, ScheduleEnterAircraft,
        /// ScheduleLeave, SetWeatherObservation, ScheduleRemoveModel, ScheduleRemoveObjects)
        /// </summary>
        void ProcessScheduledWork()
        {
            // check for scheduled weather change
            if (scheduleMetar != null)
            {
#if SIMCONNECT
                // check if connected
                if (simconnect != null)
                {
                    // set weather
                    simconnect.SetWeather(scheduleMetar);
                    // display message
                    main.MonitorEvent("METAR Update '" + scheduleMetar + "'");
                }
#endif
                // reset
                scheduleMetar = null;
            }

            // check for scheduled follow
            if (followAircraft != null)
            {
                // follow aircraft
                FollowAircraft(followAircraft);
                // reset
                followAircraft = null;
            }

            // check for scheduled enter
            if (enterAircraft != null)
            {
                // enter aircraft
                EnterAircraft(enterAircraft);
                // reset
                enterAircraft = null;
            }

            // check for scheduled leave
            if (leaveAircraft)
            {
                // reset
                leaveAircraft = false;
                // leave aircraft
                LeaveAircraft();
            }

            // check for scheduled remove
            if (scheduleRemove != null)
            {
                // do remove
                RemoveObjectsFromSim(scheduleRemove);
                // reset
                scheduleRemove = null;
            }

            // check for scheduled remove
            if (scheduleRemoveObjects)
            {
                // do remove
                RemoveObjects();
                // reset
                scheduleRemoveObjects = false;
            }
        }

        /// <summary>
        /// Check connection to the simulator, once per checkConnectionTimer interval, up to
        /// CHECK_CONNECTION_ATTEMPTS
        /// </summary>
        void PollConnectionTimer()
        {
            // check connection
            if (checkConnectionTimer.Elapsed(main.ElapsedTime) && checkConnectionCount < CHECK_CONNECTION_ATTEMPTS)
            {
                // check connection
                CheckConnection();
                // update attempts
                checkConnectionCount++;
            }
        }

        /// <summary>
        /// Pull queued SimConnect messages (dispatched into Sim's callbacks on this thread - see
        /// docs/sim-thread-architecture.md), then either close a connection a callback asked to
        /// close, or steer objects once for the frames just received
        /// </summary>
        void PumpSimMessages()
        {
#if SIMCONNECT
            // process messages
            simconnect?.ReceiveMsg();
#endif

            // check for a close requested by a callback (once: calls made while closing a broken
            // connection can ask for another)
            if (scheduleClose)
            {
                scheduleClose = false;
                if (Connected)
                {
                    Close();
                }
            }
#if SIMCONNECT
            else
            {
                // steer objects once for the frames just received
                ProcessFrame();
            }
#endif
        }

        /// <summary>
        /// The user's (or, in ATC mode with no user aircraft, the configured airport's) location,
        /// used to gate object creation and activity-circle expiry this tick
        /// </summary>
        (double userLatitude, double userLongitude, double activityCircle) ComputeUserLocation()
        {
            // default user location
            double userLatitude = 0.0;
            double userLongitude = 0.0;
            // calculate activity distance
            double activityCircle = main.settingsActivityCircle;

            // check for user aircraft
            if (userAircraft != null)
            {
                // set user location from the aircraft
                userLatitude = userAircraft.simPosition.geo.z;
                userLongitude = userAircraft.simPosition.geo.x;
            }
            else if (objectList.Count > 0)
            {
                // set user location from the aircraft
                userLatitude = objectList[0].netPosition.geo.z;
                userLongitude = objectList[0].netPosition.geo.x;
            }

            // check for ATC mode and no user aircraft
            if (main.settingsAtc && userAircraft == null)
            {
                // get airport code
                string code = main.settingsAtcAirport;
                // check if airport is listed
                if (main.airportList.TryGetValue(code, out var airport))
                {
                    // convert to radians
                    userLatitude = Math.Min(90.0, Math.Max(-90.0, airport.latitude)) * (Math.PI / 180.0);
                    userLongitude = Math.Min(180.0, Math.Max(-180.0, airport.longitude)) * (Math.PI / 180.0);
                }
            }

            return (userLatitude, userLongitude, activityCircle);
        }

        /// <summary>Maximum number of times an injection failure is retried before giving up on an object for good.</summary>
        internal const int FAILED_RETRY_MAX = 30;


        /// <summary>
        /// Whether an object may be (re-)injected: it never failed, or its last failure is more than
        /// <paramref name="retrySeconds"/> ago and it has failed fewer than <see cref="FAILED_RETRY_MAX"/> times.
        /// </summary>
        internal static bool InjectionRetryEligible(Obj obj, double now, double retrySeconds)
        {
            return obj.failed == false || (now - obj.failedTime > retrySeconds && obj.failedCount < FAILED_RETRY_MAX);
        }

        /// <summary>
        /// Clear latched injection-failure state on every injected object so the finder retries them
        /// immediately. Called on a SimConnect OPEN (ProcessOpen) and on a SimStart event.
        /// </summary>
        void RearmFailedInjections(string reason)
        {
            int count = 0;
            foreach (var obj in objectList)
            {
                if (obj.Injected && (obj.failed || obj.failedCount > 0))
                {
                    obj.failed = false;
                    obj.failedTime = 0.0;
                    obj.failedCount = 0;
                    count++;
                }
            }
            if (count > 0)
            {
                main.MonitorEvent("Re-armed " + count + " failed injection(s) (" + reason + ")");
            }
        }

        /// <summary>
        /// Inject (SimConnect) or spawn (X-Plane) the next eligible object one at a time, and time
        /// out an injection that never got a response
        /// </summary>
        void UpdateCreatingObject(double userLatitude, double userLongitude, double activityCircle)
        {
#if XPLANE || SIMCONNECT || CONSOLE
            // check for new object being created
            if (creatingObject != null)
            {
                // check if new object has expired
                if (main.ElapsedTime > creatingObjectExpireTime)
                {
                    // check for aircraft
                    if (creatingObject is Aircraft)
                    {
                        // aircraft
                        Aircraft aircraft = creatingObject as Aircraft;
                        // message
                        main.MonitorEvent("ERROR - No response injecting aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - Sub '" + creatingObject.ModelTitle + "'");
                    }
                    else
                    {
                        // message
                        main.MonitorEvent("ERROR - No response injecting object - User '" + ((creatingObject.owner == Obj.Owner.Network) ? creatingObject.ownerNuid.ToString() : "Me") + "' - Sub '" + creatingObject.ModelTitle + "'");
                    }
                    // remove object
                    RemoveObject(creatingObject);
                    // no longer creating object
                    creatingObject = null;
                }
            }
            else if (Connected)
            {
                // find object that needs creating (plain loop, not Find(lambda), so this doesn't
                // allocate a closure every tick - it captures activityCircle, a per-call parameter).
                // A prior injection failure (SimConnect exception 22 - usually the simulator still
                // loading) no longer bars an object forever: it's eligible again once
                // settingsInjectionRetrySeconds have passed, up to FAILED_RETRY_MAX attempts.
                creatingObject = null;
                foreach (var o in objectList)
                {
                    if (o.owner != Obj.Owner.Me && o.Created == false && InjectionRetryEligible(o, main.ElapsedTime, main.settingsInjectionRetrySeconds) && main.log.IgnoreNode(o.ownerNuid) == false && main.log.IgnoreName(o.ownerModel) == false && o != enteredAircraft && o.distance * 0.00053995680346 < activityCircle)
                    {
                        creatingObject = o;
                        break;
                    }
                }

                // clear a re-armed failure flag so this attempt starts clean
                if (creatingObject != null && creatingObject.failed)
                {
                    creatingObject.failed = false;
                    main.MonitorEvent("Retrying injection (attempt " + (creatingObject.failedCount + 1) + ") - User '" + ((creatingObject.owner == Obj.Owner.Network) ? creatingObject.ownerNuid.ToString() : "Me") + "' - Sub '" + creatingObject.ModelTitle + "'");
                }

                // check for object
                if (creatingObject != null)
                {
#if XPLANE || CONSOLE
                    // set timer
                    creatingObjectExpireTime = main.ElapsedTime + NEW_OBJECT_EXPIRE_TIME;
                    // inject aircraft into xplane
                    creatingObject.simId = xplane.InjectAircraft(creatingObject.distance);
                    // check if injection successful
                    if (creatingObject.simId != uint.MaxValue)
                    {
                        // update model
                        UpdateObject(creatingObject, creatingObject.ownerModel, creatingObject.ownerLivery, creatingObject.ownerIcaoType, creatingObject.ownerIcaoAirline, creatingObject.ownerClassCode, creatingObject.ownerWtc, creatingObject.ownerClassCodeConfirmed, creatingObject.typerole);
                        // create variables
                        CreateModelVariables(creatingObject);
                        // show event
                        LogInjecting(creatingObject);
                    }
                    // finished injection
                    creatingObject = null;
#elif SIMCONNECT
                    // check for simconnect
                    if (simconnect != null)
                    {
                        // set timer
                        creatingObjectExpireTime = main.ElapsedTime + NEW_OBJECT_EXPIRE_TIME;
                        // update model
                        UpdateObject(creatingObject, creatingObject.ownerModel, creatingObject.ownerLivery, creatingObject.ownerIcaoType, creatingObject.ownerIcaoAirline, creatingObject.ownerClassCode, creatingObject.ownerWtc, creatingObject.ownerClassCodeConfirmed, creatingObject.typerole);
                        // show event
                        LogInjecting(creatingObject);

                        simconnect.CreateObject(creatingObject);
                    }
#endif
                    }
            }
#endif // XPLANE || SIMCONNECT
        }

        /// <summary>
        /// Log an object about to be injected/created in the simulator - shared by the X-Plane
        /// (synchronous) and SimConnect (async, awaiting ProcessAssignedObjectId) injection paths,
        /// which used to carry an identical copy of this message each
        /// </summary>
        void LogInjecting(Obj obj)
        {
            if (obj is Aircraft aircraft)
            {
                main.MonitorEvent("Injecting aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "' - Sub '" + obj.ModelTitle + "'");
            }
            else
            {
                main.MonitorEvent("Injecting object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "' - Sub '" + obj.ModelTitle + "'");
            }
        }

        /// <summary>
        /// Update whether the user aircraft is under remote control (shared cockpit, or another
        /// node's rebroadcast), and reset its network positioning when it just lost that control
        /// </summary>
        void UpdateRemoteControl()
        {
            // update remote control
            if (userAircraft != null)
            {
                // new control
                bool remoteFlightControl;

                // check if entered another aircraft
                if (enteredAircraft != null)
                {
                    // get current remote flight control
                    remoteFlightControl = enteredAircraft.FlightControlsShared == false;
                }
                else
                {
                    // update remote control states
                    remoteFlightControl = main.network.Peers.shareFlightControls.Valid();
                }

                // check if losing flight control
                if (userAircraft.remoteFlightControl == false && remoteFlightControl)
                {
                    // reset network data
                    ResetObject(userAircraft);
                }

                // update remote control states
                userAircraft.remoteFlightControl = remoteFlightControl;
            }
        }

        /// <summary>
        /// Once per objectProcessTimer interval: expire stale objects, and for the rest, take
        /// control of objects the simulator handed back, update distance, drop injected objects that
        /// left the activity circle, and hide aircraft the log is set to ignore
        /// </summary>
        void ProcessObjectList(double time, double userLatitude, double userLongitude, double activityCircle)
        {
            // object process
            if (objectProcessTimer.Elapsed(time))
            {
                // for each object
                foreach (var obj in objectList)
                {
                    // check if object has expired
                    if (obj.owner != Obj.Owner.Me && time > obj.expireTime)
                    {
                        // add to remove list
                        removeList.Add(obj);
                    }
                    else
                    {
#if SIMCONNECT
                        // take control
                        if (simconnect != null && obj.takeControl)
                        {
                            // reset
                            obj.takeControl = false;
                            // take control of the object
                            simconnect.ReleaseControl(obj.simId, Requests.RELEASE_AI);

                            // check for aircraft and ATC mode
                            if (obj is Aircraft && main.settingsAtc)
                            {
                                // set waypoint
                                simconnect.SetWaypoint(obj.simId);
                                // update ATC ID
                                SetAtcId(obj as Aircraft);
                            }
                        }

//                        // check for no aerobatics
//                        if (Math.Abs(obj.simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(obj.simPosition.angles.z) < Math.PI * 0.5)
//                        {
//                            // update object velocity
//                            UpdateSimObjectVelocity(obj);
//                        }
#endif

                        // update object distance
                        obj.distance = Vector.GeodesicDistance(obj.netPosition.geo.x, obj.netPosition.geo.z, userLongitude, userLatitude);

                        // if object is injected
                        if (obj.Injected && obj.Created)
                        {
                            // check if outside of activity circle
                            if (obj.distance * 0.00053995680346 > activityCircle + 10.0)
                            {
                                // remove from sim
                                RemoveObjectFromSim(obj);
                            }
                        }

                        // check for network aircraft
                        if (obj is Aircraft && obj.owner == Obj.Owner.Sim)
                        {
                            // check if ignoring this aircraft
                            if (main.log.IgnoreName((obj as Aircraft).flightPlan.callsign) && obj.SimValid && obj.simPosition.geo.y < 30000.0)
                            {
                                // set high altitude
                                obj.simPosition.geo.y = 50000.0;
                                // update aircraft
                                UpdateObject(obj, obj.simPosition);
                            }
                        }
                    }
                }

                DoRemove();
            }
        }

        /// <summary>
        /// Timer-driven requests to the simulator: object info, position polling, weather
        /// </summary>
        void ProcessTimedRequests(double time)
        {
            // info request
            if (requestInfoTimer.Elapsed(time))
            {
                // info request
                RequestInfo();
            }

            // position request
            if (requestPositionTimer.Elapsed(time))
            {
                // position request
                RequestPosition();
            }

            // weather request
            if (requestWeatherTimer.Elapsed(time))
            {
                // weather request
                RequestWeather();
            }

            // get local states
            if (requestLocalStateTimer.Elapsed(time))
            {
                // local states request
//                RequestLocalStates();
            }
        }

        /// <summary>
        /// Rebuild the per-pair send-interval masks (SendPolicy), once per updateIntervalsTimer
        /// interval
        /// </summary>
        void RebuildIntervalMasks(double time)
        {
            // check for next time to update intervals
            if (updateIntervalsTimer.Elapsed(time))
            {
                // clear existing intervals
                intervalMasks.Clear();

                // for each object
                foreach (var localObject in objectList)
                {
                    // check if broadcasting this local object
                    if (IsBroadcast(localObject))
                    {
                        // for each object
                        foreach (var remoteObject in objectList)
                        {
                            // check for valid pair combination
                            if (localObject.Injected == false && remoteObject.owner == Aircraft.Owner.Network && remoteObject is Aircraft && (remoteObject as Aircraft).user)
                            {
                                // interval mask for this pair
                                int mask = 0;

                                // check for valid position
                                //if (localObject.simValid && remoteObject.netValid)
                                //{
                                //    // get distance
                                //    double distance = Vector.GeodesicDistance(localObject.simPosition.longitude, localObject.simPosition.latitude, remoteObject.netPosition.longitude, remoteObject.netPosition.latitude);
                                //    // check if outside activity circle
                                //    if (distance * 0.00053995680346 > mainForm.network.Peers.GetNodeActivityCircle(remoteObject.ownerNuid))
                                //    {
                                //        mask = 0xf;
                                //    }
                                //}

                                // check for remote node
                                if (main.network.LowBandwidth || main.network.NodeLowBandwidth(remoteObject.ownerNuid))
                                {
                                    // double the interval
                                    mask <<= 1;
                                    mask += 1;
                                }

                                // add to index
                                intervalMasks[(localObject, remoteObject)] = mask;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Drive the user aircraft's heading/bearing autopilot variable from the tracked object,
        /// once per trackingTimer interval
        /// </summary>
        void ProcessTracking(double time)
        {
            // tracking
            if (trackingTimer.Elapsed(time))
            {
                // check for user aircraft
                if (userAircraft != null && userAircraft.variableSet != null)
                {
                    // check if tracking by heading
                    if (trackHeadingObject != null)
                    {
                        // get position
                        Pos position = trackHeadingObject.Position;
                        // check for valid position
                        if (position != null)
                        {
                            // change state
                            userAircraft.variableSet.UpdateInteger(headingVuid, Vector.HeadingDegrees(position.angles.y));
                        }
                    }
                    // check if tracking by bearing
                    else if (trackBearingObject != null)
                    {
                        // get position
                        Pos objPosition = trackBearingObject.Position;
                        // get user position
                        Pos userPosition = userAircraft.Position;
                        // check for valid position
                        if (objPosition != null && userPosition != null)
                        {
                            // get bearing
                            double bearing = Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, objPosition.geo.x, objPosition.geo.z);
                            // change state
                            userAircraft.variableSet.UpdateInteger(headingVuid, Vector.HeadingDegrees(bearing));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Broadcast and/or record each object's variables, once per variablesTimer interval
        /// </summary>
        void BroadcastObjectVariables(double time)
        {
            // update variables
            if (variablesTimer.Elapsed(time))
            {
                // for each object
                foreach (var obj in objectList)
                {
                    // check if object has variables and has started
                    if (obj.variableSet != null && obj.variableStartTime < main.ElapsedTime)
                    {
                        // check for network
                        if (main.network.Connected)
                        {
                            // check for shared cockpit
                            if (obj.owner == Obj.Owner.Me && enteredAircraft != null)
                            {
                                // our variables for the entered (shared-cockpit) aircraft
                                main.network.SimSender.SendVariables(uint.MaxValue, obj.variableSet.integers, obj.variableSet.floats, obj.variableSet.string8s, [enteredAircraft.ownerNuid]);
                            }
                            // check if aircraft is being broadcast
                            else if (IsBroadcast(obj) && obj.Injected == false)
                            {
                                // diagnostic - dump float vuids/values actually being broadcast (only build the
                                // string when the monitor is actually showing it - MonitorVariables checks the
                                // same flag internally, but the string would still be built for nothing if we
                                // only guarded there)
                                if (main.monitor != null && main.monitor.variables)
                                {
                                    string floatsDump = "";
                                    foreach (var kv in obj.variableSet.floats)
                                    {
                                        floatsDump += kv.Key + "=" + kv.Value + ", ";
                                    }
                                    main.MonitorVariables("BROADCAST FLOATS - " + obj.ModelTitle + " - " + floatsDump);
                                }

                                main.network.SimSender.BroadcastVariables(obj.netId, obj.variableSet.integers, obj.variableSet.floats, obj.variableSet.string8s);
                            }
                        }

                        // check if recording
                        if (main.recorder.recording && obj.record)
                        {
                            // record variables
                            main.recorder.Record(obj.recorderObj, obj.variableSet.integers);
                            main.recorder.Record(obj.recorderObj, obj.variableSet.floats);
                            main.recorder.Record(obj.recorderObj, obj.variableSet.string8s);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Broadcast flight plan updates for broadcast aircraft, once per flightPlanTimer interval
        /// </summary>
        void BroadcastFlightPlans(double time)
        {
            // update flight plans
            if (flightPlanTimer.Elapsed(time))
            {
                // for each object
                foreach (var obj in objectList)
                {
                    // if object is being broadcast
                    if (IsBroadcast(obj) && obj is Aircraft aircraft)
                    {
                        // send flight plan
                        main.network.SimSender.BroadcastFlightPlanUpdate(obj.netId, aircraft.flightPlan);
                    }
                }
            }
        }

        /// <summary>
        /// Once per connection, try the configured auto-join address (Connect on Launch)
        /// </summary>
        void TryAutoNetworkJoin()
        {
            // detect connection state transitions to trigger auto network join
            bool connectedNow = Connected;
            if (!previousConnected && connectedNow)
            {
                // we just connected
                autoNetworkJoinAttempted = false;
            }

            if (connectedNow && !autoNetworkJoinAttempted)
            {
                // only attempt once per connection
                autoNetworkJoinAttempted = true;

                // if user requested Connect on Launch, try to join the selected hub/address
                try
                {
                    if (main.settingsConnectOnLaunch)
                    {
                        // Use the persisted join address (reflects the selected dropdown entry). If empty, do nothing.
                        string joinText = Settings.Default.JoinAddress;
                        if (!string.IsNullOrWhiteSpace(joinText))
                        {
                            main.EnqueueCommand(() => main.Join(joinText.TrimStart(' ').TrimEnd(' ')));
                        }
                    }
                }
                catch { }
            }

            previousConnected = connectedNow;
        }
    }
}
