#if SIMCONNECT
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
        /// Resolve the junk-stripped raw type/model and the confidence-hierarchy-resolved ICAO type/
        /// airline/classCode/WTC for an OBJECT_INFO response. Shared by both "a new SimConnect object
        /// appeared" and "the user's existing own aircraft object was re-reported" handling in
        /// ProcessSimObjectData, so a genuinely new object and an in-place aircraft/callsign change on
        /// an existing one resolve identically.
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

        readonly UserAircraftTracker userAircraftTracker = new();

        /// <summary>
        /// The ATC flight number of the user's aircraft, with an IATA-style prefix mapped to ICAO
        /// (CallsignRules.NormalizeIataPrefix). Logs when a mapping actually happened.
        /// </summary>
        string UserFlightNumber(ObjectGetInfo info, string configuredAirline, int typerole)
        {
            string flightNumber = info.flightNumber.TrimStart(' ', '\t').TrimEnd(' ', '\t');
#if FS2024
            string livery = info.livery;
#else
            string livery = "";
#endif
            IataResolution result = CallsignRules.NormalizeIataPrefix(flightNumber, configuredAirline, typerole == Substitution.TypeRole_Airliner, livery, AirlineDirectory.Bundled);
            if (result.Callsign != flightNumber)
            {
                main.MonitorEvent("Flight number '" + flightNumber + "' read as '" + result.Callsign + "' (" + result.Reason + ")");
            }
            return result.Callsign;
        }

        /// <summary>The callsign, airline, type and registration the user's aircraft is broadcast under, from the raw sim data.</summary>
        ResolvedAircraftIdentity ResolveUserAircraft(ObjectGetInfo info, int typerole)
        {
            ResolveObjectInfoType(info, out string type, out _, out string learnIcaoType, out _, out _, out _, out string configuredAirline);
            string tailNumber = info.callsign.TrimStart(' ', '\t').TrimEnd(' ', '\t');
            string flightNumber = UserFlightNumber(info, configuredAirline, typerole);
            string callsign = CallsignRules.Resolve(configuredAirline, flightNumber, tailNumber);
            string icaoAirline = configuredAirline.Length > 0 ? configuredAirline : CallsignRules.DeriveIcaoAirline(callsign, AirlineDirectory.Bundled);
            string icaoType = learnIcaoType.Length > 0 ? learnIcaoType : type;
            return new ResolvedAircraftIdentity(callsign, icaoType, icaoAirline, tailNumber, flightNumber);
        }

        /// <summary>
        /// Detects a change of the user's aircraft from the raw SimConnect data. Called on every OBJECT_INFO for the
        /// user's aircraft; resolution (and any config-file read) happens only when the raw data changed.
        /// </summary>
        void CheckUserAircraftChanged(Aircraft aircraft, ObjectGetInfo info)
        {
#if FS2024
            string airline = info.airline, livery = info.livery, liveryFolder = info.liveryFolder;
#else
            string airline = "", livery = "", liveryFolder = "";
#endif
            var raw = new UserAircraftInfo(info.type, info.model, info.callsign, info.flightNumber, airline, livery, liveryFolder,
                info.isUser != 0, info.category, info.engineType, info.numEngines);
            if (userAircraftTracker.Check(raw, _ => ResolveUserAircraft(info, aircraft.typerole), out ResolvedAircraftIdentity identity))
            {
                RefreshUserFlightPlanFromSim(aircraft, identity);
            }
        }

        /// <summary>
        /// Applies a changed identity to the user's aircraft: the flight plan, then a re-match of the livery, which reads
        /// the airline and registration. The next position message carries the new identity to other clients.
        /// A detected change is a new flight, so the callsign returns to auto-tracking and SimBrief is re-fetched if enabled.
        /// </summary>
        void RefreshUserFlightPlanFromSim(Aircraft aircraft, ResolvedAircraftIdentity identity)
        {
            identity.ApplyTo(aircraft.flightPlan);
            UpdateObject(aircraft, aircraft.ownerModel, aircraft.ownerLivery, identity.IcaoType, identity.IcaoAirline,
                aircraft.ownerClassCode, aircraft.ownerWtc, aircraft.ownerClassCodeConfirmed, aircraft.typerole);
#if !CONSOLE
            bool autoImport = Settings.Default.SimBriefAutoImport && string.IsNullOrWhiteSpace(Settings.Default.SimBriefUsername) == false;
#else
            bool autoImport = false;
#endif
            main.MonitorEvent("Own aircraft changed - refreshed callsign '" + identity.Callsign + "', airline '" + identity.IcaoAirline + "', type '" + identity.IcaoType + "' from the sim" + (autoImport ? ", re-fetching SimBrief" : ""));
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
                                    AddObjectToList(obj);
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
                                        string flightNumber = obj.owner == Obj.Owner.Me
                                            ? UserFlightNumber(info, resolvedIcaoAirline, aircraft.typerole)
                                            : info.flightNumber.TrimStart(' ', '\t').TrimEnd(' ', '\t');
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
                                            aircraft.flightPlan.callsign = CallsignRules.Resolve(resolvedIcaoAirline, flightNumber, tailNumber);
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
                                        if (obj.owner == Obj.Owner.Me)
                                        {
                                            CheckUserAircraftChanged(aircraft, info);
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

                                // the user's own aircraft can be re-reported under the same object ID (e.g. a
                                // livery or registration swap that keeps the ID); CheckUserAircraftChanged
                                // resolves only when the raw data differs from the last poll.
                                if (obj.owner == Obj.Owner.Me && obj is Aircraft aircraft)
                                {
                                    CheckUserAircraftChanged(aircraft, (ObjectGetInfo)data);
                                }
                            }
                        }
                        break;

                    case Requests.OBJECT_POSITION_VELOCITY:
                        {
                            ObjectPositionVelocity positionVelocity = (ObjectPositionVelocity)data;
                            ProcessObjectPositionVelocity(objectId, ref positionVelocity);
                        }
                        break;

                    case Requests.OBJECT_POSITION:
                        {
                            ObjectPosition objPosition = (ObjectPosition)data;
                            ProcessObjectPosition(objectId, ref objPosition);
                        }
                        break;

                    default:
                        if (requestId >= (uint)PositionPollRequestIdBase)
                        {
                            // per-object position feed or poll (see PositionPollRequestIdBase) - the data's
                            // type says which definition it was requested with
                            switch (data)
                            {
                                case AircraftPosition aircraftPosition:
                                    ProcessAircraftPosition(objectId, main.ElapsedTime, ref aircraftPosition);
                                    break;
                                case ObjectPositionVelocity positionVelocity:
                                    ProcessObjectPositionVelocity(objectId, ref positionVelocity);
                                    break;
                                case ObjectPosition objPosition:
                                    ProcessObjectPosition(objectId, ref objPosition);
                                    break;
                            }
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

        /// <summary>
        /// A non-aircraft object's position and velocity (our own or one we broadcast)
        /// </summary>
        void ProcessObjectPositionVelocity(uint objectId, ref ObjectPositionVelocity positionVelocity)
        {
            // get object
            Obj obj = objectList.Find(o => o.simId == objectId);
            if (obj == null)
            {
                return;
            }

            // update position
            obj.simPosition = new Pos(ref positionVelocity);
            // store current time
            obj.simTime = main.ElapsedTime;
            // positions may arrive every frame; the network and the recorder get them at the usual rate
            bool sendDue = SendDue(obj, obj.simTime);

            // check if user or broadcasting this aircraft
            if (obj.owner == Obj.Owner.Me || main.network.Connected && IsBroadcast(obj))
            {
                // check if not under remote control
                if (obj.remoteFlightControl == false)
                {
                    // update velocity
                    obj.netVelocity = new Vel(ref positionVelocity);
                }

                // check if broadcasting
                if (sendDue && main.network.Connected)
                {
                    try
                    {
                        if (IsBroadcast(obj))
                        {
                            // get nodes (cached once per tick - see RefreshTickCaches)
                            NodeId[] nodeList = tickPeerIds;
                            // the nodes due an update this tick
                            Span<NodeId> due = stackalloc NodeId[nodeList.Length];
                            int dueCount = 0;
                            // for each node
                            foreach (var nuid in nodeList)
                            {
                                // get remote object (cached once per tick - see RefreshTickCaches)
                                Obj remoteObject = tickUserAircraftByNode.GetValueOrDefault(nuid);
                                // get interval mask
                                int intervalMask = GetIntervalMask(obj, remoteObject);
                                // check if node's simulator is not connected
                                if (main.network.Peers.GetNodeSimulatorConnected(nuid) == false)
                                {
                                    // increase interval (every 32)
                                    intervalMask = 0x1f;
                                }

                                // check send interval
                                if ((obj.positionCount & intervalMask) == 0)
                                {
                                    due[dueCount++] = nuid;
                                }
                            }
                            main.network.SimSender.SendObjectPosition(obj, ref positionVelocity, due[..dueCount]);
                        }
                        // increment count
                        obj.positionCount++;
                    }
                    catch (Exception ex)
                    {
                        main.MonitorError(ex, "Failed to write position/velocity message");
                    }
                }
            }

            // check if recording
            if (sendDue && main.recorder.recording && obj.record && obj.Injected == false)
            {
                // record position and velocity
                main.recorder.Record(obj.recorderObj, main.ElapsedTime, ref positionVelocity);
            }
    }

        /// <summary>
        /// An object's position (injected, or not ours to broadcast)
        /// </summary>
        void ProcessObjectPosition(uint objectId, ref ObjectPosition objPosition)
        {
            // get object
            Obj obj = objectList.Find(o => o.simId == objectId);
            if (obj != null)
            {
                // check if user object is no longer entered
                if (obj.owner != Obj.Owner.Me || enteredAircraft != null)
                {
                    // update position
                    obj.simPosition = new Pos(ref objPosition);
                    // store current time
                    obj.simTime = main.ElapsedTime;
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
                            if (main.network.Connected)
                            {
                                main.network.SimSender.BroadcastWeather(metar);
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
                        // the simulator removed it, and its feed with it
                        ForgetPositionFeed(obj);
                        // remove object
                        RemoveObjectFromList(obj);
                    }
                    break;
            }
        }

        public void ProcessEventFrame(uint eventId)
        {
            switch ((Event)eventId)
            {
                case Event.FRAME:
                    // increment update counter
                    frameCount++;
                    // steer once the dispatch has finished (ProcessFrame), so several FRAME events
                    // queued during a stall produce one pass, using the freshest sim positions
                    frameDue = true;
                    break;
            }
        }

        public void ProcessEvent(uint eventId, uint data)
        {
            // get event ID
            Event e = (Event)eventId;

            // check for sim start/stop - re-arm any failed injections so traffic appears once the
            // user is actually in a flight, without needing a [Sim] toggle (see RearmFailedInjections)
            if (e == Event.SIM_START || e == Event.SIM_STOP)
            {
                bool simRunning = e == Event.SIM_START;
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
                    if (main.network.Connected)
                    {
                        try
                        {
                            // check if entered another aircraft
                            if (aircraft.owner == Obj.Owner.Me && enteredAircraft != null)
                            {
                                // to the owner of the entered aircraft
                                main.network.SimSender.SendEvent(aircraft.netId, eventId, data, [enteredAircraft.ownerNuid]);
                            }
                            // check if aircraft is being broadcast
                            else if (IsBroadcast(aircraft) && aircraft.Injected == false)
                            {
                                main.network.SimSender.BroadcastEvent(aircraft.netId, eventId, data);
                            }
                        }
                        catch (Exception ex)
                        {
                            main.MonitorError(ex, "Failed to write sim event message");
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

            // a fresh SimConnect OPEN means we're (re)connected - clear any latched injection
            // failures from a previous session/attempt so traffic doesn't wait on a [Sim] toggle
            RearmFailedInjections("ProcessOpen");

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
        public volatile bool requestModelListInProgress = false;
        public volatile bool requestModelListIsVerbose = false;
        /// <summary>
        /// Enumeration requests (aircraft/helicopter/balloon) still awaiting completion
        /// </summary>
        readonly HashSet<Requests> pendingModelListRequests = [];
        public void ProcessModelList(SIMCONNECT_RECV_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST data)
        {
            // copy the entries out of the message. Submitting them reads each model's aircraft.cfg
            // from disk, so it runs in Substitution's background queue, never on the sim thread
            Substitution substitution = main.substitution;
            List<(string title, string livery)> entries = new((int)data.dwArraySize);
            for (int i = 0; i < data.dwArraySize; ++i)
	        {
		        SIMCONNECT_ENUMERATE_SIMOBJECT_LIVERY element = (SIMCONNECT_ENUMERATE_SIMOBJECT_LIVERY) data.rgData[i];
                if (element.AircraftTitle.Contains("PassiveAircraft") == false)
                {
                    // We're using in MSFS2024 the variation as livery.
                    // This is not totally correct in MSFS2024, since variation
                    // is "Passengers" or "Cargo" and not the livery.
                    // In MSFS2024 the variation is embedded in the model name.
                    entries.Add((element.AircraftTitle, element.LiveryName));
                }
            }
            if (substitution != null && entries.Count > 0)
            {
                substitution.RunInBackground(() =>
                {
                    foreach (var (title, livery) in entries)
                    {
                        substitution.SubmitModel(title, "", title, livery, 0, "MSFS2024");
                    }
                });
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

                bool verbose = requestModelListIsVerbose;
                requestModelListIsVerbose = false;

                // after every submission queued before it
                void Finish()
                {
                    main.MonitorEvent("All models from the simulator ingested.");
                    requestModelListInProgress = false;

                    if (verbose)
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
                    }
                    // rebuild ICAO indexes now that all three enumeration requests have populated models[]
                    main.substitution?.MakeIcaoIndex();
                    // at the very end
                    main.EnqueueCommand(() =>
                    {
                        main.substitution?.Match();
                    });
                }
                if (substitution != null)
                {
                    substitution.RunInBackground(Finish);
                }
                else
                {
                    requestModelListInProgress = false;
                }
            }
        }
#endif

        public void ProcessQuit()
        {
            // close once the dispatch has finished
            ScheduleClose();
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

                        // failed - but not permanently. The most common cause is the simulator still
                        // sitting on the menu / loading a flight when the attempt was made; record the
                        // time and count so the injection finder can re-arm this after a backoff.
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
    }
}
#endif
