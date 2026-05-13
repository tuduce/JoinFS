# Positioning Improvements Specification
## Target file: `JoinFS/Sim.cs`

This document specifies four improvements to `UpdateSimObjectVelocity` and related code in `Sim.cs`.
All changes are self-contained and do not require protocol or file-format changes.

---

## Context

The central method is `UpdateSimObjectVelocity(Obj obj)`, called every simulator frame from
`ProcessEventFrame`. It is responsible for driving each injected object's position and orientation
toward the latest known network/recorder state by manipulating the SimConnect velocity variables.

The key state on `Obj` that this method reads and writes:

| Field | Meaning |
|---|---|
| `obj.simPosition` | Last position read back from the simulator |
| `obj.simTime` | Local `ElapsedTime` when `simPosition` was last updated |
| `obj.netPosition` | Last position received from network/recorder |
| `obj.netVelocity` | Last velocity received from network/recorder |
| `obj.netStateTime` | Remote clock value of last network/recorder update |
| `obj.netRealTime` | Locally-estimated current remote clock value |
| `obj.netSimTime` | Local `ElapsedTime` when `netRealTime` was last updated |
| `obj.owner` | `Obj.Owner.Network` or `Obj.Owner.Recorder` |
| `obj.prevDelay` | Low-pass filtered RTT for this node (network only) |

---

## Improvement 2 — Separate Recorder Playback from Network Playback

### Problem

`UpdateObject(Obj obj, double netTime)` merges the remote clock into a local estimate using a
low-pass filter (`TIME_ERROR_RATE = 0.02`). This is correct for network objects where jitter
from variable network latency is real. For recorder objects the timestamp is a precise local
clock value recorded on the same machine — there is no jitter and no one-way propagation delay.
Applying the same filter and the same `0.52 * delay` RTT compensation to recorder objects
introduces artificial lag and error.

Additionally `UpdateSimObjectVelocity` unconditionally reads and filters the RTT:

```csharp
float delay = 0.0f;
if (obj.owner == Obj.Owner.Network)
{
    float prevDelay = obj.prevDelay;
    delay = main.network.localNode.GetNodeRTT(obj.ownerNuid);
    float alpha = 0.75f;
    delay = alpha * delay + (1.0f - alpha) * prevDelay;
    obj.prevDelay = delay;
}
// ...
double netDeltaTime = obj.netRealTime - obj.netStateTime + main.ElapsedTime - obj.netSimTime + 0.52 * delay;
```

The `delay` variable is already zero for recorder objects because of the `if` guard, so
`netDeltaTime` is already correct there. The real problem is in `UpdateObject(Obj, double)`.

### Change 1 — Skip clock-merge for recorder objects

In `UpdateObject(Obj obj, double netTime)`, the `else` branch that applies `TIME_ERROR_RATE`
should be skipped entirely for recorder objects. For recorder objects `netTime` IS the precise
playback clock and should be assigned directly.

**Location:** `UpdateObject(Obj obj, double netTime)` — the `else` branch.

**Current code:**
```csharp
else
{
    // update estimated network time
    obj.netRealTime += main.ElapsedTime - obj.netSimTime;
    // calculate error between network update and estimated time
    double error = obj.netStateTime - obj.netRealTime;
    // gradually merge to remove error over time
    obj.netRealTime += error * TIME_ERROR_RATE;
}
```

**Replace with:**
```csharp
else if (obj.owner == Obj.Owner.Recorder)
{
    // Recorder timestamps are precise local clock values - no jitter to filter.
    // Assign the remote time directly so netRealTime tracks the playback cursor exactly.
    obj.netRealTime = obj.netStateTime;
}
else
{
    // update estimated network time
    obj.netRealTime += main.ElapsedTime - obj.netSimTime;
    // calculate error between network update and estimated time
    double error = obj.netStateTime - obj.netRealTime;
    // gradually merge to remove error over time
    obj.netRealTime += error * TIME_ERROR_RATE;
}
```

### Change 2 — Skip RTT compensation for recorder objects in `UpdateSimObjectVelocity`

The `netDeltaTime` calculation already excludes the delay for recorder objects (delay stays 0.0f).
Add an explanatory comment and assert this intent explicitly to avoid future regression.

**Location:** In `UpdateSimObjectVelocity`, after the delay calculation block, replace the comment
on the `netDeltaTime` line:

**Current code:**
```csharp
// delay is measured round-trip, so divide by two
double netDeltaTime = obj.netRealTime - obj.netStateTime + main.ElapsedTime - obj.netSimTime + 0.52*delay;
```

**Replace with:**
```csharp
// For network objects: compensate for one-way propagation (RTT / ~2).
// For recorder objects: delay is 0 and netRealTime == netStateTime, so this reduces to
//   (main.ElapsedTime - obj.netSimTime), the time since the last recorder update was applied.
double netDeltaTime = obj.netRealTime - obj.netStateTime + main.ElapsedTime - obj.netSimTime + 0.52 * delay;
```

No code change is needed here, only the comment. The behaviour is already correct because
`delay` is 0 for recorder objects. The comment documents the intent.

---

## Improvement 3 — Adaptive Error-Correction Gain (PD Controller)

### Problem

The catch-up gain is hardcoded at `* 1.5` for both linear and angular corrections:

```csharp
netVelocity.linear += deltaGeo * 1.5;
// ...
netVelocity.angular += deltaAngles * 1.5;
```

This is too aggressive for small errors (causes overshoot and oscillation around the target)
and too weak for errors near the 50 m reset threshold (the aircraft visibly lags before teleport).
A proportional gain that scales with error magnitude, combined with a derivative term that
counteracts overshoot, produces smoother convergence at all error sizes.

### Change — Replace fixed gain with a PD controller

**Location:** The `else` branch of the distance/altitude check in `UpdateSimObjectVelocity`
(the branch that handles errors smaller than 50 m / `altitudeDeltaLimit`).

The derivative term is approximated by subtracting a fraction of the current velocity component
along the error direction, which damps overshoot without requiring state from the previous frame.

**Current code (the soft-correction block):**
```csharp
// get world space relative position
Vector deltaGeo = new(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
// get delta between current and network orientations
Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

// add delta to velocity to catch up
netVelocity.linear += deltaGeo * 1.5;

// only catch up the orientation if no high angular turns are being made
if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
{
    if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
    {
        // add delta to angular velocity to catch up
        netVelocity.angular += deltaAngles * 1.5;
    }
    // ...
}
```

**Replace the gain lines only** (preserve all surrounding structure, the Euler SetData calls,
and the aerobatics guards):

```csharp
// get world space relative position
Vector deltaGeo = new(distance * Math.Sin(bearing), netPosition.geo.y - simPosition.geo.y, distance * Math.Cos(bearing));
// get delta between current and network orientations
Vector deltaAngles = Vector.AnglesDelta(simPosition.angles, netPosition.angles);

// Proportional gain scales with positional error: gentle near zero, stronger at larger errors.
// Clamped to avoid over-correcting close to the hard-reset threshold.
// Derivative term subtracts a fraction of current velocity along the error direction to
// damp overshoot and prevent oscillation around the target position.
double posGain = Math.Clamp(0.8 + distance * 0.03, 0.8, 2.5);
const double posDerivative = 0.25;
netVelocity.linear += deltaGeo * posGain - netVelocity.linear * posDerivative;

// only catch up the orientation if no high angular turns are being made
if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
{
    if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
    {
        // Angular gain scales with orientation error magnitude.
        double angErrorMag = Math.Sqrt(deltaAngles.x * deltaAngles.x + deltaAngles.y * deltaAngles.y + deltaAngles.z * deltaAngles.z);
        double angGain = Math.Clamp(0.8 + angErrorMag * 1.5, 0.8, 2.5);
        // add delta to angular velocity to catch up
        netVelocity.angular += deltaAngles * angGain;
    }
    // ...
}
```

### Gain parameter rationale

| Parameter | Value | Reasoning |
|---|---|---|
| `posGain` base | 0.8 | At zero error the gain is low, reducing steady-state oscillation |
| `posGain` slope | 0.03 per metre | At 50 m error the gain reaches 2.3, close to the old fixed 1.5 but stronger where needed |
| `posGain` clamp max | 2.5 | Prevents overcorrection just below the 50 m hard-reset threshold |
| `posDerivative` | 0.25 | Subtracts 25% of the current velocity, damping but not over-damping |
| `angGain` base | 0.8 | Same rationale as positional gain |
| `angGain` slope | 1.5 per radian | Angular errors are in radians, so 1 rad ? 57° gives a large response |
| `angGain` clamp max | 2.5 | Symmetrical with positional gain |

---

## Improvement 4 — Quaternion-Based Orientation (Gimbal Lock Fix)

### Problem

All orientation arithmetic uses Euler angles (pitch, heading, bank stored as `Vector.x/y/z`).
`Vector.AnglesDelta` computes the per-axis difference modulo 2?. This breaks down when an aircraft
is in a high bank or pitch attitude because Euler angles have singularities (gimbal lock), and
the `AnglesDelta` result can flip sign or have large discontinuities. The code works around this
with guards:

```csharp
if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
```

These guards disable angular correction during aerobatics, leaving orientation to drift until
the hard reset fires. Using quaternion SLERP for the final orientation sent to SimConnect
eliminates the singularity entirely.

### Approach

`System.Numerics.Quaternion` is available in .NET 8 without any additional packages.
The change is **only in `UpdateSimObjectVelocity`**, in the two places where `OBJECT_EULER` is
set. The `netVelocity.angular` correction can remain Euler-based since its values are small
(it is a velocity, not an absolute angle). Only the **absolute orientation target** needs
quaternion interpolation.

### Prerequisite — Add two static helper methods to `Sim`

Add these two private static methods inside the `Sim` class, near the top of the `#region Object`
section or alongside `ConvertToAxis`/`ConvertFromAxis`:

```csharp
/// <summary>
/// Convert Euler angles (pitch, heading, bank in radians) to a quaternion.
/// The convention matches SimConnect: pitch = X, heading/yaw = Y, bank/roll = Z.
/// </summary>
static System.Numerics.Quaternion EulerToQuat(Vector angles)
{
    // System.Numerics uses yaw=Y, pitch=X, roll=Z
    return System.Numerics.Quaternion.CreateFromYawPitchRoll(
        (float)angles.y,
        (float)angles.x,
        (float)angles.z);
}

/// <summary>
/// Convert a quaternion back to Euler angles (pitch, heading, bank in radians).
/// </summary>
static Vector QuatToEuler(System.Numerics.Quaternion q)
{
    // Normalise to avoid numerical drift
    q = System.Numerics.Quaternion.Normalize(q);

    // Heading (yaw, Y)
    float siny_cosp = 2.0f * (q.W * q.Y + q.X * q.Z);
    float cosy_cosp = 1.0f - 2.0f * (q.Y * q.Y + q.X * q.X);
    double heading = Math.Atan2(siny_cosp, cosy_cosp);

    // Pitch (X)
    float sinp = 2.0f * (q.W * q.X - q.Z * q.Y);
    double pitch = Math.Abs(sinp) >= 1.0f
        ? Math.CopySign(Math.PI / 2.0, sinp)
        : Math.Asin(sinp);

    // Bank (roll, Z)
    float sinr_cosp = 2.0f * (q.W * q.Z + q.Y * q.X);
    float cosr_cosp = 1.0f - 2.0f * (q.Z * q.Z + q.Y * q.Y);
    double bank = Math.Atan2(sinr_cosp, cosr_cosp);

    return new Vector(pitch, heading, bank);
}
```

### Change — Replace direct Euler assignment with SLERP in `UpdateSimObjectVelocity`

There are **four** sites in `UpdateSimObjectVelocity` where `OBJECT_EULER` is set and
`obj.simPosition.angles` is updated. Each one must be replaced with a SLERP version.
The SLERP `t` parameter controls how aggressively to snap: use `1.0f` (snap immediately)
in the hard-reset branch because we are teleporting anyway, and use a softer value like `0.5f`
in the soft-correction branch so orientation changes are smoothed over two frames.

**Site 1 — paused branch** (snap to target, use `t = 1.0f`):

```csharp
// Current:
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(obj.netPosition.angles));
obj.simPosition.angles = obj.netPosition.angles.Clone();

// Replace with:
var qPausedSim = EulerToQuat(obj.simPosition.angles);
var qPausedNet = EulerToQuat(obj.netPosition.angles);
var qPausedResult = System.Numerics.Quaternion.Slerp(qPausedSim, qPausedNet, 1.0f);
Vector pausedAngles = QuatToEuler(qPausedResult);
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(pausedAngles));
obj.simPosition.angles = pausedAngles;
```

**Site 2 — hard-reset branch** (also snap, use `t = 1.0f`):

```csharp
// Current:
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
obj.simPosition.angles = netPosition.angles.Clone();

// Replace with:
var qResetSim = EulerToQuat(obj.simPosition.angles);
var qResetNet = EulerToQuat(netPosition.angles);
var qResetResult = System.Numerics.Quaternion.Slerp(qResetSim, qResetNet, 1.0f);
Vector resetAngles = QuatToEuler(qResetResult);
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(resetAngles));
obj.simPosition.angles = resetAngles;
```

**Site 3 — soft-correction branch, inside the aerobatics guard (`angles.x < PI*0.25`)** (smooth, use `t = 0.5f`):

```csharp
// Current:
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
obj.simPosition.angles = netPosition.angles.Clone();

// Replace with:
var qSoftSim = EulerToQuat(simPosition.angles);
var qSoftNet = EulerToQuat(netPosition.angles);
var qSoftResult = System.Numerics.Quaternion.Slerp(qSoftSim, qSoftNet, 0.5f);
Vector softAngles = QuatToEuler(qSoftResult);
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(softAngles));
obj.simPosition.angles = softAngles;
```

**Site 4 — soft-correction branch, the aerobatics `else` (high pitch/bank)** (smooth but faster, use `t = 0.7f`):

```csharp
// Current:
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(netPosition.angles));
obj.simPosition.angles = netPosition.angles.Clone();

// Replace with:
var qAeroSim = EulerToQuat(simPosition.angles);
var qAeroNet = EulerToQuat(netPosition.angles);
var qAeroResult = System.Numerics.Quaternion.Slerp(qAeroSim, qAeroNet, 0.7f);
Vector aeroAngles = QuatToEuler(qAeroResult);
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(aeroAngles));
obj.simPosition.angles = aeroAngles;
```

### Removing the aerobatics guard on angular velocity correction

Because orientation is now handled correctly through SLERP at all attitudes, the guard that
prevents angular velocity correction during aerobatics can be relaxed. The inner `if` that
checks `netVelocity.angular` magnitudes should remain (it prevents fighting a high angular
velocity), but the outer pitch/bank attitude guard can be removed:

**Current structure:**
```csharp
if (Math.Abs(simPosition.angles.x) < Math.PI * 0.25 && Math.Abs(simPosition.angles.z) < Math.PI * 0.5)
{
    if (Math.Abs(netVelocity.angular.x) < 0.2 && ...)
    {
        netVelocity.angular += deltaAngles * angGain; // (gain from Improvement 3)
    }
    // SLERP site 3
}
else
{
    // SLERP site 4
}
```

**Replace with:**
```csharp
// Angular velocity correction: apply when no rapid spin is in progress.
// The outer attitude guard is no longer needed because SLERP handles all attitudes correctly.
if (Math.Abs(netVelocity.angular.x) < 0.2 && Math.Abs(netVelocity.angular.y) < 0.2 && Math.Abs(netVelocity.angular.z) < 0.2)
{
    netVelocity.angular += deltaAngles * angGain; // angGain from Improvement 3
}

// Set orientation via SLERP - works correctly at all pitch/bank attitudes
bool inAerobatics = Math.Abs(simPosition.angles.x) >= Math.PI * 0.25 || Math.Abs(simPosition.angles.z) >= Math.PI * 0.5;
float slerpT = inAerobatics ? 0.7f : 0.5f;
var qSlerpSim = EulerToQuat(simPosition.angles);
var qSlerpNet = EulerToQuat(netPosition.angles);
var qSlerpResult = System.Numerics.Quaternion.Slerp(qSlerpSim, qSlerpNet, slerpT);
Vector slerpAngles = QuatToEuler(qSlerpResult);
simconnect.SetData(Definitions.OBJECT_EULER, obj.simId, new ObjectEuler(slerpAngles));
obj.simPosition.angles = slerpAngles;
```

This eliminates Sites 3 and 4 as separate cases and replaces them with a single SLERP block.

---

## Improvement 5 — Ground State Hysteresis

### Problem

On FS2020/FS2024 the altitude reset threshold tightens to 0.2 m when on the ground:

```csharp
#if (FS2020 || FS2024)
if (simPosition.ground != 0) altitudeDeltaLimit = 0.2;
#endif
```

Because SimConnect reports ground altitude with sub-centimetre noise, and because the sim
may "glue" the aircraft to the terrain surface at a slightly different altitude than the
network value, this threshold fires on almost every frame during taxi, producing a rapid
series of hard teleports that appear as stuttering.

The fix is to require the altitude error to exceed the threshold for **N consecutive frames**
before triggering a hard reset. This is hysteresis: the state must be persistently outside
the boundary before we act on it.

### Change 1 — Add `groundResetFrameCount` field to `Obj`

Add the following field to the `Obj` class, near the other positioning fields (`paused`, `positionCount`, etc.):

```csharp
/// <summary>
/// Number of consecutive frames the ground altitude error has exceeded the reset threshold.
/// Used to implement hysteresis and prevent taxi stuttering on FS2020/FS2024.
/// </summary>
public int groundResetFrameCount = 0;
```

### Change 2 — Add a constant for the hysteresis frame count

Add the following constant to the `#region Constants` section of `Sim`:

```csharp
/// <summary>
/// Number of consecutive frames the ground altitude error must exceed the threshold
/// before a hard position reset is triggered. Prevents taxi stuttering on FS2020/FS2024.
/// </summary>
const int GROUND_RESET_HYSTERESIS_FRAMES = 3;
```

### Change 3 — Replace the immediate ground reset with a hysteresis check

**Location:** In `UpdateSimObjectVelocity`, inside the `#if (FS2020 || FS2024)` block that
sets `altitudeDeltaLimit`.

**Current code:**
```csharp
// largest difference in altitude before reset
double altitudeDeltaLimit = 50.0;
#if (FS2020 || FS2024)
// FS2020 has an issue where the aircraft remains glued to the ground, so reset much earlier when the altitude diverts on the ground
if (simPosition.ground != 0) altitudeDeltaLimit = 0.2;
#endif

// check if object is beyond specific distance
if (distance > 50.0 || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit)
{
    // hard reset ...
}
```

**Replace with:**
```csharp
// largest difference in altitude before reset
double altitudeDeltaLimit = 50.0;

#if (FS2020 || FS2024)
// FS2020/2024 glues injected aircraft to the terrain surface at a slightly different altitude
// than the network value. The tight threshold (0.2 m) is necessary to unstick the aircraft,
// but firing every frame during taxi causes visible stuttering.
// Solution: require the error to persist for GROUND_RESET_HYSTERESIS_FRAMES frames before
// triggering the hard reset (hysteresis).
bool groundAltitudeError = simPosition.ground != 0 && Math.Abs(simPosition.geo.y - netPosition.geo.y) > 0.2;
if (groundAltitudeError)
{
    obj.groundResetFrameCount++;
}
else
{
    obj.groundResetFrameCount = 0;
}
bool groundResetRequired = obj.groundResetFrameCount >= GROUND_RESET_HYSTERESIS_FRAMES;
#else
const bool groundResetRequired = false;
#endif

// check if object is beyond specific distance or persistently out of altitude on the ground
if (distance > 50.0 || Math.Abs(simPosition.geo.y - netPosition.geo.y) > altitudeDeltaLimit || groundResetRequired)
{
#if (FS2020 || FS2024)
    // reset the counter now that we are acting on it
    obj.groundResetFrameCount = 0;
#endif
    // hard reset ...
    // (existing hard-reset code unchanged)
}
```

### Change 4 — Reset `groundResetFrameCount` when the object is reset

In `ResetObject(Obj obj)`, add a reset of the new field alongside the existing time resets:

**Current code:**
```csharp
public static void ResetObject(Obj obj)
{
    if (obj != null)
    {
        obj.netStateTime = 0.0;
        obj.netRealTime = 0.0;
        obj.netSimTime = 0.0;
    }
}
```

**Replace with:**
```csharp
public static void ResetObject(Obj obj)
{
    if (obj != null)
    {
        obj.netStateTime = 0.0;
        obj.netRealTime = 0.0;
        obj.netSimTime = 0.0;
        obj.groundResetFrameCount = 0;
    }
}
```

---

## Implementation Order

The improvements are independent and can be implemented one at a time. Recommended order:

1. **Improvement 5** (ground hysteresis) — lowest risk, highest immediate user-visible benefit
   for MSFS2020/2024 users. Only adds a counter field and wraps an existing `if` block.
2. **Improvement 2** (recorder vs network timing) — low risk, two-line logic change plus a
   comment. Improves recorder playback accuracy without affecting network behaviour.
3. **Improvement 3** (adaptive PD gain) — medium risk, replaces two multiplication lines with
   a formula. Test with a slow aircraft (Cessna) and a fast aircraft (jet) at various distances.
4. **Improvement 4** (quaternion SLERP) — highest complexity, adds two helper methods and
   replaces four `OBJECT_EULER` SetData sites. Test with aerobatics (full rolls, loops) and
   verify no regression in straight-and-level cruise orientation.

## Testing Checklist

After implementing each improvement, verify:

- [ ] Straight-and-level cruise: injected aircraft tracks smoothly, no oscillation
- [ ] Taxiing on MSFS2020/2024: no rapid stuttering (Improvement 5)
- [ ] Recorder playback: aircraft follows recorded path without clock drift artefacts (Improvement 2)
- [ ] Large positional error (move aircraft > 50 m manually): hard reset fires correctly
- [ ] Small positional error (< 5 m): smooth catch-up, no overshoot oscillation (Improvement 3)
- [ ] Aerobatics (full roll, loop, inverted flight): orientation tracks correctly without
    attitude-lock artefacts (Improvement 4)
- [ ] Paused object: stays pinned to `netPosition` without drift
- [ ] Formation flight (< 30 m separation): no aggressive correction that pushes aircraft away
