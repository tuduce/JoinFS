# The JoinFS Legacy Protocol

**Specification, reconstructed from the implementation.** It defines the wire format and the behaviour
that a node needs to interoperate with the JoinFS builds that speak the original protocol ("legacy").
The protocol was never specified before; every statement here was read from the code and, for byte
layouts, checked against the golden fixtures in `JoinFS.Tests/Legacy/Fixtures`. Appendix C lists the
files and the commit the document was checked against.

**Status.** The legacy wire is frozen. Released builds speak it, `LegacyPluginGoldenTests` pins it, and
new fields and messages go into JFP2 (`jfp2/protocol.md`), never here. Every current build still speaks
legacy next to JFP2, so this document describes a protocol that is in use, not a historical one.

## Abstract

Legacy carries the real-time state of simulated aircraft and the supporting traffic of a shared flight
(identity, variables, events, flight plans, notes, weather, hub status) between simulator clients and
hubs over a single IPv4 UDP socket. Nodes form a session: a group that exchanges datagrams peer to peer,
with a one-hop relay for nodes that cannot reach each other directly. A datagram has a fixed 21-byte
header that names the sender and the recipient by network address. A subset of messages is delivered
with an acknowledge-and-retransmit scheme. The protocol has no encryption, no authentication of senders
and no negotiation: each application message carries the sender's data version and the receiver
decides what to read.

## Contents

1. [Introduction](#1-introduction)
2. [Protocol overview](#2-protocol-overview)
3. [Datagram format](#3-datagram-format)
4. [Data types](#4-data-types)
5. [Receiving a datagram](#5-receiving-a-datagram)
6. [Guaranteed delivery](#6-guaranteed-delivery)
7. [Session membership and routing](#7-session-membership-and-routing)
8. [Message flows](#8-message-flows)
9. [Message formats](#9-message-formats)
10. [Periodic traffic and rates](#10-periodic-traffic-and-rates)
11. [Versioning and compatibility](#11-versioning-and-compatibility)
12. [Security considerations](#12-security-considerations)
13. [Quirks of the implementation](#13-quirks-of-the-implementation)
- [Appendix A. Example datagrams](#appendix-a-example-datagrams)
- [Appendix B. Constants](#appendix-b-constants)
- [Appendix C. Sources and verification](#appendix-c-sources-and-verification)

---

## 1. Introduction

### 1.1 Purpose and scope

This document specifies the legacy datagram format, how a node receives and relays datagrams, guaranteed
delivery, session membership (create, join, login, pulse, pathfinder, leave), the message flows between
nodes, and the byte layout of every message. It does not specify:

- JFP2 (`jfp2/protocol.md`);
- the link between the app and the X-Plane plugin, which reuses the 21-byte framing
  (`LegacyWire.cs`, `JoinFS-XP/Link.h`) but is a different conversation;
- what an application does with a message (simulator access, model matching, the UI);
- the recording file format (`../recording/jfs-format.md`).

### 1.2 Requirements language

The key words "MUST", "MUST NOT", "SHOULD", "SHOULD NOT" and "MAY" are used as in RFC 2119 and RFC 8174
when, and only when, they appear in capitals. Because the protocol is frozen, a MUST here means "a node
that does not do this does not interoperate with released builds", and is derived from what the reference
implementation does. Where the implementation is merely tolerant, the text says "the reference
implementation accepts", not MUST.

### 1.3 Terminology

| Term | Meaning |
|---|---|
| Node | One running JoinFS instance: a simulator client or a console (headless) instance. |
| Hub | A node with the hub setting on. It answers directory requests and usually relays. Protocol-wise it is an ordinary node (§2.1). |
| Session | A group of nodes that exchange traffic. Identified by a 32-bit **suid**. |
| Peer | A node as seen by another node in the same session. |
| Nuid | The 7-byte network identity of a node (§3.3). The code calls it `NodeId`. |
| Internal message | A session-management message (`Flags.Internal`): Join, Pulse, ... (§9.2). |
| Application message | Any other message (§9.3 to §9.11). |
| Guaranteed | Sent with the acknowledge-and-retransmit scheme of §6. |
| Reference implementation | `JoinFS/Net/Protocols/Legacy/` plus `JoinFS/Net/Core/MeshManager.cs`. |

### 1.4 Conventions

- All integers are little-endian. `byte`, `ushort`, `uint`, `short`, `int`, `long` have their .NET sizes
  (1, 2, 4, 2, 4, 8 bytes).
- All floating-point values are IEEE 754, little-endian: `float` is 4 bytes, `double` is 8 bytes.
- Offsets in tables are in bytes from the start of the unit they describe.
- "Unreliable" means sent once, never acknowledged. "Guaranteed" means §6.
- Names in `Code font` are the names used in the code, so the text can be searched for.

---

## 2. Protocol overview

### 2.1 Roles and topology

Every node runs the same protocol. There are no client and server roles on the wire: a hub is a node that
(a) answers `StatusRequest`, `UserListRequest` and `UserPositionsRequest` with its directory data (§8.8,
§8.9) and (b) is listed in other nodes' hub directories, which is how nodes find a session to join
(§8.8). Any node, hub or not, relays for others within its relay budget (§7.9). A session is created by
one node (`MeshManager.Create`) and grows by nodes joining any member (§7). Members aim at a full mesh:
each member sends to every other member directly. Members that cannot reach each other directly use one
relay hop (§7.9).

### 2.2 Transport

- UDP over IPv4. The socket is bound to `0.0.0.0`. The default port is **6112** (`Network.DEFAULT_PORT`);
  a node MAY use any port.
- One socket carries all traffic of a node, legacy and JFP2. The first byte of a datagram tells them
  apart: legacy datagrams start with `0x0B`, JFP2 datagrams with `0xFA` (§2.4).
- The reference implementation receives datagrams of up to 65,536 bytes. A legacy sender never produces
  more than 1,021 bytes for a guaranteed message segment (§6.2). Unreliable messages are not segmented and
  have no limit in the protocol; the variable, hub list and user position messages are chunked by count
  instead (§9.4, §9.8, §9.9).

### 2.3 Layers

```
 application messages     positions, variables, events, peer info, status, notes ...   (§9.3 - §9.11)
 ---------------------------------------------------------------------------------------
 internal messages        Join, Pulse, Pathfinder, AddNode, Leave ...                  (§9.2, §7)
 ---------------------------------------------------------------------------------------
 guaranteed delivery      segments, GuaranteedDone, resend every 2 s                   (§6)
 ---------------------------------------------------------------------------------------
 datagram                 21-byte header + payload                                      (§3)
 ---------------------------------------------------------------------------------------
 UDP / IPv4
```

### 2.4 Coexistence with JFP2

Legacy is the baseline: every node speaks it and it can carry every message to every node. JFP2 is
negotiated per neighbour and per message kind and replaces legacy for the kinds both sides agree on. A
legacy-only node never sees a JFP2 datagram: a node that has not negotiated JFP2 with a peer sends it
legacy. The membership messages (Join, Pulse, Pathfinder, AddNode, Leave, Login) and the directory
messages are carried by legacy only. The one thing legacy cannot carry is the canonical `Identity`
message, because legacy sends identity inline in each position (§9.3).

When a hub translates a JFP2 message for a legacy-only peer, it writes the original sender into the
`Sender` field and sets `Flags.Forward`, which is exactly the shape of a relayed datagram (§7.9); legacy
receivers need no special handling.

---

## 3. Datagram format

### 3.1 Header

Every legacy datagram starts with this 21-byte header (`LegacyWire`). The payload follows without any
length field: it runs to the end of the datagram.

| Offset | Size | Field | Description |
|---|---|---|---|
| 0 | 2 | `Version` | `short`, the constant `0x520B`. On the wire the bytes are `0B 52`. A datagram whose first two bytes differ is dropped. |
| 2 | 1 | `Flags` | §3.2. |
| 3 | 2 | `GuaranteedId` | `ushort`. Identifies the guaranteed message (§6). `0` when `Flags.Guaranteed` is clear. |
| 5 | 1 | `GuaranteedIndex` | `byte`. Zero-based segment index. `0` when not guaranteed. |
| 6 | 1 | `GuaranteedCount` | `byte`. Number of segments of the message. Senders write `1` for every datagram that is not a segment of a larger message, guaranteed or not. |
| 7 | 7 | `Sender` | Nuid of the node that originated the datagram (§3.3). |
| 14 | 7 | `Recipient` | Nuid of the node the datagram is for, or the null Nuid (`ip == 0`) for "whoever receives this". |
| 21 | ... | payload | §3.4 and §3.5. |

`Version` is a framing constant, not a protocol version (§11). A sender writes the header once and, for a
message that goes to several recipients, patches only the `Recipient` field for each one: the bytes of the
copies are identical otherwise, including `GuaranteedId`.

### 3.2 Flags

| Bit | Mask | Name | Meaning |
|---|---|---|---|
| 0 | `0x01` | `Internal` | Session-management message. The payload is §3.4, not §3.5. |
| 1 | `0x02` | `Guaranteed` | The datagram is a segment of a guaranteed message and is acknowledged (§6). |
| 2 | `0x04` | `Forward` | The datagram was originated by `Sender`, not by the node that sent it on the wire: it has been relayed (§7.9) or translated (§2.4). |

Other bits MUST be sent as zero. The reference receiver ignores them.

### 3.3 Node identifier (Nuid)

A node's identity is its network address. The Nuid is 7 bytes:

| Offset | Size | Field | Description |
|---|---|---|---|
| 0 | 4 | `ip` | `uint`. The node's public IPv4 address packed as `b0<<24 \| b1<<16 \| b2<<8 \| b3`, written little-endian. On the wire the address therefore appears **reversed**: 203.0.113.1 is `01 71 00 CB`. |
| 4 | 2 | `port` | `ushort`. The UDP port the node listens on. |
| 6 | 1 | `local` | `byte`. The last octet of the node's LAN address. |

- `ip == 0` is the null Nuid ("no node"). It is used as the recipient of unaddressed datagrams (replies
  and requests sent to a raw endpoint) and as "unknown".
- Two Nuids are equal when all three fields are equal. `SameDevice` compares `ip` and `local` only; it
  identifies several nodes behind one NAT address that run on the same LAN host (§7.5).
- `local` lets a node reach a peer that sits behind the same public address: if a peer's `ip` equals the
  node's own public address, the node addresses that peer at its own LAN address with the last octet
  replaced by the peer's `local`, port `port` (`LocalIdentity.MakeEndPoint`).
- The Nuid is whatever the sender writes. Nothing binds it to the datagram's source address (§12).
- A node learns its public address out of band (an HTTPS lookup of its address, `NetBootstrap`), not from
  the protocol. It cannot take part in a session before it knows it.

### 3.4 Payload of an internal message

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | `InternalId` (`short`, §9.1) |
| 2 | ... | message-specific fields (§9.2) |

Internal messages carry no data version.

### 3.5 Payload of an application message

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | `DataVersion` (`short`): the sender's data-format version. Current builds write `21008`. |
| 2 | 2 | `AppId` (`short`, §9.1) |
| 4 | ... | message-specific fields (§9.3 to §9.11) |

A receiver drops the datagram when `DataVersion < 10014`. It reads the fields it knows for the `AppId`;
bytes left over are ignored. It silently ignores an `AppId` it does not know (§11 has the rules for how
`DataVersion` gates fields).

### 3.6 Datagram size

The protocol sets no maximum size for unreliable datagrams. Guaranteed messages are cut into segments
whose payload is at most 1,000 bytes, so a segment is at most 1,021 bytes (§6.2). The unreliable messages
that can grow are chunked by entry count (§9.4, §9.8, §9.9); the others are as large as their strings.

---

## 4. Data types

The payload is written with .NET `BinaryWriter` and read with `BinaryReader`; the types below are what
that means on the wire.

| Type | Encoding |
|---|---|
| `bool` | 1 byte, `0` or `1`. A reader treats any non-zero value as true. |
| `string` | The UTF-8 byte count as a 7-bit variable-length integer (1 byte for 0 to 127, 2 bytes for 128 to 16,383, and so on, least-significant group first, high bit set on all but the last byte), then the UTF-8 bytes. There is no terminator and no maximum length in the protocol. |
| `Guid` | 16 bytes, .NET `Guid.ToByteArray()`: the first three fields little-endian, the remaining 8 bytes as is. `0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0` is `3C 2D 1E 0F 5A 4B 78 69 87 96 A5 B4 C3 D2 E1 F0`. |
| `Nuid` | 7 bytes, §3.3. |
| `KnownNode` | `Nuid` (7 bytes) then `Port` (`ushort`). `Port` is the UDP port at which the sender has seen that node (its `peer.EndPoint.Port`), which can differ from the Nuid's `port` when a NAT remapped it. |
| axis | `short` fixed point. Written as `(short)(input * 16384.0)`, which truncates toward zero. Read as `value * (1/16384)`. Control inputs lie in -1.0 to 1.0. |
| `NetHash` (32-bit) | The hash of §4.1. Used for session passwords, login passwords, user uuids and variable ids. |

### 4.1 The string hash

`NetHash.HashString` is a 32-bit hash over the string's UTF-16 code units (the construction of the
.NET Framework's `String.GetHashCode`). All arithmetic is `uint` and wraps.

```
hash1 = (5381 << 16) + 5381
hash2 = hash1
for i = 0; i < len; i += 2:
    hash1 = ((hash1 << 5) + hash1) ^ s[i]
    if i == len - 1: break
    hash2 = ((hash2 << 5) + hash2) ^ s[i + 1]
return hash1 + hash2 * 1566083941
```

`HashPassword(p)` is `0` for the empty string, otherwise `HashString(p)` with `0` replaced by `1`. A
password hash of `0` therefore means "no password". The hash is unsalted and not a cryptographic
function (§12).

---

## 5. Receiving a datagram

A node processes a datagram in this order. Each "drop" is silent: the datagram is discarded and nothing is
sent.

1. **Ban list.** Drop if the source IPv4 address is in the node's ban list. This is checked for both
   protocols before anything else.
2. **Protocol.** If the first byte is `0x0B`, the datagram belongs to legacy; otherwise another protocol
   gets it.
3. **Length.** Drop if shorter than 21 bytes.
4. **Version.** Drop if bytes 0 to 1 are not `0B 52`.
5. **Own datagram.** Drop if the node knows its own Nuid and `Sender` equals it.
6. **Not for this node.** If `Recipient` is non-null, differs from the node's Nuid, and the node knows its
   own Nuid, the datagram is for someone else (the node never processes it) and one of these happens:
   1. an internal `GuaranteedDone` of at least 26 bytes that acknowledges a message this node sent on
      `Recipient`'s behalf (§6.5) is consumed here;
   2. otherwise the datagram is relayed if the conditions of §7.9 hold;
   3. otherwise it is dropped.
7. **Guaranteed.** If `Flags.Guaranteed` is set, send a `GuaranteedDone` for the segment at once, then
   reassemble (§6.3). Stop here when the message is incomplete or was already delivered.
8. **Dispatch.** If `Flags.Internal` is set, read the `InternalId` and decode (§9.2). Otherwise read the
   `DataVersion`, drop if it is below 10014, read the `AppId` and decode (§9.3 and following).
9. **Errors.** An exception while decoding (a truncated message, for example) is logged and the datagram
   is discarded. The rest of its payload is not interpreted.

A node that does not yet know its own address (it has not found its public IP) treats every datagram as
addressed to itself: step 6 is skipped.

After decoding, the application drops these messages while the node is not in a session:
`AircraftPosition`, `ObjectPosition`, `SimEvent`, the three variable messages, `WeatherRequest`,
`WeatherReply`, `WeatherUpdate`, `SharedData`, `StatusRequest`, `UserListRequest` and
`UserPositionsRequest`. Everything else is processed outside a session (`Status`, `HubList`, `UserList2`,
`UserPositions`, `Online`, `UserNuidRequest`, `UserNuid`, `Notes`, `FlightPlan`, `RemoveObject` and the
comms requests, and all internal messages subject to their own checks in §7).

### 5.1 The reply address

The address a node uses for a reply (and as the address it records for a sender that just contacted it) is
chosen once per datagram:

- if the sender is a known peer that has answered this node (`SendEstablished`, §7.6), the peer's
  **route** (§7.8); otherwise
- the datagram's source address.

Replies to a raw endpoint (`Join`, `Status`, user lists) go to that address with a null `Recipient`.
Replies to a node (`PulseResponse`, `GuaranteedDone`) are addressed to its Nuid.

---

## 6. Guaranteed delivery

A message sent as guaranteed is delivered at least once or, after 180 seconds, abandoned. Which messages
are guaranteed is fixed per message type (§9.1); a sender does not choose.

### 6.1 Ids

Each guaranteed message gets one 16-bit id from a counter that starts at an implementation-chosen value
(the reference implementation seeds it from the low 16 bits of a high-resolution timestamp) and increases
by one per message, shared by all peers. It wraps around and then reuses ids, including `0`. A receiver
identifies a message by the pair (sender endpoint, id), so a message sent to several recipients (a
broadcast) has one id and the same bytes for each of them.

### 6.2 Sending

1. The datagram (header and payload) is cut into segments: the payload is split into pieces of at most
   **1,000 bytes**, and each piece gets its own copy of the 21-byte header.
2. If there is more than one segment, each header gets `GuaranteedIndex` (0 to count-1) and
   `GuaranteedCount`. A single segment keeps index 0 and count 1. A message can have at most 255 segments.
3. All segments are sent at once. Then every segment not yet acknowledged is sent again every **2 seconds**.
4. The sender stops when every segment is acknowledged, or when a resend round finds the message older than
   **180 seconds**; it then drops the message without telling anyone.

The resend repeats every unacknowledged segment, with no backoff and no use of the measured round-trip
time.

### 6.3 Receiving

For each guaranteed datagram addressed to it, a node:

1. Sends `GuaranteedDone` (§6.4) to the reply address (§5.1), always, including for a duplicate.
2. Finds the reassembly entry for (the sender's registered endpoint, `GuaranteedId`), or creates one with
   room for `GuaranteedCount` segments.
3. Returns without delivering if the entry is already complete (a duplicate).
4. For a one-segment message, marks the entry complete and delivers it. Otherwise stores the segment at its
   index if that slot is empty; when every slot is filled, it concatenates the payloads (under the header
   of segment 0), marks the entry complete and delivers the message once.

An entry, complete or not, is discarded **240 seconds** after its first segment. Until then it suppresses
duplicates. After that, a very late retransmission would be delivered again.

### 6.4 GuaranteedDone

An internal, unreliable message, addressed to the sender of the segment: `GuaranteedId` (`ushort`) and
`GuaranteedIndex` (`byte`) of the segment it acknowledges (§9.2). It acknowledges exactly one segment;
there is no cumulative acknowledgement.

### 6.5 Matching acknowledgements

A sender marks a segment acknowledged when it receives a `GuaranteedDone` whose id matches a pending
message of its own and whose sender is the message's recipient (a message with a null recipient accepts
an acknowledgement from anyone). A message that a hub sent on another node's behalf (translation, §2.4) is
matched by the original sender and the acknowledging node; the hub consumes that `GuaranteedDone` instead
of relaying it (§5, step 6.1).

### 6.6 Peers that leave

When a peer is removed from the session, the reference implementation drops the guaranteed messages still
pending for it (and unaddressed ones sent to its endpoint). Resends always go to the peer's *current* route
(§7.8), not to the address it had when the message was queued. The comments in `LegacyReliability.cs` say
that earlier releases resent to the address recorded at queue time and matched an acknowledgement by id
alone; the bytes on the wire are the same, only where and when they go differs (§13).

---

## 7. Session membership and routing

The rules in this section are implemented once, in `MeshManager`, for all protocols. They run on the
network thread.

### 7.1 State

A node keeps:

| State | Meaning |
|---|---|
| `suid` | The 32-bit id of the session it is in. `0` = not in a session, `1` = the global session. |
| `active` | The node has started to create or join. With `suid == 0` this is "connecting". |
| `passwordHash` | The session password hash (§4.1). `0` = none. |
| `Creator`, `LoginRequired` | The node created the session; the session requires a login. |
| `allowJoin` | The node accepts `Join` requests. |

and, for each peer, in insertion order:

| Peer state | Meaning |
|---|---|
| `EndPoint` | Where the peer is (its own address and port). |
| `RouteEndPoint`, `RouteVia` | Where datagrams for the peer are sent: `EndPoint`, or a relay's endpoint (and the relay's Nuid). |
| `Direct` | `RouteEndPoint.Address == EndPoint.Address`. |
| `ReceiveEstablished` | The node has received something from the peer. |
| `SendEstablished` | The peer has answered the node (its traffic reaches the peer). |
| `LowBandwidth` | The peer's last `Pulse` asked for reduced traffic. |
| `Rtt` | Round-trip time in seconds, from pulses. |
| `ExpireTime` | When the peer is dropped unless it answers again. |

### 7.2 Creating a session

`Create` sends nothing. A normal session gets `suid` = the current high-resolution timestamp truncated to
32 bits, incremented until it is above 1; the node is the `Creator`; it stores the password hash it is
given. The global session has `suid = 1` and no password. A node enters it by creating it and then sending a
`Join` to each hub that runs it (§8.1); all those nodes share `suid` 1.

### 7.3 Joining

A node joins by sending `Join` (guaranteed, null recipient) to the endpoint of any member. It remembers the
password hash it sent as its own `passwordHash` (so it will in turn require that password from nodes that
join through it), allows joins, and waits in the "connecting" state.

A member that receives `Join` acts only if it is in a session (`suid != 0`) and accepts joins. In order:

1. If the sender's device (`SameDevice`) already accounts for **32** nodes (this node, if it is on that
   device, plus the peers on it), ignore the request. No reply is sent.
2. If the member has a non-zero `passwordHash` and the request's hash differs, answer `JoinFail` with
   `PasswordRequired` (guaranteed, to the source endpoint).
3. Else if the session requires a login, answer `JoinFail` with `LoginRequired`.
4. Else answer `JoinReply` (guaranteed, to the source endpoint): the `suid` and the list of every peer for
   which `ReceiveEstablished` is true (not including the sender, not including the member itself); then
   register the sender (§7.5).

The joiner, on `JoinReply` and while not yet in a session, adopts the `suid`. If the reply's `suid`
equals its own, it registers every listed node (without having heard from it, `receive = false`) and then
the replier as a sender. It starts pulsing all of them (§7.6). A `JoinReply` for a different `suid` is
ignored.

`JoinFail` only records the result for the application (`ActiveJoinResult`); the node stays "connecting"
and does not retry by itself in the protocol.

### 7.4 Login

A session can require a login. A joiner then sends `Login` instead of `Join` (guaranteed, null recipient),
with its email address, a password hash and a `Verify` flag, and does not accept joins itself. Only the
node that created the session and requires logins answers:

1. The device limit of §7.3 step 1 applies.
2. An email address that cannot be parsed gets `LoginFail(InvalidAddress)`.
3. The credentials store (`password.txt` in the creator's documents folder; one line per user,
   `address|name|hash`, a two-field `address|hash` is also read) decides:
   - an address not in the store: `InvalidAddress`;
   - `Verify` clear and the stored hash is `0` (no password set yet): `VerifyPassword`;
   - `Verify` clear and the stored hash differs from the presented one: `InvalidPassword`;
   - `Verify` set: the presented hash **replaces** the stored one, whatever it was, and the login is
     accepted;
   - otherwise accepted.
4. A refusal is a guaranteed `LoginFail` with the `LoginResult`. An accepted login gets exactly the
   `JoinReply` and registration of §7.3 step 4.

The password hash check of §7.3 step 2 does not apply to `Login`. The current application creates sessions
with `loginRequired: false` (`Network.Create`), so no current build creates a session that asks for a
login; the messages are still answered if such a session exists.

### 7.5 Registering nodes

`RegisterNode(id, port, receive, direct)` is how a node enters the peer table:

- It is ignored if `id` is the null Nuid, the node itself, or if the node's own device plus the peers of
  that device already number 32.
- A new peer gets `EndPoint = MakeEndPoint(id, port)` (§3.3), route = endpoint, and
  `ExpireTime = now + 30 s`. When `receive` and `direct` are both true (the node just received a datagram
  that was not relayed), the endpoint is replaced by the datagram's source address and the `port` sent in
  the message, so a peer behind a NAT is followed to its mapped address and port.
- An existing peer that was just heard from directly has its endpoint address and port updated the same way.
- The first time a peer is **received** from (`receive` and not yet `ReceiveEstablished`), the node tells
  the application the peer joined, and broadcasts `AddNode` for it (guaranteed, to every peer, including
  the new one).

Two sources feed it. The sender of a datagram is registered (`RegisterSender`, `receive = true`) when a
`Join` or `Login` is accepted and on `JoinReply`, `Pulse`, `PulseResponse`, `Pathfinder` and
`PathfinderResponse`. The node lists of `JoinReply` and `AddNode` register the nodes they name
(`receive = false`: the peer exists but has not been heard from). No other message type registers a node.

### 7.6 Pulse and establishment

Once a second a node sends `Pulse` to **every** peer in its table, unreliable and addressed to the peer
(the message goes to the peer's route, so it is relayed when the route is a relay). A `Pulse` carries the
`suid`, the sender's high-resolution timestamp, and a flags byte whose bit 0 asks the receiver for reduced
traffic.

A node that receives a `Pulse` for the `suid` it is in:

1. records the peer's low-bandwidth flag;
2. registers the sender (§7.5);
3. answers `PulseResponse` (unreliable, addressed to the sender, to the reply address of §5.1) containing
   the timestamp unchanged.

On `PulseResponse` from a known peer, the node computes `Rtt = (now_timestamp - echoed_timestamp) /
frequency` (the frequency is local; it is not on the wire), registers the sender, and:

- if the peer was not `SendEstablished`, tells the application the peer is **established** (this is the
  moment the application sends its `SharedData`, §8.6);
- sets `SendEstablished` and `ExpireTime = now + 30 s`.

`SendEstablished` is set only by `PulseResponse` and by `PathfinderResponse` (§7.8). A `JoinReply` does not
set it. A received `Pulse` does not extend `ExpireTime`: a peer that sends pulses but never answers ours is
dropped after 30 seconds.

### 7.7 Expiry and leaving

Each tick the node drops every peer whose `ExpireTime` has passed. Other peers that were routed through the
dropped one (by `RouteVia`, or by sharing its route endpoint) lose their route: `SendEstablished` is
cleared and the route reset to their own endpoint, so §7.8 looks for a new one. A dropped peer's objects
and pending guaranteed messages are removed and the application is told it left.

A node leaves by broadcasting `Leave{suid}` (unreliable) and resetting everything: all peers, objects,
relay slots and its `suid`. A node that receives a `Leave` for its own `suid` removes the sender at its next
tick. A node that crashes sends nothing and is dropped by the others after 30 seconds.

### 7.8 Pathfinder

Every **5 seconds** a node runs a pathfinder round. It counts rounds; every 8th round is a *direct check*.
It builds the wanted list: the peers that are not `SendEstablished` and, in a direct check, also the peers
that are not `Direct`. It keeps at most **100** of them. If the list is empty, nothing is sent. Otherwise
it sends one `Pathfinder{suid, wanted}` (unreliable, null recipient) to the *own endpoint* (not the route)
of:

- in an ordinary round, every peer that is `SendEstablished` and `Direct`;
- in a direct check, every peer.

A node that receives a `Pathfinder` for its `suid`, not relayed (`Forward` clear), registers the sender and
builds a reachable list from the wanted Nuids:

- its own Nuid, if listed;
- another peer, if it is `SendEstablished` and `Direct` and the relay budget has room
  (`relayNodes.Count + reachable.Count < budget`, default 10).

If the list is not empty it answers `PathfinderResponse{suid, reachable}` (unreliable, null recipient) to
the reply address (§5.1).

A node that receives a `PathfinderResponse` for its `suid`, not relayed, registers the sender and, for each
Nuid in it that is a known peer:

- if it is the responder itself: the peer is reachable directly. Route = its own endpoint, `RouteVia`
  cleared, `SendEstablished` set and `ExpireTime` extended;
- otherwise, if the peer is not `SendEstablished`: route it through the responder. `RouteEndPoint` = the
  responder's endpoint, `RouteVia` = the responder, `SendEstablished` set, `ExpireTime` extended.

A peer that receives a `Pathfinder` in which it is itself listed answers with itself in a
`PathfinderResponse`, and the requester then makes the route to it direct. So in a direct check, a
`Pathfinder` sent straight to a peer that is currently reached through a relay turns the route direct as
soon as one datagram gets through (the outgoing datagram can also open a mapping in the sender's own NAT).

### 7.9 Relaying

A node forwards a datagram that is addressed to a third node when **all** of these hold:

1. `Recipient` is a known peer of this node and that peer is `Direct`;
2. `Flags.Forward` is clear (a datagram is relayed once: the usable depth is one intermediate node);
3. the node's own low-bandwidth setting is off;
4. the node has a relay slot for `Sender`.

Relaying sets `Flags.Forward` and sends the unchanged datagram to the recipient's **own** endpoint. The
node does not look at the content, does not acknowledge guaranteed segments (the final recipient does) and
does not change `Sender` or `Recipient`.

A relay slot is kept per distinct `Sender`. A slot is taken or refreshed each time a datagram of that sender
is relayed and expires **5 seconds** after the last one; a node relays for at most `RelayBudget` senders at a
time (default **10**, hubs can raise it) and refuses a datagram from an eleventh. A `Leave` from a sender
frees its slot.

A node that receives a datagram with `Forward` set treats the node in `Sender` as the sender but does *not*
learn its address from the datagram's source (the source is the relay): the registered endpoint of the
sender stays as it was.

---

## 8. Message flows

Notation: `X -> Y: Message` is a datagram. `[G]` marks a guaranteed message, whose acknowledgement
(`GuaranteedDone`, §6.4) is shown only the first time and is otherwise implied. A message with a null
`Recipient` is "to endpoint".

### 8.1 Joining a session

```
 Joiner J                                   Member H (in the session; peers B, C)
   | -- Join{pwHash} [G] ------------------------>|  H checks device limit, password, login
   |<-- GuaranteedDone(join) ---------------------|
   |<-- JoinReply{suid, [B, C]} [G] ---------------|  then H registers J (ReceiveEstablished)
   | -- GuaranteedDone(reply) ------------------->|
   |      J: adopts suid; registers B, C (not yet heard from); registers H
   |<-- AddNode{suid, J} [G] ----------------------|  H broadcasts it to B, C and J as well
   | -- Pulse{suid, t, flags} to H, B, C, every 1 s ...
   |<-- PulseResponse{t} from H ------------------|   J: H is established; J sends SharedData to H
   |      (B and C answer when their pulses reach J and J's reach them, see §8.3)
```

Datagram-for-datagram this is the fixture `mesh_join_accepted` (acknowledgement, `JoinReply`, two
`AddNode`), decoded in Appendix A.2. The global session is entered the same way, once per hub that runs it:
the node creates the session with `suid = 1` and sends a `Join` to each such hub (`Network.StartGlobalSession`).

### 8.2 A join that is refused

```
 J -- Join{wrongHash} [G] ------------------> H
 J <-- GuaranteedDone ------------------------ H
 J <-- JoinFail{PasswordRequired} [G] -------- H     (or LoginRequired)
 J -- GuaranteedDone -----------------------> H
```

`J` is not registered by `H`, and `H` sends nothing more. `J` remains "connecting" until the user acts or
the application's reconnect logic (§10.2) retries.

### 8.3 Convergence of three nodes

`C` joins through `A`; `B` is already in the session.

```
 C -> A : Join                                    A -> C : JoinReply{suid, [B]}
 A -> B : AddNode{C} [G]   (A also sends it to C)
 C: registers B (receive = false) and A (heard from)
 C -> A, B : Pulse        A -> C : PulseResponse      C: A is established
 B -> A, C : Pulse        (B knows C from AddNode)
 B receives C's Pulse: registers C as sent (first contact: B broadcasts AddNode{C} [G] too)
                       answers PulseResponse         C: B is established
 C receives B's Pulse: answers PulseResponse         B: C is established
```

After one second per hop, every pair has exchanged a `Pulse` and a `PulseResponse` and both sides see the
other as established. The redundant `AddNode` messages are harmless: a node that already has the peer
ignores them.

### 8.4 Reaching a node through a relay

`A` and `C` are in the session but cannot reach each other (for example each behind a NAT that does not
let the other's datagrams in), and both reach `B`.

```
 A -> C : Pulse                  (lost)
 A: C is not SendEstablished -> wanted = [C]
 A -> B : Pathfinder{suid, [C]}   (B is established and direct)
 B: C is established and direct, budget ok -> reachable = [C]
 B -> A : PathfinderResponse{suid, [C]}
 A: C is not established -> route C through B
        RouteEndPoint = B's endpoint, RouteVia = B, SendEstablished = true
 A -> B : Pulse{from A, to C}     (datagram to B, Recipient = C)
 B -> C : same datagram, Flags.Forward added, to C's own endpoint
 C: sender A via B; registers A, but keeps A's endpoint
 C -> B : PulseResponse{to A}     (reply address: B)
 B -> A : same datagram, Forward added
 A: PulseResponse from C: C is established (through B)
```

Every 8th round, `A` also sends a `Pathfinder` straight to `C`; if that gets through a NAT that now has a
mapping for `A`, `C` answers `PathfinderResponse{[C]}` and the route becomes direct.

### 8.5 Steady state

Between two established peers, per direction:

```
 every 1 s    Pulse                      ->   PulseResponse
 every 5 s    SharedData (PeerInfo)
 every 50 ms  AircraftPosition / ObjectPosition per object, to each peer   (§10.1)
 when needed  variables, SimEvent [G], RemoveObject [G], FlightPlan, WeatherUpdate, Notes [G]
```

### 8.6 After establishment

When a peer becomes established the application sends it `SharedData` at once (not waiting for the 5 s
timer) and, for the first three peers established after the node joined (and only if the comms window is
open), a session comms history request (§8.10).

### 8.7 A segmented guaranteed message

A `Notes` message of 2,300 bytes of payload is three segments sharing one id:

```
 S -> R : seg 0/3  id=17 (1000 B)         R -> S : GuaranteedDone{17, 0}
 S -> R : seg 1/3  id=17 (1000 B)  lost
 S -> R : seg 2/3  id=17 ( 300 B)         R -> S : GuaranteedDone{17, 2}
 ... 2 s later, only the unacknowledged segment is sent again ...
 S -> R : seg 1/3  id=17                  R -> S : GuaranteedDone{17, 1}
 R: all three present: concatenate, deliver one Notes message
```

### 8.8 Hub discovery and status

```
 N -> H : StatusRequest{hubEnabled, hubListRequested, uuid}     (N has H from the seed list,
 N <- H : Status{...}                                            hubs.dat, a HubList or a SharedData)
 N <- H : HubList{[hub...]}   (only if H is a hub and the request asked for it; 25 hubs per datagram)
 N -> each unknown hub in the list : StatusRequest{..., true}    (repeat)
```

The responder answers only while it is in a session. If the request says `HubEnabled`, the responder
treats the sender as a candidate hub and asks it for its own status. If the responder is a hub it records
`uuid -> Nuid, port` of the sender as an online user (§8.10).

A node polls hubs every 5 seconds: for its first three polls it asks every hub that is not known to be
online; after that it asks one hub per poll, in turn. The hub list is requested on every 8th poll. A hub
that answers `Status` with `HubEnabled` set is added to the hub list; one that answers with it clear is
removed. A hub not heard from for 300 seconds is offline and one not heard from for 7 days is forgotten.

### 8.9 Who is online (hub user feeds)

```
 N -> H : UserListRequest              (every 2 s round, while something shows hub users)
 N <- H : UserList2 x users            (one datagram per user)
 N -> H : UserPositionsRequest         (only if N publishes its own whazzup feed)
 N <- H : UserPositions x ceil(users/20)
```

A hub rebuilds its user list every 10 seconds from itself and its session peers. A user not refreshed for
300 seconds is expired by the receiver. `UserList2` carries identity and flight plan; `UserPositions`
refreshes only the positions.

### 8.10 Finding a user and chat history

Online directory (join a user by uuid):

```
 N -> every online hub, every 60 s : Online{uuid}               (hubs record uuid -> Nuid, port)
 N -> up to 5 online hubs          : UserNuidRequest{uuid} [G]
 N <- H                            : UserNuid{uuid, nuid, port} [G]     (if H knows it)
 N -> that node                    : Join
```

Chat:

```
 poster -> every peer : Notes{one comms note} [G]
 new node N -> peer P : CommsRequest(session) [G]     (first three established peers)
 N <- P               : Notes{session history} [G]    (to the request's endpoint)
```

---

## 9. Message formats

### 9.1 Registries

The numeric value of each id is its position in the enum (`LegacyWire.InternalId`, `LegacyWire.AppId`):
values are implicit, so the order IS the protocol. New ids were only ever appended.

**Internal ids** (payload of §3.4):

| Id | Name | Guaranteed | Section |
|---|---|---|---|
| 0 | `Join` | yes | §9.2 |
| 1 | `JoinReply` | yes | §9.2 |
| 2 | `AddNode` | yes | §9.2 |
| 3 | `Leave` | no | §9.2 |
| 4 | `Pulse` | no | §9.2 |
| 5 | `PulseResponse` | no | §9.2 |
| 6 | `GuaranteedDone` | no | §9.2 |
| 7 | `AddNodes` | - | reserved: no sender, no receiver |
| 8 | `Pathfinder` | no | §9.2 |
| 9 | `PathfinderResponse` | no | §9.2 |
| 10 | `JoinFail` | yes | §9.2 |
| 11 | `Login` | yes | §9.2 |
| 12 | `LoginFail` | yes | §9.2 |

**Application ids** (payload of §3.5). "Sent" and "Handled" describe the current builds.

| Id | Name | Guaranteed | Sent | Handled | Section |
|---|---|---|---|---|---|
| 0 | `ObjectPosition` | no | yes | yes | §9.3 |
| 1 | `AircraftPosition` | no | yes | yes | §9.3 |
| 2-9 | `PlaneState`, `HelicopterState`, `AircraftState`, `PistonEngineState`, `TurbineEngineState`, `AircraftFuel`, `AircraftPayload`, `ObjectSmoke` | - | no | no | §9.12 |
| 10 | `WeatherRequest` | yes | no | yes | §9.7 |
| 11 | `WeatherReply` | yes | yes | yes | §9.7 |
| 12 | `WeatherUpdate` | no | yes | yes | §9.7 |
| 13 | `SharedData` | no | yes | yes | §9.6 |
| 14 | `StatusRequest` | no | yes | yes | §9.8 |
| 15 | `Status` | no | yes | yes | §9.8 |
| 16 | `HubList` | no | yes | yes | §9.8 |
| 17 | `RemoveObject` | yes | yes | yes | §9.5 |
| 18 | `UserListRequest` | no | yes | yes | §9.9 |
| 19 | `UserList` | - | no | yes (read only) | §9.9 |
| 20 | `UsageLog` | - | no | no | §9.12 |
| 21 | `SimEvent` | yes | yes | yes | §9.5 |
| 22 | `KeyLog` | - | no | no | §9.12 |
| 23 | `Shutdown` | - | no | no | §9.12 |
| 24 | `SessionCommsRequest` | yes | yes | yes | §9.11 |
| 25 | `GlobalCommsRequest` | yes | no | decoded, ignored | §9.11 |
| 26 | `CommsListenRequest` | yes | no | decoded, ignored | §9.11 |
| 27 | `AllNotesRequest` | - | no | no | §9.12 |
| 28 | `Notes` | yes | yes | yes | §9.11 |
| 29 | `UserNuidRequest` | yes | yes | yes | §9.10 |
| 30 | `UserNuid` | yes | yes | yes | §9.10 |
| 31 | `Online` | no | yes | yes | §9.10 |
| 32 | `FlightPlanRequest` | - | no | no | §9.12 |
| 33 | `FlightPlan` | no | yes | yes | §9.5 |
| 34 | `UserList2` | no | yes | yes | §9.9 |
| 35 | `UserPositionsRequest` | no | yes | yes | §9.9 |
| 36 | `UserPositions` | no | yes | yes | §9.9 |
| 37 | `IntegerVariables` | no | yes | yes | §9.4 |
| 38 | `FloatVariables` | no | yes | yes | §9.4 |
| 39 | `String8Variables` | no | yes | yes | §9.4 |
| 40 | `ShowOnRadar` | yes | no | decoded, ignored | §9.5 |

"Guaranteed" is what the encoder in `LegacyPlugin` does; for a message the current code has no encoder for,
the column is "-". A receiver does not care whether a segment is guaranteed: it acts on the flag.

In the layouts that follow, "Fields" lists the bytes after the 4-byte application prefix (§3.5) or after
the 2-byte internal id (§3.4). A type such as `string` is §4.

### 9.2 Internal messages

**Join** (0): `PasswordHash` `uint`. A receiver that finds fewer than four bytes uses `0`.

**JoinReply** (1)

| Field | Type |
|---|---|
| `Suid` | `uint` |
| `Count` | `ushort` (read as signed `short`) |
| nodes | `Count` x `KnownNode` (9 bytes: `Nuid`, `Port`) |

**AddNode** (2): `Suid` `uint`, `Node` `Nuid`, `Port` `ushort`.

**Leave** (3): `Suid` `uint`.

**Pulse** (4)

| Field | Type |
|---|---|
| `Suid` | `uint` |
| `Time` | `long`: the sender's high-resolution timestamp (opaque to the receiver) |
| `Flags` | `byte`: bit 0 (`0x01`) = the sender asks for reduced traffic |

**PulseResponse** (5): `Time` `long`, copied from the `Pulse`.

**GuaranteedDone** (6): `GuaranteedId` `ushort`, `GuaranteedIndex` `byte`. Always sent with `Flags` =
`Internal` only, count 1, addressed to the sender of the acknowledged segment. A relay recognises it by its
length of at least 26 bytes.

**Pathfinder** (8) and **PathfinderResponse** (9)

| Field | Type |
|---|---|
| `Suid` | `uint` |
| `Count` | `ushort` (read as signed `short`) |
| nodes | `Count` x `Nuid` (7 bytes) |

A sender writes at most 100 nodes in a `Pathfinder`.

**JoinFail** (10): `Result` `byte`: 0 `Accepted`, 1 `PasswordRequired`, 2 `LoginRequired`.

**Login** (11)

| Field | Type |
|---|---|
| `Email` | `string` |
| `PasswordHash` | `uint` |
| `Verify` | `bool` |

A receiver uses `""`, `0` and `false` for fields that are missing or unreadable.

**LoginFail** (12): `Result` `byte`: 0 `Accepted`, 1 `InvalidAddress`, 2 `VerifyPassword`, 3
`InvalidPassword`.

### 9.3 Object state: AircraftPosition and ObjectPosition

Both are unreliable and carry the object's identity inline (the receiver splits each into an identity and a
position). `NetId` is the sender's id for the object; `0xFFFFFFFF` is reserved for "the sender's own
aircraft, for shared cockpit" (the receiver then treats the data as controlling its own aircraft and reads
no identity tail).

**AircraftPosition** (1)

| Field | Type | Notes |
|---|---|---|
| `NetId` | `uint` | |
| `User` | `bool` | the aircraft is flown by a human |
| `IsPlane` | `bool` | |
| `Callsign` | `string` | |
| `Model` | `string` | |
| `TypeRole` | `byte` | the object's type/role code used for model matching |
| `Flags` | `byte` | bit 0 = paused |
| `NetTime` | `double` | the sender's time of the sample |
| `Latitude`, `Longitude`, `Altitude` | `double` x 3 | |
| `Pitch`, `Bank`, `Heading` | `float` x 3 | |
| `VelocityX/Y/Z` | `float` x 3 | |
| `AngularVelocityX/Y/Z` | `float` x 3 | |
| `AccelerationX/Y/Z` | `float` x 3 | |
| `Rudder`, `Elevator`, `Aileron`, `BrakeLeft`, `BrakeRight` | axis x 5 | |
| `Elevation` | `float` | read only if `DataVersion >= 10023`, else 0 |
| `GroundFlags` | `byte` | read only if `DataVersion >= 10023`: bit 0 = on ground, bit 1 = the sender applies elevation correction |
| `Livery` | `string` | optional, see below |
| `IcaoType` | `string` | optional |
| `IcaoAirline` | `string` | optional |
| `Registration` | `string` | optional |
| `FlightNumber` | `string` | optional |
| `ClassCode` | `string` | optional |
| `Wtc` | `string` | optional |
| `ClassCodeConfirmed` | `bool` | optional |
| `StaticCgToGround` | `float` | optional |

The fields from `Livery` to `StaticCgToGround` form a tail that was extended over time. The receiver reads
them in this order and stops at the end of the datagram: a missing string is `""`, a missing
`ClassCodeConfirmed` is false, a missing `StaticCgToGround` is NaN (distinguishable from a real 0.0). The
tail is read only when `NetId != 0xFFFFFFFF`. A sender MUST write the tail in this order and MUST NOT leave
a gap: a field can be omitted only together with every field after it. The reader is driven by "any bytes
left", not by `DataVersion`.

**ObjectPosition** (0): a non-aircraft object (boat, vehicle, ...).

| Field | Type | Notes |
|---|---|---|
| `NetId` | `uint` | |
| `Model` | `string` | |
| `TypeRole` | `byte` | |
| `Flags` | `byte` | bit 0 = paused |
| `NetTime` | `double` | |
| `Latitude`, `Longitude`, `Altitude` | `double` x 3 | |
| `Pitch`, `Bank`, `Heading` | `float` x 3 | |
| `Velocity`, `AngularVelocity`, `Acceleration` | `float` x 9 | X, Y, Z of each |
| `Height` | `float` | read only if `DataVersion >= 10023` |
| `GroundFlags` | `byte` | read only if `DataVersion >= 10023`; same bits as above |
| `Livery`, `IcaoType`, `IcaoAirline`, `ClassCode`, `Wtc` | `string` x 5 | optional tail |
| `ClassCodeConfirmed` | `bool` | optional |

### 9.4 Variables

`IntegerVariables` (37), `FloatVariables` (38) and `String8Variables` (39) share a layout and differ in the
value type: `int` (4 bytes), `float` (4 bytes), `string`.

| Field | Type | Notes |
|---|---|---|
| `Owner` | `Nuid` | the sender's own Nuid (see below) |
| `NetId` | `uint` | `0xFFFFFFFF` = the sender's variables for the receiver's own aircraft (shared cockpit) |
| `Count` | `ushort` | |
| entries | `Count` x (`Vuid` `uint`, value) | `Vuid` identifies the variable: `HashString` (§4.1) of its name, such as `"transponder code:1"`, with 0 replaced by 1 (`VariableMgr.CreateVuid`) |
| `ChangeTimes` | `ushort` | always written as 0, ignored by the reader |

A sender splits a larger set into several messages of at most **100** (integer, float) or **80** (string)
entries; each carries the same `Owner` and `NetId`. Current builds write the origin of the message in
`Owner`, which is the header `Sender`; the receiver uses the header `Sender` and only logs a mismatch.

### 9.5 Events, removal, flight plan, radar

**SimEvent** (21), guaranteed: `NetId` `uint`, `EventId` `uint`, `Data` `uint`. A simulator event on one of
the sender's objects; `NetId = 0xFFFFFFFF` addresses the receiver's own aircraft (shared cockpit). The protocol does
not interpret `EventId` and `Data`.

**RemoveObject** (17), guaranteed: `NetId` `uint`. The sender stopped showing the object.

**FlightPlan** (33), unreliable, broadcast

| Field | Type | Notes |
|---|---|---|
| `Owner` | `Nuid` | the node that owns the aircraft |
| `NetId` | `uint` | |
| `Version` | `byte` | always 1; no reader branches on it |
| `IcaoType`, `Departure`, `Destination`, `Rules`, `Route`, `Remarks` | `string` x 6 | |
| `Alternate`, `Speed`, `Altitude`, `Callsign` | `string` x 4 | read only if `DataVersion >= 21003`, else `""` |
| `Registration`, `IcaoAirline`, `FlightNumber` | `string` x 3 | read only if `DataVersion >= 21006`, else `""` |

**ShowOnRadar** (40), guaranteed: `Owner` `Nuid`, `NetId` `uint`, `Show` `bool`. Decoded and ignored by the
current application; nothing sends it.

### 9.6 SharedData (peer information)

`SharedData` (13), unreliable, sent to each peer individually (canonical name `PeerInfo`). It says who the
sender is.

| Field | Type | Notes |
|---|---|---|
| `ShareFlags` | `byte` | bit 0 = the sender shares its cockpit with the recipient; bits 1, 2, 3 = the recipient currently holds the sender's flight, ancillary, navigation controls |
| `Nickname` | `string` | |
| `Guid` | `Guid` | the installation's identity |
| `Flags` | `byte` | bit 0 = hub, bit 1 = ATC, bit 2 = simulator connected |
| `AtcAirport` | `string` | |
| `AtcLevel` | `byte` | |
| `AtcFrequency` | `ushort` | |
| `ActivityCircle` | `byte` | |
| `Version` | `string` | application version; read only if `DataVersion >= 10019`, else `""` |
| `Simulator` | `string` | simulator name; read only if `DataVersion >= 10019`, else `""` |

Bit 2 of `Flags` is meaningful only if `DataVersion >= 10024`; a reader of an older sender assumes the
simulator is connected.

### 9.7 Weather

`WeatherRequest` (10), guaranteed: `NetId` `uint` (optional, a missing value is 0). `WeatherReply` (11),
guaranteed, to the requester: `Metar` `string`. `WeatherUpdate` (12), unreliable, broadcast: `Metar`
`string`, the weather at the sender's aircraft. A reader ignores an empty METAR. No current build sends a
`WeatherRequest`; one that receives it answers with a `WeatherReply` if it has weather.

### 9.8 Status and hub list

**StatusRequest** (14), unreliable

| Field | Type | Notes |
|---|---|---|
| `HubEnabled` | `byte` | the sender is a hub; the receiver may add it to its hub list |
| `HubListRequested` | `byte` | send me the hubs you know |
| `Uuid` | `uint` | the sender's user uuid; optional (older peers do not send it) |

**Status** (15), unreliable, the reply to a `StatusRequest`

| Field | Type | Notes |
|---|---|---|
| `Guid` | `Guid` | |
| `AppVersion` | `string` | |
| `Users` | `ushort` | users in the sender's session |
| `AtcCount` | `ushort` | |
| `AtcAirport` | `string` | only if `AtcCount > 0` |
| `AtcLevel` | `byte` | only if `AtcCount > 0` |
| `Planes`, `Helicopters`, `Boats`, `Vehicles` | `ushort` x 4 | object counts |
| `HubEnabled` | `bool` | |
| `Address`, `Name`, `About`, `Voip`, `NextEvent`, `Airport` | `string` x 6 | only if `HubEnabled`; the reader trims spaces |
| `ActivityCircle` | `int` | only if `HubEnabled` |
| `HubFlags` | `byte` | only if `HubEnabled` and `DataVersion >= 10025`: bit 1 = global session, bit 2 = password required |

An empty `Address` is replaced by the source address of the datagram.

**HubList** (16), unreliable: `Count` `ushort`, then `Count` x `KnownNode`. A sender sends at most **25**
hubs per datagram, several datagrams for more, and sends nothing for an empty list.

### 9.9 Hub user feeds

**UserListRequest** (18) and **UserPositionsRequest** (35): no fields.

**UserList2** (34), unreliable, one user per datagram

| Field | Type | Notes |
|---|---|---|
| `Guid` | `Guid` | |
| `Flags` | `byte` | bit 0 = ATC, bit 1 = IFR |
| `Callsign`, `Nickname` | `string` x 2 | |
| `Frequency` | `ushort` | |
| `Latitude`, `Longitude` | `float` x 2 | |
| `Altitude`, `Speed` | `ushort` x 2 | |
| `Squawk` | `ushort` | |
| `Level`, `Range` | `byte` x 2 | |
| `Heading` | `ushort` | |
| `IcaoType`, `Departure`, `Destination`, `Rules`, `Route`, `Remarks` | `string` x 6 | |
| `Alternate`, `FlightSpeed`, `FlightAltitude` | `string` x 3 | read only if `DataVersion >= 21003` |
| `Registration`, `IcaoAirline`, `FlightNumber` | `string` x 3 | read only if `DataVersion >= 21006` |

**UserList** (19): the pre-`UserList2` form, read but never sent. `Count` `ushort`, then per user: `Guid`,
`Flags` `byte`, `Callsign`, `Nickname` `string`, `Frequency` `ushort`, `Latitude`, `Longitude` `float`,
`Altitude`, `Speed` `ushort`, `IcaoType`, `From`, `To` `string`, `Squawk` `ushort`, `Level`, `Range`
`byte`, `Heading` `ushort`. The fields it lacks are taken as empty.

**UserPositions** (36), unreliable: `Count` `ushort`, then per user `Guid`, `Latitude`, `Longitude`
(`float`), `Altitude`, `Speed`, `Squawk`, `Heading` (`ushort`). At most **20** users per datagram.

### 9.10 Online directory

**Online** (31), unreliable: `Uuid` `uint`. "This user is reachable at the Nuid and port I sent from."
**UserNuidRequest** (29), guaranteed: `Uuid` `uint`. **UserNuid** (30), guaranteed, the reply: `Uuid`
`uint`, `Node` `Nuid`, `Port` `ushort`.

### 9.11 Comms and notes

**SessionCommsRequest** (24), **GlobalCommsRequest** (25), **CommsListenRequest** (26): guaranteed, no
fields. Only the session request is sent, and only that is answered (with a `Notes` to the request's
endpoint); the other two are decoded and ignored.

**Notes** (28), guaranteed: a nested container of users and their notes.

```
 repeat while EndOfUsers == 0:
     EndOfUsers   byte     0 = another user follows, 1 = end of the message
     Guid         Guid
     Nickname     string
     Callsign     string
     repeat while EndOfNotes == 0:
         EndOfNotes  byte  0 = another note follows, 1 = end of this user's notes
         NoteId      uint
         Type        ushort  0 = Comms; others are skipped using Length
         Expire      ushort  written as 10 (minutes); ignored by readers
         Length      ushort  bytes of the type-specific part that follow
         type-specific part, for Comms:
             Age       float    seconds since the note was posted
             Channel   ushort
             Text      string
     (the loop ends with an EndOfNotes byte of 1)
 (the loop ends with an EndOfUsers byte of 1)
```

A reader skips a note whose `Type` it does not know with `ReadBytes(Length)`, the one forward-compatible
extension point of the protocol. For a Comms note, `Length` is written as `4 + 2 + 1 + text.Length`, or as
`2 + text.Length + 2` when the sender builds the message for the "all" scope (nothing in the current
application does). Both formulas are historical, count characters rather than bytes, and nobody reads
them for Comms notes. A sender of any other note type MUST write the correct byte length.

### 9.12 Reserved and unused ids

| Ids | Status |
|---|---|
| 2-9 (`PlaneState` to `ObjectSmoke`) | Obsolete; never sent or read. |
| 20 `UsageLog`, 23 `Shutdown` | Declared; the current code neither sends nor handles them. (The protocol notes of the pre-rewrite code gave `UsageLog` as `Version` `string`, `Guid` and `Shutdown` as `Reason` `int`; that is not checked here.) |
| 22 `KeyLog`, 27 `AllNotesRequest`, 32 `FlightPlanRequest` | Declared; never produced or consumed. |
| 7 `AddNodes` (internal) | Declared; never produced or consumed. |

Do not assign a meaning to these ids; use JFP2.

---

## 10. Periodic traffic and rates

### 10.1 Rates

| What | When | To whom |
|---|---|---|
| `Pulse` | every 1 s | every peer |
| `Pathfinder` | every 5 s, if anything is wanted | established, direct peers (everyone in every 8th round) |
| `SharedData` | when a peer becomes established; every 5 s; and when cockpit sharing changes | each peer |
| `AircraftPosition`, `ObjectPosition` | per object at most every 50 ms (20 Hz); the simulator layer thins it per peer (below) | every peer in the table |
| `Online` | every 60 s | every online hub |
| `StatusRequest` | every 5 s round (§8.8) | hubs |
| `UserListRequest` | every 2 s round, while something shows hub users | online hubs |
| `UserPositionsRequest` | as above, if the node publishes a whazzup feed | online hubs |

Positions are sent to every peer in the table, whether or not it is established. The simulator layer sends
one tick in two to a peer that asked for reduced traffic (`Pulse` flag) or when the node itself is in
low-bandwidth mode, and one tick in 32 to a peer whose simulator is not connected. A node in low-bandwidth
mode does not relay (§7.9).

### 10.2 Reconnect

This is application behaviour, not wire format, but it explains traffic after a loss. A node that joined a
session (it did not create it), still has a `suid`, and has lost every peer, repeats its `Join`, `Login` or
global-session join every 10 seconds for up to 5 minutes, after resetting its session so that a restarted hub's new
`suid` is accepted.

### 10.3 Limits

| Limit | Value |
|---|---|
| Nodes per device (`SameDevice`) in a session | 32 |
| Peers relayed for at once | 10 (hubs can set more) |
| Hubs per IP address in a node's hub list | 4 |
| Nodes in a `Pathfinder` | 100 |
| Entries in a variables message | 100 / 100 / 80 (integer / float / string) |
| Hubs in a `HubList` | 25 |
| Users in a `UserPositions` | 20 |

---

## 11. Versioning and compatibility

### 11.1 Two constants, no negotiation

- `Version` `0x520B` in the header is the framing constant. A node discards anything else. It has not
  changed since the protocol was released and cannot change without breaking every deployed node.
- `DataVersion` in every application message is the sender's data-format version (21008 today; it began as
  the simulator code's own version number and is now a separate, frozen constant). Nothing is negotiated.
  A receiver reads the fields it knows and decides per field from the number.

### 11.2 Fields gated by `DataVersion`

All gates are in the reader; the writer of a current build always writes the full layout.

| `DataVersion` | Effect |
|---|---|
| `< 10014` | The message is dropped. |
| `>= 10019` | `SharedData` has `Version` and `Simulator`. |
| `>= 10023` | `AircraftPosition` has `Elevation` and `GroundFlags`; `ObjectPosition` has `Height` and `GroundFlags`. |
| `>= 10024` | `SharedData.Flags` bit 2 (simulator connected) is meaningful. |
| `>= 10025` | `Status` has `HubFlags` after `ActivityCircle`. |
| `>= 21003` | `FlightPlan` and `UserList2` have `Alternate`, `Speed`, `Altitude` (and `FlightPlan` also `Callsign`). |
| `>= 21006` | `FlightPlan` and `UserList2` have `Registration`, `IcaoAirline`, `FlightNumber`. |

`AircraftPosition`'s `StaticCgToGround` (added with 21008) and the identity tail are not version-gated; they
are read while bytes remain (§9.3). `StatusRequest.Uuid` and `Login`'s fields are read with the same
tolerance.

### 11.3 Rules for a change to the wire

The protocol is frozen, so there should be none. If one is unavoidable:

- Append fields at the end of a message and gate their read; never insert, remove, resize or retype one.
- Append new ids at the end of an enum; never reorder or reuse.
- A reader must ignore trailing bytes; a writer must not rely on a reader to reject them.
- A newer node must remain readable by an older one, since there is no way for a reader to tell a sender
  what it understands.

---

## 12. Security considerations

The protocol has none of the usual protections, by design of its era. Operators and implementers need to
know exactly what the reference implementation does:

- **No encryption, no integrity.** Everything is plaintext UDP. Only UDP's own checksum protects the
  bytes.
- **No sender authentication.** A node is whatever Nuid the datagram says. Nothing ties the Nuid to the
  source address, and a `Pulse`, `PulseResponse`, `Pathfinder` or `PathfinderResponse` that claims a known
  peer's Nuid, carries the session's `suid` (which travels in the clear) and arrives directly, re-points
  where the receiver sends that peer's traffic if it is reached directly (§7.5). A third party that can
  send datagrams can therefore redirect a peer's traffic, impersonate a peer, or inject positions, events
  and chat.
- **Weak password.** A session password is a 32-bit unsalted hash (§4.1) sent in clear in `Join`, so it can
  be recovered by brute force from one captured datagram and replayed as is. `Login` sends the same kind
  of hash plus the email address in clear.
- **Login can reset a password.** With `Verify` set the creator stores the presented hash for any address
  in its store, replacing the existing one (§7.4).
- **No replay protection** beyond duplicate suppression of guaranteed messages for 240 seconds.
- **Relaying.** A node forwards unauthenticated datagrams to its peers on behalf of any sender. It bounds
  this by the relay budget (10 senders for 5 seconds) and one hop, and a hub MAY raise the budget.
- **Ban list.** The only access control besides the password: an in-memory list of banned source IPv4
  addresses, checked before any parsing.
- **Resource use.** A receiver trusts the first segment's `GuaranteedCount` and keeps an incomplete
  reassembly entry for 240 seconds, and a reader allocates the lists whose `Count` a message states. Such
  a message is not validated beyond what the datagram holds.

---

## 13. Quirks of the implementation

Behaviour a second implementation needs to know, because released peers depend on it or because it is not
obvious from the formats.

1. **Nuid byte order.** The address is written as a little-endian `uint` of the big-endian-packed value, so
   the bytes are reversed (§3.3). A Nuid copied from a packet capture reads backwards.
2. **`KnownNode.Port` is not `Nuid.port`.** In `JoinReply`, `AddNode`, `HubList` and `UserNuid` it is the
   port where the sender saw the node (§4).
3. **Replies to a join go to the source endpoint with a null recipient.** The joiner is not yet a peer and
   the Nuid it sent may be wrong behind a NAT; the datagram's source address is what is trusted.
4. **`GuaranteedCount` is 1 in every datagram that is not a segment of a larger message**, guaranteed or
   not; only the `Guaranteed` flag says whether an acknowledgement is wanted.
5. **Guaranteed id 0 occurs** after the counter wraps; the flag, not the id, says "guaranteed".
6. **Exactly one hop of relay.** A forwarded datagram is never forwarded again, so a path of two
   intermediate nodes does not exist.
7. **Peer expiry ignores received pulses.** Only an answer extends a peer's lifetime (§7.6).
8. **A node in low-bandwidth mode refuses to relay**, and so makes its direct neighbours unreachable to
   peers that depended on it.
9. **A `Join` to a node that is not in a session, or has joined through a `Login`, is silently ignored**,
   as is one that exceeds the device limit: the joiner gets no `JoinFail` and keeps waiting.
10. **`FlightPlan.Version`, `Notes.Expire` and the `Notes` lengths and `Variables.ChangeTimes`** are written
    for byte fidelity and read by nobody.
11. **Sender-side differences between released and current builds.** The bytes are the same; the
    differences (taken from the comments in `LegacyReliability.cs` and `MeshManager.cs`):
    - released builds sent a guaranteed message on the next tick; current builds send at once;
    - released builds resent to the address recorded when the message was queued, so a peer reachable only
      through a relay never received a resend; current builds resend to the peer's current route
      (finding 8 in `jfp2/history/protocol-v2-implementation-review.md`);
    - released builds matched an acknowledgement by id alone, so on a broadcast one recipient's
      acknowledgement cancelled another's pending copy; current builds match (id, recipient)
      (finding 9);
    - the released login handler wrote its `LoginFail(InvalidAddress)` for an unparsable address into a
      stale send buffer (comment in `MeshManager.Handle(LoginRequest)`); current builds send it properly.

    A released peer's own guaranteed sends through a relay still suffer from findings 8 and 9, and a
    receiver cannot fix that.

---

## Appendix A. Example datagrams

All come from the golden fixtures; the source Nuid is 203.0.113.1:6112/42 (`01 71 00 CB E0 17 2A`).

### A.1 Join

`internal_join.hex`

```
0B 52                    Version 0x520B
03                       Flags  Internal | Guaranteed
00 01                    GuaranteedId 0x0100
00                       GuaranteedIndex 0
01                       GuaranteedCount 1
01 71 00 CB E0 17 2A     Sender 203.0.113.1, port 6112, local 42
00 00 00 00 00 00 00     Recipient null
00 00                    InternalId 0  Join
BE BA FE CA              PasswordHash 0xCAFEBABE
```

### A.2 Accepted join (`mesh_join_accepted.hex`)

The member 203.0.113.1:6112/42 (the sender of all four) accepts a `Join` from 127.0.0.1:46112/1 and has one
other peer, 198.51.100.2:6113/7:

```
1  0B52 01 0000 00 01  <sender>  01 00 00 7F 20 B4 01   06 00  77 07  00
     Flags Internal only, addressed to the joiner; GuaranteedDone id 0x0777 index 0

2  0B52 03 0001 00 01  <sender>  00 00 00 00 00 00 00   01 00  0D F0 AD 0B  01 00
                       02 64 33 C6 E1 17 07  20 B4
     JoinReply, guaranteed, null recipient: suid 0x0BADF00D, 1 node: 198.51.100.2 port 6113 local 7,
     seen at port 0xB420 = 46112

3  0B52 03 0101 00 01  <sender>  02 64 33 C6 E1 17 07   02 00  0D F0 AD 0B
                       01 00 00 7F 20 B4 01  20 B4
     AddNode{suid, 127.0.0.1:46112/1, port 46112}, addressed to the existing peer 198.51.100.2

4  as 3, but addressed to the joiner itself (recipient 01 00 00 7F 20 B4 01)
```

`<sender>` is `01 71 00 CB E0 17 2A`. `GuaranteedId` is taken from a counter shared by all guaranteed
messages, here 0x0100 for the `JoinReply` and 0x0101 for the `AddNode` broadcast; datagrams 3 and 4 are one
broadcast and carry the same id and the same bytes except for the recipient.

### A.3 Pulse and PulseResponse

`mesh_pulse_timestamp_zeroed.hex` (Pulse with the timestamp zeroed so the fixture is stable):

```
0B52 01 0000 00 01  <sender>  02 64 33 C6 E1 17 07   04 00  0D F0 AD 0B   00 00 00 00 00 00 00 00   01
                              recipient 198.51.100.2   Pulse  suid           Time 0                  Flags: low bandwidth
```

`mesh_pulse_response.hex`: `... 05 00 08 07 06 05 04 03 02 01` = `PulseResponse`, `Time` = 0x0102030405060708.

### A.4 An application message

`app_online.hex`: `Online{uuid 0x12345678}`, unreliable.

```
0B52 00 0000 00 01  <sender>  00 00 00 00 00 00 00   10 52   1F 00   78 56 34 12
     Flags 0                  null recipient         21008   AppId 31  Uuid
```

### A.5 A relayed datagram

`mesh_forward.hex`: node 192.0.2.153:6200/9 sent a `WeatherUpdate{"METAR"}` to 198.51.100.2:6113/7, which
this node forwarded:

```
0B52 04 0000 00 01  99 02 00 C0 38 18 09   02 64 33 C6 E1 17 07   10 52   0C 00   05 4D 45 54 41 52
     Flags Forward  Sender (unchanged)     Recipient (unchanged)  21008   AppId 12  "METAR"
```

### A.6 A segmented message

`guaranteed_segmented_comms_note.hex` is a `Notes` message of 3 segments. The first starts
`0B52 02 0001 00 03 <sender> <recipient> 1052 1C00 ...`: Flags `Guaranteed`, id 0x0100, index 0, count 3,
`AppId` 28 (`Notes`). The other two differ in index (1 and 2) and carry the continuation of the payload;
the application prefix (`DataVersion`, `AppId`) is only in segment 0, since it is part of the payload that
was cut.

---

## Appendix B. Constants

| Name | Value | Where |
|---|---|---|
| Default UDP port | 6112 | `Network.DEFAULT_PORT` |
| `Version` | `0x520B` (bytes `0B 52`) | `LegacyWire.Version` |
| Header size, `DataOffset` | 21 | `LegacyWire.DataOffset` |
| `Flags` | Internal `0x01`, Guaranteed `0x02`, Forward `0x04` | `LegacyWire` |
| Pulse flag | low bandwidth `0x01` | `LegacyWire.PulseFlagLowBandwidth` |
| `DataVersion` written | 21008 | `LegacyWire.DataVersion` |
| Minimum `DataVersion` read | 10014 | `LegacyWire.MinDataVersion` |
| `MaxGuaranteedData` | 1,000 bytes | `LegacyWire.MaxGuaranteedData` |
| Guaranteed resend interval | 2 s | `LegacyReliability.ResendInterval` |
| Guaranteed send / receive expiry | 180 s / 240 s | `OutExpireTime` / `InExpireTime` |
| Pulse interval | 1 s | `MeshManager.PulseInterval` |
| Pathfinder interval | 5 s | `MeshManager.PathfinderInterval` |
| Direct check | every 8th pathfinder round | `MeshManager.DoRouting` |
| Peer expiry | 30 s | `MeshManager.ExpireTime` |
| Nodes per device | 32 | `MeshManager.MaxNodesPerDevice` |
| Relay budget, hold | 10 senders, 5 s | `MeshManager.DefaultRelayBudget`, `RelayHoldTime` |
| Nodes per `Pathfinder` | 100 | `MeshManager.MaxPathfinderNodes` |
| Variables per message | 100 / 100 / 80 | `LegacyWire.Max*Variables` |
| Hubs per `HubList` | 25 | `LegacyWire.MaxHubListEntries` |
| Users per `UserPositions` | 20 | `LegacyWire.MaxUserPositions` |
| Comms note type, expiry | 0, 10 | `LegacyWire.NoteTypeComms`, `CommsExpire` |
| Axis scale | 16384 | `LegacyWire.ConvertToAxis` |
| Position send interval | 0.05 s | `Sim.PositionSendInterval` |
| Peer info interval | 5 s | `PeerTable.peerInfoTimer` |
| Hub poll / user list / online / hub offline | 5 s / 2 s / 60 s / 300 s | `HubDirectory`, `UserDirectory` |
| Hub user list rebuild | 10 s | `HubHost.localUsersTimer` |
| Hubs per IP, dead hub | 4, 7 days | `HubDirectory.MAX_IP_HUBS`, `DEAD_HUB_DURATION` |
| Reconnect retry, give up | 10 s, 5 min | `Network.RECONNECT_*` |

## Appendix C. Sources and verification

Checked against the repository at commit `aeaff8d` (2026-10-18).

| Concern | File |
|---|---|
| Header, flags, ids, constants | `JoinFS/Net/Protocols/Legacy/LegacyWire.cs` |
| Framing, receive procedure, relay, every encoder and decoder | `JoinFS/Net/Protocols/Legacy/LegacyPlugin.cs` |
| Guaranteed delivery | `JoinFS/Net/Protocols/Legacy/LegacyReliability.cs` |
| Session membership, pulse, pathfinder, relay budget | `JoinFS/Net/Core/MeshManager.cs` |
| Ban list, protocol demultiplexing, routing, session gating | `JoinFS/Net/Core/NetworkCore.cs` |
| Peer table, `Direct`, endpoints | `JoinFS/Net/Core/PeerDirectory.cs`, `LocalIdentity.cs` |
| Nuid, hash, credentials | `JoinFS.Messages/NodeId.cs`, `JoinFS/Net/Core/NetHash.cs`, `CredentialStore.cs` |
| Canonical message field types | `JoinFS.Messages/Messages/*.cs` |
| What the application sends and when | `JoinFS/Network.cs`, `JoinFS/Session/` (`PeerTable`, `HubDirectory`, `HubHost`, `UserDirectory`, `SessionComms`, `SimSender`, `SimIngest`), `JoinFS/Sim.cs` |
| Byte-for-byte examples | `JoinFS.Tests/Legacy/Fixtures/*.hex`, `LegacyPluginGoldenTests.cs` |

Every byte layout in §9 was read from the encoder and the decoder in `LegacyPlugin.cs`. The fixtures
`internal_join`, `mesh_join_accepted`, `mesh_join_fail_password`, `mesh_pulse_*`, `mesh_pathfinder`,
`mesh_forward`, `app_online`, `app_aircraft_position`, `app_integer_variables_chunked`, `app_comms_note`,
`app_shared_data` and `guaranteed_segmented_comms_note` were decoded by hand against the layouts to check
them; the rest of the fixtures were not decoded individually. Behaviour in §7, §8 and §10 was read from the
code paths named in Appendix C; it was not observed on a live network for this document.
