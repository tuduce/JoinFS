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
