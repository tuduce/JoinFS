# JoinFS – Outbound Data Flow: How Your Aircraft Is Sent to Other Players

This document describes, in precise detail, the complete journey of your aircraft's state from the flight simulator to every peer in the session.  It covers every layer: data collection, serialisation, interval scheduling, and UDP transmission.  Optimisation opportunities for formation flying at high speed are called out inline.

---

## 1. Data Collection – Polling SimConnect (every 50 ms)

`Sim.cs` owns a 50 ms timer (`requestPositionTimer`).  On every tick it calls **`RequestPosition()`** which loops over `objectList` and issues SimConnect data requests for objects that need to be broadcast.

For aircraft owned by the local user (or explicitly marked for broadcast) the request is:

```
simconnect.RequestData(Requests.AIRCRAFT_POSITION,
					   Definitions.AIRCRAFT_POSITION,
					   obj.simId);
```

The `AIRCRAFT_POSITION` SimConnect definition (registered in `SimConnectInterface` constructor, lines 140–161) pulls the following SimVars in a single round trip:

| Field | SimVar | Type | Units |
|---|---|---|---|
| `latitude` | Plane Latitude | FLOAT64 | radians |
| `longitude` | Plane Longitude | FLOAT64 | radians |
| `altitude` | Plane Altitude | FLOAT64 | metres |
| `pitch` | Plane Pitch Degrees | FLOAT32 | radians |
| `bank` | Plane Bank Degrees | FLOAT32 | radians |
| `heading` | Plane Heading Degrees True | FLOAT32 | radians |
| `velocityX/Y/Z` | Velocity World X/Y/Z | FLOAT32 | m/s |
| `angularVelocityX/Y/Z` | Rotation Velocity Body X/Y/Z | FLOAT32 | rad/s |
| `accelerationX/Y/Z` | Acceleration World X/Y/Z | FLOAT32 | m/s² |
| `rudder` | RUDDER POSITION | FLOAT32 | position (−1..+1) |
| `elevator` | ELEVATOR POSITION | FLOAT32 | position |
| `aileron` | AILERON POSITION | FLOAT32 | position |
| `brakeLeft` / `brakeRight` | BRAKE LEFT/RIGHT POSITION | FLOAT32 | position |
| `elevation` | GROUND ALTITUDE | FLOAT32 | metres |
| `ground` | SIM ON GROUND | INT32 | bool |

Non-aircraft objects use `OBJECT_POSITION_VELOCITY`, which omits the control surfaces but adds the same kinematics.

SimConnect responds via `OnRecvSimobjectData` → `RecvSimObjectData` → `Sim.ProcessSimObjectData`.

### Optimisation note – polling rate
The 50 ms fixed poll is independent of how close the remote aircraft is.  At 600 kt closure speed (≈308 m/s) the sender moves ~15 m between polls.  This is acceptable but leaves no headroom.  **Increasing to a 20 ms (50 Hz) poll** when any node is within a configurable distance would significantly reduce position error for close formation work without material bandwidth cost for distant peers (see interval mask below).

---

## 2. Processing – `ProcessAircraftPosition`

`SimConnectInterface.RecvSimObjectData` delivers the filled `AircraftPosition` struct back through `Sim.ProcessSimObjectData` to **`ProcessAircraftPosition(uint simId, double simTime, ref AircraftPosition aircraftPosition)`** (Sim.cs ~line 2138).

Steps:
1. Locates the `Aircraft` object in `objectList` by `simId`.
2. Copies the fresh position into `aircraft.simPosition`.
3. If this aircraft is owned by the local user and is **not** under remote flight-control:
   - Caches `netVelocity = new Vel(ref aircraftPosition)` (linear + angular + acceleration vectors).
   - Records `netSimTime = main.ElapsedTime`.
4. If connected to the network and `IsBroadcast(aircraft)` is true:
   - **Shared-cockpit path**: if `enteredAircraft != null`, calls `WriteAircraftPositionMessage(uint.MaxValue, …)` and unicasts to the owner of the entered aircraft.
   - **Normal broadcast path**: calls `WriteAircraftPositionMessage(aircraft.netId, aircraft.simTime, aircraft, ref aircraftPosition)` which fills the shared send buffer.

`IsBroadcast` returns `true` when the object is not injected, below 150 000 ft, and broadcast is enabled (AutoBroadcast, explicit flag, or model name match).

---

## 3. Interval Mask – Rate Throttling Per Peer

After the send buffer is prepared, the code loops over every connected node and asks:

```csharp
int intervalMask = GetIntervalMask(aircraft, remoteObject);
if (main.network.GetNodeSimulatorConnected(nuid) == false)
	intervalMask = 0x1f;           // no sim → send every 32nd frame

if ((aircraft.positionCount & intervalMask) == 0)
	main.network.localNode.Send(nuid);
```

The `intervalMask` is a bit-AND filter on `aircraft.positionCount` (an integer incremented once per position cycle).

- `mask = 0x00` → send every frame (~20 Hz at 50 ms poll).
- `mask = 0x01` → send every 2nd frame (~10 Hz).
- `mask = 0x03` → send every 4th frame (~5 Hz).
- `mask = 0x1f` → send every 32nd frame (< 1 Hz – for non-sim clients).

The mask is rebuilt every 5 seconds (`updateIntervalsTimer`).  Currently the only factor that doubles the mask is **low-bandwidth mode**:

```csharp
if (main.network.localNode.lowBandwidth || main.network.localNode.NodeLowBandwidth(remoteObject.ownerNuid))
{
	intervalMask.mask <<= 1;
	intervalMask.mask += 1;
}
```

There is commented-out code that would further raise the mask based on geographic distance, but it is not active.

### Optimisation note – distance-based rate adaptation
The commented-out distance check could be re-enabled and extended.  A two-tier scheme would work well for formation flying:
- Peers within, say, 500 m: mask = 0x00 (full 20 Hz).
- Peers 500 m–5 km: mask = 0x01 (10 Hz).
- Peers > 5 km: mask = 0x03 (5 Hz).

This would let formation members receive the fastest possible update rate without penalising distant peers.

---

## 4. Serialisation – `WriteAircraftPositionMessage`

`Network.WriteAircraftPositionMessage(uint netId, double netTime, Sim.Aircraft aircraft, ref Sim.AircraftPosition aircraftPosition)` (Network.cs ~line 1693) assembles the UDP payload into a shared `MemoryStream` via a `BinaryWriter`.

**Message layout** (all values little-endian):

```
[UDP / Node Header – ~21 bytes]
  short  0x520b          protocol version
  byte   flags           bit1=guaranteed (0 here), bit2=forward
  ushort guaranteedId    (0 – not guaranteed)
  byte   guaranteedIndex (0)
  byte   guaranteedCount (1)
  bytes[7] senderNuid    IP(4)+local(1)+port(2)
  bytes[7] recipientNuid all-zero for broadcast

[Application Header – 4 bytes]
  short  Sim.VERSION     (21003 or 21004)
  short  MESSAGE_ID.AircraftPosition   (= 1)

[Payload]
  uint   netId           sender's SimConnect object ID (4)
  bool   user            is this the pilot's own aircraft (1)
  bool   plane           true=plane, false=helicopter (1)
  string callsign        length-prefixed UTF-8 (~6 bytes typical)
  string model           aircraft title (~30–100 bytes typical)
  byte   typerole        Substitution type role (1)
  byte   flags           bit0=paused (1)
  double netTime         sim elapsed time in seconds (8)

[AircraftPosition – 102 bytes fixed]
  double latitude        radians (8)
  double longitude       radians (8)
  double altitude        metres  (8)
  float  pitch           radians (4)
  float  bank            radians (4)
  float  heading         radians (4)
  float  velocityX       m/s world (4)
  float  velocityY       m/s world (4)
  float  velocityZ       m/s world (4)
  float  angularVelocityX rad/s body (4)
  float  angularVelocityY rad/s body (4)
  float  angularVelocityZ rad/s body (4)
  float  accelerationX   m/s² world (4)
  float  accelerationY   m/s² world (4)
  float  accelerationZ   m/s² world (4)
  short  rudder          16-bit axis (2)
  short  elevator        axis (2)
  short  aileron         axis (2)
  short  brakeLeft       axis (2)
  short  brakeRight      axis (2)
  float  elevation       ground height metres (4)
  byte   flags           bit0=onGround, bit1=elevCorr (1)

[FS2024 only]
  string ownerLivery     livery name (variable)
```

Total minimum on wire (excluding strings): ≈ 21 + 4 + 16 + 102 = **~143 bytes** plus variable-length strings.  A typical packet is **180–240 bytes**.

Controls are quantised to 16-bit signed integers (`ConvertToAxis`: `input × 16384`) giving ±1 in steps of 1/16384 ≈ 0.006 %, which is perfectly adequate.

### Optimisation note – precision of position fields
`latitude` and `longitude` are **FLOAT64** (radians), giving ~15 significant decimal digits.  At the equator one radian ≈ 6 378 km so the LSB ≈ 0.6 μm – far more precision than needed.  Altitude and time are also FLOAT64.  The velocity and angular velocity fields are **FLOAT32**.

For formation flying the relative position error matters more than absolute precision.  However, with both aircraft using the same double-precision lat/lon, the relative error is at most 1 ULP at a global scale, which is negligible.  No change is recommended here.

### Optimisation note – packet structure
The model title string (`aircraft.ModelTitle`) is sent on **every position update** (≈20 Hz).  For a 40-byte title this wastes ~800 bytes/s per peer.  Separating identity messages (sent once or on change) from position messages (sent at full rate) and eliminating the model string from the hot path would reduce bandwidth by 15–20 % and shrink the packet, improving jitter.

---

## 5. Transport – `LocalNode.Send`

After the buffer is prepared, `main.network.localNode.Send(nuid)` resolves the node's `routeEndPoint` and calls:

```csharp
udpClient.Send(data, length, endPoint);
```

This is a raw **UDP unicast** to each peer.  There is no aggregation (each peer gets its own copy).  There is no Nagle or batching – each position update is sent immediately as a separate UDP datagram.

Position messages use `guaranteed = false`.  If the UDP packet is dropped it is **not retransmitted**.  The receiver will use its extrapolation to bridge the gap (see Document 2).

Guaranteed delivery (`guaranteed = true`) is used for: object creation/removal, flight plan changes, show-on-radar, weather, notes, and session metadata.  Guaranteed messages are split into segments and re-sent until a `GuaranteedDone` ACK is received.

### Optimisation note – batching position updates
In a 4-ship formation each pilot sends one position datagram per 50 ms per peer.  With 4 active peers that is 4 datagrams × 200 bytes = 800 bytes/s **send** per aircraft.  This is very low and does not benefit from batching.  However, routing nodes that are forwarding messages for many users do iterate over `nodes` for each message; a single broadcast write to all nodes simultaneously (one buffer, multiple `Send` calls) is already what `Broadcast()` does.

---

## 6. Variable Updates (5 Hz)

Simulator variables (gear, flaps, lights, transponder, etc.) are sent on a separate 0.2 s timer (`variablesTimer`) as `IntegerVariables`, `FloatVariables`, or `String8Variables` messages.  These iterate over `variableSet` change sets and batch up to `MAX_INTEGER_VARIABLES` (100) or `MAX_FLOAT_VARIABLES` (100) per message.

SimEvents (RUDDER_SET, ELEVATOR_SET, AILERON_SET, SMOKE_ON/OFF, and 11 custom events mapped to event IDs `0x00011000`–`0x0001100A`) are sent as `SimEvent` messages, guaranteed delivery, whenever they are triggered.

---

## 7. End-to-End Timing Summary

```
Sim tick (≈50 ms, 20 Hz)
  │
  ├─ SimConnect poll via ONCE request
  │    (~5 ms SimConnect round-trip)
  │
  ├─ ProcessAircraftPosition → WriteAircraftPositionMessage
  │    (< 0.1 ms)
  │
  ├─ Interval mask check per node
  │
  └─ UDP send per permitted node
	   (< 0.2 ms kernel overhead + network latency)
```

Total pipeline latency from simulator state change to bytes-on-wire: **~5–8 ms** in normal conditions.

---

## 8. Formation Flying – Outbound Optimisation Summary

| Issue | Impact | Suggested Fix |
|---|---|---|
| Fixed 50 ms poll regardless of proximity | 15 m position error at 600 kt closure | Add proximity-aware dynamic poll interval (20 ms within 1 km) |
| Interval mask not distance-aware | Distant peers receive same rate as close ones | Re-enable and tune the commented distance-based throttle |
| Model title string in every packet | 15–20 % bandwidth overhead | Separate identity vs. kinematic messages |
| No jitter compensation on send side | UDP timing variance reaches receiver | Timestamp each packet with sender's `ElapsedTime` (already done) and consider smoothing send cadence with a high-resolution timer |
| Angular velocity in body frame, velocity in world frame | Coordinate frame mismatch requires conversion on receiver | Document is fine; ensure receiver converts correctly (see Document 2) |
