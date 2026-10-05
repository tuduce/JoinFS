# Remote-aircraft position estimation: analysis and plan

## Context

In close formation at speed, a remote aircraft's displayed position must be accurate to well below a
wingspan. At 150 m/s, every 10 ms of unaccounted delay is 1.5 m of along-track error. Every 10%
error in modelled turn curvature over a 200 ms horizon is a few metres cross-track. The goal here
is to:

1. say exactly which data and which algorithm JoinFS uses today;
2. list where accuracy is lost;
3. propose better data and data sources;
4. propose an architecture in which the estimator is a swappable plugin;
5. answer whether a Kalman filter, or another control or estimation method, fits.

Written 2026-10-01. Status:

- Phase 1 (refactor, no behaviour change) is done; see §7.1.
- Phase 2: the field logging (`-estimationlog`, §6.1) and the replay of logs through the
  estimators (§6.3) are done. Replaying `.jfs` recordings through an impairment model, and
  shadow mode, are open.
- Phase 3: `ClassicFixed` (F3, F4) is built and has been the default since 2026-10-04 (`-estimator
  Classic` selects the old one); see §7.2 and §6.4. F9 is settled by the SDK documentation (F9 row
  in §2). The X-Plane quick win is open.
- Phase 4: sim-time stamping is built for MSFS (F2) and field-tested (§6.4). X-Plane's is open.
- Phase 5: the passive fallback, `MinOffsetClock` (`-clock MinOffset`), is built and in the tester
  package, not yet the default; see §6.5 and §7.3. The NTP-style exchange on JFP2 is open.
- Phases 6–8 are open; §6.4 lists the next steps.

**Prior art in the repo:**

- `5ff5737` designed a PD gain, SLERP and ground hysteresis
  (`git show 73b203d^:docs/positioning-improvements.md`). None of it was implemented, and it was
  removed in `73b203d`.
- The `net-delay` branch has a `LatencyTracer` and analysis scripts for app-layer latency. It
  predates the thread reorganisation; its receive-stamping goal was achieved in
  `sim-thread-architecture.md` §2.7.

---

## 1. How it works today

### 1.1 Sender (MSFS/FSX/P3D)

- **Subscription.** The user aircraft is subscribed with `AIRCRAFT_POSITION` at
  `SIMCONNECT_PERIOD.VISUAL_FRAME` ([Sim.Requests.cs:84-110](../JoinFS/Sim.Requests.cs#L84-L110),
  [SimConnectInterface.cs:878](../JoinFS/SimConnectInterface.cs#L878)).
- **Fields** ([SimConnectInterface.cs:217-243](../JoinFS/SimConnectInterface.cs#L217-L243)):
  - lat/lon (rad, f64) and altitude (m, f64);
  - pitch/bank/true heading (rad, f32);
  - `Velocity World X/Y/Z` (m/s, f32);
  - `Rotation Velocity Body X/Y/Z` (rad/s, f32; **body-frame** rates);
  - `Acceleration World X/Y/Z` (m/s², f32);
  - rudder/elevator/aileron/brakes;
  - ground altitude, `SIM ON GROUND`, static CG height.
- **Timestamp.** `simTime = main.ElapsedTime` (the sender's own local stopwatch), taken **when the
  sim thread dispatches the SimConnect message**, not when the sim sampled it
  ([Sim.SimConnect.cs:286](../JoinFS/Sim.SimConnect.cs#L286)). Nothing from the sim's own clock
  (`SIMULATION TIME`, `ABSOLUTE TIME`, `SIMULATION RATE`) is used.
- **Rate.** At most one send per `PositionSendInterval = 0.05` s (20 Hz,
  [Sim.cs:45-64](../JoinFS/Sim.cs#L45-L64)).
  - The distance-based thinning in `RebuildIntervalMasks` is **commented out**
    ([Sim.cs:896-906](../JoinFS/Sim.cs#L896-L906)).
  - "Low bandwidth" halves the rate to 10 Hz.
  - A peer with no sim connected gets 1 in 32.
- **Wire.** `SimMessageMapper.ToPositionUpdate` carries all the fields above
  ([SimMessageMapper.cs:35-70](../JoinFS/Session/SimMessageMapper.cs#L35-L70)).
  - `NetTime` is f64 seconds on the sender's own clock, in both legacy and JFP2 PositionV1
    ([PositionCodec.cs:21-92](../JoinFS/Net/Protocols/Jfp2/Codecs/PositionCodec.cs#L21-L92)).
  - Lat/lon/alt are f64. Angles, velocities and accelerations are f32. There is no lossy
    quantisation; quantised PositionV2 is specified but not implemented.
  - Relays (legacy and JFP2, max one hop) forward `NetTime` unchanged and add no per-hop timing.
    `MessageMeta.Forwarded` is known but dropped by `SimIngest`.
- **Clocks.** `main.ElapsedTime` is a process-local QPC `Stopwatch` (monotonic, arbitrary zero).
  There is **no clock synchronisation** between nodes anywhere.

### 1.2 Network delay measurement

- **Source.** RTT comes only from the **legacy** Pulse
  ([MeshManager.cs:226-234, 478, 488](../JoinFS/Net/Core/MeshManager.cs#L226-L234)).
  - Every 1 s the node sends its own `Stopwatch` ticks; the peer echoes them, and
    `RTT = now − echoed`.
  - One raw sample per second, f32 seconds, with **no filtering**. Lost pulses leave the old value,
    which never ages.
  - A known peer that has not answered yet reports **0**; 9999 only appears when the peer is unknown.
- **Relays.** Through a relay the pulse travels the relay both ways, so the RTT is **end-to-end**,
  which is good.
- **JFP2** has no RTT or timing exchange at all. Its Pulse classes are reserved but unused, and its
  keep-alive is a 5 s Hello.
- **Arrival time.** `receivedAt` is stamped on the UDP receive thread right after `ReceiveFrom`
  ([NetworkService.cs:57-58](../JoinFS/Net/Service/NetworkService.cs#L57-L58)). It is carried through
  the network mailbox, the app thread (`SimIngest`, about 5 ms tick) and the sim mailbox
  (`PostToSim`), so the three queues downstream are already accounted for
  (`docs/sim-thread-architecture.md` §2.7).

### 1.3 Receiver: time base ([Sim.ObjectLifecycle.cs:475-500](../JoinFS/Sim.ObjectLifecycle.cs#L475-L500))

Each update gives `netTime` (sender clock) and `receivedAt` (local arrival time). Per object:

- `netStateTime` = `netTime` of the newest accepted sample. Older samples are dropped, with no
  buffer and no reordering.
- `netSimTime` = local arrival time of that sample.
- `netRealTime` = an estimate of "sender clock now". It is advanced by local elapsed time and pulled
  2% per update toward `netStateTime` (`TIME_ERROR_RATE = 0.02`). So it converges to
  `netTime + (constant part of transit)`: it smooths arrival **jitter** but by construction absorbs
  (cannot see) the constant one-way latency.

### 1.4 Receiver: estimation and steering ([Sim.Steering.cs:33-147](../JoinFS/Sim.Steering.cs#L33-L147)), every SimConnect `Frame` event

1. `delay = 0.75·RTT(owner) + 0.25·prevDelay`. The filter runs **per frame**, so it smooths almost
   nothing and depends on frame rate.
2. `netDeltaTime = (netRealTime − netStateTime) + (now − netSimTime) + 0.52·delay`, clamped to ±2 s.
   That is: time since arrival, plus jitter correction, plus about one-way latency taken as RTT/2.
3. Dead reckoning with `Pos.Extrapolate` ([Sim.Data.cs:381-389](../JoinFS/Sim.Data.cs#L381-L389)):
   - `geo += (v·t + a·t²)` with a spherical-earth metres→radians scale;
   - `angles += ω_body · t`;
   - velocity `v += a·t`.
   The local injected object's last read position is also extrapolated by the same velocity over
   `simDeltaTime`.
4. Error = geodesic distance/bearing plus altitude difference. A **hard reset** happens if the
   horizontal error is over 50 m or the vertical error is over 50 m (0.2 m on ground in FS2020/2024).
5. Otherwise it is a **P-controller with velocity feed-forward**:
   - `v_cmd = v_net + 1.5·Δpos` (world frame, then rotated into the body frame);
   - `ω_cmd = 0.3·(ω_net + 1.5·Δangles)` (catch-up only when |ω| < 0.2 rad/s and attitude is
     gentle);
   - `SetData(OBJECT_VELOCITY)`.
   On FS2020/2024 the attitude is also **forced every frame** to the extrapolated angles via
   `OBJECT_EULER`.
6. A paused object snaps to `netPosition` with zero velocity.

### 1.5 X-Plane and recorder paths

**X-Plane sender** ([JoinFS-XP.cpp:1816-1888](../JoinFS-XP/JoinFS-XP.cpp#L1816-L1888)):

- `DoAircraftPosition` runs at **10 Hz**, half the MSFS rate.
- Datarefs read:
  - `latitude/longitude/elevation`;
  - Euler `theta/phi/psi` (no quaternion);
  - `local_vx/vy/vz` and `local_ax/ay/az`;
  - body rates `Q/R/P`.
- `netTime` is the plugin's own QPC/gettimeofday timer, taken from `now` set in `DoFrame`. The
  datarefs are read in `DoAircraftPosition`, about one frame later.
- [XPlane.cs:656-689](../JoinFS/XPlane.cs#L656-L689) converts units and signs to match SimConnect.
  The receiver therefore sees a different clock domain per sender sim, which is fine for per-sender
  offset estimation.

**X-Plane receiver: a separate C++ copy of the estimator** (`AdvancePosition`,
[JoinFS-XP.cpp:2070-2190](../JoinFS-XP/JoinFS-XP.cpp#L2070-L2190)):

- C# forwards only `netTime`; `receivedAt` and RTT are not sent.
- The plugin drains its link socket **only every 0.1 s** (`Link::DoWork` is called from
  `DoAircraftPosition`) and stamps the arrival with that batch time.
- It runs the same `netRealTime` scheme, `a·t²`, body rates applied as Euler rates, 1.5 catch-up,
  ±2 s clamp and a 50 m snap.
- It has **no latency term at all**.
- The snap test uses signed `dx/dy/dz` without `fabs`, so it only snaps on positive deltas.

**Recorder playback** ([Recorder.cs:1427-1592](../JoinFS/Recorder.cs#L1427-L1592)):

- True **interpolation** between recorded frames: linear for position, quaternion SLERP for
  attitude, with an `AngleDelta` fallback.
- It then feeds the same steering with `receivedAt = 0` and RTT 0.
- [Quaternion.cs](../JoinFS/Quaternion.cs) `Slerp` has no shortest-path sign flip, so it can take the
  long way round.

**Tests.** Only `PositionSendRateTests` (`SendDue`), `ReceiveTimestampTests` and the codec
round-trip exist. **Nothing tests extrapolation, steering, time estimation, or the Vector and
Quaternion math.**

---

## 2. Where accuracy is lost (findings)

Ranked by the expected effect on formation flying. The Status column shows what is built; the
status as of 2026-10-05.

| # | Finding | Effect | Status |
|---|---|---|---|
| F1 | **Latency comes from node RTT/2 (×1.04 fudge), not from the sample's real age.** The RTT is a single unfiltered legacy Pulse sample per second. It is 0 until the first answer, so the start has no compensation. It never ages after loss. RTT/2 assumes symmetric paths. | Along-track error ≈ v × (latency error). 20 ms off at 150 m/s gives 3 m. | Open; `MinOffsetClock` replaces RTT/2 (§6.5), field test pending |
| F2 | **The timestamp is the dispatch time, not the sim sample time.** Sim-thread wakeup and SimConnect queueing jitter (a few ms, more under load) are stamped into `netTime`, and `SendDue` picks "first frame after 50 ms". | Jitter goes straight into the extrapolation horizon. | **Done** for FS2020/2024 (§7.2); field-confirmed (§6.4). FSX, P3D and X-Plane open |
| F3 | **The acceleration term is doubled.** `Extrapolate` uses `a·t²`; kinematics is `½·a·t²`. | At 2 g in a turn and t = 0.15 s: 0.22 m extra, growing with t². With t = 0.3 s it is about 0.9 m. | **Done** in `ClassicFixed`, the default (§7.2) |
| F4 | **Body rates are added to Euler angles.** `angles += ω_body·t` ignores the body→Euler kinematics: heading rate = (q·sinφ + r·cosφ)/cosθ. In a 60° banked turn the yaw rate in the body frame is about half the heading rate. Heading wrap is not handled in extrapolation. | Heading is predicted wrong in every banked turn. On FS2020/2024 that attitude is forced every frame, so the error is visible directly. | **Done** in `ClassicFixed`; field-confirmed (§6.4) |
| F5 | **Constant-acceleration world model during turns.** In a coordinated turn the velocity vector **rotates**. Constant-a extrapolation is the second-order Taylor approximation, which diverges as t grows and with noisy `Acceleration World`. A coordinated-turn (constant turn rate) model is exact for steady turns. | Cross-track error in sustained turns, which is the formation case. | Open (phase 6); no measurable gain at a 60 ms horizon (§6.2) |
| F6 | **The RTT low-pass runs per frame** (alpha 0.75 on the new value). | It filters almost nothing, depends on frame rate, and carries RTT spikes straight into the horizon. | Open; `MinOffsetClock` reads the minimum, not a per-frame RTT (§6.5) |
| F7 | **There is no clock offset model.** `netRealTime` hides the latency (see 1.3), which is why F1's RTT term is needed at all. | It couples the estimator to the transport's RTT. | Open; `MinOffsetClock` reads the clock offset directly (§6.5), field test pending |
| F8 | **The steering controller is P on position, with an ad hoc 0.3× on angular velocity.** There is no damping term on velocity error, the gain is fixed (1.5 s⁻¹, about 0.67 s time constant), and the reset thresholds are binary. | Visible lag and overshoot on manoeuvre onset, and snaps at resets. | Open (phase 6) |
| F9 | **The `OBJECT_VELOCITY` definition declares `Acceleration Body X/Y/Z` with unit "radians per second"** ([SimConnectInterface.cs:207-209](../JoinFS/SimConnectInterface.cs#L207-L209)). | Needs verification: the acceleration we write may be mis-scaled or ignored. | Settled by the SDK documentation: `ACCELERATION BODY X/Y/Z` are not settable and are in ft/s², so the sim ignores what we write. The extrapolation reads `Acceleration World` in m/s², which is right. The wrong unit string is harmless; left as is. |
| F10 | **Spherical earth (R = 6 371 009 m) is used for the m→rad scale**, while the sims use WGS-84. The meridional radius varies from 6 335 to 6 400 km. | Up to about 1% of the extrapolated distance, about 0.4 m over 45 m. Low priority. | Open; low priority |
| F11 | **20 Hz fixed rate, no event-driven sends.** A sudden roll-in is only seen up to 50 ms later, plus latency. | Error spikes at manoeuvre onsets. | Open (phase 7) |
| F12 | **Control inputs (aileron/elevator/rudder) are sent but only animate surfaces**, and are not used to predict rate onset. | A missed lead signal of about 50–150 ms. | Open (phase 8) |
| F13 | **The X-Plane sender reports at 10 Hz.** Its timestamp is about one frame off the dataref read. | Twice the update gap of MSFS, so twice the manoeuvre-onset error. | Open (X-Plane quick win) |
| F14 | **The X-Plane receiver has no latency compensation at all.** Arrivals are stamped in 0.1 s batches, and the estimator is a separate C++ copy (duplicated logic, which violates DRY). | The full one-way latency (plus up to 100 ms) is uncompensated. At 150 m/s with 50 ms latency that is 7.5 m or more. This is likely the largest single error in X-Plane formations. | Open (X-Plane quick win) |
| F15 | **Small bugs.** The plugin's 50 m snap uses signed deltas. `Quaternion.Slerp` lacks the shortest-path sign flip. | Rare large excursions, and a long-way rotation in playback. | Open (X-Plane quick win) |

---

## 3. Better data and data sources

Ordered by benefit/cost. "JFP2" means the field goes into the JFP2 `PositionUpdate` schema (new
schema version, negotiated per message class at Hello). The legacy wire stays frozen; legacy peers
just keep today's behaviour.

1. **Sample timestamp from the sim, not dispatch time** (fixes F2). *Done for FS2020/2024 (§7.2);
   open for X-Plane (including the 20 Hz rate), FSX and P3D.*
   - MSFS: add `SIMULATION TIME` (seconds, sim-rate aware) to `AIRCRAFT_POSITION`. Keep a
     per-object mapping from sim time to `ElapsedTime`, using the minimum of
     (dispatch − simulation time) over a window to remove dispatch jitter.
   - X-Plane: take `GetTime()` in `DoAircraftPosition` itself, where the datarefs are read, and
     raise the rate to 20 Hz or every frame (fixes F13).
   - The sent `NetTime` becomes "sender clock at sample", without jitter. No wire change is needed
     for this part.
2. **Clock-offset estimation per peer, then sample age = now − (netTime + offset)** *(the passive
   fallback is built, §7.3; the exchange is open)* (fixes F1 and
   F7, and makes relays transparent). Use NTP-style four-timestamp exchanges on JFP2's own
   keep-alive or Hello traffic, filtered:
   - minimum-RTT sample selection;
   - an α-β filter (or 2-state Kalman filter) over offset and drift.
   Because `netTime` is end-to-end, every relay hop, queue and asymmetric route is included in the
   computed age.
   - **Where to carry the exchange.** The legacy Pulse is frozen and only echoes the originator's
     own time, so it gives RTT but not offset. Put the exchange in JFP2's reserved Pulse classes
     (5/6, [Envelope.cs](../JoinFS/Net/Protocols/Jfp2/Envelope.cs)): t1 is sent, the peer stamps t2/t3,
     and the receiver stamps t4.
   - **Relays.** Today's pulse already travels relays end-to-end, so the same routing gives an
     end-to-end offset.
   - **Fallback.** For legacy-only peers, estimate the offset passively, from the minimum of
     (arrival − `netTime`) over a sliding window minus RTT_min/2. This needs no wire change and is
     still much better than the per-frame RTT/2.
   - **Interim fix.** Until then, filter the RTT on the network thread (median or EWMA over samples,
     not frames), age it after lost pulses, and treat 0 as "unknown".
3. **Sender-side dead-reckoning threshold (DIS / IEEE 1278 style)** (fixes F11). *Open.*
   - The sender runs the *same* estimator the receiver runs on its own last-sent state.
   - When predicted vs actual error exceeds a threshold (e.g. 0.3 m or 1° when a peer is within
     about 200 m), it sends immediately instead of waiting for the 20 Hz slot.
   - The extra sends are only for nearby peers (reuse `intervalMasks` distance info). This works
     with legacy peers too: they just see a higher rate.
4. **Turn-consistent kinematic state** (fixes F4 and F5). *Partly done: the receiver converts the body
   rates properly through a quaternion (F4, §7.2). Angular acceleration, load factor and the
   coordinated-turn model (F5) are open.*
   - Send attitude as a quaternion (or keep Euler but convert rates properly on the receiver).
   - Send body rates p,q,r (already sent) **plus** angular acceleration from a sender-side finite
     difference over 2–3 frames.
   - Optionally send `Acceleration Body` / load factor as well. In a coordinated turn the turn rate
     follows from g·tanφ / V, which is a consistency check against noisy `Acceleration World`.
5. **Lead signals for manoeuvre onset** (fixes F12). *Open.* Aileron/elevator/rudder positions are already
   on the wire. An estimator may use them to predict rate onset (roll rate ≈ k·aileron, with k
   learned online per aircraft from the observed p vs aileron). This is optional and experimental:
   a good candidate for the plugin architecture.
6. **Sim rate and pause state.** *Open: pause exists; the stamper copes with other rates, by falling
   back or starting over, but does not use the rate.* Include `SIMULATION RATE` (MSFS) or `sim/time/sim_speed` (X-Plane).
   The receiver then scales the horizon or refuses to extrapolate when the sender runs at a
   non-1× rate. Pause already exists as a flag.
7. **WGS-84 local tangent plane** (fixes F10). *Open.* Do extrapolation in an ENU/NED frame around the
   sample, with proper ellipsoid radii, then convert back.

Expected combined effect, estimated before measurement:

- F1, F2, F6 and F7 are timing errors of typically 5–30 ms, which is 1–5 m at formation speeds.
  Removing them is the biggest win.
- F3, F4 and F5 are model errors, mostly in turns; fixing them gives up to several metres at
  200–300 ms horizons.
- F11 bounds the worst case at manoeuvre onsets.

These numbers must be confirmed with the measurement harness in §6 before choosing.

---

## 4. Pluggable estimator architecture

### 4.1 Split today's single method into three roles

```
 network ──► SimIngest ──► Sim.UpdateObject(netTime, receivedAt, state)
                                │
                                ▼
                     ┌──────────────────────┐     per peer
                     │  IClockModel         │◄─── timing samples (RTT, NTP exchange,
                     │  sender→local time   │     arrival vs netTime)
                     └─────────┬────────────┘
                               ▼
                     ┌──────────────────────┐     per remote object
                     │  IStateEstimator     │  AddSample(sample) / Predict(localTime)
                     │  (the swappable part)│
                     └─────────┬────────────┘
                               ▼ KinematicState target(t_now [+ actuation lead])
                     ┌──────────────────────┐     per sim backend
                     │  IObjectSteering     │  MSFS: velocity+euler SetData; X-Plane: plugin
                     └──────────────────────┘
```

### 4.2 Types (new folder `JoinFS/Estimation/`, pure C#, no `#if`)

The folder is outside `#if SIMCONNECT`, so tests (FS2024-Debug) and the XPLANE/CONSOLE builds see
it.

- **`KinematicState`** (immutable struct):
  - position as a geodetic double triple **plus** a local ENU helper;
  - attitude as a quaternion (existing [Quaternion.cs](../JoinFS/Quaternion.cs));
  - world velocity, body rates and world acceleration;
  - optional angular acceleration and control inputs;
  - flags: on ground, paused.
- **`StateSample`**: a `KinematicState`, plus `SenderTime` (`netTime`), `ArrivalTime` (local) and a
  `Source` (Network or Recorder), plus optional sim rate.
- **`IClockModel`** (per peer node):
  - `void OnTimingSample(...)`;
  - `double SampleAge(double senderTime, double localNow)`;
  - `double Uncertainty`.
  Implementations:
  - `RttHalfClock` reproduces today's `netRealTime + 0.52·RTT` exactly, for A/B comparison;
  - `OffsetClock` is the NTP-style offset and drift filter.
- **`IStateEstimator`** (per remote object):
  - `void Reset()`;
  - `void AddSample(in StateSample s, IClockModel clock)`;
  - `bool TryPredict(double localTime, out KinematicState state, out double sigma)`.
  `sigma` is a confidence value the steering may use to soften corrections.
- **`IObjectSteering`**: `void Steer(Obj obj, in KinematicState target, in KinematicState measured,
  double dt)`. It is the only part that touches SimConnect or X-Plane. The MSFS implementation is
  today's controller moved out of `UpdateSimObjectVelocity`.
- **`EstimatorRegistry`**:
  - name → factory;
  - selected by a setting (`Settings.Default.PositionEstimator`, default `"ClassicFixed"` since 2026-10-04);
  - per-object override for A/B;
  - shown in the Monitor/Objects form for diagnostics.

### 4.3 Wiring

These are minimal edits to existing owners; the sim thread owns all of it.

- `Sim.Obj` gains `IStateEstimator estimator`, created lazily in `Sim.UpdateObject(Obj, netTime, …)`
  ([Sim.ObjectLifecycle.cs:475](../JoinFS/Sim.ObjectLifecycle.cs#L475)). That method then calls
  `estimator.AddSample(...)`. The `netRealTime/netSimTime` maths moves into `RttHalfClock`.
- Per-peer `IClockModel` instances live in a small `ClockModels` map on the sim side.
  - Timing samples come in via the existing session → sim posting (`ISimSink`).
  - The network thread publishes RTT and offset in `Network.Snapshot`. We do not reach into
    `NetworkCore`.
- `Sim.UpdateSimObjectVelocity` becomes:
  1. `estimator.TryPredict(now, out target)`;
  2. build `measured` from `simPosition`;
  3. `steering.Steer(...)`.
- **X-Plane.** Stop duplicating the estimator in C++.
  - **Step 1:** C# computes the sample age with its `IClockModel` and sends it with each update.
    The plugin drains its link every frame and adds the age to `ndt`. This alone fixes F14.
  - **Step 2:** C# runs the chosen `IStateEstimator` and streams *predicted target state + velocity
    at local time T* to the plugin at frame rate, or 30–60 Hz over the local UDP link. The plugin's
    `AdvancePosition` is reduced to an `IObjectSteering` (short-horizon follow).
  - From then on one estimator implementation serves every sim, and estimators are tested in C#.
- Recorder playback uses a `RecorderClock` (age = playback time − sample time, no RTT), so the same
  estimators replay recordings.
- **Shadow mode.** Optionally run N estimators per object in parallel and drive the sim from one.
  For each, log the prediction error when the *next* real sample arrives. The later sample is ground
  truth for the earlier prediction at that sender time, which gives a live, per-flight scoreboard
  for free (see §6).

### 4.4 First estimators to implement behind the interface

1. **`Classic`**: today's behaviour, bit for bit, as a baseline. This includes F3 and F4, on
   purpose.
2. **`ClassicFixed`**: ½·a·t², body→Euler rate kinematics (or quaternion integration), heading
   wrap, and a time-constant (not per-frame) RTT filter.
3. **`CoordinatedTurn`** (CTRA): integrates the attitude quaternion with body rates and rotates the
   velocity vector by the turn rate. Along-track acceleration comes from the velocity derivative.
4. **`KalmanCA` / `IMM`**: see §5.

---

## 5. Kalman filters and alternatives: what fits here

**Why a classic Kalman filter is less of a win here than in NASA/radar tracking.** A Kalman filter
pays off when *measurements are noisy* and a model must weigh them against prediction. Our
measurements are the sender sim's own state, exact to float precision. Their "noise" is really:

- (a) timing uncertainty (latency and jitter);
- (b) quantisation, if JFP2 compresses fields;
- (c) the unknown pilot input during the prediction horizon (100–300 ms).

That last one is *process* uncertainty, which no filter can remove; it can only be bounded. So:

- **Clock and latency estimation: yes, use a Kalman (or α-β) filter.**
  - State: [offset, drift].
  - Measurements: NTP-style offset samples, with R inflated for high-RTT samples.
  This is exactly the noisy-measurement problem that Kalman filters are good at, and it removes the
  largest error source (F1/F7).
- **Kinematic state: an EKF is useful mainly for two things.**
  - Fusing redundant, slightly inconsistent channels (position, velocity, acceleration, rates; the
    noisy `Acceleration World`) into one consistent state, instead of trusting each raw channel.
  - Coping with lost or late samples by giving a covariance, so the steering can be soft when
    uncertain.
  On its own it will not beat a correct kinematic model by much, because of the near-perfect
  measurements.
- **IMM (Interacting Multiple Model) is the better-suited tracker.** It is the standard in ATC radar
  tracking:
  - it runs 2–3 Kalman filters in parallel: constant velocity (straight and level), coordinated turn
    (CTRV/CTRA) and a high-agility model (aerobatics or ground manoeuvring);
  - it mixes them by their likelihood.
  It switches model quickly at manoeuvre onset and is exact in steady turns, which is the formation
  case. Recommended as the "advanced" plugin once a deterministic `CoordinatedTurn` baseline exists.
- **Biggest structural improvements, which are not filters:**
  1. **Sender-side dead-reckoning thresholds (DIS).** They bound the error at the source; no
     receiver filter can do that.
  2. **Exact sample age from clock sync.**
  3. **The right motion model** (coordinated turn, quaternion attitude).
- **Steering (the "control" part): replace the P-controller with a critically damped second-order
  tracker.**
  - `a_cmd = a_ff + Kp·Δpos + Kd·Δvel`, with Kd = 2√Kp, or an equivalent PD on velocity.
  - Alternative: *projective velocity blending* (Murphy, used in games). It blends from the current
    displayed trajectory to the new predicted one over a fixed time T, with no overshoot and no snap.
  - Scale the gains with estimator `sigma` and with the distance to the user aircraft: tight when
    close in formation, softer when far.
  - **MPC is overkill.** The plant (MSFS velocity SetData) is close to a pure integrator with about
    one frame of delay, so PD plus feed-forward is near optimal.
  - Use SLERP (quaternion) for attitude catch-up instead of forcing Euler each frame. This was
    previously designed in `5ff5737` (`docs/positioning-improvements.md`, never implemented).
- **Interpolation (playout buffer) is the wrong default for formation.** It adds deliberate delay. In
  formation the other pilot needs where the leader is *now*, so it stays extrapolation. A short
  buffer (one send interval) can be an option for far traffic, where smoothness matters more than
  timeliness.

---

## 6. Measurement first: the tracer bullet

Nothing above should be chosen on intuition. Build the scoring harness first.

1. **Offline harness in `JoinFS.Tests/Estimation/`:**
   - load a `.jfs` recording (truth at 20 Hz, or every frame if a dense-recording option is added);
   - replay it through a `NetworkImpairment` model: latency distribution, jitter, loss, relay hop;
   - drive each `IStateEstimator` with `ManualClock`;
   - score its prediction at each frame against the interpolated truth: RMS and 95th-percentile
     position error in metres (split into along-track, cross-track and vertical) and attitude error
     in degrees.
   Recordings with formation turns, rolls and taxiing are the test set.
2. **Online shadow scoring** (see §4.3) in the log, compared with each evening's field test using
   the existing `check-logs` workflow. The `net-delay` branch's `LatencyTracer` (ring buffer and
   JSON dump) is prior art worth reusing for app-layer latency.
3. **Acceptance:** an estimator replaces `Classic` as default only when it lowers the 95th-percentile
   error on the recorded set at 50/100/200 ms latency, and the evening field test shows no new
   jitter.

### 6.1 The estimation log (built)

This is the field-data half of item 2. It records, on the sim thread, everything needed to score
positioning offline.

**Turning it on.** Start JoinFS with `-estimationlog`. Each session writes a new file in the
instance's storage folder:

```
%LOCALAPPDATA%\JoinFS-<build>\estimation-<port>-<yyyyMMdd-HHmmss>.csv
```

The monitor log shows the path (`Estimation log - …`). If a write fails, the log stops and says so
(`Estimation log stopped - …`); positioning is never affected. The file is flushed every second, and
again when the simulator link closes.

**Rows.** See [EstimationLog.cs](../JoinFS/Estimation/EstimationLog.cs) for the columns and units.

- **`clock`**, once a second: `utc` (Unix seconds) against `local` (this process's `ElapsedTime`).
- **`send`** (from 2026-10-03), for each of our own samples sent: `local` is when its message was
  handled, `netTime` the stamp it was sent with, and `simClock` the simulator's own clock at the
  sample (empty without one). A receiver's `sample` row carries the same `netTime`, so the two
  logs join exactly, and every sample can be scored with either stamp.
- **`sample`**, for each accepted position of a network object (and of your own aircraft while
  someone else flies it). Recorder playback is not logged. Each row has:
  - the sample: sender `netTime`, arrival `receivedAt`, `rtt` to the owner, then position,
    attitude, velocity, angular velocity, acceleration, `ground` and `paused`;
  - **the newest prediction made before it arrived**: `predLocal` (when it was made), `predFrom`
    (the sender time of the sample it came from), `predAge` (the horizon the clock chose), and the
    predicted position and attitude. These are empty until the first prediction, and only
    SimConnect builds predict in C#; X-Plane's plugin predicts on its own;
  - **where the simulator last reported the injected object**: `simTime` and the position and
    attitude.

**Analysis it enables.**

- **Model error at the chosen horizon (exact).** The prediction targets sender time
  `predFrom + predAge`. Interpolate the logged samples of that object at that sender time, and
  compare the position with the predicted one. Split the error into along-track, cross-track and
  vertical, and add the attitude error.
- **Timing error (estimated).** The true age of a sample at local time T is
  T − (`netTime` + clock offset). Estimate the offset per sender from `receivedAt − netTime`: its
  minimum over a window, minus RTT/2. Or measure it exactly when **both** machines log and their
  Windows clocks are synchronised (`w32tm /query /status`): map the sender's `netTime` to UTC via
  its clock rows, and the receiver's `receivedAt` via its own. That works for SimConnect senders,
  whose `netTime` is their `ElapsedTime`. An X-Plane sender's `netTime` is the plugin's clock, so it
  cannot be mapped that way.
- **Displayed error.** Compare the sim-reported position against the truth at the matching sender
  time. This covers estimator, clock and steering together: what the other pilot actually sees.
- **Replay.** Feed the `sample` rows (with their `receivedAt`) to any `IStateEstimator`/`IClockModel`
  offline. This is the input for the §6 harness.

**Tester package.** To hand the logging build to a group of pilots, run:

```
JoinFS\util\tester-package\Build-TesterPackage.ps1 -UploadTarget <user>@<host>:<path>/ -UploadPort <port>
```

The upload server is passed here and never written in the repository.

- **Uploading.** Each package gets its own ed25519 login key, so testers need no password.
  `Collect test logs.bat` uploads with Windows' own `sftp` in batch mode. It uses `sftp`, not
  `scp`, because Windows 10's `scp` speaks the old SCP protocol, which the server refuses.
- **Installing the key.** The build script prints the line to add on the server:
  `restrict,expiry-time="<date>" ssh-ed25519 …`. The key stops working after `-KeyValidDays`
  (default 60).
- **Host keys.** The server's host keys (ed25519, ecdsa, rsa) are taken at build time and pinned
  in the package. Testers are never asked to trust the server, and an impostor is refused.
- **Without a server.** Leave out `-UploadTarget`, and `-UploadInfo "<where to send the logs>"`
  tells testers where to send the zip.

The server account is a write-only drop box, so a leaked package can only upload. The sshd config:

```
Match User uploaduser
    ChrootDirectory /var/sftp/uploaduser            # root:root 755
    ForceCommand internal-sftp -d /incoming -u 0077 -p open,close,write,fstat,lstat,stat,realpath,limits
    AuthorizedKeysFile /etc/ssh/authorized_keys/%u  # root-owned: the user cannot add keys
    PasswordAuthentication no
    PermitTunnel no
    AllowAgentForwarding no
    AllowTcpForwarding no
    X11Forwarding no
```

`/var/sftp/uploaduser/incoming` is `root:uploaduser 1730`. Testing on 2026-10-01 confirmed that
uploading works and that everything else is refused: listing, downloading, deleting, renaming,
creating folders, changing permissions, writing outside `incoming`, a shell, tunnelling and the
old SCP protocol.

It writes `artifacts\JoinFS-test-<commit>.zip` with:

- the FS2024 and FS2020 builds (choose with `-Builds`; `-SelfContained` bundles .NET);
- one `Start JoinFS - <sim>.bat` per build. It runs the app with `-estimationlog`, after recording
  the PC's clock offset to `time.windows.com` in `clock-*.txt`;
- `Collect test logs.bat`, which zips the last 7 days of `estimation-*.csv`, `log-*.txt` and
  `clock-*.txt`, plus the package info, onto the Desktop;
- a README for the testers.

The clock files let logs from several PCs be put on one time line, even when the PCs' clocks differ
(one test PC was 211 ms off).

**Caveats.**

- After a reset (pause, cockpit entry, playback seek), the first row's prediction can be from before
  the reset. Drop rows where `predFrom` is older than the previous sample's `netTime`.
- Size is about 500 bytes per full row. At 20 Hz per remote aircraft that is roughly 35–40 MB per
  hour per aircraft (half that for 10 Hz senders: X-Plane, or low bandwidth). Clock rows add about
  0.15 MB per hour.

### 6.2 First field results (2026-10-01)

**Session.** Two MSFS 2024 jets, HB-TDX on FLIGHTSIM and YR-SCD on CRISTII5DESK, both running
`Classic`.

- Both logs were analysed together with
  `JoinFS/util/estimation-analysis/analyze_estimation.py`, through the `analyze-estimation` skill.
- 45 minutes of both flying, at a median 150 m/s.
- About 18 ms each way, steady (p5 to p95 within about 1 ms).

| Error against the sender's state at that moment | YR-SCD on FLIGHTSIM | HB-TDX on CRISTII5DESK |
|---|---|---|
| Along track, p50 / p95 / p99 | 0.29 / 1.44 / 2.50 m | 0.17 / 0.99 / 2.58 m |
| Cross track, p95 | 0.02 m | 0.03 m |
| Vertical, p95 | 0.21 m | 0.10 m |
| Close formation (< 100 m, median 62 m), along p95 / drawn along p95 | 1.31 / 1.60 m | 0.92 / 1.47 m |
| Heading in turns, p95 / p99: Classic → with Euler rates | 0.56 / 2.18 → 0.05 / 0.19 deg | 0.71 / 2.55 → 0.04 / 0.27 deg |
| Without prediction, horizontal p95 | 14.6 m | 16.0 m |

**Conclusions.**

- **F2 dominates.** The along-track error tracks the sender's timestamp jitter:
  - CRISTII5DESK stamps with ±7 ms of noise (p5–p95) and FLIGHTSIM with ±4 ms;
  - the along-track error is larger for the noisier sender.

  Phase 4 (sim-time stamps) is the biggest win.
- **F4 is confirmed and cheap to fix.**
- **F3 and F5 make no measurable difference at a 60 ms horizon.** Kalman/IMM work is not justified
  at this latency.
- **Steering.** The drawn error exceeds the predicted one mostly in turns and vertical manoeuvres:
  the Euler singularity near ±90° pitch.
- **Rare spikes (<0.1%).** They hit both directions at the same seconds, alongside 250–700 ms gaps
  in the senders' samples while delays stayed normal. Most likely both sims hitched together.
- **Unexplained.** One 5 s episode where YR-SCD's sim drew HB-TDX level and 357 m too high.

**Lessons for the measurement.**

- `w32tm` against `time.windows.com` misaligned the PCs by +15 to +70 ms, and Windows Time stepped
  FLIGHTSIM's clock mid-session. The tool therefore aligns the PCs from their two-way traffic.
- The 1/32 send rate (1.6 s gaps) is by design while a peer's simulator is disconnected. YR-SCD's
  JoinFS ran without MSFS for an hour.

### 6.3 The log replay (built)

`JoinFS.Tests/Estimation/Replay/` replays the `sample` rows of estimation logs through every
estimator in `EstimationRegistry`, with the real C# code.

- **Scoring.** Each prediction is compared with the sender's own samples, interpolated at the
  predicted moment on the sender's clock. The horizon is given, not chosen by a clock model, so
  this scores the estimator alone. The horizons are the ones the receiver's clock chose in the
  field (`logged`), plus 50, 100 and 200 ms for other network delays.
- **Output.** Along-track, cross-track, vertical, heading, pitch and bank error percentiles, per
  phase (ground, straight, turning). It also names the estimator that ran, by which one
  reproduces the logged predictions.
- **Running it.** Name the logs (CSV files, folders, or the tester package's zips, separated by
  `;`) in `JOINFS_ESTIMATION_LOGS`; the report goes to `JOINFS_REPLAY_REPORT`, by default
  `estimation-replay.md` in the temp folder. The test does nothing when the variable is unset.

```
$env:JOINFS_ESTIMATION_LOGS = "D:\logs"
dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug -p:Platform=x64 --filter "FullyQualifiedName~ReplayFieldLogs"
```

**Results on the 2026-10-01 and 2026-10-02 logs** (two MSFS 2024 receivers, about 1.5 million
predictions, including other pilots' traffic on 2026-10-02). In turns (|bank| ≥ 30°), p95:

| | Classic | ClassicFixed |
|---|---|---|
| Heading, logged horizon (58–72 ms) | 0.6–2.1° | 0.04–0.13° |
| Pitch, logged horizon | 1.5–2.2° | 0.03–0.10° |
| Bank, logged horizon | 0.4–2.0° | 0.13–0.24° |
| Cross-track, 200 ms | 0.55–0.79 m | 0.05–0.08 m |
| Heading, 200 ms | 2.0–2.3° | 0.14–0.56° |
| Bank, 200 ms | 1.8–2.5° | 1.1–1.6° |
| Along-track, any horizon | the same | the same |

- ClassicFixed is better or equal in every phase, at every horizon, in every log. The pitch and
  bank results also confirm the sign conventions behind `Attitude.Integrate`.
- What is left at 200 ms is mostly bank: the roll rate changes within the horizon (roll-in and
  roll-out), which constant body rates cannot follow. That is the case for angular acceleration
  or control-input lead (§3 items 4 and 5), when longer horizons matter.
- The along-track error does not change: it is the senders' stamping jitter (F2), which no
  estimator can remove. That is what sim-time stamping (§7.2) is for.

### 6.4 Field results with ClassicFixed and sim-time stamps (2026-10-04)

**Session.** The same two MSFS 2024 PCs, from 19:04 to 20:19 UTC. Both ran the tester package built
from `86a0cfe` (the same change as `a979145` on this branch, before a rebase) with `ClassicFixed`
and sim-time stamps. About 79 000 predictions per direction; the fastest way took 17.2 ms and the round trip was 35.5 ms. The comparison is with the `Classic` sessions of
2026-10-01 to 03 in the same upload; its analysis was done with `analyze-estimation`.

| | Classic, earlier sessions | ClassicFixed + sim-time stamps |
|---|---|---|
| Sender stamp error between samples, p5 / p95 | YR-SCD −7.4 / +7.0 and −5.1 / +4.8 ms; HB-TDX −4.1 / +4.1 and −2.7 / +2.5 ms | −0.1 / +0.0 ms, both senders; 0% over 5 ms |
| Along track, YR-SCD on FLIGHTSIM, p50 / p95 / p99 | 0.24–0.29 / 0.92–1.42 / 1.50–2.47 m | 0.42 / 0.71 / 0.86 m |
| Along track, HB-TDX on CRISTII5DESK, p50 / p95 / p99 | 0.15–0.17 / 0.53–0.98 / 0.86–2.57 m | 0.23 / 0.45 / 0.59 m |
| Cross track, p95 | 0.01–0.03 m | 0.01 m |
| Heading in turns (\|bank\| ≥ 30°), p95 / p99 | 0.6–2.0 / 2.2–9.6° | 0.08 / 0.28° (YR-SCD), 0.13 / 0.35° (HB-TDX) |
| Predictions more than 3 m off | 0.36–0.78% | 0.13% (YR-SCD), 0.04% (HB-TDX) |
| Drawn along track, p95 | 0.8–1.8 m | 0.86–0.88 m |

**Conclusions.**

- **F2 is fixed.** The sender's stamps are now as steady as the simulator's clock. The along-track p99
  fell from about 2.5 m to under 0.9 m.
- **F4 is confirmed in the simulator**, not only in the replay: the heading error in turns fell by an
  order of magnitude, and the cross track is at 0.01 m.
- **The along-track median rose** by about 0.1–0.2 m (0.4 m for YR-SCD, 0.2 m for HB-TDX). The
  cause is the clock model, not the stamper (§6.5): `RttHalfClock` gave ages 3.2 and 1.7 ms too low
  tonight, and 3 ms at 135 m/s is 0.4 m. On the earlier days its bias was the other way (+0.3 to
  +0.6 ms), so it moves with the path.
- **The timing p95 is now the largest error left.** It is 3.2–4.9 ms, which at 150 m/s is
  0.5–0.7 m, about the along-track p95 itself.
- **Rare large errors remain** (13–16 episodes over 10 m per direction):
  - 40–70 m along track for a fraction of a second, one of the predicted and drawn positions off
    while the other is right. They coincide with gaps in the samples (pauses, hitches).
  - YR-SCD showed 2 323 m and 1 253 m predicted errors for 0.1 s at 19:21:03 and 19:17:05, with the
    sender level. The pilot says YR-SCD was following HB-TDX to get closer, so these are the
    sender's own jumps, not an estimator fault. How the following moves the aircraft was not checked.
  - One HB-TDX prediction differs by 18 m from the offline replay of the same input, against about
    0.1 mm for all the others.
- **The 357 m episode (§6.2)** is from the 2026-10-01 session on `Classic` and is still unexplained.

**Next steps**, in this order:

1. **Field-test `MinOffsetClock`** (§6.5): the tester package starts it. Expect the timing p95 and
   the along-track median to fall to the sub-millisecond level the offline scoring shows.
2. **The remaining rare large errors.** The 19:17 and 19:21 jumps are explained (YR-SCD following
   HB-TDX). The 18 m replay mismatch is not; find the sample that causes it. Then check how the
   receiver draws a sender's jump (a hard reset, not a long steer), using those two episodes.
3. **F9** is settled (F9 row in §2); F6 is replaced by `MinOffsetClock`.
4. **Other simulators and rates.** Collect one session each from FS2020 and from FSX/P3D (the same
   code, unvalidated), and one with a sim rate other than 1×.
5. **X-Plane quick win.** A 20 Hz sender, drain the link every frame, send the sample age to the
   plugin, fix the snap sign and `Slerp`, and bump `DATA_VERSION` on both sides; then X-Plane
   sim-time stamps. Use `fake_xplane_plugin.py` first, and be careful here.
6. **The NTP-style exchange on JFP2 (phase 5, the rest).** The passive clock assumes the two ways
   of the path take the same time, like the old one. A real exchange needs a log from a long or
   asymmetric link to show a difference, and the alignment in the analysis tool cannot see one
   either.
7. **Steering and angular acceleration (phase 6).** Bank is the largest attitude error left at a
   200 ms horizon; at the 60–70 ms horizon in use it is already small.
8. **Not justified by the data:** Kalman/IMM (phase 8) and the sender dead-reckoning threshold
   (phase 7).

### 6.5 Clock models scored on the field logs (2026-10-05)

The estimator is now close to exact, so the age it is given is what is left (§6.4). The estimation
log has each prediction's age (`predAge`), and with both PCs on one time line the true age is
known, so any clock model computable from the logged samples (stamp, arrival, round trip) can be
scored. `JoinFS/util/estimation-analysis/compare_clocks.py` does it, one day at a time.

**The model scored**, as `MinOffsetClock` (§7.3) computes it: the age of a sample is the time since
it arrived, plus how much later than the fastest sample of the last 10 s it arrived, plus half the
smallest round trip of the last minute.

Error = model age − true age, in ms (p5 / p50 / p95; then |error| p95):

| session | PC watching | logged (`RttHalf`) | `MinOffset` |
|---|---|---|---|
| 2026-10-04, sim-time stamps | FLIGHTSIM | −4.9 / −3.2 / −1.1; 4.9 | −0.8 / −0.3 / 0.05; 0.8 |
| | CRISTII5DESK | −3.1 / −1.7 / 0.2; 3.2 | −0.4 / −0.1 / 0.1; 0.4 |
| 2026-10-02, dispatch stamps | FLIGHTSIM | 0.1 / 0.6 / 2.7; 2.8 | −0.2 / 0.1 / 0.3; 0.4 |
| | CRISTII5DESK | −0.3 / 0.3 / 2.4; 2.4 | 0.0 / 0.2 / 0.4; 0.4 |
| 2026-10-01, dispatch stamps | FLIGHTSIM | −0.1 / 0.5 / 3.5; 3.5 | −0.3 / 0.0 / 0.3; 0.4 |
| | CRISTII5DESK | −1.5 / 0.0 / 2.7; 3.0 | 0.0 / 0.2 / 0.5; 0.5 |

- **What it shows.** The old clock errs by 2.4–4.9 ms (p95) on every session and its median moves
  from −3 to +0.6 ms with the path; the minimum-offset clock holds below 1 ms on all of them.
- **Variants tried** on the logs (not kept): a 5 s window is as good with more noise; 30 s drifts
  on one path; a 10th or 25th percentile instead of the minimum leaves a −1 to −4 ms bias on
  tonight's logs; a median round trip over 10 s is worse than the raw one.
- **The limit of the test.** The two PCs are aligned by taking the fastest sample each way to have
  the same delay, which is also what the minimum-offset clock assumes. So it scores how well the
  clocks follow the path's variation, not an asymmetry between the ways; the old clock assumes the
  same. The 3 ms bias of the old clock tonight is real against that reference, and the along-track
  error agrees with it (0.4 m at 135 m/s).
- **The analysis tool** needed a fix to read tonight with both PCs: it matched a sender's session
  only when the PCs' clocks were within 3 s, and CRISTII5DESK ran 3.6 s off; the limit is now 10 s.
  With it, tonight's one-way delays are 17.6–17.8 / 19.7–21.2 / 24.5–26.2 ms (p5 / p50 / p95).
  The §6.4 error figures hardly changed.

---

## 7. Implementation phases

Each phase can ship on its own.

1. **Refactor with no behaviour change.**
   - Add `Estimation/` types, `Classic` + `RttHalfClock`, the registry and `IObjectSteering` (MSFS).
   - `UpdateSimObjectVelocity` delegates to them.
   - Pin `Classic` with unit tests (golden values for `Extrapolate` and for the steering commands).
2. **Harness:** the offline scorer and shadow mode, with logging.
3. **`ClassicFixed`:** F3, F4 and F6, plus a check of F9's unit string against SimConnect docs or
   the sim.
   - In the same phase, an **X-Plane quick win**: 20 Hz sender, drain the link every frame, send the
     sample age to the plugin (F13 and F14), fix the snap sign and `Slerp` (F15), and bump
     `DATA_VERSION` on both sides.
4. **Sim-time stamping** (F2): an MSFS `SIMULATION TIME` field and X-Plane flight-loop time. This is
   local only, with no wire change.
5. **`OffsetClock`:**
   - an NTP-style exchange on JFP2 Pulse classes 5/6, on the network thread, published in
     `Network.Snapshot`;
   - a passive minimum-filter offset for legacy-only peers;
   - filtered and ageing RTT as the last fallback.
6. **`CoordinatedTurn` estimator and a PD/blending steering.**
7. **Sender DR threshold** for close peers.
8. **`IMM` estimator** and control-input lead (experimental plugins).

### 7.1 Phase 1 as built

**Code in `JoinFS/Estimation/`** (plain C#, compiled in every configuration):

- `KinematicState` and `PeerTiming`.
- **The clock.** `IClockModel`, with `RttHalfClock`. It takes the old `netRealTime`/`netSimTime`/
  `prevDelay` logic and `TIME_ERROR_RATE` from `Sim`.
- **The estimator.** `IStateEstimator`, with `ClassicEstimator` (`Pos.Extrapolate`/`Vel.Extrapolate`
  and the ±2 s limit).
- **The steering.** `ISteeringLaw` returns a `SteeringCommand`, and `ClassicSteering` implements it.
- `EstimationRegistry` creates each part by name and falls back to the defaults.

**Wiring in `Sim`:**

- `Sim.Obj` owns a clock and an estimator, created lazily. Snapshot copies (`CloneView`) get their
  own, so the UI thread never touches the sim thread's instances.
- `Sim.UpdateObject(obj, netTime, receivedAt)` feeds both.
- `Sim.UpdateSimObjectVelocity` runs clock → estimator → steering law. It then applies the command
  through SimConnect (`ApplySteering`), keeping the old order of SetData calls.
- `ResetObject` and entering a cockpit reset or copy the clock, as they did the old fields.
- The sender-side writes of `netSimTime` were removed: every switch to remote control goes through
  `ResetObject` first, so they were never read.

**Where it departs from §4, and why:**

- **The clock model is per object, not per peer.** The old timing state and RTT filter were per
  object, so this reproduces them exactly. A per-peer `OffsetClock` (phase 5) can sit behind the
  same interface, with each object's clock reading the shared peer offset.
- **The steering is a pure law returning a command**, not `Steer(obj, …)` making SimConnect calls.
  That makes it testable without a simulator, and keeps every SimConnect call inside `Sim`.

**The one deliberate difference:** `main.ElapsedTime` is now read once per frame instead of twice.
The difference is sub-microsecond.

**Tests in `JoinFS.Tests/Estimation/`:**

- `ClassicEquivalenceTests` hold a verbatim copy of the old code as a frozen reference. Over 20 000
  random states per build variant (MSFS and others) and 50 000-step clock sequences, the new parts
  give bit-identical SimConnect calls and sample ages.
- The tests assert that every branch was reached: hold, reset, track, and track with attitude.
- A 1e-7 mutation of the catch-up gain, or of the 0.52 delay factor, makes them fail.
- `EstimationWiringTests` cover the registry, one instance per object, and snapshot isolation.

**Not changed:** the X-Plane plugin's own estimator (phase 3).

### 7.2 Phases 3 and 4 as built (2026-10-03)

**`ClassicFixed`** ([ClassicFixedEstimator.cs](../JoinFS/Estimation/ClassicFixedEstimator.cs)) is
Classic with two fixes:

- **F4.** The attitude follows the body rates through
  [Attitude.cs](../JoinFS/Estimation/Attitude.cs). It integrates a quaternion, which is exact for
  constant body rates and has no singularity at ±90° pitch. Heading and bank stay continuous
  across the 0/360 line.
- **F3.** The acceleration moves the position by ½·a·t².

It has been the default since 2026-10-04, after the field test in §6.4; `-estimator Classic` selects
the original, which stays as the frozen reference for the tests. It is untested in the field on FSX
and P3D, which share the code but not the per-frame attitude steering. On FS2020/2024 the steering sets the predicted
attitude every frame, so the fix shows directly in the drawn aircraft. The tester package starts
`ClassicFixed`.

**Sim-time stamps** ([SimClockStamper.cs](../JoinFS/Estimation/SimClockStamper.cs), F2):

- **Reading the clock.** On FS2020/2024, `AIRCRAFT_POSITION` also reads `SIMULATION TIME`. It is
  registered as `AircraftPositionTimed`, so `AircraftPosition` itself does not change: the recorder
  and the X-Plane link still use it.
- **Stamping.** Each own-aircraft sample is stamped with the simulator time plus the smallest
  (handled − simulator time) of the last second. That is the least-delayed samples' offset, so
  the dispatch jitter drops out. A stamp is never later than the handling time, and stamps only
  move forward.
- **Falling back to the handling time.** It does that when the simulator's clock stands still
  (pause), and when it jumps back or falls behind ours by more than 40 ms (a reload, a hitch, the
  end of a pause). From there it starts over.
- **Other simulators and `-dispatchtime`.** FSX, P3D and X-Plane keep the old stamps. So does any
  build started with `-dispatchtime`.
- **Wire and receivers.** No wire change: `NetTime` is still the sender's `ElapsedTime`, so
  receivers of any version benefit.

**Still open from phases 3 and 4:**

- F6, the per-frame RTT filter, which `MinOffsetClock` replaces (§7.3);
- F9, which the SDK documentation settles (F9 row in §2);
- the X-Plane quick win (F13, F14, F15) and X-Plane's sim-time stamps;
- a sim rate other than 1×. The stamper copes, falling back or starting over, but does not use
  the rate.

### 7.3 Phase 5, the passive clock, as built (2026-10-05)

[MinOffsetClock.cs](../JoinFS/Estimation/MinOffsetClock.cs), `-clock MinOffset`. `RttHalf` stays
the default until a field session confirms it; the tester package starts `MinOffset`.

- **Offset.** Arrival minus stamp of each sample goes into 10 buckets of 1 s; the smallest is the
  offset plus the least delay.
- **Round trip.** The network side's value goes into 12 buckets of 5 s; half the smallest,
  ignoring 0 and 9999, is the least one-way delay. Both windows are a few numbers, so they are
  cheap to read every frame and to copy.
- **Age** = time since the newest sample arrived + how much later than the fastest it was + the
  least one-way delay. Without a link (recorder playback, local updates) the last term is 0.
- **Restart.** When ten samples in a row arrive more than 0.3 s later than the fastest of the
  window (the sender's clock stepped, or its sim restarted), the window starts over. A step the
  other way takes effect at once.
- **Per object**, like `RttHalf`, behind the same `IClockModel`. `Reset` forgets the sender but not
  the round trip; `CopyFrom` copies it all.
- **Tests:** `MinOffsetClockTests` (steady stream, a late sample, minimum not average, a permanent
  delay forgotten after the window, a clock step, unknown round trips, playback, reset, copy).
- **Not built:** the NTP-style exchange on JFP2 Pulse classes 5/6.

## 8. Critical files

- [Sim.Steering.cs](../JoinFS/Sim.Steering.cs): becomes a thin caller.
- [Sim.ObjectLifecycle.cs](../JoinFS/Sim.ObjectLifecycle.cs): `UpdateObject(netTime…)` feeds the
  estimator.
- [Sim.Data.cs](../JoinFS/Sim.Data.cs): `Pos.Extrapolate`/`Vel.Extrapolate` stay for `Classic` only.
- [Sim.Object.cs](../JoinFS/Sim.Object.cs): estimator field; `prevDelay` moves into the clock model.
- [SimConnectInterface.cs](../JoinFS/SimConnectInterface.cs): sim-time field; F9.
- [SimMessageMapper.cs](../JoinFS/Session/SimMessageMapper.cs) and the JFP2 Position schema: new
  fields.
- [Recorder.cs](../JoinFS/Recorder.cs): `RecorderClock`; optional dense recording.
- [XPlane.cs](../JoinFS/XPlane.cs) and [JoinFS-XP.cpp](../JoinFS-XP/JoinFS-XP.cpp) / `Link.cpp`:
  - 20 Hz sender, drain the link every frame;
  - later, C# sends the predicted target state and the plugin becomes an `IObjectSteering`.
  Both need a `DATA_VERSION` bump on both sides.
- [MeshManager.cs](../JoinFS/Net/Core/MeshManager.cs) / [NetworkSnapshot.cs](../JoinFS/Net/Service/NetworkSnapshot.cs):
  publish filtered RTT and clock offset.
- [Jfp2Plugin.cs](../JoinFS/Net/Protocols/Jfp2/Jfp2Plugin.cs) / [Envelope.cs](../JoinFS/Net/Protocols/Jfp2/Envelope.cs):
  the timing exchange (reserved Pulse classes 5/6).
- New: `JoinFS/Estimation/*`, `JoinFS.Tests/Estimation/*`.

Constraints:

- The legacy codecs are untouched.
- Every SimConnect call stays on the sim thread.
- The network side only publishes timing through `Network.Snapshot`.
- When the plugin's data protocol changes, bump `DATA_VERSION` in both `XPlane.cs` and
  `JoinFS-XP/Link.cpp`.

## 9. Verification

- `dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug -p:Platform=x64`. Phase 1 must keep
  all tests green and add golden tests showing `Classic` equals the old code.
- Run the offline harness and compare error tables per estimator (§6).
- Build every configuration (`FS2024, FS2020, FSX, P3D, XPLANE, CONSOLE`) to catch `#if` breakage.
- Field test: two clients in formation, direct and through the minion hub, with shadow-mode logs
  checked with `check-logs`.
- For X-Plane: `JoinFS/util/fake_xplane_plugin.py` for the link, and the real plugin if the data
  protocol changes.
