#if SIMCONNECT
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using static JoinFS.Sim;
#endif

namespace JoinFS
{
#if SIMCONNECT
    /// <summary>
    /// Simconnect interface
    /// </summary>
    class SimConnectInterface : IDisposable
    {    /// <summary>
         /// SimConnect interface
         /// </summary>
        readonly SimConnect sc;
        private bool _isSimOpen = false;
        private readonly List<Action> _pendingRequests = [];
        public bool Valid { get { return sc != null; } }

        /// <summary>
        /// Link to sim
        /// </summary>
        readonly Sim sim;

        /// <summary>
        /// Link to main form
        /// </summary>
        readonly Main main;

        /// <summary>
        /// The thread that created the SimConnect object. SimConnect is not thread-safe, so every
        /// call must be made on this thread.
        /// </summary>
        readonly int ownerThreadId = Environment.CurrentManagedThreadId;

        /// <summary>
        /// Callers already reported for calling from the wrong thread (each is logged once)
        /// </summary>
        readonly HashSet<string> wrongThreadCallers = [];

        /// <summary>
        /// Signalled by SimConnect whenever messages are waiting; the sim thread waits on it
        /// </summary>
        readonly EventWaitHandle messageEvent = new(false, EventResetMode.AutoReset);

        /// <summary>
        /// Signalled whenever SimConnect has messages for <see cref="ReceiveMsg"/>
        /// </summary>
        public WaitHandle MessageEvent => messageEvent;

        /// <summary>
        /// True when called on the owner thread. Otherwise the call is re-posted to the owner
        /// (sim) thread through <paramref name="retry"/> and false is returned, so a caller on
        /// another thread never touches SimConnect itself. The first deferred call of each method
        /// is logged, to show which paths still call from other threads.
        /// </summary>
        bool OnOwnerThread(Action retry, [CallerMemberName] string caller = "")
        {
            if (Environment.CurrentManagedThreadId == ownerThreadId)
            {
                return true;
            }
            SimService service = main.simService;
            if (service == null)
            {
                // no sim thread (never expected) - run here rather than lose the call
                return true;
            }
            bool first;
            lock (wrongThreadCallers)
            {
                first = wrongThreadCallers.Add(caller);
            }
            if (first)
            {
                main.MonitorNetwork("THREAD - SimConnect '" + caller + "' called from thread " + Environment.CurrentManagedThreadId + " (" + Thread.CurrentThread.Name + "), deferred to the sim thread");
            }
            service.Post(retry);
            return false;
        }

        /// <summary>
        /// Close the SimConnect connection
        /// </summary>
        public void Dispose()
        {
            if (!OnOwnerThread(Dispose)) return;
            try
            {
                sc?.Dispose();
            }
            catch (Exception ex)
            {
                main.MonitorError(ex, "Closing SimConnect");
            }
            messageEvent.Dispose();
        }

        /// <summary>
        /// Handle simconnect errors
        /// </summary>
        /// <param name="ex">Exception</param>
        public void HandleException(COMException ex)
        {
            switch ((uint)ex.ErrorCode)
            {
                case 0x80004005:
                    break;
                case 0xC00000B0:
                    main.MonitorEvent("Lost connection to simulator");
                    main.recorder?.NotifySimulatorError("Lost connection to simulator");
                    // close once the current dispatch has finished, never inside it
                    sim.ScheduleClose();
                    break;
                default:
                    main.MonitorEvent("SIMCONNECT ERROR - " + ex.Message);
                    main.recorder?.NotifySimulatorError("SIMCONNECT ERROR - " + ex.Message);
                    break;
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="messageId"></param>
        public SimConnectInterface(Sim sim, Main main, string name)
        {
            try
            {
                // set sim
                this.sim = sim;
                // set main
                this.main = main;
                // try simconnect
                // no window: SimConnect signals messageEvent when messages are waiting
                this.sc = new SimConnect(name, IntPtr.Zero, 0, messageEvent, 0);

                // define an object structure
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "CATEGORY", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "ATC ID", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "ATC MODEL", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "IS USER SIM", null, SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "ENGINE TYPE", null, SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "NUMBER OF ENGINES", null, SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
#if FS2024
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "LIVERY NAME", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "LIVERY FOLDER", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "ATC AIRLINE", null, SIMCONNECT_DATATYPE.STRING64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
#endif
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_GET_INFO, "ATC FLIGHT NUMBER", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define a position velocity variables structure
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Plane Latitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Plane Longitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Plane Altitude", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Plane Pitch Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Plane Bank Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Plane Heading Degrees True", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Velocity World X", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Velocity World Y", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Velocity World Z", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Rotation Velocity Body X", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Rotation Velocity Body Y", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Rotation Velocity Body Z", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Acceleration World X", "meters per second squared", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Acceleration World Y", "meters per second squared", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "Acceleration World Z", "meters per second squared", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "GROUND ALTITUDE", "meter", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_VELOCITY, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define a position structure
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "Plane Latitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "Plane Longitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "Plane Altitude", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "Plane Pitch Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "Plane Bank Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "Plane Heading Degrees True", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "GROUND ALTITUDE", "meter", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define a position structure
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "Plane Latitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "Plane Longitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "Plane Altitude", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "Plane Pitch Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "Plane Bank Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "Plane Heading Degrees True", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_POSITION_UPDATE, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define a velocity structure
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "Velocity Body X", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "Velocity Body Y", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "Velocity Body Z", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "ROTATION VELOCITY BODY X", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "ROTATION VELOCITY BODY Y", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "ROTATION VELOCITY BODY Z", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "Acceleration Body X", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "Acceleration Body Y", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_VELOCITY, "Acceleration Body Z", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define a euler structure
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_EULER, "Plane Pitch Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_EULER, "Plane Heading Degrees True", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.OBJECT_EULER, "Plane Bank Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define a position velocity variables structure
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Plane Latitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Plane Longitude", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Plane Altitude", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Plane Pitch Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Plane Bank Degrees", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Plane Heading Degrees True", "radians", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Velocity World X", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Velocity World Y", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Velocity World Z", "m/s", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Rotation Velocity Body X", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Rotation Velocity Body Y", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Rotation Velocity Body Z", "radians per second", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Acceleration World X", "meters per second squared", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Acceleration World Y", "meters per second squared", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "Acceleration World Z", "meters per second squared", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "RUDDER POSITION", "position", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "ELEVATOR POSITION", "position", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "AILERON POSITION", "position", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "BRAKE LEFT POSITION", "position", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "BRAKE RIGHT POSITION", "position", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "GROUND ALTITUDE", "meter", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                // diagnostic only (see helicopters-on-elevated-platforms feature) - not yet used in any trust/correction decision, logged to verify reliability for injected aircraft first
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "PLANE ALT ABOVE GROUND", "feet", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                // used to ground a substitute model using its own real clearance instead of the sender's -
                // see AircraftPosition.staticCgToGround / the ground-jitter-on-model-mismatch fix
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_POSITION, "STATIC CG TO GROUND", "feet", SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // define an ID structure
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_SET_ID, "ATC ID", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_SET_ID, "ATC AIRLINE", null, SIMCONNECT_DATATYPE.STRING64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_SET_ID, "ATC FLIGHT NUMBER", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                //// define a waypoint
                sc.AddToDataDefinition(Sim.Definitions.AIRCRAFT_WAYPOINTS, "AI WAYPOINT LIST", "number", SIMCONNECT_DATATYPE.WAYPOINT, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // IMPORTANT: register it with the simconnect managed wrapper marshaller
                // if you skip this step, you will only receive a uint in the .dwData field.
                sc.RegisterDataDefineStruct<Sim.ObjectGetInfo>(Sim.Definitions.OBJECT_GET_INFO);
                sc.RegisterDataDefineStruct<Sim.ObjectPositionVelocity>(Sim.Definitions.OBJECT_POSITION_VELOCITY);
                sc.RegisterDataDefineStruct<Sim.ObjectPosition>(Sim.Definitions.OBJECT_POSITION);
                sc.RegisterDataDefineStruct<Sim.ObjectPositionUpdate>(Sim.Definitions.OBJECT_POSITION_UPDATE);
                sc.RegisterDataDefineStruct<Sim.ObjectVelocity>(Sim.Definitions.OBJECT_VELOCITY);
                sc.RegisterDataDefineStruct<Sim.ObjectEuler>(Sim.Definitions.OBJECT_EULER);
                sc.RegisterDataDefineStruct<Sim.AircraftPosition>(Sim.Definitions.AIRCRAFT_POSITION);
                sc.RegisterDataDefineStruct<Sim.AircraftSetId>(Sim.Definitions.AIRCRAFT_SET_ID);
                sc.RegisterDataDefineStruct<Object[]>(Sim.Definitions.AIRCRAFT_WAYPOINTS);

                // map events
                sc.MapClientEventToSimEvent(Sim.Event.RUDDER_SET, "RUDDER_SET");
                sc.MapClientEventToSimEvent(Sim.Event.ELEVATOR_SET, "ELEVATOR_SET");
                sc.MapClientEventToSimEvent(Sim.Event.AILERON_SET, "AILERON_SET");
                sc.MapClientEventToSimEvent(Sim.Event.SMOKE_ON, "SMOKE_ON");
                sc.MapClientEventToSimEvent(Sim.Event.SMOKE_OFF, "SMOKE_OFF");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011000, "#0x00011000");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011001, "#0x00011001");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011002, "#0x00011002");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011003, "#0x00011003");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011004, "#0x00011004");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011005, "#0x00011005");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011006, "#0x00011006");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011007, "#0x00011007");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011008, "#0x00011008");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_00011009, "#0x00011009");
                sc.MapClientEventToSimEvent(Sim.Event.EVENT_0001100A, "#0x0001100A");

                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.RUDDER_SET, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.ELEVATOR_SET, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.AILERON_SET, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.SMOKE_ON, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.SMOKE_OFF, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011000, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011001, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011002, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011003, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011004, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011005, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011006, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011007, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011008, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_00011009, false);
                sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, Sim.Event.EVENT_0001100A, false);

                sc.SetNotificationGroupPriority(Sim.Groups.GROUP0, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);

                // event handlers
                sc.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(RecvSimObjectData);
                sc.OnRecvSimobjectData += new SimConnect.RecvSimobjectDataEventHandler(RecvSimObjectData);
                sc.OnRecvWeatherObservation += new SimConnect.RecvWeatherObservationEventHandler(RecvWeatherObservation);
                sc.OnRecvAssignedObjectId += new SimConnect.RecvAssignedObjectIdEventHandler(RecvAssignedObjectId);
                sc.OnRecvEventObjectAddremove += new SimConnect.RecvEventObjectAddremoveEventHandler(RecvEventObjectAddremove);
                sc.OnRecvOpen += new SimConnect.RecvOpenEventHandler(RecvOpen);
                sc.OnRecvQuit += new SimConnect.RecvQuitEventHandler(RecvQuit);
                sc.OnRecvException += new SimConnect.RecvExceptionEventHandler(RecvException);
                sc.OnRecvEventFrame += new SimConnect.RecvEventFrameEventHandler(RecvEventFrame);
                sc.OnRecvEvent += new SimConnect.RecvEventEventHandler(RecvEvent);
#if FS2024
                sc.OnRecvEnumerateSimobjectAndLiveryList += new SimConnect.RecvEnumerateSimobjectAndLiveryListEventHandler(RecvModelList);
#endif

                // system events
                sc.SubscribeToSystemEvent(Sim.Event.OBJECT_ADDED, "ObjectAdded");
                sc.SubscribeToSystemEvent(Sim.Event.OBJECT_REMOVED, "ObjectRemoved");
                sc.SubscribeToSystemEvent(Sim.Event.FRAME, "Frame");
                sc.SubscribeToSystemEvent(Sim.Event.PAUSE, "Pause");
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void RequestSimulatorModels()
        {
            if (!OnOwnerThread(() => RequestSimulatorModels())) return;
            Action request = () =>
            {
#if FS2024
                // aircraft only - deliberately omits BOAT/GROUND so boats and ground vehicles never
                // enter the substitution/mapping list. Each SimObject category needs its own call.
                sc.EnumerateSimObjectsAndLiveries(Sim.Requests.GET_MODELS_AIRCRAFT, SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT);
                sc.EnumerateSimObjectsAndLiveries(Sim.Requests.GET_MODELS_HELICOPTER, SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER);
                sc.EnumerateSimObjectsAndLiveries(Sim.Requests.GET_MODELS_BALLOON, SIMCONNECT_SIMOBJECT_TYPE.HOT_AIR_BALLOON);
#endif
            };

            if (_isSimOpen)
            {
                request(); // Execute immediately
            }
            else
            {
                _pendingRequests.Add(request); // Save for later
                main.MonitorEvent("SimConnect not ready. Request queued.");
            }
        }

        void RecvSimObjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            sim.ProcessSimObjectData(data.dwObjectID, data.dwRequestID, data.dwData[0]);
        }

        void RecvWeatherObservation(SimConnect sender, SIMCONNECT_RECV_WEATHER_OBSERVATION data)
        {
            sim.ProcessWeatherObservation(data.dwRequestID, data.szMetar);
        }

        void RecvAssignedObjectId(SimConnect sender, SIMCONNECT_RECV_ASSIGNED_OBJECT_ID data)
        {
            sim.ProcessAssignedObjectId(data.dwObjectID, data.dwRequestID);
        }

        void RecvEventObjectAddremove(SimConnect sender, SIMCONNECT_RECV_EVENT_OBJECT_ADDREMOVE data)
        {
            sim.ProcessEventObjectAddremove(data.uEventID, data.dwData);
        }

        void RecvEventFrame(SimConnect sender, SIMCONNECT_RECV_EVENT_FRAME data)
        {
            sim.ProcessEventFrame(data.uEventID);
        }

        void RecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            sim.ProcessEvent(data.uEventID, data.dwData);
        }

#if FS2024
        void RecvModelList(SimConnect sender, SIMCONNECT_RECV_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST data)
        {
            sim.ProcessModelList(data);
        }
#endif

        void RecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            sim.ProcessOpen(data.szApplicationName,
                         data.dwSimConnectVersionMajor, data.dwSimConnectVersionMinor, data.dwSimConnectBuildMajor, data.dwSimConnectBuildMinor,
                         data.dwApplicationVersionMajor, data.dwApplicationVersionMinor, data.dwApplicationBuildMajor, data.dwApplicationBuildMinor);
            _isSimOpen = true;
            // Execute all delayed actions
            foreach (var action in _pendingRequests)
            {
                action();
            }
            _pendingRequests.Clear(); // Clear the queue
        }

        void RecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            sim.ProcessQuit();
        }

        void RecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            sim.ProcessException((uint)data.dwException);
        }


        /// <summary>
        /// Register an integer variable
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="units"></param>
        public void RegisterIntegerVariable(VariableMgr.Definition definition)
        {
            if (!OnOwnerThread(() => RegisterIntegerVariable(definition))) return;
            // check for valid name
            if (definition.scName.Length > 0)
            {
                try
                {
                    // register structure
                    sc.RegisterDataDefineStruct<Sim.IntegerStruct>(definition.scDefinition);
                    // add definition
                    sc.AddToDataDefinition(definition.scDefinition, definition.scName, definition.scUnits, SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            }
        }

        /// <summary>
        /// Register an float variable
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="units"></param>
        public void RegisterFloatVariable(VariableMgr.Definition definition)
        {
            if (!OnOwnerThread(() => RegisterFloatVariable(definition))) return;
            // check for valid name
            if (definition.scName.Length > 0)
            {
                try
                {
                    // add definition
                    sc.AddToDataDefinition(definition.scDefinition, definition.scName, definition.scUnits, SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    // register structure
                    sc.RegisterDataDefineStruct<Sim.FloatStruct>(definition.scDefinition);
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            }
        }

        /// <summary>
        /// Register a string variable
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="units"></param>
        public void RegisterString8Variable(VariableMgr.Definition definition)
        {
            if (!OnOwnerThread(() => RegisterString8Variable(definition))) return;
            // check for valid name
            if (definition.scName.Length > 0)
            {
                try
                {
                    // register structure
                    sc.RegisterDataDefineStruct<Sim.String8Struct>(definition.scDefinition);
                    // add definition
                    sc.AddToDataDefinition(definition.scDefinition, definition.scName, definition.scUnits, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            }
        }

        /// <summary>
        /// Register a variable event
        /// </summary>
        public void RegisterVariableEvent(VariableMgr.Definition definition)
        {
            if (!OnOwnerThread(() => RegisterVariableEvent(definition))) return;
            try
            {
                // check for valid event name
                if (definition.scEventName.Length > 0)
                {
                    // register event
                    sc.MapClientEventToSimEvent(definition.scEvent, definition.scEventName);
                    sc.AddClientEventToNotificationGroup(Sim.Groups.GROUP0, definition.scEvent, false);
                }
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        /// <summary>
        /// Module used to hold the structures generated at runtime for bundled variable reads.
        /// One field is emitted per bundled variable (in order), typed to match the variable's
        /// SimConnect datatype, so a single RequestDataOnSimObject can return many variables in
        /// one SIMCONNECT_RECV_SIMOBJECT_DATA message instead of one message per variable.
        /// </summary>
        static readonly ModuleBuilder bundleModuleBuilder = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("JoinFS.VariableBundles"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("VariableBundles");

        /// <summary>
        /// Unique name suffix for generated bundle structures
        /// </summary>
        static int bundleTypeCounter;

        /// <summary>
        /// Build a value type with one field per entry in <paramref name="fields"/>, in order,
        /// laid out and marshalled exactly like the existing single-variable structs
        /// (<see cref="Sim.IntegerStruct"/>, <see cref="Sim.FloatStruct"/>, <see cref="Sim.String8Struct"/>)
        /// so SimConnect's managed wrapper can marshal a whole bundle's worth of variables at once.
        /// </summary>
        static Type BuildBundleStructType(List<VariableMgr.Definition> fields)
        {
            TypeBuilder typeBuilder = bundleModuleBuilder.DefineType(
                "Bundle" + System.Threading.Interlocked.Increment(ref bundleTypeCounter),
                TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed |
                TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                typeof(ValueType),
                PackingSize.Size1);

            ConstructorInfo marshalAsCtor = typeof(MarshalAsAttribute).GetConstructor([typeof(UnmanagedType)]);
            FieldInfo sizeConstField = typeof(MarshalAsAttribute).GetField(nameof(MarshalAsAttribute.SizeConst));

            for (int index = 0; index < fields.Count; index++)
            {
                // field name encodes its position - this is how DetectSimconnect maps values
                // back onto vuids, since reflection does not guarantee GetFields() order
                string fieldName = "F" + index;

                switch (fields[index].type)
                {
                    case VariableMgr.Definition.Type.INTEGER:
                        typeBuilder.DefineField(fieldName, typeof(int), FieldAttributes.Public);
                        break;
                    case VariableMgr.Definition.Type.FLOAT:
                        typeBuilder.DefineField(fieldName, typeof(float), FieldAttributes.Public);
                        break;
                    case VariableMgr.Definition.Type.STRING8:
                        {
                            FieldBuilder stringField = typeBuilder.DefineField(fieldName, typeof(string), FieldAttributes.Public);
                            // [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] - matches Sim.String8Struct
                            CustomAttributeBuilder marshalAsAttr = new(
                                marshalAsCtor,
                                [UnmanagedType.ByValTStr],
                                [sizeConstField],
                                [8]);
                            stringField.SetCustomAttribute(marshalAsAttr);
                        }
                        break;
                }
            }

            return typeBuilder.CreateType();
        }

        /// <summary>
        /// Register the combined read structure for a variable file: build a struct matching
        /// its fields, add each field to the SimConnect data definition (same name/units as the
        /// per-variable registration would have used), and register the struct with SimConnect's
        /// marshaller so OnRecvSimobjectData hands back one populated instance per request instead
        /// of requiring one request (and one COM round trip) per variable.
        /// </summary>
        public void RegisterVariableBundle(VariableMgr.Bundle bundle, List<VariableMgr.Definition> fields)
        {
            if (!OnOwnerThread(() => RegisterVariableBundle(bundle, fields))) return;
            try
            {
                // build the structure type for this bundle
                Type structType = BuildBundleStructType(fields);

                // add every field to the data definition, in the same order they were built into the struct
                foreach (var field in fields)
                {
                    switch (field.type)
                    {
                        case VariableMgr.Definition.Type.INTEGER:
                            sc.AddToDataDefinition(bundle.scDefinition, field.scName, field.scUnits, SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                            break;
                        case VariableMgr.Definition.Type.FLOAT:
                            sc.AddToDataDefinition(bundle.scDefinition, field.scName, field.scUnits, SIMCONNECT_DATATYPE.FLOAT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                            break;
                        case VariableMgr.Definition.Type.STRING8:
                            sc.AddToDataDefinition(bundle.scDefinition, field.scName, field.scUnits, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                            break;
                    }
                }

                // register the structure with the managed wrapper's marshaller - the generic
                // method is only known at runtime, so invoke it via reflection
                typeof(SimConnect)
                    .GetMethod(nameof(SimConnect.RegisterDataDefineStruct))
                    .MakeGenericMethod(structType)
                    .Invoke(sc, [bundle.scDefinition]);

                // cache the field accessors, in field order, for fast unpacking on receipt
                FieldInfo[] fieldInfos = new FieldInfo[fields.Count];
                for (int index = 0; index < fields.Count; index++)
                {
                    fieldInfos[index] = structType.GetField("F" + index);
                }

                bundle.structType = structType;
                bundle.fields = fieldInfos;
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is COMException comEx)
            {
                // RegisterDataDefineStruct is invoked via reflection - unwrap so COM errors
                // are handled the same way as everywhere else
                HandleException(comEx);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void RemoveObject(uint simId, Sim.Requests request)
        {
            if (!OnOwnerThread(() => RemoveObject(simId, request))) return;
            try
            {
                sc.AIRemoveObject(simId, request);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void SetData(Enum def, uint simId, object data)
        {
            if (!OnOwnerThread(() => SetData(def, simId, data))) return;
            if ((Sim.Definitions)def != Sim.Definitions.OBJECT_VELOCITY)
            {
                main.MonitorNetwork("SetData ID '" + simId + "' - Data '" + Sim.DefinitionToString((Sim.Definitions)def) + "'");
            }

            try
            {
                // update object position and velocity
                sc.SetDataOnSimObject(def, simId, SIMCONNECT_DATA_SET_FLAG.DEFAULT, data);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void SetWaypoint(uint simId)
        {
            if (!OnOwnerThread(() => SetWaypoint(simId))) return;
            // initialise single waypoiny
            SIMCONNECT_DATA_WAYPOINT[] wp = new SIMCONNECT_DATA_WAYPOINT[1];
            wp[0].Flags = (uint)SIMCONNECT_WAYPOINT_FLAGS.SPEED_REQUESTED;
#if !P3D
            wp[0].percentThrottle = 0;
#endif
            wp[0].Latitude = 0;
            wp[0].Longitude = 0;
            // copy to object array
            Object[] waypoint = new Object[wp.Length];
            wp.CopyTo(waypoint, 0);
            // set waypoint
            SetData(Sim.Definitions.AIRCRAFT_WAYPOINTS, simId, waypoint);
        }

        public void DoEvent(uint simId, Enum simEvent, uint data)
        {
            if (!OnOwnerThread(() => DoEvent(simId, simEvent, data))) return;
            try
            {
                // simconnect event
                sc.TransmitClientEvent(simId, simEvent, data, Sim.Groups.GROUP0, SIMCONNECT_EVENT_FLAG.DEFAULT);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void SetWeather(string metar)
        {
            if (!OnOwnerThread(() => SetWeather(metar))) return;
            main.MonitorNetwork("SimEvent - Metar '" + metar + "'");

            try
            {
                // set weather
                sc.WeatherSetModeGlobal();
                sc.WeatherSetObservation(0, "GLOB " + metar);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void RequestDataByType(Sim.Requests request, Sim.Definitions def, uint radius)
        {
            if (!OnOwnerThread(() => RequestDataByType(request, def, radius))) return;
            Action lambdaCall = () =>
            {
                try
                {
                    // ugly, I know
                    if (main.sim.GetSimulatorName() != "Microsoft Flight Simulator 2024")
                    {
                        sc.RequestDataOnSimObjectType(request, def, radius, SIMCONNECT_SIMOBJECT_TYPE.ALL);
                    }
#if FS2024
                    else if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                    {
                        //sc.RequestDataOnSimObjectType(request, def, radius, SIMCONNECT_SIMOBJECT_TYPE.ALL);
                        sc.RequestDataOnSimObjectType(request, def, radius, SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT);
                        sc.RequestDataOnSimObjectType(request, def, radius, SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER);
                        sc.RequestDataOnSimObjectType(request, def, radius, SIMCONNECT_SIMOBJECT_TYPE.HOT_AIR_BALLOON);
                    }
#endif
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            };
            if (_isSimOpen)
            {
                lambdaCall(); // Execute immediately
            }
            else
            {
                _pendingRequests.Add(lambdaCall); // Save for later
                main.MonitorEvent("SimConnect not ready. Request queued.");
            }
        }

        public void RequestData(Sim.Requests request, Sim.Definitions def, uint simId)
        {
            if (!OnOwnerThread(() => RequestData(request, def, simId))) return;
            Action lambdaCall = () =>
            {
                try
                {
                    // request full aircraft position
                    sc.RequestDataOnSimObject(request, def, simId, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 1);
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            };
            if (_isSimOpen)
            {
                lambdaCall(); // Execute immediately
            }
            else
            {
                _pendingRequests.Add(lambdaCall); // Save for later
                main.MonitorEvent("SimConnect not ready. Request queued.");
            }
            
        }

        public void RequestVariable(Enum scRequest, Enum scDefinition, uint simId)
        {
            if (!OnOwnerThread(() => RequestVariable(scRequest, scDefinition, simId))) return;
            Action request = () =>
            {
                try
                {
                    // request updates
                    sc.RequestDataOnSimObject(scRequest, scDefinition, simId, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 1, 0);
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            };
            if (_isSimOpen)
            {
                request(); // Execute immediately
            }
            else
            {
                _pendingRequests.Add(request); // Save for later
                main.MonitorEvent("SimConnect not ready. Request queued.");
            }
        }

        /// <summary>
        /// Have the simulator send an object's data every visual frame, or every second, until
        /// <see cref="UnsubscribeData"/>. Visual rather than simulation frames: they keep coming
        /// while the simulation is paused.
        /// </summary>
        public void SubscribeData(Sim.Requests request, Sim.Definitions def, uint simId, bool everyFrame)
        {
            if (!OnOwnerThread(() => SubscribeData(request, def, simId, everyFrame))) return;
            Action subscribe = () =>
            {
                try
                {
                    sc.RequestDataOnSimObject(request, def, simId, everyFrame ? SIMCONNECT_PERIOD.VISUAL_FRAME : SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                }
                catch (COMException ex)
                {
                    HandleException(ex);
                }
                catch (Exception ex)
                {
                    main.MonitorError(ex);
                }
            };
            if (_isSimOpen)
            {
                subscribe();
            }
            else
            {
                _pendingRequests.Add(subscribe);
            }
        }

        /// <summary>
        /// Stop a feed started with <see cref="SubscribeData"/>
        /// </summary>
        public void UnsubscribeData(Sim.Requests request, Sim.Definitions def, uint simId)
        {
            if (!OnOwnerThread(() => UnsubscribeData(request, def, simId))) return;
            try
            {
                sc.RequestDataOnSimObject(request, def, simId, SIMCONNECT_PERIOD.NEVER, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void StopRequest(Enum scRequest, Enum scDefinition, uint simId)
        {
            if (!OnOwnerThread(() => StopRequest(scRequest, scDefinition, simId))) return;
            try
            {
                // stop request updates
                sc.RequestDataOnSimObject(scRequest, scDefinition, simId, SIMCONNECT_PERIOD.NEVER, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void WeatherRequest(Sim.Requests request, double lat, double lon, double alt)
        {
            if (!OnOwnerThread(() => WeatherRequest(request, lat, lon, alt))) return;
            try
            {
                // request weather
                sc.WeatherRequestInterpolatedObservation(request, (float)lat, (float)lon, (float)alt);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        public void ReleaseControl(uint simId, Sim.Requests request)
        {
            if (!OnOwnerThread(() => ReleaseControl(simId, request))) return;
            // take control of the object
            sc.AIReleaseControl(simId, request);
        }

        public void CreateObject(Sim.Obj obj)
        {
            if (!OnOwnerThread(() => CreateObject(obj))) return;
            // create sim position
            SIMCONNECT_DATA_INITPOSITION initPosition = new()
            {
                Airspeed = 0,
                Latitude = obj.netPosition.geo.z * (180.0 / Math.PI),
                Longitude = obj.netPosition.geo.x * (180.0 / Math.PI),
                Altitude = obj.netPosition.geo.y * Sim.FEET_PER_METRE,
                Pitch = obj.netPosition.angles.x * (180.0 / Math.PI),
                Bank = obj.netPosition.angles.z * (180.0 / Math.PI),
                Heading = obj.netPosition.angles.y * (180.0 / Math.PI),
                OnGround = 0
            };

            try
            {
                // get title
                string title = obj.ModelTitle;
#if FS2024
                // get livery
                string livery = obj.ModelLivery;
#endif
                // convert the long hyphen
                title = title.Replace("–", "â€“");

                // check for plane
                if (obj is Sim.Plane || obj is Sim.Helicopter)
                {
                    // create aircraft
                    // ugly, I know
                    if (main.sim.GetSimulatorName() != "Microsoft Flight Simulator 2024")
                    {
                        // MSFS2020 can't hadle helicopter creation as aircraft, must create object
                        if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2020" &&
                            obj is Sim.Helicopter)
                        {
                            sc.AICreateSimulatedObject(title, initPosition, Sim.Requests.CREATE_OBJECT);
                        }
                        else
                        {
                            sc.AICreateNonATCAircraft(title, sim.MakeAtcId(obj as Sim.Aircraft), initPosition, Sim.Requests.CREATE_OBJECT);
                        }
                    }
#if FS2024
                    else if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                    {
                        // remove monitor output
                        string tailNumber = sim.MakeAtcId(obj as Sim.Aircraft);
                        // main.MonitorEvent("Spawning " + title + " w/ livery " + livery + " tail no: " + tailNumber);
                        sc.AICreateNonATCAircraft_EX1(title, livery, tailNumber, initPosition, Sim.Requests.CREATE_OBJECT);
                    }
#endif
                }
                else
                {
                    // create other object
                    sc.AICreateSimulatedObject(title, initPosition, Sim.Requests.CREATE_OBJECT);
                }
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }

        // [HandleProcessCorruptedStateExceptions]
        public void ReceiveMsg()
        {
            if (!OnOwnerThread(() => ReceiveMsg())) return;
            try
            {
                sc.ReceiveMessage();
            }
            catch (AccessViolationException ex)
            {
                main.MonitorError(ex, "Access violation");
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                main.MonitorError(ex);
            }
        }
    }

#endif //SIMCONNECT
}
