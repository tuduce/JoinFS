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
        /// Register the combined SimConnect read structure for a whole variable file (bundles many
        /// variables into a single data definition, so they can be requested from the simulator with
        /// one RequestDataOnSimObject call instead of one call per variable)
        /// </summary>
        public void RegisterVariableBundle(VariableMgr.Bundle bundle, List<VariableMgr.Definition> fields)
        {
#if SIMCONNECT
            // check for simconnect
            // register bundle
            simconnect?.RegisterVariableBundle(bundle, fields);
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
        public void DoSimEvent(NodeId nodeNuid, uint eventId, uint data)
        {
            // check if aircraft found
            if (objectList.Find(o => o.ownerNuid == nodeNuid && o is Aircraft && (o as Aircraft).user) is Aircraft aircraft)
            {
                // do event
                DoSimEvent(aircraft.simId, (Event)eventId, data);
            }
        }

        /// <summary>
        /// Get the ATC ID for an aircraft
        /// </summary>
        public string MakeAtcId(Aircraft aircraft)
        {
            // get nickname
            string nickname = aircraft.user ? main.network.Peers.GetNodeName(aircraft.ownerNuid) : "";
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
        public void SetAtcId(NodeId ownerNuid)
        {
            // find user aircraft
            if (objectList.Find(o => o.ownerNuid == ownerNuid && o is Aircraft && (o as Aircraft).user) is Aircraft aircraft)
            {
                // Set ID
                SetAtcId(aircraft);
            }
        }
    }
}
