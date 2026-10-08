# JFP2: the JoinFS Protocol, version 2

**Specification, edition 1.** It defines the wire format and the behaviour that two JFP2 nodes need
to interoperate. It does not say why the protocol looks this way (`rationale.md`), how the code
implements it (`implementation.md`), or what is still to be built (`roadmap.md`).

**Status of this edition.** Edition 1 is the wire of JoinFS 26.6, the first release that speaks
JFP2. Until 26.6 ships, parts of it may still change deliberately (`goals.md` §5); from 26.6 on,
§5.8 and §10 bind. Where the implementation does not yet do something this edition specifies, or
the reverse, `implementation.md` §2 says so.

## Abstract

JFP2 carries the real-time state of simulated aircraft and the supporting traffic of a shared
flight (identity, variables, events, flight plans, notes, weather, hub status) between simulator
clients and hubs over UDP. It replaces JoinFS's original wire protocol ("legacy"). A JFP2 datagram
has an 8-byte header on the common path. Each message class has its own schema version, negotiated
per neighbour, so classes evolve independently. Nodes that also speak legacy keep working with
nodes that do not, through hop-by-hop translation.

## Contents

1. [Introduction](#1-introduction)
2. [Protocol overview](#2-protocol-overview)
3. [Datagram format](#3-datagram-format)
4. [Data types](#4-data-types)
5. [Handshake and negotiation](#5-handshake-and-negotiation)
6. [Sessions and routing](#6-sessions-and-routing)
7. [Guaranteed delivery](#7-guaranteed-delivery)
8. [Relaying and translation](#8-relaying-and-translation)
9. [Message formats](#9-message-formats)
10. [Extending the protocol](#10-extending-the-protocol)
11. [Security considerations](#11-security-considerations)
12. [Interoperability](#12-interoperability)
- [Appendix A. Example datagrams](#appendix-a-example-datagrams)
- [Appendix B. Constants](#appendix-b-constants)
- [Appendix C. Change history](#appendix-c-change-history)
- [References](#references)

---

## 1. Introduction

### 1.1 Purpose and scope

This document specifies the datagram format of JFP2, its handshake and version negotiation, the
rules for sessions, routing, guaranteed delivery, relaying and translation, and the message formats
of release 1. It does not specify the legacy protocol (`../network-protocol.md`), the recording
file format (`../recording-protocol.md`), or anything above the wire (simulator access, model
matching).

JFP2 does not yet carry the session's membership: the mesh (Join, Pulse, Pathfinder, directory
messages) still runs over legacy, and every JFP2 node therefore also speaks legacy (§2.4). That is
a property of release 1, not of the protocol; `roadmap.md` tracks its removal.

### 1.2 Requirements language

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT" and "MAY" are to be
interpreted as in RFC 2119 and RFC 8174 when, and only when, they appear in capitals.

### 1.3 Terminology

| Term | Meaning |
|---|---|
| Node | A running JoinFS instance: a simulator client or a hub. |
| Neighbour | A node this node exchanges datagrams with directly, without a relay. |
| Hop | One datagram transfer between two neighbours. |
| Session | The state two neighbours share once they have shaken hands: ids, agreed versions, health (§6). A session is with a neighbour, never end to end. |
| Origin, target | The node a message really comes from, and the node it is finally for. They differ from the sender and recipient of a hop when a relay is involved. |
| Relay | A node that passes a datagram from one neighbour to another (§8). |
| Hub | A node, often headless, that other nodes rendezvous through and that relays for them. |
| Class | A kind of message with one wire layout per schema version (§3.3). |
| Partition | A class number space: *application* or *internal* (§3.3). |
| Schema version | The version of one class's layout. Classes are versioned independently. |
| Capability | A bit in a 64-bit set, agreed per session, for behaviour that is not tied to one class (§5.6). |
| Name | The 8-byte reference to a node used on the wire (§4.3). |
| Legacy | JoinFS's original wire protocol, which is frozen. |
| Release 1 | The wire of JoinFS 26.6, defined by this edition. |

### 1.4 Conventions

- All multi-byte integers are **little-endian**, except the address inside a `WireEndPoint` (§4.4).
- Offsets and sizes are in bytes. "u8", "u16", "u32" and "u64" are unsigned; "i16" and "i32" are
  signed two's complement; "f32" and "f64" are IEEE 754.
- A table row gives the offset of a field within the structure the table describes.
- Section 10.2 collects every number space (flags, classes, tags, kinds, values) in one place.

---

## 2. Protocol overview

### 2.1 Components

- **Envelope** (§3): a minimal header on every datagram, 8 bytes for the common unreliable case, 12
  for guaranteed delivery, plus 16 when relayed.
- **Negotiation** (§5): a Hello/HelloAck handshake per neighbour. It yields, for each message class,
  the schema version both sides speak, and a set of agreed capabilities. The result is computed once
  and then only looked up.
- **Codecs** (§9): one encoder and decoder per (class, schema version). A new version of a class is
  a new codec; nothing else changes.

### 2.2 Sharing the socket with legacy

JFP2 and legacy share one UDP socket and port. Every JFP2 datagram starts with the magic byte
`0xFA`. A legacy datagram starts with `0x0B` (the low byte of its little-endian `0x520B` marker),
so the two never collide. A receiver MUST look at byte 0 before parsing anything else and hand the
datagram to the protocol that owns that value. A protocol after JFP2 takes another first byte.

### 2.3 Neighbours, sessions and hops

JFP2 is a property of each hop. Two neighbours that agree on a class speak JFP2 for it; the next
hop toward a node that speaks only legacy speaks legacy. A relay may therefore speak JFP2 on one
side and legacy on the other, and translates between them (§8). A message addressed beyond a relay
carries the origin and the target names (§3.5); the ids in the fixed header always name the
session of the hop (§3.1).

### 2.4 Protocol choice per peer and per message kind

The choice between JFP2 and legacy is made per peer and per kind of message, not per mesh:
- JFP2 is used for a kind when the next hop toward the peer (§6.3) has a verified session that
  agreed the class (§5.5).
- Everything else goes over legacy: kinds JFP2 does not carry (§9.6), peers that never answered a
  Hello, sessions that are not, or no longer, verified, and the membership traffic.
- The choice is re-evaluated whenever a peer's route or a session's state changes.

In release 1 every node speaks legacy, so falling back is always possible. A node that speaks JFP2
alone is outside this edition.

### 2.5 Evolution in brief

New fields and message kinds are added as new classes, new schema versions of a class, new
extension tags in the handshake, or capability-gated flags; never by changing the bytes of
something already released (§5.8, §10.1). Each of these is skipped, or not sent, by a node that
does not know it.

---

## 3. Datagram format

A datagram is: the fixed header (§3.1), the extensions its flags announce, in the order of §3.2,
then the payload. Release 1 has no trailer.

### 3.1 Fixed header (8 bytes)

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | Magic | MUST be `0xFA`. |
| 1 | 1 | ProtoMajor | `2`. A receiver MUST drop a datagram with any other value, because nothing after this byte need look the same in a later major version. Hello and HelloAck always carry `2` (§5.8). |
| 2 | 1 | Flags | §3.2. |
| 3 | 2 | SenderId | u16: the sender's id for the session between the two nodes exchanging *this datagram*, assigned in the handshake (§5.1). |
| 5 | 2 | RecipientId | u16: the receiver's id for that session. A receiver finds the session by this id alone, never by source endpoint (§6.1). `0` while the sender does not know it yet, which is only the case in the first Hello to a neighbour. No session has id `0`. |
| 7 | 1 | Class | §3.3. |

### 3.2 Flags

| Bit | Name | Meaning |
|---|---|---|
| 0 | Guaranteed | The 4-byte guaranteed extension follows (§3.4). |
| 1 | Forwarded | The 16-byte forwarded extension follows (§3.5). |
| 2 | Coalesced | Reserved. |
| 3 | Internal | Class is in the internal partition. |
| 4–7 | — | Unassigned. |

The extensions follow the fixed header in flag-bit order: guaranteed, then forwarded.

**Rules.**
- A receiver MUST drop a datagram with any bit set other than Guaranteed, Forwarded and Internal,
  and SHOULD log it at network level. A flag may add a header extension or change the framing of
  the payload, so a receiver that ignored it would misparse everything after it.
- A sender MUST NOT set any other bit toward a neighbour with which the capability that defines
  that bit was not agreed (§5.6), and MUST NOT set one on Hello or HelloAck, before any agreement
  exists. A new flag is therefore always defined together with a capability.
- A relay that forwards a datagram byte for byte is a sender too: it MUST forward a datagram with
  such a bit only to a target that agreed the same capability.

### 3.3 Message classes

The class byte selects a message class within a partition. The Internal flag selects the partition.
Every value is permanently assigned: a number MUST NOT be reused or renumbered once released, and
new classes take the next unused number.

**Internal partition**

| Value | Class | Notes |
|---|---|---|
| 0 | Hello | §5.1 |
| 1 | HelloAck | §5.1 |
| 2–8 | — | Unassigned. |
| 9 | GuaranteedDone | The acknowledgement of §7.3. |

Hello, HelloAck and GuaranteedDone are never offered (§5.3): every node speaks them, always at
version 1.

**Application partition**

| Value | Class | Guaranteed? | Section |
|---|---|---|---|
| 0 | Position | no | 9.1 |
| 1 | Identity | no | 9.2 |
| 2 | VariableSync | no | 9.3 |
| 3 | Event | yes | 9.4 |
| 4 | FlightPlan | no | 9.4 |
| 5 | Notes | yes | 9.4 |
| 6 | Weather | no | 9.4 |
| 7 | Status | no | 9.4 |
| 8 | StatusRequest | no | 9.4 |
| 9 | WeatherReply | yes | 9.4 |
| 10–254 | — | | Unassigned. |
| 255 | Extended | | Reserved, never assigned. |

A sender MUST send only classes the receiver offered and the session agreed (§5.5). A message of
a class that was never agreed with the neighbour it arrives from MUST NOT be acknowledged or
delivered, so that its sender does not take it as delivered.

### 3.4 Guaranteed extension (4 bytes)

Present when the Guaranteed flag is set, after the fixed header.

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | GuaranteedId |
| 2 | 1 | GuaranteedIndex |
| 3 | 1 | GuaranteedCount |

Release 1 sends index `0` and count `1`: a guaranteed message is one datagram. Behaviour is in §7.

### 3.5 Forwarded extension (16 bytes)

Present when the Forwarded flag is set, after the guaranteed extension if there is one.

| Offset | Size | Field |
|---|---|---|
| 0 | 8 | Origin: the name (§4.3) of the node the message really comes from |
| 8 | 8 | Target: the name of the node it is finally for |

Both fields are always present. Origin and Target are end to end. SenderId and RecipientId in the
fixed header stay hop-scoped, exactly as on a direct datagram: they name the session between the
two nodes that exchange this datagram. A relay rewrites those two ids, which sit at fixed offsets,
when it passes the datagram on, and MUST NOT rewrite the names. A receiver therefore finds the
neighbour's session by id and never guesses it from the source endpoint. Receiving rules are in §8.2.

### 3.6 Datagram size

No JFP2 datagram MAY exceed **1,200 bytes**: the minimum IPv6 MTU is 1,280, less 48 bytes of IPv6
and UDP headers, with room for a tunnel. Headers are at most 28 bytes (8 fixed, 4 guaranteed, 16
forwarded). Room is kept for 24 bytes of per-hop protection that a later edition may add (§11.3),
so every payload is at most **1,100 bytes**. The field limits of §9.7 guarantee this for every
class of release 1. A receiver MUST accept any size its buffer holds (a receive buffer of at least
16 KB) and MUST NOT check the 1,200-byte limit.

### 3.7 Datagrams a receiver drops

A receiver drops, and SHOULD log at network level, a datagram that:
- does not start with `0xFA` (it belongs to another protocol, §2.2);
- has a ProtoMajor other than `2`;
- has a flag bit it does not support (§3.2);
- names a session id it does not hold, other than a Hello (§6.1);
- is a Forwarded datagram whose Origin or Target it cannot resolve (§8.2). A guaranteed one MUST
  NOT be acknowledged, so its sender keeps retrying until its own limit (§7.1);
- is of a class not agreed with the neighbour (§3.3), or a guaranteed segment of several (§7.5).

A receiver MUST NOT stop reading the socket, and MUST NOT disturb any session, because of a
datagram it drops.

---

## 4. Data types

### 4.1 Numbers

Integers are little-endian. Floating-point values are IEEE 754, little-endian. A fixed-point i16 is
the value times 16384.

### 4.2 Text

A string is UTF-8 with a **u16 length prefix** (the length in bytes, without the prefix). Every
string field of every class has a byte limit (§9.7). A sender MUST cut a longer text to the limit
at a UTF-8 character boundary, never inside a character, so what it sends is valid UTF-8. A lone
UTF-16 surrogate, which is not text, is sent as U+FFFD. A receiver does not check limits: a
message is valid at any size that fits its datagram.

### 4.3 Names

Every reference to a node on the wire is a **name**: 8 bytes.

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Kind |
| 1 | 7 | Value, by kind |

| Kind | Value | Use in release 1 |
|---|---|---|
| 0 | Legacy node id: ip u32, port u16, local u8, the legacy header's layout | Sent and resolved |
| 1 | Random key, 56 bits, not all zero | Reserved |
| 2 | Key-pair id: the first 7 bytes of the SHA-256 of the node's public key | Reserved |
| 3–254 | — | Unassigned |
| 255 | Group name: `FF 00 00 00 00 00 00 00` is all members of the session | Reserved |

All zero is "no node". Two names are equal when their 8 bytes are equal, whatever the kind.

- **Which name a node writes for another.** The kind-0 name while that node has a legacy id,
  otherwise its key. Every member has a legacy id while legacy carries membership, so release 1
  writes kind 0 only.
- **Comparing** needs no kind: a node consumes a forwarded datagram whose Target is one of its own
  names, and relays the others.
- **Resolving** (finding the node a name belongs to) is by kind 0 only, converted to the legacy
  node id; a kind-0 name whose ip is 0 resolves to no node. A node MUST skip, and MUST NOT guess
  at, names of other kinds. Where a name would have to be resolved and cannot be, the rules of §8.2
  and §5.7 apply.
- **Listing.** A node has one name per kind it has. Handshakes list all of them (§5.4).

### 4.4 WireEndPoint

How the wire writes an address and port.

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Family: `4` IPv4, `6` IPv6; others unassigned |
| 1 | 4 or 16 | Address, network byte order |
| 5 or 17 | 2 | Port, u16 little-endian |

The size follows from the family (7 or 19 bytes), so the container bounds it: a TLV's length, or a
u8 length before each entry in a list. A reader MUST ignore a value with an unassigned family or
shorter than its family needs, and MUST read a longer one up to the family's size. An IPv4-mapped
IPv6 address (what a dual-mode socket reports for an IPv4 peer) MUST be written as family 4, so one
peer never has two forms.

---

## 5. Handshake and negotiation

### 5.1 Hello and HelloAck

Hello and HelloAck are internal-partition classes 0 and 1. Their payload has the same shape:

| Field | Size | Notes |
|---|---|---|
| ProtoMajorMin | 1 | Lowest envelope version the sender speaks. |
| ProtoMajorMax | 1 | Highest. |
| Capabilities | 8 | u64 bitset (§5.6). |
| SelfAssignedId | 2 | The id the sender wants to be addressed by in this session. |
| Result | 1 | Meaningful only in HelloAck (§5.2). Zero in Hello. |
| OfferCount | 2 | Number of offers. |
| Offers | 4 × OfferCount | §5.3. |
| Extensions | rest | TLV records (§5.4). |

A node sends a Hello to the endpoint it currently reaches a peer on, for each peer the mesh knows
and does not reach through a relay. The reply goes to the UDP source of the Hello. Both messages
say who is speaking, in the `Names` extension: a Hello names its sender, and a HelloAck names the
node that actually answered. That tells the asker whether the peer it asked for, or another node
sharing the endpoint, replied.

The envelope of both carries ProtoMajor `2`, the Internal flag and no other, and no guaranteed or
forwarded extension.

### 5.2 Result

| Value | Meaning |
|---|---|
| 0 | Accepted |
| 1 | No compatible ProtoMajor |
| 2 | Not admitted: no session was created |
| 3–255 | Unassigned |

A responder answers `0` when the asker's ProtoMajor range includes 2, and `1` when it does not.
Value `2` is reserved for a node that admits members only; release 1 never sends it.

A receiver MUST read any value other than `0` as **"no session now"**, whatever the reason: a later
edition may refuse for a reason this one does not know (too many sessions, not admitted), and that
says nothing about whether the sender speaks JFP2. The asker MUST NOT take such a peer for
legacy-only. It routes the peer through legacy meanwhile, as for any peer without a session, asks
again after the cooldown of §5.7, logs the value, and still reads the HelloAck's extensions.

### 5.3 Offers

An offer is 4 bytes: Partition u8 (`0` application, `1` internal), Class u8, MinVersion u8,
MaxVersion u8. It says: this node can encode and decode this class at every schema version from
Min to Max. A schema version is 1 to 255; `0` means "none".

An offer whose partition is neither `0` nor `1` belongs to a class space the receiver does not
know, and MUST be skipped; the offers after it are read as usual. Hello, HelloAck and
GuaranteedDone MUST NOT be offered.

### 5.4 Extensions (TLV)

The extension area is a sequence of records `(Tag u16, Length u16, Value)` after the offer list. It
lets the handshake grow without a new envelope version.

**Reading rules.**
- An unknown tag MUST be skipped by its length. A record whose length runs past the end of the
  datagram ends the area.
- A value shorter than its tag needs MUST be ignored.
- A value longer than its tag needs MUST be read up to the prefix the receiver knows, and the rest
  skipped. That is how a later edition extends a value.
- For a repeated tag the first copy counts. A list goes inside one value.
- Tags are appended and never reused.

**Registered tags**

| Tag | Name | In | Value |
|---|---|---|---|
| 1 | Names | Hello, HelloAck | The speaker's names (§4.3), 8 bytes each, preferred first. A reader takes whole names and skips a trailing remainder under 8 bytes. In a HelloAck, the names of the node that answered. REQUIRED: a handshake from which the receiver can resolve no name to a node it knows is ignored (§5.7). |
| 2 | Build | Hello, HelloAck | Text naming the speaker's build, for diagnostics and for counting which builds speak JFP2; `<version> <assembly name>`, for example `26.6.0 JoinFS-FS2024`. Printable ASCII (`0x20`–`0x7E`), at most 64 bytes: a sender cleans it to that, and a receiver cuts it to 64 bytes and drops every other character before using it. OPTIONAL. Nothing in the protocol depends on it. |
| 3 | ObservedEndPoint | HelloAck | A `WireEndPoint` (§4.4): the UDP source of the Hello being answered, as the responder received it (an IPv4-mapped address normalized). Every HelloAck carries it, refusals included. OPTIONAL for a reader. |

An asker MUST count an `ObservedEndPoint` only from a HelloAck that answers its own Hello, from the
endpoint the Hello went to (§5.7), so that a datagram from elsewhere cannot plant an address.

### 5.5 Agreed versions

For each (partition, class) offered by either side:

```
lo = max(local.Min, remote.Min)
hi = min(local.Max, remote.Max)
agreed = hi >= lo ? hi : 0
```

`0` means "do not send this class to this neighbour". The result is kept per session in two flat
256-entry tables, one per partition. Every Hello and HelloAck, keepalives included, resolves again
from scratch. When the result for a class changes (the peer restarted as another build), anything
cached on the old agreement MUST be dropped, so that a class the peer no longer takes goes back to
legacy rather than to a hop that would drop it.

### 5.6 Capabilities

The capability field is a u64 bitset for behaviour not tied to one class's schema. The agreed set is
the bitwise AND of both sides' sets. **No bit is assigned in release 1**, and a node advertises
none. A bit is assigned together with the design that needs it, and with any flag it defines
(§3.2).

### 5.7 Handshake procedure

**Asker.**
- Retry an unanswered Hello every **2 s**. After **5** attempts, mark the peer legacy-only and ask
  again after **30 s**, or at once if it sends a Hello itself.
- Keep one Hello outstanding per endpoint. Each Hello tells the node that answers which id to use
  for the asker, so two in flight to one endpoint would leave that node holding the wrong one.
- Match a HelloAck to the session by the id it is addressed to. It MUST name the node the Hello was
  for. If another node answers, that node owns the endpoint (§6.3) and the peer asked for is not
  there.
- A HelloAck with `Result` `0` and no resolvable name marks the peer legacy-only.
- On `Result` other than `0`: the session stops being verified, the answer's `Build` is remembered,
  and the peer is asked again after **30 s**, not sooner, even if it sends a Hello itself.
- A refusal counts only when it answers the asker's own Hello: it comes from the endpoint the Hello
  went to and its `Names` resolve to the peer asked. The id it is addressed to is 16 bits and could
  be guessed off the path, so any other refusal (from another source, naming another node or none)
  is logged and otherwise ignored. A node sharing the endpoint that refuses is not taken for the
  peer asked.

**Responder.**
- Ignore a Hello whose `Names` hold no name that resolves to a node the receiver knows from the
  membership layer. In release 1 membership comes from legacy, which always happens first, so a
  build that does not say who it is stays on legacy. With several resolvable names, the speaker is
  the first that is a known peer.
- Ignore unknown extension tags.
- Remember the speaker's `Build` for the session, and forget it when a later handshake omits it.
- A Hello refreshes the receiver's view of the sender's id. It lets the receiver *decode* what the
  sender sends, but does not make the session usable for *sending*: only the answer to the
  receiver's own Hello proves that its datagrams reach that node.
- A responder MUST NOT answer a Hello from a node it does not know, not even statelessly.

### 5.8 Stability of the handshake

Hello and HelloAck are how every release, past and future, starts talking to every other. From
release 1 on, they never change:
- the envelope: ProtoMajor `2` (a constant of the handshake that stays `2` even if the envelope's
  major version moves), the Internal flag and no other, classes `0` and `1`;
- the payload's fixed fields and the offer list layout of §5.1.

Anything new goes into the extension area (§5.4). A future major version is reached by negotiating
it inside a ProtoMajor-2 Hello: a node that speaks 2 and 3 offers ProtoMajorMin `2` and Max `3`.
A receiver of this edition accepts and answers in 2. The major version is agreed per session, like
the capabilities, not per class: every later datagram of the session uses it, except Hello and
HelloAck (keepalives included), which always travel in ProtoMajor 2.

---

## 6. Sessions and routing

### 6.1 Session binding

A session is with a neighbour. It is bound to the node name the peer stated in the handshake, and
never to the endpoint a datagram came from: two nodes can share one public endpoint (a hub and a
client behind one router that forwards a port to the hub, both known by the same public IP and
port), and a source address cannot tell them apart. A receiver MUST match a datagram to a session by
the two ids in the header. Ids are random, not sequential, so a datagram meant for another node
cannot match a session by coincidence.

### 6.2 Verification and keepalive

A session becomes usable for sending when the asker's own Hello is answered by the right node.
Afterwards a keepalive Hello goes to the verified endpoint every **5 s**; the answer MUST name the
same node. A session that has not been answered for **15 s**, whose endpoint is now answered by
another node (the network steers it elsewhere), or whose route moved, stops being verified, and the
peer falls back to legacy until it is verified again. A peer that restarts, or forgets its sessions,
is recovered by the next keepalive: a Hello always refreshes the receiver's view of the sender's id,
and the ack refreshes the asker's.

A keepalive is a full Hello, with the full offer list, deliberately: every Hello and HelloAck
resolves the agreement again (§5.5), so a peer that restarted as another build is noticed within one
keepalive.

A node whose replies are steered to another node cannot verify the path back, so that direction
stays on legacy.

### 6.3 Next hop

JFP2 datagrams for a peer go to the first of these that has a verified session:
1. the peer itself, if it answered the Hello at the endpoint it is currently reached on;
2. the relay the mesh routes the peer through;
3. another node that answered at the peer's own endpoint: the peer shares the endpoint and is
   behind that node.

The datagram is unaddressed when the hop is the peer itself, and Forwarded (§3.5) otherwise. If none
applies, legacy carries everything for the peer.

### 6.4 Peers without JFP2

A peer that never answers a Hello, or answers without a resolvable name, is legacy-only. One that
refuses a session (`Result` not `0`) has no session for now (§5.2). Everything to either goes
through legacy, unless a neighbour that does speak JFP2 carries its traffic (§6.3), in which case
that neighbour translates (§8.4). A node MUST NOT assume any JFP2 capability beyond what
negotiation established.

---

## 7. Guaranteed delivery

A guaranteed message is acknowledged and retransmitted. JFP2 has no other reliability model: no
ordering, no streams, no congestion control.

### 7.1 Sending

The sender retransmits every **2 s** until the message is acknowledged, and gives up after
**180 s**. Each attempt goes through the target's next hop as it is then (§6.3), so a route change,
or a session that is demoted and verified again, does not strand the message. While JFP2 has no
hop to the target that agreed the message's schema version, the message waits; it is never handed
to legacy.

### 7.2 Ids

A guaranteed id is unique per **(origin, final target)** within the 30 s duplicate window. One
message to several targets MAY share one id, as legacy broadcasts do.
- A node takes the ids of its own messages from one counter, which counts up from a start taken
  from the clock, so that a node that restarts does not reuse the ids its peers still remember.
- An id is never `0`. Inside a node, `0` means "no origin id".
- A relay that re-sends a message on its origin's behalf MUST keep the origin's id; it MUST NOT take
  one from its own counter. The final target deduplicates by the origin's ids, where one of the
  relay's could collide with one the origin used for another message.
- Pending segments are kept per (origin, final target, id, index), since a relay holds ids it did
  not choose.

### 7.3 Receiving and acknowledging

The receiver answers every guaranteed datagram, duplicates included, with an internal
`GuaranteedDone` (class 9) whose payload is the u16 id and the u8 index of the segment it
acknowledges: always 3 bytes. A shorter payload acknowledges nothing. The receiver delivers the
message only the first time: ids are remembered for **30 s** for duplicate suppression, keyed by
(origin, id), the origin being the true sender; reassembly is keyed the same way.

### 7.4 Acknowledgements through a relay

When the acknowledged datagram arrived relayed, the `GuaranteedDone` MUST itself be sent
Forwarded, with Origin = the acknowledging node and Target = the true sender, so that it travels
back through the relay end to end. A plain `GuaranteedDone` comes only from the neighbour that was
the final target of the sender's own message. Either way an ack names both ends, and the sender
clears exactly the pending segment of that (origin, final target, id, index): never another
target's copy, nor another origin's message with the same id.

A relay that translates (§8.4) also:
- deduplicates and reassembles per (origin, final target, id), so that one origin's message for two
  targets under one id reaches both;
- acknowledges upstream itself, with a Forwarded `GuaranteedDone` whose origin is the final target
  and whose target is the original sender;
- when re-sending in JFP2, keeps the origin's id and consumes the downstream ack, which names the
  origin and the target exactly; every other Forwarded ack it passes on.

### 7.5 Segments

The fields allow a message of up to 255 segments of 1,100 payload bytes. Release 1 sends none. A
receiver that gets a datagram with count above 1 MUST drop it and MUST NOT acknowledge it, so that
its sender does not take a message that was never delivered as delivered.

---

## 8. Relaying and translation

### 8.1 Origination

A node sends to a peer it does not reach directly through the neighbour that carries that peer's
traffic (§6.3), in the schema agreed with that neighbour, as a Forwarded datagram. It does not need
to know whether the final target speaks JFP2: the relay either forwards or translates.

### 8.2 Receiving a Forwarded datagram

The rule is identical on every node.
- **Target is one of my names:** consume it, attributing it to Origin and decoding it with the
  schema agreed with the neighbour it came from.
- **Otherwise relay it**, but only if Target is a *direct* neighbour (no relay involved in reaching
  it) and the relay budget (§8.5) allows it. That caps relaying at one hop.
- **A name it cannot resolve** (§4.3): the Origin of a datagram it consumes, or the Origin or Target
  of one it would relay. The datagram MUST be dropped, and a guaranteed one MUST NOT be
  acknowledged.

Origin and Target are both always present. A single field whose meaning flips by direction was
rejected: in neither role would it equal the receiver's own name, so a node could not tell "relay
further" from "consume".

### 8.3 Forwarding

If the target has a verified session with the relay and agreed the **same schema version** for the
class as the sender's hop used (or the datagram is internal), the relay forwards the bytes with the
two hop ids rewritten.

### 8.4 Translation

Otherwise the relay decodes the message and re-sends it in the target's own terms. This makes the
translation path a per-hop version adapter as well as a protocol bridge.
- **The target agreed a different version of the class:** the relay decodes with the sender's
  version and encodes with the target's. A Position goes out only after its object's Identity
  (§9.2), so a relay that forwards an Identity byte for byte also decodes it, and remembers that the
  target has it. A guaranteed message is acknowledged upstream in the target's name and re-sent
  under the origin's id; the target's ack, addressed to the origin, ends at the relay (§7.4).
- **The target does not speak JFP2 for that class:** the relay decodes the message into its
  canonical form and sends it with whichever protocol reaches the target, keeping the true sender.
  - Over legacy, the original sender goes in the header with the Forward flag set, which is what a
    legacy receiver expects from a relayed message.
  - Identity is not forwarded as a message of its own: it updates a per-object cache that the legacy
    encoding inlines into the next position.
  - A guaranteed message is delivered hop by hop. The relay acknowledges upstream in JFP2, with a
    Forwarded `GuaranteedDone` in the target's name, then sends a legacy guaranteed message
    downstream with legacy's own ids and consumes the legacy acknowledgement itself. An upstream
    retransmission is acknowledged again but not sent downstream a second time: the relay remembers
    the (origin, target, id) triples it translated.
- **Legacy to JFP2** needs no translation in release 1: every JFP2 node understands legacy, so the
  legacy relay carries the message unchanged.

### 8.5 Relay budget

A node relays for at most a configured number of distinct senders at a time, shared by every
protocol it speaks. The budget of a released node is **10**; a hub MAY set another. A datagram from
a sender beyond the budget MUST NOT be relayed.

---

## 9. Message formats

Each class has one layout per schema version. Release 1 defines version 1 of each application
class. Field names are the canonical ones of the application's message model
(`../reference/joinfs-architecture.md` §4).

### 9.1 Position (class 0, version 1)

Fixed **103 bytes**. The hot path: no strings, no allocation.

| Field | Type | Bytes |
|---|---|---|
| ObjectId | u32 (`0xFFFFFFFF` = shared cockpit: the recipient's own aircraft) | 4 |
| NetTime | f64, the sender's clock, seconds | 8 |
| Latitude, Longitude, Altitude | f64 × 3 | 24 |
| Pitch, Bank, Heading | f32 × 3 | 12 |
| Velocity XYZ, AngularVelocity XYZ, Acceleration XYZ | f32 × 9 | 36 |
| Rudder, Elevator, Aileron, BrakeLeft, BrakeRight | i16 × 5, fixed point | 10 |
| Elevation | f32 | 4 |
| StaticCgToGround | f32, feet | 4 |
| StateFlags | u8: OnGround 1, ElevationCorrection 2, UserControlled 4, Paused 8 | 1 |

With the fixed header that is 111 bytes per datagram, against about 204 for legacy's equivalent
message, which repeats the full identity on every tick.

### 9.2 Identity (class 1, version 1)

The slowly changing description of an object: model, livery, ICAO type and airline, registration,
flight number and class code. It is sent when it changes and as a **4 s** heartbeat per object and
peer, so that a peer that joined late, or lost a datagram, converges within a bounded window. A
sender MUST send Identity before an object's first Position to a neighbour that agreed the Identity
class, and MUST withhold Position until it can. (A neighbour that agreed Position but not Identity
gets Position alone.)

Layout: ObjectId u32, Flags u8 (IsAircraft 1, IsPlane 2, ClassCodeConfirmed 4), TypeRole u8, then
the strings Callsign, Model, Livery, IcaoType, IcaoAirline, Registration, FlightNumber, ClassCode,
Wtc.

### 9.3 VariableSync (class 2, version 1)

One message for what legacy carries as three (integer, float, string variables). Layout: ObjectId
u32, Count u8, then Count entries of Vuid u32, Kind u8 (0 Int32, 1 Float32, 2 String8) and a
value (i32, f32, or a string of at most 256 bytes). A sender splits an update into messages of at
most **1,000** payload bytes and at most 255 entries. An entry is at most 263 bytes, so a single
entry always fits. The owner of the object is the origin of the datagram; there is no owner field.

### 9.4 Other classes (version 1)

| Class | Layout |
|---|---|
| Event (3) | ObjectId u32, EventId u32, Data u32: 12 bytes |
| FlightPlan (4) | ObjectId u32, then the strings IcaoType, Departure, Destination, Rules, Route, Remarks, Alternate, Speed, Altitude, Callsign, Registration, IcaoAirline, FlightNumber. The owner is the origin. |
| Notes (5) | One comms note: Guid 16, strings Nickname, Callsign, then NoteId u32, Age f32, Channel u16, string Text. A bulk "all notes" reply is one Notes message per note. |
| Weather (6), WeatherReply (9) | String Metar |
| StatusRequest (8) | Flags u8 (HubEnabled 1, HubListRequested 2), Uuid u32: 5 bytes |
| Status (7) | Guid 16, string AppVersion, Users u16, AtcCount u16, string AtcAirport, AtcLevel u8, Planes, Helicopters, Boats, Vehicles u16 × 4, HubFlags u8 (HubEnabled 1, GlobalSession 2, PasswordRequired 4), strings Address, Name, About, Voip, NextEvent, Airport, ActivityCircle i32 |

Status always carries every field; no part is conditional.

### 9.5 Variable identification

A variable is identified by a 32-bit `vuid`, the same one legacy uses: a hash of the variable's name,
with `0` remapped to `1` and an alias table for renamed variables. Every node computes it from the
name, so no negotiation is needed.

### 9.6 Not carried by JFP2

Always legacy in release 1: object positions of non-aircraft objects, RemoveObject, ShowOnRadar,
PeerInfo, WeatherRequest, hub directory messages, comms requests and all mesh messages.

### 9.7 Field limits

Every string field has a byte limit (UTF-8 bytes, without the length prefix), so that every message
fits the 1,100-byte payload ceiling (§3.6) by construction. A sender cuts per §4.2.

| Class | Fixed bytes | String limits (bytes) | Largest payload |
|---|---|---|---|
| Position | 103 | — | 103 |
| Identity | 6 + 9 prefixes = 24 | Callsign 32, Model 256, Livery 256, IcaoType 8, IcaoAirline 8, Registration 32, FlightNumber 16, ClassCode 16, Wtc 8 | 656 |
| VariableSync | 5 + per entry 5 (+ 2 + string) | String8 value 256; messages at most 1,000 bytes | 1,000 |
| Event | 12 | — | 12 |
| FlightPlan | 4 + 13 prefixes = 30 | IcaoType 8, Departure 8, Destination 8, Rules 8, Route 512, Remarks 256, Alternate 8, Speed 16, Altitude 16, Callsign 32, Registration 32, IcaoAirline 8, FlightNumber 16 | 958 |
| Notes | 26 + 3 prefixes = 32 | Nickname 32, Callsign 32, Text 768 | 864 |
| Weather, WeatherReply | 2 | Metar 1,024 | 1,026 |
| StatusRequest | 5 | — | 5 |
| Status | 34 + 8 prefixes = 50 | AppVersion 32, AtcAirport 8, Address 128, Name 64, About 512, Voip 128, NextEvent 128, Airport 8 | 1,058 |

The largest, Status at 1,058 bytes, makes a 1,086-byte datagram with every header, 1,110 with the
room kept for per-hop protection. Handshakes are about 100 bytes (`Build` is at most 64). A later
schema version of a class MAY raise its limits once multi-segment delivery exists. A text cut to a
limit (a hub's About, a long note) can differ from what legacy peers receive for the same message,
since legacy has no such limits.

---

## 10. Extending the protocol

### 10.1 Compatibility rules

1. **Skip what declares its size; drop what changes the framing.** TLV records, offers of an unknown
   partition, names of an unknown kind and `WireEndPoint` values of an unknown family declare their
   own size, so a reader skips them. An unknown ProtoMajor or flag bit can change where everything
   after it lies, so such a datagram is dropped (§3.7).
2. **Never send what was not agreed.** A sender uses only classes, schema versions, capabilities and
   flags the receiver offered or agreed (§3.3, §3.2, §5.5).
3. **Append, never reuse.** Class numbers, tags, kinds, flag bits, Result values and capability
   bits are assigned once.
4. **Wire shapes never depend on build symbols.** A codec is selected only by the negotiated schema
   version. Simulator or platform build configurations never change a wire layout.
5. **No EOF-sensing.** A reader never decides what a field is by whether bytes remain.
6. **The handshake is permanent** (§5.8).

### 10.2 Registries

Every number space of the protocol. "Unassigned" values MUST NOT be sent, and are handled as the
cited rule says.

| Space | Size | Assigned | Unknown values |
|---|---|---|---|
| Flag bits (§3.2) | 8 bits | 0 Guaranteed, 1 Forwarded, 3 Internal; 2 Coalesced reserved | Drop the datagram |
| Internal classes (§3.3) | u8 | 0 Hello, 1 HelloAck, 9 GuaranteedDone | Not agreed: drop, do not acknowledge |
| Application classes (§3.3) | u8 | 0–9; 255 reserved as Extended | Not agreed: drop, do not acknowledge |
| Offer partitions (§5.3) | u8 | 0 application, 1 internal | Skip the offer |
| Handshake `Result` (§5.2) | u8 | 0, 1, 2 | Read as "no session now" |
| Handshake TLV tags (§5.4) | u16 | 1 Names, 2 Build, 3 ObservedEndPoint | Skip |
| Name kinds (§4.3) | u8 | 0 assigned; 1, 2, 255 reserved | Skip; unresolvable |
| `WireEndPoint` families (§4.4) | u8 | 4 IPv4, 6 IPv6 | Ignore the value |
| Capability bits (§5.6) | 64 | none | Not in the AND of both sides, so never agreed |
| ProtoMajor (§3.1) | u8 | 2 | Drop the datagram |

### 10.3 Adding a class or a schema version

At protocol level:
- A **new class** takes the next unused number of its partition and a codec for version 1. It
  becomes visible to a neighbour only when that neighbour offers it.
- A **new schema version** of a class is a new codec. A node offers `[min, new]`; a pair agrees it
  only when both speak it. A codec stays as long as some release may still agree its version.
- A change of header framing is a capability-gated flag, never a change to an existing flag.

How the reference implementation does this, and how to test version skew, is in `implementation.md`
§5.

---

## 11. Security considerations

### 11.1 What release 1 protects

Nothing. JFP2 carries no encryption, signing or replay protection, like legacy. Anyone who knows a
node's name can send a Hello claiming it, as with the legacy header's sender field. The defences
that exist limit the damage of off-path forgery, not of an on-path attacker:
- **Session ids.** To inject into a session an off-path attacker must guess the pair (SenderId,
  RecipientId), 32 bits. A HelloAck is matched on the recipient id alone, 16 bits, which is why an
  observed endpoint or a refusal counts only from the endpoint the Hello went to (§5.4, §5.7).
- **Refusals are guarded** the same way, because a refusal takes a working link off JFP2 for 30 s.
- **Names and sessions** are bound by the handshake, not by source endpoint (§6.1).
- **Unknown nodes get no answer** (§5.7), so a scanner cannot enumerate nodes by sending Hellos.

### 11.2 Amplification and resource use

The answer to a Hello goes to its UDP source, so a spoofed Hello that names a known node makes a node
send one HelloAck, about the size of the Hello, to the spoofed address: no amplification, and none
at all for unknown nodes, which get no answer (§5.7). A relay serves a bounded number of senders
(§8.5).

### 11.3 Room left for per-hop protection

Edition 1 builds none of this; the format leaves room. Protection is **per hop**: a relay decodes
to translate between versions and protocols (§8.4), so it must read every payload, and end-to-end
confidentiality does not fit JFP2's relaying. The reserved shape is flag bit 4 `Authenticated`
(sent only toward a neighbour that agreed the capability), an 8-byte per-session send counter after
the forwarded extension, and a 16-byte tag after the payload, over the whole datagram up to the
counter as sent on that hop. §3.6 keeps 24 bytes for it. Handshakes cannot carry the flag (§3.2)
and would be protected through a TLV. The design is in `design-future.md`.

---

## 12. Interoperability

### 12.1 Release 1 as the baseline

26.6 is the oldest JFP2 every later build must work with. A later edition MUST therefore keep every
rule marked as permanent in §5.8 and §10.1, and MUST treat the following as the behaviour of a
release-1 peer:
- it resolves kind-0 names only, drops unresolvable ones, and does not acknowledge them (§4.3);
- it reads a non-zero `Result` as "no session now" (§5.2);
- it ignores a Hello from a node it does not know (§5.7);
- it advertises no capability and offers only the application classes 0–9;
- it drops a datagram with a flag it does not know, and never acknowledges a segment of several
  (§3.2, §7.5);
- it sends a datagram of at most 1,200 bytes and cuts text to the limits of §9.7.

### 12.2 Pairs

| Pair | What happens |
|---|---|
| Legacy-only build ↔ any | Legacy only, unchanged. A legacy-only build drops every `0xFA` datagram. |
| Release 1 ↔ a later edition | The later edition names a node by kind 0 while it has a legacy id, so release 1 resolves every name it is sent. New flags go only to a neighbour that agreed their capability, which release 1 never advertises. Non-zero `Result` values mean "no session now" to release 1. A non-member's Hello is ignored by release 1. |
| A relay with one release-1 side and one later side | The forwarded layout is the same for every build, so the relay forwards byte for byte or translates, as today; it never rewrites names. A translating relay keeps the origin's guaranteed id whichever side is release 1. The one exception is a future fan-out (`design-future.md`). |

### 12.3 Coexistence with legacy

- A JFP2 relay between a JFP2 neighbour and a legacy neighbour translates (§8.4); the legacy
  receiver sees an ordinary relayed legacy message.
- Legacy is frozen. New fields, messages and capabilities go into JFP2, never into legacy's codecs.
- Falling back to legacy for a peer is always possible in release 1 (§2.4).

---

## Appendix A. Example datagrams

These are the exact bytes pinned by the golden tests (`JoinFS.Tests/Jfp2/HandshakeGoldenTests.cs`).
They are a frozen part of this specification.

**A.1 Hello** from 203.0.113.1:6112 (local 20), id `0x1234`, to a peer whose id it does not know yet;
offers Position and Status at [1, 1]; build "26.6.0 JoinFS-FS2024".

```
FA 02 08 34 12 00 00 00                   magic, ProtoMajor 2, Internal, sender 0x1234, recipient 0, class Hello
02 02                                     ProtoMajorMin, ProtoMajorMax
00 00 00 00 00 00 00 00                   Capabilities
34 12                                     SelfAssignedId
00                                        Result
02 00                                     OfferCount
00 00 01 01                               application Position [1, 1]
00 07 01 01                               application Status [1, 1]
01 00 08 00  00 01 71 00 CB E0 17 14      TLV Names: kind 0, legacy id 203.0.113.1, port 6112, local 20
02 00 14 00  32 36 2E 36 2E 30 20 ...     TLV Build: "26.6.0 JoinFS-FS2024" (20 bytes)
```

**A.2 HelloAck** from 198.51.100.2:6112 (local 2), id `0x5678`, offering Position at [1, 1], build
"26.6.0 JoinFS-CONSOLE", answering A.1. A.1's source was 203.0.113.1:6112, so the same HelloAck
ends with the observed endpoint:

```
FA 02 08 78 56 34 12 01                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x1234, class HelloAck
02 02                                     ProtoMajorMin, ProtoMajorMax
00 00 00 00 00 00 00 00                   Capabilities
78 56                                     SelfAssignedId
00                                        Result: accepted
01 00                                     OfferCount
00 00 01 01                               application Position [1, 1]
01 00 08 00  00 02 64 33 C6 E0 17 02      TLV Names: kind 0, legacy id 198.51.100.2, port 6112, local 2
02 00 15 00  32 36 2E 36 2E 30 20 ...     TLV Build: "26.6.0 JoinFS-CONSOLE" (21 bytes)
03 00 07 00  04 CB 00 71 01 E0 17         TLV ObservedEndPoint: family 4, 203.0.113.1, port 6112
```

---

## Appendix B. Constants

| Name | Value | Section |
|---|---|---|
| Magic | `0xFA` | 3.1 |
| ProtoMajor | 2 | 3.1 |
| Datagram ceiling | 1,200 bytes | 3.6 |
| Payload ceiling | 1,100 bytes | 3.6 |
| Hello retry | 2 s, 5 attempts, then 30 s cooldown | 5.7 |
| Keepalive Hello | 5 s | 6.2 |
| Session expiry without an answer | 15 s | 6.2 |
| Guaranteed retransmit / give up | 2 s / 180 s | 7.1 |
| Duplicate window | 30 s | 7.2 |
| Identity heartbeat | 4 s | 9.2 |
| VariableSync message size | 1,000 bytes, 255 entries | 9.3 |
| Relay budget | 10 senders (released nodes) | 8.5 |

---

## Appendix C. Change history

| Edition | Date | Change |
|---|---|---|
| 1 | 2026-10 | The wire of JoinFS 26.6: 8-byte names, per-origin guaranteed ids, field limits and the 1,200-byte ceiling, the observed endpoint, and defined handling of unknown values. The development history before this edition is `history/`. |

---

## References

- `../network-protocol.md`: the legacy wire protocol.
- `../reference/joinfs-architecture.md`: how the application uses the protocol.
- `goals.md`, `rationale.md`, `implementation.md`, `roadmap.md`: the other JFP2 documents.
- RFC 2119 and RFC 8174: requirements language.
