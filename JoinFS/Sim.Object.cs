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
using JoinFS.Estimation;




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

            public Owner owner = Owner.Me;
            public NodeId ownerNuid;
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

            /// <summary>
            /// SimConnect position feed (docs/sim-thread-architecture.md, Phase 3): the subscription made
            /// on <see cref="positionRequestId"/> - which data, whether every frame (else every second),
            /// and for which sim ID. Null definition when there is none.
            /// </summary>
            public Definitions? positionFeedDefinition;
            public bool positionFeedEveryFrame;
            public uint positionFeedSimId = uint.MaxValue;

            /// <summary>
            /// Request ID for a one-off poll when the feed goes quiet (polling on the feed's own ID would
            /// replace the subscription), and when the next such poll may be made
            /// </summary>
            public int positionFallbackRequestId = -1;
            public double nextPositionFallbackTime;

            /// <summary>
            /// When this object's position is next due out to the network and the recorder: they stay
            /// at <see cref="PositionSendInterval"/> however often the simulator reports it
            /// </summary>
            public double nextSendTime;
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
            public bool failed = false;
            /// <summary>main.ElapsedTime when failed was last set - see Sim.FAILED_RETRY_MAX / UpdateCreatingObject's retry backoff.</summary>
            public double failedTime = 0.0;
            /// <summary>Number of injection attempts that have failed for this object so far - capped at Sim.FAILED_RETRY_MAX.</summary>
            public int failedCount = 0;
            public double expireTime = 0.0;
            public bool broadcast = false;
            public double netStateTime = 0.0;
            public double simTime = 0.0;
            /// <summary>The simulator's own clock at the last position report (MSFS), NaN when there is none - times the drawn object without the handling jitter</summary>
            public double simulationTime = double.NaN;

            /// <summary>
            /// Following the sender's clock and predicting this object's state from network samples
            /// (docs/position-estimation-plan.md §4). Sim thread only, so snapshot copies get their own.
            /// </summary>
            IClockModel clock;
            IStateEstimator estimator;
            SimClockStamper stamper;
            public IClockModel Clock => clock ??= EstimationRegistry.CreateClock(EstimationRegistry.SelectedClock);
            /// <summary>Times this object's own samples for sending (sim thread only)</summary>
            public SimClockStamper Stamper => stamper ??= new SimClockStamper();
            public IStateEstimator Estimator => estimator ??= EstimationRegistry.CreateEstimator(EstimationRegistry.SelectedEstimator);

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
            /// <summary>TEMPORARY - throttle for the diagnostic RawPos/RawPosRelay traces in UpdateAircraft (both keyed by the sender's netTime), ground-jitter/model-mismatch investigation. Remove this field and its log lines once diagnosed.</summary>
            public double nextRawDiagLogTime = 0.0;
            /// <summary>TEMPORARY - throttle for the diagnostic RawPosSend trace in ProcessAircraftPosition, keyed by local simTime - kept separate from nextRawDiagLogTime because that one is keyed by the unrelated netTime clock; sharing one field let whichever diagnostic ran first starve the other. Remove this field and its log line once diagnosed.</summary>
            public double nextRawPosSendDiagLogTime = 0.0;
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
            /// For a snapshot copy (<see cref="SimSnapshot"/>): the live object it was copied from.
            /// Only the sim thread may touch it, so other threads pass it back in a posted command
            /// (<see cref="Main.PostToSim"/>). Null for live objects.
            /// </summary>
            public Obj Source { get; private set; }

            /// <summary>
            /// Copy for a snapshot. The copy shares nothing the sim thread goes on changing: positions,
            /// flight plan and variable values are copied; models and recorder objects are shared
            /// references, as they are not changed in place by the sim thread.
            /// </summary>
            public virtual Obj CloneView()
            {
                Obj view = (Obj)MemberwiseClone();
                view.Source = this;
                view.clock = null;
                view.estimator = null;
                view.stamper = null;
                view.simPosition = simPosition?.CloneAll();
                view.netPosition = netPosition?.CloneAll();
                view.netVelocity = netVelocity?.Clone();
                view.oldEuler = oldEuler?.Clone();
                view.variableSet = variableSet?.CloneView();
                return view;
            }

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
            public Obj(NodeId ownerNuid, uint netId)
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
            public Boat(NodeId ownerNuid, uint netId) : base(ownerNuid, netId) { }
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
            public Vehicle(NodeId ownerNuid, uint netId) : base(ownerNuid, netId) { }
        }
    }
}
