# JFP2's first released wire

**Design, 2026-10-07. Design approved by the owner on 2026-10-08 (§10); implemented in stages,
status in the table at the end.**
Scope: what JFP2's first released wire (26.6) must look
like so that the next few years of work (review `docs/jfp2-standalone-review.md`, items B1–B7) can
be added without a second protocol version or a compatibility layer. Status table at the end.
Line numbers cited as `Jfp2Plugin.cs:NNN` refer to that file as of commit `4918143`, before stage 2
split it into `Jfp2Plugin.cs`, `.Handshake.cs`, `.Relay.cs` and `.Codec.cs`.

## Summary

JFP2 is not frozen until 26.6 ships, and 26.6 waits until its wire is right. Most of today's wire
already extends well: the magic and ProtoMajor bytes, the per-class offers, the TLV area, the
capability bits and the rule that a new flag needs an agreed capability. Five things must change
before the release, and a few rules must be written down:
- **Node names.** Every node reference on the wire (Hello, HelloAck, the Forwarded extension)
  becomes an 8-byte tagged name: a kind byte plus 7 bytes. Kind 0 is a legacy id, kind 1 a random
  key, kind 2 is reserved for B7 key-pair ids, kind 255 for group names (B4 "all members"). Release 1
  sends and resolves only kind 0, but parses and relays every kind, so keys, IPv6-only nodes and
  key-pair ids later need no new layout and no rewriting at relays. Cost: 2 bytes per relayed
  datagram.
- **Guaranteed ids** are unique per (origin, final target); a relay that re-sends on someone's
  behalf keeps the origin's id and deduplicates per (origin, target, id). This fixes a collision at
  translating relays today.
- **Field limits.** Every string of every v1 codec has a byte limit, sized so that every message
  with every header fits a **1,200-byte** datagram (IPv6's minimum MTU is 1,280).
- **Observed endpoint.** Every HelloAck says from which address and port the Hello arrived, in a
  `WireEndPoint` (family, address, port) that replaces the unused `PeerKey`. Release 1 builds report
  it, so later builds can learn their public endpoint and NAT behaviour from release-1 peers.
- **Defined handling of the unknown:** `Result` values, name kinds, offer partitions, TLV tags and
  lengths, flags; reserved capability and class numbers released until a design assigns them.

Everything else is reserved room that costs nothing now: a per-hop security flag with a counter and
a MAC trailer (B7), an extension-chain flag, the `Extended` class escape, coalescing. Review items
D2 and D1 ship in 26.6 too. After the release, identity in the core moves to the 8-byte name as a
pure refactor, and the random key follows it, so that the key is an identity from the day it
exists. Cookies, probes, pre-membership sessions and the mesh over JFP2 are B3 and are sketched in
an appendix that binds nothing.

## First-release wire at a glance

| Element | Decision | Reason | Cost |
|---|---|---|---|
| Magic `0xFA`, dispatch on byte 0 | keep | Shares the socket with legacy (`0x0B`); a future protocol takes another magic | — |
| ProtoMajor byte, unknown → drop | keep | The escape for a breaking redesign | — |
| Flags: Guaranteed, Forwarded, Internal | keep | Hot extensions at fixed offsets | — |
| Flag bit 2 Coalesced | reserve | Format sketched (appendix A.6); needs a partition bit per sub-message | — |
| Flag bits 4–7, a bit per extension | keep, rule written | Each new bit comes with a capability; candidates: 4 Authenticated (B7), 7 extension chain | — |
| Session ids, u16 each | keep | Room for 65,535 neighbours per node; authenticity comes from B7, not width | — |
| Class u8 per partition, 255 = `Extended` | keep; 255 reserved, not implemented | 255 classes per partition last years; an extended class could not be offered in a u8 offer anyway | — |
| Internal classes 2–8 (mesh) | release the reservation | B3 needs another set (Login, LoginFail, AddNode, ...); assigned when designed | — |
| Guaranteed extension (id u16, index u8, count u8) | keep the layout | Count 255 × 1,100 bytes is ample for B6 | — |
| Guaranteed ids | **change**: unique per (origin, final target); relays keep the origin's id and key on (origin, target, id) (§2.4) | Fixes a collision at translating relays; avoids wrap at large hubs; ready for fan-out | relay tables re-keyed |
| A guaranteed segment that is dropped | **change**: never acknowledged | Today count > 1 is acked, then dropped (`Jfp2Plugin.cs:762`, `:768`) | — |
| `GuaranteedDone` | change: always 3 bytes | Drop the 2-byte form kept for dev builds | less code |
| Forwarded extension: two 7-byte legacy ids | **change**: two 8-byte names (§3) | Later kinds without a new layout or relay rewrite | +2 bytes per relayed datagram |
| Hello Node TLV: one 7-byte legacy id | **change**: `Names`, any number of names (§3, §4.3) | A node has several names (legacy id, key) | +1 byte |
| Hello fixed fields | keep | ProtoMajor range, capabilities, id, result, offers carry the next years | — |
| Keepalive Hellos with the full offer list | keep, deliberately (§4.1) | Each keepalive re-resolves the agreement, so a restarted peer's new offers are noticed | about 100 bytes per neighbour every 5 s |
| String fields of v1 codecs | **change**: byte limits per field (§4.6) | Every message fits 1,200 bytes; no u16 wrap, no buffer overflow | text cut at a character boundary |
| `Result` values | change: rule for unknown values (§4.2) | A later refusal reason must not read as "legacy only" | — |
| Offer entry (partition u8, class u8, min u8, max u8) | keep; unknown partition ignored | Leaves room for another class space | — |
| Capability u64 | keep; release the four reserved meanings | Assigned only with the design that needs one | — |
| TLV (tag u16, length u16) | keep, rules written (§4.4) | Unknown tags skipped; short values ignored, long ones read up to the known prefix; first copy counts | — |
| `ObservedEndPoint` TLV in HelloAck | **add** (§7) | B2's STUN function from day one, also from release-1 peers | 11–23 bytes per HelloAck |
| `PeerKey` (family, address, port, local) | **replace** by `WireEndPoint` (§7.1) | Identity belongs to names; an address is only an address | code removed |
| Datagram size | **rule**: ≤ 1,200 bytes, enforced by field limits (§4.5, §4.6) | IPv6 minimum MTU is 1,280 | — |
| Security (MAC, counter, encryption) | reserve: flag bit 4, per hop (§2.5) | Relays decode to translate, so protection is per hop; fits behind a capability | — |
| Node key and its store | not in release 1 | Not a wire element: lands after the core migration (stage 11) | — |
| Admission, cookie, probe | not in release 1 | Nothing to do with a non-member until B3; a release-1 node ignores such Hellos, which is the right answer | — |

## 1. Ground rules

- **Until 26.6 ships** the wire changes freely. Daily dev builds are no constraint: testers run the
  same daily build. The JFP2 golden tests (`HandshakeGoldenTests`) are updated by the stages that
  change the wire on purpose, never to make a failing test pass.
- **From 26.6 on** the spec's evolution rules bind: Hello and HelloAck keep their envelope, fixed
  fields and offer layout (spec §5.2); new things go into TLVs, new classes, new schema versions and
  capability-gated flags. The wire in the main body of this document binds once approved; §5–§6
  (core identity, the key) are designs for stages after the release; the appendix binds nothing.
- **Legacy stays frozen** (`Legacy/Fixtures/*.hex`). Released v26.5 builds speak only legacy and
  drop every `0xFA` datagram.
- **The receiver drops what it cannot place**, and the sender never sends what the receiver did not
  agree: a ProtoMajor or flag it does not know (spec §4.1, §4.2), a class it did not agree. A
  receiver **skips** what declares its own size: TLVs, offers of an unknown partition, names of an
  unknown kind (compared as bytes).
- **No release-1 code for B3 and later**, except parsing rules that later builds depend on.

## 2. Envelope

The fixed 8 bytes (spec §4.1) stay: magic, ProtoMajor, flags, sender and recipient session ids,
class. Then the extensions in flag-bit order, then the payload, then trailers (none in release 1).

### 2.1 Flags

| Bit | Name | Release 1 |
|---|---|---|
| 0 | Guaranteed | 4-byte extension (§2.4) |
| 1 | Forwarded | 16-byte extension: origin name, target name (§3) |
| 2 | Coalesced | reserved; dropped |
| 3 | Internal | class is in the internal partition |
| 4–7 | — | unassigned; dropped |

**One bit per extension, or a chain?** One bit per extension keeps the hot extensions at fixed
offsets and the parse branch-free. A chain (type, length, value items) lets a sender add header
fields that a receiver may skip. But every foreseen header addition changes how a datagram must be
handled (security, segmentation, a hop limit for multi-hop relaying), so it would be "must
understand" anyway and need a capability. Four free bits cover the foreseen ones (bit 4 for B7's
`Authenticated`, appendix A.5). **Decision:** keep one bit per extension; if the bits run out, one
of them becomes "an extension chain follows", defined with a capability like any other flag. No
release-1 code.

**Rule** (spec §4.2, unchanged): a receiver drops a datagram with a bit it does not support; a
sender sets a new bit only toward a neighbour that agreed the capability defining it, never on
Hello or HelloAck; a relay that forwards a datagram byte for byte is a sender too.

### 2.2 Session ids

u16 each, random per node, `0` = none (a first Hello; a HelloAck that creates no session, appendix
A.2). A node can hold 65,535 sessions; the largest foreseen hub has a few hundred. To inject into a
session, an off-path attacker must guess the pair (sender id, recipient id), 32 bits; a HelloAck is
matched on the recipient id alone, 16 bits, which is why an observation also requires the expected
source endpoint (§7.3). This is not authentication and does not need to be (§2.5). **Keep.**

### 2.3 Classes

u8 per partition. Application classes 0–9 are used; internal 0 Hello, 1 HelloAck, 9
`GuaranteedDone`. The internal numbers 2–8 were reserved for a mesh whose message set B3 will
design afresh (Login, LoginFail, AddNode and probably a challenge are missing from it); the
reservation is released and B3 appends. `255` stays reserved as `Extended`: it is never assigned,
and a class beyond 255 would also need an offer form other than the u8 in an offer entry (an offer
TLV, behind a capability), so nothing in release 1 needs to parse it. A sender only sends classes
the receiver offered, so a release-1 node never receives one.

### 2.4 Guaranteed extension

GuaranteedId u16, index u8, count u8. **The layout stays.**

**Segments (B6).** 255 segments of up to 1,100 payload bytes is 280 KB, more than any message
JoinFS sends. B6 enables segmentation behind a capability, so a release-1 node never receives
count > 1 from a later build. If it does receive one, it drops it **without acknowledging it**:
today it acknowledges first and then `Reassemble` refuses it (`Jfp2Plugin.cs:762`, then `:768`), so
the sender believes a message was delivered that never was.

**The id rule** (binding from release 1). A guaranteed id is unique per **(origin, final target)**
within the 30 s duplicate window. One message to several targets may share one id, as legacy
broadcasts do.
- **Final target:** deduplicates and reassembles per (origin, id), its own name being the target;
  unchanged.
- **Relay that translates** (decodes and re-sends: to legacy or another JFP2 schema version, later
  B4 fan-out): deduplicates and reassembles per (origin, final target, id), not (origin, id) as
  today (`Jfp2Plugin.cs:960`, `IsDuplicate(origin, id)`, and `Reassemble`). That code runs on every
  translation, to legacy too, which is the common case today. Otherwise one origin's message with
  one id for two targets reaches only the first.
- **A translating relay acknowledges upstream with a Forwarded `GuaranteedDone`**: origin = the
  final target, target = the original sender. Today it sends a plain ack (`Jfp2Plugin.cs:959`,
  `SendGuaranteedDone(..., null)`), which the sender matches by (id, index, hop), clearing every
  entry that matches (the fallback branch of `Jfp2Reliability.Acknowledge`). With one id for two
  targets, the first ack would clear both entries, and a lost copy for the second target would never
  be resent. With the Forwarded ack the sender matches (origin, target, id, index) exactly, and the
  fallback branch is deleted.
- **A relay re-sending on someone's behalf keeps the upstream id**; it does not draw one from its
  own counter (`Jfp2Reliability.NextId`). Today it does, and the final target then deduplicates the
  relay's id under the origin's name: it can collide with an id the origin used for a message of
  its own, which is then suppressed as a duplicate. Keeping the id also lets the relay match the
  downstream ack to the upstream message without a table.
- **Pending table** (`Jfp2Reliability`): keyed (origin, target, id, index) instead of (target, id,
  index), since a relay now holds ids it did not choose. A forwarded `GuaranteedDone` names both
  origin and target, so it finds its entry directly.
- **Wrap.** A sender may keep one counter for everything (as now) or one per target; either way the
  rule holds as long as one counter does not wrap within 30 s, which per-target counters guarantee
  at any hub size.
- Downstream of a translation to legacy, delivery is hop by hop with legacy's own ids, as today
  (spec §7.7).

**`GuaranteedDone`.** Payload is always id u16 + index u8. Release 1 drops the acceptance of a
2-byte ack, which only dev builds sent.

### 2.5 Room for per-datagram security (B7)

Nothing is built in release 1; this checks that it fits. Protection is **per hop**: a relay
decodes to translate between versions and protocols (spec §7.7), so it must read every payload,
and end-to-end confidentiality does not fit JFP2's relaying. Each relay checks what it receives and
protects again what it sends, with the key of each hop's session.
- **Flag bit 4 `Authenticated`**, set only toward a neighbour that agreed its capability.
- **Header extension**, after the Forwarded extension: an 8-byte send counter per session and
  direction, never reused; the receiver keeps a replay window per session.
- **Trailer**, after the payload: a 16-byte tag. With integrity only, an HMAC-SHA256 truncated to 16
  bytes over the associated data and the payload; with encryption, the payload is encrypted with an
  AEAD (for example ChaCha20-Poly1305: nonce = 4 bytes of direction, 0 or 1, then the 8-byte
  counter) and the tag is the AEAD's.
- **Associated data, exactly:** every byte of the datagram from offset 0 up to the end of the last
  header extension, as sent on this hop: the fixed 8 bytes (with this hop's session ids and flag
  bit 4 set), the guaranteed extension if present, the Forwarded extension if present, and the
  counter. A relay rewrites the session ids, so it must check the incoming tag before and compute
  the outgoing one after the rewrite.
- 24 bytes per datagram; the field limits leave room for it (§4.6).
- **Handshakes** cannot use the flag (Hello and HelloAck never carry new flags, spec §4.2), so B7
  protects them through a TLV: the public key, and a signature or MAC over the handshake payload
  that precedes it, bound to the responder's cookie or nonce (appendix A.5).

## 3. Node names

### 3.1 Format

Every node reference on the JFP2 wire is a **name**, 8 bytes:

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Kind |
| 1 | 7 | Value, by kind |

| Kind | Value | Status |
|---|---|---|
| 0 | Legacy id: ip u32 LE, port u16 LE, local u8 (the legacy header's layout) | release 1 |
| 1 | Random key, 56 bits, not all zero (§6) | sent from stage 11 |
| 2 | Key-pair id: first 7 bytes of SHA-256 of the node's public key | reserved for B7 |
| 3–254 | — | unassigned |
| 255 | Group name: `FF 00 00 00 00 00 00 00` = all members of the session (B4 fan-out) | reserved |

All zero is "no node". Two names are equal when their 8 bytes are equal, whatever the kind.

### 3.2 Where names appear

- **Hello and HelloAck:** TLV `Names` (§4.3) lists the speaker's names.
- **Forwarded extension:** origin name (8), target name (8). It replaces two 7-byte legacy ids, so
  a relayed datagram grows by 2 bytes (a relayed Position: 127 bytes instead of 125).
- From B3, in mesh and directory payloads (appendix A.3).

### 3.3 Handling names a node does not know

- **Comparing** never needs the kind: a relay forwards to the neighbour that owns the target name,
  a receiver consumes when the target is one of its own names.
- **Resolving** (finding the peer a name belongs to): release 1 resolves **kind 0 only**, by
  converting it to a legacy id. Every other kind is dropped. Nothing more is needed: by the naming
  rule below, no node can send a 26.6 node a kind-1 name it could resolve, since a node is named by
  its key only when it has no legacy id, and such a node is never a 26.6 node's peer. Later builds
  resolve other kinds through the core's alias index (§5.3), fed by B3 membership.
- **Unresolved:** the datagram is dropped and logged at network level, and a guaranteed one is
  **not acknowledged**, so its sender retries until membership catches up (or gives up after
  180 s).
- **Group names** are only sent through a hop that agreed the fan-out capability (B4), so a
  release-1 relay never receives one; if it does, the target does not resolve and it is dropped.

### 3.4 Why now, and options considered

| Option | Verdict |
|---|---|
| Keep 7-byte legacy ids; add an 8-byte form behind a capability and a flag later | Rejected. Every relay between a new and a release-1 hop would have to rewrite the extension or drop the datagram, forever. |
| Names in release 1, legacy kind only | **Chosen.** Release 1 is wire-ready for keys, IPv6-only nodes and key-pair ids; later kinds need no flag, capability or rewrite. 2 bytes per relayed datagram. (Fan-out still needs work at the hub for 26.6 members, appendix A.4.) |
| Longer names (16 bytes) for strong key-pair ids | Rejected. A name is a handle, not a credential: B7 authenticates by the public key and a signature in the handshake (A.5). |
| A self-describing name (kind + length) | Rejected. Fixed size keeps the relay extension at fixed offsets. |

**Naming rule** (which name a node writes for another): a node is named by its kind-0 name while it
has a legacy id, otherwise by its key. Every node can apply it without knowing what others know,
and a release-1 node can resolve every name it is sent, because until B3 every member has a legacy
id. A node without one (IPv6-only, JFP2-only) is named by its key, which other nodes learn from B3
membership; a release-1 node cannot resolve it and drops such traffic, as it could not have carried
it over legacy either. When legacy retires and nodes stop having legacy ids, the rule moves to keys
by itself.

## 4. Handshake

### 4.1 Fixed fields and offers

Unchanged: ProtoMajorMin/Max, capabilities u64, `SelfAssignedId` u16, `Result` u8, `OfferCount`
u16, offers of 4 bytes (partition u8, class u8, min u8, max u8). They carry the foreseeable work:
a major version is negotiated inside a ProtoMajor-2 Hello (spec §5.2); schema versions per class go
up to 255; B3's mesh classes are offered in the internal partition.

**Keepalives carry the full offer list, deliberately.** A keepalive is a full Hello every 5 s, and
every Hello and HelloAck resolves the agreement again (spec §5.3), so a peer that restarted with
another build is noticed within one keepalive and the router stops sending it classes it no longer
takes. A short keepalive would need a second mechanism for that. The cost is about 100 bytes per
neighbour every 5 s: 10 KB/s each way for a hub with 500 neighbours.

Rules written now:
- **Partition byte:** 0 application, 1 internal; an offer with another value is skipped, which
  leaves room for another class space.
- Hello, HelloAck and `GuaranteedDone` are never offered; they are always version 1.
- **Capabilities:** none assigned in release 1. The spec's four reserved meanings (Coalescing,
  QuantizedPosition, Ipv6Peers, SelectiveAck) are released: PositionV2 is chosen by schema version,
  names make `Ipv6Peers` unnecessary, and the other two are assigned with their designs. No build
  has ever advertised a capability (`LocalCapabilities = Capability.None` since `43b4360`).

### 4.2 `Result`

| Value | Meaning |
|---|---|
| 0 | Accepted |
| 1 | No compatible ProtoMajor |
| 2 | Not admitted: no session was created (B3, appendix A.2) |
| 3–255 | unassigned |

**Unknown values.** Release 1 treats any value other than 0 like 1 does today, marking the peer
`AssumedLegacy` for 30 s (`Jfp2Plugin.cs:844-850`). That would make a later refusal reason, say "too
many sessions, ask again later" from a large hub, look like "this node does not speak JFP2". The
rule becomes: **a non-zero `Result` means "no session now"; the asker retries after the 30 s
cooldown and still reads the HelloAck's TLVs** (an observed endpoint, later a cookie or a
redirect). Routing falls back to legacy meanwhile, as it does for any peer without a session. The
logged reason is the value, not "legacy-only peer".

### 4.3 TLVs

| Tag | Name | In | Value |
|---|---|---|---|
| 1 | `Names` | Hello, HelloAck | Any number of names (§3), 8 bytes each, the speaker's, preferred first. Release 1 sends one, kind 0. |
| 2 | `Build` | Hello, HelloAck | Printable ASCII, at most 64 bytes; a receiver reads the first 64 bytes and drops other characters (unchanged) |
| 3 | `ObservedEndPoint` | HelloAck | `WireEndPoint` (§7.1): the source of the Hello this answers |

`Names` replaces the 7-byte `Node` TLV. A receiver uses the names it can resolve (kind 0 in release
1) and skips the others; a handshake with none it can resolve to a mesh member is ignored, as a
Hello without `Node` is today. A node lists its key (kind 1) next to its legacy id from stage 11;
that is new TLV content, not a new layout.

### 4.4 TLV rules

- Tag u16, length u16, unchanged; unknown tags are skipped by length.
- **Short value:** shorter than its tag needs (a `Names` under 8 bytes, a `WireEndPoint` shorter
  than its family needs): the TLV is ignored.
- **Long value:** read up to the prefix the receiver knows and skip the rest. That is how a later
  build extends a value: `Names` is read in whole names (a trailing remainder under 8 bytes is
  skipped), `Build` up to 64 bytes, `ObservedEndPoint` up to its family's size.
- **Repeated tag:** the first copy counts. (Today `Tlv.ReadAll` keeps the last, `Negotiation.cs:82`.)
  A list goes inside one value.
- Tags are appended and never reused, like classes.

### 4.5 Datagram ceiling

No JFP2 datagram exceeds **1,200 bytes**: the IPv6 minimum MTU is 1,280, less 48 bytes of IPv6 and
UDP headers, with room for a tunnel. Headers are at most 28 bytes (8 fixed, 4 guaranteed, 16
Forwarded), 52 with B7's 24 (§2.5). So **every v1 payload is at most 1,100 bytes**, which §4.6
guarantees by construction. Handshakes are about 99 bytes (Hello with `Names` and `Build`), far
below. A receiver accepts any size its buffer holds (16 KB today).

### 4.6 Field limits of the v1 codecs

Every string field gets a byte limit (UTF-8 bytes, without the u16 length prefix). The sender cuts
a longer text at a UTF-8 character boundary, never inside a character, so a cut text is still valid
UTF-8. The receiver does not check limits: a v1 message is valid at any size that fits its datagram.
A later schema version of a class may raise its limits once B6 segmentation exists. With these
limits the u16 wrap in `WireText.WriteString` (`(ushort)bytes.Length`) and an overflow of the 16 KB
`payloadBuffer` cannot happen.

| Class | Fixed bytes | String limits (bytes) | Largest payload |
|---|---|---|---|
| Position | 103 | — | 103 |
| Identity | 6 + 9 prefixes = 24 | Callsign 32, Model 256, Livery 256, IcaoType 8, IcaoAirline 8, Registration 32, FlightNumber 16, ClassCode 16, Wtc 8 | 656 |
| VariableSync | 5 + per entry 5 (+ 2 + string) | `String8` value 256: one entry is at most 263 bytes, so a single entry always fits; chunks stay at 1,000 bytes (`VariableSyncMaxPayload`) | 1,000 |
| Event | 12 | — | 12 |
| FlightPlan | 4 + 13 prefixes = 30 | IcaoType 8, Departure 8, Destination 8, Rules 8, Route 512, Remarks 256, Alternate 8, Speed 16, Altitude 16, Callsign 32, Registration 32, IcaoAirline 8, FlightNumber 16 | 958 |
| Notes | 26 + 3 prefixes = 32 | Nickname 32, Callsign 32, Text 768 | 864 |
| Weather, WeatherReply | 2 | Metar 1,024 | 1,026 |
| StatusRequest | 5 | — | 5 |
| Status | 34 + 8 prefixes = 50 | AppVersion 32, AtcAirport 8, Address 128, Name 64, About 512, Voip 128, NextEvent 128, Airport 8 | 1,058 |

The largest, Status at 1,058 bytes, makes a 1,086-byte datagram with every header, 1,110 with B7.
The limits live in each codec as constants, next to the field they bound. Text cut to a limit (a
hub's About, a long note) can differ from what legacy peers receive for the same message.

## 5. Identity in the core

Not wire; it follows the release. It decides how the names of §3 map onto the core's ids.

### 5.1 Today

`NodeId` (`Net/Core/NodeId.cs`) is public IPv4, **bound** port and LAN octet. It occurs about 415
times on 378 lines in 51 files:

| Use | Where |
|---|---|
| Key of core state (15 files in `Net/`) | `PeerDirectory`, `MessageMeta.Sender/Recipient`, `NetworkEvent.Node`, `ObjectStateCache`, `MeshManager`, both plugins, `NetworkSnapshot` |
| Opaque handle (about 30 app files) | `PeerTable.Nodes`, `Sim` owners (`ownerNuid`), shared cockpit, `SimIngest`, forms; `default` = "this node" (`PeerTable.cs:218`) or the recorder (`Recorder.cs:1331` ff.) |
| An address (about 10 files) | `LocalIdentity.MakeEndPoint` and its copy `NetworkSnapshot.MakeEndPoint`; `MeshManager.RegisterNode` (`:327`); `LegacyPlugin.cs:141` (endpoint of a target not in the directory); `HubDirectory` (`HubCount_IP` on `.ip` `:404`, `MakeEndPoint` of HubList entries `:296`, `hubs.dat` `:613`); `UserDirectory`; `LocalId.port` (`SessionForm`, `Program.cs:205`); `XPlaneLink.cs:118` (plugin framing); `NodeId.SameDevice` for `MaxNodesPerDevice` (`MeshManager.cs:295`, `PeerDirectory.cs:149`) |

Persistent app state never stores a `NodeId` as identity (`Log` keeps the peer's `Guid` and IPv4
address, `Log.cs:62-108`); `hubs.dat` stores one as an address.

It fails as an identity: it changes with the public address (`LocalIdentity.cs:31-39`; hubs refresh
it every 3 h, `NetBootstrap.cs:196`), it cannot exist before the public IPv4 is known
(`Network.cs:158`), two CGNAT users with port 6112 and the same LAN octet share one, and IPv6-only
nodes have none.

### 5.2 Options

| | Option | Verdict |
|---|---|---|
| a | Opaque key everywhere; re-key a peer when its Hello names a key | Rejected. A re-key must move core and app state (the sim thread's objects included) at one instant while messages under the old id are queued, and an unauthenticated Hello would trigger it. |
| b | `NodeId` becomes the 8-byte name; a peer's id is the name it was first learned by; `PeerDirectory` keeps an alias index from every name of a peer to its id | **Chosen.** Nothing is ever re-keyed; the app keeps treating ids as opaque; when legacy retires every id is a key, which is (a) without the re-keying. |
| c | Legacy `NodeId` stays; keys live only in the JFP2 plugin | Only as an interim. It cannot represent a node without a legacy id. |

### 5.3 The migration (stage 10, a pure refactor)

1. `NodeId` holds the 8 bytes of a name. `NodeId.FromLegacy(ip, port, local)` and
   `TryGetLegacy(out LegacyNodeId)` convert kind 0 both ways without a table.
2. The old 7-byte struct becomes `LegacyNodeId`, used only by `LegacyPlugin`, `LocalIdentity`,
   `XPlaneLink` (pinned by `XPlaneLinkTests`) and `hubs.dat`.
3. `Valid()`: not all zero, and for kind 0 a non-zero ip (today's meaning). `ToString()`: kind 0 as
   today; kind 1 as `k` and 14 hex digits; other kinds as the kind number and hex.
4. The address uses read the legacy id: `MakeEndPoint(LegacyNodeId, port)`; `LegacyPlugin.cs:141`
   and `HubDirectory.cs:296` skip a node without one; `HubCount_IP` counts per `hub.endPoint`
   address; `SessionForm` and `Program.cs` read `Snapshot.Port`. `SameDevice` compares legacy ids
   (ip and octet); a node without a legacy id is its own device.
5. `PeerDirectory` gets the alias index (name → id), filled from handshakes; `LegacyPlugin`
   resolves every id it decodes through it and writes a peer's legacy id when encoding, leaving a
   peer without one out of every legacy message.
6. Stage 3's `NodeName` merges into `NodeId`: the envelope's names and the plugin's lookups use
   `NodeId` directly.

Every existing test passes unchanged, because every member still has a legacy id and so a kind-0 id.

### 5.4 What it does not fix

A peer learned through legacy keeps its kind-0 id, and legacy cannot tell that a node whose public
IP changed is the same node: in mixed sessions it reappears as a new peer, as today (decision D).
Two CGNAT users with one legacy id stay one confused peer to legacy; JFP2 can only detect it
(§6.4).

## 6. The node key

Not wire (only `Names` content); ships in stage 11, after the core migration, so that it is an
identity from the day it exists (decision A).

### 6.1 Generation and size

56 bits from `RandomNumberGenerator`, not all zero, as a kind-1 name. With 400,000 keys in
existence (100,000 installs, 4 instances each) the chance of any collision is about 10⁻⁶;
collisions are detected anyway (§6.4).

### 6.2 Store

`NodeKeyStore` (`JoinFS/Session/NodeKeyStore.cs`, app thread, created by `Main` after
`storagePath`). `storagePath` (`%LOCALAPPDATA%\<name>`, `Program.cs:759`) is per variant;
instances of one variant share it, and share the per-install `Guid` too (`Program.cs:781-800`).

- **Slots** `node-<slot>.key`, 0–3 (`MAX_INSTANCES` = 4, `Program.cs:29`). An instance opens a slot
  with `FileShare.None` and keeps it open while it runs.
- **Which slot:** the one whose file records this instance's bound UDP port, if it is free;
  otherwise the first free one, which then records the port. Taking slots in start order alone
  would swap keys between a hub on 6112 and a client on 6113 whenever they start in another order.
- **File:** version u8 (`1`), key 8 bytes, port u16 LE. An unreadable file gets a new key.
- **No slot free** (more instances than slots, read-only folder): a key for this run only, logged.
  `MAX_INSTANCES` is not enforced for `dotnet JoinFS-CONSOLE.dll`: the process name counted at
  `Program.cs:728` is `dotnet`.
- **No machine check.** A machine-name hash would change with every Docker container and would not
  tell cloned VMs apart. A copied folder keeps its key; the duplicate is detected (§6.4).
- **Locks:** .NET implements `FileShare.None` on Linux with advisory `flock`, which works between
  JoinFS processes on a local file system. It is unreliable on NFS and SMB shares and through Docker
  Desktop bind mounts, and `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` turns it off: two instances may
  then share a slot and a key, which §6.4 logs.
- **Docker:** the CONSOLE image has no volume for the storage folder today, so a recreated
  container would get a new key. Stage 11 adds a `VOLUME` line for the storage folder to the
  `Dockerfile` (decision F). The minion hub's systemd service keeps its home folder.

`NetBootstrap` takes the key and posts it with the addresses; the network thread's `LocalIdentity`
gets `Key`. `TestNode` gives every node a deterministic key.

### 6.3 Privacy and spoofing

The key is stable on purpose. It links a node across sessions and networks no more than the
per-install `Guid` that every peer already receives (`PeerInfo.Guid`, `StatusUpdate.Guid`). Anyone
can claim any key; B7's kind 2 adds a key pair (appendix A.5). Until then a key is bound only by a
HelloAck that answers our own Hello at the endpoint we asked, never by a Hello alone.

### 6.4 Binding in the JFP2 plugin before B3

A CGNAT legacy-id collision (two strangers with one legacy id) cannot be fixed before B3: legacy's Pulse and PulseResponse keep
moving the shared peer's port between the two nodes (`MeshManager.cs:319` via `:477` and `:489`),
so the JFP2 session keeps being demoted as "route moved" (`Jfp2Plugin.cs:336`). Any rule that binds
JFP2 sessions by key would flap with it. So until B3 the plugin only **detects and logs**:
- the key is recorded or replaced only from a HelloAck that answers our Hello (matched id, the node
  asked for, `from` equal to the probed endpoint);
- a different key on a session that is unverified or lapsed is a restart: replaced, logged at
  network level;
- a different key on a verified, fresh session is a collision: logged once per pair at event level,
  with both keys and endpoints, and nothing else changes;
- the same key on two legacy ids (a clone, an IP change, or a shared slot): logged.

A node never changes its own key because another node shows it: that is trivially spoofable (B7).

## 7. Observed endpoint

### 7.1 `WireEndPoint`

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Family: `4` IPv4, `6` IPv6; others unassigned |
| 1 | 4 or 16 | Address, network byte order |
| 5 or 17 | 2 | Port, u16 LE |

7 or 19 bytes; in a TLV the TLV length bounds it, in a list each entry is preceded by a u8 length.
An IPv4-mapped IPv6 address (the future dual-stack socket) is written as family 4. It replaces
`PeerKey` (spec §4.8, `Envelope.cs:417`), never used, which mixed identity (`Local`) with address.

### 7.2 What is sent

Every HelloAck carries `ObservedEndPoint`: the UDP source of the Hello it answers, as received.
11 bytes (IPv4) on a datagram that goes every 5 s per neighbour.

### 7.3 What the receiver does (release 1)

1. **Plugin** (network thread): counts an observation only from a HelloAck that answers our own
   Hello: addressed to the session's id, from the endpoint the Hello went to (`from ==
   ProbeEndPoint`; the session id alone is 16 bits, constant for the session, and guessable off the
   path), and naming the node asked for or the endpoint's occupant. It reports changes per
   neighbour through `IProtocolHost.EndPointObserved(reporter, endPoint)`.
2. **Core** raises `NetworkEventKind.EndPointObserved` (`NetworkEvent` already has `Node` and
   `EndPoint`).
3. **App** (`Network` drains the event): `NetBootstrap` hands it to `ObservedEndPoints`
   (`JoinFS/Session/ObservedEndPoints.cs`, plain C#), which keeps the latest observation of up to 8
   reporters, ignores LAN observations (our /24, private ranges), and classifies:

| Class | Evidence |
|---|---|
| `Public` | the observed address is ours and the port is the bound port |
| `PortPreserving` | the port is the bound port |
| `Translated` | another port, the same for every reporter |
| `EndpointDependent` | different reporters see different ports or addresses: the node depends on relays (typical of CGNAT) |
| `Unknown` | no public observation yet (two needed for `EndpointDependent`) |

The class and the observed endpoints are logged at event level when they change, next to the HTTP
address: field data for review items D and B4. Nothing else uses them in release 1 (decision E).
The observed address never replaces the public address in release 1 (decision C): the
HTTP lookup (`NetBootstrap.cs:25-26`) stays the source of the legacy id, because a join to a
release-1 or legacy hub needs it first. Using an observation to bootstrap a node whose lookup
failed needs a probe answered by a non-member, which is B3 (appendix A.2): today a remembered
address already makes a node `Ready` (`NetBootstrap.cs:64-68`), `DownloadAll` blocks in the
`Network` constructor (`Network.cs:79`), and only hubs retry the lookup (`NetBootstrap.cs:196`),
so the trigger needs its own design.

## 8. Interop

Before 26.6 ships, the stages that change the wire (3–7) break JFP2 between a build before and a
build after them; the legacy fallback carries the session meanwhile, exactly as with a v26.5 peer.
Testers run the same daily build.

From 26.6 on:

| Pair | What happens |
|---|---|
| v26.5 ↔ any | Legacy only, unchanged: `0xFA` datagrams are dropped by v26.5; no stage touches the legacy wire. |
| 26.6 ↔ stages 10–12 | No wire change. Stage 11 adds a kind-1 name to `Names`, which 26.6 skips (it resolves kind 0 only), while the kind-0 name still binds the session. |
| 26.6 ↔ B3 and later | B3 builds name nodes with legacy ids by kind 0, so 26.6 resolves every name it is sent until a node without a legacy id appears; such a node's traffic is dropped by 26.6, which could not have reached it over legacy either. New flags (coalescing, segmentation, security, fan-out) go only to hops that agreed their capability, which 26.6 never advertises. Non-zero `Result` values from a later build mean "no session now" to 26.6 (§4.2). A non-member's Hello is ignored by 26.6, which is the answer B3 must expect from a hub that admits no non-members. |
| Relay with one 26.6 hop and one later hop | The Forwarded layout is the same for every build, so the relay forwards byte for byte or translates, as today; it never rewrites names. A translating relay keeps the origin's guaranteed id (§2.4) whichever side is 26.6. The one exception is B4 fan-out (appendix A.4). |

Topologies that work today keep their tests: the shared endpoint of network-plugin-architecture
§2.12 (`Jfp2RelayTests`), translation both ways, restart recovery
(`PeerThatForgetsItsSessions_IsRecoveredByTheKeepalive`), version skew (`Jfp2VersionSkewTests`).

## 9. Stages

Each stage is an independently testable step: it is reviewed and committed on branch
`jfp2-node-identity`, keeps every existing test green (golden JFP2 constants change only where a
stage changes the wire on purpose; the legacy fixtures never), and builds in all six
configurations. Tests: `dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug -p:Platform=x64`.

### Before the wire freezes (26.6 waits for these)

1. **Decision record and spec.** Docs only.
   - `docs/network-plugin-architecture.md` §2.13 "JFP2 as the successor protocol": JFP2 replaces
     legacy, which retires when the A4 build counts say so; until B3 legacy is the membership layer;
     the release-1 wire of this document; security is a goal (B7). It supersedes, with a pointer at
     each: §2.4's "JFP2 stays a link upgrader", §2.4.1's "Not now", §2.10 item 3.
   - `docs/reference/jfp2-protocol.md`: §2 non-goals ("indefinitely", security), §3 last paragraph,
     §7.1, §7.2's "Because every node speaks legacy"; the frozen-handshake rule marked as binding
     from 26.6. Each later stage writes its own spec sections.
   - `.claude/CLAUDE.md`'s "Every node speaks legacy" stays until B3 makes it untrue; §2.13 records
     the direction.
2. **Partial-class split of `Jfp2Plugin`** (the safe half of review item C3). `partial class
   Jfp2Plugin` across `Jfp2Plugin.Handshake.cs` (handshake, keepalive, occupants),
   `Jfp2Plugin.Relay.cs` (next hop, relay) and `Jfp2Plugin.Codec.cs` (encoder, decode); members
   move, nothing else changes. Tests: all existing, unchanged.
3. **Names** (§3). `NodeName` struct (`Net/Protocols/Jfp2/NodeName.cs`) with kind-0 conversion;
   `Names` TLV in place of `Node`; 16-byte Forwarded extension; `RelayNuid` deleted; kind-0-only
   resolution and the no-ack rule of §3.3.
   - Tests: `HandshakeGoldenTests` constants rewritten for `Names` (a deliberate wire change);
     `EnvelopeTests` — `Forwarded_CarriesTwoEightByteNames`; `Jfp2PluginTests` —
     `ForwardedWithAnUnknownNameKind_IsDroppedAndNotAcknowledged`,
     `HelloWithANameOfAnUnknownKindBesideALegacyName_BindsByTheLegacyName`; all `Jfp2RelayTests`
     pass unchanged.
   - Docs: spec §4.5, §5.5, new §4.9 "Names"; `JoinFS/util/wireshark/joinfs.lua`.
4. **Handshake, envelope and TLV rules** (§2.3, §4.1–§4.4). Unknown `Result` values; the partition
   byte; capabilities and internal classes 2–8 released; the TLV length and repetition rules (first
   copy counts, `Names` any multiple of 8, `Build` read up to 64 bytes); `GuaranteedDone` always 3
   bytes; `Jfp2Profile` gains the capabilities it advertises (today a constant, `Jfp2Plugin.cs:46`)
   so skew tests can vary them.
   - Tests: `Jfp2PluginTests` — `HelloAckWithAnUnknownResult_MeansNoSessionNow_NotLegacyOnly`,
     `OfferOfAnUnknownPartition_IsSkipped`, `TwoByteGuaranteedDone_IsIgnored`;
     `NegotiationTests` — `RepeatedTag_TheFirstCopyCounts`, `ShortValue_IsIgnored`,
     `LongValue_IsReadUpToTheKnownPrefix`, `Names_WithATrailingRemainder_ReadsTheWholeNames`.
   - Docs: spec §4.2–§4.4, §5.2–§5.5.
5. **Guaranteed-id rule** (§2.4). Deduplication and reassembly at a relay keyed (origin, final
   target, id); a relay that re-sends keeps the upstream id; a translating relay acknowledges
   upstream with a Forwarded `GuaranteedDone` (origin = final target, target = sender);
   `Jfp2Reliability`'s pending table keyed (origin, target, id, index), and the fallback branch of
   `Acknowledge` deleted; a segment that is dropped is never acknowledged.
   - Tests: with a relay translating to a legacy target (`Jfp2RelayTests`) and to a JFP2 target of
     another version (`Jfp2VersionSkewTests`) —
     `TranslatingRelay_SameIdToTwoTargets_DeliversBoth` (one legacy and one JFP2 target pair each),
     `TranslatingRelay_AckForOneTarget_DoesNotClearTheOther`, `TranslatingRelay_KeepsTheOriginsId`,
     `TranslatingRelay_DownstreamAck_ClearsTheEntryOfItsOriginAndTarget`; `Jfp2PluginTests` —
     `GuaranteedSegmentOfSeveral_IsDroppedWithoutAck`; `Jfp2RelayTests` —
     `RelayedAckForAnotherOrigin_DoesNotClearTheHubsOwnMessage` passes unchanged.
   - Docs: spec §4.4, §7.7.
6. **Field limits and the 1,200-byte ceiling** (§4.5, §4.6). A limit constant per string field in
   each v1 codec; `WireText` cuts at a UTF-8 character boundary.
   - Tests: `WireTextTests` — `LongText_IsCutAtACharacterBoundary` (multi-byte characters at the
     limit); one test per codec in `JoinFS.Tests/Jfp2/*CodecTests.cs` — `LongStrings_AreCutToTheirLimits`;
     `Jfp2PluginTests` — `EveryJfp2DatagramSent_IsAtMost1200Bytes`, which sends every class with
     maximum-length strings in every field, guaranteed where the class is, through a relay so that
     the Forwarded extension is included, beside `EveryJfp2DatagramSent_IsOneEveryJfp2BuildCanRead`.
   - Docs: spec §4.4, §6 (a limits table per class).
7. **Observed endpoint** (§7). `WireEndPoint` replaces `PeerKey`; `ObservedEndPoint` TLV;
   `IProtocolHost.EndPointObserved`; `NetworkEventKind.EndPointObserved`; `ObservedEndPoints`; the
   log line.
   - Tests: `Jfp2/WireEndPointTests` (IPv4, IPv6, unknown family); `HandshakeGoldenTests` — a
     HelloAck with the TLV; `Jfp2PluginTests` — `HelloAck_TellsTheAskerItsSourceEndPoint`,
     `ObservationFromAnotherEndPoint_IsIgnored`; `Jfp2RelayTests` —
     `BehindNat_TheObservedEndPointIsTheMappedOne` (`AddBehindNat`, a port-changing
     `InMemoryNetwork.Nat`); `Session/ObservedEndPointsTests` — one test per class, and
     `LanObservation_IsIgnored`.
   - Docs: spec §4.8 as `WireEndPoint`, §5.5, §7.4, §7.6 and the mentions of `PeerKey` in §9;
     architecture §5.5, §7 (`NetBootstrap`); `joinfs.lua`.
8. **D2: false same-LAN detection** (review section D; no wire change). `MeshManager.RegisterNode`
   updates only a peer's port, never its address (`MeshManager.cs:319`), and `MakeEndPoint` sends to
   `<our /24>.<their octet>` for any peer behind our public IP. Keep the address a peer was actually
   heard from.
   - Test: `LegacyMeshTests` — `StrangersBehindOnePublicIp_AreReachedAtTheirSource`
     (`AddBehindNat`, `Nat`).
9. **D1: hub relay budget** (no wire change). It starts by reading the minion hub's journal for
   "relay capacity reached" (the `check-logs` skill) and recording what it shows. Then
   `MeshManager.MaxRoutingNodes` (10) becomes a hub setting.
   - Test: `LegacyMeshTests` — `RelayBudget_IsTheConfiguredOne`.

**The wire is then complete for release 1, and 26.6 can ship.**

### After the release (no wire change)

10. **Core identity migration** (§5.3), a pure refactor. Tests: all existing, unchanged, plus
    `NodeIdTests` (kind 0 round trip, `Valid`, text) and `PeerDirectoryAliasTests`.
11. **Node key** (§6). `NodeKeyStore`, `LocalIdentity.Key`, the kind-1 name in `Names`, alias
    binding only from a matching HelloAck, the logs of §6.4, and a `VOLUME` line for the storage
    folder in the `Dockerfile`.
    - Tests: `Session/NodeKeyStoreTests` (temporary folder) — `SameSlot_ReopensTheSameKey`,
      `SlotRecordingThisPort_IsPreferred`, `AllSlotsHeld_GivesAKeyForThisRunOnly`,
      `CorruptFile_IsReplaced`; `Jfp2PluginTests` — `Handshake_CarriesTheKeyBesideTheLegacyName`;
      `Jfp2RelayTests` — `CgnatStrangersWithOneLegacyId_TheCollisionIsLoggedAndNothingFlaps`
      (`AddBehindNat("10.0.0.5", "203.0.113.9")` and `AddBehindNat("10.0.1.5", "203.0.113.9")`: one
      legacy id; `Nat` maps them to different public ports), `RestartWithANewKey_ReplacesTheKey`.
    - Docs: architecture §5.5, §7, §11; spec §5.5; the README's Docker section shows a **named**
      volume, since the `VOLUME` line alone creates an anonymous volume that `docker rm` and a new
      `docker run` do not reuse: `-v joinfs:/root/.local/share` (the storage folder is
      `LocalApplicationData`, which .NET maps to `$HOME/.local/share` on Linux; adjust if the image
      runs as another user).
12. **JFP2-only `TestMesh`** (review item C6). A test whose nodes register only `Jfp2Plugin`: it
    must create, join, exchange positions and leave. It fails until B3 is done; it is committed
    skipped, with the reason, and B3's stages un-skip it.

B3 and later follow from appendix A, each with its own design approval.

## Status

| Stage | Scope | Wire change | Status |
|---|---|---|---|
| 1 | Decision record §2.13 and spec | — | Done, reviewed |
| 2 | Partial-class split of `Jfp2Plugin` | no | Done, reviewed |
| 3 | Node names in Hello and Forwarded | yes | Not started |
| 4 | Handshake, envelope and TLV rules | yes | Not started |
| 5 | Guaranteed-id rule | yes | Not started |
| 6 | Field limits and the 1,200-byte ceiling | yes | Not started |
| 7 | Observed endpoint | yes | Not started |
| 8 | D2: false same-LAN detection | no | Not started |
| 9 | D1: hub relay budget (minion journal first) | no | Not started |
| — | **Wire frozen: 26.6 may ship** | | |
| 10 | Core identity migration (pure refactor) | no | Not started |
| 11 | Node key and its store, `Dockerfile` volume | no (TLV content) | Not started |
| 12 | JFP2-only `TestMesh` (skipped tracer bullet for B3) | no | Not started |

## 10. Decisions

Taken by the project owner: JFP2 is not frozen until its first release; 26.6 waits for the wire to
be right; each reviewed stage is committed on `jfp2-node-identity`; this design is approved before
implementation starts; and the rules of §2.4 (guaranteed ids), §4.6 (field limits), §4.4 (TLV
lengths), §2.5 (per-hop security) and §4.1 (full offers in keepalives). On the questions this
design raised:

| | Question | Decision |
|---|---|---|
| A | Random keys (kind 1) first, or key-pair ids (kind 2) from the start? The wire carries either (§3.1). | Random keys in stage 11; kind 2 with B7, when there is something to sign. |
| B | When do D2 and D1 ship? | In 26.6, as stages 8 and 9 before the freeze, since 26.6 waits anyway. |
| C | Does the HTTP my-IP lookup stay the primary source of the public address? | Yes, until B3's JFP2-first join; observations are logged only. |
| D | A legacy-learned peer whose public IP changes reappears as a new peer in mixed sessions (§5.4). | Accepted; it is today's behaviour, and only a JFP2-learned identity (B3) can fix it. |
| E | The NAT class in the UI? | Log only, until field logs show the classification is right. |
| F | Docker persistence of the key | A `VOLUME` line in the `Dockerfile`, in stage 11. |
| G | Names in release 1 (2 more bytes per relayed datagram), or a capability-gated layout later? | Names now (§3.4). |
| H | Does release 1 answer a Hello from a non-member, even statelessly? | No: it would show every running node's names to scanners; B3 designs it with admission (A.2). |
| I | An extension-chain mechanism in release 1? | No (§2.1); a flag bit can define one later. |
| J | Datagram ceiling | 1,200 bytes rather than the exact 1,232, for tunnels. |
| K | IPv6-only nodes and legacy peers: hub-assigned synthetic legacy ids, or no visibility? | Decided in the IPv6 stage, from how many legacy users remain. |
| L | A "new identity" action replacing key and `Guid` together? | Later, if users ask. |

## Appendix: sketches for B3 and later (non-binding)

These record the current thinking so that release 1 is checked against it. They bind nothing; each
gets its own design when its stage comes.

**A.1 Mesh over JFP2 (B3).** Join, JoinReply, JoinFail, Login, LoginFail, AddNode, Leave, Pulse,
PulseResponse, Pathfinder, PathfinderResponse as internal classes appended after 9, offered with
versions. Membership entries carry all of a member's names and its endpoint candidates
(`WireEndPoint` list); long lists are split into several messages (the handlers accept partial
lists). The core's alias index (§5.3) merges a node learned both ways. `Jfp2Profile` looks classes
up by partition and number.

**A.2 Admission (B2/B3).** A Hello from a non-member carries an `Admission` TLV, empty the first
time. The answer is a HelloAck with `Result` 2, sender id 0, no offers, `ObservedEndPoint` and a
16-byte cookie: HMAC-SHA256 over the source endpoint and the asker's names with a per-process
secret replaced every 60 s (the previous still accepted). It creates no state, is never larger
than the Hello, is rate-limited (one token bucket), carries no key, and is sent only by a node in a
session or a hub. Every Hello with `Admission` is handled this way, member or not; the probe table
uses ids disjoint from session ids and is checked first; a `Result` 2 without an observed endpoint
is ignored. A Hello echoing a valid cookie creates a session that is not a member: never a next hop,
`CanCarry` false, limited in number and lifetime, carrying Join/Login and kinds that need no
session; the same session serves the node once `MeshManager` registers it. The probe also gives
JFP2-first join and, gated on a failed lookup, the public address.

**A.3 Endpoint candidates (B3, IPv6).** `Peer` holds candidates (public IPv4, LAN, IPv6) with the
time each last answered; a JFP2 session probes them and keeps the one that answered. Legacy keeps
one IPv4 endpoint.

**A.4 Relaying (B4).** Hub relay budgets per hub setting (stage 9) and advertised in Status; fan-out
by sending to the group name "all members" through a hub that agreed the fan-out capability; a
hop limit only if relaying ever goes beyond one hop, with its own flag. **Fan-out is the exception
to "relays never rewrite":** a 26.6 member cannot resolve the group name (§3.3) and never agreed the
capability, so the hub re-originates the message for each such member: a Forwarded datagram with
the member's own name as target (the origin's name and guaranteed id kept, §2.4), or the core's
translation path. Fan-out to members that agreed the capability may stay one datagram per hop.

**A.5 Security (B7).** Challenge-response Join: the cookie or a nonce in HelloAck, a keyed hash of
the password in Join. Key-pair ids: kind-2 names, the public key in a Hello TLV, a signature over
the handshake payload and the responder's cookie, in a TLV. Per-datagram protection per hop as
§2.5 specifies.

**A.6 Coalescing and PositionV2.** Coalesced payload: sub-messages of (flags u8 with an internal
bit, class u8, length u16, body), behind a capability. PositionV2 is a new schema version of
Position, chosen by negotiation alone.

**A.7 Directory kinds (B5), large messages (B6).** Directory classes carry `WireEndPoint` lists
instead of legacy ids, over admitted sessions. Multi-segment guaranteed delivery behind a
capability, segments of up to 1,100 payload bytes, guaranteed ids per (origin, target) (§2.4).
