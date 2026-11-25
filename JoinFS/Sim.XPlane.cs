#if XPLANE || CONSOLE
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
        /// XPlane model notify
        /// </summary>
        /// <param name="model"></param>
        void XPlaneModelUpdate(uint simId, bool user, bool plane, string callsign, string model, string icaoType, string tailNumber)
        {
            // trim callsign
            callsign = callsign.TrimStart(' ', '\t').TrimEnd(' ', '\t');
            // the user's aircraft: the flight ID (airline + flight number, or the registration) and its tail number
            ResolvedAircraftIdentity identity = null;
            UserAircraftInfo raw = null;
            if (user)
            {
                identity = ResolvedAircraftIdentity.FromXPlane(icaoType, callsign, tailNumber);
                raw = new UserAircraftInfo(icaoType, model, tailNumber ?? "", callsign, "", "", "", true, "", 0, 0);
                callsign = identity.Callsign;
            }
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
                AddObjectToList(obj);

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
                    if (identity != null)
                    {
                        ResolvedAircraftIdentity before = userAircraftTracker.Current;
                        if (userAircraftTracker.Check(raw, _ => identity, out _))
                        {
                            // listed again with another identity (the aircraft changed from plane to helicopter)
                            RefreshUserFlightPlanFromSim(aircraft, before, identity);
                        }
                        else if (before == null && CheckUserAircraftFirstSeen(aircraft, identity))
                        {
                            // a plan for another type was there before the aircraft: reset from the sim
                        }
                        else if (aircraft.flightPlan.callsignSetByUser == false)
                        {
                            identity.ApplyTo(aircraft.flightPlan);
                        }
                    }
                    else if (aircraft.flightPlan.callsignSetByUser == false)
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
                    if (identity != null)
                    {
                        // the user's aircraft: a new type, callsign or registration updates the flight plan and sends it
                        aircraft.user = user;
                        aircraft.originalCallsign = identity.Callsign;
                        ResolvedAircraftIdentity before = userAircraftTracker.Current;
                        if (userAircraftTracker.Check(raw, _ => identity, out _))
                        {
                            RefreshUserFlightPlanFromSim(aircraft, before, identity);
                        }
                        return;
                    }
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

        /// <summary>
        /// XPlane connected notify
        /// </summary>
        void XPlaneConnected(short version)
        {
            main.MonitorEvent("Connected to simulator");
            main.MonitorEvent("X-Plane " + version);

            // set simulator information - simulatorName is not set here: GetSimulatorName() ignores
            // the field for XPLANE/CONSOLE builds and always returns a literal "X-Plane" instead
            simulatorVersion = version.ToString();

            // load models for this version
            main.ScheduleSubstitutionLoad();
            main.ScheduleHeightAdjustmentLoad();

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
    }
}
#endif
