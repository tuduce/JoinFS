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
file format (`../../recording/jfs-format.md`), or anything above the wire (simulator access, model
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
| 2 | Join | Edition 2, §5.10. Offered. |
| 3 | JoinResult | Edition 2, §5.10. Offered. |
| 4 | MemberList | Edition 2, §5.10. Offered. |
| 5 | MemberAnnounce | Edition 2, §5.11. Offered. |
| 6 | MemberLeave | Edition 2, §5.11. Offered. |
| 7 | RouteQuery | Edition 2, §5.13. Offered. |
| 8 | RouteAnswer | Edition 2, §5.13. Offered. |
| 9 | GuaranteedDone | The acknowledgement of §7.3. |

Hello, HelloAck and GuaranteedDone are never offered (§5.3): every node speaks them, always at
version 1. Join, JoinResult, MemberList, MemberAnnounce, MemberLeave, RouteQuery and RouteAnswer are offered, in the
internal partition, like any other class: a node sends them only on a session whose neighbour offered them.

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
| 2 | Not admitted: no session was created. A node in a session answers a stranger's Hello with it and a cookie (§5.9) |
| 3–255 | Unassigned |

A responder answers `0` when the asker's ProtoMajor range includes 2, and `1` when it does not.
Value `2` is sent by a node that admits members only, to a stranger (§5.9); release 1 never sends it.

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
| 4 | Admission | Hello, HelloAck | Edition 2 (§5.9). In a Hello: empty, a stranger asking to be admitted; 16 bytes, the cookie it was given, echoed. In a HelloAck with `Result` 2: the 16-byte cookie. A value of 1 to 15 bytes is ignored; a longer one is read as its first 16 bytes. OPTIONAL. Release 1 skips the tag. |
| 5 | Candidates | Hello, HelloAck | Edition 2 (§5.14). The endpoints the speaker can be tried at, preferred first: a list of entries, each a length byte and a `WireEndPoint` (§4.4) of that length. At most 8 are read; an entry that cannot be read is skipped by its length. OPTIONAL. Release 1 skips the tag. |

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
- A responder MUST NOT answer a Hello from a node it does not know, not even statelessly, with one
  exception: a node in a session answers a Hello that carries an `Admission` extension, and only
  as §5.9 says (edition 2). A Hello without `Admission` from an unknown node is still ignored, so a
  release-1 node and a node outside a session stay silent to strangers.

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

### 5.9 Admission of a stranger (edition 2)

Until edition 2 a node could start a JFP2 conversation only with a node it already knew from
legacy (§5.7). A node that has never met another (a joiner and the node it joins, a client and a hub
from its list) needs a first exchange that works with no state at the answering end and cannot be
used to attack a third party. It is the exchange below; the Hello and HelloAck keep the layout of
§5.1 and the envelope of §5.8, and the new things are the extension `Admission` (§5.4) and the
`Result` value 2 (§5.2).

**Asking.** The **stranger** sends a Hello whose `Names` hold its own names (preferred first) and
whose `Admission` is empty. It also sends its offers and build as for any Hello, which makes the
Hello at least as large as the answer. Its `SelfAssignedId` is the id it will be addressed by if it
is admitted; that id is not yet a session id, and a receiver of the answer finds the exchange by it.

**Challenge.** A node that is in a session (a hub is one) and receives a Hello with an `Admission`
extension answers with a HelloAck that has:
- `Result` 2, no offers, `SelfAssignedId` 0, the envelope's sender id 0 and its recipient id the
  Hello's `SelfAssignedId`;
- `Names`: the answering node's names; `ObservedEndPoint`: the UDP source of the Hello;
- `Admission`: a **cookie** of 16 bytes, `HMAC-SHA256(secret, source address, source port, context)`
  cut to 16 bytes. The address is 4 bytes for IPv4 (an IPv4-mapped IPv6 address counts as IPv4) or 16
  for IPv6, the port is big-endian, and the context is the first four names of the Hello, 8 bytes
  each, as sent. The secret is random, changes every **60 s**, and the previous one is still
  accepted, so a cookie is valid for 60 to 120 s.

The challenge keeps no state, is never larger than the Hello it answers (a Hello smaller than the
answer is ignored, which removes any amplification), and is rate-limited by one token bucket for the
node (10 per second, a burst of 20). A node outside a session answers nothing, and neither does a
release-1 node, which does not know the tag and ignores the Hello (§5.7).

**Echo.** The stranger takes the cookie only from a HelloAck with `Result` 2 that arrives from the
endpoint its Hello went to, is addressed to its id, and carries both a 16-byte cookie and an
`ObservedEndPoint`; any other such answer is ignored. It sends the same Hello again with `Admission`
set to the cookie, and `SelfAssignedId` unchanged. The answer to an echo may be another challenge
(the secret rotated twice, or the cookie was forged): the stranger takes its new cookie, at most
three Hellos in all, and then gives up. An unanswered Hello is repeated every **2 s** within the same
three.

**Admission.** A Hello whose cookie validates for its source and its names creates a **non-member
session** with the stranger, named by the first name it stated, and is answered with an ordinary
HelloAck (`Result` 0, offers, `Names`, `ObservedEndPoint`). The cookie shows that the stranger
receives at the endpoint it sends from, so the session is verified at once at both ends: the answering
node from the cookie, the stranger from the answer. A retransmitted echo answers again and refreshes
the stranger's id and endpoint. A Hello that carries an `Admission` is a stranger's whatever names it
states, even a name the receiver holds as a member: a member's keepalive never carries one, and a node
that restarted holds no session, so it can only ask as a stranger (§5.11, Rejoining).

**A non-member session** has the id space and the negotiation of any session, and these limits:
- It is never a next hop, never carries a Forwarded datagram, and `CanCarry` is false for every
  message kind. The classes it carries are Join and JoinResult (§5.10), and MemberList to a node that
  joined; every other datagram on it is dropped. (The directory classes of a hub are a later part of
  edition 2.)
- At most **32** are open at once; a Hello that would open a 33rd is ignored.
- It ends **60 s** after it was opened, at both ends, unless a Join (§5.10) makes it the member
  session. A keepalive does not extend it.

### 5.10 Joining over JFP2 (edition 2)

Until edition 2 only legacy made a node a member (§5.7). A node that holds a non-member session
(§5.9) becomes a member with **Join**, and is answered with **JoinResult** and told of the members with
**MemberList**. All three are internal classes (partition 1) at version 1, offered in Hello and used only
on a session where the neighbour offered them. None is guaranteed: the joiner repeats Join, and the
admitting node answers every copy in full. They travel on the session between the two nodes, never
Forwarded.

**Join** (class 2), joiner to admitting node, on the non-member session. Payload, 33 bytes:

| Field | Size | Notes |
|---|---|---|
| Flags | 1 | Bit 0: `Proof` is present. Other bits are zero and ignored. |
| Proof | 32 | `HMAC-SHA256(key, data)` when bit 0 is set, else zeros. `key` is the session's password hash as 4 bytes little-endian; `data` is the ASCII text `JFP2 Join`, then the 16-byte cookie the session was admitted with (§5.9), then the 8-byte name the joiner stated first in its Hello. |

The password hash itself never travels. The proof is bound to the cookie and the name, so a captured Join
proves nothing in another session.

**Judging.** The admitting node honours a Join only from the endpoint its cookie proved, and only while it
is in a session that takes joiners; a node in no session, or whose session takes no joiners, ignores it, as
a legacy node ignores a legacy Join. Otherwise it answers JoinResult, in this order:
1. the session has a password and `Proof` is absent or not the proof the node computes: `PasswordRequired`;
2. the session requires a login (an email and password, which JFP2 does not carry): `LoginRequired`;
3. otherwise `Accepted`. A node that already has the joiner as a member, whatever name it joined by, is
   not added twice, and a device that holds as many nodes as the legacy limit (32) is ignored, as legacy
   ignores it.

**JoinResult** (class 3), admitting node to joiner. Payload, 6 bytes:

| Field | Size | Notes |
|---|---|---|
| Result | 1 | 0 `Accepted`, 1 `PasswordRequired`, 2 `LoginRequired`. Any other value is a refusal. |
| Suid | 4 | u32 little-endian: the session's id when accepted, else 0. |
| ListSegments | 1 | How many MemberList datagrams follow when accepted. |

**MemberList** (class 4), admitting node to a member that just joined: the members it knows other than the
joiner and itself, split into datagrams of at most 1,000 bytes of entries. The schema is version 1, or version 2
when both ends offered it (§5.14), which adds the member's candidates to each entry. Payload:

| Field | Size | Notes |
|---|---|---|
| Index | 1 | 0-based, below `ListSegments`. |
| EntryCount | 1 | Entries in this datagram. |
| Entries | | `EntryCount` of the entry below. |

An entry is `NameCount` u8 (1 to 4, the member's names, preferred first), that many names of 8 bytes
(§4.3), `EndPointLength` u8, and a `WireEndPoint` (§4.4) of that length: where the admitting node reaches
the member. A reader skips an entry whose first name is not valid or whose endpoint it cannot read (the
lengths tell where the next entry begins), and takes an entry cut short as the end of the list.

**The member session.** When the node accepts, it turns the non-member session into the member session
for the joiner: the same ids, the same endpoint, still verified, and the 60 s lifetime of §5.9 no longer
applies. The joiner does the same on reading an `Accepted` JoinResult. Both then hold the other as a
neighbour with a verified session (§6): the joiner is a **member**, and no legacy datagram is exchanged with it.
From then on the keepalive of §6.2 keeps the neighbour, and the members reached through it, in the
session: a Hello or HelloAck from it renews their expiry (30 s) as a Pulse does for a legacy member.

**Repeating.** The joiner repeats Join every **2 s**, at most **5** times, until it holds a JoinResult and the
`ListSegments` MemberList datagrams it announced; a refusal ends the attempt. A Join on a session that is
already a member session is answered again in full (JoinResult, then every MemberList), so a lost answer costs
one repeat. A joiner that hears nothing for **15 s** gives up. MemberList datagrams that arrive before the
JoinResult are kept until it comes.

**What the joiner learns.** Each listed member becomes a peer that is **reached through the admitting node**
(§6.3, item 2) until a direct link exists: the datagrams for it go to the admitting node, Forwarded
(§8). A member the joiner knows already, by any of its names, is left as it is. How the other members learn of
the joiner (MemberAnnounce) and of a departure (MemberLeave) is §5.11.

### 5.11 Members joining and leaving (edition 2)

A member reached through a neighbour (§5.10) is kept as long as that neighbour keeps answering (its
Hello or HelloAck renews the member, §5.10) and until a MemberLeave names it. Both classes are
internal, version 1, offered in Hello, unreliable and never Forwarded, and travel on the member
session between two neighbours. Neither is repeated: a lost one costs the member's expiry (30 s).

**MemberAnnounce** (class 5), from the node that admitted a member to every other neighbour of it
that is a member and offered the class. Payload: one member entry exactly as MemberList (§5.10)
has it, in the schema version agreed with the neighbour (§5.14): `NameCount` u8 (1 to 4), the names (8 bytes each), `EndPointLength` u8, a `WireEndPoint`. A
receiver that holds the member by none of its names adds it, reached through the sender (as a
MemberList entry is); one it holds is left as it is. An entry it cannot use (§5.10) is skipped. A node
announces a joiner only when it adds it: a repeated Join, or the Join of a member it still holds
(Rejoining below), is not announced.

**MemberLeave** (class 6), sent by a node that leaves the session, to every neighbour that is a member and
offered the class, naming itself by the first name it stated; and by a node that stops holding a
member, whether by expiry or by a MemberLeave, to every other such neighbour. Payload: one name, 8 bytes
(§4.3). A receiver acts only on a member it holds that is either its neighbour and the sender (it
leaves itself), or is reached through the sender; any other MemberLeave is ignored. It drops the member
as for an expiry, which makes it announce the departure to its own neighbours in turn; a member it no
longer holds is not announced twice.

**Rejoining.** A node that restarts within the 30 s expiry is still a member at its admitting node,
with a session whose ids it has forgotten. It asks as a stranger (§5.9), and the admitting node answers
it as one, whatever it holds. On a Join that is judged `Accepted` for a name it holds as a member, the
admitting node replaces the member session by the new one, takes the endpoint the cookie proved as the
member's, holds it as its neighbour (no longer reached through another node), renews its expiry, does
not announce it again, and answers in full (§5.10). A node it holds as a member by another name
(an alias) is the same member.

### 5.12 Choosing JFP2 or legacy to join (edition 2)

What a node does to join the node at an address is the joiner's policy and puts nothing on the
wire that §5.9 and §5.10 do not. It asks as a stranger first. If no HelloAck of any kind (a cookie
or an answer) arrives within **2 s** of the first Hello, or the JFP2 join fails for a reason other than
a refusal (the node takes no Join, or a Join gets no answer within the limits of §5.10), it sends the
legacy Join to the same address instead and forgets the JFP2 join; a refusal by JoinResult is final
and is never retried over legacy. It remembers the address as legacy-only for **1 hour**, and
joins it over legacy at once in that time. A node that speaks both protocols admits a legacy Join like
any other, as a member linked over legacy. A node released before edition 2 ignores the stranger's Hello (§5.9)
and so costs the 2 s once.

### 5.13 Members that speak another protocol, and finding a relay (edition 2)

A session can hold members that speak JFP2 only (they joined over §5.10) and members that speak legacy
only (a node released before edition 2, or one that fell back to legacy, §5.12). A node that has both
kinds of neighbour keeps each kind informed in that neighbour's own words, and carries the traffic
between them (§8.4). Nothing here puts a legacy message on a JFP2 link or the other way round.

**Telling each side.** A node that adds a member with no legacy link (a joiner it admitted, or a
member of a MemberList or MemberAnnounce) tells the neighbours it has a legacy link with by legacy's
AddNode, so that a released node knows the member exists; its datagrams for the member then arrive at
this node. A node that first hears directly of a member over legacy (a legacy joiner, which sends it
a Join or a Pulse) tells its JFP2 neighbours by MemberAnnounce (§5.11), as it tells its legacy
neighbours by AddNode. When a member with no legacy link leaves, expires or is told to be gone, the node
tells the legacy neighbours with a legacy Leave sent in the member's name (the Forward flag set and the
member's legacy id as sender), since the member cannot say it to them; a member with a legacy link that is
gone is told to the JFP2 neighbours by MemberLeave (§5.11). A member with no legacy id is left out
of every legacy message.

**Liveness.** A legacy neighbour keeps a member by the pulses it sends it. A member with no legacy link
cannot answer them, so a node that is a verified neighbour of the member (§6.2) answers a legacy
Pulse addressed to it itself, with a PulseResponse in the member's name (the Forward flag set, the member's legacy
id as sender), and does not carry the pulse on. The round trip it measures is the one to the relay.

**RouteQuery** (class 7) and **RouteAnswer** (class 8) are the JFP2 counterpart of legacy's Pathfinder
and PathfinderResponse. Both are internal, version 1, offered in Hello, unreliable, never Forwarded, and
travel on the member session between two neighbours. The payload of both is the same:

| Field | Size | Notes |
|---|---|---|
| Count | 1 | At most 100; a greater count is a datagram to ignore. |
| Names | 8 × Count | A name that is not valid is skipped. |

A node sends a RouteQuery to every neighbour that is a member, **every 5 s**, with the names of the members
that have no legacy link and that it has no direct link to, except those a neighbour confirmed (below) within
the last 10 s. A neighbour answers with a RouteAnswer naming those of them that it reaches directly itself
(no relay is involved in reaching them, whatever the protocol) and for which its relay budget (§8.5) has
room, using the names as they were asked; it answers nothing when there are none. A query never names the
asker, and an answer never names its sender.

A RouteAnswer from the neighbour a member is already reached through confirms that route. One from another
neighbour replaces the route (datagrams for the member go through that neighbour, and the member lives for
another 30 s, §5.10) when the member has no route that works: the neighbour it is reached through has not
confirmed it for **15 s** (counted from the time the member was first asked about, when it never did), or
the member was left without a route because the node it was reached through is gone. A node does not leave a
route that works for another that answers, so a route never flips between two neighbours that both reach the
member. One hop, as with Pathfinder: the member is reached through a neighbour of the asker or not at all.

### 5.14 Endpoint candidates and the path of a link (edition 2)

A node can be reached at more than one endpoint: an IPv4 address and a global IPv6 address, a public
IPv4 address and a LAN one. Legacy names a node by its IPv4 address alone; JFP2 lets a node say where else it
is, tries those places, and keeps the link on the best one that answers. Nothing here is carried by legacy,
which is never handed an IPv6 endpoint.

**Candidates.** The extension `Candidates` (§5.4, tag 5) lists the endpoints the speaker can be tried at,
preferred first: its global IPv6 addresses (2000::/3, stable addresses before temporary ones, at most 4), its public
IPv4 address, its LAN IPv4 address, each with the port of its socket. A node that has candidates states them in every
Hello and HelloAck it sends, keepalives included, so a change reaches its neighbours within a keepalive (§6.2); it
leaves them out of the challenge of §5.9, which keeps that answer small. A node that reads them takes what it read last
from that speaker as the speaker's list, and keeps only the entries another host could reach: not a port of 0, not an
IPv4 loopback, unspecified, multicast or link-local address, and an IPv6 address only in 2000::/3. A node that has none
states none; a reader then has only the endpoint the member was registered at.

**In member entries.** MemberList and MemberAnnounce (§5.10, §5.11) offer schema versions 1 and 2. Version 2 adds, after
the `WireEndPoint` of an entry, `CandidateCount` u8 and that many candidates, each a length byte and a `WireEndPoint`
(at most 8 are read; an entry cut short ends the list where it is cut). The members a joiner learns of come with the
places it can try them at, and the admitting node states those it was told in the member's own Hello or Join.

**Joining a node that has several addresses** (a host name with an A and an AAAA record). The joiner of §5.12 asks all of
the addresses as a stranger (§5.9): the IPv6 ones at once, the IPv4 ones **250 ms** later when there is an IPv6 one,
and the first to admit it is the one it joins; the conversations with the others end. A node with no IPv6 address of its
own asks the IPv4 ones only. The legacy Join of §5.12 goes to the IPv4 address, and a node with none has no fallback.

**Reaching a member directly.** The Hellos of §6.3 go to every candidate of the member and to the endpoint it was
registered at, the IPv6 ones first and the IPv4 ones 250 ms later when there is an IPv6 one; the first to answer is
the member's endpoint (the node named in the HelloAck is still the one asked for, §5.7). A node with no IPv6 address
of its own does not send to an IPv6 candidate, and an IPv4 candidate in a private, shared (100.64.0.0/10) or link-local
range is tried only by a node on the same /24, since it names a device of someone else's network. The endpoint a member
was registered at is always tried.

**A host with several IPv6 addresses** answers from its preferred one, not the one it was asked at (UDP has no
connection, and an interface can have a SLAAC and a DHCPv6 address). The stranger of §5.9 therefore takes the answer
to its first Hello from any address of the same /64 and the same port, goes on at that address (the echo, the Join and
the session are there), and the other addresses of the join are given up for it.

**Preference.** Between two nodes that both have IPv6, the link is verified on IPv6 whenever IPv6 answers, and IPv4 is
the fallback:
- A link on IPv4 sends a Hello to the member's IPv6 candidates every **60 s**, and moves to the first that answers.
- A link on IPv6 that has not been answered for **15 s** (§6.2) moves to IPv4, and the member's IPv6 is not used
  for **60 s**; the 60 s are counted from the move, and the probe above resumes after them.
- A HelloAck from an endpoint the link is not on moves it only from IPv4 to IPv6, when the member's IPv6 is not held off.
  Any other (the same node on the other family, a straggler of an earlier round) is read as a sign of life and moves
  nothing, so a link never flips between the two because datagrams happen to arrive on both.
- Only a member with no legacy link has candidates that matter. A member with a legacy link stays where legacy has it.

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

**Direct links (edition 2).** A member with no legacy link that is reached through a relay (§5.10, §5.13) is not
left there: its neighbour-to-be sends it a Hello at its own endpoint (the one its MemberList or MemberAnnounce entry
gave), up to 3 times, 2 s apart, and again a minute after the last of them, for as long as it is reached through a relay.
Each of the two does it when it learns of the other, which is what opens a home router to the other's datagrams.
The Hello is an ordinary one (§5.7, no `Admission`), answered by a node that holds the sender as a member. A
HelloAck that verifies the session at the member's own endpoint makes the member a direct neighbour: item 1
above applies, and the member is no longer reached through the relay. A route a RouteAnswer (§5.13) gives never
replaces a verified direct link. If the direct session stops being answered (§6.2) the member is reached through no
one until a neighbour answers a RouteQuery for it, then through that neighbour; it is probed again as above. A
member that has a legacy link keeps the route legacy gives it.

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
- **Legacy to JFP2.** In release 1 every JFP2 node understood legacy, so a legacy relay carried a
  message for any neighbour unchanged. A neighbour with no legacy link (§5.10, §5.13) is not sent a legacy
  datagram: when a legacy datagram addressed to one arrives from a legacy neighbour (the relay budget
  allowing it, and only when it was not itself relayed), the relay decodes the message into its canonical
  form and sends it over JFP2 as a Forwarded datagram, keeping the true sender as Origin. Pulse is answered
  by the relay itself (§5.13). A guaranteed message is acknowledged upstream by the relay in the neighbour's
  name, with a legacy `GuaranteedDone` whose sender is the neighbour's legacy id; the relay keeps the legacy id
  of the message as the id of the JFP2 message, and consumes the neighbour's Forwarded acknowledgement itself.
  Any other legacy mesh message for such a neighbour (Leave, AddNode) is dropped.

### 8.5 Relay budget

A node relays for at most a configured number of distinct senders at a time, shared by every
protocol it speaks. The budget of a released node is **10**; a hub MAY set another. A datagram from
a sender beyond the budget MUST NOT be relayed.

---

## 9. Message formats

Each class has one layout per schema version. Release 1 defines version 1 of each application
class. Field names are the canonical ones of the application's message model
(`../../architecture.md` §4).

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
| Internal classes (§3.3) | u8 | 0 Hello, 1 HelloAck, 2 Join, 3 JoinResult, 4 MemberList, 5 MemberAnnounce, 6 MemberLeave, 7 RouteQuery, 8 RouteAnswer, 9 GuaranteedDone | Not agreed: drop, do not acknowledge |
| Application classes (§3.3) | u8 | 0–9; 255 reserved as Extended | Not agreed: drop, do not acknowledge |
| Offer partitions (§5.3) | u8 | 0 application, 1 internal | Skip the offer |
| Handshake `Result` (§5.2) | u8 | 0, 1, 2 | Read as "no session now" |
| Handshake TLV tags (§5.4) | u16 | 1 Names, 2 Build, 3 ObservedEndPoint, 4 Admission, 5 Candidates | Skip |
| Schema versions of internal classes (§5.5) | u8 | 1 for each, and 2 for MemberList and MemberAnnounce (§5.14) | Not agreed: the lower common version, or none |
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
- **Unknown nodes get no answer** (§5.7), so a scanner cannot enumerate nodes by sending Hellos. The
  one exception is the cookie challenge of a node in a session (§5.9): it is no larger than the
  Hello, rate-limited, and keeps no state; a non-member session it leads to is limited in number and
  lifetime and carries no application class.

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

The three datagrams of the admission exchange of §5.9 (edition 2) are pinned by
`JoinFS.Tests/Jfp2/AdmissionGoldenTests.cs`, which sits next to the golden tests above and never changes
them. The cookie of the examples is `00 01 02 ... 0F`.

**A.3 Hello asking for admission:** A.1 with one more extension at the end, `Admission` empty.

```
FA 02 08 34 12 00 00 00 ... 02 00 14 00 ...  (A.1 up to and including the Build extension)
04 00 00 00                               TLV Admission: empty
```

**A.4 Hello echoing the cookie:** the same, with the cookie.

```
FA 02 08 34 12 00 00 00 ... 02 00 14 00 ...  (A.1 up to and including the Build extension)
04 00 10 00  00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F   TLV Admission: the cookie
```

**A.5 The challenge:** from 198.51.100.2:6112 (local 2) to A.3's sender, which came from 203.0.113.1:6112.

```
FA 02 08 00 00 34 12 01                   magic, ProtoMajor 2, Internal, sender 0, recipient 0x1234, class HelloAck
02 02                                     ProtoMajorMin, ProtoMajorMax
00 00 00 00 00 00 00 00                   Capabilities
00 00                                     SelfAssignedId
02                                        Result: not admitted
00 00                                     OfferCount
01 00 08 00  00 02 64 33 C6 E0 17 02      TLV Names: kind 0, legacy id 198.51.100.2, port 6112, local 2
03 00 07 00  04 CB 00 71 01 E0 17         TLV ObservedEndPoint: family 4, 203.0.113.1, port 6112
04 00 10 00  00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F   TLV Admission: the cookie
```

The five datagrams of the join of §5.10 and the members of §5.11 (edition 2), A.6 to A.10, are pinned by `JoinFS.Tests/Net/Jfp2JoinTests.cs`; the two of §5.13, A.11 and A.12, by `JoinFS.Tests/Net/Jfp2MixedSessionTests.cs`.

**A.6 Join** from the node of A.1 (id `0x1234`) to the node of A.2 (id `0x5678`), with no password:

```
FA 02 08 34 12 78 56 02                   magic, ProtoMajor 2, Internal, sender 0x1234, recipient 0x5678, class Join
00                                        Flags: no proof
00 00 00 00 00 00 00 00 ... (32 bytes)    Proof: zeros
```

**A.7 JoinResult** from the node of A.2 to the node of A.1: accepted, session `0x01020304`, one MemberList follows.

```
FA 02 08 78 56 34 12 03                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x1234, class JoinResult
00                                        Result: accepted
04 03 02 01                               Suid
01                                        ListSegments
```

**A.8 MemberList** from the node of A.2, listing one member, 203.0.113.1:6112 (local 20):

```
FA 02 08 78 56 34 12 04                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x1234, class MemberList
00 01                                     Index 0, one entry
01                                        NameCount
00 01 71 00 CB E0 17 14                   name: kind 0, legacy id 203.0.113.1, port 6112, local 20
07  04 CB 00 71 01 E0 17                  EndPointLength 7, WireEndPoint: family 4, 203.0.113.1, port 6112
```

**A.9 MemberAnnounce** from the node of A.2 (id `0x5678`) to a member (id `0x4321`), announcing the member of A.8:

```
FA 02 08 78 56 21 43 05                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x4321, class MemberAnnounce
01                                        NameCount
00 01 71 00 CB E0 17 14                   name: kind 0, legacy id 203.0.113.1, port 6112, local 20
07  04 CB 00 71 01 E0 17                  EndPointLength 7, WireEndPoint: family 4, 203.0.113.1, port 6112
```

**A.10 MemberLeave** from the same node to the same member, for the same member:

```
FA 02 08 78 56 21 43 06                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x4321, class MemberLeave
00 01 71 00 CB E0 17 14                   name: kind 0, legacy id 203.0.113.1, port 6112, local 20
```

**A.11 RouteQuery** from the node of A.1 (id `0x1234`) to the node of A.2 (id `0x5678`), asking for the member of A.8:

```
FA 02 08 34 12 78 56 07                   magic, ProtoMajor 2, Internal, sender 0x1234, recipient 0x5678, class RouteQuery
01                                        Count
00 01 71 00 CB E0 17 14                   name: kind 0, legacy id 203.0.113.1, port 6112, local 20
```

**A.12 RouteAnswer** from the node of A.2 to the node of A.1: it reaches that member directly.

```
FA 02 08 78 56 34 12 08                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x1234, class RouteAnswer
01                                        Count
00 01 71 00 CB E0 17 14                   name: kind 0, legacy id 203.0.113.1, port 6112, local 20
```

**A.13 Hello with Candidates** (edition 2): the Hello of A.1 with its candidates, an IPv6 address and the IPv4 address, appended:

```
05 00 1C 00                               TLV Candidates, 28 bytes
13 06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17    length 19: IPv6 2001:db8::1, port 6112
07 04 CB 00 71 01 E0 17                   length 7: IPv4 203.0.113.1, port 6112
```

**A.14 MemberAnnounce, version 2** (edition 2) from the node of A.2 to the member of A.9, for the member of A.8, with the same two candidates after its endpoint:

```
FA 02 08 78 56 21 43 05                   magic, ProtoMajor 2, Internal, sender 0x5678, recipient 0x4321, class MemberAnnounce
01                                        NameCount
00 01 71 00 CB E0 17 14                   name: kind 0, legacy id 203.0.113.1, port 6112, local 20
07  04 CB 00 71 01 E0 17                  EndPointLength 7, WireEndPoint: family 4, 203.0.113.1, port 6112
02                                        CandidateCount
13  06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17    length 19: IPv6 2001:db8::1, port 6112
07  04 CB 00 71 01 E0 17                  length 7: IPv4 203.0.113.1, port 6112
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
| Admission: Hellos of a stranger | 3 in all, every 2 s | 5.9 |
| Admission: cookie secret | rotated every 60 s, the previous one accepted | 5.9 |
| Admission: challenges answered | 10 per second, burst 20 | 5.9 |
| Non-member sessions | 32 at once, 60 s each | 5.9 |
| Join: repeat / give up | 2 s, 5 attempts / 15 s | 5.10 |
| Join over JFP2 before legacy: silence / memory of a legacy-only address | 2 s / 1 hour | 5.12 |
| MemberList entries per datagram | 1,000 bytes | 5.10 |
| Expiry of a member linked over JFP2 | 30 s, renewed by each Hello or HelloAck | 5.10 |
| RouteQuery interval / member not asked again after a confirmation / route given up | 5 s / 10 s / 15 s | 5.13 |
| Keepalive Hello | 5 s | 6.2 |
| Session expiry without an answer | 15 s | 6.2 |
| Guaranteed retransmit / give up | 2 s / 180 s | 7.1 |
| Duplicate window | 30 s | 7.2 |
| Identity heartbeat | 4 s | 9.2 |
| VariableSync message size | 1,000 bytes, 255 entries | 9.3 |
| Relay budget | 10 senders (released nodes) | 8.5 |
| IPv6 head start over IPv4 | 250 ms | 5.14 |
| Link on IPv4 tries IPv6 / IPv6 held off after silence | 60 s / 60 s | 5.14 |
| Candidates a reader keeps | 8 | 5.14 |

---

## Appendix C. Change history

| Edition | Date | Change |
|---|---|---|
| 1 | 2026-10 | The wire of JoinFS 26.6: 8-byte names, per-origin guaranteed ids, field limits and the 1,200-byte ceiling, the observed endpoint, and defined handling of unknown values. The development history before this edition is `history/`. |

---

## References

- `../network-protocol.md`: the legacy wire protocol.
- `../../architecture.md`: how the application uses the protocol.
- `goals.md`, `rationale.md`, `implementation.md`, `roadmap.md`: the other JFP2 documents.
- RFC 2119 and RFC 8174: requirements language.
