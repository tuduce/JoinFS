# JFP2 — the JoinFS Protocol, version 2

**Reference document.** It describes the JFP2 wire protocol — why it exists, its envelope,
negotiation, message catalog and coexistence with the legacy protocol — as implemented in
`JoinFS/Net/Protocols/Jfp2/`. Where the design specifies something the code does not use yet, the
section says so explicitly ("*specified, not implemented*").

Related reading:
- `docs/reference/joinfs-architecture.md` — how JFP2 fits into the application as one protocol
  plugin next to the legacy one.
- `docs/network-protocol.md` — the legacy wire protocol that JFP2 coexists with.
- `docs/recording-protocol.md` and `docs/protocol-changes-v26.4-v26.5.md` — the audits that
  motivated JFP2 (cited in §1).
- `docs/protocol-v2-implementation-plan.md` and `docs/protocol-v2-implementation-review.md` —
  historical record of how JFP2 was first built and field-tested, including Findings 1–9.

## 1. Motivation

The legacy protocol works, but six concrete, previously-documented problems motivate a new protocol
rather than another incremental patch:

**1.1 One global version number gates everything.** `DataVersion` (docs/network-protocol.md §5) is a
single number every application message is implicitly validated against. When any one message's shape
changes, the version moves for the whole protocol, and every receiver has to reason about "what does
dataVersion N mean for message X" for every X. The v26.4→v26.5 change
(docs/protocol-changes-v26.4-v26.5.md §1.1) needed an intermediate version (21006) purely as a
migration step for fields on two unrelated messages, and the version number carries no structured
information about which fields are actually present.

**1.2 Version gates that don't match compile-time gates.** The recorder gated the
Livery/IcaoType/IcaoAirline tail with `#if FS2024` (compile time) while the network gated the same
fields with a runtime `dataVersion` check, so an FS2020 build and an FS2024 build produced different
shapes for "the same version" (docs/protocol-changes-v26.4-v26.5.md §2). Any design that mixes
compile-time and runtime gating of wire fields reproduces this class of bug.

**1.3 EOF-sensing as the extension mechanism.** Messages are extended by appending fields and having
the reader keep reading until the datagram runs out (docs/network-protocol.md §9.2,
docs/recording-protocol.md §6). A receiver can't skip a field type it doesn't recognize without
knowing its exact length, which rules out inserting an independent new field anywhere but the tail.

**1.4 No peer capability exchange.** Two peers cannot tell each other "here is the schema range I can
speak" — there is no per-message-class negotiation, no capability bits, and no extension area in the
handshake.

**1.5 Header overhead on every packet.** The legacy 21-byte header is paid on every datagram,
including 4 bytes of guaranteed-delivery bookkeeping on datagrams that are never guaranteed — Position,
the highest-frequency message, is never guaranteed.

**1.6 IPv4-only peer addressing.** The legacy `Nuid` hard-codes a 4-byte IPv4 address into every
peer identifier (docs/network-protocol.md §9.5).

## 2. Design goals and non-goals

**Goals, in priority order:**

1. **Speed.** The hot path (Position) must be smaller on the wire and cheaper to encode/decode than
   legacy, with no heap allocation per message and no per-packet branching on version once a peer's
   capabilities are known.
2. **Extensibility without a flag day.** New fields, message classes and schema versions can be added
   without breaking peers that haven't been updated, and without every class bumping in lockstep.
3. **Explicit backward compatibility.** Every version of every message class is handled by an explicit
   codec (§6), never by a reader that consumes bytes until it runs out. Old and new builds coexist on
   the same mesh and the same UDP port.
4. **Peer capability/version exchange**, once per connection (§5).

**Non-goals:**

- **Security/authentication.** JFP2 carries no encryption, signing or replay protection beyond what the
  legacy protocol has (effectively none). This is deliberate and should be revisited separately.
- **New reliability semantics.** JFP2 keeps the legacy two-tier model: unreliable, or guaranteed
  (acknowledged and retransmitted). No ordered streams or congestion control.
- **Changing anything above the wire format** (simulator abstraction, model matching).
- **A flag-day cutover.** JFP2 runs alongside the legacy protocol indefinitely (§7).

## 3. Overview

Three pieces:

- **Envelope** (§4): a minimal header on every datagram — 8 bytes for the common unreliable case,
  12 for guaranteed delivery, plus 14 when relayed.
- **Negotiation** (§5): a Hello/HelloAck handshake per peer, producing a flat per-message-class table
  of agreed schema versions and a set of agreed capabilities. Computed once, then only indexed.
- **Codecs** (§6): one encoder/decoder per (message class, schema version). A new version of a class is
  a new codec; nothing else changes.

JFP2 and the legacy protocol share one UDP socket and port: every JFP2 datagram starts with the magic
byte `0xFA`, which can never collide with the legacy protocol's first byte (`0x0B`, the low byte of
the little-endian `0x520B` constant). A receiver looks at byte 0 before parsing anything else.

In the application, JFP2 is a *link upgrader*: every node also speaks legacy, the legacy mesh
(Join/Pulse/Pathfinder) discovers peers, and JFP2 negotiates per directly reachable peer and carries
the message kinds both sides agreed on (§7.2).

## 4. Wire format

All multi-byte integers are little-endian. Strings are UTF-8 with a **u16 length prefix**
(`WireText`) — not the legacy 7-bit-encoded .NET `BinaryWriter` prefix.

### 4.1 Fixed envelope (8 bytes)

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | Magic | Always `0xFA`. |
| 1 | 1 | ProtoMajor | `2`. A future breaking redesign would branch on this byte. |
| 2 | 1 | Flags | §4.2. |
| 3 | 2 | SenderPeerId | u16, assigned at handshake time (§5.2): the sender's own id for the session between the two nodes exchanging *this datagram*. |
| 5 | 2 | RecipientPeerId | u16: the receiver's id for that session. A receiver finds the session by this id alone, never by source endpoint (§5.7). |
| 7 | 1 | RawMessageClass | §4.3. |

The optional extensions follow in this order: guaranteed (§4.4), then relay (§4.5), then the
payload.

### 4.2 Flags

| Bit | Name | Meaning |
|---|---|---|
| 0 | Guaranteed | Wants acknowledgement/retransmission; the 4-byte guaranteed extension follows (§4.4). |
| 1 | Forwarded | Addressed by origin/target node ids instead of PeerIds; the 14-byte relay extension follows (§4.5). |
| 2 | Coalesced | Payload is a sequence of sub-messages (§4.7). *Specified, not implemented.* |
| 3 | Internal | `RawMessageClass` indexes the internal partition (§4.3). |
| 4–7 | Reserved | Zero on send, ignored on receive. |

### 4.3 Message classes

Every value is an explicit, permanently reserved constant (`MessageClasses` in `Envelope.cs`) —
unlike the legacy enum, whose values come from declaration order. The same byte range is used by two
partitions, selected by the `Internal` flag. **Always append at the next unused number; never
renumber or reuse a shipped value.**

**Internal partition:**

| Value | Class | Status |
|---|---|---|
| 0 | Hello | Implemented (§5.2) |
| 1 | HelloAck | Implemented (§5.2) |
| 2 | Join | Reserved (mesh messages go over legacy today) |
| 3 | JoinReply | Reserved |
| 4 | Leave | Reserved |
| 5 | Pulse | Reserved |
| 6 | PulseResponse | Reserved |
| 7 | Pathfinder | Reserved |
| 8 | PathfinderResponse | Reserved |
| 9 | GuaranteedDone | Implemented (§4.4) |

**Application partition:**

| Value | Class | Codec | Guaranteed? |
|---|---|---|---|
| 0 | Position | `PositionV1Codec` | no |
| 1 | Identity | `IdentityV1Codec` | no |
| 2 | VariableSync | `VariableSyncV1Codec` | no |
| 3 | Event | `EventV1Codec` | yes |
| 4 | FlightPlan | `FlightPlanV1Codec` | no |
| 5 | Notes | `NotesV1Codec` | yes |
| 6 | Weather (weather update) | `WeatherUpdateV1Codec` | no |
| 7 | Status | `StatusV1Codec` | no |
| 8 | StatusRequest | `StatusRequestV1Codec` | no |
| 9 | WeatherReply | `WeatherReplyV1Codec` | yes |
| 255 | Extended | §4.6 — *specified, not implemented* | — |

### 4.4 Guaranteed delivery

When `Guaranteed` is set, 4 bytes follow the fixed header:

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | GuaranteedId |
| 2 | 1 | GuaranteedIndex |
| 3 | 1 | GuaranteedCount |

Behaviour, as implemented:
- JFP2 guaranteed messages are single-datagram: index/count are always 0/1. Multi-segment delivery is
  reserved by the field layout but not used.
- The sender retransmits every **2 s** until acknowledged, giving up after **5** attempts.
- The receiver answers every guaranteed datagram, including duplicates, with an internal
  `GuaranteedDone` whose payload is the u16 `GuaranteedId`. It delivers the message only the first
  time; ids are remembered for **30 s** for duplicate suppression, keyed by the true origin.
- When the acknowledged datagram arrived relayed (§4.5), the `GuaranteedDone` is itself sent
  `Forwarded`, with origin = the acknowledging node and target = the true sender, so it travels back
  through the relay end to end. A relay that re-sent the message on the origin's behalf (translation
  or a different schema version) acknowledges upstream itself, and consumes the downstream ack.

### 4.5 Relay extension (Forwarded)

When `Forwarded` is set, 14 bytes follow the (optional) guaranteed extension:

| Offset | Size | Field |
|---|---|---|
| 0 | 7 | OriginNode — who the message really comes from |
| 7 | 7 | TargetNode — who it is ultimately for |

Each node id has the legacy `Nuid` layout: `ip` (u32), `port` (u16), `local` (u8). Every mesh member
already knows every other member's node id through the legacy mesh, so no new synchronization is
needed.

**Two levels of addressing.** Origin and Target are end to end. SenderPeerId and RecipientPeerId in
the fixed header stay *hop-scoped*, exactly as on a direct datagram: they name the session between
the two nodes that exchange this datagram (sender to relay, then relay to target). The receiver
therefore finds the neighbour session by id and never guesses it from the source endpoint. A relay
rewrites the two ids, which sit at fixed offsets, when it passes a datagram on.

Receiving rule, identical on every node:
- **TargetNode is me:** consume it, attributing it to OriginNode and decoding it with the schema
  agreed with the neighbour it came from.
- **Otherwise:** relay it, but only if TargetNode is a *direct* neighbour (no relay involved in
  reaching it) and the node's relay budget (10 concurrent senders, shared with legacy relaying)
  allows it. That caps relaying at one hop.
  - If the target has a verified session with the relay and agreed the **same schema version** for
    the class as the sender's hop used (or the datagram is internal), forward the bytes with the hop
    ids rewritten.
  - Otherwise — a legacy-only target, or a different version — decode the message and hand it to the
    translation path (§7.7), which re-sends it in the target's own terms. The translation path is
    thereby also a per-hop version adapter.

Both fields are always present. A single field whose meaning flips by direction was rejected: in
neither role would that field equal the receiver's own id, so a node could not tell "relay further"
from "consume".

**Origination.** A node sends to a peer it does not reach directly through the neighbour that carries
that peer's traffic (§5.7), in the schema agreed with that neighbour, as a Forwarded datagram. It does
not need to know whether the final target speaks JFP2: the relay either forwards or translates.

### 4.6 `Extended` escape hatch — *specified, not implemented*

`RawMessageClass == 255` would mean the real class id is a u16 immediately after the fixed header,
giving headroom past 255 classes per partition without widening the envelope.

### 4.7 Coalesced payload — *specified, not implemented*

A sequence of `(MessageClass: u8, Length: u16, Body)` triples, letting small updates that become ready
in the same tick share one datagram. The wire shape is reserved; no build sends it.

### 4.8 `PeerKey` — *specified, not implemented*

A self-describing peer address for payloads that must name other peers (membership lists,
pathfinder targets) once the mesh itself runs over JFP2: `Family` (4 or 6), `Address` (4 or 16
bytes), `Port` (u16), `Local` (u8). Because each entry declares its own size, readers that only
understand IPv4 can skip IPv6 entries. Defined in `Envelope.cs`; unused until JFP2 carries mesh
messages.

## 5. Capability and schema negotiation

### 5.1 Why per message class

Each message class has its own schema version, and each peer declares the inclusive range it can
encode and decode per class. Two peers can run Position at v2 while running VariableSync at v1, with
no coupling between the decisions.

### 5.2 Hello / HelloAck

Internal-partition messages. A node sends `Hello` to the route endpoint of each peer the mesh knows
and does not reach through a relay. The reply goes back to the UDP source. Both messages say who is
speaking (the `Node` extension, §5.5): a Hello names its sender, and a HelloAck names the node that
actually answered, which is what tells a node whether the peer it asked for, or another node sharing
that endpoint, replied.

Payload (same shape for both; `Result` is meaningful only in HelloAck):

| Field | Size | Notes |
|---|---|---|
| ProtoMajorMin | 1 | Lowest envelope version this build speaks (2). |
| ProtoMajorMax | 1 | Highest (2). |
| Capabilities | 8 | u64 bitset, §5.4. |
| SelfAssignedId | 2 | The PeerId this node wants to be addressed by. |
| Result | 1 | HelloAck: `0` accepted, `1` no compatible ProtoMajor. |
| OfferCount | 2 | Number of offers. |
| Offers | 4 × OfferCount | `Internal(1) MessageClass(1) MinVersion(1) MaxVersion(1)` |
| Extensions | rest | TLV records, §5.5. |

Behaviour, as implemented (`Jfp2Plugin`):
- A Hello without a `Node`, or naming a node that isn't a known mesh peer, is ignored. The legacy
  Join always happens first, and a build that doesn't say who it is stays on legacy.
- An unanswered Hello is retried every **2 s**. After **5** attempts the peer is marked
  `AssumedLegacy` (legacy only) and is tried again after **30 s**, or at once if it sends a Hello
  itself.
- Only one Hello is outstanding per endpoint. Each Hello tells the node that answers which id to use
  for us, so two in flight to one endpoint would leave that node holding the wrong one.
- A HelloAck is matched to the session by the id it is addressed to, and must name the node the Hello
  was for. If another node answers, that node owns the endpoint (see §5.7) and the peer we asked for
  is not there.
- A HelloAck with `Result != 0` marks the peer `AssumedLegacy`.
- Receiving a peer's Hello lets us *decode* what it sends. It does not make the session usable for
  *sending*: only the ack of our own Hello proves our datagrams reach that node.

Current offers: every application class 0–9 at version range [1, 1]; no internal classes.

### 5.3 Resolution

For each (Internal, MessageClass) key offered by either side:

```
lo = max(local.Min, remote.Min)
hi = min(local.Max, remote.Max)
agreed = hi >= lo ? hi : 0
```

`0` means "don't send this class to this peer". The result is stored once per peer session in two
flat 256-entry arrays, `AgreedAppVersion[]` and `AgreedInternalVersion[]`. Sending a message is then
one array read plus a codec lookup. The application-level router caches the choice per (peer, kind)
too (`docs/reference/joinfs-architecture.md` §5.3), so the hot path never negotiates.

### 5.4 Capabilities — *specified, none in use*

A u64 bitset for behaviours not tied to one class's schema. Agreed capabilities are the bitwise AND
of both sides'. Current builds advertise none.

| Bit | Capability |
|---|---|
| 0 | Coalescing (§4.7) |
| 1 | QuantizedPosition (informational; Position v2 selection itself is by schema version) |
| 2 | Ipv6Peers (§4.8) |
| 3 | SelectiveAck (reserved) |

### 5.5 Extension area (TLV)

`(Tag: u16, Length: u16, Value)` records after the offer list. Unknown tags are skipped by length,
so the handshake can grow without a new envelope version.

| Tag | Name | Value |
|---|---|---|
| 1 | Node | 7 bytes, the legacy `Nuid` layout: the speaking node's own id. In a HelloAck, the node that answered. Required: a handshake message without it is treated as coming from a legacy-only peer. |

### 5.6 Peers that don't speak JFP2

A peer that never answers Hello, or rejects it, is `AssumedLegacy`. Everything to it goes through
the legacy plugin, unless a neighbour that does speak JFP2 carries its traffic (§5.7), in which case
that neighbour translates. No JFP2 capability is ever assumed beyond what negotiation established.

### 5.7 Sessions, next hop and health

**A session is with a neighbour.** It is bound to the node id the peer stated in the handshake, and
never to the endpoint the datagram came from: two nodes can share one public endpoint (a hub and a
client behind one router that forwards a port to the hub, both known by the same public IP and port),
and a source address cannot tell them apart. Datagrams are matched to a session by the two ids in the
envelope. Ids are random, not sequential, so a datagram meant for another node cannot match a session
by coincidence.

**Next hop.** JFP2 datagrams for a peer go to the first of these that has a verified session:
1. the peer itself, if it answered our Hello at the endpoint we currently reach it on;
2. the relay the mesh routes it through (`Peer.RouteVia`, set by the pathfinder);
3. another node that answered at the peer's own endpoint: the peer shares the endpoint and is behind
   that node.

The datagram is unaddressed when the hop is the peer itself, and Forwarded (§4.5) otherwise. If none
applies, the legacy plugin carries everything for the peer. The protocol is therefore a property of
each hop, and a hub may speak JFP2 to one side of a relay and legacy to the other.

**Verified, and kept verified.** A session becomes usable for sending when our own Hello is answered
by the right node. Afterwards a keepalive Hello goes to the verified endpoint every **5 s**; the
answer must name the same node. A session that has not been answered for **15 s**, or whose
endpoint is now answered by another node (the network steers it elsewhere), or whose route moved,
stops being verified and the peer falls back to legacy until it is verified again. A peer that
restarts, or forgets its sessions, is recovered by the next keepalive: a Hello always refreshes the
receiver's view of the sender's id, and the ack refreshes ours.

## 6. Message catalog

Each codec maps one canonical in-memory struct (`JoinFS/Net/Messages`) to bytes. Field meanings are
the canonical ones; see `docs/reference/joinfs-architecture.md` §4 for the message model.

### 6.1 Position — the hot path

**PositionV1** (schema 1), fixed **103 bytes** (`PositionV1Codec.Size`):

| Field | Type | Bytes |
|---|---|---|
| ObjectId | u32 (`0xFFFFFFFF` = shared cockpit: the recipient's own aircraft) | 4 |
| NetTime | f64, sender's clock, seconds | 8 |
| Latitude, Longitude, Altitude | f64 × 3 | 24 |
| Pitch, Bank, Heading | f32 × 3 | 12 |
| Velocity XYZ, AngularVelocity XYZ, Acceleration XYZ | f32 × 9 | 36 |
| Rudder, Elevator, Aileron, BrakeLeft, BrakeRight | i16 × 5, fixed point `value × 16384` | 10 |
| Elevation | f32 | 4 |
| StaticCgToGround | f32, feet | 4 |
| StateFlags | u8: OnGround, ElevationCorrection, UserControlled, Paused | 1 |

With the 8-byte envelope that is **111 bytes** per datagram. The equivalent legacy AircraftPosition is
about **204 bytes**, because it repeats the full identity (strings) on every tick. That saving is §6.2.

**PositionV2** (quantized: lat/lon as i32 at 1e-7°, angles as i16) — *specified, not implemented*.
It would be opt-in per peer pair through negotiation (§5.3), invisible to peers that never offer it.

### 6.2 Identity — off the hot path

Model, livery, ICAO type/airline, registration, flight number and class code are their own class,
sent when they change and as a **4 s** heartbeat per object and peer. The heartbeat lets a peer that
joined late, or lost a datagram, converge within a bounded window. The sender always sends Identity
before an object's first Position to a peer, and withholds Position until it can.

**IdentityV1** layout: `ObjectId u32`, `Flags u8` (IsAircraft 1, IsPlane 2, ClassCodeConfirmed 4),
`TypeRole u8`, then strings: Callsign, Model, Livery, IcaoType, IcaoAirline, Registration,
FlightNumber, ClassCode, Wtc.

This is a structural fix for §1.2: identity is one message class with one schema version, so there is
no runtime/compile-time gating boundary for these fields to fall on either side of.

### 6.3 VariableSync — one message for three legacy ones

Replaces the legacy Integer/Float/String8 variable messages. Each entry carries its own kind tag, so
one code path handles every kind (closing docs/recording-protocol.md §7.2's bug class).

**VariableSyncV1** layout: `ObjectId u32`, `Count u8`, then Count × entries of `Vuid u32`,
`Kind u8` (0 Int32, 1 Float32, 2 String8), and a value (i32, f32, or u16-prefixed UTF-8 string with
no length cap). Senders chunk at **200** entries per message. The owner of the object is the sender
(or the relayed origin); there is no owner field.

### 6.4 Other v1 codecs

| Class | Layout |
|---|---|
| Event | `ObjectId u32, EventId u32, Data u32` — 12 bytes |
| FlightPlan | `ObjectId u32`, then strings: IcaoType, Departure, Destination, Rules, Route, Remarks, Alternate, Speed, Altitude, Callsign, Registration, IcaoAirline, FlightNumber |
| Notes | one comms note: `Guid 16`, strings Nickname, Callsign, `NoteId u32, Age f32, Channel u16`, string Text |
| Weather, WeatherReply | string Metar |
| StatusRequest | `Flags u8` (HubEnabled 1, HubListRequested 2), `Uuid u32` — 5 bytes |
| Status | `Guid 16`, string AppVersion, `Users u16, AtcCount u16`, string AtcAirport, `AtcLevel u8`, `Planes, Helicopters, Boats, Vehicles u16 × 4`, `HubFlags u8` (HubEnabled 1, GlobalSession 2, PasswordRequired 4), strings Address, Name, About, Voip, NextEvent, Airport, `ActivityCircle i32` |

Unlike legacy Status, JFP2 Status always carries every field; there are no conditional or
EOF-sensed parts.

Notes carries one note per message. A bulk "all notes" reply is sent as one Notes message per note.

**Not carried by JFP2** (always legacy): object positions of non-aircraft objects, RemoveObject,
ShowOnRadar, PeerInfo (legacy SharedData), WeatherRequest, hub directory messages, comms requests,
and all mesh messages.

### 6.5 Variable identification: `vuid`

Variables are identified by the same 32-bit `vuid` the legacy protocol uses: `VariableMgr.CreateVuid`
(`NetHash.HashString(name)`, with 0 remapped to 1 and an alias table). Every peer computes it
independently from the name, so no negotiation is needed.

## 7. Coexistence with the legacy protocol

**7.1 One socket.** The magic byte (§3) routes each incoming datagram to the right plugin before
anything else is parsed. Both protocols share the socket and port indefinitely.

**7.2 Per peer and per message kind.** The choice between JFP2 and legacy is made per peer and per
message kind, not per mesh:
- JFP2 is used for a kind when the peer's next hop (§5.7) negotiated it (§5.3).
- Everything else goes over legacy: kinds JFP2 doesn't carry (§6.4), `AssumedLegacy` peers, and
  sessions that are not (or no longer) verified.
- Because every node speaks legacy, falling back is always possible.
- The choice is re-evaluated when a peer's route or a session's state changes.

**7.3 Independent versions per class.** No class's version is coupled to another's (§5.1).

**7.4 Reserved values are ignored, not rejected.** Undefined flag bits, TLV tags and (once
implemented) coalesced sub-messages, extended classes and `PeerKey` families all declare their own
size, so a reader can skip what it doesn't understand. This is the structural alternative to
EOF-sensing (§1.3).

**7.5 No compile-time wire gating.** Codecs are selected only by the negotiated schema version.
Simulator build symbols (`FS2020`, `FS2024`, `XPLANE`, `CONSOLE`, ...) never change a wire shape.

**7.6 IPv6 is additive**, through `PeerKey` (§4.8), once the mesh runs over JFP2.

**7.7 Relaying and translation.**
- **Two JFP2 neighbours of a relaying node that agree on a class *and its schema version*:** the
  relay forwards the datagram (§4.5) with the hop ids rewritten, like legacy `FLAG_FORWARD`.
- **The target agreed a different version:** the relay decodes with the sender's version, and the
  core re-sends it encoded for the target's.
- **The target doesn't speak JFP2 for that class:** the relaying node decodes the message into its
  canonical form and hands it to the application's network core. The core sends it to the target
  with whichever protocol reaches it — legacy, in practice — keeping the true sender:
  - The legacy encoding writes the original sender into the header, with the Forward flag set, which
    is exactly what a legacy receiver expects from a relayed message.
  - Identity is not forwarded as its own message. It updates a per-object cache that the legacy
    encoder inlines into the next position.
  - Guaranteed messages are delivered hop by hop: the relaying node acknowledges upstream in JFP2,
    then sends a legacy guaranteed message downstream and consumes the legacy acknowledgement itself.
- **The reverse direction (legacy → JFP2)** never needs translation: every JFP2 node understands
  legacy, so the legacy relay carries it unchanged.

There is no protocol-pair-specific bridge code. See `docs/reference/joinfs-architecture.md` §6.

## 8. Recording format

The recording format (`docs/recording-protocol.md`) is independent of JFP2. Its version is
`Recorder.FileVersion`, no longer tied to any wire version. Adopting the codec pattern of §6 for
recordings (explicit per-record-type versions instead of EOF-sensing) remains a possible follow-on.

## 9. Status and future work

**Implemented:**
- the envelope with its guaranteed and relay extensions, hop-scoped ids on relayed datagrams;
- Hello/HelloAck with per-class negotiation, node identity, and verified sessions with a keepalive;
- next-hop origination of relayed traffic, and relay or translation at the hop;
- single-datagram guaranteed delivery;
- the v1 codecs for all ten application classes.

It was first field-tested against MSFS 2024 before the plugin architecture; the shared-endpoint
topology in `JoinFS.Tests/Net/Jfp2RelayTests.cs` reproduces the failure of the next field test.

**Specified, not implemented:**
- PositionV2
- Coalescing
- the `Extended` escape hatch
- `PeerKey`
- capability bits
- multi-segment guaranteed delivery
- JFP2 mesh messages

**Open questions:**
- **A JFP2-native mesh:** carrying Join/Pulse/Pathfinder over JFP2. It needs multi-segment
  guaranteed delivery and a pre-Join bootstrap. It is worth doing only for authentication, IPv6, NAT
  hole punching or retiring legacy; see `docs/network-plugin-architecture.md` §2.4.1.
- **Relay fan-out:** one datagram to a hub for "all your other neighbours" instead of one per peer,
  which would cut the uplink of a node behind a hub. It needs relay budgets and amplification limits.
- **Limits of verification behind a shared endpoint:** a node whose replies are steered to another
  node cannot verify the path back, so that direction stays on legacy (§5.7).
- **Selective acknowledgement** and **coalescing policy** (batch window, eligible classes).
- **Governance for ProtoMajor 3+:** a future breaking version should keep the magic/version-byte
  dispatch before anything else is parsed.
