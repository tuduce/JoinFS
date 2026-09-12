# JoinFS Recording Protocol

This document describes the on-disk format JoinFS uses for `.jfs` recording files — the "record a session, play it back later" feature exposed by the Recorder window. It is derived directly from `JoinFS/Recorder.cs`, the shared struct read/write code in `JoinFS/Sim.cs`, `JoinFS/Forms/RecorderForm.cs` and `JoinFS/Forms/MainForm.cs`, plus a from-scratch independent reader implementation in the companion `RecordingXRay` tool (`RecordingXRay/RecordingReader.cs`), which was useful cross-referencing and also exposes one of the compatibility problems documented below. Line numbers are approximate and will drift as the code evolves; they're accurate as of this writing (`Sim.VERSION = 21008`).

This document assumes familiarity with `docs/network-protocol.md`, since the recording format deliberately reuses large parts of the network protocol's wire encoding (the same `BinaryWriter`/`BinaryReader` struct serializers are called from both). Where the two formats share code, this document says so instead of repeating the field tables.

## 1. Overview

A recording captures the position, control, event and custom-variable history of every simulator object the user has flagged "record" (`simObject.record`), for later local playback. It has nothing to do with the network layer at transport level — it is a private file format, written and read entirely client-side by `Recorder.cs` — but its payload structures for positions and variables are literally the same `Sim.Write`/`Sim.Read` overloads the network code uses to serialize `ObjectPosition`/`AircraftPosition`/`*Variables` messages onto the wire (§5 of the network protocol document). A recording is, in effect, "a saved sequence of the same structs that would otherwise have gone out over UDP," plus per-object identity/model metadata and per-frame timestamps.

Key properties:

- **Container:** a single file, conventionally named with the `.jfs` extension (`RecorderForm.cs`: `Filter = "JoinFS files (*.jfs)|*.jfs"`), written with a plain `BinaryWriter` directly over a `FileStream` — there is no wrapping container format (no ZIP, no JSON manifest, no separate header file).
- **Encoding:** little-endian binary via `BinaryWriter`/`BinaryReader`, identical string encoding to the network protocol (7-bit length-prefixed UTF-8, i.e. `.Write(string)`/`.ReadString()`).
- **Versioning:** a single 16-bit version number at the very start of the file, reusing the *same* monotonically increasing counter as the network protocol (`Sim.VERSION`, currently `21008`) — there is no separate "recording format version." Every version-gated read inside the file (frame fields, per-object trailing fields) is conditioned on this one number, using the same `if (version >= N) ... else <default>` idiom documented in network-protocol.md §5.
- **No checksum, no length framing, no magic number.** Unlike the network protocol, where a corrupt/misread UDP datagram only costs one dropped message, a recording is one continuous stream: a single misaligned read anywhere in the file desyncs every byte read after it for the rest of the file. This makes the file format considerably less forgiving of the version-compatibility mistakes network-protocol.md warns about (§9.2), and two such mistakes already exist in the current code — see §7.
- **Read/write symmetry lives entirely in one class.** All of it — writer, reader, in-memory model, playback interpolation — is `Recorder.cs`; there is no separate "recording protocol" module the way `Network.cs`/`Node.cs` are split out from the rest of the app.

## 2. File-level layout

`Recorder.Write(BinaryWriter writer)` produces, and `Recorder.Read(BinaryReader reader)` / `Recorder.Read1` consume, this top-level shape:

| Order | Field | Type | Description |
|---|---|---|---|
| 1 | `Version` | `short` | The writer's `Sim.VERSION` at save time. Read first, unconditionally, by `Recorder.Read()`. If `version < 10022`, the whole file is rejected outright (`Resources.Strings.OldRecording` is shown to the user and nothing further is read) — this is the recording format's equivalent of the network protocol's `dataVersion < 10014` floor (§5 of network-protocol.md), except here **the whole file** is the unit of rejection, not a single message. |
| 2 | `AircraftCount` | `int32` | Count of aircraft records that follow. |
| 3 | `Aircraft[AircraftCount]` | `Aircraft` records | See §3.2. Written first, in `objList` order, for every recorded object that is an `Aircraft` (i.e. a user- or AI-controlled plane/helicopter/boat/vehicle with a callsign). |
| 4 | `ObjectCount` | `int32`, **optional** | Count of non-aircraft object records that follow — **only present if there are any**. |
| 5 | `Object[ObjectCount]` | `Obj` records, **optional** | See §3.1. Every recorded object that is *not* an `Aircraft` (a plain scenery/AI object with no callsign). |

Fields 4–5 are gated by **EOF-sensing**, not a version check: `Recorder.Read1` reads the aircraft array, then does `if (reader.PeekChar() != -1) { read object count + objects }`. This is the same "if there are any bytes left, there's another field" idiom network-protocol.md §5 documents for trailing optional network-message fields — reused here for an entire top-level *section* rather than a single field. It works today because there is exactly one such optional trailing section. See §7.3 and §8.2 for why this is a trap if a second one is ever added.

There is no overall file length, item count checksum, or footer — the file simply ends after the last object's data (or after the aircraft array, if there are no plain objects and the writer happened to omit the now-always-written object count — see the note in §3.1's `Write`, which currently writes the count unconditionally as `0` in that case, so in practice field 4 is *always* present as of the current code; only field 5 (the array itself) is truly empty).

## 3. Recorded object records

### 3.1 `Obj` — a non-aircraft recorded object

Written/read by `Recorder.Obj.Write` / `Recorder.Obj.Read1`:

| Order | Field | Type | Version gate |
|---|---|---|---|
| 1 | `Model` | `string` | always |
| 2 | `TypeRole` | `byte` (see §6 for the `Substitution.TypeRole_*` values) | always |
| 3 | `FrameCount` | `int32` | always |
| 4 | `Frame[FrameCount]` | frame records, see §4 | always |
| 5 | `Livery` | `string` | **`FS2024` builds only**, and only if `version >= 21004` on read |
| 6 | `IcaoType` | `string` | if `version >= 21004` (non-`FS2024` builds) or `>= 21005` (`FS2024` builds) |
| 7 | `IcaoAirline` | `string` | same gate as `IcaoType` |

Field 5 is gated by a **compile-time** `#if FS2024` directive in addition to the runtime version check — see §7.1 for why this is a real cross-build compatibility hazard, not just a documentation footnote.

Fields not persisted at all: `owner` (`Sim.Obj.Owner` — `Me`/`Network`/`Sim`/`Recorder`; this is transient runtime state re-derived at load time, not saved), `id` (reassigned from `Obj.nextId` on load), `playing`/`frameIndex`/`timeOffset*`/`playbackAngles*` (all playback/recording runtime state).

### 3.2 `Aircraft` — a recorded aircraft (extends `Obj`)

`Aircraft.Write` writes three fields of its own, then delegates to `Obj.Write` (`base.Write(writer)`) for the rest, so the on-wire order interleaves as:

| Order | Field | Type | Version gate |
|---|---|---|---|
| 1 | `Plane` | `bool` | always (`true` for a fixed-wing aircraft, `false` for helicopter/boat/ground vehicle recorded via the `Aircraft` path) |
| 2 | `Callsign` | `string` | always |
| 3 | `Nickname` | `string` | always — the node nickname of whoever owned the aircraft at record time (`main.network.GetNodeName(...)`), not the pilot's callsign |
| 4 | `Model` | `string` | always (same field as `Obj.Model`, via `base.Write`) |
| 5 | `TypeRole` | `byte` | always |
| 6 | `FrameCount` | `int32` | always |
| 7 | `Frame[FrameCount]` | frame records, see §4 | always |
| 8 | `Livery` | `string` | `FS2024` builds only, `version >= 21004` |
| 9 | `IcaoType` | `string` | `>= 21004` / `>= 21005` per build, same as §3.1 |
| 10 | `IcaoAirline` | `string` | same gate as `IcaoType` |

Note that `Aircraft.Read1` is a **new** method (`public new void Read1(...)`), not an `override` — it does not call `base.Read1`, it duplicates the whole read sequence itself (the switch/case for frame types differs — see §4). This is a maintenance hazard independent of wire compatibility: `Obj.Read1` and `Aircraft.Read1` must be kept in sync by hand for every shared field (`Model`, `TypeRole`, the icaoType/icaoAirline/livery tail); a future edit to one that isn't mirrored in the other will silently produce two different wire layouts for what look like shared fields.

## 4. Frame catalog

Every recorded object owns an ordered `List<Frame>`. `Recorder.FrameType` is a `byte` enum (declaration order = wire value, exactly like the network protocol's `MESSAGE_ID` — see network-protocol.md §9.1 for why declaration order matters):

```
ObjectPosition, AircraftPosition, PlaneState, HelicopterState, AircraftState,
PistonEngineState, TurbineEngineState, ObjectSmoke, AircraftFuel, AircraftPayload,
SimEvent, IntegerVariables, FloatVariables, String8Variables
```

Eight of these fourteen values — `PlaneState`, `HelicopterState`, `AircraftState`, `PistonEngineState`, `TurbineEngineState`, `ObjectSmoke`, `AircraftFuel`, `AircraftPayload` — are declared but have **no producer and no consumer** anywhere in `Recorder.cs`. They are the exact same reserved/unimplemented set as the network protocol's `MESSAGE_ID` placeholders (network-protocol.md §8.12), which makes sense: both enums describe the same eventual "granular systems state" feature that was scaffolded once and never finished on either side of the codebase.

Every frame starts with a fixed 9-byte generic header, written by `Frame.Write`/read by `Frame.Read1`:

| Offset | Size | Field | Description |
|---|---|---|---|
| 0 | 1 | `Type` | `FrameType`, cast to/from `byte`. |
| 1 | 8 | `Time` | `double`. Seconds since the recording started (or since the file's own start, for an *appended* file before its frame times are shifted — see §5). |
| 9 | variable | payload | Type-specific, see below. |

The generic header is always read via a throwaway base `Frame` object first (`Frame baseFrame = new(); baseFrame.Read(version, reader);`), and only *then* does the caller decide, from `baseFrame.type`, which concrete `Frame` subclass to instantiate and hand the stream to for the type-specific payload. This two-step read is the root cause of the bug in §7.2: the type-dispatch `switch` is duplicated (once in `Obj.Read1`, once in `Aircraft.Read1`) and the two switches don't handle the same set of cases.

### 4.1 `ObjectPositionFrame`

Payload is exactly `Sim.Write(BinaryWriter, ref Sim.ObjectPositionVelocity)` / `Sim.Read(short, BinaryReader, ref Sim.ObjectPositionVelocity)` — the *identical* method the network code calls to serialize the velocity portion of a network `ObjectPosition`/`AircraftPosition` message body (network-protocol.md §8.2 lists the same fields under `VelocityXYZ`/`AngularVelocityXYZ`/`AccelerationXYZ`/`Height`/`GroundFlags`). Recorded field order:

| Offset (from payload start) | Size | Field | Version gate |
|---|---|---|---|
| 0 | 8×3 | `Latitude, Longitude, Altitude` | always |
| 24 | 4×3 | `Pitch, Bank, Heading` | always |
| 36 | 4×3 | `VelocityX, VelocityY, VelocityZ` | always |
| 48 | 4×3 | `AngularVelocityX, AngularVelocityY, AngularVelocityZ` | always |
| 60 | 4×3 | `AccelerationX, AccelerationY, AccelerationZ` | always |
| 72 | 4 | `Height` | `version >= 10023`; else `0.0f` |
| 76 | 1 | `GroundFlags` (bit 0 = on ground, bit 1 = sender had elevation correction enabled) | `version >= 10023`; else treated as `0` |

Total payload size is 72 bytes for `version < 10023`, 77 bytes for `version >= 10023` (all recordings made with any currently-shipping build, since the minimum accepted version is 10022 and the very next version already adds these fields — so in practice essentially every file a modern build can open uses the 77-byte form).

Not recorded here at all, unlike the network `ObjectPosition` message: `NetId`, `Model` (recorded once per-object instead, §3.1), `TypeRole` (ditto), the `paused` flag, `Livery`/`IcaoType`/`IcaoAirline`/`ClassCode`/`Wtc`/`ClassCodeConfirmed` (also per-object, and `ClassCode`/`Wtc`/`ClassCodeConfirmed` are not recorded *at all* — see §8.1).

### 4.2 `AircraftPositionFrame`

Payload is `Sim.Write(BinaryWriter, ref Sim.AircraftPosition)` / the matching `Read` — again the exact struct serializer the network protocol uses for `AircraftPosition` messages (network-protocol.md §8.2):

| Offset | Size | Field | Version gate |
|---|---|---|---|
| 0 | 8×3 | `Latitude, Longitude, Altitude` | always |
| 24 | 4×3 | `Pitch, Bank, Heading` | always |
| 36 | 4×9 | `VelocityXYZ, AngularVelocityXYZ, AccelerationXYZ` | always |
| 72 | 2×5 | `Rudder, Elevator, Aileron, BrakeLeft, BrakeRight` — `int16`, fixed-point: `stored = (short)(input * 16384.0)` (a truncating cast, not a rounding one, despite `network-protocol.md`'s "round" wording for the same field), decoded as `value = (float)(int)stored / 16384.0f` | always |
| 82 | 4 | `Elevation` | `version >= 10023`; else `0.0f` |
| 86 | 1 | `GroundFlags` (bit 0 = on ground, bit 1 = elevation correction enabled) | `version >= 10023`; else `0` |
| 87 | 4 | `StaticCgToGround` | `version >= 21008`; else the reader gets `NaN`, deliberately distinguishable from a real 0.0 clearance (same convention as the network message, network-protocol.md §8.2 line for `StaticCgToGround`) |

Total payload is 72 / 87 / 91 bytes depending on which of the two later version gates the file predates. **This field is always written** by `Sim.Write` regardless of the local build's own version floor (it was added unconditionally, "older readers simply don't read it," matching the pattern the network protocol already uses) — so every recording saved by a `21008`+ build includes it, whether or not that particular build variant's own feature work needed it.

Not recorded here, unlike the network `AircraftPosition` message: `NetId`, `User`, `IsPlane`, `Callsign` (all recorded once per-object instead, §3.2), `NetTime` (superseded by the frame's own `Time`), `Registration`/`FlightNumber`/`ClassCode`/`Wtc`/`ClassCodeConfirmed` (not recorded at all — §8.1), `RadarAltitude` (present in the live `Sim.AircraftPosition` struct as of the current code but not part of either `Sim.Write`'s wire format or this frame — it's a diagnostic-only in-memory field, per its doc comment in `Sim.cs`).

### 4.3 `SimEventFrame`

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | `EventId` (`uint`) |
| 4 | 4 | `Data` (`uint`) |

No version gating; unchanged since the format's `10022` baseline.

### 4.4 `IntegerVariablesFrame` / `FloatVariablesFrame` / `String8VariablesFrame`

All three call the same shared helper (`Sim.Write(BinaryWriter, Dictionary<uint, T>)` / matching `Read`) the network protocol uses for its `IntegerVariables`/`FloatVariables`/`String8Variables` messages (network-protocol.md §8.2):

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | `Count` (`ushort`) |
| 2 | `Count` × (4 + sizeof(T)) | `Count` × (`VariableId: uint`, `Value: int32 \| float \| string`) |

Two differences from the network version of the same payload worth calling out:

- The network sender automatically **splits** a variable set larger than `MAX_INTEGER_VARIABLES`/`MAX_FLOAT_VARIABLES` (100) or `MAX_STRING8_VARIABLES` (80) into multiple messages (network-protocol.md §8.2). The recorder does **not** — `Recorder.Record(Obj, Dictionary<uint,...>)` always writes exactly one frame per call, with no size cap, so a single `IntegerVariablesFrame` can in principle hold far more than 100 entries. This is consistent (a file has no per-datagram size limit to respect), but it does mean the `Count` field's `ushort` range (65,535) is the *only* ceiling, and it's enforced by a silent narrowing cast (`writer.Write((ushort)variables.Count)`) with no bounds check — see §8.2.
- `VariableId` (a `uint`, elsewhere called a `vuid`) is a hash of a simulator variable's SimConnect/X-Plane name (`HashString`/`CreateVuid` — the same DJB2-style hash the network protocol's `HashPassword` reuses for an unrelated purpose, per network-protocol.md §7). **The recording file stores only the hash, never the human-readable variable name.** Reconstructing what a given `VariableId` means requires the same lookup table the running application builds from `Variables.cs`/the `Documents\JoinFS\Variables\*.txt` files (this is exactly what `RecordingXRay/VariableLookup.cs` reimplements independently, by parsing the same `Variables.cs` source or the exported `.txt` files, to make recordings human-readable outside the app). A recording made with one build's variable set can only be fully decoded by a build (or a copy of `VariableLookup`) that has the same name→hash mapping — nothing in the file itself is self-describing here.

## 5. Append semantics

`Recorder.Append(BinaryReader reader)` merges a second `.jfs` file's contents into the currently-loaded recording: it sets `append = true`, records `appendTime = EndTime` (the current recording's own last frame time), then calls the normal `Read(reader)` path — which reads *that file's own* version short and parses it under that version, not the currently-loaded recording's version. Every frame's `Time` from the appended file is then shifted forward by `appendTime` before being added to `objList`. Two recordings made by different build versions can therefore be concatenated this way without a compatibility check beyond each file independently passing the `>= 10022` floor — there is no requirement, or even a warning, that the two files share a `Sim.VERSION`, build variant, or list of recorded objects.

## 6. `TypeRole` values

`Obj.TypeRole`/`Aircraft.TypeRole` is a `byte` holding one of `Substitution.TypeRole_*` (`Substitution.cs`):

| Value | Constant |
|---|---|
| 1 | `TypeRole_SingleProp` |
| 2 | `TypeRole_TwinProp` |
| 3 | `TypeRole_Airliner` |
| 4 | `TypeRole_Rotorcraft` |
| 5 | `TypeRole_Glider` |
| 6 | `TypeRole_Fighter` |
| 7 | `TypeRole_Bomber` |
| 8 | `TypeRole_FourProp` |
| 9 | `TypeRole_Airship` |
| 10 | `TypeRole_Balloon` |

This is model-substitution metadata (which local model-matching category to use on playback), unrelated to the network protocol's own `TypeRole` byte on `ObjectPosition`/`AircraftPosition` messages, though it is the same underlying concept and the same constant set.

## 7. Compatibility problems found in the current code

These aren't hypothetical extension risks — they're concrete mismatches visible today by comparing what `Write` actually emits against what `Read1` (in both `Recorder.cs` and the independent `RecordingXRay` reader) actually expects.

### 7.1 The `Livery`/`IcaoType`/`IcaoAirline` tail differs by *build variant*, not just by version

`Obj.Write`'s tail is:

```csharp
#if FS2024
    writer.Write(livery);
#endif
    writer.Write(icaoType);
    writer.Write(icaoAirline);
```

So an `FS2024` build writes **three** trailing strings (`Livery`, `IcaoType`, `IcaoAirline`); every other build variant (`FS2020`, `FSX`, `P3D`, `XPLANE`, `CONSOLE`) writes **two** (`IcaoType`, `IcaoAirline` only — `Livery` is silently dropped, never persisted). The matching `Read1` mirrors this with the same `#if`:

```csharp
#if FS2024
    livery = (version >= 21004) ? reader.ReadString() : "";
    icaoType = (version >= 21005) ? reader.ReadString() : "";
    icaoAirline = (version >= 21005) ? reader.ReadString() : "";
#else
    icaoType = (version >= 21004) ? reader.ReadString() : "";
    icaoAirline = (version >= 21004) ? reader.ReadString() : "";
#endif
```

Because `Sim.VERSION` (`21008`) is one number shared by every build variant (per this project's compiler-directive setup — every variant is compiled from the same source with a different `#if` flag, not a different version floor), a recording made by any two variants at the *same* `Sim.VERSION` can have genuinely different byte layouts for this tail once `version >= 21004`. Concretely: open an `FSX`- or `MSFS2020`-recorded file (2-string tail) in an `FS2024` build (which expects a 3-string tail): the `FS2024` reader reads the real `IcaoType` string into `Livery`, the real `IcaoAirline` string into `IcaoType`, and then reads whatever bytes come next — the *next object's* leading `Model`/`Plane` field, or end-of-stream — as `IcaoAirline`. From that point on, every subsequent read in the file is offset and the rest of the recording is garbage or throws.

This is not a paper risk: `RecordingXRay/RecordingReader.cs` (the standalone companion tool meant to inspect `.jfs` files outside the app) is written to *exactly* the `FS2024` branch's gating (`Livery` at `>= 21004`, then `IcaoType`/`IcaoAirline` at `>= 21005`), unconditionally, with no build-variant switch of its own. It will misparse and eventually desync on any recording produced by a non-`FS2024` build once that recording contains more than one aircraft/object past the point of divergence.

**Fix direction:** make the tail's shape depend only on `version`, not on which simulator the recording was made with — e.g. write `Livery` unconditionally starting at a new version number (bump past `21008`), with `Read1` gated purely on `version >= <new number>` in every build. This is exactly the additive, version-only pattern network-protocol.md §9.1 already recommends for message fields; the mistake here was letting a `#if SIMULATOR` compile flag leak into a decision that the wire format needs to be build-invariant about.

### 7.2 Non-aircraft objects can record frame types their reader doesn't reconstruct

`Recorder.Record(Obj obj, uint eventId, uint data)` and the three `Record(Obj obj, Dictionary<uint, ...>)` overloads all accept a plain `Obj`, not specifically an `Aircraft`. In `Sim.cs`, the variable-broadcast loop that calls `main.recorder.Record(obj.recorderObj, obj.variableSet.integers/floats/string8s)` iterates `foreach (var obj in objectList)` over **all** simulator objects, gated only on `obj.record` and `obj.variableSet != null` — nothing restricts it to aircraft. So a plain (non-`Aircraft`) recorded object's frame list can legitimately contain `SimEvent`/`IntegerVariables`/`FloatVariables`/`String8Variables` frames, and `Obj.Write` (the base class's writer, used for every non-aircraft object) happily serializes them — it just calls `frame.Write(writer)` generically for whatever is in the list.

But `Obj.Read1`'s frame-type dispatch only knows about one case:

```csharp
switch (baseFrame.type)
{
    case FrameType.ObjectPosition:
        frame = new ObjectPositionFrame();
        break;
}
```

For any other `FrameType` value on a plain object — `SimEvent`, `IntegerVariables`, `FloatVariables`, `String8Variables` — `frame` stays `null`, the `if (frame != null)` guard skips the specific-payload read entirely, and the frame's payload bytes (already consumed only up through the 9-byte generic header) are **never read**. The stream position is now wrong for everything that follows in the file — the next iteration of the loop reads the *next frame's* header starting partway into the orphaned payload, and things cascade from there. (`Aircraft.Read1`'s switch, by contrast, does handle all six live frame types — the bug is specific to the `Obj` path.)

**Fix direction:** either give `Obj.Read1` the same complete `switch` `Aircraft.Read1` has (cheapest, and arguably correct — there's no format reason a plain object's frames must be more limited than an aircraft's, since `Write` already doesn't distinguish), or restrict `Record(Obj, ...)` for `SimEvent`/variables to aircraft only, matching what `Read1` actually supports today. Either is a compatible fix for *future* recordings; existing files already written with orphaned frames on a plain object cannot be recovered without knowing the frame's original type from context, since the type byte itself was read correctly — only the payload was skipped.

### 7.3 The optional object-array section can't be joined by a second one later

As noted in §2, whether the file has *any* object section at all is decided by `PeekChar() != -1` right after the aircraft array — not by a version check or a length prefix. This works today because it's the only such decision point in the file. But it means the same trap network-protocol.md §9.2 flags for message-level EOF-sensing applies one level up, to the whole file: if a future change wants to append *another* optional top-level section after the object array (recording metadata — session id, creator `Guid`, free-text notes, a saved weather snapshot, etc. — would all be natural asks), "more bytes remain" can no longer tell an old reader *which* new section is present, or let it skip a section it doesn't recognize while still finding the next one. See §8.3 for the recommended fix (length-prefix the section, don't rely on EOF).

## 8. Other things worth knowing before extending the format

### 8.1 Fields the network protocol carries per-position that the recorder drops

`ClassCode`, `Wtc` and `ClassCodeConfirmed` exist on the live `ObjectPosition`/`AircraftPosition` network messages (network-protocol.md §8.2, a "Phase 3 network-only addition" per the comment in `Recorder.Jump()`) but are **not** captured by either position frame type. `Recorder.Jump()` already has to work around this on playback — it passes empty/unconfirmed values and relies on the sim re-deriving class/WTC locally from `icaoType`, "same as before this feature existed" (see the comment above the `UpdateAircraft` call in `Jump()`). Any future recorder change should either keep this deliberate omission (it's a reasonable simplification, since class/WTC are derivable from `icaoType` at playback time) or, if it's ever recorded, do so as a new version-gated per-object field rather than per-frame, since it's static for the object's lifetime like `IcaoType` already is.

### 8.2 Unbounded, unchecked variable count

`Sim.Write(BinaryWriter, Dictionary<uint, int|float|string>)` writes `(ushort)variables.Count` with no check that `Count <= ushort.MaxValue`. In the network protocol this is a non-issue because the sender caps the dictionary size before calling it (`MAX_*_VARIABLES`, split across messages — network-protocol.md §8.2). The recorder calls the exact same method with **no such cap**. A variable set larger than 65,535 entries — implausible today, but not something the type system prevents — would silently wrap the written count, causing the reader to stop early and leave the remaining, un-declared variable entries in the stream to be misread as the start of the next frame. Given how unforgiving the file format is of any desync (§1), this is worth a defensive bounds check (or, more robustly, the length-prefixed framing recommended next) even though it's very unlikely to trigger in practice.

### 8.3 Recommended direction: length-prefix records instead of relying on strict ordering

Sections §7.2 and §7.3 are both instances of the same underlying gap: nothing in the file lets a reader skip a frame, an object, or a whole section it doesn't fully understand. The network protocol already has a working template for this — the `Notes` message's length-prefixed, type-tagged inner records (network-protocol.md §8.9 and §9.2), where an unrecognized `Type` is skipped with `reader.ReadBytes(length)` instead of being blindly parsed field-by-field. Applying the same idea here — a `uint`/`ushort` byte-length prefix in front of each `Frame`'s type-specific payload, and in front of each `Obj`/`Aircraft` record's trailing optional tail — would mean:

- A frame type a given build doesn't recognize (one of the eight reserved-but-unused `FrameType` values, or a future addition) can be skipped by byte count instead of requiring every reader to have an up-to-date `switch` statement, which directly fixes the class of bug in §7.2 going forward.
- A version mismatch in the trailing tail fields (§7.1's root cause) becomes recoverable — a reader that doesn't know about a new trailing field can skip past it via the length prefix instead of misreading subsequent fields as if they were the one it expected.
- A second optional top-level section (§7.3) becomes addressable by giving the *whole* object-array section (or any future section) its own length prefix, so a reader can jump straight to (or past) it without depending on exact byte-for-byte agreement with the writer's field list.

This does cost 2–4 bytes per record/frame, but frames are already tens of bytes each, so the overhead is small relative to the robustness gained — and it would have prevented both bugs in §7 from being silent, file-corrupting mistakes rather than caught-and-skipped ones.

### 8.4 Recording format version is not independent of the network protocol's version

Because both share `Sim.VERSION`, a version bump made purely to change the network wire format (say, a new network-only message field) also raises the floor for what recording-format changes look "new" relative to it, and vice versa — a recording-only change consumes a slot in the same monotonic sequence the network protocol uses. This mirrors the capabilities/versioning critique network-protocol.md §9.3 already makes about `Join`/`SharedData` piggybacking on one global version number: two logically independent features (what a *file* looks like vs. what a *network message* looks like) are forced onto one shared timeline. Splitting a dedicated `Recorder.FORMAT_VERSION` (written as field 1 of the file, independent of `Sim.VERSION`) out from the network version would let recording-only changes ship without implying anything about network compatibility, and would also make the "reject anything below floor X" check in §2 independently tunable from the network protocol's own `dataVersion < 10014` floor.

### 8.5 No magic number / self-identification

The file format's only self-identifying marker is that its first two bytes parse as a plausible `short` version number `>= 10022`. Any other binary file whose first two little-endian bytes happen to fall in that numeric range would be accepted by `Recorder.Read()` and then fail confusingly (or, worse, partially succeed with garbage data) deeper into parsing, rather than being rejected immediately with a clear "this isn't a JoinFS recording" message. A 4-byte magic value (e.g. `"JFS1"`) ahead of the version short — itself an additive, backward-compatible change for any *future* version, though not retroactively for files already written — would make misidentified files fail fast and legibly, the same way a real container format (ZIP, PNG, etc.) does.

## 9. Extending the format without breaking older clients

Generalizing from network-protocol.md §9 and the findings above:

1. **Keep using tail-only, version-gated additive fields** for anything that's per-object or per-frame and always applies going forward — this is already how `Height`/`GroundFlags` (10023), `IcaoType`/`IcaoAirline` (10024/21005), and `StaticCgToGround` (21008) were added, and it works cleanly as long as the gate is a runtime `version` check only (see the counter-example in §7.1).
2. **Never let a build-target `#if` change a wire layout that a version number is also supposed to describe.** §7.1 is the concrete lesson: if a field is genuinely simulator-specific (e.g. only `FS2024` can source a "livery variation" value at all), still write a placeholder (empty string, or a per-field presence flag) in every build so the tail's *shape* is build-invariant, and let the *value* differ.
3. **Fix the two live bugs (§7.1, §7.2) before adding anything else that depends on the affected code paths** — extending a format on top of an already-inconsistent byte layout compounds the blast radius of both problems.
4. **Adopt length-prefixed framing for frames, per-object tails, and top-level sections** (§8.3) — this is the single highest-leverage structural change available, since it converts "reader doesn't understand this" from a silent, whole-file-ending desync into a cheap, local skip.
5. **Give the recording format its own version counter**, decoupled from `Sim.VERSION` (§8.4), so a recording-only feature doesn't force a decision about network compatibility and vice versa.
6. **Add a magic number** (§8.5) so malformed or foreign files are rejected immediately and legibly instead of partially parsing.
7. **Bounds-check the variable-count cast** (§8.2) — cheap insurance given how expensive a desync is in this format specifically.
8. There's no pressure to conserve `FrameType` values (only 6 of 14 declared values are in use, matching the network protocol's own generous headroom per network-protocol.md §9.1) — new frame types can simply be appended to the enum, keeping the existing "append-only, never reorder" discipline.

## 10. Quick reference

| Constant / fact | Value | Meaning |
|---|---|---|
| File extension | `.jfs` | Conventional, enforced only by the save/open dialog filters, not by content |
| Minimum accepted recording version | `10022` | Below this, `Recorder.Read()` rejects the whole file |
| `10023` | Adds `Height`/`GroundFlags` to `ObjectPositionFrame`, `Elevation`/`GroundFlags` to `AircraftPositionFrame` |
| `21004` | Adds `IcaoType`/`IcaoAirline` (non-`FS2024` builds) or `Livery` (`FS2024` builds) to the per-object tail — see §7.1 |
| `21005` | Adds `IcaoType`/`IcaoAirline` to the per-object tail, `FS2024` builds only |
| `21008` | Adds `StaticCgToGround` to `AircraftPositionFrame`, always written regardless of build |
| Current `Sim.VERSION` | `21008` | Shared with the network protocol — see §8.4 |
| `FrameType` values in active use | 6 of 14 | `ObjectPosition`, `AircraftPosition`, `SimEvent`, `IntegerVariables`, `FloatVariables`, `String8Variables` |
| Per-object trailing tail | 0–3 strings | `Livery`?, `IcaoType`?, `IcaoAirline`? — shape depends on build variant, see §7.1 |
| Variable-frame count field | `ushort`, unchecked | No cap on write, unlike the network protocol's `MAX_*_VARIABLES` (§8.2) |

## 11. Source map

| Concern | File |
|---|---|
| Recording data model, writer, reader, playback interpolation | `JoinFS/Recorder.cs` |
| Shared struct wire format (`ObjectPositionVelocity`, `AircraftPosition`, integer/float/string8 variable dictionaries) also used by the network protocol | `JoinFS/Sim.cs` (`Sim.Write`/`Sim.Read` overloads) |
| Model-substitution `TypeRole_*` constants | `JoinFS/Substitution.cs` |
| Recorder UI, file open/save/append dialogs (`.jfs` filter) | `JoinFS/Forms/RecorderForm.cs` |
| `SaveRecording()` (the actual `FileStream`/`BinaryWriter` creation) | `JoinFS/Forms/MainForm.cs` |
| Independent reference reader for `.jfs` files, useful for spotting divergence from the in-app reader (see §7.1) | `RecordingXRay/RecordingReader.cs` |
| Variable-hash → human-readable-name lookup (mirrors `Variables.cs`) | `RecordingXRay/VariableLookup.cs` |
