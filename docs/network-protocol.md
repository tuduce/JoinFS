# JoinFS Network Protocol

This document describes the wire protocol JoinFS instances (simulator-connected clients and hub-only console instances) use to talk to each other over the network. It is derived directly from `JoinFS/Node.cs`, `JoinFS/Network.cs` and `JoinFS/Sim.cs` (current `Sim.VERSION = 21008`). Line numbers referenced below are approximate and will drift as the code evolves; they're accurate as of this writing.

## 1. Overview

JoinFS uses a single UDP socket per running instance to form a peer-to-peer mesh: every node that has joined a "session" (a group of nodes cooperating in the same multiplayer world) can, in principle, exchange datagrams directly with every other node in that session. There is no central server in the traditional sense — a "hub" is just a regular JoinFS instance (usually the `CONSOLE` build with no simulator attached) that other nodes rendezvous through and that offers directory/relay services (session membership propagation, ATC/pilot list aggregation, hub discovery). All simulator variants (FS2020, FS2024, FSX, P3D, X-Plane) and the console/hub build share **exactly the same wire protocol** — there are no `#if SIMCONNECT`/`#if XPLANE`/`#if CONSOLE` branches anywhere in `Node.cs` or `Network.cs`. Simulator-specific code only exists above this layer, translating SimConnect/X-Plane SDK data into the structures described here.

Key properties:

- **Transport:** plain UDP (`System.Net.Sockets.UdpClient`), IPv4 only, default port **6112** (`Network.DEFAULT_PORT`).
- **Topology:** full mesh P2P between session members, with best-effort UDP hole-punching/relaying for nodes that can't reach each other directly, and store-and-forward routing through other mesh members as a fallback.
- **Reliability:** selective, message-by-message. Most high-frequency state (positions) is unreliable/unordered (fire-and-forget UDP); a subset of control messages opt into an in-house "guaranteed" delivery scheme (ACK + retransmit) implemented on top of UDP.
- **Encoding:** everything is little-endian binary, written with .NET's `BinaryWriter`/`BinaryReader` (which also defines the string encoding: a 7-bit-encoded length prefix followed by UTF-8 bytes, i.e. `BinaryWriter.Write(string)`).
- **Security:** none at the transport level. No TLS/DTLS, no per-packet authentication or integrity check. A session can optionally require a password (hashed, not encrypted) and/or an email/password "login" gate enforced by the session creator node.
- **Versioning:** every application-level message is individually tagged with a 16-bit "data version" number, and the receiver decides what to read based on that number. There is no single session-wide protocol version negotiation — each message is self-describing.

## 2. Transport framing

Every UDP datagram JoinFS sends — whether it's an internal housekeeping message (`Join`, `Pulse`, ...) or an application message (`AircraftPosition`, `Notes`, ...) — starts with the same fixed-size 21-byte header, defined by the offsets in `LocalNode` (`Node.cs`):

| Offset | Size | Field | Description |
|---|---|---|---|
| 0 | 2 | `Version` | Fixed protocol/handshake constant, currently `0x520B`. Not a data/feature version — see §5. Any datagram whose first 2 bytes don't match this value is dropped (`Stats.WrongVersion`) before anything else is parsed. |
| 2 | 1 | `Flags` | Bit 0 (`0x01`) = `FLAG_INTERNAL` (transport/session-management message, handled inside `LocalNode` and never surfaced to the application). Bit 1 (`0x02`) = `FLAG_GUARANTEED` (this datagram wants an ACK, see §4). Bit 2 (`0x04`) = `FLAG_FORWARD` (this datagram has already been relayed once by an intermediate node — see §6). |
| 3 | 2 | `GuaranteedId` | `ushort`. Non-zero only when `FLAG_GUARANTEED` is set; identifies the logical guaranteed message this segment belongs to (see §4). |
| 5 | 1 | `GuaranteedIndex` | `byte`. Zero-based segment index within the guaranteed message. |
| 6 | 1 | `GuaranteedCount` | `byte`. Total number of segments the guaranteed message was split into. |
| 7 | 7 | `Sender` (`Nuid`) | The sending node's network identity — see §3. |
| 14 | 7 | `Recipient` (`Nuid`) | The intended final recipient's `Nuid`, or the "null" `Nuid` (`ip == 0`) to mean "everyone in the mesh" / "no specific recipient" (used for broadcasts and most internal replies). |
| 21 | variable | `Data` | The message payload — see §5 for how its first bytes are interpreted. |

This 21-byte prefix is constructed once per outbound message by `LocalNode.PrepareMessage()` / `PrepareInternalMessage()`, and the recipient field is patched in-place just before the socket send (`Send()` rewrites the 7 bytes at offset 14 so the same buffer can be reused for different recipients without rebuilding the whole message, e.g. when a guaranteed message is queued for one recipient and then must be resent).

There is no length field in the header: UDP already delivers a message as one discrete datagram, so `Data` simply runs to the end of the received buffer. There is no checksum/CRC of the JoinFS payload itself (UDP's own 16-bit checksum is the only integrity check in play).

## 3. Node identity — the `Nuid`

Nodes do not have an application-assigned ID; a node's identity *is* its network address, packed into a 7-byte structure (`LocalNode.Nuid`):

| Size | Field | Description |
|---|---|---|
| 4 | `ip` | `uint`, big-endian-packed IPv4 address (`(b0<<24)\|(b1<<16)\|(b2<<8)\|b3`). |
| 2 | `port` | `ushort`, the UDP port the node is listening on. |
| 1 | `local` | `byte`, the last octet of the node's *local* (LAN) IP address. Used to distinguish multiple JoinFS instances running behind the same NAT/public IP (`Nuid.SameDevice`), and included in the human-readable `ip/local` node label used in logs. |

A `Nuid` with `ip == 0` is the sentinel "invalid/none" value, used as a recipient placeholder for broadcasts and for internal replies where the reply is simply sent back to the datagram's source `IPEndPoint` rather than routed by `Nuid`.

**Implication for extensibility:** because `Nuid` is hard-wired to a 4-byte IPv4 address, the protocol has no representation for an IPv6 peer. `AddressFamily.InterNetwork` is asserted at every address-parsing call site in `Network.cs`/`Node.cs`. Adding IPv6 support would require a new, larger identity structure end-to-end (see §9).

## 4. Reliability — the "guaranteed" delivery mechanism

Most traffic (position updates, variable syncs, pulses) is sent once via `udpClient.Send()` and never retried — acceptable because a fresher update follows within a second or two anyway. A smaller set of one-shot/control messages (`Join`, `JoinReply`, `JoinFail`, `Login`, `LoginFail`, `RemoveObject`, `WeatherRequest`/`WeatherReply`, `Notes`, `SessionCommsRequest`/`GlobalCommsRequest`/`CommsListenRequest`, `SimEvent`, `ShowOnRadar`, `UsageLog`, `UserNuid`, ...) are sent with `guaranteed: true`, which engages a lightweight custom ARQ layer. Notably `Leave`, `Pulse`/`PulseResponse`, `Pathfinder`/`PathfinderResponse` and `WeatherUpdate` are *not* guaranteed even though they're control-plane messages — they're either broadcast redundantly on a timer anyway (`Pulse`) or best-effort by design (`Leave` is a courtesy notice; a node that misses it will simply time out the peer after `EXPIRE_TIME`, see §6.2):

- The sender assigns a new 16-bit `GuaranteedId` (`LocalNode.nextGuaranteedId`, seeded from a high-resolution timestamp and incremented per message — it is **not** reset per-peer, so it can theoretically wrap after 65536 guaranteed sends and collide with an older, not-yet-expired id for the same peer).
- The payload is split into segments of at most `MAX_GUARANTEED_DATA = 1000` bytes each (so a message up to 255 × 1000 = 255,000 bytes could theoretically be represented, bounded by the 1-byte segment index/count fields), each segment getting its own copy of the 21-byte transport header with `GuaranteedIndex`/`GuaranteedCount` filled in.
- All not-yet-acknowledged segments are resent unconditionally **every 2 seconds** (`DoGuaranteedMessages()`) until each is acknowledged or the whole message expires **180 seconds** after creation (`GUARANTEED_OUT_EXPIRE_TIME`), at which point it's silently dropped.
- On the receiving side, each segment is buffered (`GuaranteedIn`, indexed by sender `IPEndPoint` + `GuaranteedId`) until all `GuaranteedCount` segments have arrived, then the segments' data (with headers stripped) is concatenated back into one logical message and dispatched exactly like a normal message. Reassembly state for a given guaranteed message is discarded **240 seconds** after first segment receipt (`GUARANTEED_IN_EXPIRE_TIME`) whether or not it ever completed.
- Every received segment — even a duplicate of one already marked done — triggers an immediate, unreliable `GuaranteedDone` reply (`{ GuaranteedId: ushort, GuaranteedIndex: byte }`) sent back to the segment's sender, acknowledging that specific segment. There is no cumulative/selective-repeat ACK; each segment is acknowledged individually.
- This is a **per-segment stop-and-wait style scheme with blind retransmission**, not a sliding window: all unacked segments of a message are re-sent on every 2-second tick regardless of how many were already acked, which is simple but wastes bandwidth for partially-acked large messages and does not adapt to RTT (the pulse mechanism computes RTT per node, see §7, but the guaranteed-resend timer ignores it).

## 5. Application-message envelope and versioning

For a **non-internal** message (`FLAG_INTERNAL` clear — i.e., anything the application layer cares about, `MESSAGE_ID` values listed in §8), the `Data` portion of the transport frame (offset 21+) is laid out as:

| Offset (relative) | Size | Field |
|---|---|---|
| 0 | 2 | `DataVersion` (`short`) — the sender's `Sim.VERSION` constant at build time, currently `21008`. |
| 2 | 2 | `MessageId` (`short`, cast from the `MESSAGE_ID` enum in `Network.cs`) |
| 4 | variable | message-specific payload, see §8 |

`Network.ReceiveMsg()` reads `DataVersion` first and immediately discards the whole message if `dataVersion < 10014` — that's the oldest data-format baseline the current code still understands. Otherwise every field-level read inside each `case` block is itself conditioned on `dataVersion`, e.g.:

```csharp
aircraftPosition.staticCgToGround = version >= 21008 ? reader.ReadSingle() : float.NaN;
```

Because `DataVersion` travels with **every individual message** rather than being negotiated once at session join, a mixed-version mesh works cleanly: an old node and a new node can be connected in the same session, and each message the new node sends is self-describing, so the old node's reader (compiled against an older `dataVersion` floor) simply never executes the `if (dataVersion >= X)` branches it doesn't know about — except that in practice the *reader's code* is what decides what to do with a version number, so what really matters is: a receiver only understands version-gated branches that existed in its own build. A newer node talking to an older node must therefore keep its message layout backward-readable (see §9), not the other way around; there's no mechanism for a receiver to tell a sender "please don't send me the new fields."

Two concrete tail-extension patterns are already in production use, both worth reusing for future changes:

1. **Version-table dispatch** (`Sim.cs`, e.g. `positionVelocityVersions`): a `Dictionary<short, ReadVersion<T>>` maps a minimum `dataVersion` to a reader function; `Sim.Read()` picks the highest-keyed entry whose key is `<= dataVersion`. This is used for whole-struct reads (`ObjectPositionVelocity`, `AircraftPosition`) where a version bump changes multiple fields at once.
2. **Trailing optional fields via EOF sensing** (`Network.cs`, `ObjectPosition`/`AircraftPosition` handlers): several string fields (`variation`, `icaoType`, `icaoAirline`, `classCode`, `wtc`, ...) were added over time simply by appending `message.Write(...)` calls to the end of the sender, and reading them back with:
   ```csharp
   string variation = (reader.PeekChar() != -1) ? reader.ReadString() : "";
   ```
   i.e. "if there are any bytes left in this datagram, there's another (fixed-order) field to read." This works because a `BinaryReader` over a `MemoryStream` can cheaply peek for end-of-stream, and because UDP delivers each message as a discrete unit (so "end of stream" reliably means "end of this datagram", unlike a TCP byte stream).

**Internal** messages (`FLAG_INTERNAL` set — `Join`, `JoinReply`, `Leave`, `AddNode`, `Pulse`, `PulseResponse`, `GuaranteedDone`, `AddNodes`, `Pathfinder`, `PathfinderResponse`, `JoinFail`, `Login`, `LoginFail`) do **not** carry a `DataVersion` field — only `MessageId` (`short`) follows the 21-byte transport header directly. Their format is pinned to the fixed transport `Version` constant (`0x520B`); there is currently no mechanism to version internal/session-management messages independently of application messages (see §9).

## 6. Mesh membership, routing and NAT traversal

### 6.1 Joining a session

- The node that starts a session calls `LocalNode.Create()`, generating a random 32-bit session id (`suid`) and, if a password was configured, storing `HashPassword(password)` (see §7 for the hash algorithm — it is **not** cryptographically strong).
- A node that wants to join sends an internal `Join` message (password hash + nothing else) directly to a known peer's `IPEndPoint` (obtained via manual address entry, the address book, a hub's `Status` broadcast, or hub discovery — §6.4).
- The receiver validates the password hash (if any) and login requirement (if any — see §7), and replies with `JoinReply`: the session id (`suid`) plus the full list of `Nuid`+port pairs of every node it currently has an established *receive* link with. This is how a newly joined node learns about everyone already in the mesh in one shot, without having to be told about each one individually.
- The joining node registers every peer from that list (without yet having exchanged a single packet with most of them) and starts sending them `Pulse` messages directly — this is what actually establishes bidirectional connectivity/NAT bindings with each mesh member (see 6.2).
- Whenever a *new* node successfully joins, every already-established node broadcasts `AddNode` (session id + the new node's `Nuid`/port) so the rest of the mesh learns about it too, without needing another `JoinReply` round trip.

### 6.2 Keepalive / link establishment — `Pulse`

Every connected node broadcasts a `Pulse` message to every other known node once per second (`PULSE_INTERVAL = 1`): `{ Suid: uint, Time: long (high-res timestamp), Flags: byte (bit0 = low-bandwidth mode) }`. The recipient replies (unicast, unreliable) with `PulseResponse`: `{ Time: long (echoed back) }`. Two effects follow from this exchange:

- The sender computes round-trip time from the echoed timestamp (`Node.rtt`), though — as noted in §4 — this RTT is not currently fed back into the guaranteed-retransmit timer.
- A link is only considered `sendEstablished` once a `PulseResponse` (or any other reply, e.g. `JoinReply`) has actually been received back from that peer; only then does the application get the `nodeEstablished` callback and only then does JoinFS start sending that peer full simulation traffic. A node that has sent a `Pulse` but never received anything back is a suspected-dead link. Nodes that haven't refreshed within `EXPIRE_TIME = 30s` are dropped from the mesh locally; a link that stops responding gets **re-established** (falls back to indirect routing) after `REESTABLISH_TIME = 3s` of silence.

### 6.3 Store-and-forward relay and hole-punching — `Pathfinder`

Direct UDP connectivity between two arbitrary peers behind different NATs is not guaranteed. JoinFS addresses this two ways simultaneously:

1. **Relay through the mesh.** If node A cannot reach node C directly but both are connected to node B, a datagram addressed to C (`Recipient` `Nuid` != local, but not the sender's own) that arrives at B gets its `FLAG_FORWARD` bit set and is resent verbatim to C's registered endpoint (`ReceiveMsg`, direct/forward branch). Relaying is capped at `MAX_ROUTING_NODES = 10` concurrently-routed peers per node and disabled entirely when the node has set `lowBandwidth`. A message is only forwarded once (the `FLAG_FORWARD` bit prevents forwarding an already-forwarded message a second hop), so the mesh's usable relay depth is exactly one intermediate hop.
2. **Active hole-punching via `Pathfinder`.** Every 5 seconds (`PATHFINDER_INTERVAL`), each node sends every other node it doesn't yet have `sendEstablished` for (or, on roughly every 8th cycle, *every* node, even established ones, to opportunistically detect a newly-available direct path) a `Pathfinder` message listing the `Nuid`s of the peers it wants a direct/rendezvous path to (up to `MAX_PATHFINDER_NODES = 100`). Any recipient that is itself connected to one of the listed target `Nuid`s answers with `PathfinderResponse`, which either causes the requester to talk to that target directly (if the responder *is* the target — `Direct`) or to route via the responder's endpoint as a rendezvous point (if the responder merely knows the target). This is effectively an application-level NAT traversal / DHT-lite discovery mechanism layered on top of the plain relay in (1).

### 6.4 Hub discovery and directory services

A "hub" is simply a node with `settingsHub = true`; it periodically broadcasts its extra hub metadata (name, "about" text, VOIP info, next event, whether it requires a password, whether it's part of the "global session", current user/ATC/aircraft counts) inside the `Status` message (§8.6), and other hubs propagate knowledge of each other via `HubList` (§8.7) so the whole hub directory eventually converges without a single authoritative registry. New installs bootstrap this directory from a small seed list fetched over HTTPS from a fixed GitHub-hosted text file (`util/seedhubs.txt`), with a bundled fallback copy if that fetch fails. A hub-connected client can additionally ask its hub for a snapshot of every other pilot/ATC currently visible to that hub via `UserListRequest`/`UserList2` and `UserPositionsRequest`/`UserPositions` — this is a lighter-weight "who's online" feed than joining the full P2P mesh, comparable to a VATSIM/IVAO "whazzup" feed, used for radar/map display without establishing a direct link to every visible aircraft.

## 7. Session security

- **Password gate:** optional. `Join` carries a 32-bit hash of the session password (`HashPassword`), compared server-side against the creator's stored hash. The hash function is a hand-rolled DJB2-style 32-bit string hash (`HashString`, `Node.cs`) — it is fast, unsalted, has no cryptographic collision/preimage resistance, and is trivially brute-forceable offline from a captured `Join` packet. The password itself, and this hash, are never encrypted in transit (there is no transport encryption at all).
- **Login gate:** optional, independent of the password gate. `Login` carries a plaintext email address, a 32-bit password hash (same weak hash function), and a `verify` flag. The session creator checks the email against a locally-stored credentials table (first-use = trust-on-first-use: if `verify` is set the presented hash becomes the stored hash for that address) and returns `LoginFail` with a `LoginResult` (`InvalidAddress` / `VerifyPassword` / `InvalidPassword`) or folds straight into the same `JoinReply` flow as a successful `Join`.
- **No sender authentication.** A node's identity on the wire is nothing more than the `Nuid` embedded in the packet by the sender itself — there is no signature, token, or address verification tying a `Nuid` to the actual source `IPEndPoint` of a datagram (`ReceiveMsg` does cross-check that a message's *claimed* sender isn't the local node, and routes based on the registered node's known endpoint once established, but nothing stops a third party from spoofing another node's claimed `Nuid` in an unsolicited packet, or, since this is UDP, from spoofing the source IP itself).
- **No replay protection.** Beyond `GuaranteedId`/`GuaranteedIndex` de-duplicating retransmits of the *same* logical guaranteed message, there is no nonce, sequence number, or timestamp validation that would prevent a captured `Join`/`Login` datagram from being replayed later.
- **IP ban list.** The only access control beyond the above is a simple in-memory list of banned source IPs (`BanIP`), checked before any other parsing.

None of this is unusual for a hobby/community flight-sim P2P protocol operating over UDP, but it's worth stating plainly for anyone evaluating what the protocol does and doesn't protect against — see §9.4 for concrete suggestions.

## 8. Message catalog

`MESSAGE_ID` is a 16-bit enum (`Network.cs`); values are assigned by declaration order (i.e. they are **not** explicit `= N` literals), so the numeric value of every entry is determined purely by its position in the list. **Reordering or inserting into the middle of this enum silently breaks wire compatibility** — see §9.1.

### 8.1 Internal / session-management messages (`LocalNode.MESSAGE_ID`, separate 16-bit space from the application enum)

| Message | Guaranteed? | Payload |
|---|---|---|
| `Join` | yes | `PasswordHash: uint` |
| `JoinReply` | yes | `Suid: uint`, `Count: ushort`, then `Count` × (`Nuid: 7 bytes`, `Port: ushort`) |
| `JoinFail` | yes | `Result: byte` (`JoinResult`: `Accepted`=0, `PasswordRequired`=1, `LoginRequired`=2) |
| `Login` | yes | `Email: string`, `PasswordHash: uint`, `Verify: bool` |
| `LoginFail` | yes | `Result: byte` (`LoginResult`: `Accepted`=0, `InvalidAddress`=1, `VerifyPassword`=2, `InvalidPassword`=3) |
| `Leave` | no (broadcast) | `Suid: uint` |
| `AddNode` | yes (broadcast, guaranteed per-recipient) | `Suid: uint`, `Nuid: 7 bytes`, `Port: ushort` |
| `AddNodes` | — | declared in the enum; no producer or consumer currently implemented (reserved) |
| `Pulse` | no | `Suid: uint`, `Time: long`, `Flags: byte` (bit0 = low bandwidth) |
| `PulseResponse` | no | `Time: long` (echo) |
| `Pathfinder` | no | `Suid: uint`, `Count: ushort`, then `Count` × `Nuid: 7 bytes` |
| `PathfinderResponse` | no | `Suid: uint`, `Count: ushort`, then `Count` × `Nuid: 7 bytes` |
| `GuaranteedDone` | no (it *is* the ack) | `GuaranteedId: ushort`, `GuaranteedIndex: byte` |

### 8.2 Position / state messages

**`ObjectPosition`** (unreliable) — a non-user-controlled AI/static object's position. Sent by `Network.WriteObjectPositionVelocityMessage`:

| Field | Type |
|---|---|
| `NetId` | `uint` |
| `Model` | `string` |
| `TypeRole` | `byte` (model-substitution category) |
| `Flags` | `byte` (bit0 = paused) |
| `SimTime` | `double` |
| `Latitude, Longitude, Altitude` | `double, double, double` |
| `Pitch, Bank, Heading` | `float × 3` |
| `VelocityXYZ` | `float × 3` |
| `AngularVelocityXYZ` | `float × 3` |
| `AccelerationXYZ` | `float × 3` |
| `Height` | `float` |
| `GroundFlags` | `byte` (bit0 = on ground, bit1 = sender has elevation correction enabled) |
| `Livery` | `string` |
| `IcaoType`, `IcaoAirline` | `string, string` |
| `ClassCode`, `Wtc` | `string, string` |
| `ClassCodeConfirmed` | `bool` |

**`AircraftPosition`** (unreliable) — a user-flown or AI aircraft. Sent by `Network.WriteAircraftPositionMessage`; superset of `ObjectPosition` plus control-surface and identity fields:

| Field | Type |
|---|---|
| `NetId` | `uint` (`0xFFFFFFFF` is reserved to mean "the sender's own user aircraft, for shared-cockpit purposes") |
| `User` | `bool` (true if this is a human-controlled aircraft, not AI) |
| `IsPlane` | `bool` |
| `Callsign` | `string` |
| `Model` | `string` |
| `TypeRole` | `byte` |
| `Flags` | `byte` (bit0 = paused) |
| `NetTime` | `double` |
| `Latitude, Longitude, Altitude` | `double × 3` |
| `Pitch, Bank, Heading` | `float × 3` |
| `VelocityXYZ`, `AngularVelocityXYZ`, `AccelerationXYZ` | `float × 9` |
| `Rudder, Elevator, Aileron, BrakeLeft, BrakeRight` | `int16`, each a fixed-point control axis, `value = round(input × 16384)` / `input = value / 16384.0` |
| `Elevation` | `float` |
| `GroundFlags` | `byte` (bit0 = on ground, bit1 = elevation correction enabled) |
| `StaticCgToGround` | `float` (version ≥ 21008; older peers omit it, and a reader on an older-than-21008 stream gets `NaN` back, deliberately distinguishable from a real 0.0) |
| `Livery`, `IcaoType`, `IcaoAirline` | `string × 3` |
| `Registration`, `FlightNumber` | `string × 2` |
| `ClassCode`, `Wtc` | `string × 2` |
| `ClassCodeConfirmed` | `bool` |

Both messages are unreliable and sent at simulator update rate; they carry the bulk of network traffic in a session.

**`SimEvent`** (guaranteed) — a discrete SimConnect/X-Plane event forwarded to a specific object (e.g. gear up/down, lights toggle): `{ NetId: uint, EventId: uint, Data: uint }`.

**`RemoveObject`** (guaranteed, broadcast) — despawn notice: `{ NetId: uint }`.

**`ShowOnRadar`** (guaranteed, broadcast) — `{ OwnerNuid: 7 bytes, NetId: uint, Show: bool }`. Declared and sent by `SendShowOnRadarMessage`, but there is currently **no receive-side handler** for it in `ReceiveMsg` — it's produced but not yet consumed anywhere in this codebase (reserved/in-progress feature).

**`IntegerVariables` / `FloatVariables` / `String8Variables`** (unreliable) — generic key/value sync for simulator "L:var"-style custom variables, used e.g. for animation state. All three share one shape: `{ OwnerNuid: 7 bytes, NetId: uint, Count: ushort, then Count × (VariableId: uint, Value: <int32|float|string>) }`. `NetId == 0xFFFFFFFF` again means "the sender's own user aircraft" (shared cockpit). Sends are automatically split into multiple messages if the variable count would exceed `MAX_INTEGER_VARIABLES`/`MAX_FLOAT_VARIABLES` (100) or `MAX_STRING8_VARIABLES` (80).

### 8.3 Identity / presence — `SharedData`

Sent unreliably, unicast to each known peer individually (not via the mesh-wide `Broadcast()` helper), immediately when a link is newly established and again to every peer every 5 seconds thereafter (`sharedDataTimer`, `DoSharedData()`). This is the message that tells the rest of the mesh who you are:

| Field | Type |
|---|---|
| `ShareFlags` | `byte` (bit0 = sharing cockpit with recipient, bit1/2/3 = recipient currently holds flight/ancillary/nav control) |
| `Nickname` | `string` |
| `Guid` | 16 bytes (raw `Guid.ToByteArray()`) — the node's persistent installation identity |
| `Flags` | `byte` (bit0 = hub, bit1 = ATC, bit2 = simulator connected — version ≥ 10024) |
| `AtcAirport` | `string` |
| `AtcLevel` | `byte` |
| `AtcFrequency` | `ushort` |
| `ActivityCircle` | `byte` |
| `Version` | `string` (application version, ≥ 10019) |
| `Simulator` | `string` (simulator name, ≥ 10019) |

### 8.4 Weather

`WeatherRequest` (guaranteed) `{ NetId: uint }`; `WeatherReply` (guaranteed, unicast) and `WeatherUpdate` (unreliable, broadcast) both `{ Metar: string }`.

### 8.5 Flight plan

`FlightPlan` (unreliable, broadcast): `{ OwnerNuid: 7 bytes, NetId: uint, Version: byte, IcaoType, Departure, Destination, Rules, Route, Remarks: string×6, Alternate, Speed, Altitude, Callsign: string×4 (≥21003), Registration, IcaoAirline, FlightNumber: string×3 (≥21006) }`.

### 8.6 `Status` / `StatusRequest` (node & hub health)

`StatusRequest` (unreliable): `{ HubEnabled: byte, HubListRequested: byte, Uuid: uint }` — also doubles as "please add me to your hub list" and "please send me your hub list" in one shot.

`Status` (unreliable, the reply): `{ Guid: 16 bytes, AppVersion: string, Users: ushort, AtcCount: ushort, [AtcAirport: string, AtcLevel: byte] if AtcCount>0, Planes/Helicopters/Boats/Vehicles: ushort×4, HubEnabled: bool, [if HubEnabled: Address, Name, About, Voip, NextEvent, Airport: string×6, ActivityCircle: int32, Flags: byte (≥10025; bit1=global session, bit2=password required)] }`.

### 8.7 `HubList`

Unreliable, chunked into groups of `MAX_HUB_LIST_MESSAGE = 25` entries per datagram: `{ Count: ushort, then Count × (Nuid: 7 bytes, Port: ushort) }`.

### 8.8 Hub client feeds

`UserListRequest`/`UserPositionsRequest` (unreliable, no payload). `UserList2` (unreliable, current version — one user per datagram): `{ Guid: 16 bytes, Flags: byte (bit0 atc, bit1 ifr), Callsign, Nickname: string×2, Frequency: ushort, Latitude, Longitude: float×2, Altitude, Speed: ushort×2, Squawk: ushort, Level, Range: byte×2, Heading: ushort, IcaoType, Departure, Destination, Rules, Route, Remarks: string×6, Alternate, Speed(str), Altitude(str): string×3 (≥21003), Registration, IcaoAirline, FlightNumber: string×3 (≥21006) }`. `UserList` (legacy, still received for backward compatibility, batched): `{ Count: ushort, then Count × { Guid: 16 bytes, Flags: byte, Callsign, Nickname: string×2, Frequency: ushort, Latitude, Longitude: float×2, Altitude, Speed: ushort×2, IcaoType, From, To: string×3, Squawk: ushort, Level, Range: byte×2, Heading: ushort } }` — note `UserList` has no producer left in the current code (`SendUserListMessage` only ever emits `UserList2`); it is read-only-compatibility surface for talking to older hubs. `UserPositions` (unreliable, batched in groups of `MAX_USER_POSITIONS = 20`): `{ Count: ushort, then Count × (Guid: 16 bytes, Latitude, Longitude: float×2, Altitude, Speed, Squawk, Heading: ushort×4) }`.

### 8.9 Text / comms — `Notes`

A generic, extensible, nested container used for chat text (only "type 0 = Comms" is implemented today, but the wire format was clearly designed to carry other note types): `{ repeat { EndOfUsers: byte(0=more,1=stop); Guid: 16 bytes; Nickname, Callsign: string×2; repeat { EndOfNotes: byte(0=more,1=stop); NoteId: uint; Type: ushort; Expire: ushort; Length: ushort; <Type-specific payload, currently only Comms: Age: float, Channel: ushort, Text: string> } } }`. An unrecognized `Type` is skipped using the `Length` prefix (`reader.ReadBytes(length)`), which is the one place in the protocol that already supports forward-compatible "unknown extension, skip it" semantics for free — see §9.2. `SessionCommsRequest`/`GlobalCommsRequest`/`CommsListenRequest` (all guaranteed, no payload) are the corresponding pull requests for session-scoped, global, and live-update comms feeds; only `SessionCommsRequest` currently has a receive-side handler (it triggers a `Notes` reply back to the requester) — `GlobalCommsRequest` and `CommsListenRequest` have producers (`SendGlobalCommsRequestMessage`, `SendCommsListenRequestMessage`) but no matching `case` in `ReceiveMsg` yet. `AllNotesRequest` is declared in the enum with neither a producer nor a consumer anywhere in the current codebase (fully reserved, like `KeyLog`).

### 8.10 Online-user rendezvous

`Online` (unreliable, sent to every known hub): `{ Uuid: uint }` — announces "this UUID is reachable via the sender's `Nuid`/port", letting a hub act as a lightweight rendezvous directory. `UserNuidRequest` (unreliable): `{ Uuid: uint }`; `UserNuid` (guaranteed, the reply): `{ Uuid: uint, Nuid: 7 bytes, Port: ushort }`.

### 8.11 Diagnostics / operational

`UsageLog` (guaranteed): `{ Version: string, Guid: 16 bytes }` — anonymous install ping. `Shutdown` (guaranteed, `DEBUG` builds only): `{ Reason: int32 }`. `KeyLog` is declared in the enum but has no producer or consumer anywhere in the current codebase (fully reserved).

### 8.12 Declared but unimplemented placeholders

The following `MESSAGE_ID` values exist in the enum with **no** producer and **no** consumer anywhere in `Network.cs`/`Sim.cs` today: `PlaneState`, `HelicopterState`, `AircraftState`, `PistonEngineState`, `TurbineEngineState`, `AircraftFuel`, `AircraftPayload`, `ObjectSmoke`. They read as reserved slots for a more granular systems-state sync than the current catch-all `IntegerVariables`/`FloatVariables`/`String8Variables` messages provide (e.g. so a receiver could subscribe to just engine state without pulling arbitrary L:vars). Anyone picking these up should confirm with the maintainers before assuming their intended layout, since none exists in code yet.

## 9. Extending the protocol without breaking older clients

The codebase already demonstrates several backward-compatible extension idioms (documented inline in §5); the recommendations below generalize those patterns and flag the places where the current design would make an easy mistake hard to avoid.

### 9.1 Keep using additive, tail-only changes — and protect the enum

- **Application message fields:** keep appending new fields at the end of a message and gate every new read with `if (dataVersion >= N) ... else <safe default>`, exactly as `staticCgToGround`, `registration`/`flightNumber`, and the `SharedData` `Flags` bit 2 already do. Never insert a field in the middle, never remove or resize an existing field, and never change a field's on-wire type — all three would silently desync every reader compiled against the old layout (there's no length-prefixing to make a parser resilient to a mismatched field width).
- **`MESSAGE_ID` enum:** because values are implicit (declaration order), **new message types must always be appended at the end of the enum**, never inserted or reordered — inserting shifts every subsequent value and instantly breaks the wire format for any two peers built from enum definitions that disagree on ordering. Given how easy this mistake is to make in a future PR, it's worth switching the enum to explicit values (`ObjectPosition = 0, AircraftPosition = 1, ...`) so an accidental reorder becomes a compile-time-visible diff instead of a silent runtime incompatibility. The same applies to `LocalNode.MESSAGE_ID` and `JoinResult`/`LoginResult`.
- There is effectively unbounded room to grow: `MESSAGE_ID` is a 16-bit value with roughly 40 of 65536 slots used, and eight enum values (§8.12) are already reserved-but-unused, so there's no pressure to reuse or overload existing IDs.

### 9.2 Prefer length-prefixed/TLV extensions over EOF-sensing for new *optional* fields

The `reader.PeekChar() != -1` idiom (§5) is fine for a message that only ever grows a strictly-ordered tail of fields controlled from one place, but it has sharp edges worth avoiding for future work:

- It silently breaks if two independent features each want to add an optional trailing field and their inclusion becomes conditional (not just version-gated) — there's no way to skip field N while still reading field N+1, because "more bytes remain" doesn't tell you *which* optional field is next.
- It only works because these particular messages are unreliable/single-segment; if a future optional-tail field were ever added to a message that also goes through the guaranteed-segmentation path, a partially-received multi-segment reassembly could look like "end of stream" prematurely at a segment boundary rather than genuine end-of-message. (Today's guaranteed messages don't use `PeekChar`, so this isn't a live bug — just a trap for later.)
- The `Notes` message's `Length`-prefixed, `Type`-tagged inner records (§8.9) are the better template: unknown `Type` values are already skipped via `reader.ReadBytes(length)` rather than crashing or misaligning the stream. Generalizing this "16-bit length prefix in front of anything whose format might change" pattern to other frequently-extended messages (`SharedData`, `Status`) would let a receiver skip a whole new/unknown block cleanly instead of relying on trailing-field ordering discipline.

### 9.3 Give internal/session messages the same version discipline application messages already have

`Join`, `SharedData`-adjacent identity data, and friends currently rely on the fixed transport `Version` (`0x520B`) for compatibility and have no per-message `DataVersion` the way application messages do (§5). Concretely:

- Add a `DataVersion` (or even just reuse `Sim.VERSION`) to the `Join`/`JoinReply` payload so version can be negotiated **before** either side has committed to full mesh membership, instead of the current approach where a node only learns a peer's version indirectly, later, from the first `SharedData` broadcast (`node.dataVersion`) — which today is stored but not actually used to gate anything.
- More generally, consider a small bitmask "capabilities" field in `Join`/`JoinReply`/`SharedData` (a few spare bytes now costs nothing and headroom is cheap) so future optional features can be negotiated explicitly (e.g. "I understand the new engine-state messages", "I support IPv6 Nuids") rather than inferred from a single monotonic version number, which forces every feature to be strictly ordered along one timeline even when two features are actually independent.

### 9.4 Security hardening (independent of wire-format compatibility, but worth planning alongside it)

These aren't strict "extend the protocol" changes, but any protocol version bump is a natural time to also address them, and doing so compatibly is easier if planned for:

- Replace `HashString`/`HashPassword`'s DJB2-style hash with a real KDF (e.g. PBKDF2/Argon2) for the password/login hash, and/or move to a proper authenticated handshake (e.g. SRP or a pre-shared-key-derived session key) so a captured `Join`/`Login` packet can't be brute-forced or replayed. This can be rolled out compatibly by having `JoinReply`/`LoginFail` (or a new negotiation message per §9.3) advertise which hash scheme(s) the creator will accept, letting a new client fall back to the legacy hash only when talking to an old creator.
- Consider an optional per-session symmetric-encryption mode (e.g. AES-GCM with a key derived from the session password) for payload confidentiality/integrity, negotiated the same way — a session without a password would stay in today's plaintext mode by default, so this is purely additive.
- A per-node monotonic sequence number (even just a `ushort` added to the transport header under a new flag bit) would let a receiver detect and drop obviously-replayed or wildly-out-of-order datagrams, independent of the existing `GuaranteedId` mechanism which only dedupes *retransmits of the same message*.

### 9.5 IPv4-only `Nuid`

Supporting IPv6 peers is the one change on this list that can't be done as a pure tail-append, because `Nuid` (§3) is embedded by value, twice, in the fixed 21-byte transport header that *every* datagram starts with — there's no spare room to widen it in place without breaking the fixed header offsets every existing build assumes (`DATA_OFFSET = 21` is relied on throughout `Node.cs`). The realistic compatible path is a new transport header version: bump the fixed `Version` constant (`0x520B` → something else) to mean "this datagram uses the wider/variable-length address header", have `ReceiveMsg` branch on that constant to pick the header layout, and keep emitting the legacy `0x520B` header (IPv4-only) by default until a session's members have all advertised IPv6 support via the capabilities mechanism in §9.3. This is a substantial change and should be scoped as its own project rather than folded into an incremental field addition.

## 10. Quick reference — constants

| Constant | Value | Meaning |
|---|---|---|
| `Network.DEFAULT_PORT` | 6112 | Default UDP port |
| `LocalNode.VERSION` | `0x520B` | Fixed transport/handshake constant (first 2 bytes of every datagram) |
| `Sim.VERSION` | 21008 | Current application data-format version, sent in every non-internal message |
| Minimum accepted `dataVersion` | 10014 | Messages tagged below this are dropped outright |
| `MAX_GUARANTEED_DATA` | 1000 bytes | Max payload per guaranteed-delivery segment |
| Guaranteed resend interval | 2 s | `DoGuaranteedMessages()` |
| Guaranteed out/in expiry | 180 s / 240 s | `GUARANTEED_OUT_EXPIRE_TIME` / `GUARANTEED_IN_EXPIRE_TIME` |
| `PULSE_INTERVAL` | 1 s | Keepalive broadcast interval |
| `PATHFINDER_INTERVAL` | 5 s | NAT-traversal/routing discovery interval |
| Node expire / reestablish | 30 s / 3 s | `EXPIRE_TIME` / `REESTABLISH_TIME` |
| `MAX_NODES_PER_DEVICE` | 32 | Per-device join limit |
| `MAX_ROUTING_NODES` | 10 | Concurrent relayed peers per node |
| `MAX_PATHFINDER_NODES` | 100 | Targets per `Pathfinder` request |
| `MAX_HUB_LIST_MESSAGE` | 25 | Hubs per `HubList` datagram |
| `MAX_USER_POSITIONS` | 20 | Users per `UserPositions` datagram |
| `MAX_INTEGER_VARIABLES` / `MAX_FLOAT_VARIABLES` / `MAX_STRING8_VARIABLES` | 100 / 100 / 80 | Variables per variable-sync datagram before it's split |

## 11. Source map

| Concern | File |
|---|---|
| Transport framing, `Nuid`, guaranteed delivery, Join/Pulse/Pathfinder mesh logic | `JoinFS/Node.cs` (`LocalNode` class) |
| Application `MESSAGE_ID` enum, all `Send*`/`Write*Message` producers, `ReceiveMsg` application dispatch, hub/session/discovery logic | `JoinFS/Network.cs` |
| Wire structs and version-gated field readers for position/velocity and variable dictionaries (`Sim.Read`/`Sim.Write`) | `JoinFS/Sim.cs` |
