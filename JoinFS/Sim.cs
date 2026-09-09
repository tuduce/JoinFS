using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using JoinFS.Properties;




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
#region Constants

#if DEBUG
        const float OBJECT_EXPIRE_TIME = 30.0f;
#else
        const float OBJECT_EXPIRE_TIME = 10.0f;
#endif
        const float NEW_OBJECT_EXPIRE_TIME = 60.0f;
        /// <summary>Max auto-retries of an injection the sim refused (SimConnect exception 22) before giving up on that object - see Fix 3. The delay between retries is Main.settingsInjectionRetrySeconds (-injectionretryseconds, default 10s).</summary>
        const int FAILED_RETRY_MAX = 30;

        public const double TIME_ERROR_RATE = 0.02;
        public const double FEET_PER_METRE = 3.28084;
        public const double METRES_PER_FOOT = 0.3048;
        /// <summary>How long the sender's raw "SIM ON GROUND" bit must hold its current value before trustingPlatformGround follows it - see Aircraft.pendingGroundFlag.</summary>
        const double GroundTrustDebounceSeconds = 0.3;
        /// <summary>How long a freshly (re)created Regime A ground object is left alone vertically after spawn, before JoinFS's own vertical correction (see UpdateSimObjectVelocity) is allowed to engage - see Obj.verticalCorrectionSuppressedUntil. Gives the sim's own gear-compression/attitude settle (e.g. a taildragger's tail lowering) time to finish on its own, undisturbed by a competing correction toward a single fixed target altitude that doesn't account for the aircraft's current, still-changing pitch. The hard-reset safety net (genuinely wrong placement) is unaffected - only the gentle catch-up nudge is suppressed.</summary>
        const double VerticalSettleGraceSeconds = 8.0;

#endregion

#region Types

        /// <summary>
        /// SimConnect structures
        /// </summary>
        public enum Definitions
        {
            OBJECT_GET_INFO,
            OBJECT_POSITION_VELOCITY,
            OBJECT_POSITION,
            OBJECT_POSITION_UPDATE,
            OBJECT_VELOCITY,
            OBJECT_EULER,
            /// <summary>Dedicated one-field "GEAR HANDLE POSITION" write - used to force the gear down on an injected substitute while the sender is on the ground, bypassing the model-variable change/delay gate so the sim's per-frame AI gear-phase logic can't win (see Fix 1f).</summary>
            OBJECT_GEAR,
            AIRCRAFT_POSITION,
            AIRCRAFT_GET_INFO,
            AIRCRAFT_SET_ID,
            PLANE_STATE,
            PLANE_STATE_UPDATE,
            HELICOPTER_STATE,
            HELICOPTER_STATE_UPDATE,
            AIRCRAFT_STATE,
            AIRCRAFT_STATE_UPDATE_FLIGHT,
            AIRCRAFT_STATE_UPDATE_ANCILLARY,
            AIRCRAFT_STATE_UPDATE_NAV,
            PISTON_ENGINE1,
            PISTON_ENGINE2,
            PISTON_ENGINE3,
            PISTON_ENGINE4,
            PISTON_ENGINE1_UPDATE,
            PISTON_ENGINE2_UPDATE,
            PISTON_ENGINE3_UPDATE,
            PISTON_ENGINE4_UPDATE,
            TURBINE_ENGINE1,
            TURBINE_ENGINE2,
            TURBINE_ENGINE3,
            TURBINE_ENGINE4,
            TURBINE_ENGINE1_UPDATE,
            TURBINE_ENGINE2_UPDATE,
            TURBINE_ENGINE3_UPDATE,
            TURBINE_ENGINE4_UPDATE,
            AIRCRAFT_WAYPOINTS,
            AIRCRAFT_GYRO,
            AIRCRAFT_RUDDER_TRIM,
            AIRCRAFT_AILERON_TRIM,
            OBJECT_SMOKE1,
            OBJECT_SMOKE4,
            OBJECT_SMOKE50,
            OBJECT_SMOKE99,
            AIRCRAFT_FUEL,
            PAYLOAD,
            STATION1,
            STATION2,
            STATION3,
            STATION4,
            STATION5,
            STATION6,
            STATION7,
            STATION8,
            STATION9,
            STATION10,
            STATION11,
            STATION12,
            STATION13,
            STATION14,
            STATION15,
            STATION16,
            STATION17,
            STATION18,
            STATION19,
            STATION20,
        }

        /// <summary>
        /// SimConnect requests
        /// </summary>
        public enum Requests
        {
            OBJECT_INFO,
            OBJECT_POSITION_VELOCITY,
            OBJECT_POSITION,
            AIRCRAFT_POSITION,
            PLANE_STATE,
            HELICOPTER_STATE,
            AIRCRAFT_STATE,
            PISTON_ENGINE1,
            PISTON_ENGINE2,
            PISTON_ENGINE3,
            PISTON_ENGINE4,
            TURBINE_ENGINE1,
            TURBINE_ENGINE2,
            TURBINE_ENGINE3,
            TURBINE_ENGINE4,
            AIRCRAFT_FUEL,
            AIRCRAFT_PAYLOAD,
            OBJECT_SMOKE,
            WEATHER,
            CREATE_OBJECT,
            RELEASE_AI,
            REMOVE_OBJECT,
            GET_MODELS_AIRCRAFT,
            GET_MODELS_HELICOPTER,
            GET_MODELS_BALLOON,
        };

        /// <summary>
        /// Base for dynamically-allocated per-object SimConnect request IDs used for periodic
        /// AIRCRAFT_POSITION polling (see ground-jitter-on-model-mismatch fix) - each polled Obj gets its
        /// own unique, persistent request ID (Obj.positionRequestId) instead of every object sharing
        /// Requests.AIRCRAFT_POSITION, which could let SimConnect cross-match a response meant for one
        /// object's position poll to a different object (a known class of ambiguity when many concurrent
        /// requests share one request ID). Chosen well clear of both the fixed Requests enum above (0-23)
        /// and VariableMgr.ScRequest's separately-growing dynamic range (100+, one allocation per variable
        /// set) - safe from colliding with either for the lifetime of any realistic session.
        /// </summary>
        const int PositionPollRequestIdBase = 1_000_000_000;
        int nextPositionPollRequestId = PositionPollRequestIdBase;
        int NextPositionPollRequestId() { return nextPositionPollRequestId++; }

        /// <summary>
        /// SimConnect events
        /// </summary>
        public enum Event
        {
            OBJECT_ADDED,
            OBJECT_REMOVED,
            FRAME,
            PAUSE,
            SIM_START,
            SIM_STOP,
            RUDDER_SET,
            ELEVATOR_SET,
            AILERON_SET,
            SMOKE_ON,
            SMOKE_OFF,
            AP_HEADING_VAR,
            EVENT_00011000,
            EVENT_00011001,
            EVENT_00011002,
            EVENT_00011003,
            EVENT_00011004,
            EVENT_00011005,
            EVENT_00011006,
            EVENT_00011007,
            EVENT_00011008,
            EVENT_00011009,
            EVENT_0001100A,
        };

        /// <summary>
        /// Convert an event to a string
        /// </summary>
        /// <param name="simEvent"></param>
        /// <returns></returns>
        public static string EventToString(Sim.Event simEvent)
        {
            return simEvent switch
            {
                Sim.Event.OBJECT_ADDED => "OBJECT_ADDED",
                Sim.Event.OBJECT_REMOVED => "OBJECT_REMOVED",
                Sim.Event.FRAME => "FRAME",
                Sim.Event.PAUSE => "PAUSE",
                Sim.Event.SIM_START => "SIM_START",
                Sim.Event.SIM_STOP => "SIM_STOP",
                Sim.Event.RUDDER_SET => "RUDDER_SET",
                Sim.Event.ELEVATOR_SET => "ELEVATOR_SET",
                Sim.Event.AILERON_SET => "AILERON_SET",
                Sim.Event.SMOKE_ON => "SMOKE_ON",
                Sim.Event.SMOKE_OFF => "SMOKE_OFF",
                Sim.Event.EVENT_00011000 => "EVENT_00011000",
                Sim.Event.EVENT_00011001 => "EVENT_00011001",
                Sim.Event.EVENT_00011002 => "EVENT_00011002",
                Sim.Event.EVENT_00011003 => "EVENT_00011003",
                Sim.Event.EVENT_00011004 => "EVENT_00011004",
                Sim.Event.EVENT_00011005 => "EVENT_00011005",
                Sim.Event.EVENT_00011006 => "EVENT_00011006",
                Sim.Event.EVENT_00011007 => "EVENT_00011007",
                Sim.Event.EVENT_00011008 => "EVENT_00011008",
                Sim.Event.EVENT_00011009 => "EVENT_00011009",
                Sim.Event.EVENT_0001100A => "EVENT_0001100A",
                _ => "UNKNOWN",
            };
        }

        /// <summary>
        /// Convert a definition to a string
        /// </summary>
        /// <param name="simEvent"></param>
        /// <returns></returns>
        public static string DefinitionToString(Sim.Definitions simDefinition)
        {
            return simDefinition switch
            {
                Sim.Definitions.OBJECT_GET_INFO => "OBJECT_GET_INFO",
                Sim.Definitions.OBJECT_POSITION_VELOCITY => "OBJECT_POSITION_VELOCITY",
                Sim.Definitions.OBJECT_POSITION => "OBJECT_POSITION",
                Sim.Definitions.OBJECT_VELOCITY => "OBJECT_VELOCITY",
                Sim.Definitions.OBJECT_EULER => "OBJECT_EULER",
                Sim.Definitions.OBJECT_GEAR => "OBJECT_GEAR",
                Sim.Definitions.AIRCRAFT_POSITION => "AIRCRAFT_POSITION",
                Sim.Definitions.AIRCRAFT_GET_INFO => "AIRCRAFT_GET_INFO",
                Sim.Definitions.AIRCRAFT_SET_ID => "AIRCRAFT_SET_ID",
                Sim.Definitions.AIRCRAFT_WAYPOINTS => "AIRCRAFT_WAYPOINTS",
                _ => "UNKNOWN",
            };
        }

        /// <summary>
        /// SimConnect groups
        /// </summary>
        public enum Groups
        {
            GROUP0,
        };

        /// <summary>
        /// Object info in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectGetInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String category;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String callsign;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String model;
            public int isUser;
            public int engineType;
            public int numEngines;
#if FS2024
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String livery;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String liveryFolder;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public String airline;
#endif
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String flightNumber;
        };

        /// <summary>
        /// Object position velocity variables in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectPositionVelocity
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            public float velocityX;
            public float velocityY;
            public float velocityZ;
            public float angularVelocityX;
            public float angularVelocityY;
            public float angularVelocityZ;
            public float accelerationX;
            public float accelerationY;
            public float accelerationZ;
            public float height;
            public int ground;
        };

        /// <summary>
        /// Object position in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectPosition(Sim.Pos pos)
        {
            public double latitude = pos.geo.z;
            public double longitude = pos.geo.x;
            public double altitude = pos.geo.y;
            public float pitch = (float)pos.angles.x;
            public float bank = (float)pos.angles.z;
            public float heading = (float)pos.angles.y;
            public float height = (float)pos.elevation;
            public int ground = pos.ground;
        };

        /// <summary>
        /// Object position in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectPositionUpdate
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            /// <summary>Forwarded to SimConnect's "SIM ON GROUND" whenever the sender reports the object on-ground, for any aircraft type - independent of the elevated-platform elevation-trust decision. This lets the local sim's own gear/ground-contact physics handle placement instead of fighting an externally-driven altitude every tick. See helicopters-on-elevated-platforms feature.</summary>
            public int ground;

            public ObjectPositionUpdate(ref ObjectPosition position)
            {
                latitude = position.latitude;
                longitude = position.longitude;
                altitude = position.altitude;
                pitch = position.pitch;
                bank = position.bank;
                heading = position.heading;
            }

            public ObjectPositionUpdate(Pos pos)
            {
                latitude = pos.geo.z;
                longitude = pos.geo.x;
                altitude = pos.geo.y;
                pitch = (float)pos.angles.x;
                bank = (float)pos.angles.z;
                heading = (float)pos.angles.y;
            }
        };

        /// <summary>
        /// Object velocity in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectVelocity(Vector linear, Vector angular, Vector acc)
        {
            public float velocityX = (float)linear.x;
            public float velocityY = (float)linear.y;
            public float velocityZ = (float)linear.z;
            public float angularVelocityX = (float)angular.x;
            public float angularVelocityY = (float)angular.y;
            public float angularVelocityZ = (float)angular.z;
            public float accelerationX = (float)acc.x;
            public float accelerationY = (float)acc.y;
            public float accelerationZ = (float)acc.z;
        };

        /// <summary>
        /// Object orientation in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectEuler(Vector angles)
        {
            public float pitch = (float)angles.x;
            public float heading = (float)angles.y;
            public float bank = (float)angles.z;
        };

        /// <summary>
        /// Aircraft position variables in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct AircraftPosition
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            public float velocityX;
            public float velocityY;
            public float velocityZ;
            public float angularVelocityX;
            public float angularVelocityY;
            public float angularVelocityZ;
            public float accelerationX;
            public float accelerationY;
            public float accelerationZ;
            public float rudder;
            public float elevator;
            public float aileron;
            public float brakeLeft;
            public float brakeRight;
            public float elevation;
            /// <summary>"PLANE ALT ABOVE GROUND", feet - diagnostic only, see helicopters-on-elevated-platforms feature. Not yet used in any trust/correction decision.</summary>
            public float radarAltitude;
            public int ground;
            /// <summary>"STATIC CG TO GROUND", feet - this object's own real static ground clearance, read live via SimConnect (no file access needed, unlike aircraft.cfg contact-point data). Used to ground a substitute model using its own geometry instead of the sender's - see the ground-jitter-on-model-mismatch fix.</summary>
            public float staticCgToGround;
        };

        /// <summary>
        /// Aircraft ID in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct AircraftSetId
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String callsign;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public String airline;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public String number;
        };

        /// <summary>
        /// Object velocity
        /// </summary>
        public class Vel
        {
            public Vector linear;
            public Vector angular;
            public Vector acc;

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(Vector linear, Vector angular, Vector acc)
            {
                this.linear = linear;
                this.angular = angular;
                this.acc = acc;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel()
            {
                this.linear = new Vector();
                this.angular = new Vector();
                this.acc = new Vector();
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(ref ObjectPositionVelocity position)
            {
                this.linear = new Vector(position.velocityX, position.velocityY, position.velocityZ);
                this.angular = new Vector(position.angularVelocityX, position.angularVelocityY, position.angularVelocityZ);
                this.acc = new Vector(position.accelerationX, position.accelerationY, position.accelerationZ);
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(ref AircraftPosition position)
            {
                this.linear = new Vector(position.velocityX, position.velocityY, position.velocityZ);
                this.angular = new Vector(position.angularVelocityX, position.angularVelocityY, position.angularVelocityZ);
                this.acc = new Vector(position.accelerationX, position.accelerationY, position.accelerationZ);
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Vel(ref ObjectVelocity velocity)
            {
                this.linear = new Vector(velocity.velocityX, velocity.velocityY, velocity.velocityZ);
                this.angular = new Vector(velocity.angularVelocityX, velocity.angularVelocityY, velocity.angularVelocityZ);
                this.acc = new Vector(velocity.accelerationX, velocity.accelerationY, velocity.accelerationZ);
            }

            /// <summary>
            /// Clone
            /// </summary>
            public Vel Clone() => new(linear.Clone(), angular.Clone(), acc.Clone());

            /// <summary>
            /// Extrapolate a velocity in time
            /// </summary>
            public Vel Extrapolate(double time)
            {
                return new Vel(new Vector(linear.x + acc.x * time, linear.y + acc.y * time, linear.z + acc.z * time), angular.Clone(), acc.Clone());
            }
        }

        /// <summary>
        /// Object position
        /// </summary>
        public class Pos
        {
            public Vector geo;
            public Vector angles;
            public double elevation;
            /// <summary>"PLANE ALT ABOVE GROUND" in meters, diagnostic only - see helicopters-on-elevated-platforms feature. NaN when not sourced from an AircraftPosition read (distinct from a real 0.0 reading).</summary>
            public double radarHeight;
            public int ground;
            /// <summary>"STATIC CG TO GROUND" in meters - this object's own real static ground clearance. NaN when not sourced from an AircraftPosition read (a live SimConnect poll not yet arrived, or an older peer that didn't broadcast it - see the ground-jitter-on-model-mismatch fix).</summary>
            public double staticCgToGround;

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos()
            {
                this.geo = new Vector();
                this.angles = new Vector();
                this.elevation = 0.0;
                this.radarHeight = double.NaN;
                this.ground = 0;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(Vector geo, Vector angles, double elevation, int ground, double radarHeight = double.NaN)
            {
                this.geo = geo;
                this.angles = angles;
                this.elevation = elevation;
                this.radarHeight = radarHeight;
                this.ground = ground;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref ObjectPosition position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.height;
                this.radarHeight = double.NaN;
                this.ground = position.ground;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref AircraftPosition position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.elevation;
                this.radarHeight = position.radarAltitude * 0.3048;
                this.ground = position.ground;
                this.staticCgToGround = position.staticCgToGround * 0.3048;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref ObjectPositionVelocity position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.height;
                this.radarHeight = double.NaN;
                this.ground = position.ground;
                this.staticCgToGround = double.NaN;
            }

            /// <summary>
            /// Clone
            /// </summary>
            public Pos Clone() => new(geo.Clone(), angles.Clone(), elevation, ground, radarHeight);

            /// <summary>
            /// Extrapolate a position using velocity and time
            /// </summary>
            /// <param name="velocity"></param>
            /// <param name="time"></param>
            /// <returns></returns>
            public Pos Extrapolate(Vel velocity, double time)
            {
                // get rate of change of geodesic position
                double xRate = Vector.GeodesicDistance(geo.x, geo.z, geo.x + Vector.GEODESIC_EPSILON, geo.z);
                double zRate = Vector.GeodesicDistance(geo.x, geo.z, geo.x, geo.z + Vector.GEODESIC_EPSILON);
                // extrapolate position and velocity
                Vector scalar = new(1.0 / xRate * Vector.GEODESIC_EPSILON, 1.0, 1.0 / zRate * Vector.GEODESIC_EPSILON);
                return new Pos(geo + (velocity.linear * time + velocity.acc * (time * time)) * scalar, angles + velocity.angular * time, elevation, ground, radarHeight);
            }
        }

        #endregion

        #region Variables

        /// <summary>
        /// Integer value
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct IntegerStruct
        {
            public int value;
        };

        /// <summary>
        /// Float value
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct FloatStruct
        {
            public float value;
        };

        /// <summary>
        /// String value
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct String8Struct
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string value;
        };

        /// <summary>
        /// Register an integer variable
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="units"></param>
        public void RegisterIntegerVariable(VariableMgr.Definition definition)
        {
#if SIMCONNECT
            // check for simconnect
            // register variable
            simconnect?.RegisterIntegerVariable(definition);
#endif
        }

        /// <summary>
        /// Register a float variable
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="units"></param>
        public void RegisterFloatVariable(VariableMgr.Definition definition)
        {
#if SIMCONNECT
            // check for simconnect
            // register variable
            simconnect?.RegisterFloatVariable(definition);
#endif
        }

        /// <summary>
        /// Register an integer variable
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="units"></param>
        public void RegisterString8Variable(VariableMgr.Definition definition)
        {
#if SIMCONNECT
            // check for simconnect
            // register variable
            simconnect?.RegisterString8Variable(definition);
#endif
        }

        /// <summary>
        /// Register a variable event
        /// </summary>
        public void RegisterVariableEvent(VariableMgr.Definition definition)
        {
#if SIMCONNECT
            // check for simconnect
            // register event
            simconnect?.RegisterVariableEvent(definition);
#endif
        }

        /// <summary>
        /// Request a variable
        /// </summary>
        public void RequestVariable(Enum scRequest, Enum scDefinition, uint simId)
        {
#if SIMCONNECT
            // register variable
            simconnect ?. RequestVariable(scRequest, scDefinition, simId);
#endif
        }

        /// <summary>
        /// Stop request
        /// </summary>
        public void StopRequest(Enum scRequest, Enum scDefinition, uint simId)
        {
#if SIMCONNECT
            // register variable
            simconnect ?. StopRequest(scRequest, scDefinition, simId);
#endif
        }

        /// <summary>
        /// Update a variable
        /// </summary>
        public void UpdateVariable(Enum scDefinition, uint simId, object data)
        {
#if SIMCONNECT
            // register variable
            simconnect ?. SetData(scDefinition, simId, data);
#endif
        }

        #endregion

        #region Object

        /// <summary>
        /// Simulator object
        /// </summary>
        public class Obj
        {
            /// <summary>
            /// Type of owner
            /// </summary>
            public enum Owner
            {
                Me,
                Network,
                Sim,
                Recorder,
            }

            // Although rather the property of a node, than of an object,
            // the value of the previous delay is stored here for access speed.
            public float prevDelay = 0.0f;

            public Owner owner = Owner.Me;
            public LocalNode.Nuid ownerNuid;
            public uint netId = uint.MaxValue;
            public uint simId = uint.MaxValue;
            /// <summary>
            /// This object's own, persistent SimConnect request ID for periodic AIRCRAFT_POSITION polling,
            /// lazily allocated from Sim.NextPositionPollRequestId() the first time it's needed. -1 means
            /// not yet allocated. Each object gets its own ID (rather than every object sharing the single
            /// Requests.AIRCRAFT_POSITION value) to avoid SimConnect cross-matching a response meant for one
            /// object's position poll to a different object - see the ground-jitter-on-model-mismatch fix.
            /// </summary>
            public int positionRequestId = -1;
            public string ownerModel = "";
            public string ownerLivery = "";
            public string ownerIcaoType = "";
            public string ownerIcaoAirline = "";
            /// <summary>
            /// Doc8643 class code (e.g. "H1T") and wake turbulence category, as resolved by the OWNER's
            /// own JoinFS instance - config-confirmed (real aircraft.cfg/livery.cfg data) or live-derived
            /// (category/engine simvars), never a title guess. Sent over the network so every receiving
            /// peer benefits from the sender's best-available data instead of each peer independently
            /// re-deriving classCode from ownerIcaoType via its own bundled Doc8643 table, which fails
            /// whenever ownerIcaoType is a bogus/non-standard string that isn't a real Doc8643 designator.
            /// </summary>
            public string ownerClassCode = "";
            public string ownerWtc = "";
            /// <summary>True when ownerClassCode/ownerWtc came from the owner's config-confirmed or live-derived read, not a fallback guess</summary>
            public bool ownerClassCodeConfirmed = false;
            public Substitution.Model subModel = null;
            public Substitution.Type subType = Substitution.Type.Original;
            public Substitution.MatchTrace subTrace = null;
            public int typerole = Substitution.TypeRole_SingleProp;
            public VariableMgr.Set variableSet = null;
            public double variableStartTime;
            /// <summary>
            /// True when the sim refused to create this object (SimConnect exception 22). Historically
            /// latched forever, so an injection attempted while MSFS was still on the menu / loading a
            /// flight never retried and no traffic appeared until the user toggled [Sim] - see Fix 3.
            /// Now self-healing: the injection finder re-arms it after Main.settingsInjectionRetrySeconds,
            /// up to FAILED_RETRY_MAX attempts, and ProcessOpen / a SimStart event clear it outright.
            /// </summary>
            public bool failed = false;
            /// <summary>main.ElapsedTime at which <see cref="failed"/> was last set - see Fix 3.</summary>
            public double failedTime = 0.0;
            /// <summary>How many times injection of this object has been refused - caps the auto-retry so a genuinely bad model eventually stops - see Fix 3.</summary>
            public int failedCount = 0;
            /// <summary>True once the on-ground gear-down force (Fix 1f) has been sent for the current ground stay - lets the per-tick check throttle to nextGearForceTime instead of writing OBJECT_GEAR every frame.</summary>
            public bool gearForcedDown = false;
            /// <summary>main.ElapsedTime of the next allowed OBJECT_GEAR refresh write - see gearForcedDown.</summary>
            public double nextGearForceTime = 0.0;
            /// <summary>Hysteresis state for the Regime A on-ground vertical correction - see UpdateSimObjectVelocity. True while actively correcting a persistent gap; only clears once the error has closed to well inside the engage threshold, so the correction can't limit-cycle right at that threshold's edge.</summary>
            public bool verticalCorrectionActive = false;
            /// <summary>main.ElapsedTime before which the Regime A vertical catch-up correction is fully suppressed - see VerticalSettleGraceSeconds. Set on every fresh spawn/re-creation so the sim's own attitude/gear settle gets a clear run first.</summary>
            public double verticalCorrectionSuppressedUntil = 0.0;
            public double expireTime = 0.0;
            public bool broadcast = false;
            public double netStateTime = 0.0;
            public double netRealTime = 0.0;
            public double netSimTime = 0.0;
            public double simTime = 0.0;
            public bool NetValid { get { return netStateTime > 0.0; } }
            public bool SimValid { get { return simTime > 0.0; } }
            public Pos simPosition = new();
            public Pos netPosition = new();
            public Vel netVelocity = new();
            /// <summary>True when the sender's altitude should be trusted as-is (elevated platform - helipad/ship deck/rooftop) instead of being blended toward local terrain mesh. Applies to any aircraft type. Can apply on final approach too, before the sender reports on-ground. See helicopters-on-elevated-platforms feature.</summary>
            public bool trustingPlatformElevation = false;
            /// <summary>True whenever this object's on-ground state is trusted from the network and forwarded to the local sim's placement of the object, for any aircraft type - independent of trustingPlatformElevation. Always requires the sender to actually report on-ground, so the local sim is never told an airborne aircraft is resting on the ground. See helicopters-on-elevated-platforms feature.</summary>
            public bool trustingPlatformGround = false;
            /// <summary>Debounce state for trustingPlatformGround - see ground-jitter-on-spawn fix. Unlike trustingPlatformElevation (a continuous mismatch distance, smoothed via engage/release thresholds), the sender's raw "SIM ON GROUND" bit has no magnitude to apply a threshold band to - it's just noisy right after an object is created/spawned (physics still settling onto the ground). Debouncing by time instead: the raw flag must hold its current value for GroundTrustDebounceSeconds before trustingPlatformGround is allowed to follow it, so a flicker during the settle window doesn't repeatedly retarget smoothedGroundClearanceCorrection.</summary>
            public bool pendingGroundFlag = false;
            /// <summary>Net time (sender's clock) at which pendingGroundFlag last changed - see pendingGroundFlag. NaN before the first sample.</summary>
            public double pendingGroundFlagSince = double.NaN;
            /// <summary>Low-pass-filtered local-vs-remote terrain elevation offset used by the near-ground altitude blend in UpdateAircraft. NaN when not currently tracking (out of blend range, or no sample taken yet). The "GROUND ALTITUDE" probe feeding both sides has tick-to-tick noise; blending by the raw offset every update showed up as the aircraft jittering in height while sitting on/taxiing on ordinary ground.</summary>
            public double smoothedElevationOffset = double.NaN;
            /// <summary>Low-pass-filtered ground-clearance correction (own substitute clearance minus sender's) applied in UpdateAircraft - see ground-jitter-on-model-mismatch fix. NaN when not yet sampled. Smoothed for the same reason as smoothedElevationOffset: trustPlatformGround (and the sender's own reported on-ground flag it comes from) can flicker tick-to-tick, and applying the raw target value directly would snap the substitute's altitude instantly between "as if it were the original aircraft" and "properly grounded" every time that single flag flips - visible as a sharp jitter rather than the flag's own noise being smoothed away first.</summary>
            public double smoothedGroundClearanceCorrection = double.NaN;
            /// <summary>Low-pass-filtered absolute on-ground target altitude for Regime A (ordinary ground, not an elevated platform) - local GROUND ALTITUDE probe + this substitute's own STATIC CG TO GROUND. NaN when not in Regime A. Unlike smoothedGroundClearanceCorrection this has no dependence on the sender's own clearance, so a large size mismatch between reported and substituted model no longer produces a multi-metre offset (Fix 2). Only a seed - the sim owns the vertical axis in Regime A (Fix 1c).</summary>
            public double smoothedGroundAltitude = double.NaN;
            /// <summary>Throttle for the RawPos/RawPosRelay ground-placement traces in UpdateAircraft (both keyed by the sender's netTime) - see main.MonitorNetwork's "Network" category.</summary>
            public double nextRawDiagLogTime = 0.0;
            /// <summary>Throttle for the RawPosSend ground-placement trace in ProcessAircraftPosition, keyed by local simTime - kept separate from nextRawDiagLogTime because that one is keyed by the unrelated netTime clock; sharing one field let whichever trace ran first starve the other.</summary>
            public double nextRawPosSendDiagLogTime = 0.0;
            /// <summary>Throttle for the VerticalCorrection ground-placement trace in UpdateSimObjectVelocity, keyed by main.ElapsedTime - kept separate from the other two RawPos*/VerticalCorrection throttles because each fires from a different clock/loop.</summary>
            public double nextVerticalCorrectionDiagLogTime = 0.0;
            public Vector oldEuler;
            public double distance = double.MaxValue;
            public bool paused = false;
            public int positionCount = 0;
            public bool showOnRadar = true;

            public bool record = false;
            public Recorder.Obj recorderObj;

            public bool Created { get { return simId != uint.MaxValue; } }
            public bool Injected { get { return owner == Owner.Network || owner == Owner.Recorder; } }
            public bool remoteFlightControl = false;
            public bool RemoteAnyControl { get { return remoteFlightControl; } }
            public bool RemoteAllControl { get { return remoteFlightControl; } }
            public bool takeControl = false;

            public string ModelTitle { get { return subModel != null ? subModel.title : ownerModel; } }
            public string ModelLivery { get { return subModel != null ? subModel.variation : ownerLivery; } }

            /// <summary>
            /// Object position
            /// </summary>
            public Pos Position
            {
                get
                {
                    // check for network control
                    if (Injected && NetValid)
                    {
                        // return network position
                        return netPosition;
                    }

                    // check for valid simulator position
                    if (SimValid)
                    {
                        // return simulator position
                        return simPosition;
                    }

                    // no position available
                    return null;
                }
            }

            public Obj() { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect object</param>
            public Obj(uint simId, string model)
            {
                // set sim ID
                this.simId = simId;
                netId = simId;
                // update info
                ownerModel = model;
                subModel = null;
                owner = Owner.Sim;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Obj(LocalNode.Nuid ownerNuid, uint netId)
            {
                // owner of the object
                this.owner = ownerNuid.Invalid() ? Owner.Recorder : Owner.Network;
                // controlled object
                this.remoteFlightControl = true;
                // set guid
                this.ownerNuid = ownerNuid;
                // set net ID (owner's sim ID)
                this.netId = netId;
                // set broadcast flag
                this.broadcast = ownerNuid.Invalid();
                // initialise record flag
                this.record = !ownerNuid.Invalid();
            }
        }

        /// <summary>
        /// List of objects
        /// </summary>
        public List<Obj> objectList = [];

        /// <summary>
        /// Remove object from simulator
        /// </summary>
        /// <param name="obj">Object</param>
        public void RemoveObjectFromSim(Obj obj)
        {
            // check if object is in the sim
            if (Connected && obj.Injected && obj.Created)
            {
#if XPLANE || CONSOLE
                // remove from xplane
                xplane.RemoveAircraft(obj.simId);
#elif SIMCONNECT
                // check for simconnect
                // remove from simconnect
                simconnect?.RemoveObject(obj.simId, Requests.REMOVE_OBJECT);
#endif

                // check for aircraft
                if (obj is Aircraft)
                {
                    // aircraft
                    Aircraft aircraft = obj as Aircraft;
                    // message
                    main.MonitorEvent("Removing aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - ID '" + aircraft.simId + "' - Sub '" + obj.ModelTitle + "'");
                }
                else
                {
                    // message
                    main.MonitorEvent("Removing object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - ID '" + obj.simId + "' - Sub '" + obj.ModelTitle + "'");
                }

                // reset sim ID
                obj.simId = uint.MaxValue;
                // the object may be recreated with a drastically different-sized model - drop the
                // converged ground-placement state so the first fresh sample of the new model seeds
                // directly instead of easing across from the old model's value (see Fix 1/2)
                if (obj is Aircraft groundAircraft)
                {
                    groundAircraft.smoothedGroundAltitude = double.NaN;
                    groundAircraft.smoothedGroundClearanceCorrection = double.NaN;
                    groundAircraft.smoothedElevationOffset = double.NaN;
                }
                // obj.simPosition (and the SimValid it gates on) still reflect the OLD model until the
                // recreated object's own first AIRCRAFT_POSITION poll arrives. Without this, the Regime A
                // altitude seed (Fix 1b) would briefly use the OLD model's now-irrelevant STATIC CG TO
                // GROUND/elevation for the NEW model on top of the inherent one-poll-cycle bootstrap delay
                // every fresh spawn already has ("spawns 0.5-2m off, then settles") - and for a size
                // mismatch between successive substitutes, that stale value can be a much worse guess than
                // even the sender's own raw altitude. Blanking simPosition drops SimValid back to false and
                // staticCgToGround back to NaN, so the very first spawn falls back to the sender's raw
                // altitude bootstrap instead - the same, smaller gap a brand-new aircraft's first-ever spawn
                // already has - until the new model's own first poll response arrives.
                obj.simPosition = new Pos();
                obj.simTime = 0.0;
                // a new model shouldn't inherit the old model's vertical-correction hysteresis state
                obj.verticalCorrectionActive = false;
                // create variables
                CreateModelVariables(obj);
            }

            // update match
            if (obj.Injected)
            {
                UpdateObject(obj, obj.ownerModel, obj.ownerLivery, obj.ownerIcaoType, obj.ownerIcaoAirline, obj.ownerClassCode, obj.ownerWtc, obj.ownerClassCodeConfirmed, obj.typerole);
            }
        }

        /// <summary>
        /// Remove an object from the list
        /// </summary>
        /// <param name="object"></param>
        void RemoveObjectFromList(Obj obj)
        {
            // check if object is in list
            if (objectList.Contains(obj))
            {
                // check for aircraft
                if (obj is Aircraft)
                {
                    // aircraft
                    Aircraft aircraft = obj as Aircraft;
                    // message
                    main.MonitorEvent("Delisting aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "'");
                }
                else
                {
                    // message
                    main.MonitorEvent("Delisting object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "'");
                }

                // remove object from the list
                objectList.Remove(obj);

                // check for aircraft
                if (obj is Aircraft)
                {
                    // check for weather aircraft
                    if (obj == weatherAircraft)
                    {
                        // disable weather aircraft
                        SetWeatherAircraft(null);
                    }
                    // check for cockpit aircraft
                    if (obj == enteredAircraft)
                    {
                        // leave cockpit
                        LeaveAircraft();
                    }
                }

                // check for creating aircraft
                if (obj == creatingObject)
                {
                    // no longer creating
                    creatingObject = null;
                }

                // check for user aircraft
                if (obj == userAircraft)
                {
                    // reset user aircraft
                    userAircraft = null;
                }

                // check for tracking heading object
                if (obj == trackHeadingObject)
                {
                    // reset tracking object
                    trackHeadingObject = null;
                }

                // check for tracking bearing object
                if (obj == trackBearingObject)
                {
                    // reset tracking object
                    trackBearingObject = null;
                }

                // remove interval masks
                RemoveIntervalMask(obj);

                // check if local object
                if (IsBroadcast(obj) && main.network.localNode.Connected)
                {
                    // notify session
                    main.network.SendRemoveObjectMessage(obj.netId);
                }

                // stop variable requests
                obj.variableSet ?. StopRequests();
            }
        }

        /// <summary>
        /// Remove object
        /// </summary>
        /// <param name="object">Object to remove</param>
        void RemoveObject(Obj obj)
        {
            // remove from simulator
            RemoveObjectFromSim(obj);
            // remove from list
            RemoveObjectFromList(obj);
        }

        /// <summary>
        /// Remove all objects belonging to a node
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveObject(LocalNode.Nuid ownerNuid)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object is controlled by leaving node
                if (obj.ownerNuid == ownerNuid)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Remove all objects belonging to a specific node object
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveObject(LocalNode.Nuid ownerNuid, uint netId)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object has link to recorder object
                if (obj.ownerNuid == ownerNuid && obj.netId == netId)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Remove all objects belonging to a node
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveObjectsFromSim(LocalNode.Nuid ownerNuid)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object is controlled by leaving node
                if (obj.ownerNuid == ownerNuid)
                {
                    RemoveObjectFromSim(obj);
                }
            }
        }

        /// <summary>
        /// Remove all controlled objects
        /// </summary>
        /// <param name="ownerGuid">Node</param>
        public void RemoveInjectedObjects()
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object is injected by leaving node
                if (obj.Injected)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Schedule remove objects
        /// </summary>
        volatile bool scheduleRemoveObjects = false;

        /// <summary>
        /// Schedule a remove
        /// </summary>
        /// <param name="model"></param>
        public void ScheduleRemoveObjects()
        {
            // set schedule
            scheduleRemoveObjects = true;
        }

        /// <summary>
        /// Schedule a remove
        /// </summary>
        /// <param name="model"></param>
        public void RemoveObjects()
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // only remove objects we injected
                if (obj.Injected)
                {
                    // add object to remove list
                    removeList.Add(obj);
                }
            }

            DoRemove();
        }

        /// <summary>
        /// Schedule remove
        /// </summary>
        volatile string scheduleRemove = null;

        /// <summary>
        /// Schedule a remove
        /// </summary>
        /// <param name="model"></param>
        public void ScheduleRemoveModel(string model)
        {
            // set schedule
            scheduleRemove ??= model;
        }

        /// <summary>
        /// Remove all object using a given model
        /// </summary>
        /// <param name="model">Model</param>
        public void RemoveObjectsFromSim(string model)
        {
            // for all objects
            foreach (Obj obj in objectList)
            {
                // check if object needs replacing
                if (obj.ownerModel.Equals(model, StringComparison.Ordinal))
                {
                    // remove from simulator
                    RemoveObjectFromSim(obj);
                }
            }
        }

        // create remove object list
        readonly List<Obj> removeList = [];

        /// <summary>
        /// Remove all objects in the remove list
        /// </summary>
        void DoRemove()
        {
            // for each object in remove list
            foreach (Obj obj in removeList)
            {
                RemoveObject(obj);
            }

            removeList.Clear();
        }

        /// <summary>
        /// Reset network positioning of object
        /// </summary>
        /// <param name="ownerGuid">Owner guid</param>
        /// <param name="netId">Network ID</param>
        public static void ResetObject(Obj obj)
        {
            // check for valid object
            if (obj != null)
            {
                // reset network times
                obj.netStateTime = 0.0;
                obj.netRealTime = 0.0;
                obj.netSimTime = 0.0;
            }
        }

        /// <summary>
        /// Reset network positioning of object
        /// </summary>
        /// <param name="ownerGuid">Owner guid</param>
        /// <param name="netId">Network ID</param>
        public void ResetObject(LocalNode.Nuid ownerNuid, uint netId)
        {
            // get object
            ResetObject(objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId));
        }

        /// <summary>
        /// Update the model for an object
        /// </summary>
        /// <param name="model"></param>
        public async void UpdateObject(Obj obj, string model, string livery, string icaoType, string icaoAirline, string classCode, string wtc, bool classCodeConfirmed, int typerole)
        {
            obj.typerole = typerole;
            // update model
            obj.ownerModel = model;
            obj.ownerIcaoType = icaoType;
            obj.ownerIcaoAirline = icaoAirline;
            obj.ownerLivery = livery;
            obj.ownerClassCode = classCode;
            obj.ownerWtc = wtc;
            obj.ownerClassCodeConfirmed = classCodeConfirmed;
#if FS2024
            // model match - livery is only meaningful as a matching signal on FS2024, which is the
            // only sim that reports a real livery name via SimConnect; other builds still carry the
            // value through (e.g. for network relay) even though they can never populate it locally
            (obj.subModel, obj.subType, obj.subTrace) = await main.substitution?.Match(obj.ownerModel, obj.ownerLivery, obj.ownerIcaoType, obj.ownerIcaoAirline, obj.ownerClassCode, obj.ownerWtc, obj.ownerClassCodeConfirmed, obj.typerole, (obj as Aircraft)?.flightPlan.registration ?? "");
#else
            (obj.subModel, obj.subType, obj.subTrace) = await main.substitution ?. Match(obj.ownerModel, obj.ownerIcaoType, obj.ownerIcaoAirline, obj.ownerClassCode, obj.ownerWtc, obj.ownerClassCodeConfirmed, obj.typerole, (obj as Aircraft)?.flightPlan.registration ?? "");
#endif
            // reset failed flag
            obj.failed = false;
        }

        /// <summary>
        /// Update object position directly
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="positionVelocity">Position</param>
        public void UpdateObject(Obj obj, ref ObjectPosition position)
        {
#if XPLANE || CONSOLE
            // update xplane position
            xplane.UpdateAircraft(obj.simId, ref position);
#elif SIMCONNECT
            // check for valid object
            if (simconnect != null && obj.Created)
            {
                // update object position and velocity - forward on-ground to the sim's own placement whenever the
                // sender reports the object on-ground, for any aircraft type, so the sim's own gear/ground-contact
                // physics handles it instead of fighting an externally-driven altitude every tick (see
                // helicopters-on-elevated-platforms feature)
                ObjectPositionUpdate update = new(ref position)
                {
                    // Regime A (ordinary ground): forward SIM ON GROUND so the sim re-seats the object on
                    // its own gear. Regime B (elevated platform - trustingPlatformElevation): withhold it.
                    // In Regime B the object is held above absent local geometry purely by position control;
                    // telling the local sim it's "on ground" invites it to re-seat the object on the terrain
                    // far below every frame JoinFS isn't actively forcing - the platform-jitter bug (Fix 1d).
                    ground = (obj.trustingPlatformGround && obj.trustingPlatformElevation == false) ? 1 : 0
                };
                simconnect.SetData(Definitions.OBJECT_POSITION_UPDATE, obj.simId, update);
                // update stored position
                obj.simPosition = new Pos(ref position);
            }
#endif
            // store current time
            obj.simTime = main.ElapsedTime;
        }

        /// <summary>
        /// Update object velocity directly
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="positionVelocity">Velocity</param>
        public void UpdateObject(Obj obj, ref ObjectVelocity velocity)
        {
#if XPLANE || CONSOLE
            // update xplane position
            xplane.UpdateAircraft(obj.simId, ref velocity);
#elif SIMCONNECT
            // check for valid aircraft
            if (simconnect != null && obj.Created)
            {
                // update object position and velocity
                simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, velocity);
            }
#endif
        }

        /// <summary>
        /// Update object position
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="position">Position</param>
        public void UpdateObject(Obj obj, Pos position)
        {
            // check for valid object
            if (Connected && obj.Created)
            {
                // set position
                ObjectPosition objectPosition = new(position);
                // update object
                UpdateObject(obj, ref objectPosition);
            }
        }

        /// <summary>
        /// Update object velocity
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="velocity">Velocity</param>
        public void UpdateObject(Obj obj, Pos position, Vel velocity)
        {
            // check for valid object
            if (Connected && obj.Created)
            {
                // update object position
                UpdateObject(obj, position);
                ObjectVelocity objectVelocity = new(velocity.linear.InvRotate(position.angles), velocity.angular, velocity.acc.InvRotate(position.angles));
                // update object
                UpdateObject(obj, ref objectVelocity);
            }
        }

        /// <summary>
        /// Update network time
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="netTime">Network time</param>
        public void UpdateObject(Obj obj, double netTime)
        {
            // store remote state time
            obj.netStateTime = netTime;
            // check for first update
            if (obj.netRealTime == 0.0)
            {
                // set position and velocity
                UpdateObject(obj, obj.netPosition, obj.netVelocity);
                // set time
                obj.netRealTime = obj.netStateTime;
            }
            else
            {
                // update estimated network time
                obj.netRealTime += main.ElapsedTime - obj.netSimTime;
                // calculate error between network update and estimated time
                double error = obj.netStateTime - obj.netRealTime;
                // gradually merge to remove error over time
                obj.netRealTime += error * TIME_ERROR_RATE;
            }
            // store local time at which state was updated
            obj.netSimTime = main.ElapsedTime;
        }

        /// <summary>
        /// Update object position and velocity
        /// </summary>
        /// <param name="obj">Object</param>
        /// <param name="netTime">Network time</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void UpdateObject(Obj obj, double netTime, ref ObjectPositionVelocity positionVelocity)
        {
            // check for first update and reject old updates
            if (obj.NetValid == false || netTime > obj.netStateTime)
            {
                // update position
                obj.netPosition = new Pos(ref positionVelocity);
                // save old orientation
                obj.oldEuler = obj.netPosition.angles.Clone();
                // update velocity
                obj.netVelocity = new Vel(ref positionVelocity);

                // update network time
                UpdateObject(obj, netTime);
            }
        }

        /// <summary>
        /// Update object position and velocity
        /// </summary>
        /// <param name="ownerGuid">Owner of the object</param>
        /// <param name="netId">Owner's sim ID</param>
        /// <param name="engine">Aircraft engine</param>
        public Obj UpdateObject(LocalNode.Nuid ownerNuid, uint netId, string model, string livery, string icaoType, string icaoAirline, string classCode, string wtc, bool classCodeConfirmed, int typerole, double netTime, ref ObjectPositionVelocity positionVelocity)
        {
            // get object
            Obj obj = objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId);
            if (obj == null)
            {
                // create new object in list
                obj = new(ownerNuid, netId)
                {
                    // set expire time
                    expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME
                };
                // model
                UpdateObject(obj, model, livery, icaoType, icaoAirline, classCode, wtc, classCodeConfirmed, typerole);
                // update position and velocity
                UpdateObject(obj, netTime, ref positionVelocity);
                // create variables
                CreateModelVariables(obj);
                // add to object list
                objectList.Add(obj);

                // message
                main.MonitorEvent("Listing object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - Model '" + obj.ownerModel + "'");

                return obj;
            }
            else
            {
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;

                // check if model has changed
                if (model.Equals(obj.ownerModel) == false)
                {
                    // check if creating this object
                    if (creatingObject != obj)
                    {
                        // remove object
                        RemoveObjectFromSim(obj);
                    }
                }
                else
                {
                    // update position and velocity
                    UpdateObject(obj, netTime, ref positionVelocity);
                }

                return obj;
            }
        }

        /// <summary>
        /// Pause or unpause an object
        /// </summary>
        /// <param name="ownerNuid">Owner ID</param>
        /// <param name="netId">Network ID</param>
        /// <param name="pause">Pause state</param>
        public void PauseObject(LocalNode.Nuid ownerNuid, uint netId, bool pause)
        {
            // check for valid object
            if (objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is not null) is Obj obj)
            {
                // update state
                obj.paused = pause;
            }
        }

        /// <summary>
        /// Prevent object from timing out
        /// </summary>
        /// <param name="ownerNuid">Owner ID</param>
        /// <param name="netId">Network ID</param>
        public void TouchObject(LocalNode.Nuid ownerNuid, uint netId)
        {
            // check for valid object
            if (objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is not null) is Obj obj)
            {
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;
            }
        }

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

#if SIMCONNECT
        /// <summary>
        /// Update object velocity in the simulator
        /// </summary>
        /// <param name="aircraft"></param>
        void UpdateSimObjectVelocity(Obj obj)
        {
            try
            {
                // check for controlled object with valid position
                if (simconnect != null && obj.remoteFlightControl && obj.SimValid && obj.NetValid && obj.Created)
                {
                    // check if object is paused
                    if (obj.paused)
                    {
                        // reset to target position
                        UpdateObject(obj, obj.netPosition);
                        // zero sim velocity
                        simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity());
#if (FS2020 || FS2024)
                        // set orientation
                        simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(obj.netPosition.angles));
                        obj.simPosition.angles = obj.netPosition.angles.Clone();
#endif
                    }
                    else
                    {
                        float delay = 0.0f;
                        if (obj.owner == Obj.Owner.Network)
                        {
                            // Pass the network delay through a low-pass filter to smooth out the values
                            // and avoid jittering.
                            // Get the current delay and the previous delay
                            // Previous delay is a property of a node, but assigning it to the object
                            // makes for quicker access to the value. Ugly, but it here time matters.
                            float prevDelay = obj.prevDelay;
                            delay = main.network.localNode.GetNodeRTT(obj.ownerNuid);
                            float alpha = 0.75f;
                            delay = alpha * delay + (1.0f - alpha) * prevDelay;
                            obj.prevDelay = delay;
                        }

                        // calculate time deltas
                        double simDeltaTime = main.ElapsedTime - obj.simTime;
                        // delay is measured round-trip, so divide by two
                        double netDeltaTime = obj.netRealTime - obj.netStateTime + main.ElapsedTime - obj.netSimTime + 0.52*delay;
                        // limit extraploation to two seconds
                        simDeltaTime = Math.Min(2.0, Math.Max(-2.0, simDeltaTime));
                        netDeltaTime = Math.Min(2.0, Math.Max(-2.0, netDeltaTime));
                        // extrapolate positions and velocity
                        Pos simPosition = obj.simPosition.Extrapolate(obj.netVelocity, simDeltaTime);
                        Pos netPosition = obj.netPosition.Extrapolate(obj.netVelocity, netDeltaTime);
                        Vel netVelocity = obj.netVelocity.Extrapolate(netDeltaTime);

                        // get V between network and sim positions
                        double distance = Vector.GeodesicDistance(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);
                        double bearing = Vector.GeodesicBearing(simPosition.geo.x, simPosition.geo.z, netPosition.geo.x, netPosition.geo.z);

                        // largest difference in altitude before reset
                        double altitudeDeltaLimit = 50.0;
#if (FS2020 || FS2024)
                        // FS2020 has an issue where the aircraft remains glued to the ground, so reset much earlier when
                        // the altitude diverts on the ground - but 0.2m was too tight once ground placement started
                        // depending on a per-model computed correction (STATIC CG TO GROUND-based grounding, Elevation
                        // Correction): any gap over 20cm between where our computed target altitude sits and where the
                        // object's own gear/suspension physics has already settled it forced a hard position reset every
                        // time, which the sim's own gear physics immediately fights - visible as jitter that some
                        // substitutes (e.g. many FSLTL models) needed a large manual height adjustment to escape, because
                        // lifting the object off the ground exempted it from this tight threshold entirely (reverting to
                        // the loose 50m limit below) and let it resettle smoothly under the sim's own physics instead.
                        // 1.5m still corrects a genuinely wrong/stuck placement far sooner than the airborne case, while
                        // comfortably absorbing realistic per-model ground-clearance imprecision instead of fighting it.
                        //
                        // simPosition.ground is the SimConnect read-back of the INJECTED object's own on-ground bit,
                        // and it is unreliable for an injected object in OPPOSITE directions on the two sims:
                        // MSFS2024 frequently never sets it (a parked injected helicopter reports rawGround=0
                        // indefinitely), while MSFS2020 "sticks" an object on-ground once it has been placed there
                        // (see the "glued to the ground" note above and SimConnectInterface.CreateObject) - so for a
                        // network aircraft its pilot is actually flying/hovering, MSFS2020's read-back reports
                        // rawGround=1 regardless of the altitude JoinFS commands, which used to force Regime A and
                        // hand the vertical axis to the sim's gear physics - dropping the hovering aircraft onto the
                        // local terrain and making manual height adjustment impossible. Trust only the SENDER's own
                        // debounced on-ground flag (trustingPlatformGround) - the sender is authoritative about
                        // whether its own aircraft is on the ground. This also matches UpdateAircraft's own regimeA
                        // decision, which is already keyed on trustPlatformGround alone.
                        bool onGround = obj.trustingPlatformGround;
                        // two on-ground regimes (see Fix 1/2):
                        //  Regime A - ordinary ground: the sim's own gear-contact physics owns the vertical
                        //    axis AND pitch/bank; JoinFS commands only horizontal position + heading, and
                        //    the vertical hard-reset tolerance is deliberately generous (divergence is now
                        //    expected). This is what fixes nose-gear-up and the size-mismatch jitter.
                        //  Regime B - elevated platform: nothing is handed to the sim; full position (incl.
                        //    altitude) and full sender attitude are forced every frame with a tight reset,
                        //    so the sim can never drop the object onto the absent terrain below.
                        bool regimeB = onGround && obj.trustingPlatformElevation;
                        bool regimeA = onGround && obj.trustingPlatformElevation == false;
                        if (regimeA) altitudeDeltaLimit = main.settingsGroundAltitudeDeltaLimit;
                        else if (regimeB) altitudeDeltaLimit = 0.2;

                        // While the sender is on the ground, force the substitute's gear handle down,
                        // bypassing the model-variable change/delay gate (SLAVE_DELAY) so the injected AI
                        // aircraft's own per-frame gear-phase logic can't retract it - see Fix 1f. On the
                        // ground the gear must be down for the contact points to resolve; this is correct for
                        // a fixed-gear original and for a retractable original that has landed. Planes only -
                        // a helicopter substitute has no retractable gear (skids), so this would be a no-op.
                        // Throttled to once/second (not every tick, unlike the first cut of this fix) -
                        // there's no need to fight the AI logic more often than that, and it cuts a brand
                        // new per-frame native SetDataOnSimObject call down to a rare one.
                        if (onGround && obj is Plane)
                        {
                            if (obj.gearForcedDown == false || main.ElapsedTime >= obj.nextGearForceTime)
                            {
                                simconnect.SetData(Definitions.OBJECT_GEAR, obj.simId, new IntegerStruct { value = 1 });
                                obj.gearForcedDown = true;
                                obj.nextGearForceTime = main.ElapsedTime + 1.0;
                            }
                        }
                        else
                        {
                            obj.gearForcedDown = false;
                        }
#endif

                        // check if object is beyond specific distance
                        if (distance > 50.0 || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit)
                        {
#if (FS2020 || FS2024)
                            if (regimeA)
                            {
                                // reseed horizontal position + the Fix 1b altitude target, hand pitch/bank
                                // back to the sim (its own contact-point read-back), command heading only.
                                // BUG FIX: UpdateObject(obj, netPosition) builds its OBJECT_POSITION_UPDATE
                                // straight from netPosition.angles - the SENDER's raw pitch/bank - not the
                                // resetAngles computed below. Sending that first and only correcting to
                                // resetAngles a moment later (via the separate OBJECT_EULER call) snapped a
                                // large-mismatch substitute (e.g. a taildragger replacing a tricycle-gear
                                // original) toward the sender's attitude and back on every hard-reset during
                                // the settle, compounding the sim's own gear-physics bounce. Give the reset
                                // position the corrected angles up front so both SetData calls agree.
                                Vector resetAngles = new(simPosition.angles.x, netPosition.angles.y, simPosition.angles.z);
                                Pos resetPosition = netPosition.Clone();
                                resetPosition.angles = resetAngles;
                                UpdateObject(obj, resetPosition);
                                netVelocity.linear.y = 0.0;
                                simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc));
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(resetAngles));
                                obj.simPosition.angles = resetAngles;
                            }
                            else
                            {
#endif
                            // reset to target position
                            UpdateObject(obj, netPosition);
                            // update sim velocity
                            simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc));
#if (FS2020 || FS2024)
                            // set orientation
                            simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                            obj.simPosition.angles = netPosition.angles.Clone();
                            }
#endif
                        }
                        else
                        {
                            // get world space relative position
                            Vector deltaGeo = new(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
                            // get delta between current and network orientations
                            Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

#if (FS2020 || FS2024)
                            // BUG FIX (post-26.5.1-hotfix regression): hard-zeroing deltaGeo.y here (as an
                            // earlier version of this branch did) deletes the corrective error term itself,
                            // not just the base extrapolated velocity - since JoinFS is the one authoritatively
                            // commanding OBJECT_VELOCITY every tick, that froze the object at whatever altitude
                            // it happened to be when the branch engaged (no other force was left to move it),
                            // with only the then-3m hard-reset as an escape hatch - producing a sawtooth of
                            // "drift up to just under the reset threshold, snap back, repeat" that read as a
                            // constant ~3m-high float with heavy jitter. Restore the proven dead-band + reduced-
                            // gain correction (same as the pre-regime baseline): the error term survives, just
                            // damped, so the object actually converges onto the Fix 1b seed instead of freezing.
                            // Regime A also hands pitch/bank to the sim (don't add those angular-catch-up
                            // components - a substitute with a longer nose-to-CG moment arm than the original
                            // turns even a small forced-pitch mismatch into a large gap at the nose gear).
                            // Regime B: hold the sender's altitude and full attitude - the object floats above
                            // absent local geometry purely by position control.
                            if (regimeA)
                            {
                                // A true dead-band (zero correction below 0.15m) can produce its own
                                // limit-cycle for some substitutes: whatever the sim's own physics naturally
                                // settles this specific model to (tire compression, terrain-penetration
                                // handling, etc.) doesn't sit exactly at our computed target - for a large/
                                // heavy substitute the gap can persistently exceed the dead-band's lower
                                // edge, so the object sinks in unopposed until it crosses -0.15m, correction
                                // kicks in and pushes it back up past the edge, cuts off completely again,
                                // and it sinks back in - a sustained hop bounded almost exactly by the
                                // dead-band boundary (DC-3/Savage Gravel field reports).
                                //
                                // A plain always-on weak gain inside the band (tried first) removed that
                                // edge, but replaced it with a small continuous fight against the sim's own
                                // settling for EVERY on-ground substitute, not just the mismatched ones -
                                // reintroducing jitter broadly. Use hysteresis instead: engage correction at
                                // the same 0.15m threshold as before, but once engaged, don't release it
                                // again until the error is much smaller (0.03m) rather than immediately at
                                // the 0.15m edge. A substitute that never leaves the 0.15m band gets zero
                                // ongoing correction, exactly as before (no new jitter); one with a
                                // persistent gap gets pulled all the way in before release, instead of
                                // stopping right at the edge and falling back out (no more boundary hop).
                                //
                                // Separately: for a large gear-geometry mismatch (e.g. a tricycle original
                                // substituted by a taildragger), the sim's own attitude settle (tail lowering
                                // onto its wheel) and JoinFS's vertical correction toward a single FIXED
                                // target altitude are fighting a coupling neither side accounts for - the
                                // true CG-to-ground height for a taildragger genuinely depends on its current
                                // pitch, but our target doesn't change as pitch rotates. That shows up as the
                                // object settling near-level first (matching the target reasonably well at
                                // that attitude), then sinking a further few cm exactly as the tailwheel
                                // reaches the ground and pitch stops changing - visible as jitter right at
                                // that handoff. Give every freshly (re)created object a clear run to finish
                                // its own attitude settle before JoinFS's vertical nudge engages at all (the
                                // hard-reset safety net above is unaffected - only this gentle catch-up is
                                // suppressed).
                                bool verticalCorrectionSuppressed = main.ElapsedTime < obj.verticalCorrectionSuppressedUntil;
                                double rawDeltaYForDiag = deltaGeo.y;
                                if (verticalCorrectionSuppressed)
                                {
                                    deltaGeo.y = 0.0;
                                }
                                else
                                {
                                    double absDeltaY = Math.Abs(deltaGeo.y);
                                    if (absDeltaY > 0.15)
                                    {
                                        obj.verticalCorrectionActive = true;
                                    }
                                    else if (absDeltaY < 0.03)
                                    {
                                        obj.verticalCorrectionActive = false;
                                    }
                                    deltaGeo.y = obj.verticalCorrectionActive ? deltaGeo.y * 0.2 : 0.0;
                                }
                                // diagnostic only - see the hop-up-and-float-down field reports. Reveals whether
                                // verticalCorrectionActive is genuinely releasing (settling) or stuck permanently
                                // engaged (never converging inside the 0.03m release threshold, so the correction
                                // fights the sim's own settling forever instead of going quiet).
                                if (main.settingsTraceDiagnostics && main.ElapsedTime >= obj.nextVerticalCorrectionDiagLogTime)
                                {
                                    obj.nextVerticalCorrectionDiagLogTime = main.ElapsedTime + 0.2;
                                    main.MonitorNetwork("VerticalCorrection '" + (obj is Aircraft vcAircraft ? vcAircraft.flightPlan.callsign : obj.simId.ToString()) + "' model='" + obj.ModelTitle + "'" +
                                        " suppressed=" + verticalCorrectionSuppressed + " active=" + obj.verticalCorrectionActive +
                                        " rawDeltaY=" + rawDeltaYForDiag.ToString("F3") + "m appliedDeltaY=" + deltaGeo.y.ToString("F3") + "m" +
                                        " elapsedTime=" + main.ElapsedTime.ToString("F1"));
                                }
                                netVelocity.linear.y = 0.0;
                                deltaAngles.x = 0.0;
                                deltaAngles.z = 0.0;
                            }
                            else if (regimeB)
                            {
                                deltaGeo.y = Math.Abs(deltaGeo.y) < 0.05 ? 0.0 : deltaGeo.y;
                                netVelocity.linear.y = 0.0;
                            }
                            // orientation the sim is allowed to see this frame
                            Vector groundAngles = regimeA
                                ? new Vector(simPosition.angles.x, netPosition.angles.y, simPosition.angles.z)
                                : netPosition.angles;
                            // Regime A: simPosition.angles only actually changes at the (much slower) local
                            // poll rate feeding it - the extrapolation above carries it forward essentially
                            // unchanged between polls since JoinFS deliberately drives no pitch/bank rate of
                            // its own here. Re-sending the identical value every visual frame (30-60Hz)
                            // anyway is a known class of AI-object animation bug: SetDataOnSimObject on
                            // orientation can restart whatever in-flight interpolation/settle animation the
                            // sim is running, even when the value hasn't materially changed, which could be
                            // fighting - not just failing to help - a slow gear-compression settle (e.g. a
                            // taildragger's tail taking a long time to come down after a substitution). Only
                            // re-send when it actually moved.
                            const double groundEulerEpsilon = 0.05 * Math.PI / 180.0;
                            bool sendGroundEuler = regimeA == false
                                || Math.Abs(Vector.AngleDelta(obj.simPosition.angles.x, groundAngles.x)) > groundEulerEpsilon
                                || Math.Abs(Vector.AngleDelta(obj.simPosition.angles.y, groundAngles.y)) > groundEulerEpsilon
                                || Math.Abs(Vector.AngleDelta(obj.simPosition.angles.z, groundAngles.z)) > groundEulerEpsilon;
#endif
                            // add delta to velocity to catch up
                            netVelocity.linear += deltaGeo * 1.5;

                            // only catch up the orientation if no high angular turns are being made
                            if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
                            {
                                if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
                                {
                                    // add delta to angular velocity to catch up
                                    netVelocity.angular += deltaAngles * 1.5;
                                }
#if (FS2020 || FS2024)
                                // set orientation
                                if (sendGroundEuler)
                                {
                                    simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(groundAngles));
                                    obj.simPosition.angles = groundAngles.Clone();
                                }
#endif
                            }
                            else
                            {
#if (FS2020 || FS2024)
                                // set orientation
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(groundAngles));
                                obj.simPosition.angles = groundAngles.Clone();
#else
                                // set orientation
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                                obj.simPosition.angles = netPosition.angles.Clone();
#endif
                            }

                            // update sim velocity
                            simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity(netVelocity.linear.InvRotate(simPosition.angles), netVelocity.angular * 0.3, netVelocity.acc.InvRotate(simPosition.angles)));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR - " + ex.Message);
            }
        }

#endif
        /// <summary>
        /// Get the object that should be controlled
        /// </summary>
        /// <param name="obj">Object to check</param>
        /// <returns>Controlled object</returns>
        Obj GetControlledObject(Obj obj)
        {
            // check for entered aircraft
            if (userAircraft != null && (obj == enteredAircraft || userAircraft.remoteFlightControl && obj.ownerNuid == main.network.shareFlightControls && obj is Aircraft && (obj as Aircraft).user))
            {
                // control user aircraft instead
                return userAircraft;
            }

            // control specified object
            return obj;
        }

        /// <summary>
        /// Is this model part of Tacpack
        /// </summary>
        /// <returns></returns>
        public static bool IsTacpackModel(string model)
        {
            // check for valid model
            if (model != null)
            {
                //// check for "FSXatWar"
                //if (model.Length >= 8 && model.Substring(0, 8).Equals("FSXatWar", StringComparison.OrdinalIgnoreCase))
                //{
                //    return true;
                //}

                // check for "VRS_"
                if (model.Length >= 4 && model[..4].Equals("VRS_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // check for "VACMI"
                if (model.Length >= 5 && model[..5].Equals("VACMI", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // not Tacpack
            return false;
        }

        /// <summary>
        /// Is the object to be broadcast
        /// </summary>
        /// <param name="obj">Object</param>
        /// <returns>Broadcast</returns>
        public bool IsBroadcast(Obj obj)
        {
            // get altitude
            double altitude = obj.Position != null ? obj.Position.geo.y * Sim.FEET_PER_METRE : 0.0;
            return obj.owner != Obj.Owner.Network && altitude < 150000.0 && (obj.broadcast || main.log.BroadcastName(obj.ModelTitle) || Settings.Default.AutoBroadcast || Settings.Default.BroadcastTacpack && IsTacpackModel(obj.ModelTitle));
        }

#endregion

#region Aircraft

        static short ConvertToAxis(float input) { return (short)(input * 16384.0); }
        static float ConvertFromAxis(short input) { return (float)(int)input * (1.0f / 16384.0f); }

        /// <summary>
        /// Resolve a real-world callsign from ICAO airline + flight number (e.g. "DLH" + "1234" -> "DLH1234").
        /// SimConnect's ATC ID is a tail number, not a callsign, and there is no single SimConnect variable that
        /// delivers a combined real-world callsign - so fall back to the tail number when either part is missing.
        /// </summary>
        internal static string ResolveCallsign(string icaoAirline, string flightNumber, string tailNumber)
        {
            if (!string.IsNullOrEmpty(icaoAirline) && !string.IsNullOrEmpty(flightNumber))
            {
                // ATC FLIGHT NUMBER is meant to be purely numeric, but it was write-only/unread by JoinFS
                // before this feature existed, so many add-ons/pilots instead stored an entire pre-existing
                // callsign there. If it already carries the airline prefix, or already has the shape of a
                // complete airline callsign (AirlineCallsignRegex - 3-letter designator + digits + optional
                // trailing letters), trust it as complete rather than gluing the airline code onto it again.
                // A plain non-digit check here is too broad: real-world flight numbers routinely carry their
                // own trailing letter suffix (e.g. "34U", schedule/period variants) without being a full
                // callsign at all - that would wrongly skip the "EWG" + "34U" -> "EWG34U" synthesis and use
                // the bare flight number as the callsign.
                if (flightNumber.StartsWith(icaoAirline, StringComparison.OrdinalIgnoreCase) || AirlineCallsignRegex().IsMatch(flightNumber.Trim().ToUpperInvariant()))
                {
                    return flightNumber;
                }
                return icaoAirline + flightNumber;
            }
            return tailNumber;
        }

        /// <summary>Matches a commercial-airline-shaped callsign: 3-letter ICAO airline designator, 1-4 digit
        /// flight number, optional trailing letters (e.g. "DLH1234", "BAW456A", "UAL2345"). Group 1 captures
        /// the designator. General Aviation tail-number callsigns ("N12345", "D-EJOE") don't match.</summary>
        [GeneratedRegex(@"^([A-Z]{3})\d{1,4}[A-Z]{0,3}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex AirlineCallsignRegex();

        /// <summary>
        /// Derive the ICAO airline designator from a callsign's shape, for use as a fallback when nothing
        /// more authoritative (SimBrief, live sim/config data) already supplied one. Returns "" when the
        /// callsign doesn't look like a commercial airline flight (General Aviation), so this is safe to try
        /// unconditionally.
        /// </summary>
        internal static string DeriveIcaoAirlineFromCallsign(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return "";
            Match m = AirlineCallsignRegex().Match(callsign.Trim().ToUpperInvariant());
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>
        /// Flight plan
        /// </summary>
        public class FlightPlan
        {
            public const int MAX_ROUTE = 512;
            public const int MAX_REMARKS = 512;

            public string callsign = "";
            /// <summary>
            /// True once callsign has been explicitly set via SimBrief import or manual FlightPlanForm
            /// entry - once true, SimConnect-derived defaults (the raw tail-number fallback, or the
            /// ATC-FLIGHT-NUMBER-based ResolveCallsign synthesis) must never overwrite it again.
            /// </summary>
            public bool callsignSetByUser = false;
            public string registration = "";
            public string icaoType = "";
            public string icaoAirline = "";
            public string flightNumber = "";
            public string departure = "";
            public string destination = "";
            public string rules = "";
            public string route = "";
            public string remarks = "";
            public string alternate = "";
            public string speed = "";
            public string altitude = "";
        }

        // user's main flight plan
        public FlightPlan userFlightPlan = new();

        /// <summary>
        /// Callsign/type last acted on by the own-aircraft-changed auto-refresh (see ProcessSimObjectData's
        /// Requests.OBJECT_INFO handling) - compared against the freshly-resolved callsign/type on every "Me"
        /// info update to detect a real aircraft/callsign change. Deliberately lives here on Sim rather than
        /// on the Aircraft object itself: a genuine aircraft swap creates a brand-new Aircraft instance, so a
        /// per-instance field would always start empty and could never detect that case - this needs to
        /// survive across object recreation to compare the previous aircraft's identity against the new one.
        /// Empty means "not yet initialized" (first sighting this session - record only, no refresh, so this
        /// doesn't fight the ordinary reconnect/respawn "flight plan survives" behavior or duplicate the
        /// app-startup SimBrief auto-import trigger).
        /// </summary>
        string lastKnownUserCallsign = "";
        string lastKnownUserIcaoType = "";

        /// <summary>
        /// State of the most recent SimBrief fetch attempt, for the main-screen SimBrief button's coloring -
        /// NotTriggered (neutral/default, like the flight plan button) until a fetch has actually happened,
        /// auto or manual, distinguishing "never asked" from "asked and failed".
        /// </summary>
        public enum SimBriefFetchState { NotTriggered, Fetching, Success, Failed }

        /// <summary>
        /// Result of the most recent SimBrief fetch attempt, for the main-screen SimBrief button's coloring
        /// </summary>
        public SimBriefFetchState simBriefFetchState = SimBriefFetchState.NotTriggered;

#if !CONSOLE
        /// <summary>
        /// Fetch the pilot's latest SimBrief OFP and apply it to the user's flight plan if found
        /// </summary>
        public async Task<bool> RefreshUserFlightPlanFromSimBriefAsync()
        {
            simBriefFetchState = SimBriefFetchState.Fetching;
            bool ok = await JoinFS.SimBrief.FetchAsync(Settings.Default.SimBriefUsername, userFlightPlan, main);
            simBriefFetchState = ok ? SimBriefFetchState.Success : SimBriefFetchState.Failed;
            if (ok)
            {
                main.MonitorEvent("SimBrief flight plan imported: " + userFlightPlan.departure + " -> " + userFlightPlan.destination);
                // don't lock out the SimConnect fallback if SimBrief didn't actually provide a callsign
                if (userFlightPlan.callsign.Length > 0)
                {
                    userFlightPlan.callsignSetByUser = true;
                }
            }
            return ok;
        }
#endif

        /// <summary>
        /// Aircraft
        /// </summary>
        public abstract partial class Aircraft : Obj
        {
            public bool user = false;
            public string originalCallsign = "";
            /// <summary>
            /// ICAO type designator as first reported by the sim for this aircraft object, kept separate
            /// from flightPlan.icaoType (which the Flight Plan dialog's OK handler permanently overwrites
            /// with whatever was typed) so a later "fetch fresh from the sim" (see FlightPlanForm's Clear
            /// button) has something live to read back - same reasoning as originalCallsign.
            /// </summary>
            public string originalIcaoType = "";
            public byte flightPlanVersion = 0;
            public FlightPlan flightPlan = new();
            public byte cockpitShare = 0;
            public string airport = "";
            public string metar = "";
            public string wind = "";

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Aircraft(uint simId, string callsign, string icaoType, string model, string livery, string icaoAirline, bool isUser)
            {
                // set sim ID
                this.simId = simId;
                netId = simId;
                // update info
                originalCallsign = callsign;
                originalIcaoType = icaoType;
                flightPlan.callsign = callsign;
                // ATC ID is actually the tail number/registration, not a real callsign - see Substitution.cs
                flightPlan.registration = callsign;
                flightPlan.icaoType = icaoType;
                ownerModel = model;
                ownerLivery = livery;
#if FS2024
                flightPlan.icaoAirline = icaoAirline;
#endif
                subModel = null;
                // check for this user
                if (isUser)
                {
                    // user is the owner
                    owner = Owner.Me;
                    user = true;
                    broadcast = true;
                    record = true;
                }
                else
                {
                    owner = Owner.Sim;
                }
            }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Aircraft(LocalNode.Nuid ownerNuid, uint netId) : base(ownerNuid, netId)
            {
            }

            /// <summary>
            /// Set weather for this aircraft
            /// </summary>
            /// <param name="metar">Metar observation</param>
            public void SetWeather(string metar)
            {
                // update metar
                this.metar = metar;
                // find wind in metar
                Match match = MetarWindRegex().Match(metar);
                // check if found
                if (match.Success)
                {
                    // set wind
                    this.wind = match.Value;
                }
                else
                {
                    // find wind in metar
                    match = MetarWindMpsRegex().Match(metar);
                    // check if found
                    if (match.Success)
                    {
                        // set wind
                        this.wind = match.Value;
                    }
                }
            }

            [GeneratedRegex(@"\d{5}KT", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
            private static partial Regex MetarWindRegex();

            [GeneratedRegex(@"\d{5}MPS", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
            private static partial Regex MetarWindMpsRegex();

            /// <summary>
            /// State of shared cockpit
            /// </summary>
            public bool CockpitShared { get { return (cockpitShare & 0x01) != 0; } }
            public bool FlightControlsShared { get { return (cockpitShare & 0x02) != 0; } }
        }

        /// <summary>
        /// Plane
        /// </summary>
        public class Plane : Aircraft
        {

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Plane(uint simId, string callsign, string type, string model, string livery, string icaoAirline, bool isUser) : base(simId, callsign, type, model, livery, icaoAirline, isUser) { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Plane(LocalNode.Nuid ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }

        /// <summary>
        /// Helicopter
        /// </summary>
        public class Helicopter : Aircraft
        {

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Helicopter(uint simId, string callsign, string type, string model, string livery, string icaoAirline, bool isUser) : base(simId, callsign, type, model, livery, icaoAirline, isUser) { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Helicopter(LocalNode.Nuid ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }

        /// <summary>
        /// Current user aircraft
        /// </summary>
        public Aircraft userAircraft = null;

        /// <summary>
        /// Boat
        /// </summary>
        public class Boat : Obj
        {
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            //// was public Boat(uint simId, string callsign, string type, string model, bool isUser) : base(simId, model) { }
            public Boat(uint simId, string model) : base(simId, model) { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Boat(LocalNode.Nuid ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }

        /// <summary>
        /// Vehicle
        /// </summary>
        public class Vehicle : Obj
        {
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            /// was public Vehicle(uint simId, string callsign, string type, string model, bool isUser) : base(simId, model) { }
            public Vehicle(uint simId, string model) : base(simId, model) { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="ownerGuid">Node</param>
            /// <param name="netId">Network ID</param>
            public Vehicle(LocalNode.Nuid ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }

        /// <summary>
        /// Update aircraft position and velocity
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        /// <param name="netTime">Network time</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void UpdateAircraft(Aircraft aircraft, double netTime, AircraftPosition aircraftPosition)
        {
            // check for non user or remote controlled user
            if (aircraft != userAircraft || aircraft.remoteFlightControl)
            {
                // check for first update and reject old updates
                if (aircraft.NetValid == false || netTime > aircraft.netStateTime)
                {
                    // elevated platform (helipad/ship deck/rooftop) ground-trust check - see helicopters-on-elevated-platforms feature.
                    // two related but distinct decisions are made here:
                    //  - trustingPlatformElevation: whether to skip the elevation-correction blend below and trust the sender's raw
                    //    altitude. This can apply on final approach too, before the sender reports on-ground - waiting for the ground
                    //    flag let the blend keep dragging the aircraft down toward our local bare-terrain reading right up until
                    //    touchdown, then snap back up once the ground flag arrived, reported as "sinks through the platform, then
                    //    gets lifted onto it".
                    //  - trustingPlatformGround: whether to forward on-ground to the local sim's own placement of the object (which
                    //    affects local physics/animation, e.g. gear compression, rotor spin-down). Unconditional whenever the sender
                    //    reports on-ground, for any aircraft type - not really elevated-platform logic, just lets the local sim's own
                    //    ground-contact physics handle it instead of fighting an externally-driven altitude every tick.
                    // Trust engagement/release uses hysteresis (the release threshold is half the trust threshold) so a mismatch
                    // hovering near the configured threshold doesn't flip the decision every update. A previous version of this
                    // logic additionally tried to confirm/revoke trust reactively against a PLANE ALT ABOVE GROUND (radarHeight)
                    // readback of the injected object - removed: that simvar is only meaningful for the user's own physically
                    // simulated aircraft (a reference implementation, swift/pilotclient, registers it exclusively on its own-aircraft
                    // data definition, never for AI-injected traffic), and for an injected object with a persistent mismatch it never
                    // resolved, producing a continuous engage/fail/revoke/re-engage cycle each update - visible as jitter, since each
                    // flip snaps between the raw sender altitude and the corrected blended altitude. senderIndicatesElevation below
                    // is the mitigation for the false-positive case that mechanism was trying to solve instead: it requires the
                    // sender's own reading (self-consistent, immune to cross-install bare-terrain datum noise, unlike the cross-
                    // install mismatch check alone) to itself indicate real elevation before trust engages.
                    double mismatchCm = 0.0;
                    double senderHeight = double.NaN;
                    bool trustPlatformElevation = aircraft.trustingPlatformElevation;
                    if (main.settingsElevatedPlatformRecognition && aircraft.SimValid)
                    {
                        senderHeight = aircraftPosition.altitude - aircraftPosition.elevation;
                        double localHeight = aircraftPosition.altitude - aircraft.simPosition.elevation;
                        mismatchCm = Math.Abs(localHeight - senderHeight) * 100.0;

                        bool nearGround = aircraftPosition.ground != 0 || senderHeight < 50.0;
                        // "indicates elevation" has to mean the sender's own gear is resting meaningfully above the
                        // sender's own bare terrain - i.e. on a structure. senderHeight alone (altitude - GROUND
                        // ALTITUDE) is ~= the aircraft's static CG-to-ground clearance even on perfectly flat ground
                        // (about 1.5 m for a helicopter, 3-5 m for an airliner), so a flat threshold on it is
                        // satisfied by essentially every grounded aircraft and does no filtering at all - which left
                        // a routine cross-install terrain-mesh mismatch (>= threshold) as the only real gate and made
                        // elevation-trust engage on ordinary ground, burying the substitute below the local mesh and
                        // fighting the sim's terrain-penetration correction as jitter. Subtracting the sender's own
                        // reported clearance makes this ~0 on ordinary ground and only large on a genuine raised
                        // platform. Falls back to the raw check for a pre-21008 peer that doesn't send its clearance.
                        double senderClearance = aircraftPosition.staticCgToGround * 0.3048;
                        bool senderClearanceKnown = double.IsNaN(senderClearance) == false;
                        double senderStructureHeightCm = (senderClearanceKnown ? senderHeight - senderClearance : senderHeight) * 100.0;
                        double releaseThreshold = main.settingsElevatedPlatformThreshold * 0.5;

                        // BUG FIX (EDDW-rooftop field report): mismatchCm compares the LOCAL and SENDER
                        // bare-terrain ("GROUND ALTITUDE") readings, which deliberately excludes scenery/
                        // buildings on BOTH sides - so it stays near-zero even for a genuinely elevated
                        // structure (a rooftop landable on the sender's install but not modelled at all on
                        // the receiver's), because both installs are reading the same underlying bare
                        // terrain regardless of whether either one has real collision geometry for the
                        // building on top of it. Requiring mismatchCm >= threshold to engage - as this used
                        // to do unconditionally - meant elevation-trust could never engage for exactly the
                        // "structure missing on one side" case this feature exists for (observed: mismatch
                        // 18cm against a 50cm threshold, despite the sender sitting 3.71m above bare
                        // terrain). senderStructureHeightCm is already self-consistent and immune to that
                        // cross-install noise (see the comment above) whenever the sender is a modern peer
                        // that reports its own clearance - use it alone, with its own hysteresis band, in
                        // that case. Only fall back to the weaker mismatchCm-based check (which needs the
                        // corroborating cross-install signal, since raw senderHeight alone is satisfied by
                        // essentially any grounded aircraft) for an older peer that doesn't send clearance.
                        if (senderClearanceKnown)
                        {
                            if (nearGround && senderStructureHeightCm >= main.settingsElevatedPlatformThreshold)
                            {
                                trustPlatformElevation = true;
                            }
                            else if (nearGround == false || senderStructureHeightCm < releaseThreshold)
                            {
                                trustPlatformElevation = false;
                            }
                            // else: still near ground with a structure height inside the hysteresis band - keep the previous decision
                        }
                        else if (nearGround && mismatchCm >= main.settingsElevatedPlatformThreshold)
                        {
                            trustPlatformElevation = true;
                        }
                        else if (nearGround == false || mismatchCm < releaseThreshold)
                        {
                            trustPlatformElevation = false;
                        }
                        // else: still near ground with a mismatch inside the hysteresis band - keep the previous decision
                    }
                    else
                    {
                        trustPlatformElevation = false;
                    }
                    // ground is forwarded to the local sim's placement whenever the sender reports on-ground, for
                    // any aircraft type - see helicopters-on-elevated-platforms feature. heightAdjustmentCm (HeightForm
                    // - a manual, cosmetic per-model correction for a mesh/gear-reference-point offset) does not gate
                    // this: a large negative adjustment can still produce ground-penetration jitter, but that's the
                    // sim's own generic terrain-penetration correction fighting an altitude pushed below the actual
                    // mesh, independent of SIM ON GROUND - confirmed by testing with SIM ON GROUND forced permanently
                    // unforwarded, which made no difference to that jitter on either FS2020 or FS2024.
                    int heightAdjustmentCm = GetHeightAdjustment(aircraft.subModel);
                    // debounce the raw on-ground bit before trusting it - see Aircraft.pendingGroundFlag. Unlike
                    // trustPlatformElevation (a continuous mismatch distance with an engage/release threshold band),
                    // there's no magnitude here to apply a band to - SIM ON GROUND is a single bit, and it can
                    // flicker for a moment right after an object is created/injected while its physics settles onto
                    // the ground. Require it to hold its current value for GroundTrustDebounceSeconds before
                    // trustPlatformGround follows it, instead of retargeting smoothedGroundClearanceCorrection on
                    // every flicker.
                    bool rawGround = aircraftPosition.ground != 0;
                    if (rawGround != aircraft.pendingGroundFlag || double.IsNaN(aircraft.pendingGroundFlagSince))
                    {
                        aircraft.pendingGroundFlag = rawGround;
                        aircraft.pendingGroundFlagSince = netTime;
                    }
                    bool trustPlatformGround = aircraft.trustingPlatformGround;
                    if (trustPlatformGround != aircraft.pendingGroundFlag && netTime - aircraft.pendingGroundFlagSince >= GroundTrustDebounceSeconds)
                    {
                        trustPlatformGround = aircraft.pendingGroundFlag;
                    }

                    if (trustPlatformElevation != aircraft.trustingPlatformElevation || trustPlatformGround != aircraft.trustingPlatformGround)
                    {
                        main.MonitorNetwork("ElevatedPlatform '" + aircraft.flightPlan.callsign + "' mismatch=" + mismatchCm.ToString("F0") + "cm threshold=" + main.settingsElevatedPlatformThreshold + "cm senderHeight=" + (double.IsNaN(senderHeight) ? "n/a" : (senderHeight * 100.0).ToString("F0") + "cm") + " elevationTrust=" + trustPlatformElevation + " groundTrust=" + trustPlatformGround + " rawGround=" + rawGround);
                    }
                    // raw received ground-placement values (throttled to ~5/sec, not just on trust-state
                    // change like ElevatedPlatform above) - useful for diagnosing ground-clearance correction/
                    // jitter reports, e.g. whether aircraftPosition.elevation is flipping between a real
                    // terrain reading and a zeroed/default value, or the sender's on-ground flag is unstable.
                    // Opt-in only (-tracediagnostics) - it must not ship firing every tick (see Fix 1b / Fix 4).
                    if (main.settingsTraceDiagnostics && netTime >= aircraft.nextRawDiagLogTime)
                    {
                        aircraft.nextRawDiagLogTime = netTime + 0.2;
                        main.MonitorNetwork("RawPos '" + aircraft.flightPlan.callsign + "' model='" + aircraft.ModelTitle + "' rawGround=" + aircraftPosition.ground +
                            " altitude=" + aircraftPosition.altitude.ToString("F1") + "m elevation=" + aircraftPosition.elevation.ToString("F1") + "m" +
                            " senderStaticCgToGround=" + (aircraftPosition.staticCgToGround * 0.3048).ToString("F2") + "m" +
                            " localElevation=" + aircraft.simPosition.elevation.ToString("F1") + "m" +
                            " localStaticCgToGround=" + (double.IsNaN(aircraft.simPosition.staticCgToGround) ? "n/a" : aircraft.simPosition.staticCgToGround.ToString("F2") + "m") +
                            " netTime=" + netTime.ToString("F1"));
                    }
                    aircraft.trustingPlatformElevation = trustPlatformElevation;
                    aircraft.trustingPlatformGround = trustPlatformGround;

                    // On-ground handling splits into two explicit regimes, keyed by trustPlatformElevation
                    // (see Fix 1/2):
                    //
                    //  Regime A - ordinary ground (trustPlatformGround, NOT trustPlatformElevation): the
                    //    sim's own gear-contact physics owns the vertical axis. JoinFS only seeds an
                    //    absolute local target here - local GROUND ALTITUDE probe + this substitute's OWN
                    //    STATIC CG TO GROUND - and hands the axis to the sim from UpdateSimObjectVelocity.
                    //    Crucially this target has NO dependence on the sender's own clearance, so a
                    //    drastically different-sized substitute (C172 -> C-17 / B748) no longer generates a
                    //    multi-metre smoothed offset that the catch-up then fights (Fix 2). Low-pass
                    //    filtered (same 0.15 factor as smoothedElevationOffset) because trustPlatformGround
                    //    comes from the sender's single-bit on-ground flag, which flickers tick-to-tick.
                    //
                    //  Regime B - elevated platform / ship deck / rig (trustPlatformElevation): the sender
                    //    sits above absent local geometry, so keep the sender's raw altitude with no
                    //    clearance correction and no terrain blend, and hold it there purely by position
                    //    control (SIM ON GROUND withheld, Fix 1d; tight vertical reset, Fix 1c).
                    //
                    //  Neither - airborne, or ground not trusted: unchanged legacy per-model clearance
                    //    correction path, which decays to zero whenever the sender reports airborne.
                    bool regimeA = trustPlatformGround && trustPlatformElevation == false;
                    if (regimeA && aircraft.SimValid && double.IsNaN(aircraft.simPosition.staticCgToGround) == false && double.IsNaN(aircraft.simPosition.elevation) == false)
                    {
                        double targetAltitude = aircraft.simPosition.elevation + aircraft.simPosition.staticCgToGround;
                        aircraft.smoothedGroundAltitude = double.IsNaN(aircraft.smoothedGroundAltitude)
                            ? targetAltitude
                            : aircraft.smoothedGroundAltitude + (targetAltitude - aircraft.smoothedGroundAltitude) * 0.15;
                        aircraftPosition.altitude = (float)aircraft.smoothedGroundAltitude;
                        // neither legacy correction path is in use while in Regime A - see the
                        // ElevationCorrection exclusion below for smoothedElevationOffset specifically.
                        aircraft.smoothedGroundClearanceCorrection = double.NaN;
                        aircraft.smoothedElevationOffset = double.NaN;
                    }
                    else
                    {
                        // out of Regime A - forget the seeded target so a later re-entry starts fresh
                        aircraft.smoothedGroundAltitude = double.NaN;

                        // Regime B keeps the sender's raw altitude verbatim (target 0). The "neither" case
                        // also lands here: trustPlatformGround false => target 0, decaying any prior
                        // correction back toward zero.
                        double targetGroundClearanceCorrection = 0.0;
                        aircraft.smoothedGroundClearanceCorrection = double.IsNaN(aircraft.smoothedGroundClearanceCorrection)
                            ? targetGroundClearanceCorrection
                            : aircraft.smoothedGroundClearanceCorrection + (targetGroundClearanceCorrection - aircraft.smoothedGroundClearanceCorrection) * 0.15;
                        if (double.IsNaN(aircraft.smoothedGroundClearanceCorrection) == false)
                        {
                            aircraftPosition.altitude += (float)aircraft.smoothedGroundClearanceCorrection;
                        }
                    }

                    // check if correction is enabled and local height is valid
                    // BUG FIX (never-settles / periodic-wobble field reports): this legacy per-model
                    // clearance blend was only ever meant for the "Neither - airborne, or ground not
                    // trusted" case (see the Regime A/B comment above) - trustPlatformElevation == false
                    // alone doesn't exclude Regime A, since Regime A is defined as trustPlatformGround &&
                    // trustPlatformElevation == false. That let this block run on every Regime A tick too,
                    // stacking a second, independently-smoothed correction (smoothedElevationOffset) on top
                    // of the altitude Regime A had just seeded from smoothedGroundAltitude - and since height
                    // here is computed from the altitude this same block is about to modify, the two fed
                    // back into each other and the sim's own per-model gear-contact settling, producing a
                    // persistent hunting oscillation whose period varied by substitute instead of ever
                    // converging. Regime A already owns ground placement entirely on its own - explicitly
                    // exclude it here so this legacy path only runs where it was actually designed to.
                    if (Settings.Default.ElevationCorrection && aircraft.SimValid && trustPlatformElevation == false && regimeA == false)
                    {
                        // calculate height
                        double height = aircraftPosition.altitude - aircraftPosition.elevation;
                        // BUG FIX (MSFS2020 low-hovering helicopters dragged to the ground): this
                        // legacy blend pulls the displayed altitude toward THIS install's local
                        // "GROUND ALTITUDE" readback for the injected object. That readback is
                        // unreliable for an AIRBORNE AI object on MSFS2020 - it can stay stuck at
                        // the terrain elevation under the observer's own aircraft rather than
                        // tracking the injected object's position - so a genuinely hovering
                        // helicopter got a large bogus downward correction and slid around on the
                        // ground (correct on MSFS2024, which reads it properly). The original
                        // helicopter/elevated-platform design only ever pulled toward local terrain
                        // when the sender reported ON-GROUND (see cbffffe); this old block was just
                        // never brought under that gate. Require the sender's on-ground flag here
                        // too - a flying/hovering aircraft is now left at the sender's altitude.
                        // Regime A (trusted on-ground) already owns real ground placement and is
                        // excluded above, so this remains only a brief transition smoother between
                        // the ground flag arriving and Regime A engaging.
                        if (aircraftPosition.ground != 0 && height < 50.0)
                        {
                            // calculate proportion to adjust by
                            double proportion = 1.0 - height * 0.02;
                            double remoteElevation = aircraftPosition.elevation;
                            double localElevation = aircraft.simPosition.elevation;
                            // the "GROUND ALTITUDE" probe feeding both localElevation and remoteElevation has small
                            // tick-to-tick noise (terrain LOD/mesh streaming) - blending by the raw difference every
                            // update let that noise pass straight through into the displayed altitude, visible as
                            // the aircraft jittering in height while sitting on/taxiing on ordinary ground. Low-pass
                            // filter the offset instead of using the raw sample each time.
                            double rawOffset = localElevation - remoteElevation;
                            aircraft.smoothedElevationOffset = double.IsNaN(aircraft.smoothedElevationOffset)
                                ? rawOffset
                                : aircraft.smoothedElevationOffset + (rawOffset - aircraft.smoothedElevationOffset) * 0.15;
                            // blend altitude
                            aircraftPosition.altitude += aircraft.smoothedElevationOffset * proportion;
                        }
                        else
                        {
                            // sender airborne, or far from the ground - drop the smoothed offset so a
                            // later approach/landing starts fresh instead of carrying over a stale
                            // value from a different location/time.
                            aircraft.smoothedElevationOffset = double.NaN;
                        }
                    }

                    // height adjustment
                    aircraftPosition.altitude += heightAdjustmentCm * 0.01f;

                    // save old orientation
                    aircraft.oldEuler = aircraft.netPosition.angles.Clone();
                    // update position
                    aircraft.netPosition = new Pos(ref aircraftPosition);
                    // update velocity
                    aircraft.netVelocity = new Vel(ref aircraftPosition);

                    // update network time
                    UpdateObject(aircraft, netTime);

#if XPLANE || CONSOLE
                    // update simulator
                    xplane.UpdateAircraft(aircraft.simId, aircraft.user, main.network.GetNodeName(aircraft.ownerNuid), aircraft.flightPlan.callsign, aircraft.subModel, aircraft.flightPlan.icaoType);
                    xplane.UpdateAircraft(aircraft.simId, (float)aircraft.distance, netTime, aircraftPosition);
#endif
                }

                // check for valid simconnect
                if (Connected && aircraft.Created)
                {
                    // update controls
                    DoSimEvent(aircraft.simId, Event.RUDDER_SET, (uint)-ConvertToAxis(aircraftPosition.rudder));
                    DoSimEvent(aircraft.simId, Event.ELEVATOR_SET, (uint)-ConvertToAxis(aircraftPosition.elevator));
                    DoSimEvent(aircraft.simId, Event.AILERON_SET, (uint)-ConvertToAxis(aircraftPosition.aileron));
                }
            }
        }

        /// <summary>
        /// Update aircraft position and velocity
        /// </summary>
        /// <param name="ownerGuid">Owner of the aircraft</param>
        /// <param name="netId">Owner's sim ID</param>
        /// <param name="engine">Aircraft engine</param>
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, bool user, bool plane, string callsign, string registration, string nickname, string model, string livery, string icaoType, string icaoAirline, string flightNumber, string classCode, string wtc, bool classCodeConfirmed, int typerole, double netTime, ref AircraftPosition aircraftPosition)
        {
            // check for valid aircraft
            if ((objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId) is not Aircraft aircraft))
            {
                // create new aircraft
                if (plane)
                {
                    aircraft = new Plane(ownerNuid, netId);
                }
                else
                {
                    aircraft = new Helicopter(ownerNuid, netId);
                }
                // info
                aircraft.user = user;
                aircraft.flightPlan.callsign = callsign;
                aircraft.flightPlan.registration = registration;
                aircraft.flightPlan.icaoType = icaoType;
                aircraft.flightPlan.icaoAirline = icaoAirline;
                aircraft.flightPlan.flightNumber = flightNumber;
                // model
                UpdateObject(aircraft, model, livery, icaoType, icaoAirline, classCode, wtc, classCodeConfirmed, typerole);
                // create variables
                CreateModelVariables(aircraft);
                // add aircraft
                objectList.Add(aircraft);
                // message
                main.MonitorEvent("Listing aircraft '" + aircraft.flightPlan.callsign + "' from '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - Model '" + aircraft.ownerModel + "'");
            }

            // check if aircraft is injected and needs to be broadcast
            if (aircraft.Injected && IsBroadcast(aircraft))
            {
                // this re-broadcasts an already-received position onward to other nodes (multi-hop/relay
                // topology); logged distinctly from RawPosSend so a relay-introduced bad value can be told
                // apart from a freshly-read one when diagnosing ground-clearance correction/jitter reports.
                if (main.settingsTraceDiagnostics && netTime >= aircraft.nextRawDiagLogTime)
                {
                    aircraft.nextRawDiagLogTime = netTime + 0.2;
                    main.MonitorNetwork("RawPosRelay '" + aircraft.flightPlan.callsign + "' rawGround=" + aircraftPosition.ground +
                        " altitude=" + aircraftPosition.altitude.ToString("F1") + "m elevation=" + aircraftPosition.elevation.ToString("F1") + "m" +
                        " senderStaticCgToGround=" + (aircraftPosition.staticCgToGround * 0.3048).ToString("F2") + "m" +
                        " netTime=" + netTime.ToString("F1"));
                }
                // create message
                main.network.WriteAircraftPositionMessage(aircraft.netId, netTime, aircraft, ref aircraftPosition);
                // broadcast message to other nodes
                main.network.localNode.Broadcast();
            }

            // check if type has changed
            if (aircraft is Plane && plane == false || aircraft is Helicopter && plane)
            {
                // remove aircraft because the type has changed
                RemoveObject(aircraft);
                // invalid aircraft
                return null;
            }
            else
            {
                // set expire time
                aircraft.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;

                // check if model has changed
                if (model.Equals(aircraft.ownerModel) == false)
                {
                    // check if creating this object
                    if (creatingObject != aircraft)
                    {
                        // change model
                        aircraft.ownerModel = model;
                        // remove aircraft
                        RemoveObjectFromSim(aircraft);
                        // update information
                        aircraft.user = user;
                    }
                }
                else
                {
                    // check if callsign has changed
                    if (callsign.Equals(aircraft.flightPlan.callsign) == false)
                    {
                        // update ATC ID
                        SetAtcId(aircraft);
                    }

                    // update callsign
                    aircraft.flightPlan.callsign = callsign;
                    aircraft.flightPlan.registration = registration;
                    aircraft.flightPlan.flightNumber = flightNumber;
                    // update position and velocity
                    UpdateAircraft(GetControlledObject(aircraft) as Aircraft, netTime, aircraftPosition);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft sim event
        /// </summary>
        /// <param name="ownerGuid">Owner of the aircraft</param>
        /// <param name="netId">Owner's sim ID</param>
        /// <param name="eventId">Event ID</param>
        /// <param name="data">Data</param>
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, uint eventId, uint data, bool flight)
        {
            // get aircraft
            Aircraft aircraft = objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is Aircraft) as Aircraft;
            if (aircraft != null)
            {
                // get controlled aircraft
                Aircraft controlledAircraft = GetControlledObject(aircraft) as Aircraft;

                // check for valid simconnect
                if (Connected && controlledAircraft.Created && controlledAircraft.remoteFlightControl)
                {
                    // check if aircraft is being controlled
                    if (flight)
                    {
                        // change state
                        DoSimEvent(controlledAircraft.simId, (Event)eventId, data);
                    }
                }

                // check if aircraft is injected and needs to be broadcast
                if (aircraft.Injected && IsBroadcast(aircraft))
                {
                    // create message
                    main.network.WriteSimEventMessage(aircraft.netId, eventId, data);
                    // broadcast message to other nodes
                    main.network.localNode.Broadcast();
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft integer variables
        /// </summary>
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, Dictionary<uint, int> variables)
        {
            // get aircraft
            Aircraft aircraft = objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is Aircraft) as Aircraft;
            if (aircraft != null)
            {
                // get controlled aircraft
                Aircraft controlledAircraft = GetControlledObject(aircraft) as Aircraft;

                // update variable set
                controlledAircraft.variableSet ?. UpdateIntegers(variables);

                // check if aircraft is injected and needs to be broadcast
                if (aircraft.Injected && IsBroadcast(aircraft))
                {
                    // create message
                    main.network.SendIntegerVariablesMessage(new LocalNode.Nuid(), aircraft.netId, variables, aircraft.ownerNuid);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft float variables
        /// </summary>
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, Dictionary<uint, float> variables)
        {
            // get aircraft
            Aircraft aircraft = objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is Aircraft) as Aircraft;
            if (aircraft != null)
            {
                // get controlled aircraft
                Aircraft controlledAircraft = GetControlledObject(aircraft) as Aircraft;

                // update variable set
                controlledAircraft.variableSet ?. UpdateFloats(variables);

                // check if aircraft is injected and needs to be broadcast
                if (aircraft.Injected && IsBroadcast(aircraft))
                {
                    // create message
                    main.network.SendFloatVariablesMessage(new LocalNode.Nuid(), aircraft.netId, variables, aircraft.ownerNuid);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft string8 variables
        /// </summary>
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, Dictionary<uint, string> variables)
        {
            // get aircraft
            Aircraft aircraft = objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is Aircraft) as Aircraft;
            if (aircraft != null)
            {
                // get controlled aircraft
                Aircraft controlledAircraft = GetControlledObject(aircraft) as Aircraft;

                // update variable set
                controlledAircraft.variableSet ?. UpdateString8(variables);

                // check if aircraft is injected and needs to be broadcast
                if (aircraft.Injected && IsBroadcast(aircraft))
                {
                    // create message
                    main.network.SendString8VariablesMessage(new LocalNode.Nuid(), aircraft.netId, variables, aircraft.ownerNuid);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Process an aircraft
        /// </summary>
        void ProcessAircraftPosition(uint simId, double simTime, ref AircraftPosition aircraftPosition)
        {
            // get aircraft
            if (objectList.Find(o => o.simId == simId && o is Aircraft) is Aircraft aircraft)
            {
                // update position
                aircraft.simPosition = new Pos(ref aircraftPosition);
                // store current time
                aircraft.simTime = simTime;

                // raw SimConnect read for whichever aircraft this is (own aircraft or a locally-simulated
                // one being broadcast), before anything else touches it - useful for diagnosing ground-
                // clearance correction/jitter reports, e.g. whether SimConnect itself intermittently returns
                // a zeroed/default "GROUND ALTITUDE" on this periodic per-object read.
                if (main.settingsTraceDiagnostics && simTime >= aircraft.nextRawPosSendDiagLogTime)
                {
                    aircraft.nextRawPosSendDiagLogTime = simTime + 0.2;
                    main.MonitorNetwork("RawPosSend '" + aircraft.flightPlan.callsign + "' owner=" + aircraft.owner + " model='" + aircraft.ModelTitle + "'" +
                        " rawGround=" + aircraftPosition.ground + " altitude=" + aircraftPosition.altitude.ToString("F1") + "m" +
                        " elevation=" + aircraftPosition.elevation.ToString("F1") + "m" +
                        " ownStaticCgToGround=" + (aircraftPosition.staticCgToGround * 0.3048).ToString("F2") + "m" +
                        " simTime=" + simTime.ToString("F1"));
                }

                // check if user or broadcasting this aircraft
                if (aircraft.owner == Obj.Owner.Me || main.network.localNode.Connected && IsBroadcast(aircraft))
                {
                    // check if not under remote control
                    if (aircraft.remoteFlightControl == false)
                    {
                        // update velocity
                        aircraft.netVelocity = new Vel(ref aircraftPosition);
                        // store current time
                        aircraft.netSimTime = simTime;
                    }

                    // check if broadcasting
                    if (main.network.localNode.Connected)
                    {
                        try
                        {
                            // check for entered aircraft
                            if (aircraft.owner == Obj.Owner.Me && enteredAircraft != null)
                            {
                                // check that our aircraft is not under remote control
                                if (aircraft.remoteFlightControl == false)
                                {
                                    // create message
                                    main.network.WriteAircraftPositionMessage(uint.MaxValue, aircraft.simTime, aircraft, ref aircraftPosition);
                                    // send message to owner of entered aircraft
                                    main.network.localNode.Send(enteredAircraft.ownerNuid);
                                }
                            }
                            else if (IsBroadcast(aircraft) && aircraft.Injected == false)
                            {
                                // create message
                                main.network.WriteAircraftPositionMessage(aircraft.netId, aircraft.simTime, aircraft, ref aircraftPosition);

                                // get nodes
                                LocalNode.Nuid[] nodeList = main.network.localNode.GetNodeList();
                                // for each node
                                foreach (var nuid in nodeList)
                                {
                                    // get remote object
                                    Obj remoteObject = objectList.Find(o => o.ownerNuid == nuid && o is Aircraft && (o as Aircraft).user);
                                    // get interval mask
                                    int intervalMask = GetIntervalMask(aircraft, remoteObject);
                                    // check if node's simulator is not connected
                                    if (main.network.GetNodeSimulatorConnected(nuid) == false)
                                    {
                                        // increase interval (every 32)
                                        intervalMask = 0x1f;
                                    }

                                    // check send interval
                                    if ((aircraft.positionCount & intervalMask) == 0)
                                    {
                                        // broadcast message to other nodes
                                        main.network.localNode.Send(nuid);
                                    }
                                }
                            }
                            // increment count
                            aircraft.positionCount++;
                        }
                        catch (Exception ex)
                        {
                            main.MonitorEvent("ERROR: Failed to write position/velocity message: " + ex.Message);
                        }
                    }
                }

                // check if recording
                if (main.recorder.recording && aircraft.record && aircraft.Injected == false)
                {
                    // record position and velocity
                    main.recorder.Record(aircraft.recorderObj, main.ElapsedTime, ref aircraftPosition);
                }
            }
        }


        /// <summary>
        /// scheduled follow aircraft
        /// </summary>
        volatile Aircraft followAircraft = null;

        /// <summary>
        /// Schedule follow aircraft
        /// </summary>
        /// <param name="aircraft"></param>
        public void ScheduleFollow(Aircraft aircraft)
        {
            // check if not scheduled
            // set scheduled follow
            followAircraft ??= aircraft;
        }

        /// <summary>
        /// Follow another aircraft
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        public void FollowAircraft(Aircraft aircraft)
        {
            // get aircraft position
            Pos position = aircraft ?. Position;
            // check for valid aircraft
            if (userAircraft != null && aircraft.owner != Obj.Owner.Me && position != null)
            {
                // get rate of change of geodesic position
                double xRate = Vector.GeodesicDistance(position.geo.x, position.geo.z, position.geo.x + Vector.GEODESIC_EPSILON, position.geo.z);
                double zRate = Vector.GeodesicDistance(position.geo.x, position.geo.z, position.geo.x, position.geo.z + Vector.GEODESIC_EPSILON);

                // get distance
                double distance = (double)Settings.Default.FollowDistance;

                // set position
                Pos newPosition = new();
                newPosition.geo.x = position.geo.x - (distance * Math.Sin(position.angles.y)) / xRate * Vector.GEODESIC_EPSILON;
                newPosition.geo.z = position.geo.z - (distance * Math.Cos(position.angles.y)) / zRate * Vector.GEODESIC_EPSILON;
                newPosition.geo.y = position.geo.y;
                newPosition.angles = position.angles.Clone();

                // update aircraft
                UpdateObject(userAircraft, newPosition, aircraft.netVelocity);
            }
        }

#region Share Cockpit

        /// <summary>
        /// State before entering another aircraft
        /// </summary>
        bool savedBroadcast;
        Pos savedPosition = new();
        Vel savedVelocity = new();

        /// <summary>
        /// Currently entered aircraft
        /// </summary>
        public Aircraft enteredAircraft;

        /// <summary>
        /// Scheduled enter
        /// </summary>
        volatile Aircraft enterAircraft;

        /// <summary>
        /// Schedule an enter
        /// </summary>
        /// <param name="aircraft"></param>
        public void ScheduleEnterAircraft(Aircraft aircraft)
        {
            // check if not scheduled
            // schedule
            enterAircraft ??= aircraft;
        }

        /// <summary>
        /// Enter the cockpit of another aircraft
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        public bool EnterAircraft(Aircraft aircraft)
        {
            // check that the aircraft can be entered
            if (aircraft.owner != Obj.Owner.Me)
            {
                // get aircraft position
                Pos aircraftPosition = aircraft ?. Position;
                // check user aircraft
                if (userAircraft != null && userAircraft.SimValid && aircraftPosition != null)
                {
                    // save broadcast state
                    savedBroadcast = userAircraft.broadcast;
                    // switch off broadcast
                    userAircraft.broadcast = false;
                    // under remote control
                    userAircraft.remoteFlightControl = true;
                    ResetObject(userAircraft);

                    // get entered aircraft position and velocity
                    Pos position = userAircraft.simPosition;
                    Vel velocity = userAircraft.netVelocity;

                    // save position of user aircraft
                    savedPosition = position.Clone();
                    // save velocity
                    savedVelocity = new Vel(velocity.linear.InvRotate(position.angles), velocity.angular.Clone(), velocity.acc.InvRotate(position.angles));

                    // update aircraft
                    UpdateObject(userAircraft, aircraftPosition, aircraft.netVelocity);
                    // copy net time
                    userAircraft.netRealTime = aircraft.netRealTime;
                    userAircraft.netStateTime = aircraft.netStateTime;
                    userAircraft.netSimTime = aircraft.netSimTime;

                    // update net position
                    userAircraft.netPosition = aircraft.Position.Clone();

                    // set entered aircraft
                    enteredAircraft = aircraft;
                    // remove aircraft from simulator
                    RemoveObjectFromSim(aircraft);

                    // check if aircraft is broadcast
                    if (IsBroadcast(aircraft) && main.network.localNode.Connected)
                    {
                        // notify session
                        main.network.SendRemoveObjectMessage(aircraft.netId);
                    }

                    // delay variable broadcast
                    userAircraft.variableStartTime = main.ElapsedTime + 6.0;

                    // refresh
#if !SERVER && !CONSOLE
                    main.aircraftForm ?. refresher.Schedule();
                    main.objectsForm ?. refresher.Schedule();
#endif

                    // entered
                    return true;
                }
            }

            // not entered
            return false;
        }

        /// <summary>
        /// Scheduled leave
        /// </summary>
        volatile bool leaveAircraft = false;

        /// <summary>
        /// Schedule a leave
        /// </summary>
        public void ScheduleLeave()
        {
            // leave
            leaveAircraft = true;
        }

        /// <summary>
        /// Leave the currently entered cockpit
        /// </summary>
        public void LeaveAircraft()
        {
            // check for entered aircraft
            if (enteredAircraft != null)
            {
                // check user aircraft
                if (userAircraft != null)
                {
                    // reset aircraft
                    ResetObject(userAircraft);
                    // update position and velocity
                    UpdateObject(userAircraft, savedPosition, savedVelocity);
                    // restore broadcast
                    userAircraft.broadcast = savedBroadcast;
                    // reset remote control state
                    userAircraft.remoteFlightControl = false;
                }

                // get entered aircraft
                Aircraft aircraft = enteredAircraft;
                // no longer in other aircraft
                enteredAircraft = null;
                // remove aircraft
                RemoveObjectFromList(aircraft);

                // refresh
#if !SERVER && !CONSOLE
                main.aircraftForm ?. refresher.Schedule();
                main.objectsForm ?. refresher.Schedule();
#endif
            }
        }

        /// <summary>
        /// Set share cockpit state
        /// </summary>
        /// <param name="nodeGuid">Guid of owner node</param>
        /// <param name="shareCockpit">State</param>
        public void ShareCockpit(LocalNode.Nuid nodeNuid, byte share)
        {
            // check if aircraft found
            if (objectList.Find(o => o.ownerNuid == nodeNuid && o is Aircraft && (o as Aircraft).user) is Aircraft aircraft)
            {
                // set state
                aircraft.cockpitShare = share;
            }
        }

#endregion

        /// <summary>
        /// Do a sim event
        /// </summary>
        /// <param name="nodeGuid">Guid of owner node</param>
        /// <param name="shareCockpit">State</param>
        public void DoSimEvent(uint simId, VariableMgr.Definition definition, uint data)
        {
#if SIMCONNECT
            // check for simconnect
            // simconnect event
            simconnect?.DoEvent(simId, definition.scEvent, data);
#endif
        }

        /// <summary>
        /// Do a sim event
        /// </summary>
        /// <param name="nodeGuid">Guid of owner node</param>
        /// <param name="shareCockpit">State</param>
        public void DoSimEvent(uint simId, Event simEvent, uint data)
        {
#if XPLANE || CONSOLE
            // do xplane event
            xplane.DoEvent(simId, simEvent, data);
#elif SIMCONNECT
            // check for simconnect
            if (simconnect != null)
            {
                main.MonitorNetwork("DoSimEvent ID '" + simId + "' - Event '" + Sim.EventToString(simEvent) + "' - Data '" + (int)data + "'");

                // simconnect event
                simconnect.DoEvent(simId, simEvent, data);
            }
#endif
        }

        /// <summary>
        /// Do a sim event
        /// </summary>
        /// <param name="nodeGuid">Guid of owner node</param>
        /// <param name="shareCockpit">State</param>
        public void DoSimEvent(LocalNode.Nuid nodeNuid, uint eventId, uint data)
        {
            // check if aircraft found
            if (objectList.Find(o => o.ownerNuid == nodeNuid && o is Aircraft && (o as Aircraft).user) is Aircraft aircraft)
            {
                // do event
                DoSimEvent(aircraft.simId, (Event)eventId, data);
            }
        }

#endregion

#region Weather

        /// <summary>
        /// Aircraft used to update the weather from
        /// </summary>
        public Aircraft weatherAircraft;

        /// <summary>
        /// Current weather METAR
        /// </summary>
        public volatile string scheduleMetar = null;

        /// <summary>
        /// Set a new aircraft for getting the weather
        /// </summary>
        /// <param name="aircraft">Aircraft to monitor</param>
        public void SetWeatherAircraft(Aircraft aircraft)
        {
            // set aircraft
            weatherAircraft = aircraft;
            // check for valid aircraft
            if (weatherAircraft != null)
            {
                // set weather from aircraft
                SetWeatherObservation(weatherAircraft.metar);
            }
        }

        /// <summary>
        /// Set weather from observation
        /// </summary>
        /// <param name="metar">METAR</param>
        public void SetWeatherObservation(string metar)
        {
            // schedule metar change
            scheduleMetar ??= metar;
        }

        /// <summary>
        /// Set weather from observation
        /// </summary>
        /// <param name="metar">METAR</param>
        public void SetWeatherObservation(LocalNode.Nuid nuid, string metar)
        {
            // get all aircraft for this node
            List<Obj> nodeAircraft = objectList.FindAll(o => o.ownerNuid == nuid && o is Aircraft);
            // for each object
            foreach (var obj in nodeAircraft)
            {
                // check for weather aircraft
                if (obj == weatherAircraft)
                {
                    // set weather from aircraft
                    SetWeatherObservation(metar);
                }
                // set weather for the aircraft
                (obj as Aircraft).SetWeather(metar);
            }
        }

        #endregion

        #region Requests

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

        /// <summary>
        /// Request list of models and liverlies
        /// </summary>
        public void RequestSimulatorModels()
        {
#if SIMCONNECT && FS2024
            // check for FS connection
            if (simconnect != null)
            {
                requestModelListInProgress = true;
                // aircraft/helicopter/balloon are enumerated as three separate SimConnect requests;
                // track all three so completion only fires once every one of them has reported back
                pendingModelListRequests.Clear();
                pendingModelListRequests.Add(Requests.GET_MODELS_AIRCRAFT);
                pendingModelListRequests.Add(Requests.GET_MODELS_HELICOPTER);
                pendingModelListRequests.Add(Requests.GET_MODELS_BALLOON);
                simconnect.RequestSimulatorModels();
            }
#endif
        }

        /// <summary>
        /// Request information about aircraft in the sim
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RequestInfo()
        {
#if SIMCONNECT
            // check for FS connection
            simconnect?.RequestDataByType(Requests.OBJECT_INFO, Definitions.OBJECT_GET_INFO, 10000);
#endif
        }

#if SIMCONNECT
        int requestPositionCount = 0;
#endif

        /// <summary>
        /// Request position of network aircraft in the sim
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RequestPosition()
        {
#if SIMCONNECT
            // check for FS connection
            if (simconnect != null)
            {
                // for each object
                foreach (var obj in objectList)
                {
                    // check if object needs to be broadcast or recorded by this node
                    if (obj.Created)
                    {
                        // check if object is injected
                        if (obj.Injected)
                        {
                            if (obj is Aircraft)
                            {
                                // request full aircraft position - own persistent request ID per object,
                                // not the shared Requests.AIRCRAFT_POSITION value, see PositionPollRequestIdBase
                                if (obj.positionRequestId < 0)
                                {
                                    obj.positionRequestId = NextPositionPollRequestId();
                                }
                                simconnect.RequestData((Requests)obj.positionRequestId, Definitions.AIRCRAFT_POSITION, obj.simId);
                            }
                            else
                            {
                                // request position
                                simconnect.RequestData(Requests.OBJECT_POSITION, Definitions.OBJECT_POSITION, obj.simId);
                            }
                        }
                        // check if object needs to be broadcast or recorded by this node
                        else if (obj.owner == Obj.Owner.Me || IsBroadcast(obj) || main.recorder.recording && obj.record)
                        {
                            if (obj is Aircraft)
                            {
                                // request full aircraft position - own persistent request ID per object,
                                // not the shared Requests.AIRCRAFT_POSITION value, see PositionPollRequestIdBase
                                if (obj.positionRequestId < 0)
                                {
                                    obj.positionRequestId = NextPositionPollRequestId();
                                }
                                simconnect.RequestData((Requests)obj.positionRequestId, Definitions.AIRCRAFT_POSITION, obj.simId);
                            }
                            else
                            {
                                // request full object position
                                simconnect.RequestData(Requests.OBJECT_POSITION_VELOCITY, Definitions.OBJECT_POSITION_VELOCITY, obj.simId);
                            }
                        }
                        else if ((requestPositionCount & 0xf) == 0)
                        {
                            // request position
                            simconnect.RequestData(Requests.OBJECT_POSITION, Definitions.OBJECT_POSITION, obj.simId);
                        }
                    }
                }

                // increment count
                requestPositionCount++;
            }
#endif
        }


        /// <summary>
        /// Request position of network aircraft in the sim
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void RequestWeather()
        {
#if SIMCONNECT
            // get user aircraft
            if (objectList.Find(o => o.owner == Obj.Owner.Me) is Aircraft aircraft)
            {
                // check for FS connection
                // request weather
                simconnect?.WeatherRequest(Requests.WEATHER, aircraft.simPosition.geo.z * (180.0 / Math.PI), aircraft.simPosition.geo.x * (180.0 / Math.PI), aircraft.simPosition.geo.y * FEET_PER_METRE);
            }
#endif
        }

#endregion

#region SimConnect

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
        /// Is a simulator currently connecting
        /// </summary>
        public bool Connecting { get { return Connected == false && checkConnectionCount < CHECK_CONNECTION_ATTEMPTS; } }

        /// <summary>
        /// Try connection
        /// </summary>
        public void Connect()
        {
            // reset connection attempts
            checkConnectionCount = 0;
            checkConnectionTimer.Reset();
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Close()
        {
            // disable weather aircraft
            SetWeatherAircraft(null);
            // leave cockpit of other aircraft
            LeaveAircraft();
            // remove all simulator objects
            foreach (var obj in objectList)
            {
                // check for simulator object
                if (obj.Injected == false)
                {
                    // add to remove list
                    removeList.Add(obj);
                }
            }
            // clear objects
            DoRemove();
            // reset user aircraft
            userAircraft = null;
#if XPLANE || CONSOLE
            xplane.Close();
#elif SIMCONNECT
            // close simconnect
            simconnect = null;
#endif
            simulatorName = "";
            // set connection attempts
            checkConnectionCount = CHECK_CONNECTION_ATTEMPTS;
            checkConnectionTimer.Reset();
            // clear model matching
            main.ScheduleSubstitutionClear();
            // refresh
#if !SERVER && !CONSOLE
            main.aircraftForm ?. refresher.Schedule();
            main.objectsForm ?. refresher.Schedule();
            // show message
            main.MonitorEvent("Disconnected from simulator");
#endif
        }

        /// <summary>
        /// Check connection to FS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void CheckConnection()
        {
            // check for existing connection
            if (Connected == false)
            {
#if SIMCONNECT
                try
                {
                    // message
                    main.MonitorEvent("Looking for simulator (" + (checkConnectionCount + 1) + "/" + CHECK_CONNECTION_ATTEMPTS + ")");

                    Random rand = new((int)DateTime.Now.Ticks);
                    string name = "";
                    for (int i = 0; i < 10; i++)
                    {
                        name += (char)rand.Next((int)'A', (int)'Z');
                    }
                    // create simconnect interface
                    simconnect = new SimConnectInterface(this, main, name);

                    if (simconnect.Valid)
                    {
                        // no need to ask
                        Settings.Default.AskSimConnect = true;
                    }
                    else
                    {
                        // delete simconnect
                        simconnect = null;
                    }
                }
                catch (System.IO.FileNotFoundException)
                {
                    // simconnect message
                    main.scheduleAskSimConnect = true;
                    main.MonitorEvent("SimConnect not installed.");
                    simconnect = null;
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("ERROR - Failed to initialize SimConnect. " + ex.Message);
                    simconnect = null;
                }
#elif XPLANE || CONSOLE
                if (main.settingsXplane)
                {
                    // message
                    main.MonitorEvent("Looking for simulator (" + (checkConnectionCount + 1) + "/" + CHECK_CONNECTION_ATTEMPTS + ")");
                    // try xplane
                    xplane.Open();
                }
#endif
            }
        }

        #endregion

        #region Intervals

        readonly Timer updateIntervalsTimer = new(5.0);

        /// <summary>
        /// Interval mask for a pair of objects
        /// </summary>
        class IntervalMask
        {
            /// <summary>
            /// Object on this node
            /// </summary>
            public Obj localObject;
            /// <summary>
            /// object on remote node
            /// </summary>
            public Obj remoteObject;
            /// <summary>
            /// Interval mask
            /// </summary>
            public int mask = 0;
        }

        /// <summary>
        /// List of interval masks between pair of objects
        /// </summary>
        readonly List<IntervalMask> intervalMasks = [];

        /// <summary>
        /// Get the interval mask for a pair of objects
        /// </summary>
        /// <param name="localObject">Local object</param>
        /// <param name="remoteObject">Remote object</param>
        /// <returns>Interval mask value</returns>
        int GetIntervalMask(Obj localObject, Obj remoteObject)
        {
            // get interval
            IntervalMask intervalMask = intervalMasks.Find(i => i.localObject == localObject && i.remoteObject == remoteObject);
            // check if interval found
            if (intervalMask != null)
            {
                // return mask
                return intervalMask.mask;
            }
            else
            {
                // always process
                return 0;
            }
        }

        /// <summary>
        /// Remove interval masks for an object
        /// </summary>
        /// <param name="object"></param>
        void RemoveIntervalMask(Obj obj)
        {
            // get all masks referencing object
            List<IntervalMask> list = intervalMasks.FindAll(i => i.localObject == obj || i.remoteObject == obj);
            // for each interval mask
            foreach (var intervalMask in list)
            {
                // remove
                intervalMasks.Remove(intervalMask);
            }
        }

#endregion

#region Streaming

        /// <summary>
        /// Current data version - the format version of the P2P network stream and the
        /// recording (.jfs) file. This is INDEPENDENT of XPlane.DATA_VERSION (the JoinFS
        /// &lt;-&gt; X-Plane-plugin IPC protocol counter); the two just happen to live in the
        /// same integer range for historical reasons. Never compare them, and never pass
        /// one where the other is expected - see XPLANE_POSITION_BLOB_VERSION below.
        /// </summary>
        public const short VERSION = 21009;

        /// <summary>
        /// From this version on, every AircraftPosition / ObjectPositionVelocity blob written
        /// to the network or a .jfs file is preceded by a ushort byte-length. A reader that
        /// doesn't understand a field added later simply skips to the end of the blob instead
        /// of desyncing the rest of the stream. (Legacy readers &lt; this version read the raw
        /// body as before.)
        /// </summary>
        public const short POSITION_BLOB_LENGTH_PREFIXED = 21009;

        /// <summary>
        /// The frozen AircraftPosition byte layout that the native X-Plane plugin speaks
        /// (JoinFS-XP/Link.h, struct AircraftPositionMsg): position + controls + elevation +
        /// ground flag, and nothing after it. The X-Plane IPC read/write sites in XPlane.cs
        /// pin Sim.Read / Sim.Write to this value instead of the plugin's DATA_VERSION, so a
        /// future Sim.VERSION field can never make them over-read a packet the plugin never
        /// grew. Value is "&gt;= 10023 (elevation + flags) but &lt; 21008 (no staticCgToGround),
        /// and &lt; POSITION_BLOB_LENGTH_PREFIXED (no length prefix)".
        /// </summary>
        public const short XPLANE_POSITION_BLOB_VERSION = 21007;

        /// <summary>
        /// Method for reading specific data versions
        /// </summary>
        /// <param name="reader"></param>
        public delegate void ReadVersion(short version, BinaryReader reader);

        /// <summary>
        /// Exception for reading data
        /// </summary>
        public class ReadException(string message) : Exception(message)
        {
        }

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, Dictionary<short, ReadVersion> versions, BinaryReader reader)
        {
            // get keys
            List<short> keys = [.. versions.Keys];
            // sort keys
            keys.Sort();
            // go backwards through the versions
            for (int index = keys.Count - 1; index >= 0; index--)
            {
                // check version
                if (version >= keys[index])
                {
                    // read version
                    versions[keys[index]](version, reader);
                    break;
                }
            }
        }

        /// <summary>
        /// Method for reading specific data versions
        /// </summary>
        /// <param name="reader"></param>
        public delegate void ReadVersion<T>(short version, BinaryReader reader, ref T t);

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read<T>(short version, Dictionary<short, ReadVersion<T>> versions, BinaryReader reader, ref T t)
        {
            // get keys
            List<short> keys = [.. versions.Keys];
            // sort keys
            keys.Sort();
            // go backwards through the versions
            for (int index = keys.Count - 1; index >= 0; index--)
            {
                // check version
                if (version >= keys[index])
                {
                    // read version
                    versions[keys[index]](version, reader, ref t);
                    break;
                }
            }
        }

        /// <summary>
        /// Write a length-prefixed position blob. From POSITION_BLOB_LENGTH_PREFIXED on, the
        /// body is preceded by a ushort byte-count so a reader can skip a field it doesn't
        /// know; older versions write the raw body. See Sim.VERSION doc comment.
        /// </summary>
        static void WriteLengthPrefixed(BinaryWriter writer, short version, Action<BinaryWriter> writeBody)
        {
            if (version < POSITION_BLOB_LENGTH_PREFIXED)
            {
                writeBody(writer);
            }
            else if (writer.BaseStream.CanSeek)
            {
                long lengthPos = writer.BaseStream.Position;
                writer.Write((ushort)0);
                long bodyStart = writer.BaseStream.Position;
                writeBody(writer);
                long bodyEnd = writer.BaseStream.Position;
                writer.BaseStream.Position = lengthPos;
                writer.Write((ushort)(bodyEnd - bodyStart));
                writer.BaseStream.Position = bodyEnd;
            }
            else
            {
                // non-seekable target: buffer the body so the ushort length is still correct
                using MemoryStream buffer = new();
                using (BinaryWriter bufferWriter = new(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writeBody(bufferWriter);
                }
                writer.Write((ushort)buffer.Length);
                buffer.Position = 0;
                buffer.CopyTo(writer.BaseStream);
            }
        }

        /// <summary>
        /// Read a length-prefixed position blob written by WriteLengthPrefixed. The stored
        /// length is authoritative: after the body reader runs, the stream is repositioned to
        /// exactly the end of the blob, so an unknown trailing field (or a short read) can't
        /// desync whatever follows.
        /// </summary>
        static void ReadLengthPrefixed(short version, BinaryReader reader, Action<BinaryReader> readBody)
        {
            if (version < POSITION_BLOB_LENGTH_PREFIXED)
            {
                readBody(reader);
            }
            else if (reader.BaseStream.CanSeek)
            {
                ushort length = reader.ReadUInt16();
                long bodyStart = reader.BaseStream.Position;
                // The prefix must fit what's left of the message. A bad length means the sender
                // isn't actually speaking the length-prefixed format (version mismatch) or the
                // datagram is truncated - fail loudly so the caller keeps its last good state.
                if (length > reader.BaseStream.Length - bodyStart)
                {
                    throw new ReadException("position blob length " + length + " exceeds "
                        + (reader.BaseStream.Length - bodyStart) + " bytes remaining");
                }
                // A body reader that runs off the end is a real desync - let it propagate.
                // (A body that reads FEWER bytes than 'length' is fine: a newer peer appended a
                // field we don't know; the reposition below skips it.)
                readBody(reader);
                reader.BaseStream.Position = bodyStart + length;
            }
            else
            {
                // non-seekable source: pull the exact blob into a buffer and parse from there
                ushort length = reader.ReadUInt16();
                byte[] body = reader.ReadBytes(length);
                if (body.Length != length)
                {
                    throw new ReadException("position blob truncated: got " + body.Length + " of " + length + " bytes");
                }
                using MemoryStream buffer = new(body);
                using BinaryReader bufferReader = new(buffer);
                readBody(bufferReader);
            }
        }

        /// <summary>
        /// Write position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="version">Stream/file format version (Sim.VERSION, or a pinned blob version)</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public static void Write(BinaryWriter writer, short version, ref ObjectPositionVelocity positionVelocity)
        {
            ObjectPositionVelocity pv = positionVelocity;
            WriteLengthPrefixed(writer, version, w => WriteObjectPositionVelocityBody(w, ref pv));
        }

        /// <summary>
        /// Write position/velocity body (no length prefix)
        /// </summary>
        static void WriteObjectPositionVelocityBody(BinaryWriter writer, ref ObjectPositionVelocity positionVelocity)
        {
            // add position
            writer.Write(positionVelocity.latitude);
            writer.Write(positionVelocity.longitude);
            writer.Write(positionVelocity.altitude);
            writer.Write(positionVelocity.pitch);
            writer.Write(positionVelocity.bank);
            writer.Write(positionVelocity.heading);
            // add velocity
            writer.Write(positionVelocity.velocityX);
            writer.Write(positionVelocity.velocityY);
            writer.Write(positionVelocity.velocityZ);
            writer.Write(positionVelocity.angularVelocityX);
            writer.Write(positionVelocity.angularVelocityY);
            writer.Write(positionVelocity.angularVelocityZ);
            writer.Write(positionVelocity.accelerationX);
            writer.Write(positionVelocity.accelerationY);
            writer.Write(positionVelocity.accelerationZ);
            writer.Write(positionVelocity.height);
            // ground flags
            byte flags = 0;
            if (positionVelocity.ground != 0) flags |= 0x01;
            if (Settings.Default.ElevationCorrection) flags |= 0x02;
            writer.Write(flags);
        }

        /// <summary>
        /// Read position and velocity from a stream
        /// </summary>
        /// <param name="reader">Binary reader</param>
        public static void ReadPositionVelocity1(short version, BinaryReader reader, ref ObjectPositionVelocity positionVelocity)
        {
            // update position
            positionVelocity.latitude = reader.ReadDouble();
            positionVelocity.longitude = reader.ReadDouble();
            positionVelocity.altitude = reader.ReadDouble();
            positionVelocity.pitch = reader.ReadSingle();
            positionVelocity.bank = reader.ReadSingle();
            positionVelocity.heading = reader.ReadSingle();
            // update velocity
            positionVelocity.velocityX = reader.ReadSingle();
            positionVelocity.velocityY = reader.ReadSingle();
            positionVelocity.velocityZ = reader.ReadSingle();
            positionVelocity.angularVelocityX = reader.ReadSingle();
            positionVelocity.angularVelocityY = reader.ReadSingle();
            positionVelocity.angularVelocityZ = reader.ReadSingle();
            positionVelocity.accelerationX = reader.ReadSingle();
            positionVelocity.accelerationY = reader.ReadSingle();
            positionVelocity.accelerationZ = reader.ReadSingle();
            // update ground state
            positionVelocity.height = version >= 10023 ? reader.ReadSingle() : 0.0f;
            byte flags = version >= 10023 ? reader.ReadByte() : (byte)0;
            positionVelocity.ground = (flags & 0x01) != 0 ? 1 : 0;
        }

        /// <summary>
        /// Version table for reading position and velocity
        /// </summary>
        static readonly Dictionary<short, ReadVersion<ObjectPositionVelocity>> positionVelocityVersions = new()
        {
            { 10022, ReadPositionVelocity1 },
        };

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, BinaryReader reader, ref ObjectPositionVelocity positionVelocity)
        {
            ObjectPositionVelocity pv = positionVelocity;
            ReadLengthPrefixed(version, reader, r => Read<ObjectPositionVelocity>(version, positionVelocityVersions, r, ref pv));
            positionVelocity = pv;
        }

        /// <summary>
        /// Write aircraft position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="version">Stream/file format version (Sim.VERSION, or a pinned blob version)</param>
        /// <param name="aircraftPosition">Position and Velocity</param>
        public static void Write(BinaryWriter writer, short version, ref AircraftPosition aircraftPosition)
        {
            AircraftPosition ap = aircraftPosition;
            WriteLengthPrefixed(writer, version, w => WriteAircraftPositionBody(w, version, ref ap));
        }

        /// <summary>
        /// Write aircraft position/velocity body (no length prefix)
        /// </summary>
        static void WriteAircraftPositionBody(BinaryWriter writer, short version, ref AircraftPosition aircraftPosition)
        {
            // add position
            writer.Write(aircraftPosition.latitude);
            writer.Write(aircraftPosition.longitude);
            writer.Write(aircraftPosition.altitude);
            writer.Write(aircraftPosition.pitch);
            writer.Write(aircraftPosition.bank);
            writer.Write(aircraftPosition.heading);
            // add velocity
            writer.Write(aircraftPosition.velocityX);
            writer.Write(aircraftPosition.velocityY);
            writer.Write(aircraftPosition.velocityZ);
            writer.Write(aircraftPosition.angularVelocityX);
            writer.Write(aircraftPosition.angularVelocityY);
            writer.Write(aircraftPosition.angularVelocityZ);
            writer.Write(aircraftPosition.accelerationX);
            writer.Write(aircraftPosition.accelerationY);
            writer.Write(aircraftPosition.accelerationZ);
            // add control positions
            writer.Write(ConvertToAxis(aircraftPosition.rudder));
            writer.Write(ConvertToAxis(aircraftPosition.elevator));
            writer.Write(ConvertToAxis(aircraftPosition.aileron));
            writer.Write(ConvertToAxis(aircraftPosition.brakeLeft));
            writer.Write(ConvertToAxis(aircraftPosition.brakeRight));
            // add ground state
            writer.Write(aircraftPosition.elevation);
            // ground flags
            byte flags = 0;
            if (aircraftPosition.ground != 0) flags |= 0x01;
            if (Settings.Default.ElevationCorrection) flags |= 0x02;
            writer.Write(flags);
            // "STATIC CG TO GROUND", feet - the sender's own real ground clearance, used by the receiver to
            // ground a substitute model using its own clearance instead of the sender's (see
            // helicopters-on-elevated-platforms feature / ground-jitter-on-model-mismatch fix). Gated on
            // version >= 21008 to mirror ReadAircraftPosition1: the X-Plane IPC path pins to
            // XPLANE_POSITION_BLOB_VERSION (21007) and must NOT emit this field, because the native
            // plugin's AircraftPositionMsg has no slot for it.
            if (version >= 21008)
            {
                writer.Write(aircraftPosition.staticCgToGround);
            }
        }

        /// <summary>
        /// Read aircraft position and velocity from a stream
        /// </summary>
        /// <param name="reader">Binary reader</param>
        public static void ReadAircraftPosition1(short version, BinaryReader reader, ref AircraftPosition aircraftPosition)
        {
            // update position
            aircraftPosition.latitude = reader.ReadDouble();
            aircraftPosition.longitude = reader.ReadDouble();
            aircraftPosition.altitude = reader.ReadDouble();
            aircraftPosition.pitch = reader.ReadSingle();
            aircraftPosition.bank = reader.ReadSingle();
            aircraftPosition.heading = reader.ReadSingle();
            // update velocity
            aircraftPosition.velocityX = reader.ReadSingle();
            aircraftPosition.velocityY = reader.ReadSingle();
            aircraftPosition.velocityZ = reader.ReadSingle();
            aircraftPosition.angularVelocityX = reader.ReadSingle();
            aircraftPosition.angularVelocityY = reader.ReadSingle();
            aircraftPosition.angularVelocityZ = reader.ReadSingle();
            aircraftPosition.accelerationX = reader.ReadSingle();
            aircraftPosition.accelerationY = reader.ReadSingle();
            aircraftPosition.accelerationZ = reader.ReadSingle();
            // update controls
            aircraftPosition.rudder = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.elevator = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.aileron = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.brakeLeft = ConvertFromAxis(reader.ReadInt16());
            aircraftPosition.brakeRight = ConvertFromAxis(reader.ReadInt16());
            // update ground state
            aircraftPosition.elevation = version >= 10023 ? reader.ReadSingle() : 0.0f;
            byte flags = version >= 10023 ? reader.ReadByte() : (byte)0;
            aircraftPosition.ground = (flags & 0x01) != 0 ? 1 : 0;
            // "STATIC CG TO GROUND" - see Write() above. NaN (not 0.0f) for an older peer that didn't send
            // it, so downstream code can tell "no data" apart from a real zero clearance and fall back to
            // uncorrected placement instead of attempting a wrong correction.
            aircraftPosition.staticCgToGround = version >= 21008 ? reader.ReadSingle() : float.NaN;
        }

        /// <summary>
        /// Version table for reading position and velocity
        /// </summary>
        static readonly Dictionary<short, ReadVersion<AircraftPosition>> aircraftPositionVersions = new()
        {
            { 10022, ReadAircraftPosition1 },
        };

        /// <summary>
        /// Generic read handler
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <param name="version">Version to read</param>
        /// <param name="reader">Reader</param>
        public static void Read(short version, BinaryReader reader, ref AircraftPosition aircraftPosition)
        {
            AircraftPosition ap = aircraftPosition;
            ReadLengthPrefixed(version, reader, r => Read<AircraftPosition>(version, aircraftPositionVersions, r, ref ap));
            aircraftPosition = ap;
        }

        // Latitude/longitude/pitch/bank/heading are radians at this layer; altitude is metres.
        // A decode that landed on the wrong byte boundary (e.g. a peer/hub on a different wire
        // format) produces non-finite or absurd values - callers use these to drop the packet
        // and keep the last good state instead of publishing/relaying garbage.
        public static bool PlausibleAircraftPosition(in AircraftPosition p)
        {
            return double.IsFinite(p.latitude) && double.IsFinite(p.longitude) && double.IsFinite(p.altitude)
                && float.IsFinite(p.pitch) && float.IsFinite(p.bank) && float.IsFinite(p.heading)
                && float.IsFinite(p.velocityX) && float.IsFinite(p.velocityY) && float.IsFinite(p.velocityZ)
                && Math.Abs(p.latitude) <= 3.2 && Math.Abs(p.longitude) <= 6.4
                && p.altitude >= -2000.0 && p.altitude <= 200000.0;
        }

        public static bool PlausibleObjectPositionVelocity(in ObjectPositionVelocity p)
        {
            return double.IsFinite(p.latitude) && double.IsFinite(p.longitude) && double.IsFinite(p.altitude)
                && float.IsFinite(p.pitch) && float.IsFinite(p.bank) && float.IsFinite(p.heading)
                && float.IsFinite(p.velocityX) && float.IsFinite(p.velocityY) && float.IsFinite(p.velocityZ)
                && Math.Abs(p.latitude) <= 3.2 && Math.Abs(p.longitude) <= 6.4
                && p.altitude >= -2000.0 && p.altitude <= 200000.0;
        }

        /// <summary>
        /// Write integer variables to a stream
        /// </summary>
        public static void Write(BinaryWriter writer, Dictionary<uint, int> variables)
        {
            // write count
            writer.Write((ushort)variables.Count);
            // for each variable
            foreach (var variable in variables)
            {
                // add variable ID
                writer.Write(variable.Key);
                // add value
                writer.Write(variable.Value);
            }
        }

        /// <summary>
        /// Read integer variables from a stream
        /// </summary>
        public static void Read(short version, BinaryReader reader, Dictionary<uint, int> variables)
        {
            // read count
            ushort count = reader.ReadUInt16();
            // for each variable
            for (int i = 0; i < count; i++)
            {
                // read variable ID
                uint vuid = reader.ReadUInt32();
                // read integer
                int value = reader.ReadInt32();
                // add variable
                variables[vuid] = value;
            }
        }

        /// <summary>
        /// Write float variables to a stream
        /// </summary>
        public static void Write(BinaryWriter writer, Dictionary<uint, float> variables)
        {
            // write count
            writer.Write((ushort)variables.Count);
            // for each variable
            foreach (var variable in variables)
            {
                // add variable ID
                writer.Write(variable.Key);
                // add value
                writer.Write(variable.Value);
            }
        }

        /// <summary>
        /// Read float variables from a stream
        /// </summary>
        public static void Read(short version, BinaryReader reader, Dictionary<uint, float> variables)
        {
            // read count
            ushort count = reader.ReadUInt16();
            // for each variable
            for (int i = 0; i < count; i++)
            {
                // read variable ID
                uint vuid = reader.ReadUInt32();
                // read float
                float value = reader.ReadSingle();
                // add variable
                variables[vuid] = value;
            }
        }

        /// <summary>
        /// Write string8 variables to a stream
        /// </summary>
        public static void Write(BinaryWriter writer, Dictionary<uint, string> variables)
        {
            // write count
            writer.Write((ushort)variables.Count);
            // for each variable in the set
            foreach (var variable in variables)
            {
                // add variable ID
                writer.Write(variable.Key);
                // add value
                writer.Write(variable.Value);
            }
        }

        /// <summary>
        /// Read string8 variables from a stream
        /// </summary>
        public static void Read(short version, BinaryReader reader, Dictionary<uint, string> variables)
        {
            // read count
            ushort count = reader.ReadUInt16();
            // for each variable
            for (int i = 0; i < count; i++)
            {
                // read variable ID
                uint vuid = reader.ReadUInt32();
                // read string
                string value = reader.ReadString();
                // add variable
                variables[vuid] = value;
            }
        }

#endregion

#region Callbacks

#if XPLANE || CONSOLE
        /// <summary>
        /// XPlane model notify
        /// </summary>
        /// <param name="model"></param>
        void XPlaneModelUpdate(uint simId, bool user, bool plane, string callsign, string model, string icaoType)
        {
            // trim callsign
            callsign = callsign.TrimStart(' ', '\t').TrimEnd(' ', '\t');
            // get object
            Obj obj = objectList.Find(o => o.simId == simId);
            if (obj == null)
            {
                // check category
                switch (0)
                {
                    case 0: obj = new Plane(simId, callsign, icaoType, model, "", "", user); break;
//                    default: obj = new Obj(msg.simId, msg.model); break;
                }
                // substitution
                main.substitution ?. Masquerade(model, out obj.subModel, out obj.subType, out obj.subTrace);
                // set type role
                if (main.substitution != null) obj.typerole = main.substitution.GetTypeRole(obj.ownerModel);
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;
                // create variables
                CreateModelVariables(obj);
                // add new object to list
                objectList.Add(obj);

                // check for aircraft
                if (obj is Aircraft aircraft)
                {
                    // check for user aircraft
                    if (aircraft.owner == Obj.Owner.Me)
                    {
                        // set user aircraft
                        userAircraft = aircraft;
                        // set flight plan
                        userAircraft.flightPlan = userFlightPlan;
                    }
                    if (aircraft.flightPlan.callsignSetByUser == false)
                    {
                        aircraft.flightPlan.callsign = aircraft.originalCallsign;
                    }
                    // message
                    main.MonitorEvent("Listing aircraft '" + aircraft.flightPlan.callsign + "' User 'Me' - ID '" + obj.simId + "' - Model '" + obj.ownerModel + "'");
                }
                else
                {
                    // message
                    main.MonitorEvent("Listing object 'Me' - ID '" + obj.simId + "' - Model '" + obj.ownerModel + "'");
                }
            }
            else if (obj is Plane && plane == false || obj is Helicopter && plane)
            {
                // remove aircraft because the type has changed
                RemoveObject(obj);
            }
            else
            {
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;

                // check if model has changed
                if (obj.ownerModel.Equals(model) == false)
                {
                    // update model
                    obj.ownerModel = model;
                    main.substitution ?. Masquerade(model, out obj.subModel, out obj.subType, out obj.subTrace);
                }

                // check for aircraft
                if (obj is Aircraft)
                {
                    // aircraft
                    Aircraft aircraft = obj as Aircraft;
                    // check if callsign has changed
                    if (aircraft.originalCallsign.Equals(callsign) == false)
                    {
                        // update callsign
                        aircraft.originalCallsign = callsign;
                        if (aircraft.flightPlan.callsignSetByUser == false)
                        {
                            aircraft.flightPlan.callsign = aircraft.originalCallsign;
                        }
                    }
                    // update icao
                    aircraft.flightPlan.icaoType = icaoType;
                    // update user
                    aircraft.user = user;
                }
            }
        }
#endif

        /// <summary>
        /// XPlane connected notify
        /// </summary>
        void XPlaneConnected(short version)
        {
            main.MonitorEvent("Connected to simulator");
            main.MonitorEvent("X-Plane " + version);

            // set simulator information
            simulatorName = "X-Plane";
            simulatorVersion = version.ToString();

            // load models for this version
            main.ScheduleSubstitutionLoad();
            main.ScheduleHeightAdjustmentLoad();
            // refresh
#if !SERVER && !CONSOLE
            main.aircraftForm ?. refresher.Schedule(3);
            main.objectsForm ?. refresher.Schedule(3);
#endif

#if SIMCONNECT
            // close simconnect
            simconnect = null;
#endif
            // reset variable manager
            main.variableMgr.Reset();
            // load model variables
            LoadModelVariables();
        }

        /// <summary>
        /// XPlane remove notify
        /// </summary>
        void XPlaneRemove(uint simId)
        {
            // get object
            Obj obj = objectList.Find(o => o.simId == simId);
            if (obj != null)
            {
                if (obj.Injected)
                {
                    // remove object from sim
                    RemoveObjectFromSim(obj);
                }
                else
                {
                    // remove object completely
                    RemoveObject(obj);
                }
            }
        }

#if SIMCONNECT
        /// <summary>
        /// Resolve the junk-stripped raw type/model and the confidence-hierarchy-resolved ICAO type/airline/
        /// classCode/WTC for an OBJECT_INFO response (see the confidence hierarchy comment at ProcessSimObjectData's
        /// main call site). Shared by both "a new SimConnect object appeared" and "the user's existing own aircraft
        /// object was re-reported" handling - see the own-aircraft-changed auto-refresh - so a genuinely new object
        /// and an in-place aircraft/callsign change on an existing one resolve identically.
        /// </summary>
        void ResolveObjectInfoType(ObjectGetInfo info, out string type, out string model, out string learnIcaoType,
            out string learnClassCode, out string learnWtc, out bool learnClassCodeConfirmed, out string resolvedIcaoAirline)
        {
            // remove any junk from type
            type = info.type;
            type = type.Replace("TTATCCOM.AC_MODEL ", "");
            type = type.Replace("TTATCCOM.AC_MODEL_", "");
            type = type.Replace("TT:ATCCOM.AC_MODEL ", "");
            type = type.Replace("TT:ATCCOM.AC_MODEL_", "");
            type = type.Replace("ATCCOM.AC_MODEL ", "");
            type = type.Replace("ATCCOM.AC_MODEL_", "");
            type = type.Replace("$$:", "");
            type = type.Replace(".0.text", "");
            model = info.model;
            // convert the long hyphen
            model = model.Replace("â€“", "–");

            // learn this model's real ICAO type/airline/classCode/registration now that it's actually
            // instantiated - closes the gap for aircraft a title guess can't tag, and for add-ons whose
            // reported type doesn't match any Doc8643 designator. Confidence hierarchy (highest first): (1)
            // real aircraft.cfg/livery.cfg data, located via LIVERY FOLDER - FS2024 only, same reliability
            // tier non-FS2024 builds already get from their upfront folder scan; (2) DeriveLiveClassCode
            // (category/engine simvars) when no config file can be found/parsed; (3) a title-text guess
            // (handled elsewhere), for a model never yet instantiated.
            Substitution.DeriveLiveClassCode(info.category, info.engineType, info.numEngines, out string liveClassCode, out string liveWtc);
#if FS2024
            string configIcaoType = "", configWtc = "", configIcaoAirline = "", configAtcId = "", configClassCode = "", configIcaoResolutionNote = "";
            bool configConfirmed = main.substitution != null && main.substitution.TryReadConfigFromLiveryFolder(
                info.liveryFolder, model, out configIcaoType, out configWtc,
                out configIcaoAirline, out configAtcId, out configClassCode, out configIcaoResolutionNote);
            learnIcaoType = configConfirmed ? configIcaoType : type;
            learnClassCode = configConfirmed ? configClassCode : liveClassCode;
            learnWtc = configConfirmed && configWtc.Length > 0 ? configWtc : liveWtc;
            string learnIcaoAirline = configConfirmed && configIcaoAirline.Length > 0 ? configIcaoAirline : info.airline;
            string learnAtcId = configConfirmed ? configAtcId : "";
            learnClassCodeConfirmed = configConfirmed || liveClassCode.Length > 0;
            resolvedIcaoAirline = main.substitution?.LearnIcaoFromLiveObject(model, info.livery, learnIcaoType, learnIcaoAirline, learnClassCode, learnWtc, learnAtcId, configConfirmed, configConfirmed ? configIcaoResolutionNote : "") ?? "";
#else
            learnIcaoType = type;
            learnClassCode = liveClassCode;
            learnWtc = liveWtc;
            learnClassCodeConfirmed = liveClassCode.Length > 0;
            resolvedIcaoAirline = main.substitution?.LearnIcaoFromLiveObject(model, "", type, "", liveClassCode, liveWtc) ?? "";
#endif
        }

        /// <summary>
        /// Re-fetch callsign/type for the user's own aircraft from the sim, and if SimBrief auto-import is
        /// enabled, re-run the SimBrief fetch too - the same thing that already happens once at JoinFS
        /// startup (see Program.cs), now also triggered whenever the sim reports a genuinely different
        /// aircraft/callsign for "Me" mid-session (see ProcessSimObjectData's own-aircraft change detection).
        /// A detected change is treated as "a new flight": the callsign goes back to auto-tracking even if it
        /// had been manually set for the previous leg.
        /// </summary>
        void RefreshUserFlightPlanFromSim(Aircraft aircraft, string resolvedCallsign, string resolvedType)
        {
            aircraft.flightPlan.callsignSetByUser = false;
            aircraft.flightPlan.callsign = resolvedCallsign;
            aircraft.flightPlan.icaoType = resolvedType;
#if !CONSOLE
            bool autoImport = Settings.Default.SimBriefAutoImport && string.IsNullOrWhiteSpace(Settings.Default.SimBriefUsername) == false;
#else
            bool autoImport = false;
#endif
            main.MonitorEvent("Own aircraft changed - refreshed callsign '" + resolvedCallsign + "'/type '" + resolvedType + "' from the sim" + (autoImport ? ", re-fetching SimBrief" : ""));
#if !CONSOLE
            if (autoImport)
            {
                _ = RefreshUserFlightPlanFromSimBriefAsync();
            }
#endif
        }

        public void ProcessSimObjectData(uint objectId, uint requestId, object data)
        {
            // check object ID
            if (objectId > 0)
            {
                switch ((Requests)requestId)
                {
                    case Requests.OBJECT_INFO:
                        {
                            // get object
                            Obj obj = objectList.Find(o => o.simId == objectId);
                            if (obj == null)
                            {
                                // check that not currently creating object
                                if (creatingObject == null)
                                {
                                    // get info
                                    ObjectGetInfo info = (ObjectGetInfo)data;

                                    // Do not create object for "Asobo PassiveAircraft"
                                    if (info.model.Contains("PassiveAircraft"))
                                    {
                                        break;
                                    }

                                    // get nickname
                                    string callsign = info.callsign.TrimStart(' ', '\t').TrimEnd(' ', '\t');
                                    // diagnostic - confirm what SimConnect actually returns for ATC ID/AIRLINE/FLIGHT NUMBER
                                    // on the user's own aircraft when a distinct MSFS Call Sign is set (temporary, remove once confirmed)
                                    if (info.isUser != 0)
                                    {
#if FS2024
                                        main.MonitorEvent("DIAG ATC ID='" + info.callsign + "' ATC AIRLINE='" + info.airline + "' ATC FLIGHT NUMBER='" + info.flightNumber + "'");
#else
                                        main.MonitorEvent("DIAG ATC ID='" + info.callsign + "' ATC FLIGHT NUMBER='" + info.flightNumber + "'");
#endif
                                    }
                                    ResolveObjectInfoType(info, out string type, out string model, out string learnIcaoType,
                                        out string learnClassCode, out string learnWtc, out bool learnClassCodeConfirmed, out string resolvedIcaoAirline);

                                    // check category
                                    switch (info.category)
                                    {
                                        // case "Boat": obj = new Boat(objectId, callsign, type, model, info.isUser != 0); break;
                                        case "Boat": obj = new Boat(objectId, model); break;
                                        // case "GroundVehicle": obj = new Vehicle(objectId, callsign, type, model, info.isUser != 0); break;
                                        case "GroundVehicle": obj = new Vehicle(objectId, model); break;
                                        case "Airplane":
                                            {
                                                if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                                                {
#if FS2024
                                                    obj = new Plane(objectId, callsign, type, model, info.livery, info.airline, info.isUser != 0);
#endif
                                                } else
                                                {
#if !FS2024
                                                    obj = new Plane(objectId, callsign, type, model, "", "", info.isUser != 0);
#endif
                                                }
                                                break;
                                            }
                                        case "Helicopter":
                                            {
                                                if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                                                {
#if FS2024
                                                    obj = new Helicopter(objectId, callsign, type, model, info.livery, info.airline, info.isUser != 0);
#endif
                                                } else
                                                {
#if !FS2024
                                                    obj = new Helicopter(objectId, callsign, type, model, "", "", info.isUser != 0);
#endif
                                                }
                                                break;
                                            }
                                        default: obj = new Obj(objectId, model); break;
                                    }
                                    // set type role
                                    if (main.substitution != null) obj.typerole = main.substitution.GetTypeRole(obj.ownerModel);
                                    // carry the live-resolved ICAO type/airline/classCode onto the object itself, not
                                    // just the installed model's metadata - this is what Match()/the Recorder/the
                                    // network broadcast actually read, and previously stayed blank (or, for icaoType,
                                    // used the raw unconfirmed ATC MODEL string) forever for locally-discovered
                                    // objects otherwise
                                    obj.ownerIcaoType = learnIcaoType.Length > 0 ? learnIcaoType : type;
                                    obj.ownerIcaoAirline = resolvedIcaoAirline;
                                    obj.ownerClassCode = learnClassCode;
                                    obj.ownerWtc = learnWtc;
                                    obj.ownerClassCodeConfirmed = learnClassCodeConfirmed;
                                    // substitute model
                                    main.substitution ?. Masquerade(obj.ownerModel, out obj.subModel, out obj.subType, out obj.subTrace);
                                    // set expire time
                                    obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;
                                    // create variables
                                    CreateModelVariables(obj);
                                    // add new object to list
                                    objectList.Add(obj);
                                    // check for user aircraft
                                    if (obj.owner == Obj.Owner.Me && obj is Aircraft)
                                    {
                                        // set user aircraft
                                        userAircraft = obj as Aircraft;
                                        // the aircraft adopts the existing userFlightPlan object (not the other way
                                        // around) so anything already fetched into it - e.g. SimBrief on startup,
                                        // which can complete before the sim even reports this aircraft - survives
                                        userAircraft.flightPlan = userFlightPlan;
                                    }

                                    // check for aircraft
                                    if (obj is Aircraft)
                                    {
                                        // aircraft
                                        Aircraft aircraft = obj as Aircraft;
                                        // ATC ID is a tail number, not a callsign
                                        string tailNumber = callsign;
                                        string flightNumber = info.flightNumber.TrimStart(' ', '\t').TrimEnd(' ', '\t');
                                        aircraft.flightPlan.registration = tailNumber;
                                        aircraft.flightPlan.flightNumber = flightNumber;
                                        // prefer a synthesized real callsign (ICAO airline + flight number) over the tail number,
                                        // but only when the user hasn't explicitly set one - a manually-entered or SimBrief-
                                        // imported callsign must survive this aircraft being (re-)listed, matching the
                                        // "userFlightPlan survives" intent above; without this guard the sim-reported ATC
                                        // AIRLINE/FLIGHT NUMBER (aircraft.cfg/livery.cfg/MSFS2024 aircraft customization)
                                        // would clobber it every time. callsignSetByUser (not just emptiness) so this also
                                        // holds even if the user explicitly cleared the field on purpose.
                                        if (aircraft.flightPlan.callsignSetByUser == false)
                                        {
                                            aircraft.flightPlan.callsign = ResolveCallsign(resolvedIcaoAirline, flightNumber, tailNumber);
                                        }
                                        // the aircraft's own live-resolved type (learnIcaoType - the confidence
                                        // hierarchy above, e.g. base_container-resolved config data, not the raw
                                        // unresolved SimConnect ATC MODEL some add-ons report) - always kept here,
                                        // separate from flightPlan.icaoType below, so a later "fetch fresh from
                                        // the sim" (see FlightPlanForm's Clear button) has the accurate value to
                                        // read back rather than the raw constructor-time type or a stale one.
                                        aircraft.originalIcaoType = learnIcaoType.Length > 0 ? learnIcaoType : type;
                                        // fill in ICAO type/airline from the live-resolved data (learnIcaoType/
                                        // resolvedIcaoAirline - see the confidence hierarchy above), but only when
                                        // not already set - this was never populated here at all before, leaving
                                        // the Flight Plan dialog's Type field blank and never broadcasting an ICAO
                                        // type for the user's own aircraft unless a SimBrief import had already
                                        // filled it in; a prior SimBrief-sourced value still takes precedence,
                                        // matching the "userFlightPlan survives" intent right above
                                        if (aircraft.flightPlan.icaoType.Length == 0 && learnIcaoType.Length > 0)
                                        {
                                            aircraft.flightPlan.icaoType = learnIcaoType;
                                        }
                                        if (aircraft.flightPlan.icaoAirline.Length == 0 && resolvedIcaoAirline.Length > 0)
                                        {
                                            aircraft.flightPlan.icaoAirline = resolvedIcaoAirline;
                                        }
                                        // detect a real aircraft/callsign change for the user's own aircraft (a
                                        // genuinely new SimConnect object here, e.g. from a category-changing
                                        // swap) and auto-refresh the flight plan the same way this already
                                        // happens once at startup - see RefreshUserFlightPlanFromSim.
                                        if (obj.owner == Obj.Owner.Me)
                                        {
                                            string resolvedCallsign = ResolveCallsign(resolvedIcaoAirline, flightNumber, tailNumber);
                                            string resolvedType = learnIcaoType.Length > 0 ? learnIcaoType : type;
                                            if (lastKnownUserCallsign.Length > 0 && (lastKnownUserCallsign != resolvedCallsign || lastKnownUserIcaoType != resolvedType))
                                            {
                                                RefreshUserFlightPlanFromSim(aircraft, resolvedCallsign, resolvedType);
                                            }
                                            lastKnownUserCallsign = resolvedCallsign;
                                            lastKnownUserIcaoType = resolvedType;
                                        }
                                        // message
#if FS2024
                                        main.MonitorEvent("Listing aircraft '" + aircraft.flightPlan.callsign + "' User 'Me' - ID '" + obj.simId + "' - Model '" + obj.ownerModel + "' Livery '" + info.livery + "'");
#else
                                        main.MonitorEvent("Listing aircraft '" + aircraft.flightPlan.callsign + "' User 'Me' - ID '" + obj.simId + "' - Model '" + obj.ownerModel + "'");
#endif

                                    }
                                    else
                                    {
                                        // message
                                        main.MonitorEvent("Listing object 'Me' - ID '" + obj.simId + "' - Model '" + obj.ownerModel + "'");
                                    }
                                }
                            }
                            else
                            {
                                // check if owned by the simulator
                                if (obj.Injected == false)
                                {
                                    // set expire time
                                    obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;
                                }

                                // the user's own aircraft can be re-reported under the same SimConnect object
                                // ID too (e.g. a same-category livery/registration swap that doesn't get a new
                                // ID) - re-resolve and check for a change the same way a genuinely new object
                                // does above, see the own-aircraft-changed auto-refresh (RefreshUserFlightPlanFromSim).
                                if (obj.owner == Obj.Owner.Me && obj is Aircraft aircraft)
                                {
                                    ObjectGetInfo info = (ObjectGetInfo)data;
                                    string tailNumber = info.callsign.TrimStart(' ', '\t').TrimEnd(' ', '\t');
                                    string flightNumber = info.flightNumber.TrimStart(' ', '\t').TrimEnd(' ', '\t');
                                    ResolveObjectInfoType(info, out string type, out _, out string learnIcaoType,
                                        out _, out _, out _, out string resolvedIcaoAirline);
                                    string resolvedCallsign = ResolveCallsign(resolvedIcaoAirline, flightNumber, tailNumber);
                                    string resolvedType = learnIcaoType.Length > 0 ? learnIcaoType : type;
                                    if (lastKnownUserCallsign.Length > 0 && (lastKnownUserCallsign != resolvedCallsign || lastKnownUserIcaoType != resolvedType))
                                    {
                                        RefreshUserFlightPlanFromSim(aircraft, resolvedCallsign, resolvedType);
                                    }
                                    lastKnownUserCallsign = resolvedCallsign;
                                    lastKnownUserIcaoType = resolvedType;
                                }
                            }
                        }
                        break;

                    case Requests.OBJECT_POSITION_VELOCITY:
                        {
                            // get object
                            Obj obj = objectList.Find(o => o.simId == objectId);
                            if (obj != null)
                            {
                                // get sim state
                                ObjectPositionVelocity positionVelocity = (ObjectPositionVelocity)data;

                                // update position
                                obj.simPosition = new Pos(ref positionVelocity);
                                // store current time
                                obj.simTime = main.ElapsedTime;

                                // check if user or broadcasting this aircraft
                                if (obj.owner == Obj.Owner.Me || main.network.localNode.Connected && IsBroadcast(obj))
                                {
                                    // check if not under remote control
                                    if (obj.remoteFlightControl == false)
                                    {
                                        // update velocity
                                        obj.netVelocity = new Vel(ref positionVelocity);
                                        // store current time
                                        obj.netSimTime = main.ElapsedTime;
                                    }

                                    // check if broadcasting
                                    if (main.network.localNode.Connected)
                                    {
                                        try
                                        {
                                            if (IsBroadcast(obj))
                                            {
                                                // create message
                                                main.network.WriteObjectPositionVelocityMessage(obj, ref positionVelocity);

                                                // get nodes
                                                LocalNode.Nuid[] nodeList = main.network.localNode.GetNodeList();
                                                // for each node
                                                foreach (var nuid in nodeList)
                                                {
                                                    // get remote object
                                                    Obj remoteObject = objectList.Find(o => o.ownerNuid == nuid && o is Aircraft && (o as Aircraft).user);
                                                    // get interval mask
                                                    int intervalMask = GetIntervalMask(obj, remoteObject);
                                                    // check if node's simulator is not connected
                                                    if (main.network.GetNodeSimulatorConnected(nuid) == false)
                                                    {
                                                        // increase interval (every 32)
                                                        intervalMask = 0x1f;
                                                    }

                                                    // check send interval
                                                    if ((obj.positionCount & intervalMask) == 0)
                                                    {
                                                        // broadcast message to other nodes
                                                        main.network.localNode.Send(nuid);
                                                    }
                                                }
                                            }
                                            // increment count
                                            obj.positionCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            main.MonitorEvent("ERROR - Failed to write position/velocity message: " + ex.Message);
                                        }
                                    }
                                }

                                // check if recording
                                if (main.recorder.recording && obj.record && obj.Injected == false)
                                {
                                    // record position and velocity
                                    main.recorder.Record(obj.recorderObj, main.ElapsedTime, ref positionVelocity);
                                }
                            }
                        }
                        break;

                    case Requests.OBJECT_POSITION:
                        {
                            // get object
                            Obj obj = objectList.Find(o => o.simId == objectId);
                            if (obj != null)
                            {
                                // check if user object is no longer entered
                                if (obj.owner != Obj.Owner.Me || enteredAircraft != null)
                                {
                                    // get sim position
                                    ObjectPosition objPosition = (ObjectPosition)data;

                                    // update position
                                    obj.simPosition = new Pos(ref objPosition);
                                    // store current time
                                    obj.simTime = main.ElapsedTime;
                                }
                            }
                        }
                        break;

                    default:
                        if (requestId >= (uint)PositionPollRequestIdBase)
                        {
                            // per-object AIRCRAFT_POSITION poll response - see PositionPollRequestIdBase
                            AircraftPosition aircraftPosition = (AircraftPosition)data;
                            ProcessAircraftPosition(objectId, main.ElapsedTime, ref aircraftPosition);
                        }
                        else if (requestId < (uint)VariableMgr.ScDefinition.ID0)
                        {
                            main.MonitorEvent("ERROR - Unknown request ID '" + requestId + "'");
                        }
                        break;
                }

                // check for variable
                if (requestId >= (uint)VariableMgr.ScRequest.ID0 && requestId < (uint)PositionPollRequestIdBase)
                {
                    // get aircraft
                    if (objectList.Find(o => o.simId == objectId) is Aircraft aircraft)
                    {
                        // update variable
                        aircraft.variableSet ?. DetectSimconnect((VariableMgr.ScRequest)requestId, data);
                    }
                }
            }
        }

        public void ProcessWeatherObservation(uint requestId, string metarData)
        {
            switch ((Requests)requestId)
            {
                case Requests.WEATHER:
                    {
                        // strip station from METAR
                        int spaceIndex = metarData.IndexOf(' ');
                        // check for valid METAR
                        if (spaceIndex != -1 && metarData.Length > 1)
                        {
                            // set weather
                            string metar = metarData[(spaceIndex + 1)..];

                            // for each object
                            foreach (var obj in objectList)
                            {
                                // if aircraft is broadcast
                                if (obj is Aircraft && IsBroadcast(obj))
                                {
                                    // set weather
                                    (obj as Aircraft).SetWeather(metar);
                                }
                            }

                            // check if connected
                            if (main.network.localNode.Connected)
                            {
                                // create message
                                main.network.WriteWeatherUpdateMessage(metar);
                                // broadcast message to other nodes
                                main.network.localNode.Broadcast();
                            }
                        }
                    }
                    break;
            }
        }

        public void ProcessAssignedObjectId(uint objectId, uint requestId)
        {
            switch ((Requests)requestId)
            {
                case Requests.CREATE_OBJECT:
                    {
                        // check for new aircraft
                        if (creatingObject != null)
                        {
                            // get creating object
                            Obj obj = creatingObject;
                            // reset creating object
                            creatingObject = null;

                            // set sim ID
                            obj.simId = objectId;
                            // take control
                            obj.takeControl = true;
                            // give the sim's own attitude/gear settle a clear run before JoinFS's vertical
                            // correction starts nudging toward a fixed target - see VerticalSettleGraceSeconds
                            obj.verticalCorrectionSuppressedUntil = main.ElapsedTime + VerticalSettleGraceSeconds;
                            // reset object
                            ResetObject(obj);
                            // create variables
                            CreateModelVariables(obj);
                            // check for aircraft
                            if (obj is Aircraft)
                            {
                                // aircraft
                                Aircraft aircraft = obj as Aircraft;
                                // show event
                                main.MonitorEvent("Aircraft injected '" + aircraft.flightPlan.callsign + "' - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - ID '" + obj.simId + "' - Sub '" + obj.ModelTitle + "'");
                            }
                            else
                            {
                                // show event
                                main.MonitorEvent("Object injected - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - ID '" + obj.simId + "' - Sub '" + obj.ModelTitle + "'");
                            }
                        }
                        else
                        {
                            // remove from sim
                            simconnect ?. RemoveObject(objectId, Requests.REMOVE_OBJECT);
                            // show event
                            main.MonitorEvent("Unknown object assigned an ID " + objectId + " Request=" + requestId);
                        }
                    }
                    break;
            }
        }

        public void ProcessEventObjectAddremove(uint eventId, uint data)
        {
            switch ((Event)eventId)
            {
                case Event.OBJECT_ADDED:
                    break;

                case Event.OBJECT_REMOVED:
                    // find object in list
                    Obj obj = objectList.Find(o => o.simId == data);
                    if (obj != null)
                    {
                        // remove object
                        RemoveObjectFromList(obj);
                    }
                    break;
            }
        }

        /// <summary>
        /// Frame counter
        /// </summary>
        public int frameCount = 0;

        public void ProcessEventFrame(uint eventId)
        {
            switch ((Event)eventId)
            {
                case Event.FRAME:
                    // increment update counter
                    frameCount++;

                    // for each object
                    foreach (var obj in objectList)
                    {
                        // check for aerobatics
//                        if (Math.Abs(obj.simPosition.angles.x) > Math.PI * 0.25 || Math.Abs(obj.simPosition.angles.z) > Math.PI * 0.5)
                        {
                            // update object velocity
                            UpdateSimObjectVelocity(obj);
                        }
                    }

                    break;
            }
        }

        public void ProcessEvent(uint eventId, uint data)
        {
            // get event ID
            Event e = (Event)eventId;

            // check for sim start/stop - re-arm any failed injections so traffic appears once the user
            // is actually in a flight, without needing a [Sim] toggle (see Fix 3)
            if (e == Event.SIM_START || e == Event.SIM_STOP)
            {
                simRunning = (e == Event.SIM_START);
                main.MonitorEvent("Simulator " + (simRunning ? "started" : "stopped") + " (SimConnect event)");
                if (simRunning)
                {
                    RearmFailedInjections("SimStart");
                }
                return;
            }

            // check for pause event
            if (e == Event.PAUSE)
            {
                // monitor
                main.MonitorNetwork("Simulator Pause '" + data + "'");

                // for each object
                foreach (var obj in objectList)
                {
                    // check for our object
                    if (obj.owner != Obj.Owner.Network)
                    {
                        // set object paused state
                        obj.paused = (data == 1);
                    }
                }
            }
            // intercept relevant events
            else if (e >= Event.EVENT_00011000 && e <= Event.EVENT_0001100A)
            {
                // get user aircraft
                if (objectList.Find(o => o.owner == Obj.Owner.Me) is Aircraft aircraft)
                {
                    // check for broadcast
                    if (main.network.localNode.Connected)
                    {
                        try
                        {
                            // check if entered another aircraft
                            if (aircraft.owner == Obj.Owner.Me && enteredAircraft != null)
                            {
                                // create message
                                main.network.WriteSimEventMessage(aircraft.netId, eventId, data);
                                // broadcast message to other nodes
                                main.network.localNode.Send(enteredAircraft.ownerNuid);
                            }
                            // check if aircraft is being broadcast
                            else if (IsBroadcast(aircraft) && aircraft.Injected == false)
                            {
                                // create message
                                main.network.WriteSimEventMessage(aircraft.netId, eventId, data);
                                // broadcast message to other nodes
                                main.network.localNode.Broadcast();
                            }
                        }
                        catch (Exception ex)
                        {
                            main.MonitorEvent("ERROR - Failed to write sim event message: " + ex.Message);
                        }

                        // check if recording
                        if (main.recorder.recording && aircraft.record)
                        {
                            // record event
                            main.recorder.Record(aircraft.recorderObj, eventId, data);
                        }
                    }
                }
            }
        }
#endif

        /// <summary>
        /// Simulator details
        /// </summary>
        string simulatorName = "";
        string simulatorVersion = "0";

        /// <summary>Tracks the SimStart/SimStop system events for logging/diagnostics only - the injection branch is never gated on it (see Fix 3).</summary>
        public bool simRunning = false;

        /// <summary>
        /// Clear latched injection-failure state on every injected object so the finder retries them
        /// immediately - see Fix 3. Called from ProcessOpen and on a SimStart event.
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

        public void ProcessOpen(string name, uint simVerMaj, uint simVerMin, uint simBuiMaj, uint simBuiMin, uint appVerMaj, uint appVerMin, uint appBuiMaj, uint appBuiMin)
        {
            // convert name for MSFS2020
            if (name == "KittyHawk")
            {
                name = "Microsoft Flight Simulator 2020";
            }

            // convert name for MSFS2024
            if (name == "SunRise")
            {
                name = "Microsoft Flight Simulator 2024";
            }

            // show messages
            main.MonitorEvent("Connected to simulator");
            main.MonitorEvent("SimConnect '" + simVerMaj.ToString() + "." + simVerMin.ToString() + "." + simBuiMaj.ToString() + "." + simBuiMin.ToString() + "'");
            main.MonitorEvent(name + " '" + appVerMaj.ToString() + "." + appVerMin.ToString() + "." + appBuiMaj.ToString() + "." + appBuiMin.ToString() + "'");

            // store simulator details
            simulatorName = name;
            simulatorVersion = appVerMaj + "." + appVerMin;

            // a fresh SimConnect OPEN means we're (re)connected - clear any latched injection failures
            // from a previous session/attempt so traffic doesn't wait on the substitution reload's flush
            // or a [Sim] toggle (see Fix 3)
            RearmFailedInjections("ProcessOpen");

            // load models for this version
            main.ScheduleSubstitutionLoad();
            main.ScheduleHeightAdjustmentLoad();
            // refresh
#if !SERVER && !CONSOLE
            main.aircraftForm ?. refresher.Schedule(3);
            main.objectsForm ?. refresher.Schedule(3);
#endif

            // reset variable manager
            main.variableMgr.Reset();
            // load model variables
            LoadModelVariables();
        }

#if FS2024
        public bool requestModelListInProgress = false;
        public bool requestModelListIsVerbose = false;
        /// <summary>
        /// Enumeration requests (aircraft/helicopter/balloon) still awaiting completion
        /// </summary>
        readonly HashSet<Requests> pendingModelListRequests = [];
        public void ProcessModelList(SIMCONNECT_RECV_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST data)
        {
            for (int i = 0; i < data.dwArraySize; ++i)
	        {
		        SIMCONNECT_ENUMERATE_SIMOBJECT_LIVERY element = (SIMCONNECT_ENUMERATE_SIMOBJECT_LIVERY) data.rgData[i];
                if (element.AircraftTitle.Contains("PassiveAircraft") == false)
                {
                    // We're using in MSFS2024 the variation as livery.
                    // This is not totally correct in MSFS2024, since variation
                    // is "Passengers" or "Cargo" and not the livery.
                    // In MSFS2024 the variation is embedded in the model name.
                    main.substitution.SubmitModel(element.AircraftTitle, "", element.AircraftTitle, element.LiveryName, 0, "MSFS2024");
                    // main.MonitorEvent("Model " + element.AircraftTitle + " livery " + element.LiveryName);
                }
            }
            // main.MonitorEvent("Read " + data.dwArraySize + " models from the simulator.");
            if (data.dwEntryNumber + 1 == data.dwOutOf)
            {
                // this particular enumeration request (aircraft/helicopter/balloon) has finished
                pendingModelListRequests.Remove((Requests)data.dwRequestID);
                if (pendingModelListRequests.Count > 0)
                {
                    // still waiting on the other enumeration calls
                    return;
                }

                main.MonitorEvent("All models from the simulator ingested.");

                // TODO: cleanup code
                //if (main.settingsUseAIFeatures)
                //{
                //    main.EnqueueCommand(async () =>
                //    {
                //        await main.substitution.enrichModelService.EnrichModelsWithDetailsAsync(main.substitution.models);
                //        main.MonitorEvent("Model data enriched");
                //        await main.substitution.embeddingService.GenerateEmbeddingsFromModelsAsync(main.substitution.models);
                //        main.MonitorEvent("Model data embedded");
                //    });
                //}

                requestModelListInProgress = false;

                if (requestModelListIsVerbose)
                {
                    // check for models scanned
                    if (main.substitution.models.Count > 0)
                    {
                        main.scheduleShowMessage = Resources.Strings.FoundPrefix + " " + main.substitution.models.Count.ToString() + " " + Resources.Strings.FoundSuffix;
                    }
                    else
                    {
                        main.scheduleShowMessage = "No models found";
                    }
                    requestModelListIsVerbose = false;
                }
                // rebuild ICAO indexes now that all three enumeration requests have populated models[]
                main.substitution?.MakeIcaoIndex();
                // at the very end
                main.EnqueueCommand(() =>
                {
                    main.substitution?.Match();
                });
            }
        }
#endif

        public void ProcessQuit()
        {
            // close
            Close();
        }

        public void ProcessException(uint exception)
        {
            switch (exception)
            {
                case 5:
                    main.MonitorEvent("ERROR - Simconnect version mismatch");
                    break;

                case 14:
                    main.MonitorEvent("ERROR - Invalid METAR");
                    break;

                case 15:
                    main.MonitorEvent("ERROR - Unable to get weather observation");
                    break;

                case 20:
                    main.MonitorEvent("ERROR - SimConnect data error");
                    break;

                case 22:
                    // check for creating object
                    if (creatingObject != null)
                    {
                        // get creating object
                        Obj obj = creatingObject;
                        // reset creating object
                        creatingObject = null;
                        // check type
                        if (obj is Aircraft)
                        {
                            // aircraft
                            Aircraft aircraft = obj as Aircraft;
                            // show event
                            main.MonitorEvent("ERROR - Failed to inject aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - ID '" + obj.simId + "' - Sub '" + obj.ModelTitle + "'");
                        }
                        else
                        {
                            main.MonitorEvent("ERROR - Failed to inject object - User '" + ((obj.owner == Obj.Owner.Network) ? obj.ownerNuid.ToString() : "Me") + "' - ID '" + obj.simId + "' Sub - '" + obj.ModelTitle + "'");
                        }

                        // failed - but not permanently. The most common cause is MSFS still sitting on
                        // the menu / loading a flight when the attempt was made; record the time and
                        // count so the injection finder can re-arm this after a backoff (see Fix 3).
                        obj.failed = true;
                        obj.failedTime = main.ElapsedTime;
                        obj.failedCount++;
                    }
                    else
                    {
                        main.MonitorEvent("ERROR - Failed to create object");
                    }
                    break;
                    
                default:
                    main.MonitorEvent("ERROR - Simconnect exception - " + exception);
                    break;
            }
        }

#endregion

#region Height Adjustment

        /// <summary>
        /// list of height adjustments
        /// </summary>
        readonly Dictionary<string, int> heightAdjustments = [];

        /// <summary>
        /// get the height adjustment for a model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public int GetHeightAdjustment(Substitution.Model model)
        {
            // find model
            if (model != null && heightAdjustments.TryGetValue(model.longType, out int value))
            {
                // return adjustment in metres
                return value;
            }

            // no adjustment
            return 0;
        }

        /// <summary>
        /// update a height adjustment
        /// </summary>
        /// <param name="model"></param>
        /// <param name="adjustment"></param>
        public void UpdateHeightAdjustment(Substitution.Model model, int adjustment)
        {
            // check for valid model
            if (model != null)
            {
                // check for no adjustment
                if (adjustment == 0)
                {
                    // check for model
                    heightAdjustments.Remove(model.longType);
                }
                else
                {
                    // set adjustment
                    heightAdjustments[model.longType] = adjustment;
                }
            }
        }

        /// <summary>
        /// Load height adjustment
        /// </summary>
        public void LoadHeightAdjustments()
        {
            // check for simulator
            if (Connected)
            {
                try
                {
                    // make filename
                    string filename = main.storagePath + Path.DirectorySeparatorChar + "heights - " + GetSimulatorName() + ".txt";

                    // check for matching file
                    if (File.Exists(filename))
                    {
                        // clear list
                        heightAdjustments.Clear();

                        // open file
                        StreamReader reader = File.OpenText(filename);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // parse line
                            string[] parts = line.Split('=');
                            // check for three parts
                            if (parts.Length == 2)
                            {
                                // get model
                                string type = parts[0].TrimStart(' ').TrimEnd(' ');
                                // get adjustment
                                if (int.TryParse(parts[1].TrimStart(' ').TrimEnd(' '), NumberStyles.Number, CultureInfo.InvariantCulture, out int adjustment))
                                {
                                    // validate
                                    if (type.Length > 0)
                                    {
                                        // check for title
                                        Substitution.Model model = main.substitution ?. GetModel(type);
                                        // check if model found
                                        if (model != null)
                                        {
                                            // add adjustment
                                            heightAdjustments[model.longType] = adjustment;
                                        }
                                        else
                                        {
                                            // add adjustment
                                            heightAdjustments[type] = adjustment;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // monitor
                                main.ShowMessage(Resources.Strings.InvalidHeight + ": " + line);
                            }
                        }
                        // close reader
                        reader.Close();

                        // message
                        main.MonitorEvent("Loaded " + heightAdjustments.Count + " height adjustments");
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
            else
            {
                // error
                main.MonitorEvent("Unable to load height adjustments because a simulator is not connected.");
            }

#if !SERVER && !CONSOLE
            // refresh
            main.aircraftForm ?. refresher.Schedule();
#endif
        }

        /// <summary>
        /// Save height adjustments
        /// </summary>
        public void SaveHeightAdjustments()
        {
            // check for simulator
            if (Connected)
            {
                try
                {
                    // make filename
                    string filename = main.storagePath + Path.DirectorySeparatorChar + "heights - " + GetSimulatorName() + ".txt";

                    // open file
                    StreamWriter writer = new(filename);
                    if (writer != null)
                    {
                        // for each adjustment
                        foreach (var pair in heightAdjustments)
                        {
                            // write adjustment
                            writer.WriteLine(pair.Key + "=" + pair.Value);
                        }
                        // close writer
                        writer.Close();
                    }

                    // message
                    main.MonitorEvent("Saved " + heightAdjustments.Count + " height adjustments");
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
            else
            {
                // error
                main.MonitorEvent("Unable to save height adjustments because a simulator is not connected.");
            }
        }

#endregion

#region Variable Sets

        // get heading vuid
        readonly uint headingVuid = VariableMgr.CreateVuid("sim/cockpit/autopilot/heading_mag");

        /// <summary>
        /// List of user defined variable sets for different models
        /// </summary>
        public Dictionary<string, List<string>> modelVariables = [];

        /// <summary>
        /// Make the filename from the simulator name and version
        /// </summary>
        /// <returns></returns>
        string MakeModelVariablesFilename()
        {
            return main.storagePath + Path.DirectorySeparatorChar + "variables.txt";
        }

        /// <summary>
        /// Load model variables
        /// </summary>
        void LoadModelVariables_Old(string line)
        {
            // split line
            string[] parts = line.Split('|');
            // check that model is not already present
            if (modelVariables.ContainsKey(parts[0]) == false)
            {
                // add new entry
                modelVariables[parts[0]] = [];
                // for each variable file
                for (int index = 1; index < parts.Length; index++)
                {
                    // add variable filename
                    modelVariables[parts[0]].Add(parts[index]);
                }
            }
        }


        /// <summary>
        /// Load model variables
        /// </summary>
        void LoadModelVariables()
        {
            // clear existing data
            modelVariables.Clear();

            try
            {
                // make filename
                string filename = MakeModelVariablesFilename();
                // check for models file
                if (File.Exists(filename))
                {
                    // read all models from file
                    string[] lines = File.ReadAllLines(filename);
                    // for all lines
                    foreach (string line in lines)
                    {
                        // split line
                        string[] separator = ["[+]"];
                        string[] parts = line.Split(separator, StringSplitOptions.None);
                        // check for parts
                        if (parts.Length > 1)
                        {
                            // check that model is not already present
                            if (modelVariables.ContainsKey(parts[0]) == false)
                            {
                                // add new entry
                                modelVariables[parts[0]] = [];
                                // for each variable file
                                for (int index = 1; index < parts.Length; index++)
                                {
                                    // add variable filename
                                    modelVariables[parts[0]].Add(parts[index]);
                                }
                            }
                        }
                        else
                        {
                            // load old format
                            LoadModelVariables_Old(line);
                        }
                    }
                }

                // message
                main.MonitorEvent("Loaded " + modelVariables.Count + " variable set(s)");
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Save model variables
        /// </summary>
        public void SaveModelVaribles()
        {
            // open models file
            StreamWriter writer = null;

            try
            {
                // make filename
                string filename = MakeModelVariablesFilename();

                // open models file
                writer = new StreamWriter(filename);
                // for all models
                foreach (var model in modelVariables)
                {
                    // check that model is not using default variables
                    if (UsingDefaultVariables(model.Key) == false)
                    {
                        // write model
                        writer.Write(model.Key);
                        // for each variable file
                        foreach (var variableFilename in model.Value)
                        {
                            // write filename
                            writer.Write("[+]" + variableFilename);
                        }
                        // finish line
                        writer.WriteLine();
                    }
                }
                // close file
                writer.Close();

                // message
                main.MonitorEvent("Saved " + modelVariables.Count + " variable set(s)");
            }
            catch (Exception ex)
            {
                // monitor
                main.ShowMessage(ex.Message);
                // close writer
                writer?.Close();
            }
        }

        /// <summary>
        /// Is a model using default variables
        /// </summary>
        bool UsingDefaultVariables(string title)
        {
            // check for model variables
            if (modelVariables.TryGetValue(title, out List<string> modelFiles))
            {
                // get default files
                List<string> defaultFiles = GetModelDefaultVariables(title);
                // check if number of files is the same
                if (modelFiles.Count == defaultFiles.Count)
                {
                    // for each file
                    for (int index = 0; index < modelFiles.Count; index++)
                    {
                        // check if file is different
                        if (modelFiles[index] != defaultFiles[index])
                        {
                            // not using the default
                            return false;
                        }
                    }

                    // using default
                    return true;
                }
                else
                {
                    // not using default
                    return false;
                }
            }
            else
            {
                // using default
                return true;
            }
        }

        /// <summary>
        /// Get list of variables for a particular model
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public List<string> GetModelDefaultVariables(string title)
        {
            // create list
            List<string> list = [];

            // get model
            Substitution.Model model = main.substitution ?. GetModel(title);
            if (model != null)
            {
                // check model type
                switch (model.typerole)
                {
                    case Substitution.TypeRole_SingleProp:
                        list.Add("Plane.txt");
                        list.Add("SingleProp.txt");
                        break;

                    case Substitution.TypeRole_TwinProp:
                        list.Add("Plane.txt");
                        list.Add("TwinProp.txt");
                        break;

                    case Substitution.TypeRole_Airliner:
                        list.Add("Plane.txt");
                        list.Add("QuadTurbine.txt");
                        break;

                    case Substitution.TypeRole_Rotorcraft:
                        list.Add("Rotorcraft.txt");
                        list.Add("SingleTurbine.txt");
                        break;

                    case Substitution.TypeRole_Glider:
                        list.Add("Plane.txt");
                        break;

                    case Substitution.TypeRole_Fighter:
                        list.Add("Plane.txt");
                        list.Add("TwinTurbine.txt");
                        break;

                    case Substitution.TypeRole_Bomber:
                        list.Add("Plane.txt");
                        list.Add("QuadTurbine.txt");
                        break;

                    case Substitution.TypeRole_FourProp:
                        list.Add("Plane.txt");
                        list.Add("QuadProp.txt");
                        break;
                }
            }
            else
            {
                // use default
                list.Add("Plane.txt");
                list.Add("SingleProp.txt");
            }

            // return new list
            return list;
        }

        /// <summary>
        /// Get list of variables for a particular model
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public List<string> GetModelVariables(string title)
        {
            // check for existing variables
            if (modelVariables.TryGetValue(title, out List<string> value))
            {
                // return variable file list
                return value;
            }
            else
            {
                // return default variables
                return GetModelDefaultVariables(title);
            }
        }

        /// <summary>
        /// Load model variables for a particular object
        /// </summary>
        void CreateModelVariables(Obj obj)
        {
            // variable lists
            List<string> files = [];
            // check if sim is connected
            if (Connected)
            {
                // get files from model
                files = GetModelVariables(obj.ModelTitle);
            }
            else if (obj is Plane)
            {
                // add plane variables as default
                files.Add("Plane.txt");
            }
            else if (obj is Helicopter)
            {
                // add rotorcraft variables as default
                files.Add("Rotorcraft.txt");
            }
            // stop requests
            obj.variableSet ?. StopRequests();
            // reload variables
            obj.variableSet = new VariableMgr.Set(main, obj.simId, obj.Injected, files);
        }

#endregion

#region Main Interface

        /// <summary>
        /// Reference to the main form
        /// </summary>
        readonly Main main;

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
        /// Get the ATC ID for an aircraft
        /// </summary>
        public string MakeAtcId(Aircraft aircraft)
        {
            // get nickname
            string nickname = aircraft.user ? main.network.GetNodeName(aircraft.ownerNuid) : "";
            // get callsign depending on option
            string simCallsign = (Settings.Default.ShowNicknames && nickname.Length > 0) ? nickname : aircraft.flightPlan.callsign;
            // truncate string
            return (simCallsign.Length > 10) ? simCallsign[..10] : simCallsign;
        }

        /// <summary>
        /// Set ATC ID for an aircraft
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        void SetAtcId(Aircraft aircraft)
        {
#if SIMCONNECT
            // check for simconnect
            if (simconnect != null && aircraft.Created)
            {
                // update aircraft ID
                AircraftSetId setId = new()
                {
                    // truncate string
                    callsign = MakeAtcId(aircraft),
                    airline = "",
                    number = ""
                };

                simconnect.SetData(Definitions.AIRCRAFT_SET_ID, aircraft.simId, setId);
            }
#endif
        }

        /// <summary>
        /// Set ATC ID for an owner
        /// </summary>
        /// <param name="aircraft">Owner</param>
        public void SetAtcId(LocalNode.Nuid ownerNuid)
        {
            // find user aircraft
            if (objectList.Find(o => o.ownerNuid == ownerNuid && o is Aircraft && (o as Aircraft).user) is Aircraft aircraft)
            {
                // Set ID
                SetAtcId(aircraft);
            }
        }

        /// <summary>
        /// Convert frequency from an int to string
        /// </summary>
        /// <param name="frequency">Frequency</param>
        /// <returns>Frequency</returns>
        public static string FrequencyIntToString(int frequency)
        {
            // limit frequency
            frequency = Math.Min(60000, Math.Max(0, frequency));
            // convert to string
            return "1" + (frequency / 1000).ToString() + "." + (frequency % 1000).ToString("D3");
        }

        /// <summary>
        /// Convert frequency from a string to an int
        /// </summary>
        /// <param name="frequency">Frequency</param>
        /// <returns>Frequency</returns>
        public static int FrequencyStringToInt(string frequency)
        {
            // frequency result
            int result = 0;
            // split around dot
            string[] parts = frequency.Split('.');
            // check for first part
            if (parts.Length > 0)
            {
                // convert first part
                if (int.TryParse(parts[0][1..], NumberStyles.Number, CultureInfo.InvariantCulture, out int r0))
                {
                    // add to result
                    result += r0 * 1000;
                }
            }
            // check for second part
            if (parts.Length > 1)
            {
                // convert first part
                if (int.TryParse(parts[1].PadRight(3, '0'), NumberStyles.Number, CultureInfo.InvariantCulture, out int r1))
                {
                    // add to result
                    result += r1;
                }
            }
            // return
            return result;
        }

        /// <summary>
        /// Make callsign string from an airport and ATC level
        /// </summary>
        /// <param name="airport">Airport</param>
        /// <param name="level">Level</param>
        /// <returns>Callsign</returns>
        public static string MakeAtcCallsign(string airport, int level)
        {
            // callsign
            string callsign = airport;
            // check for airport
            if (callsign.Length > 0)
            {
                // add level
                switch (level)
                {
                    case 0: callsign += "_DEL"; break;
                    case 1: callsign += "_GND"; break;
                    case 2: callsign += "_TWR"; break;
                    case 3: callsign += "_APP"; break;
                    case 4: callsign += "_CTR"; break;
                }
            }
            // return result
            return callsign;
        }

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

        /// <summary>
        /// Process module
        /// </summary>
        public void DoWork()
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

            // check connection
            if (checkConnectionTimer.Elapsed(main.ElapsedTime) && checkConnectionCount < CHECK_CONNECTION_ATTEMPTS)
            {
                // check connection
                CheckConnection();
                // update attempts
                checkConnectionCount++;
            }

#if SIMCONNECT
            // process messages
            simconnect?.ReceiveMsg();
#endif

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
                // find object that needs creating. A prior injection failure (SimConnect exception 22 -
                // usually MSFS still loading) no longer bars an object forever: it's eligible again once
                // settingsInjectionRetrySeconds have passed, up to FAILED_RETRY_MAX attempts (see Fix 3).
                creatingObject = objectList.Find(o => o.owner != Obj.Owner.Me && o.Created == false
                    && (o.failed == false || (main.ElapsedTime - o.failedTime > main.settingsInjectionRetrySeconds && o.failedCount < FAILED_RETRY_MAX))
                    && main.log.IgnoreNode(o.ownerNuid) == false && main.log.IgnoreName(o.ownerModel) == false && o != enteredAircraft && o.distance * 0.00053995680346 < activityCircle);

                // check for object
                if (creatingObject != null)
                {
                    // clear a re-armed failure flag so this attempt starts clean
                    if (creatingObject.failed)
                    {
                        creatingObject.failed = false;
                        main.MonitorEvent("Retrying injection (attempt " + (creatingObject.failedCount + 1) + ") - User '" + ((creatingObject.owner == Obj.Owner.Network) ? creatingObject.ownerNuid.ToString() : "Me") + "' - Sub '" + creatingObject.ModelTitle + "'");
                    }
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
                        // check for aircraft
                        if (creatingObject is Aircraft)
                        {
                            // aircraft
                            Aircraft aircraft = creatingObject as Aircraft;
                            // show event
                            main.MonitorEvent("Injecting aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - Model '" + creatingObject.ownerModel + "' - Sub '" + creatingObject.ModelTitle + "'");
                        }
                        else
                        {
                            // show event
                            main.MonitorEvent("Injecting object - User '" + ((creatingObject.owner == Obj.Owner.Network) ? creatingObject.ownerNuid.ToString() : "Me") + "' - Model '" + creatingObject.ownerModel + "' - Sub '" + creatingObject.ModelTitle + "'");
                        }
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
                        // check for aircraft
                        if (creatingObject is Aircraft)
                        {
                            // aircraft
                            Aircraft aircraft = creatingObject as Aircraft;
                            // show event
                            main.MonitorEvent("Injecting aircraft '" + aircraft.flightPlan.callsign + "' - User '" + ((aircraft.owner == Obj.Owner.Network) ? aircraft.ownerNuid.ToString() : "Me") + "' - Model '" + creatingObject.ownerModel + "' - Sub '" + creatingObject.ModelTitle + "'");
                        }
                        else
                        {
                            // show event
                            main.MonitorEvent("Injecting object - User '" + ((creatingObject.owner == Obj.Owner.Network) ? creatingObject.ownerNuid.ToString() : "Me") + "' - Model '" + creatingObject.ownerModel + "' - Sub '" + creatingObject.ModelTitle + "'");
                        }

                        simconnect.CreateObject(creatingObject);
                    }
#endif
                    }
            }
#endif // XPLANE || SIMCONNECT
            // get elapsed time
            double time = main.ElapsedTime;

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
                    remoteFlightControl = main.network.shareFlightControls.Valid();
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
                                // create new interval mask
                                IntervalMask intervalMask = new()
                                {
                                    localObject = localObject,
                                    remoteObject = remoteObject
                                };

                                // check for valid position
                                //if (localObject.simValid && remoteObject.netValid)
                                //{
                                //    // get distance
                                //    double distance = Vector.GeodesicDistance(localObject.simPosition.longitude, localObject.simPosition.latitude, remoteObject.netPosition.longitude, remoteObject.netPosition.latitude);
                                //    // check if outside activity circle
                                //    if (distance * 0.00053995680346 > mainForm.network.GetNodeActivityCircle(remoteObject.ownerNuid))
                                //    {
                                //        intervalMask.mask = 0xf;
                                //    }
                                //}

                                // check for remote node
                                if (main.network.localNode.lowBandwidth || main.network.localNode.NodeLowBandwidth(remoteObject.ownerNuid))
                                {
                                    // double the interval
                                    intervalMask.mask <<= 1;
                                    intervalMask.mask += 1;
                                }

                                // add to list
                                intervalMasks.Add(intervalMask);
                            }
                        }
                    }
                }
            }

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
                            userAircraft.variableSet.UpdateInteger(headingVuid, (int)(position.angles.y * 180.0 / Math.PI));
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
                            userAircraft.variableSet.UpdateInteger(headingVuid, (int)(bearing *= 180.0 / Math.PI));
                        }
                    }
                }
            }

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
                        if (main.network.localNode.Connected)
                        {
                            // check for shared cockpit
                            if (obj.owner == Obj.Owner.Me && enteredAircraft != null)
                            {
                                // send variables
                                main.network.SendIntegerVariablesMessage(enteredAircraft.ownerNuid, uint.MaxValue, obj.variableSet.integers, main.network.localNode.GetLocalNuid());
                                main.network.SendFloatVariablesMessage(enteredAircraft.ownerNuid, uint.MaxValue, obj.variableSet.floats, main.network.localNode.GetLocalNuid());
                                main.network.SendString8VariablesMessage(enteredAircraft.ownerNuid, uint.MaxValue, obj.variableSet.string8s, main.network.localNode.GetLocalNuid());
                            }
                            // check if aircraft is being broadcast
                            else if (IsBroadcast(obj) && obj.Injected == false)
                            {
                                // diagnostic - dump float vuids/values actually being broadcast
                                string floatsDump = "";
                                foreach (var kv in obj.variableSet.floats)
                                {
                                    floatsDump += kv.Key + "=" + kv.Value + ", ";
                                }
                                main.MonitorVariables("BROADCAST FLOATS - " + obj.ModelTitle + " - " + floatsDump);

                                // broadcast variables
                                main.network.SendIntegerVariablesMessage(new LocalNode.Nuid(), obj.netId, obj.variableSet.integers, main.network.localNode.GetLocalNuid());
                                main.network.SendFloatVariablesMessage(new LocalNode.Nuid(), obj.netId, obj.variableSet.floats, main.network.localNode.GetLocalNuid());
                                main.network.SendString8VariablesMessage(new LocalNode.Nuid(), obj.netId, obj.variableSet.string8s, main.network.localNode.GetLocalNuid());
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
                        main.network.SendFlightPlanMessage(main.network.localNode.GetLocalNuid(), obj.netId, aircraft.flightPlan);
                    }
                }
            }

#if XPLANE || CONSOLE
            // process xplane
            xplane.DoWork();
#endif

            // increment count
            workCount++;

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

#endregion
    }
}
