# Protocol Changes Between v26.4 and v26.5

This document catalogs every change to JoinFS's network communication and recording (`.jfs`) format between the `v26.4` and `v26.5` git tags, and analyzes the backward-compatibility risk of each. It is a companion to `docs/network-protocol.md` and `docs/recording-protocol.md`, which describe the *current* (post-`v26.5`) state of each format in full; this document instead answers "what changed, and could it break an older peer or an old file."

**Method:** `git clone https://github.com/tuduce/JoinFS.git` (the repo's `origin` remote), then `git diff v26.4 v26.5` scoped to the relevant files, `git log --oneline v26.4..v26.5` for the commit list (73 commits), and `git show <tag>:<path> | grep ...`/`git log -S ...` to pin down exactly which commit introduced each version-gate number. `v26.4` = `d4d98aa`, `v26.5` = `a261309`. Only `JoinFS/Network.cs`, `JoinFS/Sim.cs` and `JoinFS/Recorder.cs` changed among the files the two protocol documents cover; `JoinFS/Node.cs` (transport framing, `Nuid`, guaranteed delivery, session/mesh management — network-protocol.md §§2–4, 6–7) is **byte-for-byte identical** between the two tags, and `JoinFS-XP` (the X-Plane native plugin) has zero changes either, since it never touches this shared C# wire-format code.

## 1. Network protocol changes

### 1.1 The data-version floor was unified across build variants — and bumped twice

At `v26.4`, `Sim.VERSION` (the `DataVersion` every non-internal message carries, network-protocol.md §5) was itself compile-time conditional:

```csharp
#if FS2024
    // TODO: Advance the data version for everybody, not just FS2024
    public const short VERSION = 21005;
#else
    public const short VERSION = 21004;
#endif
```

By `v26.5` this is a single unconditional constant, `public const short VERSION = 21007;`, reached in two steps within the range: commit `1279948` ("Add distinct registration/icaoAirline/flightNumber fields to flight plan data model") introduced version `21006`, and commit `c05d32e` ("Extend network protocol with classCode/WTC; full localization retrofit") introduced `21007` and removed the `#if FS2024` split at the same time. Both commits exist only on the `v26.5` side (`git tag --contains` confirms neither is reachable from `v26.4`).

This closes a real latent problem: before this change, an `FS2024` build and every other build (`FS2020`/`FSX`/`P3D`/`XPLANE`/`CONSOLE`) were *already* sending different `DataVersion` numbers for identical wire content, and — worse — some fields' presence in a message depended on the sending build's compile flag, not on the number it announced (see §1.2). Unifying the constant doesn't retroactively fix already-shipped mismatches, but it removes the root cause for anything shipped from `v26.5` onward.

### 1.2 `Livery` is now sent unconditionally on `ObjectPosition`/`AircraftPosition`

`Network.WriteObjectPositionVelocityMessage`/`WriteAircraftPositionMessage` previously wrote the livery string only under `#if FS2024`:

```csharp
#if FS2024
    message.Write(simObject.ownerLivery);
#endif
```

At `v26.5` this is unconditional in every build variant, and the receive side's `#if FS2024` guard around reading it back was removed the same way — `variation` (the wire name for livery) is now always read via the existing EOF-sensing idiom (`(reader.PeekChar() != -1) ? reader.ReadString() : ""`) regardless of build. `Sim.Obj.ownerLivery` and the derived `ModelLivery` property lost their `#if FS2024` guards too, so every build now *carries* a livery value through its object model, even though only `FS2024` can ever *populate* it locally from SimConnect — other builds pass through whatever a peer sent them.

This is the network-protocol-side twin of the compile-time-divergence bug documented in `docs/recording-protocol.md` §7.1 for the **recording** format — and here it is genuinely fixed: the wire tail's shape no longer depends on which simulator wrote it, only on `DataVersion`/EOF position, which is exactly what network-protocol.md §9.1 recommends. **The equivalent recording-format bug was not touched by this range — see §2 below.**

### 1.3 New trailing fields on `ObjectPosition` and `AircraftPosition`

Both messages grew new tail fields, added via the same "EOF-sensing, no explicit version gate" idiom already documented in network-protocol.md §5/§9.2 — a receiver simply keeps reading fields as long as bytes remain, so the new fields are read by any build new enough to know about them and silently skipped (never even attempted) by anything older, version number aside:

| Message | New trailing fields (in order, after the pre-existing `icaoType`/`icaoAirline`) |
|---|---|
| `ObjectPosition` | `ClassCode: string`, `Wtc: string`, `ClassCodeConfirmed: bool` |
| `AircraftPosition` | `Registration: string`, `FlightNumber: string`, `ClassCode: string`, `Wtc: string`, `ClassCodeConfirmed: bool` |

`ClassCode`/`Wtc`/`ClassCodeConfirmed` are the sender's own Doc8643 class code and wake-turbulence category, resolved once locally (from config-confirmed data or live SimConnect category/engine variables — see §1.5) and now broadcast so every receiving peer can use the sender's best-available classification for model matching, instead of every peer separately re-deriving it from `icaoType` — which fails whenever `icaoType` is a bogus/non-standard string an add-on reports. `Registration`/`FlightNumber` on `AircraftPosition` mirror the same fields added to the `FlightPlan`-carrying messages (§1.4).

Corresponding signature changes ripple through `Sim.UpdateObject`/`Sim.UpdateAircraft` (both gained `classCode`/`wtc`/`classCodeConfirmed` parameters, and `UpdateAircraft` also gained `registration`/`flightNumber`), and every call site — including `Recorder.cs`'s playback calls, which now pass empty-string/`false` placeholders for the fields recordings don't capture (§2.3).

### 1.4 New fields on flight-plan-carrying messages, gated by `DataVersion >= 21006`

Unlike §1.3, these use an explicit version check rather than EOF-sensing, because the write side for at least one of the affected messages (`UserList2`) writes the new fields unconditionally regardless of the sender's actual `DataVersion`:

- `Network.SendFlightPlanMessage` (`MESSAGE_ID.FlightPlan`) now appends `Registration`, `IcaoAirline`, `FlightNumber` after the existing `Callsign` field.
- `Network.SendUserListMessage` (`MESSAGE_ID.UserList2`, network-protocol.md §8.8) now appends the same three fields after `Altitude(str)`.
- All three corresponding receive paths (`UserList2` handling, the `Status`-embedded flight plan read, and the standalone `FlightPlan` message handler) gate the new reads on `dataVersion >= 21006`.

`Sim.FlightPlan` also gained a `callsignSetByUser` bool (purely local UI state, never serialized) and the three new string fields (`registration`, `flightNumber`, plus `icaoAirline` already existed).

A secondary, purely cosmetic wire-adjacent change: `departure`/`destination` are now upper-cased on receive (`.ToUpperInvariant()`) in every place they're read off the network. This changes the *value* peers display for a lower-case-typed ICAO airport code but not the wire format itself — an old peer sending lower-case `departure`/`destination` is read and normalized by any `v26.5`+ receiver; a `v26.5`+ sender still writes whatever case the user typed, unnormalized, so this is a receive-side display fix, not a protocol-level change.

### 1.5 Model-matching/classification changes: local computation only, not on the wire

The bulk of the 465-line `Sim.cs` diff and the entire 2221-line `Substitution.cs` diff (the "Model Matching Redesign" and "ICAO type validated against Doc8643" work described in `Readmes/Release-26.5.md`) is local classification logic: `Substitution.Match()`'s signature grew several parameters (`classCode`, `wtc`, `classCodeConfirmed`, `registration`), a new `MatchTrace`/`Explain Match` diagnostic path was added, and a Doc8643-backed validator now cross-checks a config-confirmed ICAO type designator instead of trusting it blindly. **None of this changes what goes out over the wire** beyond what's already covered in §1.3 (the `classCode`/`wtc`/`classCodeConfirmed` fields *feeding* this local logic are new network fields, but the matching algorithm that consumes them runs identically on both sides of the network boundary and produces no additional wire traffic).

Similarly, the "helicopters on elevated platforms" ground-jitter fix (`trustingPlatformElevation`/`trustingPlatformGround`/`smoothedElevationOffset`, and the new `radarAltitude`/`radarHeight` diagnostic fields on the in-memory position structs) is entirely local: `radarAltitude` and `radarHeight` are never written to a network message or a recording frame — they exist only on the live `Sim.AircraftPosition`/`Sim.Pos` structs used for local physics decisions. (The still-unreleased `staticCgToGround` field this same feature work eventually needed — documented as network-protocol.md §8.2's version-`21008` field and recording-protocol.md §4.2 — was **not yet added** at `v26.5`; it lands afterward, on the way to a `26.6` release, per `git log v26.5..origin/main`. Anyone diffing against a checkout newer than `v26.5` should expect `Sim.VERSION` to already read `21008`, not `21007`.)

### 1.6 Hub relay fix for variable-sync messages on sim-less nodes

Commit `6c96fc9` ("Fix COM frequency (and other float/string8 variable) updates not reaching sim-less hubs") relaxes the acceptance gate in `VariableMgr.Set` for `FloatVariables`/`String8Variables`/`IntegerVariables` updates (network-protocol.md §8.2) from:

```csharp
if (UserAircraft && (...) || injected && definition.injected)
```

to:

```csharp
if (main.sim == null || main.sim.Connected == false || UserAircraft && (...) || injected && definition.injected)
```

A `CONSOLE`-variant hub has no attached simulator (`main.sim == null`), so it was previously dropping received `FloatVariables`/`String8Variables` updates on the floor before they could be relayed onward — a real bug fix to how already-existing messages are *processed*, not a wire-format change. No bytes on the wire changed; this only affects whether a hub actually acts on/forwards what it receives.

### 1.7 What did **not** change

- `LocalNode.MESSAGE_ID` and the application `Network.MESSAGE_ID` enums (network-protocol.md §8.1/§8) are identical — no new message types, no reordering.
- `Node.cs` — transport framing (the 21-byte header), `Nuid`, the guaranteed-delivery/ACK mechanism, `Join`/`Pulse`/`Pathfinder` mesh logic — is byte-for-byte unchanged.
- Minimum accepted `dataVersion` (`10014`, the floor below which `ReceiveMsg` drops a message outright) is unchanged.
- Session security (password/login hashing, lack of transport encryption, IP ban list — network-protocol.md §7) is unchanged.

### 1.8 External telemetry interfaces (WebSocket / Webhook feeds) — new fields and a value-format change

These aren't part of the peer-to-peer protocol network-protocol.md documents, but they are "network communication" in the broader sense the request asked about: JoinFS's optional `-websocket`/webhook features publish live aircraft state as JSON to external tools (dashboards, ATC clients, etc.).

- **New JSON fields** on both feeds' aircraft snapshot: `registration`, `icaoAirline`, `flightNumber`, `livery`, and (WebSocket only) `onGround`.
- **`com1`/`com2` value format changed.** Both feeds previously formatted a raw integer COM-frequency variable with a hand-rolled `"raw[..3] + "." + raw[3..]"` split (`FormatFreq(int)`), which cannot represent 8.33 kHz-spaced channels correctly. Both now call a new `VariableMgr.Set.GetFrequency(uint vuid)` helper that prefers the float-typed frequency variable and falls back to the legacy integer/BCD one only if the float isn't present, formatting the result as `value.ToString("F3")`. The output is a proper 3-decimal MHz string either way, but the exact string content differs from the old format for any channel that exercises the difference (notably 8.33 kHz channels, which the old format mangled) — an external consumer that parsed the old string shape by fixed character position rather than as a plain decimal number could see a change here.

## 2. Recording (`.jfs`) format changes

### 2.1 Executive summary: the on-disk byte layout is unchanged

Despite `Recorder.cs` showing a 57-line diff, **no field was added to, removed from, or reordered in the `.jfs` file format between `v26.4` and `v26.5`.** The version floor (`10022`), the version gates (`10023`, `21004`, `21005`), the frame catalog, and the per-object/per-aircraft tail layout described in `docs/recording-protocol.md` §§2–4 are identical byte-for-byte at both tags. A `.jfs` file written by a `v26.4` build opens correctly in `v26.5` and vice versa, subject to the pre-existing caveats in §2.4 below (which are just as present at `v26.4` as at `v26.5`).

### 2.2 What *did* change: the in-memory `livery` field lost its compile-time guard — but the file writer didn't

`Recorder.Obj`/`Recorder.Aircraft`'s `livery` field, its constructors, and every call site that populates it (`Recorder.StartRecord`, `Recorder.Jump`, `Recorder.UpdateInterpolatedPosition`, and the `DoWork()` re-attachment path) had their `#if FS2024` guards removed, mirroring the same cleanup in `Sim.cs` (§1.2) — so a non-`FS2024` build's in-memory recorded-object model now carries a `livery` value too (populated from `Sim.Obj.ownerLivery`, which itself is now unconditionally populated, including via network relay from an `FS2024` peer).

**Critically, this change stops at the in-memory model.** The actual file serialization is untouched:

```csharp
// Obj.Write — identical at v26.4 and v26.5
#if FS2024
    writer.Write(livery);
#endif
    writer.Write(icaoType);
    writer.Write(icaoAirline);
```

```csharp
// Obj.Read1 — identical at v26.4 and v26.5
#if FS2024
    livery = (version >= 21004) ? reader.ReadString() : "";
    icaoType = (version >= 21005) ? reader.ReadString() : "";
    icaoAirline = (version >= 21005) ? reader.ReadString() : "";
#else
    icaoType = (version >= 21004) ? reader.ReadString() : "";
    icaoAirline = (version >= 21004) ? reader.ReadString() : "";
#endif
```

A byte-for-byte `diff` of these two methods between `v26.4` and `v26.5` is empty. So: a non-`FS2024` build at `v26.5` can now *hold* a populated `livery` string on a recorded object in memory, but `Obj.Write`/`Aircraft.Write` still silently drop it when saving to disk, exactly as at `v26.4`. **The recording-format cross-variant compatibility bug documented in `docs/recording-protocol.md` §7.1 is unchanged by this release** — an `FS2024`-recorded `.jfs` file (3-string tail: livery, icaoType, icaoAirline) still desyncs when opened by any non-`FS2024` reader expecting a 2-string tail (icaoType, icaoAirline only), and vice versa in the direction the version gate allows. This range fixed the *network* half of that same underlying mistake (§1.2) without touching the *recording* half.

### 2.3 Playback call sites updated for `Sim.UpdateObject`/`UpdateAircraft`'s new signatures

Because §1.3 added `classCode`/`wtc`/`classCodeConfirmed` (and, for aircraft, `registration`/`flightNumber`) parameters to `Sim.UpdateObject`/`Sim.UpdateAircraft`, every call site had to be updated to keep compiling — including the four places in `Recorder.cs` that call these methods during playback (`Jump()` and `UpdateInterpolatedPosition()`, for both the aircraft and plain-object cases). All four now pass empty strings and `false` for the new parameters, with an explicit comment acknowledging the gap:

```csharp
// recordings don't currently capture classCode/wtc (a Phase 3 network-only
// addition) - pass empty/unconfirmed so replay falls back to local re-derivation
// from icaoType, same as before this feature existed
```

This is a source-level adaptation to keep the recorder compiling against the new method signatures, not a recording-format change — it's already covered by `docs/recording-protocol.md` §8.1 ("Fields the network protocol carries per-position that the recorder drops"), which now additionally applies to `classCode`/`wtc`/`classCodeConfirmed`/`registration`/`flightNumber` alongside the pre-existing `Wtc`/`ClassCode`/`ClassCodeConfirmed` gap. Nothing about this requires or causes a recording-format version bump, since no new bytes are read from or written to the file.

### 2.4 What did **not** change (confirmed unchanged, not merely unmentioned)

- The frame-type read gap in `Obj.Read1` (`docs/recording-protocol.md` §7.2 — a plain, non-aircraft recorded object's `SimEvent`/`*Variables` frames are written but never read back, desyncing the rest of the file) is byte-for-byte identical code at both tags.
- The EOF-sensed optional object-array section (`docs/recording-protocol.md` §2, §7.3) is unchanged.
- The minimum accepted recording version (`10022`) and every existing version gate are unchanged.
- `RecordingXRay` (the standalone `.jfs` inspection tool) had zero commits in this range — it still assumes the `FS2024`-shaped tail unconditionally, so it remains just as likely to misparse a non-`FS2024` recording as before (`docs/recording-protocol.md` §7.1's closing paragraph).

## 3. Compatibility risk analysis

### 3.1 Network protocol: low risk, one already-mitigated transition concern

The network changes in this range follow the additive, version-tolerant idioms `docs/network-protocol.md` §9 recommends, and the design holds up:

- **§1.2 (unconditional livery) and §1.3 (new EOF-sensed tail fields on position messages) are safe in both directions.** An old (`< v26.5`) peer never attempts to read bytes it doesn't know about — UDP delivers each message as one discrete datagram (network-protocol.md §2), so unread trailing bytes are simply discarded with the rest of the packet, not carried into the next message. A new (`≥ v26.5`) peer talking to an old sender correctly falls back to defaults (`""`/`false`) via the same EOF check. This is the *lowest*-risk category of change in the protocol's existing vocabulary.
- **§1.4 (explicit `dataVersion >= 21006` gate on flight-plan fields) is also safe**, for the same reason network-protocol.md §5 gives for every existing version-gated read: the *receiver's own compiled floor* decides whether it attempts the read, and the field is always appended strictly after every pre-existing field, never inserted mid-message.
- **The one real transition-period risk is §1.1**, and it's specific to the compile-time split, not to `v26.5` itself: any two builds compiled between the introduction of the pre-`v26.4` split (`21004`/`21005`) and its removal at `c05d32e` were *already* capable of sending mutually-inconsistent `DataVersion` numbers for a given actual wire layout — an `FS2024` build claiming `21005` while a `CONSOLE`/`FSX`/etc. build claiming `21004` could, in principle, have shipped subtly different tails for the same nominal version number if a future change had ever gated on that split inconsistently (as it very nearly did — see §1.2/§2.2, where exactly this happened for `livery`). `v26.5` closes the split going forward, but does not — cannot — retroactively reconcile any `v26.4`-line build already out in the field that still reports the old, build-dependent number. Since `21004`/`21005`/`21006`/`21007` are all comfortably above the `10014` hard floor, no message gets rejected outright; the risk is confined to the specific fields that were still `#if`-gated at the time (livery on position messages, now fixed) rather than to the version negotiation mechanism itself.
- **No new `MESSAGE_ID` values, no changes to `Node.cs`.** A `v26.4` and a `v26.5` node can join the same mesh, exchange `Join`/`Pulse`/`Pathfinder` traffic, and see each other's positions with zero risk of the transport layer itself misbehaving — the worst case for any single field is "peer doesn't send/understand it yet," never a parse failure or stream desync (the network protocol's datagram framing makes desync structurally impossible for any one message, unlike the recording format — see §3.2).

**Overall: safe to mix `v26.4` and `v26.5` peers in the same session.** The only observable degradation is that a `v26.4` peer won't see a `v26.5` peer's class code/WTC/registration/flight-number data (falls back to local re-derivation, exactly as it already did for peers lacking any of the pre-existing optional fields), and vice versa for whatever a `v26.4` peer doesn't send.

### 3.2 Recording format: no new risk, but the existing risk is now easier to trigger

Since §2.1 established the file format itself is unchanged, there is **no new compatibility risk introduced by this range for `.jfs` files** — a `v26.4`-written recording opens identically in `v26.5` and vice versa, modulo the pre-existing `10022`/`10023`/`21004`/`21005` gates that were already true at `v26.4`.

However, two things raise the *practical* likelihood of hitting the pre-existing §2.2 bug going forward, even though neither is a change to the format itself:

- Because `Sim.Obj.ownerLivery` is now populated in every build (via local SimConnect on `FS2024`, via network relay on every other build — §1.2), a non-`FS2024` recorder is now more likely than before to actually have a non-empty livery value sitting on a recorded object at save time — it just still can't write it to the file. This doesn't change the *bytes*, but it does mean the fidelity gap (a real livery value the recording silently discards) is now reachable in more scenarios (any object substituted from a network-relayed `FS2024` peer, not only locally-instantiated `FS2024` aircraft), where previously the value genuinely wasn't available at all outside `FS2024`, so there was nothing to lose in the first place.
- The classification metadata added in §1.3 (`classCode`/`wtc`/`classCodeConfirmed`, `registration`, `flightNumber`) is now part of what a *live* session carries per-aircraft, widening the gap between "what a live network session knows about an aircraft" and "what a recording of that session can reproduce on playback" (§2.3). This doesn't risk a parse failure, but it is a growing fidelity/feature gap between the two formats that `docs/recording-protocol.md` §8.1 already flagged and that this range makes measurably larger.

**Recommendation, if the two live-format bugs in `docs/recording-protocol.md` §7.1/§7.2 are fixed in a future release:** do it as part of a dedicated recording-format version bump (ideally the independent `Recorder.FORMAT_VERSION` recording-protocol.md §8.4 recommends, decoupled from `Sim.VERSION`), not folded into a network-protocol-driven version bump like `21006`→`21007` was — the two formats' compatibility timelines are logically independent and this range is a good illustration of why: every network-side version bump here happened for reasons that have nothing to do with what a `.jfs` file looks like, yet they still consume slots in the one counter the recording format's minimum-version check also reads.

### 3.3 External telemetry consumers: low-to-moderate risk, additive fields are safe but the frequency format is a breaking value change

Any external tool (dashboard, ATC bridge, etc.) consuming the `-websocket` or webhook JSON feeds:

- Is unaffected by the five new fields (`registration`, `icaoAirline`, `flightNumber`, `livery`, `onGround`) as long as it ignores unknown JSON keys, which is the normal, forward-compatible way to consume JSON.
- **May break if it parses `com1`/`com2` by fixed character position** (the old `"XXX.YY"` shape from the buggy integer split) rather than as a decimal number — the new `GetFrequency`-based formatting produces a proper `"F3"`-formatted MHz value that differs from the old string for any frequency the old BCD-split logic handled incorrectly (which, per the referenced upstream fix, was specifically 8.33 kHz-spaced channels). This is a value-format change on an already-existing field, not something EOF-sensing or version-gating protects against, because these feeds have no version negotiation at all — they're a one-way JSON push with no schema versioning field. Anyone maintaining an external consumer of these feeds should treat this as a breaking change worth a changelog callout, distinct from the network protocol's own (much safer) evolution.

## 4. Quick reference

| Version | Introduced by | What it gates |
|---|---|---|
| `21004` | pre-`v26.4` (non-`FS2024` builds) | Baseline non-`FS2024` `DataVersion` at `v26.4`; also the recording format's `icaoType`/`icaoAirline` tail gate |
| `21005` | pre-`v26.4` (`FS2024` builds only) | Baseline `FS2024` `DataVersion` at `v26.4`; also the recording format's `FS2024`-only `icaoType`/`icaoAirline` tail gate (recording's `livery` gate is `21004`, not `21005`) |
| `21006` | `1279948` (within `v26.4..v26.5`) | `Registration`/`IcaoAirline`/`FlightNumber` on `UserList2` and the `FlightPlan` message/embedded flight-plan reads |
| `21007` | `c05d32e` (within `v26.4..v26.5`) | Unifies `Sim.VERSION` across all build variants; enables the `ClassCode`/`Wtc`/`ClassCodeConfirmed` fields (though those are actually read via EOF-sensing, not a version check) |
| `21008` | *after* `v26.5`, pre-`26.6` | `StaticCgToGround` on `AircraftPosition` — already documented in `docs/network-protocol.md`/`docs/recording-protocol.md` as "current," but not part of the `v26.4`→`v26.5` range covered here |

| File | Lines changed (`v26.4`→`v26.5`) | Wire/file-format-relevant? |
|---|---|---|
| `JoinFS/Node.cs` | 0 | No changes at all |
| `JoinFS/Network.cs` | 95 | Yes — §1.2–§1.4 |
| `JoinFS/Sim.cs` | 465 | Partially — §1.2/§1.3/§1.6 are wire/behavior-relevant; the rest (§1.5) is local matching logic |
| `JoinFS/Recorder.cs` | 57 | No — in-memory-only change, §2.2/§2.3 |
| `JoinFS/Substitution.cs` | 2221 | No — local model-matching/classification logic only |
| `JoinFS/WebSocketServer.cs`, `WebhookService.cs`, `Whazzup.cs` | 39 / 10 / 4 | Yes, for external consumers only — §1.8 |
| `JoinFS-XP/*` | 0 | No changes at all |

## 5. Source map

| Concern | File(s) |
|---|---|
| P2P wire protocol changes analyzed here | `JoinFS/Network.cs`, `JoinFS/Sim.cs` (struct/version portions) |
| Recording format (confirmed unchanged) | `JoinFS/Recorder.cs` |
| External telemetry feeds | `JoinFS/WebSocketServer.cs`, `JoinFS/WebhookService.cs`, `JoinFS/Whazzup.cs` |
| Local-only model-matching/classification work (out of scope for wire/file compatibility) | `JoinFS/Substitution.cs`, the non-network parts of `JoinFS/Sim.cs` |
| Release narrative from the maintainers | `Readmes/Release-26.5.md` |
| Current (post-`v26.5`) protocol state | `docs/network-protocol.md`, `docs/recording-protocol.md` |
