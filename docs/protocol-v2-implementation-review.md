# JFP2 Implementation Review: Field Audit and Rollout Readiness

## Status

Independent review of the **actual code** under `JoinFS/Jfp2/` and the JFP2 call sites in
`JoinFS/Node.cs`/`JoinFS/Network.cs`, as they stand after
`docs/protocol-v2-implementation-plan.md` Phases 0–5 (commit `83ae480`, 2026-09-14). This is not a
review of the design (`docs/protocol-v2-design.md`) or of `ProtocolV2Reference/` in isolation — it
is a field-by-field audit of what was actually built, followed by a rollout-readiness verdict.

Verified before writing this: `cd JoinFS.Tests && dotnet test --filter "FullyQualifiedName~Jfp2" -c
FS2024-Debug` → 66/66 passing, clean build. The implementation plan's own build/test claims (121/121
across all Jfp2+dependent tests) were not independently re-run in full, but the Jfp2-specific subset
matches what the plan claims.

## 1. Summary verdict

**Update, 2026-09-16 (later the same day): Position, VariableSync, and SimEvent were live-tested
end-to-end for the hub-relay scenario against a real MSFS2024 session, closing the gap the first
2026-09-16 update below left open ("negotiation confirmed, application traffic not yet exercised with
real data"). Position and VariableSync worked correctly immediately; SimEvent surfaced Finding 8, a
real pre-existing legacy bug (not JFP2's) that made guaranteed-delivery messages unreachable for any
indirect peer — now fixed and re-verified live.** See Finding 8 in §4.

**Update, 2026-09-16: the hub-relay scenario (two JFP2-capable peers that cannot reach each other
directly, everything relayed through a JFP2-capable hub) has now been live-tested and found, once
Finding 7 below was fixed, to behave exactly as it did before JFP2 existed** — see Finding 7 in §4 and
the implementation plan's own "Hub-relay verification, 2026-09-16" section for the methodology.

**Update, 2026-09-14 (later the same day): Findings 1, 2, and (partially) 5 are now fixed in code** —
see §4 for what changed (Finding 5's second half, `WireText`'s unbounded `ushort` length cast, remains
open). The summary below is left as originally written, with each affected item struck through and
annotated, so the record of what was found and what was done about it stays intact.

**Not yet ready for a default-on or "prefer JFP2" production rollout, but closer than it was.** It
is in good shape to continue the staged, opt-in, coexistence-first rollout the design already
describes (§10) — negotiation has been verified end-to-end over real sockets, including against the
real, unpatched production hub, with zero observed effect on legacy traffic, **and, as of 2026-09-14,
live Position/Identity traffic between two real JFP2-negotiated peers has been confirmed against an
actual simulator** (§3), including the mixed-version (v26.6 + legacy v26.5) fallback case. Two
concrete blockers remain, in priority order:

1. ~~**§2.1 — Three ported message classes silently lost their guaranteed-delivery semantics.**~~
   **Fixed, 2026-09-14 — see Finding 1 in §4.** Was a real behavioral regression, not a documented
   tradeoff. `Envelope` now writes/reads the spec'd guaranteed-delivery extension block, and
   `LocalNode.SendJfp2Application(..., guaranteed: true)` — now used by Event/Notes/WeatherReply,
   matching each one's legacy counterpart — retries until acked (`GuaranteedDone`) or gives up after 5
   attempts, mirroring the legacy protocol's own guaranteed-message behavior. Covered by new unit
   tests (`EnvelopeTests`); **not yet exercised live** — same caveat as every other untested class in
   §3, now joined by "does the retry/ack loop itself behave correctly under real packet loss," which
   still needs the fault-injection methodology described in §6 and was never in scope for a functional
   test pass anyway.
2. ~~**§2.2 — The Position hot path still allocates on every send**~~ **Fixed, 2026-09-14 — see
   Finding 2 in §4.** `LocalNode.SendJfp2Datagram` now rents from `ArrayPool<byte>.Shared` and sends
   via `Socket.SendTo(ReadOnlySpan<byte>, ...)` instead of allocating a fresh `byte[]` per call and
   using `UdpClient.Send(byte[], int, IPEndPoint)`. This closes the gap for every JFP2 send, Position
   included. **Not yet profiled** — this review confirmed the allocation is gone by inspection (no
   more `new byte[...]` on this path) and confirmed the change doesn't break anything (build + full
   test suite), but did not attach a profiler to confirm actual GC behavior under many-broadcast-peer
   load; see §6 for why that's a separate kind of verification from what this review does.

A third, softer gap remains: VariableSync/Event/FlightPlan/Notes/Weather still have no live-traffic
confirmation (only Position/Identity do now) — see §3. This is no longer a blocker on its own (the
highest-frequency, highest-risk class per the design doc's own §6.1 framing is proven), but the
remaining classes should get the same live confirmation before being trusted as a default. See
`docs/protocol-v2-manual-test-guide.md` for a step-by-step procedure to exercise each of them —
that guide's §4 (tolerate one-off drops for Event/Notes/WeatherReply) is now stale given the Finding 1
fix and should be revisited once these are live-tested again.

None of these require re-architecting anything — all are localized, well-understood fixes against a
design that is otherwise sound and consistently applied. See §4 for the fully itemized finding list
and §5 for what "ready" would concretely look like.

**Update, 2026-09-15 — Finding 6 added.** Field testing (two real-simulator sessions, logged in
`docs/protocol-v2-implementation-plan.md`) surfaced a pre-existing, non-JFP2-specific latency source in
`VariableMgr.Set`'s receive-side hold-off logic that explains a reported 1-2 second lag in light-state
(landing/taxi light) propagation, observed identically over both JFP2 and legacy transport. It does not
block the JFP2 rollout (it isn't a JFP2 regression), but it's worth fixing independently — see Finding
6 in §4.

## 2. Field-by-field catalog

Every field below is drawn from the actual `JoinFS/Jfp2/**/*.cs` source, not the design doc's
original proposal, and cross-checked against the legacy shape it replaces
(`docs/network-protocol.md` §8). "Assessment" judges whether the field belongs on that message —
i.e., whether its data actually varies at that message's frequency/scope — not whether the codec is
byte-correct (the unit tests already cover that).

### 2.0 Envelope and handshake (not application data, but everything else rides on it)

**Fixed envelope** (`JoinFS/Jfp2/Envelope.cs`, 8 bytes, every JFP2 datagram):

| Field | Size | Assessment |
|---|---|---|
| `Magic` | 1 | Correct — the entire coexistence strategy (§3 of the design) depends on this one byte never colliding with the legacy `0x0B`. Verified live on real sockets. |
| `ProtoMajor` | 1 | Correct placement for a future breaking redesign to branch on. Unused today (only value `2` exists) but costs nothing to reserve now. |
| `Flags` | 1 | Reasonable bitfield. Two of its four defined bits (`Guaranteed`, `Coalesced`) are declared but never actually produced or consumed anywhere in `JoinFS/` — see Findings 3a/3b. `Forwarded` is declared but has no relay logic yet either (expected — no `Jfp2Bridge`). |
| `SenderPeerId` / `RecipientPeerId` | 2 + 2 | Correctly negotiated and written on every send, but **not actually consulted on receive** — see Finding 4. Belongs on the envelope per the design's stated future payoff (cheap addressing through a relay); just not load-bearing yet. |
| `RawMessageClass` | 1 | Correct. The partition-by-flag-bit design (§4.3) is applied consistently; no collision between the two partitions' numbering was found. |

**Guaranteed-delivery extension** (4 bytes, spec'd in `Envelope.cs`'s doc comment, §4.4 of the design): declared but **entirely unimplemented** — see Finding 1.

**Hello / HelloAck** (`JoinFS/Jfp2/Negotiation.cs`, `HandshakeMessage`):

| Field | Size | Assessment |
|---|---|---|
| `ProtoMajorMin`/`Max` | 1 + 1 | Makes sense here specifically — this is the one place envelope-version compatibility has to be established before anything else is trusted. Correctly checked in `HandleJfp2Hello` (Node.cs) before any negotiation proceeds. |
| `Capabilities` | 8 | Correct scope (connection-wide optional behaviors, §5.4) — distinct from per-class schema versions, which is the right split. Currently always `0` (`LocalJfp2Capabilities`) since no capability has a real implementation yet (matches Finding 3a/3b — `Coalescing`/`QuantizedPosition`/`Ipv6Peers` bits exist but nothing sets them). |
| `SelfAssignedId` | 2 | Correct — this is the one place a fresh `PeerId` assignment has to be communicated. |
| `Result` | 1 | Correctly meaningful only in HelloAck; `Hello`'s copy is ignored, matching the doc comment. |
| `Offers` (`OfferCount` + entries) | 2 + 4×N | Correct shape and correctly round-tripped; `LocalJfp2Offers` (Node.cs) lists exactly the 10 classes that have real codecs — no orphaned offers, no missing offers for a registered codec (cross-checked against `CodecRegistry.Register` calls in `Network.cs`'s constructor). |
| `Extensions` (TLV) | variable | Sensible forward-compatibility mechanism, unused today (empty dictionary on both sides) — appropriately inert rather than half-built. |

### 2.1 Position (application class 0, schema v1) — `PositionCodec.cs`, 103 bytes fixed

| Field | Wire type | Assessment |
|---|---|---|
| `ObjectId` | `uint` | Belongs — identifies which aircraft this tick describes; `uint.MaxValue` shared-cockpit sentinel correctly preserved from legacy. |
| `NetTime` | `double` | Belongs — per-tick clock value needed for the receiver's RTT/jitter-compensated position reconciliation (`Sim.cs`'s `UpdateSimObjectVelocity`, per `docs/positioning-improvements.md`). This is exactly the kind of value that has to travel on the hot path; correctly not deferred to Identity. |
| `Latitude/Longitude/Altitude` | `double×3` | Belongs on the hot path by definition. |
| `Pitch/Bank/Heading`, velocity/angular-velocity/acceleration ×3 each | `float` | Belongs — all genuinely per-tick motion state. |
| `Rudder/Elevator/Aileron/BrakeLeft/BrakeRight` | `int16` fixed-point | Belongs — control-surface state changes every tick for a user-controlled aircraft; encoding matches `Sim.ConvertToAxis/ConvertFromAxis` exactly (same quantization the legacy wire already uses, not a JFP2-introduced precision change). |
| `Elevation`, `StaticCgToGround` | `float` | Belongs — both are per-tick ground-relative values the legacy `AircraftPosition` also resends every tick (not identity, despite sounding static-ish; `StaticCgToGround` varies as the sim recomputes it). |
| `StateFlags` (OnGround/ElevationCorrection/UserControlled/Paused) | `byte` | Belongs — all four are genuinely per-tick booleans in the legacy message too, just spread across two separate flag bytes there; consolidating them here is a clean simplification, not scope creep. |

No field here that should have stayed on Position was moved to Identity, and no field that
legitimately belongs on Identity (Callsign, Model, Livery, IcaoType, IcaoAirline, Registration,
FlightNumber, ClassCode, Wtc, ClassCodeConfirmed, TypeRole — all confirmed absent from
`PositionUpdate`) leaked back in. This split is the design's central claim and it holds up under
direct inspection of the struct.

### 2.2 Identity (application class 1, schema v1) — `IdentityCodec.cs`, variable length

| Field | Wire type | Assessment |
|---|---|---|
| `ObjectId` | `uint` | Belongs — same object-scoping role as on Position. |
| `IsAircraft`, `IsPlane` | 2 bits in a flags byte | Belongs here specifically *because* Position no longer implies object type via which legacy message class was used (`ObjectPosition` vs. `AircraftPosition`) — moving this distinction into Identity is the correct consequence of collapsing those two message types' data into one `PositionUpdate` shape. |
| `Callsign`, `Model`, `Livery`, `IcaoType`, `IcaoAirline`, `Registration`, `FlightNumber`, `ClassCode`, `Wtc` | length-prefixed UTF8 | All belong — every one changes only on join/livery-change/flight-change, exactly Identity's stated scope, and every one is a field the v26.4/v26.5 bug (`docs/protocol-changes-v26.4-v26.5.md` §1.2) was caused by conditionally appending to a hot message. `FlightNumber` was a Phase-3 omission fixed in Phase 4 — now present and tested. |
| `ClassCodeConfirmed` | 1 bit | Belongs — a rarely-changing confirmation flag paired with `ClassCode`/`Wtc`, correctly grouped with them rather than left on Position. |
| `TypeRole` | `byte` | Belongs — model-substitution category, changes only with `Model`, correctly co-located. |

No motion-state field leaked into Identity. The change-or-heartbeat send policy (diff against
`jfp2IdentitySendState`, resend every ≤4s regardless) matches the design's stated rationale (§6.2)
for why a bounded staleness window is acceptable here but would not be on Position.

### 2.3 VariableSync (application class 2, schema v1) — `VariableSyncCodec.cs`

| Field | Wire type | Assessment |
|---|---|---|
| `ObjectId` | `uint` | Belongs — same role as elsewhere. |
| `Entries[].Vuid` | `uint` | Belongs, and correctly reuses the existing `vuid` computation unchanged (design §6.5) rather than inventing a new identifier — the one field in the whole catalog explicitly designed to require *no* negotiation, and the implementation honors that. |
| `Entries[].Kind` | `byte` enum | Belongs — this is the field that structurally eliminates the "frame type implies value type" bug class (`docs/recording-protocol.md` §7.2) by making every entry self-describing instead of relying on which of three legacy messages carried it. |
| `Entries[].IntValue`/`FloatValue`/`StringValue` | 4/4/variable | Belongs — a straightforward union over Kind, correctly only one populated per entry. `StringValue` is length-prefixed UTF8 (`WireText`), **not** truncated to 8 ASCII bytes as originally implemented — see Finding 5, fixed 2026-09-14: the 8-byte cap was a JFP2-introduced regression versus the legacy `String8Variables` message it replaces, which was never wire-capped at 8 bytes despite its name. |

### 2.4 Event (application class 3, schema v1) — `EventCodec.cs`, 12 bytes fixed

| Field | Wire type | Assessment |
|---|---|---|
| `ObjectId` | `uint` | Belongs — identifies which object the event applies to; `uint.MaxValue` shared-cockpit sentinel preserved. |
| `EventId` | `uint` | Belongs — the SimConnect/X-Plane event identifier itself. |
| `Data` | `uint` | Belongs — the event's payload value (e.g. gear position). |

All three fields are a direct, sensible, minimal port of the legacy `SimEvent` shape — no field is
missing or superfluous. The problem with Event is not its fields; it's that the message stopped
being delivered reliably when it moved to JFP2 — see Finding 1.

### 2.5 FlightPlan (application class 4, schema v1) — `FlightPlanCodec.cs`

| Field | Wire type | Assessment |
|---|---|---|
| `ObjectId` | `uint` | Belongs. |
| `IcaoType`, `Departure`, `Destination`, `Rules`, `Route`, `Remarks`, `Alternate`, `Speed`, `Altitude`, `Callsign`, `Registration`, `IcaoAirline`, `FlightNumber` | length-prefixed UTF8 | All 13 belong — every one is a genuine flight-plan attribute, matching the legacy shape field-for-field. Collapsing the legacy `dataVersion≥21003`/`≥21006` conditional tail into always-present fields is a correct, low-risk simplification: the legacy *sender* already writes all 13 unconditionally on any current build (per `docs/protocol-changes-v26.4-v26.5.md` §1.4), so nothing that used to be conditionally absent becomes newly present. |

No `OwnerNuid` field — correctly omitted, consistent with the same-sender-identity precedent already
established by Position/Identity/VariableSync (the JFP2 envelope's sender *is* the owner until a hub
bridge exists to relay third-party flight plans).

### 2.6 Notes (application class 5, schema v1) — `NotesCodec.cs`

| Field | Wire type | Assessment |
|---|---|---|
| `Guid` | 16 bytes | Belongs — identifies the posting user, matches legacy. |
| `Nickname`, `Callsign` | length-prefixed UTF8 | Belong — both are per-message display fields in the legacy shape too (not deduplicated against a roster, matching existing behavior). |
| `NoteId` | `uint` | Belongs — legacy dedup/ordering key, unchanged. |
| `Age` | `float` | Belongs *for this message specifically* — it's a snapshot of the note's age at send time, matching the legacy field exactly; this is not a JFP2-introduced concept, and a live chat message is exactly the kind of thing where a one-shot age stamp (not a continuously-updated one) is the correct design, same as today. |
| `Channel` | `ushort` | Belongs — routing/filtering key, matches legacy. |
| `Text` | length-prefixed UTF8 | Belongs, obviously. |

The scope decision to port only the live single-note push and leave the bulk catch-up
(`SessionCommsRequest`/full-dump reply) on legacy is well-reasoned and documented (dead producers, an
already-broken length formula in the legacy nested-group shape) — not corner-cutting, a correct call
to not mechanically port dead/broken code. The fields that *were* ported are all correct for this
message. The reliability regression (Finding 1) is the actual issue with Notes, not its fields.

### 2.7 Weather / WeatherReply (application classes 6 / 9, schema v1) — `WeatherCodec.cs`

| Field | Wire type | Assessment |
|---|---|---|
| `Metar` | length-prefixed UTF8 | The only field, and it's the only field the legacy shape has either (`{Metar: string}`) for both `WeatherUpdate` and `WeatherReply`. Splitting into two message classes rather than one shared class is the right call given the two have independent reliability semantics and different receive-side handling (`SetWeatherObservation(nuid, metar)` — applies to a specific peer's aircraft — vs. `SetWeatherObservation(metar)` — updates this node's own weather); a peer could plausibly land on JFP2 for one and legacy for the other, and one shared class couldn't express that. |

`WeatherRequest` was correctly not ported (confirmed dead: zero call sites for
`WriteWeatherRequestMessage`, and the receive handler never reads the one field the message carries).
The one live gap here was, again, reliability: `WeatherReply` is a direct answer to a specific request
and legacy sends it guaranteed; JFP2 originally did not (Finding 1, **fixed 2026-09-14** — see §4) —
and unlike `Weather` (broadcast, implicitly "refreshed" whenever conditions next get reported) there
is no periodic resend to paper over a dropped reply, so a lost `WeatherReply` used to just leave the
requester without an answer; it's now retried until acked like its legacy counterpart.

### 2.8 Status / StatusRequest (application classes 7 / 8, schema v1) — `StatusCodecs.cs`

| Field | Wire type | Assessment |
|---|---|---|
| `StatusRequest.HubEnabled`, `HubListRequested` | 2 bits | Belong — both are legitimate one-shot request flags, matching legacy. |
| `StatusRequest.Uuid` | `uint` | Belongs — the requester's persistent id, used for hub registration. |
| `Status.Guid`, `AppVersion` | 16 bytes + string | Belong — node identity/version, matches legacy. |
| `Status.Users`, `AtcCount`, `AtcAirport`, `AtcLevel`, `Planes`, `Helicopters`, `Boats`, `Vehicles` | mixed | All belong — session summary counts, matches legacy field-for-field. |
| `Status.HubEnabled` + hub directory block (`Address`, `Name`, `About`, `Voip`, `NextEvent`, `Airport`, `ActivityCircle`, `GlobalSession`, `PasswordRequired`) | mixed | Belong together — this is one coherent "am I a hub, and if so here's my directory listing" sub-record, matching the legacy conditional block's grouping exactly, just unconditionally present now (correct simplification per the same reasoning as FlightPlan above — negligible cost at Status's frequency). |

Splitting `StatusRequest`/`Status` into two classes rather than overloading one with a discriminator
is the right call (they're structurally unrelated messages that happen to share a topic) and mirrors
the precedent already set by `Hello`/`HelloAck`.

**Cross-catalog observation:** every message class's fields are scoped correctly to that class's
actual update frequency — nothing hot-path-only leaked into a low-frequency message, and nothing
identity/rarely-changing leaked back into Position. This is the one property the whole redesign
exists to guarantee, and it holds under direct inspection of every codec.

## 3. What has and hasn't been verified live

Confirmed on real sockets (per the implementation plan's own notes, spot-checked against the code
paths they describe and found consistent):

- Legacy traffic is provably unaffected by JFP2 running alongside it, including against the real
  production hub (`joinfs.famtuduce.com:6112`) with an unmodified peer on the other end.
- Hello/HelloAck negotiation completes correctly over loopback, including the self-healing race
  described in Phase 1's notes, and correctly resolves the full 10-class offer table.
- `AssumedLegacy` fallback triggers correctly and permanently for a peer that never answers Hello.

**Update, 2026-09-14 — Position and Identity now confirmed live against a real simulator, closing
what was this review's single largest gap.** Outside this sandbox, the user built a `CONSOLE` v26.6
client and upgraded a hub to v26.6, and ran two scenarios (recorded in full in
`docs/protocol-v2-implementation-plan.md` Phase 4):

- **Two v26.6 CONSOLE instances (ports 6112/6113), both connected to the v26.6 hub, with a simulator
  attached.** All 3 aircraft appeared (local user aircraft + one injected object per peer), and
  moving the user aircraft on one instance was correctly reflected on the other, in both directions.
  This confirms real end-to-end Position delivery *and* Identity delivery (an injected aircraft
  cannot spawn at all without a correctly-applied Identity message feeding the pending-identity-cache
  object-creation path added in Phase 4) between two directly JFP2-negotiated peers — not just that
  the negotiation table resolves correctly, but that the resulting traffic actually drives a real
  simulator correctly.
- **One v26.6 client + one unpatched v26.5 (legacy-only) client, both against the v26.6 hub.**
  Position routing between the mixed pair worked as expected — the v26.5 peer never answers Hello, the
  v26.6 peer's session for it falls back to `AssumedLegacy`, and Position is sent legacy-format to that
  peer, exactly as designed. This is the first live confirmation of the coexistence/fallback path
  under actual position traffic (previously verified only via unanswered-Hello logging, not by
  confirming the resulting aircraft position actually still worked).

**Still not verified live in any phase:** VariableSync, Event, FlightPlan, Notes, and Weather/
WeatherReply send-and-receive with real triggering data (a changing animation variable, a gear-toggle
event, a posted chat message, a METAR request/update) — the reported test exercised Position and
Identity only. Code review found the receive-side logic for the untested classes to be a faithful
reuse of the exact legacy entry points (`Sim.UpdateAircraft`'s variable overloads, `HandleSimEvent`,
`ProcessCommsNote`, `SetWeatherObservation`), which is the right way to minimize risk in an unverified
path — but it remains unverified against live traffic. This is a smaller gap than before (the
highest-frequency, highest-risk class per the design doc's own §6.1 framing is now proven), but it is
not yet closed for the rest of the catalog.

## 4. Full finding list

**Finding 1 (blocking → FIXED 2026-09-14) — Guaranteed delivery is entirely unimplemented; three
ported message classes silently lost reliability they have under the legacy protocol.**

- `LocalNode.SendJfp2Application` (`JoinFS/Node.cs`) hardcodes `EnvelopeFlags.None` on every
  application-partition send — the `Guaranteed` flag is never set anywhere in the codebase.
- `Jfp2.Envelope.ReadFrom` never checks the `Guaranteed` flag to skip the spec'd 4-byte extension
  block — if any datagram ever did arrive with that bit set, the receiver would misparse the payload
  (read 4 bytes too early) rather than handle it. No ack/retransmit/`GuaranteedDone` logic exists
  anywhere under `JoinFS/Jfp2/` or the `#region JFP2` block in `Node.cs`.
- Concretely regressed relative to legacy: **Event** (legacy `WriteSimEventMessage`,
  `JoinFS/Network.cs:3512`, sends guaranteed), **Notes** (legacy `SendCommsNoteMessage`,
  `JoinFS/Network.cs:3937`, sends guaranteed), **WeatherReply** (legacy `WriteWeatherReplyMessage`,
  `JoinFS/Network.cs:2086`, sends guaranteed). All three become plain unreliable UDP the moment a peer
  negotiates JFP2 for that class.
- This is not called out as a deliberate tradeoff anywhere in the design doc or in any phase's
  "Deviation" notes in the implementation plan — every other behavioral change in this codebase was
  explicitly flagged and justified; this one reads as an oversight, not a decision.
- **Fix applied:**
  - `Jfp2.Envelope` (`JoinFS/Jfp2/Envelope.cs`) now carries `GuaranteedId`/`GuaranteedIndex`/
    `GuaranteedCount` and reads/writes the 4-byte extension block on `ReadFrom`/`WriteTo` whenever
    `Flags.Guaranteed` is set (`WireSize` reflects this too) — closing the receive-side misparse risk,
    not just the send-side gap.
  - `LocalNode.SendJfp2Application` gained a `bool guaranteed = false` parameter
    (`JoinFS/Node.cs`). When `true`, it assigns a `GuaranteedId`, sends with the extension populated,
    and tracks the send in a new `jfp2PendingGuaranteed` dictionary; `DoJfp2GuaranteedRetry` (called
    from `DoWork()` alongside `DoJfp2Handshake`) retries every `JFP2_GUARANTEED_RETRY_INTERVAL` (2s)
    up to `JFP2_GUARANTEED_MAX_ATTEMPTS` (5, matching the Hello retry cadence) before giving up and
    logging.
  - On receive, `Jfp2ReceiveMsg` acks every guaranteed datagram with a new internal-partition
    `GuaranteedDone` message (never itself guaranteed, to avoid an ack-of-an-ack loop) and
    de-duplicates via `jfp2RecentlySeenGuaranteed` (a retransmit that raced a lost ack gets re-acked
    but not re-dispatched to the application layer a second time); `HandleJfp2GuaranteedDone` on the
    sender's side removes the matching pending entry. Both new dictionaries are cleaned up when a peer
    departs (`RemoveJfp2GuaranteedStateForNode`, wired into the same `DoWork()` pass that already does
    `jfp2Sessions.Remove`) and the dedup cache is aged out on a 30s sweep inside
    `DoJfp2GuaranteedRetry`.
  - `Network.cs`'s three affected call sites (`SendEventUpdate`, `SendCommsNoteMessage`,
    `SendWeatherReply`) now pass `guaranteed: true`, matching their legacy counterparts exactly.
  - **Scope note:** this is not a full port of the legacy guaranteed-message machinery — every JFP2
    message implemented so far fits in one UDP datagram, so `GuaranteedIndex`/`GuaranteedCount` are
    always `0`/`1` (no real segmentation/reassembly). If a future JFP2 message class genuinely needs
    to span multiple datagrams, the wire format has room for it (§4.4) but the reassembly logic itself
    would still need to be added.
  - **Verified:** all 6 build configurations compile clean; full test suite 125/125 passing (4 new
    `EnvelopeTests` cases cover the extension block round-trip, `WireSize`, and the too-short-datagram
    rejection). **Not yet verified live** — the retry/ack loop itself has not been exercised against
    real packet loss; that needs the fault-injection methodology in §6, not a functional test.

**Finding 2 (should-fix before scaling Position → FIXED 2026-09-14) — The Position hot path still
allocates on every send, undercutting design goal #1.**

- `PositionV1Codec.Encode` correctly writes into a `stackalloc` buffer at the call site
  (`Network.cs:3222`) — no allocation there.
- But `LocalNode.SendJfp2Datagram` (`Node.cs:1729`) allocates a fresh `byte[]` for *every* JFP2
  datagram, `new byte[Envelope.FixedSize + payload.Length]`, before handing it to
  `UdpClient.Send(byte[], int, IPEndPoint)`. This applies to Position exactly as much as anything
  else, at simulator tick rate × broadcast-peer count.
- **Fix applied:** `SendJfp2Datagram` now rents a buffer from `ArrayPool<byte>.Shared` sized to the
  envelope + payload, writes into it, sends via `udpClient.Client.SendTo(ReadOnlySpan<byte>,
  SocketFlags, EndPoint)` (the underlying `Socket`'s span overload, available since .NET 5), and
  returns the rented array in a `finally` block so a send that throws doesn't leak it. This applies to
  every JFP2 send uniformly (Position included, since it's the same shared low-level method every
  other message class also funnels through), not a Position-specific carve-out.
- **Verified:** all 6 build configurations compile clean; full test suite 125/125 passing (no
  behavioral change to any codec — this only touches how the already-encoded bytes get to the wire).
  **Not yet profiled** — this review confirmed by inspection that the `new byte[...]` is gone and
  confirmed nothing broke, but did not attach `dotnet-trace`/`dotnet-counters` to measure actual GC
  pressure under many-broadcast-peer load; see §6.

**Finding 3 (low, inert today) — Two spec'd wire-format mechanisms are unimplemented; harmless only
because nothing currently triggers them.**

- 3a. `MessageClasses.Extended` (255): `Envelope.WriteTo`/`ReadFrom` never special-case a class byte
  of 255 to read/write the trailing 2-byte real class id the design (§4.6) specifies. Inert today
  (only 10 of 255 application-partition slots are used), but if ever relied on before being built, it
  will silently misparse rather than fail loudly.
- 3b. `EnvelopeFlags.Coalesced` / `Capability.Coalescing`: declared, included in the capability
  bitmask machinery, but no encode or decode path anywhere touches a coalesced payload. Inert today
  since `LocalJfp2Capabilities = Capability.None` — nothing ever offers or could receive Coalescing.
- **Recommendation:** no action needed before rollout as long as neither is exercised. Whoever
  eventually implements class-255 growth or coalescing should not assume either is wired up just
  because the flag/constant exists.

**Finding 4 (low, informational) — Envelope `PeerId` fields are written and negotiated but not yet
consulted on receive.**

- All receive-side peer identification (`Jfp2ReceiveMsg`, `HandleJfp2Hello`, `TryGetJfp2AppPeer`)
  goes through `FindNuidByEndPoint` — a linear scan matching the UDP source `IPEndPoint` against the
  legacy `nodes` dictionary — never through `envelope.SenderPeerId`/`RecipientPeerId`.
- This is consistent with, and explained by, `DoJfp2Handshake`'s own documented scope decision
  (direct peers only, no relay yet — a `PeerId` sent to an indirect peer's `routeEndPoint` would land
  on the wrong node). The compact `PeerId`'s real payoff (cheap addressing once a message can be
  relayed through `Jfp2Bridge` without a full readable address) simply doesn't apply yet.
- Not a bug for the current phase. Flagged so a future reviewer doesn't assume PeerId-based
  dispatch/validation is already in place — an attacker (or a bug) spoofing a JFP2 datagram's
  `SenderPeerId` today has no effect either way, since it's never read.

**Finding 5 (low, unconfirmed-but-plausible → first bullet CONFIRMED and FIXED 2026-09-14) — Two
string-encoding edge cases.**

- ~~`VariableSyncV1Codec`'s `String8` entries truncate to 8 bytes and encode as ASCII. Very likely
  correct (matches the presumed SimConnect `String8` datum's own 8-char/ASCII semantics)~~ — **this
  assumption was wrong, and the maintainer confirmed it by pointing at the evidence already sitting in
  the codebase.** `JoinFS/SimConnectInterface.cs` requests plenty of string data from the simulator at
  widths well past 8 (`CATEGORY`/`ATC ID`/`ATC MODEL`/`ATC FLIGHT NUMBER` at `STRING32`, `ATC AIRLINE`
  at `STRING64`, `TITLE`/`LIVERY NAME`/`LIVERY FOLDER` at `STRING256`) — "String8" is the name of one
  specific SimConnect datum category (`SIMCONNECT_DATATYPE.STRING8`, used only for the custom `L:var`-
  style variables `VariableMgr.Definition.Type.STRING8` declares), not a general "the simulator caps
  strings at 8 characters" rule. More importantly, checking the wire format this codec actually
  replaces settled it: the legacy `String8Variables` message's value is written with a plain
  `BinaryWriter.Write(string)` (`JoinFS/Network.cs`'s `SendString8VariablesMessage`) — an ordinary
  length-prefixed .NET string, never capped at 8 bytes on the wire despite the message's name. JFP2's
  fixed-8-byte-ASCII field was therefore a **real, JFP2-introduced regression** versus the protocol it
  replaces, not a faithful, low-risk port — any value over 8 bytes, or containing non-ASCII, was
  silently corrupted where legacy carried it losslessly.
  **Fix applied:** `VariableEntry.StringValue` now encodes via the same length-prefixed UTF8
  `WireText` helper every other string field in this codebase already uses (`JoinFS/Jfp2/Codecs/
  VariableSyncCodec.cs`), via a new `Span<byte>`-based `WireText.WriteString` overload (`JoinFS/Jfp2/
  Codecs/WireText.cs`) so the hot-ish (5Hz) `VariableSync` path still avoids a `List<byte>`
  allocation. `Network.SendJfp2VariableSync`'s send-buffer sizing, which previously assumed a flat
  worst-case-per-entry bound, now sums each entry's exact wire size (`VariableSyncV1Codec.EntrySize`)
  instead — the old fixed bound would have been too small for any String8 entry over 3 UTF8 bytes and
  corrupted the buffer write. Covered by 4 new/updated `VariableSyncCodecTests` (long value, short
  value, non-ASCII, and an `EntrySize`-sized-buffer round-trip). **Scope note, not fixed:** the
  SimConnect *write* side (`VariableMgr.Set.UpdateString8`, `Sim.String8Struct`) still marshals into a
  genuinely fixed 8-byte native buffer for a receiving SimConnect build's own locally-declared
  `STRING8` variables — a value over 8 bytes arriving for one of those specific local definitions
  (plausible if the sender is a non-SimConnect platform, e.g. an X-Plane peer whose dataref isn't
  8-byte-bounded) will fail to apply, caught and logged (`catch (Exception ex) ... "ERROR - Updating
  variable"`) rather than crashing. This is a pre-existing limitation shared identically by the legacy
  protocol (same `UpdateString8` method, same struct, reachable from `SendString8VariablesMessage` too)
  — not introduced or worsened by this fix, and out of this finding's scope, but noted for whoever
  picks up cross-platform variable sync next.
- `Jfp2.Codecs.WireText.WriteString` casts a UTF8 byte length to `ushort` with no bounds check;
  anything over 65535 bytes would silently truncate the length prefix. None of the current string
  fields (callsigns, liveries, METAR text, chat messages) plausibly reach that size, so this is a
  latent-but-inert risk rather than a live bug. **Not fixed** — still recommended only if a future
  field genuinely needs longer strings; a one-line bounds assertion (`Debug.Assert` or similar) would
  suffice then.
- **Verified:** all 6 build configurations compile clean; full test suite 127/127 passing.

**Finding 6 (medium, pre-existing — not introduced or worsened by JFP2, added 2026-09-15) — Injected-
object variable updates are held off for 3-5 seconds per vuid, and a shared composite vuid means one
light changing blocks all the others sharing it.**

- `VariableMgr.Set` (`JoinFS/VariableMgr.Set.cs`) enforces a per-vuid hold-off before an injected
  (remote) object's cached variable value may be overwritten by a newly-received one: `SLAVE_DELAY =
  3.0` seconds for an ordinary injected object, `MASTER_DELAY = 5.0` seconds for the shared-cockpit/
  entered-another-aircraft case (`Set.DelayTime`, lines 328-344). `UpdateInteger`/`UpdateFloats`/
  `UpdateString8` all gate on this **before** checking whether the incoming value differs from the
  cached one (line 507: `if (startTimes.ContainsKey(vuid) == false || startTimes[vuid] <
  main.ElapsedTime)`), so an update landing inside the window is dropped outright, not queued —
  whatever value the next post-window message happens to carry is what gets applied.
- Landing, taxi, nav, and beacon lights are not four independent variables on the wire or in this
  hold-off's bookkeeping: `Variables.cs`'s built-in `Plane.txt`/`Rotorcraft.txt` definitions declare
  them as masked bits of one shared SimConnect variable, `LIGHT STATES` (`mask:0..3`,
  `Variables.cs:176-179`). Registration (`Variables.cs:902-921`) creates one shared composite
  `Definition` (`maskVuid = CreateVuid("LIGHT STATES")`, `mask == 0`) that all four sub-variables point
  at; on receive, `UpdateInteger` only ever writes to SimConnect for that one composite vuid (`mask ==
  0`, line 526) — the individual per-bit booleans update a local dictionary entry only and never reach
  SimConnect (lines 561-567). This means nav/beacon/landing/taxi genuinely share one hold-off window:
  toggling any one of them re-arms the 3-second block for all four.
- Confirmed reachable by both receive paths identically: legacy `IntegerVariables`
  (`Network.cs:5218-5265`) and JFP2 `VariableSync` both terminate in `Sim.UpdateAircraft(ownerNuid,
  netId, Dictionary<uint,int>)` (`Sim.cs:2440`), which calls
  `controlledAircraft.variableSet.UpdateIntegers(variables)` unconditionally — this is not a
  JFP2-specific code path.
- **Observed effect, field-tested 2026-09-15** (see `docs/protocol-v2-implementation-plan.md`'s
  field-test log): toggling landing and taxi lights together — a natural real-world action —
  reproduced a 0-3 second lag on a remote peer in both a JFP2-negotiated direct-mesh pair and a mixed
  JFP2/legacy pair, matching this mechanism's predicted worst case. Strobe is the one exception in
  `Plane.txt` (its own dedicated `LIGHT STROBE`/`STROBES_SET` variable/event, unmasked,
  `Variables.cs:180`) — it has its own independent hold-off and doesn't contend with the other four; in
  `Rotorcraft.txt`, strobe is folded into the same `LIGHT STATES` mask, so all five compete there.
- **Not fixed, not a JFP2 regression** — this predates JFP2 entirely and is reached identically by both
  transports, so it is out of scope for a JFP2-specific fix, but it materially affects how quickly
  *any* remote light/switch state is perceived to update regardless of which transport carries it.
  Worth its own investigation: whether `SLAVE_DELAY`/`MASTER_DELAY` should be shorter (or skipped)
  specifically for boolean/mask-derived integer variables, since the delay's likely original purpose —
  damping rapid-fire SimConnect writes for continuously-varying values like radio frequencies or trims
  — doesn't obviously apply to a variable that changes rarely and discretely.
- **Still open:** a related but distinct field observation — a legacy v26.5 peer reportedly seeing a
  remote light stuck "always on" rather than merely delayed — is not fully explained by this hold-off
  alone (a hold-off predicts delay, not permanent staleness) and needs a live re-test with
  `monitor.variables`/`monitor.network` logging on both ends to resolve; see the field-test log for the
  specific hypotheses to check.

**Finding 7 (low severity, narrow trigger → FOUND AND FIXED 2026-09-16) — a JFP2 Hello retry could
reach the wrong endpoint, and corrupt an unrelated peer's session state, when a peer's route to
another peer switches to a relay sharing that peer's own IP address.**

- This surfaced while live-testing the exact scenario this session was asked to verify: two
  JFP2-capable (v26.6) JoinFS instances that **cannot reach each other directly at all**, with every
  message relayed through a JFP2-capable hub — distinct from the "JFP2 negotiates a direct mesh path
  via hub-assisted rendezvous" scenario the 2026-09-15 field tests already covered (both of those ended
  up direct once Pathfinder found a path).
- **Test setup:** one `CONSOLE-Debug` hub + two `CONSOLE-Debug` clients on `127.0.0.1`, with
  `LocalNode.ReceiveMessages()` temporarily patched (env-var gated, reverted after) to drop any
  datagram — legacy or JFP2 — arriving from the other client's port, forcing Pathfinder to conclude
  Indirect and forcing all real client-to-client traffic through the hub's pre-existing `FLAG_FORWARD`
  relay (`Node.cs`'s `ReceiveMsg`).
- **Confirmed correct, independent of the bug below:** hub↔client A and hub↔client B each negotiate
  JFP2 cleanly on their own; the two clients never complete (and, after the fix, never even usefully
  attempt) a JFP2 session with each other; the legacy `FLAG_FORWARD` relay — completely unmodified by
  JFP2 — carries every real message between the two clients throughout the session (confirmed live via
  repeated `NETWORK: Forwarded <A> <B>` / `<B> <A>` log lines at Pulse cadence). This is the "same
  functionality as before JFP2" outcome the design intends for a relay-only peer pair, now confirmed
  live rather than only by code inspection.
- **The bug:** `DoJfp2Handshake`'s guard against ever attempting Hello with an indirect peer
  (`Node.cs`, pre-fix) used `Node.Direct`, a property comparing only the IP **address** of a node's own
  claimed endpoint against its current `routeEndPoint` (`endPoint.Address.Equals(routeEndPoint.Address)`,
  `Node.cs:66`). This is a reliable signal in essentially every real deployment, since a hub and a
  client are normally different hosts with different IPs — but a client's `routeEndPoint` for another
  peer can be reassigned to route through the hub *before* this address-only check reflects it, if the
  hub happens to share the same IP address as the peer being relayed to (necessarily true for any
  same-machine test, since hub and both clients share `127.0.0.1`; also plausible for a real deployment
  where someone self-hosts a hub on the same machine they fly from). When hit, `DoJfp2Handshake`
  retried a Hello addressed to the just-reassigned `routeEndPoint`, landing on the hub's own socket
  instead of the intended peer's. Because the hub already had its own separate, already-negotiated
  JFP2 session with the *true sender* (from its own direct hub↔client negotiation), `HandleJfp2Hello`'s
  unconditional `session.RemoteAssignedId = hello.SelfAssignedId` (`Node.cs`, formerly line 1999)
  overwrote that unrelated, already-correct session's `RemoteAssignedId` with an unrelated PeerId the
  sender had generated for its *other*, indirect peer-session object — silently corrupting session
  state. This is exactly the "worst case" `DoJfp2Handshake`'s own pre-existing comment predicted
  ("a JFP2-capable relay completes a handshake *as itself*") but had not previously been shown to
  actually occur.
- **No observable functional break today:** per Finding 4, no current receive-path code validates
  `RecipientPeerId`/`SenderPeerId` on receive (`Jfp2ReceiveMsg` routes purely by
  `FindNuidByEndPoint`), so the corrupted field was, at the time this was found, write-only and never
  read back. The two clients' own mutual negotiation attempt still correctly self-limited (5 attempts,
  ~10s, `AssumedLegacy = true`) even before this fix, and the hub↔client sessions kept working
  throughout — which is why this produced no visible symptom without inspecting network traces
  directly.
- **Fix applied:** added `Node.RouteIsOwnEndPoint` (full `IPEndPoint.Equals` — address *and* port —
  rather than `Direct`'s address-only comparison) and switched the three JFP2-only call sites that
  need this precise a check — `DoJfp2Handshake`'s two `node.Direct` checks and
  `TryGetJfp2AppPeer`'s — to use it instead; `GetNodeJfp2State`'s UI-display branch was updated the
  same way for consistency. Legacy code, which relies on `Direct`'s existing address-only semantics
  for its own unrelated relay-forwarding decision (`Node.cs`'s `ReceiveMsg`, the
  `value1.Direct` check), is untouched.
- **Verified:** re-ran the identical localhost hub-relay scenario after the fix — the stray
  attempt-4/5 Hello and the hub's spurious duplicate "Hello from X - handshake complete, replying with
  HelloAck" are both gone; the session for the indirect peer is now silently dropped the moment
  `routeEndPoint` diverges from that peer's own endpoint, before any misdirected send can happen. All
  6 build configurations compile clean (0 warnings beyond the pre-existing, unrelated `XPlane.cs` one);
  full test suite 127/127 passing.

**Finding 8 (high severity, pure legacy code, pre-dates JFP2 → FOUND AND FIXED 2026-09-16) —
guaranteed-delivery legacy messages (SimEvent, Notes, WeatherReply) could never reach a genuinely
indirect (hub-relayed) peer at all, on any protocol version, before or after JFP2.**

- Found while live-testing Position/VariableSync/Event propagation for the hub-relay scenario (Finding
  7's own test setup) against a real MSFS2024 session, per the maintainer's request to verify these
  three actually work, not just negotiate. Position and VariableSync (light states, flaps, gear, prop
  RPM — all triggered live in the sim and confirmed applied to the injected aircraft on both ends) both
  worked correctly first try. `SimEvent` did not: a synthetic event fired locally on one client
  (`Sim.ProcessEvent`, since no control on the test aircraft in MSFS2024 maps to the narrow
  `EVENT_00011000`..`EVENT_0001100A` range JoinFS treats as a discrete event — lights/flaps/gear all
  route through VariableSync instead, confirmed by direct SimConnect-level tracing) was never received
  by the other client, despite `Network.HandleSimEvent`/`Sim.ProcessEvent`'s own broadcast-eligibility
  logic (`IsBroadcast`, `Injected`, `Connected`, peer count) all resolving correctly and the message
  being confirmed queued.
- **Root cause:** `LocalNode.Send(IPEndPoint, byte[], int)` (`Node.cs`), when the caller requested a
  guaranteed send, correctly resolves the peer's current **route** endpoint (`value.routeEndPoint` —
  the hub, for an indirect peer) and stores it on the queued `GuaranteedMessageOut`. But a guaranteed
  send never transmits immediately — queuing only adds to `guaranteedOutList`; the actual
  `udpClient.Send` happens exclusively inside `DoGuaranteedMessages()`'s periodic sweep (every tick, and
  every 2s thereafter for retries). That sweep re-resolved the destination itself, independently, via
  `value.endPoint` — the peer's own **claimed direct** address (the same field the class's own doc
  comment calls "direct endpoint of recipient") — silently discarding the correct `routeEndPoint`
  resolution from queue time. For a genuinely indirect peer, `value.endPoint` is by definition an
  address that peer is not reachable at, so **every** transmission attempt, including the first one,
  targeted the wrong address. Unreliable (non-guaranteed) sends don't hit this path at all — they
  transmit immediately from the correctly-resolved endpoint passed into `Send` — which is exactly why
  Position and VariableSync were unaffected and this went undetected until a guaranteed message was
  specifically live-tested.
- **Scope: this is not a JFP2 bug.** `Send`/`DoGuaranteedMessages` are pure legacy code, untouched by
  any JFP2 phase (confirmed: no JFP2 commit or this session's Finding 7 fix touches either method). It
  has presumably existed since the guaranteed-delivery mechanism itself was written, affecting the
  **legacy** protocol equally before JFP2 existed — `SimEvent`, `Notes` (live chat push), and
  `WeatherReply` are the three legacy message types sent guaranteed
  (`docs/protocol-v2-implementation-review.md`'s own Finding 1 lists the same three for JFP2's separate,
  since-fixed guaranteed-delivery gap). A relay-only peer pair talking over legacy could apparently never
  have exchanged any of these three correctly, on any past version — a real, if narrow (relay-only
  topologies are less common than direct mesh), regression against "same functionality as always."
- **Fix applied:** `DoGuaranteedMessages()`'s endpoint resolution now uses `value.routeEndPoint`
  instead of `value.endPoint`, matching `Send`'s own already-correct resolution — re-resolved fresh on
  every retry (not just once at queue time), so a peer that goes direct mid-retry also picks that up
  immediately rather than waiting for a new guaranteed send to be queued.
- **Verified live, real end-to-end confirmation against MSFS2024:** re-ran the hub-relay test scenario
  (two indirect peers, blocked from reaching each other directly, both with a real SimConnect
  connection) with the fix applied. The synthetic `SimEvent` was queued, correctly retried against the
  hub's address once `routeEndPoint` resolved to it, forwarded by the hub's ordinary `FLAG_FORWARD`
  relay (confirmed via the `NETWORK: Forwarded` log line — this also finally confirms, after repeated
  confusing negative results earlier in the same investigation, that hub forwarding itself works
  correctly and was never in question; the earlier confusion was this endpoint bug preventing the
  guaranteed message from ever reaching the hub in the first place), received by the other client
  (`HandleSimEvent`), and applied to the injected aircraft via SimConnect
  (`DoSimEvent ID '...' - Event 'EVENT_00011000' - Data '424242'`). Position and VariableSync were
  independently confirmed the same session with real triggered data (landing/taxi lights, flaps, gear,
  prop RPM — each detected on the sending side and correctly applied on the receiving side's injected
  aircraft). All 6 build configurations compile clean; full test suite 127/127 passing.

## 5. What "ready" would look like

The design and the phased implementation plan are both sound, and the field-level audit in §2 found
no structural problems — every message class carries exactly the fields that belong at its own update
frequency, and the Position/Identity split (the redesign's central claim) holds up under direct
inspection. The remaining work to go from "verified negotiation and now verified Position/Identity
traffic" to "ready for a real rollout" is concrete and bounded:

1. ~~Resolve Finding 1 (guaranteed delivery)~~ — **done 2026-09-14** (§4): implemented, not just
   documented as a tradeoff. Still needs a live packet-loss test (§6) before being fully trusted, but
   is no longer a design gap.
2. ~~Resolve Finding 2 (hot-path allocation)~~ — **done 2026-09-14** (§4). Still needs profiling under
   real broadcast-peer scale (§6) to confirm the GC-pressure claim, but the allocation itself is gone.
3. ~~Get one real simulator-attached test of live Position/Identity traffic between two patched
   builds~~ — **done 2026-09-14** (§3). Extend the same kind of live test to VariableSync/Event/
   FlightPlan/Notes/Weather before treating the whole catalog as field-proven, not just Position/
   Identity. Now also re-run the Event/Notes/WeatherReply portion specifically to confirm the Finding 1
   fix actually delivers under real conditions, not just in unit tests.
4. With 1–2 done and 3 partially done: the main remaining gate before considering any "prefer JFP2
   when negotiated" default-on behavior is live confirmation of the remaining message classes (item 3)
   and, ideally, the profiling/fault-injection work in §6. `Jfp2Bridge` (hub translation) remains
   correctly out of scope until direct-peer JFP2 has more field experience, per the implementation
   plan's own reasoning — nothing in this review changes that call.
5. ~~Confirm the hub-relay-only scenario (two JFP2 peers unreachable from each other, everything via
   a JFP2-capable hub) doesn't regress~~ — **done 2026-09-16** (Finding 7, §4): live-tested and, after
   fixing the bug the test surfaced, confirmed byte-for-byte legacy behavior for such a pair.

## 6. What still needs profiling or fault injection (not attempted here)

Fixing Findings 1, 2, and 5 (§4) closed the *design* gaps this review found, but none of the three
fixes has been validated with the kind of testing that would actually prove the fix works under the
conditions it was written for — that's a different kind of verification than code review, build, and
unit tests can provide:

- **Finding 1's retry/ack loop under real packet loss.** The unit tests confirm the wire format
  round-trips correctly; they don't confirm the retry timer, the dedup logic, or the
  `JFP2_GUARANTEED_MAX_ATTEMPTS` give-up path behave correctly when datagrams are actually being
  dropped. Proving this needs a lossy link (e.g. `tc netem` on Linux, or an equivalent Windows tool) or
  a temporary fault-injection hook, run against two real JFP2-negotiated peers exchanging Event/Notes/
  WeatherReply traffic while loss is induced.
- **Finding 2's allocation fix under real broadcast-peer load.** Confirming the `ArrayPool`-based
  `SendJfp2Datagram` actually eliminates the GC pressure the original code caused needs a profiler
  (`dotnet-trace`/`dotnet-counters`) attached to an instance broadcasting Position to a realistic
  number of simultaneous peers, comparing GC gen-0 collection frequency before/after.
- **Finding 5's String8 fix with a real long/non-ASCII value.** The unit tests cover the codec's wire
  format directly, but every live test run so far (Position/Identity, §3) used `--nosim` peers with no
  actual variable data flowing, so `VariableSync` traffic has never been observed live at all, let
  alone with a value that would have exposed the old 8-byte truncation. A real test would need two
  JFP2-negotiated peers with a simulator attached, syncing a custom `L:var` (or X-Plane dataref) whose
  value is deliberately set longer than 8 characters or contains non-ASCII text, confirmed to arrive
  intact on the peer (and, separately, exercising the cross-platform SimConnect-write caveat this
  finding's writeup flags — a long value landing on a receiving SimConnect build's own 8-byte-declared
  local variable — to confirm it fails as gracefully in practice as the code's `try`/`catch` suggests).

All three are tracked here so they aren't lost, but none blocks the functional rollout progression in
§5 — they're a different, lower-priority category of verification than "does the feature work."
