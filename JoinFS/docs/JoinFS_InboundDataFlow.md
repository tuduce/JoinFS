# JoinFS – Inbound Data Flow: How Remote Aircraft Are Injected Into Your Simulator

This document describes, in precise detail, the complete journey of a remote pilot's aircraft state from the UDP socket to a visible, moving AI aircraft in your flight simulator.  Every layer is covered: reception, dispatch, object lifecycle, position storage, time-tracking, per-frame velocity injection, and the convergence algorithm.  Optimisation opportunities for close formation flying at high speed are called out inline.

---

## 1. Reception – `LocalNode` UDP listener

`LocalNode` opens a UDP socket on port 6112 (default) and listens for incoming datagrams.  The raw receive loop calls **`ReceiveMsg(IPEndPoint endPoint)`** (Node.cs ~line 756) synchronously.

Steps:
1. Checks sender IP against the ban list; silently drops banned addresses.
2. Reads and validates the **node protocol header**:
   - `short 0x520b` – version constant; mismatched packets are discarded.
   - `byte flags` – `FLAG_INTERNAL (0x01)`, `FLAG_GUARANTEED (0x02)`, `FLAG_FORWARD (0x04)`.
   - `ushort guaranteedId`, `byte guaranteedIndex`, `byte guaranteedCount`.
   - 7-byte sender Nuid, 7-byte recipient Nuid.
3. **Forwarding**: if the recipient Nuid does not match the local node and a direct route exists, the datagram is forwarded with `FLAG_FORWARD` set.  This implements store-and-forward routing through hubs.
4. **Guaranteed delivery**: if `FLAG_GUARANTEED` is set, sends an immediate `GuaranteedDone` ACK.  Multi-segment messages are reassembled from `guaranteedInList` before continuing.
5. For **application messages** (FLAG_INTERNAL not set), calls `receiveNotify(endPoint, senderNuid, reader)` → **`Network.ReceiveMsg`**.

---

## 2. Dispatch – `Network.ReceiveMsg`

`Network.ReceiveMsg(IPEndPoint endPoint, LocalNode.Nuid nuid, BinaryReader reader)` (Network.cs ~line 3047) reads two fields first:

```csharp
short dataVersion = reader.ReadInt16();   // must be >= 10014
short messageId   = reader.ReadInt16();   // MESSAGE_ID enum
```

Then dispatches on `messageId`.

### 2a. `MESSAGE_ID.AircraftPosition` (value 1) — the primary position update

```csharp
uint   netId    = reader.ReadUInt32();
bool   user     = reader.ReadBoolean();   // is this the remote pilot's own aircraft
bool   plane    = reader.ReadBoolean();   // plane vs helicopter
string callsign = reader.ReadString();
string model    = reader.ReadString();    // aircraft title (model key)
int    typerole = reader.ReadByte();
byte   flags    = reader.ReadByte();      // bit0=paused
double netTime  = reader.ReadDouble();    // sender's ElapsedTime in seconds
// then Sim.Read(dataVersion, reader, ref aircraftPosition) — see §3
```

**Shared-cockpit path** (`netId == uint.MaxValue`): the packet is a flight-controls update from a pilot sharing your cockpit.  It calls `sim.UpdateAircraft(sim.userAircraft, netTime, aircraftPosition)` which applies the remote inputs directly to your user aircraft.

**Normal path**: calls `sim.UpdateAircraft(nuid, netId, user, plane, callsign, nickname, model, typerole, netTime, ref aircraftPosition)`.

### 2b. `MESSAGE_ID.ObjectPosition` (value 0)

Same structure but without the aircraft-specific `user`, `plane`, `callsign` fields.  Calls `sim.UpdateObject(nuid, netId, model, typerole, netTime, ref positionVelocity)`.

### 2c. Other messages

`IntegerVariables`, `FloatVariables`, `String8Variables` → `sim.UpdateAircraft(nuid, netId, variableDict)` → `variableSet.UpdateIntegers/Floats/String8`.

`SimEvent` → `sim.UpdateAircraft(nuid, netId, eventId, data, true)` → `DoSimEvent(simId, (Event)eventId, data)` → `sc.TransmitClientEvent`.

---

## 3. Deserialisation – `Sim.Read`

`Sim.Read(short dataVersion, BinaryReader reader, ref AircraftPosition aircraftPosition)` (Sim.cs ~line 3147) is version-dispatched via a static dictionary.  For `dataVersion >= 10022` it calls `ReadAircraftPosition1`:

```
double latitude        radians
double longitude       radians
double altitude        metres
float  pitch           radians
float  bank            radians
float  heading         radians
float  velocityX       m/s world X
float  velocityY       m/s world Y
float  velocityZ       m/s world Z
float  angularVelocityX rad/s body X
float  angularVelocityY rad/s body Y
float  angularVelocityZ rad/s body Z
float  accelerationX   m/s² world X
float  accelerationY   m/s² world Y
float  accelerationZ   m/s² world Z
short  rudder          16-bit axis → ConvertFromAxis → float (±1)
short  elevator        16-bit axis
short  aileron         16-bit axis
short  brakeLeft       16-bit axis
short  brakeRight      16-bit axis
float  elevation       ground height in metres (added dataVersion >= 10023)
byte   flags           bit0=onGround, bit1=elevationCorrection
```

Control axes are converted back to float with `ConvertFromAxis(short v) = (float)(int)v / 16384.0f`.

---

## 4. Object Lifecycle – `UpdateAircraft(nuid, netId, …)`

`Sim.UpdateAircraft(nuid, netId, user, plane, callsign, nickname, model, typerole, netTime, ref aircraftPosition)` (Sim.cs ~line 1922) manages the object list:

1. **Lookup**: `objectList.Find(o => o.ownerNuid == nuid && o.netId == netId)`.
2. **First packet for this aircraft**: creates `new Plane(nuid, netId)` or `new Helicopter(nuid, netId)`, sets `owner = Owner.Network`, `remoteFlightControl = true`, `broadcast = false`, calls `UpdateObject(aircraft, model, typerole)` (triggers async model matching/substitution), `CreateModelVariables(aircraft)`, adds to `objectList`.
3. **Relay path**: if this node is a hub or relay and the aircraft is `IsBroadcast`, the position message is re-serialised and broadcast to other nodes.
4. **Model change**: if `model` differs from `aircraft.ownerModel`, the existing SimConnect object is removed and a new one will be created on next cycle.
5. **Normal update**: calls `UpdateAircraft(GetControlledObject(aircraft), netTime, aircraftPosition)`.

`GetControlledObject` redirects to `userAircraft` when the user is in shared-cockpit mode for this remote aircraft.

`aircraft.expireTime = ElapsedTime + OBJECT_EXPIRE_TIME` (10 s in release, 30 s in debug).  If no update arrives within that window the object is removed from both the list and the simulator.

---

## 5. Position and Velocity Storage – `UpdateAircraft(aircraft, netTime, …)`

`Sim.UpdateAircraft(Aircraft aircraft, double netTime, AircraftPosition aircraftPosition)` (Sim.cs ~line 1857):

**Out-of-order rejection**:
```csharp
if (aircraft.NetValid == false || netTime > aircraft.netStateTime)
```
Packets with a timestamp ≤ the last accepted timestamp are silently discarded.  This is the only duplicate/reorder protection.

**Elevation correction** (optional, user-configurable):
```csharp
double height = aircraftPosition.altitude - aircraftPosition.elevation;
if (height < 50.0)
{
	double proportion = 1.0 - height * 0.02;
	aircraftPosition.altitude += (localElevation - remoteElevation) * proportion;
}
```
Blends local and remote ground elevation when the aircraft is within 50 m of the ground, compensating for different terrain meshes between clients.

**Height adjustment per model**: adds `GetHeightAdjustment(aircraft.subModel) * 0.01f` to compensate for model reference point offsets.

**State store**:
```csharp
aircraft.oldEuler         = aircraft.netPosition.angles.Clone();
aircraft.netPosition      = new Pos(ref aircraftPosition);   // lat/lon/alt + angles
aircraft.netVelocity      = new Vel(ref aircraftPosition);   // linear + angular + acc
```

**Calls `UpdateObject(aircraft, netTime)`** → time synchronisation (see §6).

**Control surface injection** (SimConnect path only):
```csharp
DoSimEvent(aircraft.simId, Event.RUDDER_SET,   (uint)-ConvertToAxis(aircraftPosition.rudder));
DoSimEvent(aircraft.simId, Event.ELEVATOR_SET, (uint)-ConvertToAxis(aircraftPosition.elevator));
DoSimEvent(aircraft.simId, Event.AILERON_SET,  (uint)-ConvertToAxis(aircraftPosition.aileron));
```
These use `sc.TransmitClientEvent` with `SIMCONNECT_GROUP_PRIORITY_HIGHEST`.  They are sent on every received position packet, giving control surface update rate equal to the receive rate (~20 Hz without interval throttling).

### Optimisation note – out-of-order handling
The current rejection test only compares timestamps.  UDP reordering within a single flow is rare but possible.  A reorder buffer of depth 2–3 packets would recover the correct sequence for closely-spaced packets without adding perceptible latency.

### Optimisation note – control surface timing
Control axes are injected immediately on receive, not extrapolated.  At 20 Hz the control update lag can be up to 50 ms.  For tight formation work this is generally acceptable, but adding a simple first-order predictor for the axis values (using the last two received values) would reduce the effective control lag to near zero.

---

## 6. Time Synchronisation – `UpdateObject(Obj, double netTime)`

(Sim.cs ~line 1271)

The time-tracking algorithm reconciles the sender's clock (`netTime`, which is the sender's `ElapsedTime` in seconds) with the local clock.

```csharp
obj.netStateTime = netTime;               // last received sender time
if (obj.netRealTime == 0.0)
{
	// First update: immediately place the object
	UpdateObject(obj, obj.netPosition, obj.netVelocity);
	obj.netRealTime = obj.netStateTime;
}
else
{
	// Advance the estimated sender clock by local elapsed time
	obj.netRealTime += main.ElapsedTime - obj.netSimTime;
	// Error between received timestamp and estimated clock
	double error = obj.netStateTime - obj.netRealTime;
	// Blend at 2 % per frame to remove the error smoothly
	obj.netRealTime += error * TIME_ERROR_RATE;   // TIME_ERROR_RATE = 0.02
}
obj.netSimTime = main.ElapsedTime;        // local time at which state arrived
```

`netRealTime` is therefore a continuously running estimate of what the sender's clock currently reads, corrected at 2 % per frame.  It is used during per-frame velocity injection to extrapolate the correct target position.

### Optimisation note – TIME_ERROR_RATE
`TIME_ERROR_RATE = 0.02` means it takes approximately 50 updates (2.5 s at 20 Hz) to fully correct a 50 ms clock offset.  For formation flying with 20–50 ms RTT this works well.  If RTT spikes (e.g. 200 ms), the target position lags noticeably for several seconds.  Increasing `TIME_ERROR_RATE` to 0.05–0.10 for aircraft within 500 m would correct more rapidly at the cost of slightly more jitter in the target position.

---

## 7. Object Creation in the Simulator

When a new `Obj` is first added to `objectList` it has no `simId` (= `uint.MaxValue`) — it has not been spawned in SimConnect yet.

The creation pipeline is triggered from the main work loop when:
- `obj.Injected == true` (owner = Network or Recorder)
- `obj.Created == false` (no simId yet)
- `obj.subModel != null` (model matching has completed) or enough time has passed

`SimConnectInterface.CreateObject(Obj obj)` (SimConnectInterface.cs ~line 681) builds an `SIMCONNECT_DATA_INITPOSITION`:

```csharp
Latitude  = obj.netPosition.geo.z * (180.0 / Math.PI)   // degrees
Longitude = obj.netPosition.geo.x * (180.0 / Math.PI)
Altitude  = obj.netPosition.geo.y * FEET_PER_METRE       // feet (SimConnect unit)
Pitch     = obj.netPosition.angles.x * (180.0 / Math.PI)
Bank      = obj.netPosition.angles.z * (180.0 / Math.PI)
Heading   = obj.netPosition.angles.y * (180.0 / Math.PI)
OnGround  = 0
Airspeed  = 0
```

Then:
- MSFS 2020: `sc.AICreateNonATCAircraft(title, tailNumber, initPosition, CREATE_OBJECT)`
- MSFS 2024: `sc.AICreateNonATCAircraft_EX1(title, livery, tailNumber, initPosition, CREATE_OBJECT)`
- Helicopters in MSFS 2020: `sc.AICreateSimulatedObject(title, initPosition, CREATE_OBJECT)`

SimConnect responds asynchronously with `OnRecvAssignedObjectId` → `Sim.ProcessAssignedObjectId` which stores `obj.simId`.  Only after `simId` is assigned does position injection begin.

After creation, `sc.AIReleaseControl(simId, RELEASE_AI)` is called so the AI flight model is disabled and JoinFS can set the position directly.

### Optimisation note – spawn time
Model matching (substitution lookup) is async and can take 50–500 ms.  Until it completes, `obj.subModel == null` and the object is not spawned.  For known formation aircraft this latency could be eliminated by pre-resolving the model before the peer connects (e.g. via a lobby or pre-flight metadata exchange).

---

## 8. Per-Frame Velocity Injection – `UpdateSimObjectVelocity`

This is the heart of the inbound pipeline.  It runs on every **SimConnect FRAME event** (simulator frame rate, typically 30–60 Hz) for every injected, created, non-paused object.

`Sim.UpdateSimObjectVelocity(Obj obj)` (Sim.cs ~line 1432):

### 8a. Network Delay Estimation

```csharp
float delay = main.network.localNode.GetNodeRTT(obj.ownerNuid);
float alpha = 0.75f;
delay = alpha * delay + (1.0f - alpha) * prevDelay;   // EMA filter
obj.prevDelay = delay;
```

`GetNodeRTT` returns the measured round-trip time in seconds, obtained from the low-level Pulse/PulseResponse messages.  RTT is divided by 2 (half the round-trip) inside the `netDeltaTime` calculation.

### 8b. Time Delta Calculation

```csharp
double simDeltaTime = main.ElapsedTime - obj.simTime;
double netDeltaTime = obj.netRealTime - obj.netStateTime
					+ main.ElapsedTime - obj.netSimTime
					+ 0.52 * delay;
simDeltaTime = Math.Min(2.0, Math.Max(-2.0, simDeltaTime));
netDeltaTime = Math.Min(2.0, Math.Max(-2.0, netDeltaTime));
```

- `simDeltaTime`: how long ago the sim last reported this object's position.
- `netDeltaTime`: estimated age of the last received state, corrected for network latency.  The constant `0.52` applies slightly more than half the RTT as a forward-prediction offset.

### 8c. Position Extrapolation

```csharp
Pos simPosition = obj.simPosition.Extrapolate(obj.netVelocity, simDeltaTime);
Pos netPosition = obj.netPosition.Extrapolate(obj.netVelocity, netDeltaTime);
Vel netVelocity = obj.netVelocity.Extrapolate(netDeltaTime);
```

`Pos.Extrapolate(Vel v, double t)` (Sim.cs ~line 550):
```csharp
return new Pos(
	geo + (v.linear * t + v.acc * (t * t)) * geodesicScalar,
	angles + v.angular * t,
	elevation,
	ground);
```

This is **kinematic dead reckoning**: position advances by `v·t + ½·a·t²` and angles advance by `ω·t`.  Both current sim position and network target position are extrapolated forward to the same reference moment.

### 8d. Convergence Decision

```csharp
double distance = GeodesicDistance(simPos.geo.x, simPos.geo.z, netPos.geo.x, netPos.geo.z);
double altitudeDelta = |simPos.geo.y - netPos.geo.y|;

double altitudeDeltaLimit = 50.0;    // metres
if (simPos.ground != 0) altitudeDeltaLimit = 0.2;  // MSFS2020/2024 ground snap
```

**Hard reset** if `distance > 50 m` or `|altDelta| > limit`:
```csharp
UpdateObject(obj, netPosition);                                          // teleport position
simconnect.SetData(OBJECT_VELOCITY, simId,
	new ObjectVelocity(netVelocity.linear, netVelocity.angular, netVelocity.acc));
simconnect.SetData(OBJECT_EULER, simId,
	new ObjectEuler(netPosition.angles));
```

**Soft convergence** otherwise:
```csharp
Vector deltaGeo    = (netPos - simPos) in world-space metres
Vector deltaAngles = AnglesDelta(simPos.angles, netPos.angles)

netVelocity.linear  += deltaGeo    * 1.5;   // proportional position correction
netVelocity.angular += deltaAngles * 1.5;   // proportional angle correction

SetData(OBJECT_VELOCITY, simId,
	ObjectVelocity(
		netVelocity.linear.InvRotate(simPos.angles),    // to body frame
		netVelocity.angular * 0.3,                      // dampen angular catch-up
		netVelocity.acc.InvRotate(simPos.angles)));
SetData(OBJECT_EULER, simId,
	ObjectEuler(netPosition.angles));                   // force orientation
```

The **proportional gain of 1.5** means a 1 m position error produces a 1.5 m/s corrective velocity.  At 20 Hz receive rate this settles in roughly 3–5 frames (~0.1 s).

SimConnect's `OBJECT_VELOCITY` expects **body-frame** velocity, so `InvRotate(simPos.angles)` transforms from world to body frame.  Angular velocity is scaled to 0.3 to prevent overcorrection in rotation.

### Optimisation notes – the convergence algorithm

This is the single most important section for formation flying precision.

**1. Hard reset threshold (50 m) is too coarse for formation work.**
At 600 kt (308 m/s) an aircraft covers 50 m in 162 ms — less than 4 position update intervals.  Any network hiccup causing a brief gap will trigger a teleport, which appears as a jarring jump.  
*Recommendation*: reduce the hard-reset threshold to 10–20 m for formation peers, or entirely eliminate it for aircraft within a configurable proximity and replace with purely velocity-based convergence.

**2. The proportional gain (1.5) is fixed.**
For a large error (10 m) the corrective velocity is 15 m/s.  At 600 kt the aircraft is already moving at 308 m/s and a 15 m/s correction is visible but manageable.  For a small error (0.5 m) it is 0.75 m/s which takes about 0.7 s to settle.  
*Recommendation*: use a gain schedule:
- `|deltaGeo| < 1 m` → gain 3.0 (fast small-error settling)
- `1 m – 5 m` → gain 1.5 (current)
- `> 5 m` → gain 0.8 (gentle re-approach to avoid overshoot)

**3. Angular velocity is damped to 0.3 universally.**
For a tight formation turn, the remote aircraft's true angular velocity is already significant (e.g. 0.3 rad/s in a 60° banked turn).  Multiplying by 0.3 means only 10 % of the angular catch-up is applied per frame.
*Recommendation*: separate the damping of the error component from the transmission of the actual angular velocity:
```csharp
angularCorrection = deltaAngles * gain;
SetData(OBJECT_VELOCITY, …, ObjectVelocity(
	linearCorrected.InvRotate(simPos.angles),
	(netVelocity.angular + angularCorrection) * 0.3,   // current: drops real angular vel
	accCorrected));
// better:
	netVelocity.angular.InvRotate(simPos.angles) + angularCorrection * 0.3,
```
This preserves the transmitted angular velocity and only attenuates the correction term.

**4. RTT coefficient 0.52.**
The half-RTT offset is `0.52 * delay`.  The coefficient 0.52 (slightly more than 0.5) adds a small forward bias.  For highly variable latency (Wi-Fi, satellite) this may over-predict.  Making this configurable (e.g. 0.50–0.60) would help users dial in their network conditions.

**5. No velocity smoothing between updates.**
`obj.netVelocity` is replaced wholesale on every received packet.  Between packets the same `netVelocity` is used for extrapolation.  At 20 Hz this introduces a step change in velocity every 50 ms, which is visible as micro-jitter especially under acceleration.  
*Recommendation*: apply a lightweight EMA (alpha=0.7–0.8) on the received velocity before storing it:
```csharp
aircraft.netVelocity.linear  = Lerp(aircraft.netVelocity.linear,  incoming.linear,  0.75);
aircraft.netVelocity.angular = Lerp(aircraft.netVelocity.angular, incoming.angular, 0.75);
```

**6. Per-frame velocity writes are expensive.**
At 60 Hz sim frame rate, `SetDataOnSimObject` is called 2–3 times per object per frame.  With 3 formation members that is 6–9 SimConnect calls per frame.  These cross the SimConnect inter-process boundary.  Profiling may reveal this to be a bottleneck at high traffic; batching into a single data-definition write (combining position+velocity+euler into one struct) would halve the IPC overhead.

---

## 9. FRAME Event and Call Chain

The entire injection loop is driven by the SimConnect FRAME system event:

```
SimConnect fires FRAME event
  │
  └─ SimConnectInterface.RecvEventFrame
	   └─ Sim.ProcessEventFrame
			└─ (for each object in objectList where Injected && Created)
				 └─ UpdateSimObjectVelocity(obj)
					  ├─ EMA filter on RTT delay
					  ├─ Extrapolate simPosition and netPosition
					  ├─ Compute geodesic distance
					  ├─ Hard reset (> 50 m) OR soft convergence
					  └─ SetData(OBJECT_VELOCITY) + SetData(OBJECT_EULER)
```

At the same time, `requestPositionTimer` (50 ms) is requesting updated `simPosition` for the same objects so the convergence loop has fresh feedback.

---

## 10. End-to-End Latency for Received Position

```
Sender sim frame (capture)
  + SimConnect poll round-trip   ~5 ms
  + ProcessAircraftPosition      ~0.1 ms
  + UDP wire time                ~RTT/2 ms (e.g. 10 ms on LAN)
  + ReceiveMsg dispatch          ~0.1 ms
  + UpdateAircraft store         ~0.1 ms
  → netPosition updated

  Next FRAME event (0–16 ms later at 60 Hz)
  + UpdateSimObjectVelocity      ~0.1 ms
  + SetData IPC                  ~0.5 ms
  → AI aircraft position updated in simulator

Total render-to-render latency: ~RTT/2 + 5 + up to 16 ms ≈ 30–40 ms on a LAN
```

At high frame rates (120 Hz) the FRAME latency drops to 8 ms.  On a good LAN this puts total latency below 25 ms, which is essentially invisible.

---

## 11. Formation Flying – Inbound Optimisation Summary

| Issue | Impact at 600 kt | Suggested Fix |
|---|---|---|
| Hard-reset at 50 m triggers on brief network gaps | Visible teleport every ~160 ms of packet loss | Reduce threshold to 10–20 m for close peers; use pure velocity mode |
| Fixed convergence gain 1.5 | Slow settling for tiny errors; possible overshoot for large errors | Distance-adaptive gain schedule |
| Angular velocity damped 0.3 universally | Loses real rotation signal in turns | Apply damping only to error term, not to transmitted angular velocity |
| No velocity smoothing between packets | Micro-jitter at 20 Hz updates | EMA on incoming velocity with α = 0.75 |
| RTT coefficient fixed at 0.52 | Sub-optimal prediction on variable networks | Make coefficient configurable (0.45–0.60) |
| TIME_ERROR_RATE = 0.02 (slow clock correction) | 2.5 s to fully correct 50 ms clock drift after spike | Increase to 0.05–0.10 for close formation peers |
| Multiple SimConnect SetData calls per frame | IPC overhead ×3 per object per frame | Combine position+velocity into one struct; batch per-frame writes |
| Model title in every inbound packet | Extra 30–100 bytes per packet to parse | Separate identity from kinematics on receive side too |
| Out-of-order rejection is hard-drop only | Occasional good packet discarded after reorder | 2–3 packet reorder buffer |
