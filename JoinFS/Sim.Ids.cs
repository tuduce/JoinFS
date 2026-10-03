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
            GET_MODELS_AIRCRAFT,
            GET_MODELS_HELICOPTER,
            GET_MODELS_BALLOON,
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
            // Append only: these numbers go over the legacy wire, into .jfs recordings and to the
            // X-Plane plugin, so existing members must never be renumbered (see SimEventValuesTests).
            SIM_START,
            SIM_STOP,
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
    }
}
