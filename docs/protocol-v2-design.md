# JFP2: A New JoinFS Network Protocol — Design Document

## Status

Design specification for JFP2. This document describes the protocol itself — wire format, negotiation,
message catalog, and rollout strategy — as a guide for implementing it; it does not track the state of
any particular build. For implementation progress, phase-by-phase deviations from this design, and
field-verification results, see `docs/protocol-v2-implementation-plan.md` and
`docs/protocol-v2-implementation-review.md`.

This document assumes familiarity with `docs/network-protocol.md` (the current wire protocol),
`docs/recording-protocol.md` (the current recording format and its extension recommendations), and
`docs/protocol-changes-v26.4-v26.5.md` (a concrete case study of what goes wrong today when the
protocol changes). Citations below refer to sections in those documents.

## 1. Motivation

The current protocol works, but three concrete, previously-documented problems motivate a redesign
rather than another incremental patch:

**1.1 One global version number gates everything.** `DataVersion` (docs/network-protocol.md §3) is a
single number that every message on the wire is implicitly validated against. When any one message's
shape changes, the version has to move for the whole protocol, and every receiver has to reason about
"what does dataVersion N mean for message X" for every X, not just the one that changed. The v26.4→
v26.5 change (docs/protocol-changes-v26.4-v26.5.md §1.1) needed an intermediate version (21006) purely
as a migration step for fields on two unrelated messages (FlightPlan, UserList2), and the version
number itself carries no structured information about which fields are actually present — receivers
resolve that with ad-hoc EOF-sensing (§1.3 below) instead of the version number telling them directly.

**1.2 Version gates that don't match compile-time gates.** The single most concrete bug uncovered
across both prior documents: `Recorder.Obj.Write`/`Read1` gate the Livery/IcaoType/IcaoAirline tail
with `#if FS2024` (a compile-time decision, baked into the binary), while the network-side equivalent
gated the same fields with a *runtime* `dataVersion` check. A FS2020 build and an FS2024 build produce
different file/wire shapes for what is nominally "the same version," and `git show v26.4:...` vs.
`v26.5:...` proves this exact defect survived the v26.4→v26.5 release unchanged
(docs/protocol-changes-v26.4-v26.5.md §2). Any design that continues to mix compile-time and run-time
version gating on the same fields will reproduce this class of bug indefinitely.

**1.3 EOF-sensing as the extension mechanism.** Both the wire protocol and the recording format
extend messages by appending fields and having the reader keep reading until the stream runs out
(docs/network-protocol.md §9.2, docs/recording-protocol.md §6). This works only as long as nothing is
*ever* read after the extended tail, and only as long as every build's tail-reading loop agrees, byte
for byte, on how many bytes each optional field consumes — which is exactly what broke in §1.2. It
also means a receiver can't skip a field type it doesn't recognize without knowing that field's exact
length in advance, which rules out ever inserting a genuinely new, independent field type in the
middle of an existing message without a coordinated flag day.

**1.4 No peer capability exchange.** Two peers today have no way to tell each other "here is the
schema range I can speak" — everything is inferred from a single connection-wide `dataVersion` set at
Join time (docs/network-protocol.md §4.1) with no way to negotiate per-message-class differences, no
capability bits, and no extension area in the handshake itself.

**1.5 Header overhead on every packet.** The 21-byte fixed transport header
(docs/network-protocol.md §2) is paid on every single datagram regardless of content, including 4
bytes of guaranteed-delivery bookkeeping on datagrams that are never guaranteed (the overwhelming
majority — Position updates are the highest-frequency message in the system and are never sent
guaranteed). At typical multiplayer session tick rates this header tax is pure waste on the hottest
path in the system.

**1.6 IPv4-only peer addressing.** `Nuid` (docs/network-protocol.md §9.5) hard-codes a 4-byte IPv4
address into every peer identifier used throughout the protocol, including inside membership lists
and the recording format. There is no room to describe an IPv6 peer without changing the size of a
structure that many other structures embed by value.

None of these are proposals to rewrite for its own sake — each is a specific, previously-documented
defect. The JFP2 design below addresses all six directly, while also treating raw speed
(§2, §6) as the primary constraint the rest of the design must not compromise.

## 2. Design goals and non-goals

**Goals, in priority order:**

1. **Speed.** The hot path (Position updates, the highest-frequency message type in the system) must
   be smaller on the wire and cheaper to encode/decode than today, with zero heap allocation per
   message and no per-packet branching on version once a peer's capabilities are known.
2. **Extensibility without a flag day.** New fields, new message classes, and new schema versions of
   an existing message class must be addable without breaking any peer that hasn't been updated, and
   without requiring every message class to bump in lockstep.
3. **Backward compatibility, explicit rather than emergent.** Every version of every message class is
   handled by an explicit codec (§5), not by a reader that keeps consuming bytes until it runs out.
   Old and new builds coexist on the same mesh and the same UDP port.
4. **Peer capability/version exchange.** Peers explicitly tell each other, once, per connection, which
   schema version of which message class they support, plus a set of optional capability bits — see
   §5.

**Explicit non-goals:**

- **Security/authentication.** Per the request that motivated this design, security is not a primary
  concern here. JFP2 carries no encryption, signing, or replay protection beyond what the legacy
  protocol already has (effectively none). This should be revisited separately if JoinFS's threat
  model changes; it is called out here so it is not mistaken for an oversight.
- **Reliability semantics beyond what exists today.** JFP2 keeps the same two-tier reliable/unreliable
  delivery model as the legacy protocol (docs/network-protocol.md §2, the `Guaranteed` flag). It does
  not introduce ordered streams, congestion control, or anything resembling a QUIC/TCP-style
  connection.
- **Changing the simulator abstraction layer, model matching, or anything above the wire format.**
  This is purely a transport/serialization redesign.
- **A single "flag day" cutover.** JFP2 is designed to run *alongside* the legacy protocol
  indefinitely (§7), not to replace it in one release.

## 3. Architecture overview

Three pieces, cleanly separated:

- **Envelope** (§4): a fixed, minimal transport header on every datagram, carrying just enough to
  route and dispatch the payload. Analogous in role to the current 21-byte header, but 8 bytes for the
  common (unreliable) case and 12 for guaranteed delivery.
- **Negotiation** (§5): a Hello/HelloAck handshake, run once per peer connection, that produces a
  flat, per-message-class lookup table of agreed schema versions plus a bitset of agreed optional
  capabilities. This table is computed once and then indexed, not recomputed or consulted with
  conditionals on the hot path.
- **Codecs** (§6): one independent encoder/decoder pair per (message class, schema version). Adding a
  new version of a message class means adding a new codec and registering it — no existing codec, and
  no other message class, is touched.

Both stacks — legacy and JFP2 — can share one UDP socket and port, because every JFP2 datagram starts
with a magic byte (`0xFA`) that can never collide with the legacy protocol's fixed first byte (`0x0B`,
the low byte of the little-endian `0x520B` constant). A receiver looks at byte 0 and dispatches to the
appropriate stack before parsing anything else. This is what makes "coexistence" concrete rather
than aspirational: a mesh can have a mix of legacy-only and JFP2-capable peers, and every peer decides
per-datagram, per-remote-address, which stack to use — with no shared mutable state between the two
paths.

## 4. Wire format specification

### 4.1 Fixed envelope (8 bytes)

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | Magic | Always `0xFA`. Distinguishes JFP2 from legacy (`0x0B`) at a glance. |
| 1 | 1 | ProtoMajor | Currently `2`. A future breaking redesign (JFP3) would branch to a wholly different layout at this byte; this document only specifies ProtoMajor 2. |
| 2 | 1 | Flags | See §4.2. |
| 3 | 2 | SenderPeerId | u16, little-endian. Negotiated at handshake time (§5.3) — never a raw address. |
| 5 | 2 | RecipientPeerId | u16, little-endian. `0` means broadcast to the whole mesh (mirrors the legacy null-recipient convention). |
| 7 | 1 | RawMessageClass | See §4.3. |

### 4.2 Flags (byte, bitfield)

| Bit | Name | Meaning |
|---|---|---|
| 0 | Guaranteed | This datagram wants acknowledgement/retransmission. When set, a 4-byte extended block (§4.4) immediately follows the fixed 8 bytes. |
| 1 | Forwarded | Already relayed once by an intermediate mesh node (equivalent to the legacy `FLAG_FORWARD`). |
| 2 | Coalesced | The payload is a sequence of sub-messages, each self-length-prefixed (§4.5), rather than a single message body. |
| 3 | Internal | `RawMessageClass` indexes the internal/session-management partition rather than the application partition (§4.3). |
| 4-7 | Reserved | Must be zero on send; ignored (not rejected) on receive, per §7.4. |

### 4.3 Message class partitions

Unlike the legacy `MESSAGE_ID` enum, whose numeric values are assigned by declaration order (so
inserting a new member can silently renumber every member after it unless every build is rebuilt in
lockstep), every JFP2 message class value is an **explicit, permanently reserved constant**. The same
byte range (0–254, with 255 reserved as an escape hatch, §4.6) is reused for two independent
partitions, disambiguated by the `Internal` flag:

**Internal partition** (session/mesh management — Hello, Join, Pulse, and their kin):

| Value | Class |
|---|---|
| 0 | Hello |
| 1 | HelloAck |
| 2 | Join |
| 3 | JoinReply |
| 4 | Leave |
| 5 | Pulse |
| 6 | PulseResponse |
| 7 | Pathfinder |
| 8 | PathfinderResponse |
| 9 | GuaranteedDone |

**Application partition** (simulator data):

| Value | Class |
|---|---|
| 0 | Position |
| 1 | Identity |
| 2 | VariableSync |
| 3 | Event |
| 4 | FlightPlan |
| 5 | Notes |
| 6 | Weather |
| 7 | Status |

The rule going forward is simple and permanent: **always append a new class at the next unused number
in the correct partition; never renumber or reuse a value that has ever shipped.** This alone
eliminates the class of bug where reordering source lines changes wire semantics.

### 4.4 Guaranteed-delivery extension (4 bytes, present only when Flags.Guaranteed is set)

| Offset (from end of fixed header) | Size | Field |
|---|---|---|
| 0 | 2 | GuaranteedId |
| 2 | 1 | GuaranteedIndex |
| 3 | 1 | GuaranteedCount |

Semantically identical to the legacy protocol's guaranteed-delivery fields
(docs/network-protocol.md §2), just no longer paid for on every packet. This is the single biggest
mechanical source of the header-size win in §6: the legacy header always carries these 4 bytes;
Position, the highest-frequency message class in the system, never uses guaranteed delivery, so JFP2
never pays for them there.

### 4.5 Coalesced payload (present only when Flags.Coalesced is set)

A sequence of `(MessageClass: byte, Length: u16 little-endian, Body: Length bytes)` triples filling
the rest of the datagram. This lets several small, unrelated updates (say, a Position tick for one
object and a Status update for another) share a single UDP send when they happen to become ready in
the same tick, amortizing UDP/IP overhead without requiring every message class to know about batching
itself — coalescing is purely an envelope-level concern, invisible to codecs.

### 4.6 The `Extended` escape hatch

`RawMessageClass == 255` means the real class id is a two-byte little-endian value immediately
following the fixed header (before any payload). This gives headroom past 255 classes per partition
without ever widening the 8-byte fixed header for the 255 values already in everyday use — a direct
application of the same "never break the common case to make room for the rare case" principle used
throughout this design.

### 4.7 Peer addressing payload type: `PeerKey`

The 8-byte envelope carries only small negotiated `PeerId` values (§5.3), never a raw address — but
*payloads* that need to describe a peer other than the immediate sender (membership lists, the
`JoinReply` equivalent, Pathfinder targets) need a self-describing address structure. `PeerKey` fixes
the legacy `Nuid`'s IPv4-only limitation (§1.6) additively:

| Field | Size | Notes |
|---|---|---|
| Family | 1 | `4` = IPv4, `6` = IPv6 |
| Address | 4 or 16 | Network byte order; length determined by `Family` |
| Port | 2 | u16, little-endian |
| Local | 1 | Last octet of the LAN address — disambiguates instances behind one NAT, same role as the legacy `Nuid.local` field |

Because each `PeerKey` entry declares its own family (and therefore its own size), a reader that only
understands `Family == 4` can still correctly skip over a `Family == 6` entry it doesn't recognize by
computing its length the same way — no flag day is required to introduce IPv6 peers; older builds
simply skip them when scanning a list, exactly the way `docs/recording-protocol.md`'s extension
recommendations propose handling unknown record types.

## 5. Capability and schema negotiation

### 5.1 Why negotiate per message class, not globally

The core structural change from the legacy protocol's single `DataVersion`: each message class tracks
its own schema version, and a peer declares the *inclusive range* of versions it can both encode and
decode for each class it knows about. Two peers can therefore run Position at schema v2 (say, because
both have the newest build) while running VariableSync at v1 (because one of them is older), with no
coupling between the two decisions and no synthetic "combined" version number that means nothing on
its own.

### 5.2 Handshake messages: Hello / HelloAck

Sent as `Internal`-partition messages (§4.3) using the standard JFP2 envelope. Because `PeerId`
assignment is itself the subject of this exchange, the very first `Hello` a node sends a new peer has
no meaningful `RecipientPeerId` yet — the reply is addressed back to the UDP source `IPEndPoint`,
exactly like the legacy `Join`/`JoinReply` exchange already does.

Wire layout of the Hello/HelloAck payload (identical shape for both; `Result` is meaningful only in
HelloAck):

| Field | Size | Notes |
|---|---|---|
| ProtoMajorMin | 1 | Lowest envelope version (§4.1 byte 1) this build can speak. |
| ProtoMajorMax | 1 | Highest envelope version this build can speak. |
| Capabilities | 8 | Bitset, see §5.4. |
| SelfAssignedId | 2 | The `PeerId` the sender wants to be addressed by from now on. |
| Result | 1 | HelloAck only: `0` = Accepted, `1` = NoCompatibleProtoMajor. Ignored on Hello. |
| OfferCount | 2 | Number of `SchemaOffer` entries that follow. |
| Offers | 4 × OfferCount | Each: `Internal(1) MessageClass(1) MinVersion(1) MaxVersion(1)`. |
| Extensions | variable | TLV area, see §5.5. |

### 5.3 Resolution algorithm

For each `(Internal, MessageClass)` key mentioned by *either* peer's offer list, compute:

```
lo = max(local.MinVersion, remote.MinVersion)
hi = min(local.MaxVersion, remote.MaxVersion)
agreed = hi if hi >= lo else 0
```

A class that only one side mentions still gets an entry (the missing side's range is treated as
absent, which forces `agreed = 0` unless the present side's range happens to include 0). **Version 0
is reserved, by convention, to always mean "the baseline schema every build understands"** — the
wire-compatible equivalent of what that concept already looked like under the legacy protocol. This
means a class either peer has never heard of degrades to a known-good baseline instead of failing the
whole handshake, which is what makes it safe to introduce a brand new message class incrementally:
old peers simply never offer it, every negotiation involving them resolves it to 0, and 0 is defined
to mean "don't send this to this peer" for classes with no meaningful baseline (VariableSync, for
example: a peer that never declares VariableSync support resolves it to 0 and is simply never sent
VariableSync messages, exactly mirroring how that peer behaves today under the legacy protocol, which
does not know that message either).

The result is stored as two flat 256-entry byte arrays per peer session — `AgreedAppVersion[]` and
`AgreedInternalVersion[]`, indexed directly by `MessageClass` — computed exactly once when the
handshake completes. **This is the concrete mechanism behind the "shave the last millisecond" goal**:
the hot send/receive path never branches on version or capability again for the lifetime of the
session with that peer. Sending a Position update is `codec = registry.Resolve(Position,
session.AgreedAppVersion[Position]); codec.Encode(...)` — one array read, one dictionary lookup keyed
by a value tuple, zero per-packet negotiation logic.

### 5.4 Capabilities

A separate 64-bit flag set for optional behaviors that aren't tied to any one message class's schema:

| Bit | Capability | Meaning |
|---|---|---|
| 0 | Coalescing | Peer understands `Flags.Coalesced` payloads (§4.5). |
| 1 | QuantizedPosition | Peer can decode `PositionV2Codec`'s fixed-point encoding (informational; the actual selection is still driven by the Position schema-version negotiation in §5.3 — this bit exists for capabilities that aren't naturally expressed as a schema version, such as whether a peer supports coalescing at all). |
| 2 | Ipv6Peers | Peer can parse `Family == 6` `PeerKey` entries. |
| 3 | SelectiveAck | Reserved for a future selective-acknowledgement scheme for guaranteed delivery. |
| 4-63 | Reserved | Available for future capabilities without any wire format change. |

Agreed capabilities are simply the bitwise AND of both peers' declared sets — a capability is usable
with a given peer only once both sides have it.

### 5.5 Extension area (TLV)

Anything not anticipated by the fixed Hello layout — a future auth token, a build/version string for
diagnostics, a vendor-specific extension — appends as `(Tag: u16, Length: u16, Value: Length bytes)`
records after the offer list. An unrecognized tag is skipped by its declared length rather than
desyncing the rest of the message. This generalizes the one place the legacy protocol already does
this (the `Notes` message's length-prefixed inner records, docs/network-protocol.md §8.9) to the
handshake itself, so the *negotiation protocol* can evolve the same way application messages already
can, without ever needing a new envelope version.

### 5.6 Legacy peers and the handshake

A peer that never answers a `Hello` (after a short retry window matched to the legacy `Pulse` retry
cadence) is marked `AssumedLegacy` for the rest of the session and addressed exclusively through the
untouched legacy `Node.cs`/`Network.cs` path. No JFP2 capability is ever assumed for such a peer
beyond what the legacy protocol already provides — this is the mechanism, not just a hope, behind
"coexistence": the two stacks share a socket and a port, but per-remote-peer state is what decides
which one talks to that peer, and that decision is made once, cheaply, and cached.

## 6. Message catalog and performance rationale

### 6.1 Position — the hot path

The highest-frequency message in the system gets two codecs, both sharing one version-agnostic
in-memory `PositionUpdate` struct (full double precision, every field always present — precision
concerns are purely a codec-level encoding choice, never an in-memory or call-site concern):

- **PositionV1** (schema version 1): a straight repacking of the legacy fixed Position fields
  (docs/network-protocol.md §5.2) with no precision loss. 79 bytes.
- **PositionV2** (schema version 2): quantized encoding — latitude/longitude as int32 fixed-point at
  1e-7 degree scale, altitude as float32, and the three orientation angles (pitch/bank/heading) as
  int16 at a `32767/π` scale. 61 bytes.

Across the coordinate edge cases (±90° latitude, ±180° longitude), the 1e-7 degree quantization step is
approximately 1.1 cm at the equator and never worse than that anywhere on Earth, comfortably inside
the precision that matters for multiplayer position rendering, and every quantized value fits well
within `int32`'s range with no overflow risk.

**Size comparison** (header + fixed Position fields):

| | Header | Payload | Total | vs. legacy |
|---|---|---|---|---|
| Legacy (fixed fields only) | 21B header + 4B DataVersion/MessageId | ~91B | 116B | — |
| JFP2 PositionV1 | 8B | 79B | 87B | 25.0% smaller |
| JFP2 PositionV2 (quantized) | 8B | 61B | 69B | 40.5% smaller |

Of that reduction, 17 of the 25 header-size bytes (21→8, plus the guaranteed-delivery fields moving
out of the fixed cost entirely, §4.4) come purely from envelope design and apply to *every* message
class, not just Position; the rest comes from PositionV2's quantization, which is opt-in per peer pair
via §5.3 and costs nothing for a peer that never negotiates it.

A peer that only ever offers PositionV1 is completely unaffected by PositionV2's existence — it never
appears in that peer's negotiated table (§5.3), and `CodecRegistry.Resolve` simply never selects it.
This is the concrete demonstration that extensibility (§2 goal 2) and zero-cost-when-unused (§2 goal
1) are simultaneously achievable with this design, not in tension.

### 6.2 Identity — moved off the hot path

Livery, ICAO type/airline, registration, and class-code fields — exactly the fields at the center of
the v26.4/v26.5 livery bug (docs/protocol-changes-v26.4-v26.5.md §1.2) and the recording-format bug
that mirrors it (docs/recording-protocol.md §7.1) — become their own message class, rather than
conditionally appended to a high-frequency message. A JoinFS session can outlive a single flight — a
user can end one flight and start another under an unchanged connection, with a different aircraft,
livery, or callsign — so Identity is not a pure one-shot join-time send: it goes out immediately on
any such change, and is additionally repeated at a low, fixed cadence (on the order of every 3-5
seconds) for as long as the object exists, cached per object by every receiver between updates. That
periodic resend rate is deliberately orders of magnitude below Position's per-tick rate, so it costs
nothing worth measuring against the hot-path goal in §2, while giving every receiver — including a
peer that joins the mesh mid-session, or one that simply missed a single unreliable UDP datagram — a
bounded few-second window in which its cached Identity for an object is guaranteed to converge on the
current value, rather than depending on one send arriving. This is a structural fix, not a version
bump: because Identity is its own message class with its own schema version (§5.1), there is no longer
a shared runtime/compile-time version-gating boundary for these fields to fall on either side of.
`IdentityV1Codec` uses simple length-prefixed UTF8 strings; the extra byte or two per string is
irrelevant at Identity's much lower send frequency, so no quantization or interning is applied here —
the "shave the last millisecond" goal applies specifically to the per-tick hot path, not to every
message class uniformly.

### 6.3 VariableSync — unifying three legacy messages into one

The legacy protocol has three separate wire messages for integer, float, and string8 simulator
variables (docs/network-protocol.md §8.6–8.8), mirrored by three separate frame types in the recording
format (docs/recording-protocol.md §5.4–5.6) — and it was exactly this three-way split, combined with
a recorder frame-type switch that only handled `ObjectPosition` frames, that let non-Aircraft `Obj`
instances silently corrupt a recording stream (docs/recording-protocol.md §7.2, confirmed reachable
via `Sim.cs`'s variable broadcast loop). `VariableSync` replaces all three with a single self-
describing message: each entry carries its own `VariableKind` tag (Int32/Float32/String8) rather than
the frame type implying the value type. Because there is exactly one code path that produces this
message regardless of what kind of `Obj` is being recorded or broadcast, there is no separate branch
to accidentally omit — the class of bug in §7.2 has no analogue here by construction, not by
discipline.

### 6.4 Remaining message classes

`Event`, `FlightPlan`, `Notes`, `Weather`, and `Status` map one-to-one onto their legacy counterparts
(docs/network-protocol.md §8) and are lower-frequency by nature; this document does not propose new
wire shapes for them beyond moving them onto the JFP2 envelope and giving each an explicit schema
version. Their `SchemaOffer` entries and codecs (version 1 each) are a mechanical port of the existing
field lists and are not detailed further here since they introduce no new design questions beyond
what §4–§5 already establish; each should get its own `v1` codec at implementation time using the same
pattern as `IdentityV1Codec`.

### 6.5 Variable identification: reusing `vuid` unchanged

Repeating a simulator variable's name (a string, sometimes long) on every `VariableSync` send would
be wasteful, but this needs no new mechanism: the current JoinFS implementation already identifies
every variable by a 32-bit `vuid` rather than by name, and already sends `(vuid, value)` pairs over
the wire today (`JoinFS/Sim.cs`'s integer/float/string8 message readers each do a bare
`reader.ReadUInt32()` for the id before the value). `vuid` is computed by `VariableMgr.CreateVuid`
(`JoinFS/Variables.cs`) as `LocalNode.HashString(name)` — a stable, order-independent string hash
(`JoinFS/Node.cs`) — with the zero result remapped to `1`, plus an existing alias table
(`aliasVuids`) that lets two names resolve to one canonical `vuid` when needed (e.g. a hash collision
or a renamed SimConnect variable). Because every peer computes the same `vuid` from the same name
independently, purely as a pre-existing local computation, **no negotiation of any kind is required**:
`VariableEntry` simply carries this existing `vuid` (`uint`, 4 bytes) unchanged, rather than the
`ushort`/interned-table scheme an earlier draft of this design proposed. This also means `vuid`
identity is entirely orthogonal to the schema-version negotiation in §5 — it is a payload-level detail
of the `VariableSync` codec, not something Hello/HelloAck needs to know about.

## 7. Backward compatibility and coexistence strategy

**7.1 Socket-level coexistence.** One magic byte (§3, §4.1) discriminates every incoming datagram
before anything else is parsed. Legacy and JFP2 stacks run side by side on the same UDP socket and
port indefinitely — there is no proposed decommissioning date for the legacy stack in this document.

**7.2 Per-peer, not per-mesh, protocol selection.** Whether a given remote peer is spoken to via JFP2
or legacy is a property of that one peer relationship (`PeerSession.AssumedLegacy`, §5.6), decided
once at handshake time and cached. A mesh of mixed old and new builds works correctly: each node talks
JFP2 to the peers that answer a `Hello` and legacy to the ones that don't, simultaneously.

**7.3 Per-message-class independent versioning.** As established in §5.1, no message class's version
is coupled to any other's. This directly targets the failure mode in
docs/protocol-changes-v26.4-v26.5.md §1.1, where an unrelated pair of messages needed an intermediate
global version purely to sequence their independent changes.

**7.4 Reserved bits are ignored, not rejected.** Per §4.2, undefined flag bits are ignored on receive.
Combined with the `Extended` message-class escape hatch (§4.6) and the TLV extension area in the
handshake (§5.5), every extension point in this design degrades gracefully rather than requiring every
peer to reject or choke on a value it doesn't recognize — this is the structural alternative to
EOF-sensing (§1.3): every variable-length thing (a TLV entry, a coalesced sub-message, an `Extended`
class id, a `PeerKey` entry) declares its own length up front, so a reader that doesn't recognize it
can skip it correctly instead of needing to know its meaning to know its size.

**7.5 No compile-time/runtime gating split.** Every JFP2 codec is selected purely by the schema
version negotiated at runtime (§5.3); there is no compiler-directive-gated field anywhere in a codec.
The simulator-variant compiler directives (`FS2020`, `FS2024`, `FSX`, `P3D`, `XPLANE`, `CONSOLE`,
per the project's build configuration) continue to gate which *simulator driver* code is compiled into
a given build, exactly as today — but they must never gate the shape of a wire message itself. This is
the direct, permanent fix for §1.2: the bug was possible only because a wire-format decision was made
twice, once at compile time and once at runtime, and the two were allowed to disagree. JFP2 makes that
disagreement structurally impossible by having exactly one source of truth (the negotiated schema
version) for what a codec does.

**7.6 IPv6 is additive.** As detailed in §4.7, introducing `Family == 6` `PeerKey` entries requires no
flag day: existing readers that only handle `Family == 4` skip entries they don't understand by
computing their length from the family byte, the same pattern recommended for the recording format's
own extensibility in docs/recording-protocol.md's suggestions section.

**7.7 Hub-mediated translation between protocol generations.**

> **Implementation note (added when `docs/protocol-v2-implementation-plan.md` Phase 6 landed):** this
> section as originally written describes what turned out to be only part of the picture — the
> "translate" case (Tier 2/3 below), for when the hub's two legs genuinely disagree on wire format.
> It doesn't cover the common case: two JFP2 peers, both agreeing on the same schema version for a
> class, that can't reach each other directly. For that case, the hub needs no per-object cache and
> no decode/re-encode at all — `LocalNode.RelayForwardedJfp2Datagram` forwards the datagram byte-for-
> byte, the same way the legacy `FLAG_FORWARD` relay already does, using a wire-level addressing
> extension (`EnvelopeFlags.Forwarded` + an `OriginNuid`/`TargetNuid` pair, §4.1) rather than anything
> described in this section. That mechanism, and the sender-side changes needed to actually route
> traffic through it (`LocalNode.TryGetJfp2RelayPeer`, wired into every per-peer send site in
> `Network.cs`), is fully implemented — see Phase 6 for the full writeup including a design
> correction made during implementation (the addressing extension needs both fields always present,
> not one field whose meaning flips by direction, to let a receiving node unambiguously tell "relay
> this further" from "consume this"). What follows below (Tiers 2/3 — differing JFP2 schema
> versions, or one leg genuinely legacy-only) is still just the original design, not yet built;
> `Jfp2Bridge.cs` remains an empty reserved class for it.

Everything in §7.1–§7.6 covers what an
*ordinary* peer does: pick, once per neighbor, whether to speak JFP2 or legacy to that specific
neighbor (§5.6), and never translate anything, because a peer that speaks both simply encodes its own
outgoing messages twice — once per neighbor, in whatever that neighbor negotiated — rather than
converting one neighbor's bytes into another's. A **hub sitting between a JFP2 peer and a legacy-only
peer is a different case**: when it relays one peer's update onward to the other, there is no longer a
single byte sequence both sides can parse, so the raw relay/broadcast machinery the mesh already uses
today (`Node.cs`'s `Broadcast`/`FLAG_FORWARD` path) cannot cross that boundary unmodified. For any pair
of peers on either side of a protocol or schema-version difference, the hub must fully decode the
sender's message and re-encode it separately for each differently-negotiated recipient.

This is not new machinery bolted on for hubs specifically — it is the exact mechanism §6 already
builds, used from a second call site. Every `ICodec<T>` already decodes into a version-agnostic
in-memory value (`PositionUpdate`, `IdentityUpdate`, `VariableSyncUpdate`) and encodes it back out;
a translating hub just calls decode using whatever the *sender* negotiated and encode using whatever
each *recipient* negotiated, with the in-memory struct as the pivot in between. On the legacy side of
that pivot, the "codec" is simply the existing, completely unmodified `Node.cs`/`Network.cs`
`Read`/`Write` methods (§7.5) — nothing about them changes; JFP2 support only adds a translation step
that has to live somewhere, and the hub is the natural place, since it is the only participant already
talking to both peers.

*Position, legacy → JFP2.* The hub decodes an inbound `ObjectPosition`/`AircraftPosition` datagram
exactly as it does today, via the untouched legacy path. As of the v26.5 unconditional-livery fix
(docs/protocol-changes-v26.4-v26.5.md §1.3), that decode already yields the full identity tail —
`Callsign`, `Model`, `Livery`, `IcaoType`, `IcaoAirline`, `Registration`/`FlightNumber`, `ClassCode`,
`Wtc`, `ClassCodeConfirmed`, `TypeRole` — on *every* tick, not just on change. The hub copies the
position/orientation/velocity fields straight into a `PositionUpdate` and sends it using whichever
`PositionVx` codec the JFP2-side peer negotiated (§5.3). Separately, it diffs the identity tail against
a per-object cache (see below) and emits a fresh `Identity` message to that peer only when something
changed, plus the unconditional 3–5 second heartbeat from §6.2 — both driven entirely off values that
already arrive on every legacy tick, so the hub never has to ask the legacy peer for anything it
doesn't already send.

*Position, JFP2 → legacy.* The hub decodes an inbound `Position` message using whichever `PositionVx`
codec that peer negotiated, into the same `PositionUpdate`. To build a legacy
`ObjectPosition`/`AircraftPosition` packet — which expects identity fields inline on every tick — it
merges in that object's current identity from the same per-object cache, populated by the JFP2 peer's
own (infrequent) `Identity` messages, then encodes with the unmodified legacy `Write*` methods using
whatever `dataVersion` the legacy peer itself negotiated at Join. The legacy peer sees a wire-identical
packet to what a same-version peer would have sent it directly. One edge case worth flagging for
implementation: a brand-new JFP2-side object has no cached identity until its first `Identity` message
is processed, so a sender should be required to emit `Identity` before, or atomically with, the first
`Position` for a new object; until the hub has seen it, it fills the legacy identity fields with
whatever placeholder/empty values the current implementation already uses for "identity not yet known."

*Variables, both directions.* Legacy → JFP2: the hub demultiplexes the three legacy
Integer/Float/String8 messages by `vuid`, keeps a per-object latest-value table, and batches changed
entries into a `VariableSync` message tagged with the appropriate `VariableKind` per entry. JFP2 →
legacy: the hub splits one `VariableSync`'s entries back into up to three legacy messages, grouped by
`Kind`, and sends them with the unmodified `SendIntegerVariablesMessage`/`SendFloatVariablesMessage`/
`SendString8VariablesMessage`. Neither direction needs to translate the variable's identifier at all —
`vuid` is carried unchanged on both sides of the bridge (§6.5), so the hub's per-object variable cache
is keyed by literally the same 32-bit value the legacy protocol already uses today.

*What state this adds to the hub.* Per connected peer, the `PeerSession` already specified in §5.3/§5.6
tells the hub which protocol and schema version that neighbor uses — already sufficient to determine,
per inbound message, how many distinct outbound encodings are needed (one per distinct negotiated
protocol/version actually present among the *other* connected peers, not hard-coded to "legacy or
JFP2"). Per tracked object, the hub needs an identity cache and a latest-value-per-`vuid` table. This
is not a new category of hub responsibility: it is structurally the same kind of per-object state the
hub already keeps today so a sim-less `CONSOLE` build can participate in variable relay at all — the
`VariableMgr.Set` instances that exist with `main.sim == null` per the hub relay fix
(docs/protocol-changes-v26.4-v26.5.md §1.6). JFP2 support adds one more cache of the same shape
(identity) rather than introducing something architecturally new. And critically, none of this runs
when every connected peer has negotiated the same protocol and schema version — in that all-legacy or
all-same-version-JFP2 case the hub still takes the cheap raw-relay path unchanged, and only pays the
decode/re-encode cost on the specific hop where a protocol boundary actually exists.

This also makes the hub the natural first place to land JFP2 support in the rollout plan (§9): it
already terminates every peer's connection individually, so it can offer Hello/HelloAck and translate
for whichever peers answer, while any two legacy-only peers behind it continue exactly as they do
today, unaware anything changed.

## 8. Recording format synergy

This document is scoped to the network protocol, but the codec design in §6 has a direct, low-effort
application to the recording format audited in docs/recording-protocol.md: the same
per-(record type, schema version) codec pattern described in §6 could back a recording file's
per-record serialization, replacing the current compile-directive-gated `Obj.Write`/`Read1` methods
with an explicit, versioned codec selected by a value stored once in the file header rather than
inferred from `Sim.VERSION` at read time (this generalizes the length-prefix-framing and magic-number
recommendations already made in docs/recording-protocol.md's extension-suggestions section). Recording
currently has no protocol negotiation problem (a file has exactly one writer and no live peer to
negotiate with), so §5 does not apply to it — but adopting the *codec* half of this design (§6) for
recording would close §7.1 and give recordings the same "old reader skips fields it doesn't recognize"
property that JFP2 gives live traffic. This is called out as a natural follow-on, not proposed as part
of this document's scope.

## 9. Migration and rollout plan

1. **Land JFP2 as dead code.** Ship builds that can send/receive JFP2 datagrams and complete the
   handshake, but do not yet use it for any live traffic — verify coexistence (§7.1) and negotiation
   (§5.3) in the field with zero behavioral change.
2. **Migrate one low-risk message class first.** `Status` or `Pulse`-equivalent traffic is a
   reasonable first candidate — low frequency, easy to compare against the legacy equivalent
   side-by-side.
3. **Migrate Identity and VariableSync**, closing the §1.2/§7.2 bug classes for any pair of peers that
   has completed the JFP2 handshake, while peers still on legacy-only builds continue exactly as they
   do today.
4. **Migrate Position last**, once the above have proven stable, since it is the highest-frequency and
   highest-risk-of-subtle-bugs message class. Roll out `PositionV1` (no precision change) before ever
   introducing `PositionV2` (quantized) to the mesh.
5. **`PositionV2` and any future schema version are opt-in per peer pair by construction** (§5.3, §6.1)
   — no explicit "flip a switch" step is needed once the negotiation and codec infrastructure exists;
   a build simply starts offering the new version once it ships, and only peers that also offer it
   ever use it.
6. Legacy support has no proposed sunset date in this document; that is a product decision outside
   this design's scope.

## 10. Open questions and future work

- **Selective acknowledgement** (`Capability.SelectiveAck`, §5.4): reserved but unspecified; would let
  a guaranteed multi-part message retransmit only the missing pieces instead of the whole set, further
  reducing worst-case latency for guaranteed traffic. Out of scope here since guaranteed delivery is
  not the hot path this document optimizes for.
- **Coalescing policy** (§4.5): the wire format for a coalesced payload is specified, but the policy
  for *when* to coalesce (batch window size, which message classes are eligible) is an implementation
  tuning question, not a wire-format one, and is left open.
- **Recording-format adoption of the codec pattern** (§8): flagged as a natural follow-on with a clear
  mechanism, but not designed in detail here.
- **`ProtoMajor` 3+ governance**: this document specifies only `ProtoMajor == 2`'s envelope layout.
  Any future breaking redesign should follow the same pattern (a magic-byte or version-byte dispatch
  before anything else is parsed) but is otherwise unconstrained by this document.

