# JFP2 — the JoinFS Protocol, version 2

**Reference document.** It describes the JFP2 wire protocol — why it exists, its envelope,
negotiation, message catalog and coexistence with the legacy protocol — as implemented in
`JoinFS/Net/Protocols/Jfp2/`. Where the design specifies something the code does not use yet, the
section says so explicitly ("*specified, not implemented*").

Related reading:
- `docs/reference/joinfs-architecture.md` — how JFP2 fits into the application as one protocol
  plugin next to the legacy one.
- `docs/network-protocol.md` — the legacy wire protocol that JFP2 coexists with.
- `docs/recording-protocol.md` and `73b203d^:docs/protocol-changes-v26.4-v26.5.md` (removed; read it with `git show`) — the audits that
  motivated JFP2 (cited in §1).
- `docs/protocol-v2-implementation-plan.md` and `docs/protocol-v2-implementation-review.md` —
  historical record of how JFP2 was first built and field-tested, including Findings 1–9.
- `docs/network-plugin-architecture.md` §2.13 — the decision that JFP2 succeeds legacy;
  `docs/jfp2-standalone-review.md` — the goals behind it and the work items (B1–B7) cited here as
  "review items"; `docs/jfp2-wire-design.md` — the approved design of JFP2's first released wire
  (26.6), built in stages. This document describes each part once it is built.

## 1. Motivation

The legacy protocol works, but six concrete, previously-documented problems motivate a new protocol
rather than another incremental patch:

**1.1 One global version number gates everything.** `DataVersion` (docs/network-protocol.md §5) is a
single number every application message is implicitly validated against. When any one message's shape
changes, the version moves for the whole protocol, and every receiver has to reason about "what does
dataVersion N mean for message X" for every X. The v26.4→v26.5 change
(the v26.4→v26.5 audit, §1.1) needed an intermediate version (21006) purely as a
migration step for fields on two unrelated messages, and the version number carries no structured
information about which fields are actually present.

**1.2 Version gates that don't match compile-time gates.** The recorder gated the
Livery/IcaoType/IcaoAirline tail with `#if FS2024` (compile time) while the network gated the same
fields with a runtime `dataVersion` check, so an FS2020 build and an FS2024 build produced different
shapes for "the same version" (the v26.4→v26.5 audit, §2). Any design that mixes
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

**Goals:**

1. **Speed.** The hot path (Position) must be smaller on the wire and cheaper to encode/decode than
   legacy, with no heap allocation per message and no per-packet branching on version once a peer's
   capabilities are known.
2. **Extensibility without a flag day.** New fields, message classes and schema versions can be added
   without breaking peers that haven't been updated, and without every class bumping in lockstep.
3. **Explicit backward compatibility.** Every version of every message class is handled by an explicit
   codec (§6), never by a reader that consumes bytes until it runs out. Old and new builds coexist on
   the same mesh and the same UDP port.
4. **Peer capability/version exchange**, once per connection (§5).
5. **Replacing legacy.** JFP2 is the successor protocol (`docs/network-plugin-architecture.md`
   §2.13): a node must become able to join, stay in and leave a session over JFP2 alone, after which
   legacy retires. Today JFP2 still relies on the legacy mesh for membership (§3, §7.2).
6. **Security.** Without legacy, JFP2's Join is the only access control, so authentication is a
   goal, designed with the mesh over JFP2 (review item B7: a challenge-response Join, node names
   bound to a key pair, per-hop protection of every datagram, `docs/jfp2-wire-design.md` §2.5).
   *Not implemented*: today JFP2 carries
   no encryption, signing or replay protection beyond what the legacy protocol has (effectively
   none).

**Non-goals:**

- **New reliability semantics.** JFP2 keeps the legacy two-tier model: unreliable, or guaranteed
  (acknowledged and retransmitted). No ordered streams or congestion control.
- **Changing anything above the wire format** (simulator abstraction, model matching).
- **A flag-day cutover.** JFP2 runs alongside the legacy protocol (§7) until legacy retires, which
  happens when the builds that hubs log from the `Build` extension (§5.5, review item A4) show that
  enough users run a build with the mesh over JFP2.

## 3. Overview

Three pieces:

- **Envelope** (§4): a minimal header on every datagram — 8 bytes for the common unreliable case,
  12 for guaranteed delivery, plus 16 when relayed.
- **Negotiation** (§5): a Hello/HelloAck handshake per peer, producing a flat per-message-class table
  of agreed schema versions and a set of agreed capabilities. Computed once, then only indexed.
- **Codecs** (§6): one encoder/decoder per (message class, schema version). A new version of a class is
  a new codec in the class's descriptor; nothing else changes (§6.6).

JFP2 and the legacy protocol share one UDP socket and port: every JFP2 datagram starts with the magic
byte `0xFA`, which can never collide with the legacy protocol's first byte (`0x0B`, the low byte of
the little-endian `0x520B` constant). A receiver looks at byte 0 before parsing anything else.

In the application, JFP2 today upgrades links: every node also speaks legacy, the legacy mesh
(Join/Pulse/Pathfinder) discovers peers and holds membership, and JFP2 negotiates per directly
reachable peer and carries the message kinds both sides agreed on (§7.2). That is a stage, not the
end: JFP2 is the successor protocol, it will take over the mesh (review item B3), and legacy then retires
(`docs/network-plugin-architecture.md` §2.13).

## 4. Wire format

All multi-byte integers are little-endian. Strings are UTF-8 with a **u16 length prefix**
(`WireText`) — not the legacy 7-bit-encoded .NET `BinaryWriter` prefix. Every string field has a
byte limit, and no datagram is larger than **1,200 bytes** (§6.7).

### 4.1 Fixed envelope (8 bytes)

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | Magic | Always `0xFA`. |
| 1 | 1 | ProtoMajor | `2`. A future breaking redesign would branch on this byte, so a receiver drops (logged at network level) a datagram with any other value: nothing after this byte need look the same. Hello and HelloAck always carry `2` (§5.2). |
| 2 | 1 | Flags | §4.2. |
| 3 | 2 | SenderPeerId | u16, assigned at handshake time (§5.2): the sender's own id for the session between the two nodes exchanging *this datagram*. |
| 5 | 2 | RecipientPeerId | u16: the receiver's id for that session. A receiver finds the session by this id alone, never by source endpoint (§5.7). `0` while the sender does not know it yet, which happens only on the first Hello to a peer; no session has id `0`. |
| 7 | 1 | RawMessageClass | §4.3. |

The optional extensions follow in this order: guaranteed (§4.4), then relay (§4.5), then the
payload.

### 4.2 Flags

| Bit | Name | Meaning |
|---|---|---|
| 0 | Guaranteed | Wants acknowledgement/retransmission; the 4-byte guaranteed extension follows (§4.4). |
| 1 | Forwarded | Addressed by origin/target node names instead of PeerIds; the 16-byte relay extension follows (§4.5). |
| 2 | Coalesced | Payload is a sequence of sub-messages (§4.7). *Specified, not implemented*: no build sends it, and this one drops it (below). |
| 3 | Internal | `RawMessageClass` indexes the internal partition (§4.3). |
| 4–7 | — | Unassigned; zero on send, and a datagram with one is dropped (below). |

**Rule for flags.** A new flag may add a header extension or change the payload's framing, so a
receiver that ignored it would misparse everything after it. Therefore:
- **Receiver:** a datagram with any bit set other than `Guaranteed`, `Forwarded` and `Internal`
  (`Envelope.SupportedFlags`) is dropped, logged at network level. That includes `Coalesced`, which
  this build cannot parse.
- **Sender:** any other bit is set only toward a neighbour with which the capability that defines
  that bit was agreed (§5.4), and never on Hello/HelloAck, before any agreement exists. A new flag is
  therefore always defined together with a capability. A relay that passes a datagram on byte for
  byte (§4.5) is a sender too: it forwards such a datagram only to a target that agreed the same
  capability.

Each flag stands for one header extension, at a fixed place in the order of §4.1, so the common ones
are read without parsing a chain. Bits 4–7 are free for the foreseen additions (per-hop
authentication is the candidate for bit 4); if they run out, one of them becomes "an extension chain
follows", defined with a capability like any other flag.

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
| 2–8 | — | Unassigned. They were reserved for mesh messages; the mesh over JFP2 designs its own set (review item B3) and assigns them. |
| 9 | GuaranteedDone | Implemented (§4.4) |

Hello, HelloAck and GuaranteedDone are never offered (§5.2): every build speaks them, always at
version 1.

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
| 255 | Extended | Reserved, never assigned (§4.6) — *specified, not implemented* | — |

### 4.4 Guaranteed delivery

When `Guaranteed` is set, 4 bytes follow the fixed header:

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | GuaranteedId |
| 2 | 1 | GuaranteedIndex |
| 3 | 1 | GuaranteedCount |

Behaviour, as implemented:
- JFP2 guaranteed messages are single-datagram: index/count are always 0/1. Multi-segment delivery
  is reserved by the field layout but not used. The field limits keep every payload within the
  1,100-byte ceiling (§6.7), which is also the planned segment size
  (`Jfp2Reliability.GuaranteedSegmentSize`); a payload over it can only come from a codec bug, and
  is sent as one datagram and logged. A received datagram with count > 1 is logged and dropped,
  and **not acknowledged**, so its sender does not take a message that was never delivered as
  delivered. `Jfp2Reliability` holds this logic: pending segments and acks already carry the
  index, and `Send`/`Reassemble` are where segmentation goes.
- The sender retransmits every **2 s** until acknowledged, giving up after **180 s** (as legacy
  does). Each attempt goes through the target's next hop as it is then (§5.7), so a route change or
  a session that is demoted and verified again does not strand the message. While JFP2 has no hop
  to the target that agreed the message's schema version, the message waits; it is never handed to
  legacy.
- **The id rule.** A guaranteed id is unique per **(origin, final target)** within the 30 s
  duplicate window; one message to several targets may share one, as legacy broadcasts do. A node
  takes the ids of its own messages from one counter, which counts up from a start taken from the
  clock, so a node that restarts does not reuse the ids its peers still remember from before. A
  guaranteed id is never 0: the counter skips it, and inside a node 0 means "no origin id". A
  relay that re-sends a message on its origin's behalf keeps the origin's id (below). Pending
  segments are kept per (origin, final target, id, index), since a relay holds ids it did not
  choose.
- The receiver answers every guaranteed datagram, including duplicates, with an internal
  `GuaranteedDone` whose payload is the u16 `GuaranteedId` and the u8 `GuaranteedIndex` of the
  segment it acknowledges: always 3 bytes, like legacy's. A shorter `GuaranteedDone` acknowledges
  nothing. It delivers the message only the first time; ids are remembered for **30 s** for
  duplicate suppression, keyed by (origin, id), the origin being the true sender, and reassembly
  is keyed the same way.
  A message of a class that was never agreed with the neighbor is neither acknowledged nor
  delivered, so the sender does not take it as delivered.
- When the acknowledged datagram arrived relayed (§4.5), the `GuaranteedDone` is itself sent
  `Forwarded`, with origin = the acknowledging node and target = the true sender, so it travels back
  through the relay end to end. A plain `GuaranteedDone` comes only from the neighbour that was the
  final target of the sender's own message. Either way an ack names both ends, and the sender
  clears exactly the pending segment of that (origin, final target, id, index): never another
  target's copy, nor another origin's message with the same id.
- **A relay that translates** (decodes and re-sends, to legacy or to another JFP2 schema version,
  §7.7):
  - deduplicates and reassembles per (origin, final target, id), so one origin's message for two
    targets under one id reaches both;
  - acknowledges upstream itself, with a `Forwarded` `GuaranteedDone` whose origin is the final
    target and whose target is the original sender, so the sender clears the copy for that target;
  - re-sending in JFP2, keeps the origin's id instead of taking one from its own counter (the final
    target deduplicates by the origin's ids, where one of the relay's could collide with one of the
    origin's own), and consumes the downstream ack, which names the origin and the target exactly.
    Every other Forwarded ack is passed on.

### 4.5 Relay extension (Forwarded)

When `Forwarded` is set, 16 bytes follow the (optional) guaranteed extension:

| Offset | Size | Field |
|---|---|---|
| 0 | 8 | Origin — the name (§4.9) of the node the message really comes from |
| 8 | 8 | Target — the name of the node it is ultimately for |

This build writes every node by its kind-0 name, its legacy node id. Every mesh member already knows
every other member's legacy id through the legacy mesh, so no new synchronization is needed. A relay
never rewrites the names; a relayed Position is 127 bytes (8 + 16 + 103).

**Two levels of addressing.** Origin and Target are end to end. SenderPeerId and RecipientPeerId in
the fixed header stay *hop-scoped*, exactly as on a direct datagram: they name the session between
the two nodes that exchange this datagram (sender to relay, then relay to target). The receiver
therefore finds the neighbour session by id and never guesses it from the source endpoint. A relay
rewrites the two ids, which sit at fixed offsets, when it passes a datagram on.

Receiving rule, identical on every node:
- **Target is one of my names:** consume it, attributing it to Origin and decoding it with the
  schema agreed with the neighbour it came from. Names are compared as bytes, whatever their kind.
- **Otherwise:** relay it, but only if Target is a *direct* neighbour (no relay involved in
  reaching it) and the node's relay budget (10 concurrent senders, shared with legacy relaying)
  allows it. That caps relaying at one hop.
  - If the target has a verified session with the relay and agreed the **same schema version** for
    the class as the sender's hop used (or the datagram is internal), forward the bytes with the hop
    ids rewritten.
  - Otherwise — a legacy-only target, or a different version — decode the message and hand it to the
    translation path (§7.7), which re-sends it in the target's own terms. The translation path is
    thereby also a per-hop version adapter.
- **A name it cannot resolve** (§4.9) — the Origin of a datagram it consumes, or the Origin or
  Target of one it would relay: the datagram is dropped, logged at network level, and a guaranteed
  one is not acknowledged, so its sender keeps retrying (until it gives up after 180 s).

Both fields are always present. A single field whose meaning flips by direction was rejected: in
neither role would that field equal the receiver's own name, so a node could not tell "relay further"
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

### 4.8 `WireEndPoint`

How the wire writes an address and port (`WireEndPoint.cs`; in memory an `IPEndPoint`):

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Family: `4` IPv4, `6` IPv6; others unassigned |
| 1 | 4 or 16 | Address, network byte order |
| 5 or 17 | 2 | Port, u16 little-endian |

7 or 19 bytes. The size follows from the family, so the container bounds it: a TLV's length today,
a u8 length before each entry in a list (membership lists, pathfinder targets, once the mesh itself
runs over JFP2). A reader ignores a value with an unassigned family or shorter than its family
needs, and reads a longer one up to the family's size, so a later build can extend it. An
IPv4-mapped IPv6 address (what a dual-mode socket reports for an IPv4 peer) is written as family 4
and read as the plain IPv4 address, so one peer never has two forms. It replaces the earlier
`PeerKey`, which was never used and mixed identity (`Local`) with address: an address is only an
address, a node's identity is its name (§4.9).

### 4.9 Names

Every reference to a node on the JFP2 wire is a **name** of 8 bytes (`NodeName`): the handshake's
`Names` extension (§5.5) and the Forwarded extension (§4.5).

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Kind |
| 1 | 7 | Value, by kind |

| Kind | Value | Status |
|---|---|---|
| 0 | Legacy node id: `ip` (u32), `port` (u16), `local` (u8), little-endian, the legacy header's layout | Sent and resolved |
| 1 | Random key, 56 bits, not all zero | Reserved: not sent by this build |
| 2 | Key-pair id: the first 7 bytes of the SHA-256 of the node's public key | Reserved |
| 3–254 | — | Unassigned |
| 255 | Group name: `FF 00 00 00 00 00 00 00` = all members of the session | Reserved |

All zero is "no node". Two names are equal when their 8 bytes are equal, whatever the kind.

- **Which name a node writes for another:** its kind-0 name while it has a legacy id, otherwise its
  key. Every node has a legacy id while legacy carries membership, so this build writes kind 0
  only.
- **Comparing** needs no kind: a node consumes a Forwarded datagram whose Target is one of its own
  names, and relays the others.
- **Resolving** (finding the node a name belongs to) is by kind 0 only, converted to the legacy id;
  a kind-0 name whose ip is 0 (an all-zero name included) resolves to no node.
  What carries a name of another kind is dropped where it would have to be resolved: a handshake
  without a kind-0 name is from a node this build cannot place (§5.2), and a Forwarded datagram is
  dropped without an acknowledgement (§4.5).

## 5. Capability and schema negotiation

### 5.1 Why per message class

Each message class has its own schema version, and each peer declares the inclusive range it can
encode and decode per class. Two peers can run Position at v2 while running VariableSync at v1, with
no coupling between the decisions.

### 5.2 Hello / HelloAck

Internal-partition messages. A node sends `Hello` to the route endpoint of each peer the mesh knows
and does not reach through a relay. The reply goes back to the UDP source. Both messages say who is
speaking (the `Names` extension, §5.5): a Hello names its sender, and a HelloAck names the node that
actually answered, which is what tells a node whether the peer it asked for, or another node sharing
that endpoint, replied.

Payload (same shape for both; `Result` is meaningful only in HelloAck):

| Field | Size | Notes |
|---|---|---|
| ProtoMajorMin | 1 | Lowest envelope version this build speaks (2). |
| ProtoMajorMax | 1 | Highest (2). |
| Capabilities | 8 | u64 bitset, §5.4. |
| SelfAssignedId | 2 | The PeerId this node wants to be addressed by. |
| Result | 1 | HelloAck: `0` accepted; any other value, no session now (below). |
| OfferCount | 2 | Number of offers. |
| Offers | 4 × OfferCount | `Partition(1) MessageClass(1) MinVersion(1) MaxVersion(1)`; partition `0` application, `1` internal. |
| Extensions | rest | TLV records, §5.5. |

**`Result`:**

| Value | Meaning |
|---|---|
| 0 | Accepted |
| 1 | No compatible ProtoMajor |
| 2 | Not admitted: no session was created (reserved for the mesh over JFP2; this build never sends it) |
| 3–255 | Unassigned |

A receiver reads any value other than `0` as **"no session now"**, whatever the reason: a later
build may refuse for one this build does not know (too many sessions, not admitted), and that says
nothing about whether it speaks JFP2. So it does not take the peer for legacy-only: it asks again
after the 30 s cooldown, routes the peer through legacy meanwhile, as it does for any peer without
a session, logs the value, and still reads the HelloAck's extensions.

**Offers.** An offer whose partition byte is neither `0` nor `1` belongs to a class space this build
does not know, and is skipped; the offers after it are read as usual. Hello, HelloAck and
`GuaranteedDone` are never offered.

**The permanent entry point.** Hello/HelloAck is how every build, past and future, starts talking to
every other, so from 26.6 on it never changes:
- the envelope: ProtoMajor `2` (`Envelope.HandshakeProtoMajor`, a constant of its own that stays `2`
  even if `Envelope.ProtoMajor` moves), the `Internal` flag and no other, classes `0` and `1`;
- the payload's fixed fields and the offer list layout above.

Anything new goes into the extension area (§5.5). A future major version is reached by negotiating
it inside a ProtoMajor-2 Hello: a build that speaks 2 and 3 would offer ProtoMajorMin `2`, Max `3`,
which this build accepts and answers in 2. The major version is agreed per session, like the
capabilities, not per class: every later datagram of that session uses it, except Hello and HelloAck
(keepalives included), which always travel in ProtoMajor 2. `HandshakeGoldenTests`
pins the exact bytes.

**This rule binds from 26.6**, the first release that speaks JFP2. Until 26.6 ships, JFP2's wire,
the handshake included, may still change, and 26.6 waits until it is right
(`docs/jfp2-wire-design.md` §1): testers run the same daily build, and between two builds on either
side of such a change the legacy fallback carries the session. `HandshakeGoldenTests`
changes only with a deliberate change of the handshake, never to make a failing test pass.

Behaviour, as implemented (`Jfp2Plugin`):
- A Hello whose `Names` hold no kind-0 name, or none of a known mesh peer, is ignored. The legacy
  Join always happens first, and a build that doesn't say who it is stays on legacy. Names of other
  kinds are skipped (§4.9), and so is a kind-0 name that holds no valid node id (ip 0). With several
  kind-0 names, the speaker is the first that is a known mesh peer.
- A Hello whose ProtoMajor range includes 2 (for example 2..3) is answered with `Result` `0`; one that
  excludes 2 is answered with `Result` `1`. Unknown extension tags are skipped.
- The `Build` extension (§5.5) is remembered per session (and forgotten when a later handshake
  omits it) and logged as `JFP2: <node> runs build <build>`: the first build learned for a session at
  event level, later changes at network level only.
- An unanswered Hello is retried every **2 s**. After **5** attempts the peer is marked
  `AssumedLegacy` (legacy only) and is tried again after **30 s**, or at once if it sends a Hello
  itself.
- Only one Hello is outstanding per endpoint. Each Hello tells the node that answers which id to use
  for us, so two in flight to one endpoint would leave that node holding the wrong one.
- A HelloAck is matched to the session by the id it is addressed to, and must name the node the Hello
  was for. If another node answers, that node owns the endpoint (see §5.7) and the peer we asked for
  is not there.
- A HelloAck with `Result != 0` means no session now (above): a verified session with the peer stops
  being verified, and the peer is asked again after **30 s**, not sooner, even if it sends a Hello
  itself. The answer's `Build` is remembered. The log line gives the value:
  `JFP2: <node> answered Result <n> (no session now)`.
- A refusal counts only when it answers our Hello: it comes from the endpoint the Hello went to and
  its `Names` resolve to the peer asked. The session id it is addressed to is 16 bits and could be
  guessed off the path, so any other refusal (from another source, naming another node or none) is
  logged at network level and otherwise ignored. A node sharing the endpoint that refuses is
  therefore not taken for the peer asked. The same holds for the answer's `ObservedEndPoint`
  (§5.5): it is counted only from a HelloAck that answers our Hello, from the endpoint the Hello
  went to, so an answer from elsewhere cannot plant an address; the occupant found answering at
  that endpoint may report it.
- A HelloAck with `Result` `0` but without a valid kind-0 name (ip not 0) marks the peer
  `AssumedLegacy`.
- Receiving a peer's Hello lets us *decode* what it sends. It does not make the session usable for
  *sending*: only the ack of our own Hello proves our datagrams reach that node.

Current offers: every application class 0–9 at version range [1, 1]; no internal classes. They are
derived from the plugin's profile (§6.6), in its order: Status, StatusRequest, Identity,
VariableSync, Position, Event, FlightPlan, Notes, Weather, WeatherReply.

### 5.3 Resolution

For each (Internal, MessageClass) key offered by either side:

```
lo = max(local.Min, remote.Min)
hi = min(local.Max, remote.Max)
agreed = hi >= lo ? hi : 0
```

`0` means "don't send this class to this peer". The result is stored once per peer session in two
flat 256-entry arrays, `AgreedAppVersion[]` and `AgreedInternalVersion[]`. Sending a message is then
one array read for the version and one for its codec (the class descriptor's codecs, indexed by
version, §6.6). The application-level router caches the choice per (peer, kind)
too (`docs/reference/joinfs-architecture.md` §5.3), so the hot path never negotiates.

Every Hello and HelloAck, keepalives included, resolves again from scratch. When the result changes
(the peer restarted with a build that offers other classes) the router's cached choices are dropped,
so a class the peer no longer takes goes back to legacy instead of to a JFP2 hop that would drop it.

### 5.4 Capabilities — *none assigned*

A u64 bitset for behaviours not tied to one class's schema. Agreed capabilities are the bitwise AND
of both sides'. A plugin advertises its profile's (`Jfp2Profile.Capabilities`): this build's profile
advertises none, and no build has ever advertised one.

No bit is assigned. Each is assigned by the design that needs it, together with any flag it defines
(§4.2). The four meanings once reserved here (Coalescing, QuantizedPosition, Ipv6Peers,
SelectiveAck) are released: Position v2 is chosen by schema version alone, names (§4.9) make an
IPv6 capability unnecessary, and coalescing and selective acknowledgement get theirs with their
designs.

### 5.5 Extension area (TLV)

`(Tag: u16, Length: u16, Value)` records after the offer list, so the handshake can grow without a
new envelope version. Reading rules:
- **Unknown tags** are skipped by their length. A record whose length runs past the end of the
  datagram ends the area.
- **A short value**, shorter than its tag needs (`Names` under 8 bytes, an empty `Build`), is
  ignored.
- **A long value** is read up to the prefix this build knows, and the rest is skipped: that is how a
  later build extends a value. `Names` is read in whole names (a remainder under 8 bytes is
  skipped), `Build` up to 64 bytes, `ObservedEndPoint` up to its family's size.
- **A repeated tag:** the first copy counts. A list goes inside one value.
- Tags are appended and never reused, like classes.

| Tag | Name | Value |
|---|---|---|
| 1 | Names | The speaking node's own names (§4.9), 8 bytes each, preferred first; in a HelloAck, the names of the node that answered. This build sends one, its kind-0 name, and reads the whole names of a value (a shorter remainder is skipped). Required: a handshake message without a kind-0 name is treated as coming from a legacy-only peer. |
| 2 | Build | UTF-8 text naming the speaking node's build, for diagnostics and for counting which builds speak JFP2; JoinFS sends `<version> <assembly name>`, e.g. `26.6.0 JoinFS-FS2024`. JoinFS sends and keeps only printable ASCII (`0x20`–`0x7E`), at most 64 bytes: a sender cleans it to that, and a receiver cuts the value to 64 bytes and drops every other character before using it, since it ends up in the log. Optional; nothing in the protocol depends on it. |
| 3 | ObservedEndPoint | HelloAck only: a `WireEndPoint` (§4.8), the UDP source of the Hello this answers, as the responder received it (after an IPv4-mapped address is normalized). Every HelloAck of this build carries it, refusals too: 11 bytes (IPv4) on a datagram that goes every 5 s per neighbour. The asker learns the address and port a NAT gave it; JoinFS counts it only when the HelloAck answers its own Hello (§5.2), classifies the NAT from the observations of several neighbours (LAN addresses ignored) and logs the result. Nothing else uses it in release 1: the public address stays the one the HTTP lookup gives. Optional. |

### 5.6 Peers that don't speak JFP2

A peer that never answers Hello, or answers without a name this build resolves, is `AssumedLegacy`;
one that refuses a session (`Result` not `0`) has no session for now (§5.2). Everything to either
goes through the legacy plugin, unless a neighbour that does speak JFP2 carries its traffic (§5.7),
in which case that neighbour translates. No JFP2 capability is ever assumed beyond what negotiation
established.

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
FlightNumber, ClassCode, Wtc, each within its limit (§6.7).

This is a structural fix for §1.2: identity is one message class with one schema version, so there is
no runtime/compile-time gating boundary for these fields to fall on either side of.

### 6.3 VariableSync — one message for three legacy ones

Replaces the legacy Integer/Float/String8 variable messages. Each entry carries its own kind tag, so
one code path handles every kind (closing docs/recording-protocol.md §7.2's bug class).

**VariableSyncV1** layout: `ObjectId u32`, `Count u8`, then Count × entries of `Vuid u32`,
`Kind u8` (0 Int32, 1 Float32, 2 String8), and a value (i32, f32, or u16-prefixed UTF-8 string of
at most 256 bytes, §6.7). Senders split an update into messages of at most **1000** payload bytes
(and at most 255 entries, the count being one byte), so every datagram stays under a safe UDP MTU,
as legacy's variable messages do. An entry is at most 263 bytes, so a single entry always fits.
The owner of the object is the sender (or the relayed origin); there is no owner field.

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
EOF-sensed parts. The strings of every class have byte limits (§6.7).

Notes carries one note per message. A bulk "all notes" reply is sent as one Notes message per note.

**Not carried by JFP2** (always legacy): object positions of non-aircraft objects, RemoveObject,
ShowOnRadar, PeerInfo (legacy SharedData), WeatherRequest, hub directory messages, comms requests,
and all mesh messages.

### 6.5 Variable identification: `vuid`

Variables are identified by the same 32-bit `vuid` the legacy protocol uses: `VariableMgr.CreateVuid`
(`NetHash.HashString(name)`, with 0 remapped to 1 and an alias table). Every peer computes it
independently from the name, so no negotiation is needed.

### 6.6 Adding a message class or a schema version

Every fact about a class lives in one `ClassDescriptor` (`Net/Protocols/Jfp2/ClassDescriptor.cs`):
the canonical `MessageKind`, its class number (a `MessageClasses` constant), its partition
(application for now: internal classes become descriptors with the mesh over JFP2), whether it is sent
guaranteed, one codec per schema version, and how a decoded message reaches the core. A
`Jfp2Profile` is the set of descriptors a `Jfp2Plugin` speaks. The Hello offers, routing
(`CanCarry`), encoding and decoding all read the profile, and nothing else lists classes.

**A new schema version of a class:** write the codec (`ICodec<T>`, `SchemaVersion` = the next
number) and add it to the class's descriptor in `Jfp2Profile.Default`. Its offer becomes
`[min, new]`; peers agree it only when both sides speak it (§5.3). A codec stays as long as some
release may still agree its version.

**A new plain class** (its canonical message is what goes on the wire, as Event or Weather): append
its number to `MessageClasses` (never reuse one, §4.3), write its codec, and append
`ClassDescriptor.Plain(MessageClasses.X, guaranteed, new XV1Codec())` to `Jfp2Profile.Default`.
Append it last: the offer order is visible in Hello. A plain class whose delivery must add something
(FlightPlan: the owner is the sender) passes a `Delivery<T>` to `Plain`. A class sent its own way
(Identity ahead of Position, VariableSync in chunks, Notes one note per message) is made with
`ClassDescriptor.SentByPlugin` and needs a sender in the plugin's `Encoder`; `Jfp2Plugin` refuses a
profile with such a class for a kind it has no sender for, rather than advertising it and dropping
every message.

**Testing version skew.** Each `Jfp2Plugin` takes a profile, so a `TestMesh` can run an older and a
newer build side by side: `Jfp2Profile.Default.Without(MessageClasses.X)` is a build that lacks a
class, `Default.With(descriptor.WithCodecs(v1, v2))` one that speaks another version range, with
a test-only codec for a version that does not exist yet, and `Default.WithCapabilities(bits)` one
that advertises other capabilities. `Jfp2VersionSkewTests` does all three,
including a relay translating between versions (§7.7), and `Jfp2WireCharacterizationTests` pins what
the default profile sends.

### 6.7 Field limits and the datagram ceiling

No JFP2 datagram is larger than **1,200 bytes** (`Envelope.MaxDatagramSize`): IPv6's minimum MTU of
1,280 less 48 bytes of IPv6 and UDP headers, with room for a tunnel. Headers are at most 28 bytes
(8 fixed, 4 guaranteed, 16 Forwarded), and room is kept for 24 bytes of per-hop security
(`docs/jfp2-wire-design.md` §2.5), so every v1 payload is at most **1,100 bytes**
(`Envelope.MaxPayloadSize`). The v1 codecs guarantee this by construction: every string field has a
byte limit (UTF-8 bytes, without the u16 length prefix), a constant in its codec next to the field
(`IdentityV1Codec.CallsignLimit`, ...), and `MaxSize` is the codec's largest payload.

- **Sender:** a longer text is cut to its limit at a UTF-8 character boundary, never inside a
  character, so what goes out is still valid UTF-8. `WireText.WriteString` cuts, and it takes the
  limit as a required argument, so no codec writes a string without one. A lone UTF-16 surrogate,
  which is not text, goes out as U+FFFD.
- **Receiver:** checks no limit. A v1 message is valid at any size that fits its datagram, and a
  receiver accepts any datagram its buffer holds (16 KB).
- **Legacy:** a text cut to a limit (a hub's About, a long note or route) can differ from what legacy
  peers receive for the same message; legacy has no such limits.
- A later schema version of a class may raise its limits once multi-segment delivery exists.

| Class | Fixed bytes | String limits (bytes) | Largest payload |
|---|---|---|---|
| Position | 103 | — | 103 |
| Identity | 6 + 9 prefixes = 24 | Callsign 32, Model 256, Livery 256, IcaoType 8, IcaoAirline 8, Registration 32, FlightNumber 16, ClassCode 16, Wtc 8 | 656 |
| VariableSync | 5 + per entry 5 (+ 2 + string) | `String8` value 256: an entry is at most 263 bytes, so a single entry always fits; messages are at most 1,000 bytes (§6.3) | 1,000 |
| Event | 12 | — | 12 |
| FlightPlan | 4 + 13 prefixes = 30 | IcaoType 8, Departure 8, Destination 8, Rules 8, Route 512, Remarks 256, Alternate 8, Speed 16, Altitude 16, Callsign 32, Registration 32, IcaoAirline 8, FlightNumber 16 | 958 |
| Notes | 26 + 3 prefixes = 32 | Nickname 32, Callsign 32, Text 768 | 864 |
| Weather, WeatherReply | 2 | Metar 1,024 | 1,026 |
| StatusRequest | 5 | — | 5 |
| Status | 34 + 8 prefixes = 50 | AppVersion 32, AtcAirport 8, Address 128, Name 64, About 512, Voip 128, NextEvent 128, Airport 8 | 1,058 |

The largest, Status at 1,058 bytes, would make a 1,086-byte datagram with every header, 1,110 with
per-hop security; since Status is not guaranteed, relayed it is 1,082 (8 + 16 + 1,058). Handshakes
are far below: about 100 bytes, their `Build` being at most 64 (§5.5).

## 7. Coexistence with the legacy protocol

**7.1 One socket.** The magic byte (§3) routes each incoming datagram to the right plugin before
anything else is parsed. Both protocols share the socket and port for as long as legacy is spoken;
a protocol after JFP2 would take another magic byte.

**7.2 Per peer and per message kind.** The choice between JFP2 and legacy is made per peer and per
message kind, not per mesh:
- JFP2 is used for a kind when the peer's next hop (§5.7) negotiated it (§5.3).
- Everything else goes over legacy: kinds JFP2 doesn't carry (§6.4), `AssumedLegacy` peers, and
  sessions that are not (or no longer) verified.
- Every node speaks legacy today, so falling back is always possible. That ends with the mesh over
  JFP2 (review item B3), when a node may speak JFP2 alone; how such a node falls back is B3's
  design (`docs/network-plugin-architecture.md` §2.13).
- The choice is re-evaluated when a peer's route or a session's state changes.

**7.3 Independent versions per class.** No class's version is coupled to another's (§5.1).

**7.4 Reserved values are skipped, not rejected — except in the envelope.** TLV tags and (once
implemented) coalesced sub-messages and extended classes all declare their own size (as does a
`WireEndPoint`, whose family fixes it, §4.8), so a reader can skip what it doesn't understand. This is the structural alternative to
EOF-sensing (§1.3). The envelope is the exception: an unknown ProtoMajor or flag bit can change where
everything after it lies, so such a datagram is dropped (§4.1, §4.2).

**7.5 No compile-time wire gating.** Codecs are selected only by the negotiated schema version.
Simulator build symbols (`FS2020`, `FS2024`, `XPLANE`, `CONSOLE`, ...) never change a wire shape.

**7.6 IPv6 is additive**, through `WireEndPoint` (§4.8): family 6 is already defined, and a reader
that only understands IPv4 ignores it.

**7.7 Relaying and translation.**
- **Two JFP2 neighbours of a relaying node that agree on a class *and its schema version*:** the
  relay forwards the datagram (§4.5) with the hop ids rewritten, like legacy `FLAG_FORWARD`.
- **The target agreed a different version:** the relay decodes with the sender's version, and the
  core re-sends it encoded for the target's. A JFP2 Position goes out only after its object's
  Identity (§6.2), so a relay that forwards an Identity byte for byte also decodes it into its
  identity cache, and remembers that the target has it. A guaranteed message is acknowledged
  upstream in the target's name and re-sent under the origin's id; the target's ack, addressed to
  the origin, ends at the relay (§4.4).
- **The target doesn't speak JFP2 for that class:** the relaying node decodes the message into its
  canonical form and hands it to the application's network core. The core sends it to the target
  with whichever protocol reaches it — legacy, in practice — keeping the true sender:
  - The legacy encoding writes the original sender into the header, with the Forward flag set, which
    is exactly what a legacy receiver expects from a relayed message.
  - Identity is not forwarded as its own message. It updates a per-object cache that the legacy
    encoder inlines into the next position.
  - Guaranteed messages are delivered hop by hop: the relaying node acknowledges upstream in JFP2,
    with a `Forwarded` `GuaranteedDone` in the target's name (§4.4), then sends a legacy guaranteed
    message downstream, with legacy's own ids, and consumes the legacy acknowledgement itself. An
    upstream retransmission (its ack was lost) is acknowledged again but not sent downstream a
    second time: the relay remembers the (origin, target, id) triples it translated, so one
    origin's message for two targets under one id is translated for each.
- **The reverse direction (legacy → JFP2)** never needs translation: every JFP2 node understands
  legacy today (§7.2; until B3), so the legacy relay carries it unchanged.

There is no protocol-pair-specific bridge code. See `docs/reference/joinfs-architecture.md` §6.

## 8. Recording format

The recording format (`docs/recording-protocol.md`) is independent of JFP2. Its version is
`Recorder.FileVersion`, no longer tied to any wire version. Adopting the codec pattern of §6 for
recordings (explicit per-record-type versions instead of EOF-sensing) remains a possible follow-on.

## 9. Status and future work

**Implemented:**
- the envelope with its guaranteed and relay extensions, hop-scoped ids on relayed datagrams;
- Hello/HelloAck with per-class negotiation, node names (kind 0, §4.9), the build advertisement,
  the observed endpoint (§4.8, §5.5; logged with a NAT class, otherwise unused), and verified
  sessions with a keepalive;
- dropping datagrams of another ProtoMajor or with flags this build cannot read;
- next-hop origination of relayed traffic, and relay or translation at the hop;
- single-datagram guaranteed delivery;
- a byte limit for every string of the v1 codecs, which keeps every datagram within 1,200 bytes
  (§6.7);
- the v1 codecs for all ten application classes, each class one descriptor of a per-plugin profile
  (§6.6).

It was first field-tested against MSFS 2024 before the plugin architecture; the shared-endpoint
topology in `JoinFS.Tests/Net/Jfp2RelayTests.cs` reproduces the failure of the next field test.

**Specified, not implemented:**
- name kinds other than 0 (§4.9)
- PositionV2
- Coalescing
- the `Extended` escape hatch
- `WireEndPoint` lists (membership lists, pathfinder targets)
- capability bits: they are exchanged and agreed, but none is assigned (§5.4)
- multi-segment guaranteed delivery
- JFP2 mesh messages

**Open questions:**
- **A JFP2-native mesh** — *decided*: JFP2 will carry Join/Pulse/Pathfinder (review item B3), since
  IPv6, authentication and retiring legacy are now goals (`docs/network-plugin-architecture.md`
  §2.13). It needs a pre-membership bootstrap with admission (review items B2/B3) and membership
  lists split across messages; `docs/jfp2-wire-design.md`, appendix A, sketches both.
- **Relay fan-out:** one datagram to a hub for "all your other neighbours" instead of one per peer,
  which would cut the uplink of a node behind a hub. It needs relay budgets and amplification limits.
- **Limits of verification behind a shared endpoint:** a node whose replies are steered to another
  node cannot verify the path back, so that direction stays on legacy (§5.7).
- **Selective acknowledgement** and **coalescing policy** (batch window, eligible classes).
- **Governance for ProtoMajor 3+** — *resolved.* Hello/HelloAck is the permanent entry point from
  26.6 on: its ProtoMajor-2 envelope, fixed fields and offer layout never change, anything new goes
  into the TLV extension area, and a later major version is agreed through ProtoMajorMin/Max inside
  a ProtoMajor-2 Hello (§5.2). Every other datagram keeps the magic/version-byte dispatch before
  anything else is parsed, and a receiver drops a ProtoMajor or flag it does not know (§4.1, §4.2).
