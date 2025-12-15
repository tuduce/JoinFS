using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using JoinFS.Properties;
// using System.Runtime.ExceptionServices;
using System.Linq;
using JoinFS.DataModel;
using static JoinFS.Substitution;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;




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
    public class Sim
    {
#region Constants

#if DEBUG
        const float OBJECT_EXPIRE_TIME = 30.0f;
#else
        const float OBJECT_EXPIRE_TIME = 10.0f;
#endif
        const float NEW_OBJECT_EXPIRE_TIME = 60.0f;

        public const double TIME_ERROR_RATE = 0.02;
        public const double FEET_PER_METRE = 3.28084;
        public const double METRES_PER_FOOT = 0.3048;

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
            GET_MODELS_AND_LIVERIES,
        };

        /// <summary>
        /// SimConnect events
        /// </summary>
        public enum Event
        {
            OBJECT_ADDED,
            OBJECT_REMOVED,
            FRAME,
            PAUSE,
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
            string eventStr;
            switch (simEvent)
            {
                case Sim.Event.OBJECT_ADDED: eventStr = "OBJECT_ADDED"; break;
                case Sim.Event.OBJECT_REMOVED: eventStr = "OBJECT_REMOVED"; break;
                case Sim.Event.FRAME: eventStr = "FRAME"; break;
                case Sim.Event.PAUSE: eventStr = "PAUSE"; break;
                case Sim.Event.RUDDER_SET: eventStr = "RUDDER_SET"; break;
                case Sim.Event.ELEVATOR_SET: eventStr = "ELEVATOR_SET"; break;
                case Sim.Event.AILERON_SET: eventStr = "AILERON_SET"; break;
                case Sim.Event.SMOKE_ON: eventStr = "SMOKE_ON"; break;
                case Sim.Event.SMOKE_OFF: eventStr = "SMOKE_OFF"; break;
                case Sim.Event.EVENT_00011000: eventStr = "EVENT_00011000"; break;
                case Sim.Event.EVENT_00011001: eventStr = "EVENT_00011001"; break;
                case Sim.Event.EVENT_00011002: eventStr = "EVENT_00011002"; break;
                case Sim.Event.EVENT_00011003: eventStr = "EVENT_00011003"; break;
                case Sim.Event.EVENT_00011004: eventStr = "EVENT_00011004"; break;
                case Sim.Event.EVENT_00011005: eventStr = "EVENT_00011005"; break;
                case Sim.Event.EVENT_00011006: eventStr = "EVENT_00011006"; break;
                case Sim.Event.EVENT_00011007: eventStr = "EVENT_00011007"; break;
                case Sim.Event.EVENT_00011008: eventStr = "EVENT_00011008"; break;
                case Sim.Event.EVENT_00011009: eventStr = "EVENT_00011009"; break;
                case Sim.Event.EVENT_0001100A: eventStr = "EVENT_0001100A"; break;
                default: eventStr = "UNKNOWN"; break;
            }

            return eventStr;
        }

        /// <summary>
        /// Convert a definition to a string
        /// </summary>
        /// <param name="simEvent"></param>
        /// <returns></returns>
        public static string DefinitionToString(Sim.Definitions simDefinition)
        {
            string definitionStr;
            switch (simDefinition)
            {
                case Sim.Definitions.OBJECT_GET_INFO: definitionStr = "OBJECT_GET_INFO"; break;
                case Sim.Definitions.OBJECT_POSITION_VELOCITY: definitionStr = "OBJECT_POSITION_VELOCITY"; break;
                case Sim.Definitions.OBJECT_POSITION: definitionStr = "OBJECT_POSITION"; break;
                case Sim.Definitions.OBJECT_VELOCITY: definitionStr = "OBJECT_VELOCITY"; break;
                case Sim.Definitions.OBJECT_EULER: definitionStr = "OBJECT_EULER"; break;
                case Sim.Definitions.AIRCRAFT_POSITION: definitionStr = "AIRCRAFT_POSITION"; break;
                case Sim.Definitions.AIRCRAFT_GET_INFO: definitionStr = "AIRCRAFT_GET_INFO"; break;
                case Sim.Definitions.AIRCRAFT_SET_ID: definitionStr = "AIRCRAFT_SET_ID"; break;
                case Sim.Definitions.AIRCRAFT_WAYPOINTS: definitionStr = "AIRCRAFT_WAYPOINTS"; break;
                default: definitionStr = "UNKNOWN"; break;
            }

            return definitionStr;
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
#if FS2024
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String livery;
#endif
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
        public struct ObjectPosition
        {
            public double latitude;
            public double longitude;
            public double altitude;
            public float pitch;
            public float bank;
            public float heading;
            public float height;
            public int ground;

            public ObjectPosition(Pos pos)
            {
                latitude = pos.geo.z;
                longitude = pos.geo.x;
                altitude = pos.geo.y;
                pitch = (float)pos.angles.x;
                bank = (float)pos.angles.z;
                heading = (float)pos.angles.y;
                height = (float)pos.elevation;
                ground = pos.ground;
            }
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
        public struct ObjectVelocity
        {
            public float velocityX;
            public float velocityY;
            public float velocityZ;
            public float angularVelocityX;
            public float angularVelocityY;
            public float angularVelocityZ;
            public float accelerationX;
            public float accelerationY;
            public float accelerationZ;

            public ObjectVelocity(Vector linear, Vector angular, Vector acc)
            {
                velocityX = (float)linear.x;
                velocityY = (float)linear.y;
                velocityZ = (float)linear.z;
                angularVelocityX = (float)angular.x;
                angularVelocityY = (float)angular.y;
                angularVelocityZ = (float)angular.z;
                accelerationX = (float)acc.x;
                accelerationY = (float)acc.y;
                accelerationZ = (float)acc.z;
            }
        };

        /// <summary>
        /// Object orientation in simConnect
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ObjectEuler
        {
            public float pitch;
            public float heading;
            public float bank;

            public ObjectEuler(Vector angles)
            {
                pitch = (float)angles.x;
                heading = (float)angles.y;
                bank = (float)angles.z;
            }
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
            public int ground;
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
            public Vel Clone() => new Vel(linear.Clone(), angular.Clone(), acc.Clone());

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
            public int ground;

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos()
            {
                this.geo = new Vector();
                this.angles = new Vector();
                this.elevation = 0.0;
                this.ground = 0;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(Vector geo, Vector angles, double elevation, int ground)
            {
                this.geo = geo;
                this.angles = angles;
                this.elevation = elevation;
                this.ground = ground;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref ObjectPosition position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.height;
                this.ground = position.ground;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref AircraftPosition position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.elevation;
                this.ground = position.ground;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public Pos(ref ObjectPositionVelocity position)
            {
                this.geo = new Vector(position.longitude, position.altitude, position.latitude);
                this.angles = new Vector(position.pitch, position.heading, position.bank);
                this.elevation = position.height;
                this.ground = position.ground;
            }

            /// <summary>
            /// Clone
            /// </summary>
            public Pos Clone() => new Pos(geo.Clone(), angles.Clone(), elevation, ground);

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
                Vector scalar = new Vector(1.0 / xRate * Vector.GEODESIC_EPSILON, 1.0, 1.0 / zRate * Vector.GEODESIC_EPSILON);
                return new Pos(geo + (velocity.linear * time + velocity.acc * (time * time)) * scalar, angles + velocity.angular * time, elevation, ground);
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
            if (simconnect != null)
            {
                // register variable
                simconnect.RegisterIntegerVariable(definition);
            }
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
            if (simconnect != null)
            {
                // register variable
                simconnect.RegisterFloatVariable(definition);
            }
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
            if (simconnect != null)
            {
                // register variable
                simconnect.RegisterString8Variable(definition);
            }
#endif
        }

        /// <summary>
        /// Register a variable event
        /// </summary>
        public void RegisterVariableEvent(VariableMgr.Definition definition)
        {
#if SIMCONNECT
            // check for simconnect
            if (simconnect != null)
            {
                // register event
                simconnect.RegisterVariableEvent(definition);
            }
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
            public string ownerModel = "";
#if FS2024
            public string ownerLivery = "";
#endif
            public Substitution.Model subModel = null;
            public Substitution.Type subType = Substitution.Type.Original;
            public int typerole = Substitution.TypeRole_SingleProp;
            public VariableMgr.Set variableSet = null;
            public double variableStartTime;
            public bool failed = false;
            public double expireTime = 0.0;
            public bool broadcast = false;
            public double netStateTime = 0.0;
            public double netRealTime = 0.0;
            public double netSimTime = 0.0;
            public double simTime = 0.0;
            public bool NetValid { get { return netStateTime > 0.0; } }
            public bool SimValid { get { return simTime > 0.0; } }
            public Pos simPosition = new Pos();
            public Pos netPosition = new Pos();
            public Vel netVelocity = new Vel();
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

#if FS2024
            public string ModelLivery { get { return subModel != null ? subModel.variation : ownerLivery; } }
#endif

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
                this.broadcast = ownerNuid.Invalid() ? true : false;
                // initialise record flag
                this.record = ownerNuid.Invalid() ? false : true;
            }
        }

        /// <summary>
        /// List of objects
        /// </summary>
        public List<Obj> objectList = new List<Obj>();

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
                if (simconnect != null)
                {
                    // remove from simconnect
                    simconnect.RemoveObject(obj.simId, Requests.REMOVE_OBJECT);
                }
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
                // create variables
                CreateModelVariables(obj);
            }

            // update match
#if FS2024
            UpdateObject(obj, obj.ownerModel, obj.ownerLivery, obj.typerole);
#else
            UpdateObject(obj, obj.ownerModel, obj.typerole);
#endif
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
                // add object to remove list
                removeList.Add(obj);
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
            if (scheduleRemove == null)
            {
                // set schedule
                scheduleRemove = model;
            }
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
        List<Obj> removeList = new List<Obj>();

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
        public void ResetObject(Obj obj)
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
#if FS2024
        public void UpdateObject(Obj obj, string model, string livery, int typerole)
#else
        public void UpdateObject(Obj obj, string model, int typerole)
#endif
        {
            obj.typerole = typerole;
            // update model
            obj.ownerModel = model;
#if FS2024
            obj.ownerLivery = livery;
            // model match
            main.substitution?.Match(obj.ownerModel, obj.ownerLivery, obj.typerole, out obj.subModel, out obj.subType);
#else
            // model match
            main.substitution ?. Match(obj.ownerModel, obj.typerole, out obj.subModel, out obj.subType);
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
                // update object position and velocity
                simconnect.SetData(Definitions.OBJECT_POSITION_UPDATE, obj.simId, new ObjectPositionUpdate(ref position));
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
                ObjectPosition objectPosition = new ObjectPosition(position);
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
                ObjectVelocity objectVelocity = new ObjectVelocity(velocity.linear.InvRotate(position.angles), velocity.angular, velocity.acc.InvRotate(position.angles));
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
#if FS2024        
        public Obj UpdateObject(LocalNode.Nuid ownerNuid, uint netId, string model, string livery, int typerole, double netTime, ref ObjectPositionVelocity positionVelocity)
#else
        public Obj UpdateObject(LocalNode.Nuid ownerNuid, uint netId, string model, int typerole, double netTime, ref ObjectPositionVelocity positionVelocity)
#endif
        {
            // get object
            Obj obj = objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId);
            if (obj == null)
            {
                // create new object in list
                obj = new Obj(ownerNuid, netId);
                // set expire time
                obj.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;
                // model
#if FS2024
                UpdateObject(obj, model, livery, typerole);
#else
                UpdateObject(obj, model, typerole);
#endif
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
            if (objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is Obj) is Obj obj)
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
            if (objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId && o is Obj) is Obj obj)
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
                        // FS2020 has an issue where the aircraft remains glued to the ground, so reset much earlier when the altitude diverts on the ground
                        if (simPosition.ground != 0) altitudeDeltaLimit = 0.2;
#endif

                        // check if object is beyond specific distance
                        if (distance > 50.0 || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit)
                        {
                            // reset to target position
                            UpdateObject(obj, netPosition);
                            // update sim velocity
                            simconnect.SetData(Definitions.OBJECT_VELOCITY, obj.simId, new ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc));
#if (FS2020 || FS2024)
                            // set orientation
                            simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                            obj.simPosition.angles = netPosition.angles.Clone();
#endif
                        }
                        else
                        {
                            // get world space relative position
                            Vector deltaGeo = new Vector(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
                            // get delta between current and network orientations
                            Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

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
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                                obj.simPosition.angles = netPosition.angles.Clone();
#endif
                            }
                            else
                            {
                                // set orientation
                                simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
                                obj.simPosition.angles = netPosition.angles.Clone();
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
                if (model.Length >= 4 && model.Substring(0, 4).Equals("VRS_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // check for "VACMI"
                if (model.Length >= 5 && model.Substring(0, 5).Equals("VACMI", StringComparison.OrdinalIgnoreCase))
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
        /// Flight plan
        /// </summary>
        public class FlightPlan
        {
            public const int MAX_ROUTE = 128;
            public const int MAX_REMARKS = 128;

            public string callsign = "";
            public string icaoType = "";
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
        public FlightPlan userFlightPlan = new FlightPlan();

        /// <summary>
        /// Aircraft
        /// </summary>
        public abstract class Aircraft : Obj
        {
            public bool user = false;
            public string originalCallsign = "";
            public byte flightPlanVersion = 0;
            public FlightPlan flightPlan = new FlightPlan();
            public byte cockpitShare = 0;
            public string airport = "";
            public string metar = "";
            public string wind = "";

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
#if FS2024
            public Aircraft(uint simId, string callsign, string icaoType, string model, string livery, bool isUser)
#else
            public Aircraft(uint simId, string callsign, string icaoType, string model, bool isUser)
#endif
            {
                // set sim ID
                this.simId = simId;
                netId = simId;
                // update info
                originalCallsign = callsign;
                flightPlan.callsign = callsign;
                flightPlan.icaoType = icaoType;
                ownerModel = model;
#if FS2024
                ownerLivery = livery;
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
                // format of wind
                Regex regex = new Regex(@"\d{5}KT");
                // find wind in metar
                Match match = regex.Match(metar);
                // check if found
                if (match.Success)
                {
                    // set wind
                    this.wind = match.Value;
                }
                else
                {
                    // format of wind
                    regex = new Regex(@"\d{5}MPS");
                    // find wind in metar
                    match = regex.Match(metar);
                    // check if found
                    if (match.Success)
                    {
                        // set wind
                        this.wind = match.Value;
                    }
                }
            }

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
            
#if FS2024
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Plane(uint simId, string callsign, string type, string model, string livery, bool isUser) : base(simId, callsign, type, model, livery, isUser) { }
#else
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Plane(uint simId, string callsign, string type, string model, bool isUser) : base(simId, callsign, type, model, isUser) { }
#endif

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

#if FS2024
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Helicopter(uint simId, string callsign, string type, string model, string livery, bool isUser) : base(simId, callsign, type, model, livery, isUser) { }
#else
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="simId">SimConnect ID</param>
            /// <param name="simInfo">SimConnect aircraft</param>
            public Helicopter(uint simId, string callsign, string type, string model, bool isUser) : base(simId, callsign, type, model, isUser) { }
#endif

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
            public Boat(uint simId, string callsign, string type, string model, bool isUser) : base(simId, model) { }

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
            public Vehicle(uint simId, string callsign, string type, string model, bool isUser) : base(simId, model) { }

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
                    // check if correction is enabled and local height is valid
                    if (Settings.Default.ElevationCorrection && aircraft.SimValid)
                    {
                        // calculate height
                        double height = aircraftPosition.altitude - aircraftPosition.elevation;
                        // check if close to the ground
                        if (height < 50.0)
                        {
                            // calculate proportion to adjust by
                            double proportion = 1.0 - height * 0.02;
                            double remoteElevation = aircraftPosition.elevation;
                            double localElevation = aircraft.simPosition.elevation;
                            // blend altitude
                            aircraftPosition.altitude += (localElevation - remoteElevation) * proportion;
                        }
                    }

                    // height adjustment
                    aircraftPosition.altitude += GetHeightAdjustment(aircraft.subModel) * 0.01f;

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
#if FS2024
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, bool user, bool plane, string callsign, string nickname, string model, string livery, int typerole, double netTime, ref AircraftPosition aircraftPosition)
#else
        public Aircraft UpdateAircraft(LocalNode.Nuid ownerNuid, uint netId, bool user, bool plane, string callsign, string nickname, string model, int typerole, double netTime, ref AircraftPosition aircraftPosition)
#endif
        {
            // check for valid aircraft
            if (!(objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId) is Aircraft aircraft))
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
                // model
#if FS2024
                UpdateObject(aircraft, model, livery, typerole);
#else
                UpdateObject(aircraft, model, typerole);
#endif
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

                // check for valid simconnect
                if (Connected && controlledAircraft.Created)
                {
                    // update variable set
                    controlledAircraft.variableSet ?. UpdateFloats(variables);
                }

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

                // check for valid simconnect
                if (Connected && controlledAircraft.Created)
                {
                    // update variable set
                    controlledAircraft.variableSet ?. UpdateString8(variables);
                }

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
            if (followAircraft == null)
            {
                // set scheduled follow
                followAircraft = aircraft;
            }
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
                Pos newPosition = new Pos();
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
        Pos savedPosition = new Pos();
        Vel savedVelocity = new Vel();

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
            if (enterAircraft == null)
            {
                // schedule
                enterAircraft = aircraft;
            }
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
            if (simconnect != null)
            {
                // simconnect event
                simconnect.DoEvent(simId, definition.scEvent, data);
            }
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
            // check if scheduled
            if (scheduleMetar == null)
            {
                // schedule metar change
                scheduleMetar = metar;
            }
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
        Timer objectProcessTimer = new Timer(0.1);
        Timer requestInfoTimer = new Timer(2.0);
        Timer requestPositionTimer = new Timer(0.1);
        Timer requestWeatherTimer = new Timer(60.0);
        Timer requestLocalStateTimer = new Timer(20.0);
        Timer trackingTimer = new Timer(1.0);
        Timer variablesTimer = new Timer(1.0);
        Timer flightPlanTimer = new Timer(5.0);

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
            if (simconnect != null)
            {
                simconnect.RequestDataByType(Requests.OBJECT_INFO, Definitions.OBJECT_GET_INFO, 200000);
            }
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
                                // request full aircraft position
                                simconnect.RequestData(Requests.AIRCRAFT_POSITION, Definitions.AIRCRAFT_POSITION, obj.simId);
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
                                // request full aircraft position
                                simconnect.RequestData(Requests.AIRCRAFT_POSITION, Definitions.AIRCRAFT_POSITION, obj.simId);
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
                if (simconnect != null)
                {
                    // request weather
                    simconnect.WeatherRequest(Requests.WEATHER, aircraft.simPosition.geo.z * (180.0 / Math.PI), aircraft.simPosition.geo.x * (180.0 / Math.PI), aircraft.simPosition.geo.y * FEET_PER_METRE);
                }
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

                    Random rand = new Random((int)DateTime.Now.Ticks);
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

        Timer updateIntervalsTimer = new Timer(5.0);

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
        List<IntervalMask> intervalMasks = new List<IntervalMask>();

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
        /// Current data version
        /// </summary>
#if FS2024
        // TODO: Advance the data version for everybody, not just FS2024
        public const short VERSION = 21004;
#else
        public const short VERSION = 21003;
#endif

        /// <summary>
        /// Method for reading specific data versions
        /// </summary>
        /// <param name="reader"></param>
        public delegate void ReadVersion(short version, BinaryReader reader);

        /// <summary>
        /// Exception for reading data
        /// </summary>
        public class ReadException : Exception
        {
            public ReadException(string message) : base(message) { }
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
            List<short> keys = new List<short>(versions.Keys);
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
            List<short> keys = new List<short>(versions.Keys);
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
        /// Write position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="simPositionVelocity">Position and Velocity</param>
        public static void Write(BinaryWriter writer, ref ObjectPositionVelocity positionVelocity)
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
        static readonly Dictionary<short, ReadVersion<ObjectPositionVelocity>> positionVelocityVersions = new Dictionary<short, ReadVersion<ObjectPositionVelocity>>()
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
            Read<ObjectPositionVelocity>(version, positionVelocityVersions, reader, ref positionVelocity);
        }

        /// <summary>
        /// Write aircraft position/velocity to a stream
        /// </summary>
        /// <param name="writer">Binary writer</param>
        /// <param name="simPositionVelocity">Position and Velocity</param>
        public static void Write(BinaryWriter writer, ref AircraftPosition aircraftPosition)
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
        }

        /// <summary>
        /// Version table for reading position and velocity
        /// </summary>
        static readonly Dictionary<short, ReadVersion<AircraftPosition>> aircraftPositionVersions = new Dictionary<short, ReadVersion<AircraftPosition>>()
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
            Read<AircraftPosition>(version, aircraftPositionVersions, reader, ref aircraftPosition);
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
                    case 0: obj = new Plane(simId, callsign, icaoType, model, user); break;
//                    default: obj = new Obj(msg.simId, msg.model); break;
                }
                // substitution
                main.substitution ?. Masquerade(model, out obj.subModel, out obj.subType);
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
                    // sub callsign
                    aircraft.flightPlan.callsign = main.substitution != null ? main.substitution.Callsign(aircraft.ownerModel, aircraft.originalCallsign) : aircraft.originalCallsign;
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
                    main.substitution ?. Masquerade(model, out obj.subModel, out obj.subType);
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
                        // sub callsign
                        aircraft.flightPlan.callsign = main.substitution != null ? main.substitution.Callsign(aircraft.ownerModel, aircraft.originalCallsign) : aircraft.originalCallsign;
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
                                    // remove any junk from type
                                    string type = info.type;
                                    type = type.Replace("TTATCCOM.AC_MODEL ", "");
                                    type = type.Replace("TTATCCOM.AC_MODEL_", "");
                                    type = type.Replace("TT:ATCCOM.AC_MODEL ", "");
                                    type = type.Replace("TT:ATCCOM.AC_MODEL_", "");
                                    type = type.Replace("$$:", "");
                                    type = type.Replace(".0.text", "");
                                    string model = info.model;
                                    // convert the long hyphen
                                    model = model.Replace("â€“", "–");

                                    // check category
                                    switch (info.category)
                                    {
                                        case "Boat": obj = new Boat(objectId, callsign, type, model, info.isUser != 0); break;
                                        case "GroundVehicle": obj = new Vehicle(objectId, callsign, type, model, info.isUser != 0); break;
                                        case "Airplane":
                                            {
                                                if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                                                {
#if FS2024
                                                    obj = new Plane(objectId, callsign, type, model, info.livery, info.isUser != 0);
#endif
                                                } else
                                                {
#if !FS2024
                                                    obj = new Plane(objectId, callsign, type, model, info.isUser != 0);
#endif
                                                }
                                                break;
                                            }
                                        case "Helicopter":
                                            {
                                                if (main.sim.GetSimulatorName() == "Microsoft Flight Simulator 2024")
                                                {
#if FS2024
                                                    obj = new Helicopter(objectId, callsign, type, model, info.livery, info.isUser != 0);
#endif
                                                } else
                                                {
#if !FS2024
                                                    obj = new Helicopter(objectId, callsign, type, model, info.isUser != 0);
#endif
                                                }
                                                break;
                                            }
                                        default: obj = new Obj(objectId, model); break;
                                    }
                                    // set type role
                                    if (main.substitution != null) obj.typerole = main.substitution.GetTypeRole(obj.ownerModel);
                                    // substitute model
                                    main.substitution ?. Masquerade(obj.ownerModel, out obj.subModel, out obj.subType);
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
                                        // set flight plan
                                        userFlightPlan = userAircraft.flightPlan;
                                    }

                                    // check for aircraft
                                    if (obj is Aircraft)
                                    {
                                        // aircraft
                                        Aircraft aircraft = obj as Aircraft;
                                        // substitute callsign
                                        aircraft.flightPlan.callsign = main.substitution != null ? main.substitution.Callsign(aircraft.ownerModel, callsign) : callsign;
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

                    case Requests.AIRCRAFT_POSITION:
                        {
                            // get sim position
                            AircraftPosition aircraftPosition = (AircraftPosition)data;
                            // process aircraft
                            ProcessAircraftPosition(objectId, main.ElapsedTime, ref aircraftPosition);
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
                        if (requestId < (uint)VariableMgr.ScDefinition.ID0)
                        {
                            main.MonitorEvent("ERROR - Unknown request ID '" + requestId + "'");
                        }
                        break;
                }

                // check for variable
                if (requestId >= (uint)VariableMgr.ScRequest.ID0)
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
                            string metar = metarData.Substring(spaceIndex + 1);

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

        /// <summary>
        /// Get simulator name
        /// </summary>
        /// <returns></returns>
        public string GetSimulatorName()
        {
#if XPLANE || CONSOLE
            return main.settingsXplane ? "X-Plane" : Resources.Strings.NotConnected;
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
                main.MonitorEvent("All models from the simulator ingested.");

                var stopwatchTotal = Stopwatch.StartNew();
                double enrichmentTime = 0, embeddingTime = 0;
                if (main.settingsUseAIFeatures)
                {                     
                    main.MonitorEvent("Enriching models with AI features...");
                    var stopwatchEnrich = Stopwatch.StartNew();
                    main.substitution.enrichModelService.EnrichModelsWithDetailsAsync(main.substitution.models).GetAwaiter().GetResult();
                    stopwatchEnrich.Stop();
                    enrichmentTime = stopwatchEnrich.Elapsed.TotalSeconds;
                    main.MonitorEvent("Generating embeddings for models...");
                    var stopwatchEmbed = Stopwatch.StartNew();
                    _ = main.substitution.embeddingService.GenerateEmbeddingsFromModelsAsync(main.substitution.models);
                    // main.substitution.embeddingService.GenerateEmbeddingsFromModels(main.substitution.models);
                    stopwatchEmbed.Stop();
                    embeddingTime = stopwatchEmbed.Elapsed.TotalSeconds;
                }
                stopwatchTotal.Stop();
                main.MonitorEvent($"AI enrichment took {enrichmentTime:F2} seconds.");
                main.MonitorEvent($"AI embedding took {embeddingTime:F2} seconds.");
                main.MonitorEvent($"AI enrichment and embedding total took {stopwatchTotal.Elapsed.TotalSeconds:F2} seconds.");

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
                // at the very end
                main.ScheduleSubstitutionMatch();
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

                        // failed
                        obj.failed = true;
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
        Dictionary<string, int> heightAdjustments = new Dictionary<string, int>();

        /// <summary>
        /// get the height adjustment for a model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public int GetHeightAdjustment(Substitution.Model model)
        {
            // find model
            if (model != null && heightAdjustments.ContainsKey(model.longType))
            {
                // return adjustment in metres
                return heightAdjustments[model.longType];
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
                    if (heightAdjustments.ContainsKey(model.longType))
                    {
                        // remove adjustment
                        heightAdjustments.Remove(model.longType);
                    }
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
                    StreamWriter writer = new StreamWriter(filename);
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
        uint headingVuid = VariableMgr.CreateVuid("sim/cockpit/autopilot/heading_mag");

        /// <summary>
        /// List of user defined variable sets for different models
        /// </summary>
        public Dictionary<string, List<string>> modelVariables = new Dictionary<string, List<string>>();

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
                modelVariables[parts[0]] = new List<string>();
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
                        string[] separator = { "[+]" };
                        string[] parts = line.Split(separator, StringSplitOptions.None);
                        // check for parts
                        if (parts.Length > 1)
                        {
                            // check that model is not already present
                            if (modelVariables.ContainsKey(parts[0]) == false)
                            {
                                // add new entry
                                modelVariables[parts[0]] = new List<string>();
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
                if (writer != null) writer.Close();
            }
        }

        /// <summary>
        /// Is a model using default variables
        /// </summary>
        bool UsingDefaultVariables(string title)
        {
            // check for model variables
            if (modelVariables.ContainsKey(title))
            {
                // get model files
                List<string> modelFiles = modelVariables[title];
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
            List<string> list = new List<string>();

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
            if (modelVariables.ContainsKey(title))
            {
                // return variable file list
                return modelVariables[title];
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
            List<string> files = new List<string>();
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
        Main main;

#if !CONSOLE
        [DllImport("winmm")]
        static extern int timeBeginPeriod(uint uPeriod);
        [DllImport("winmm")]
        static extern int timeEndPeriod(uint uPeriod);
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
            timeBeginPeriod(1);
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
            timeEndPeriod(1);
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
            return (simCallsign.Length > 10) ? simCallsign.Substring(0, 10) : simCallsign;
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
                AircraftSetId setId = new AircraftSetId
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
                if (int.TryParse(parts[0].Substring(1), NumberStyles.Number, CultureInfo.InvariantCulture, out int r0))
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
        Timer checkConnectionTimer = new Timer(20.0);

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
            // check for simconnect
            if (simconnect != null)
            {
                // process messages
                simconnect.ReceiveMsg();
            }
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
                if (main.airportList.ContainsKey(code))
                {
                    // convert to radians
                    userLatitude = Math.Min(90.0, Math.Max(-90.0, main.airportList[code].latitude)) * (Math.PI / 180.0);
                    userLongitude = Math.Min(180.0, Math.Max(-180.0, main.airportList[code].longitude)) * (Math.PI / 180.0);
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
                // find object that needs creating
                creatingObject = objectList.Find(o => o.owner != Obj.Owner.Me && o.Created == false && o.failed == false && main.log.IgnoreNode(o.ownerNuid) == false && main.log.IgnoreName(o.ownerModel) == false && o != enteredAircraft && o.distance * 0.00053995680346 < activityCircle);

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
                        UpdateObject(creatingObject, creatingObject.ownerModel, creatingObject.typerole);
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
#if FS2024
                        UpdateObject(creatingObject, creatingObject.ownerModel, creatingObject.ownerLivery, creatingObject.typerole);
#else
                        UpdateObject(creatingObject, creatingObject.ownerModel, creatingObject.typerole);
#endif
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
                                IntervalMask intervalMask = new IntervalMask
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
        }

#endregion
    }
}
