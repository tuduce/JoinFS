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
        /// Update aircraft position and velocity
        /// </summary>
        /// <param name="aircraft">Aircraft</param>
        /// <param name="netTime">Network time</param>
        /// <param name="positionVelocity">Position and Velocity</param>
        public void UpdateAircraft(Aircraft aircraft, double netTime, AircraftPosition aircraftPosition, double receivedAt = 0.0)
        {
            // set expire time - mirrors the other UpdateAircraft overload (used by the legacy
            // AircraftPosition receive path), which refreshes this unconditionally on every position
            // update. This overload is also the JFP2 Position receive path's "aircraft already
            // exists" branch (Network.cs's HandleJfp2Position) - without this, a network aircraft
            // that negotiated JFP2 for Position never has its expireTime pushed forward again after
            // creation, so the periodic expiry sweep (ProcessObjects) delists it exactly
            // OBJECT_EXPIRE_TIME after it first appeared even though fresh updates keep arriving,
            // then it gets relisted only once packet loss/Identity re-broadcast timing happens to
            // let it be re-created - observed as a repeating Delisting/Listing cycle for JFP2-linked
            // aircraft that legacy-only peers never exhibit.
            aircraft.expireTime = main.ElapsedTime + OBJECT_EXPIRE_TIME;

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
                        bool senderIndicatesElevation = senderHeight * 100.0 >= main.settingsElevatedPlatformThreshold;
                        double releaseThreshold = main.settingsElevatedPlatformThreshold * 0.5;

                        if (nearGround && senderIndicatesElevation && mismatchCm >= main.settingsElevatedPlatformThreshold)
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
                    // TEMPORARY diagnostic (ground-jitter/model-mismatch investigation) - fires every tick
                    // (throttled to ~5/sec, not just on trust-state change) so the raw received values can be
                    // inspected directly, e.g. to see whether aircraftPosition.elevation is flipping between a
                    // real terrain reading and a zeroed/default value. Remove once diagnosed.
                    if (netTime >= aircraft.nextRawDiagLogTime)
                    {
                        aircraft.nextRawDiagLogTime = netTime + 0.2;
                        main.MonitorNetwork("RawPos '" + aircraft.flightPlan.callsign + "' rawGround=" + aircraftPosition.ground +
                            " altitude=" + aircraftPosition.altitude.ToString("F1") + "m elevation=" + aircraftPosition.elevation.ToString("F1") + "m" +
                            " senderStaticCgToGround=" + (aircraftPosition.staticCgToGround * 0.3048).ToString("F2") + "m" +
                            " localElevation=" + aircraft.simPosition.elevation.ToString("F1") + "m" +
                            " localStaticCgToGround=" + (double.IsNaN(aircraft.simPosition.staticCgToGround) ? "n/a" : aircraft.simPosition.staticCgToGround.ToString("F2") + "m") +
                            " netTime=" + netTime.ToString("F1"));
                    }
                    aircraft.trustingPlatformElevation = trustPlatformElevation;
                    aircraft.trustingPlatformGround = trustPlatformGround;

                    // ground the substitute using its own STATIC CG TO GROUND, not the sender's - see
                    // ground-jitter-on-model-mismatch fix. Strictly gated on the sender's own reported
                    // on-ground state: an airborne aircraft must never be pulled toward a ground-relative
                    // correction, regardless of how close to the ground it might numerically appear. When
                    // on-ground, the sender's reported altitude corresponds to their own gear resting on the
                    // real terrain (terrainElevation ~= altitude - senderClearance); re-deriving that same
                    // terrain point but adding back the *substitute's own* clearance places the substitute's
                    // gear on that same terrain point, regardless of whether the substitute is bigger or
                    // smaller than the original aircraft - no matter which model the matcher happened to
                    // pick. This is independent of, and complementary to, the ElevationCorrection bare-
                    // terrain-datum blend below (that one compensates for cross-install terrain-mesh noise,
                    // not model geometry). Requires both sides' clearance to be known (not NaN - an older
                    // peer pre-dating this field, or a not-yet-settled local poll, falls back to no
                    // correction rather than attempting a wrong one) and the local aircraft to have received
                    // at least one live SimConnect update of its own (SimValid).
                    //
                    // The target correction is low-pass filtered (same 0.15 factor/pattern as
                    // smoothedElevationOffset below) rather than applied raw: trustPlatformGround comes
                    // straight from the sender's own single-bit on-ground flag, which can flicker tick-to-
                    // tick (e.g. suspension/contact noise while parked or taxiing) - applying the full
                    // correction the instant it flips would snap the substitute's altitude abruptly between
                    // "as if it were the original aircraft" and "properly grounded" every time that one flag
                    // toggles, which is itself a visible jitter of exactly the size of the geometry gap
                    // between the two aircraft.
                    double targetGroundClearanceCorrection = 0.0;
                    if (trustPlatformGround && aircraft.SimValid)
                    {
                        double senderClearance = aircraftPosition.staticCgToGround * 0.3048;
                        double localClearance = aircraft.simPosition.staticCgToGround;
                        if (double.IsNaN(senderClearance) == false && double.IsNaN(localClearance) == false)
                        {
                            targetGroundClearanceCorrection = localClearance - senderClearance;
                        }
                    }
                    aircraft.smoothedGroundClearanceCorrection = double.IsNaN(aircraft.smoothedGroundClearanceCorrection)
                        ? targetGroundClearanceCorrection
                        : aircraft.smoothedGroundClearanceCorrection + (targetGroundClearanceCorrection - aircraft.smoothedGroundClearanceCorrection) * 0.15;
                    aircraftPosition.altitude += (float)aircraft.smoothedGroundClearanceCorrection;

                    // check if correction is enabled and local height is valid
                    if (Settings.Default.ElevationCorrection && aircraft.SimValid && trustPlatformElevation == false)
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
                            // far from the ground - drop the smoothed offset so a later approach starts fresh
                            // instead of carrying over a stale value from a different location/time.
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
                    UpdateObject(aircraft, netTime, receivedAt);

#if XPLANE || CONSOLE
                    // update simulator
                    xplane.UpdateAircraft(aircraft.simId, aircraft.user, main.network.Peers.GetNodeName(aircraft.ownerNuid), aircraft.flightPlan.callsign, aircraft.subModel, aircraft.flightPlan.icaoType);
                    xplane.UpdateAircraft(aircraft.simId, (float)aircraft.distance, netTime, aircraftPosition);
#endif
                }

                // check for valid simconnect
                if (Connected && aircraft.Created)
                {
#if SIMCONNECT
                    // Only forward raw control-surface axis events to AI/network-model objects, where they're
                    // purely cosmetic (surface animation for other players watching this traffic fly by).
                    // Never send them to userAircraft: while remoteFlightControl is true (share cockpit /
                    // riding along in a recorded or network aircraft), userAircraft's position and orientation
                    // - including heading - are already being authoritatively driven every frame by
                    // UpdateSimObjectVelocity's direct SetData(OBJECT_EULER/OBJECT_POSITION/OBJECT_VELOCITY)
                    // calls. userAircraft is the one SimConnect object with a live, fully-modeled flight
                    // dynamics engine (ground steering, aerodynamic response to rudder, etc.) - AI objects
                    // don't react to control axis events the same way. Feeding it a rudder/elevator/aileron
                    // *event* on top of a hard kinematic override means two independent authorities fight for
                    // the same rotational degree of freedom every tick: the physics engine tries to rotate the
                    // airframe in response to the (possibly stale recorded, or simply network-jittery) control
                    // input, while the kinematic override snaps it straight back to the reported orientation.
                    // In free air the mismatch is absorbed smoothly, but on/near the ground - where wheel
                    // friction and nosewheel-steering-to-rudder coupling react sharply and nonlinearly, and
                    // where UpdateSimObjectVelocity's own ground altitude tolerance is far tighter (frequent
                    // hard resets rather than smooth extrapolation - see altitudeDeltaLimit there) - the two
                    // authorities visibly fight, showing up as violent shaking concentrated on the yaw axis
                    // (rudder/steering being the dominant ground-yaw actuator). How strongly a given aircraft's
                    // ground-handling model reacts to this varies with its flight model config (tailwheel vs.
                    // nosewheel coupling strength, ground friction), which is why this only reproduced on some
                    // aircraft models, and applies equally to live network flight and recording playback, since
                    // both funnel through this same GetControlledObject-redirected update path.
                    if (aircraft != userAircraft)
                    {
#endif
                        // update controls
                        DoSimEvent(aircraft.simId, Event.RUDDER_SET, (uint)-ConvertToAxis(aircraftPosition.rudder));
                        DoSimEvent(aircraft.simId, Event.ELEVATOR_SET, (uint)-ConvertToAxis(aircraftPosition.elevator));
                        DoSimEvent(aircraft.simId, Event.AILERON_SET, (uint)-ConvertToAxis(aircraftPosition.aileron));
#if SIMCONNECT
                    }
#endif
                }
            }
        }

        /// <summary>
        /// Update aircraft position and velocity
        /// </summary>
        /// <param name="ownerGuid">Owner of the aircraft</param>
        /// <param name="netId">Owner's sim ID</param>
        /// <param name="engine">Aircraft engine</param>
        public Aircraft UpdateAircraft(NodeId ownerNuid, uint netId, bool user, bool plane, string callsign, string registration, string nickname, string model, string livery, string icaoType, string icaoAirline, string flightNumber, string classCode, string wtc, bool classCodeConfirmed, int typerole, double netTime, ref AircraftPosition aircraftPosition, double receivedAt = 0.0)
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
                // TEMPORARY diagnostic (ground-jitter/model-mismatch investigation) - this re-broadcasts an
                // already-received position onward to other nodes (multi-hop/relay topology); log it
                // distinctly from RawPosSend so a relay-introduced bad value can be told apart from a
                // freshly-read one. Remove once diagnosed.
                if (netTime >= aircraft.nextRawDiagLogTime)
                {
                    aircraft.nextRawDiagLogTime = netTime + 0.2;
                    main.MonitorNetwork("RawPosRelay '" + aircraft.flightPlan.callsign + "' rawGround=" + aircraftPosition.ground +
                        " altitude=" + aircraftPosition.altitude.ToString("F1") + "m elevation=" + aircraftPosition.elevation.ToString("F1") + "m" +
                        " senderStaticCgToGround=" + (aircraftPosition.staticCgToGround * 0.3048).ToString("F2") + "m" +
                        " netTime=" + netTime.ToString("F1"));
                }
                // send to every node; each gets it in the protocol it negotiated
                main.network.SimSender.SendAircraftPosition(aircraft, ref aircraftPosition, netTime, main.network.PeerIds());
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
                    UpdateAircraft(GetControlledObject(aircraft) as Aircraft, netTime, aircraftPosition, receivedAt);
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
        public Aircraft UpdateAircraft(NodeId ownerNuid, uint netId, uint eventId, uint data, bool flight)
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
                    // send to every node
                    main.network.SimSender.BroadcastEvent(aircraft.netId, eventId, data);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft integer variables
        /// </summary>
        public Aircraft UpdateAircraft(NodeId ownerNuid, uint netId, Dictionary<uint, int> variables)
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
                    main.network.SimSender.BroadcastVariables(aircraft.netId, variables, null, null);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft float variables
        /// </summary>
        public Aircraft UpdateAircraft(NodeId ownerNuid, uint netId, Dictionary<uint, float> variables)
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
                    main.network.SimSender.BroadcastVariables(aircraft.netId, null, variables, null);
                }
            }

            return aircraft;
        }

        /// <summary>
        /// Update aircraft string8 variables
        /// </summary>
        public Aircraft UpdateAircraft(NodeId ownerNuid, uint netId, Dictionary<uint, string> variables)
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
                    main.network.SimSender.BroadcastVariables(aircraft.netId, null, null, variables);
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
                // positions may arrive every frame; the network and the recorder get them at the usual
                // rate. Gated on the current time, not simTime: the X-Plane link re-sends an old sample
                // (with its old time) to keep peers alive when the plugin goes quiet
                bool sendDue = SendDue(aircraft, main.ElapsedTime);

                // TEMPORARY diagnostic (ground-jitter/model-mismatch investigation) - raw SimConnect read for
                // whichever aircraft this is (own aircraft or a locally-simulated one being broadcast), before
                // anything else touches it, to catch whether SimConnect itself intermittently returns a
                // zeroed/default "GROUND ALTITUDE" on this periodic per-object read. Remove once diagnosed.
                if (simTime >= aircraft.nextRawPosSendDiagLogTime)
                {
                    aircraft.nextRawPosSendDiagLogTime = simTime + 0.2;
                    main.MonitorNetwork("RawPosSend '" + aircraft.flightPlan.callsign + "' owner=" + aircraft.owner +
                        " rawGround=" + aircraftPosition.ground + " altitude=" + aircraftPosition.altitude.ToString("F1") + "m" +
                        " elevation=" + aircraftPosition.elevation.ToString("F1") + "m" +
                        " ownStaticCgToGround=" + (aircraftPosition.staticCgToGround * 0.3048).ToString("F2") + "m" +
                        " simTime=" + simTime.ToString("F1"));
                }

                // check if user or broadcasting this aircraft
                if (aircraft.owner == Obj.Owner.Me || main.network.Connected && IsBroadcast(aircraft))
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
                    if (sendDue && main.network.Connected)
                    {
                        try
                        {
                            // check for entered aircraft
                            if (aircraft.owner == Obj.Owner.Me && enteredAircraft != null)
                            {
                                // check that our aircraft is not under remote control
                                if (aircraft.remoteFlightControl == false)
                                {
                                    // send to the owner of the entered aircraft, as the shared-cockpit object
                                    main.network.SimSender.SendAircraftPosition(aircraft, ref aircraftPosition, aircraft.simTime, [enteredAircraft.ownerNuid], sharedCockpit: true);
                                }
                            }
                            else if (IsBroadcast(aircraft) && aircraft.Injected == false)
                            {
                                // get nodes
                                NodeId[] nodeList = main.network.PeerIds();
                                // the nodes due an update this tick
                                Span<NodeId> due = stackalloc NodeId[nodeList.Length];
                                int dueCount = 0;
                                // for each node
                                foreach (var nuid in nodeList)
                                {
                                    // get remote object
                                    Obj remoteObject = objectList.Find(o => o.ownerNuid == nuid && o is Aircraft && (o as Aircraft).user);
                                    // get interval mask
                                    int intervalMask = GetIntervalMask(aircraft, remoteObject);
                                    // check if node's simulator is not connected
                                    if (main.network.Peers.GetNodeSimulatorConnected(nuid) == false)
                                    {
                                        // increase interval (every 32)
                                        intervalMask = 0x1f;
                                    }

                                    // check send interval
                                    if ((aircraft.positionCount & intervalMask) == 0)
                                    {
                                        due[dueCount++] = nuid;
                                    }
                                }
                                // one message; each node gets it in the protocol it negotiated
                                main.network.SimSender.SendAircraftPosition(aircraft, ref aircraftPosition, aircraft.simTime, due[..dueCount]);
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
                if (sendDue && main.recorder.recording && aircraft.record && aircraft.Injected == false)
                {
                    // record position and velocity
                    main.recorder.Record(aircraft.recorderObj, main.ElapsedTime, ref aircraftPosition);
                }
            }
        }
    }
}
