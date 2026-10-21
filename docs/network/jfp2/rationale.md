# JFP2: design rationale

Why the wire of `protocol.md` looks the way it does, and which options were rejected. The
specification states rules; this document gives reasons, so that nobody re-derives them. Decisions
taken are in `goals.md` §6.

## 1. The first-release wire at a glance

Most of the original wire already extended well: the magic and ProtoMajor bytes, the per-class
offers, the TLV area, the capability bits and the rule that a new flag needs an agreed capability.
Five things changed before the release, and a few rules were written down.

| Element | Decision | Reason | Cost |
|---|---|---|---|
| Magic `0xFA`, dispatch on byte 0 | keep | Shares the socket with legacy (`0x0B`); a future protocol takes another magic | none |
| ProtoMajor byte, unknown → drop | keep | The escape for a breaking redesign | none |
| Flags: Guaranteed, Forwarded, Internal | keep | Hot extensions at fixed offsets | none |
| Flag bit 2 Coalesced | reserve | Format sketched (`design-future.md` §7); needs a partition bit per sub-message | none |
| Flag bits 4–7, a bit per extension | keep, rule written | Each new bit comes with a capability; candidates: 4 Authenticated (B7), 7 extension chain | none |
| Session ids, u16 each | keep | Room for 65,535 neighbours per node; authenticity comes from B7, not width | none |
| Class u8 per partition, 255 = `Extended` | keep; 255 reserved, not implemented | 255 classes per partition last years; an extended class could not be offered in a u8 offer anyway | none |
| Internal classes 2–8 (mesh) | release the reservation | B3 needs another set (Login, LoginFail, AddNode, ...); assigned when designed | none |
| Guaranteed extension (id u16, index u8, count u8) | keep the layout | Count 255 × 1,100 bytes is ample for B6 | none |
| Guaranteed ids | **changed**: unique per (origin, final target); relays keep the origin's id and key on (origin, target, id) | Fixes a collision at translating relays; avoids wrap at large hubs; ready for fan-out | relay tables re-keyed |
| A guaranteed segment that is dropped | **changed**: never acknowledged | Before, count > 1 was acked, then dropped | none |
| `GuaranteedDone` | changed: always 3 bytes | Drop the 2-byte form kept for dev builds | less code |
| Forwarded extension: two 7-byte legacy ids | **changed**: two 8-byte names | Later kinds without a new layout or relay rewrite | +2 bytes per relayed datagram |
| Hello Node TLV: one 7-byte legacy id | **changed**: `Names`, any number of names | A node has several names (legacy id, key) | +1 byte |
| Hello fixed fields | keep | ProtoMajor range, capabilities, id, result, offers carry the next years | none |
| Keepalive Hellos with the full offer list | keep, deliberately | Each keepalive re-resolves the agreement, so a restarted peer's new offers are noticed | about 100 bytes per neighbour every 5 s |
| String fields of v1 codecs | **changed**: byte limits per field | Every message fits 1,200 bytes; no u16 wrap, no buffer overflow | text cut at a character boundary |
| `Result` values | changed: rule for unknown values | A later refusal reason must not read as "legacy only" | none |
| Offer entry (partition u8, class u8, min u8, max u8) | keep; unknown partition ignored | Leaves room for another class space | none |
| Capability u64 | keep; release the four reserved meanings | Assigned only with the design that needs one | none |
| TLV (tag u16, length u16) | keep, rules written | Unknown tags skipped; short values ignored, long ones read up to the known prefix; first copy counts | none |
| `ObservedEndPoint` TLV in HelloAck | **added** | B2's STUN function from day one, also from release-1 peers | 11–23 bytes per HelloAck |
| `PeerKey` (family, address, port, local) | **replaced** by `WireEndPoint` | Identity belongs to names; an address is only an address | code removed |
| Datagram size | **rule**: ≤ 1,200 bytes, enforced by field limits | IPv6 minimum MTU is 1,280 | none |
| Security (MAC, counter, encryption) | reserve: flag bit 4, per hop | Relays decode to translate, so protection is per hop; fits behind a capability | none |
| Node key and its store | not in release 1 | Not a wire element: lands after the core migration | none |
| Admission, cookie, probe | not in release 1 | Nothing to do with a non-member until B3; a release-1 node ignores such Hellos, which is the right answer | none |

## 2. Envelope

### 2.1 One flag bit per extension, or a chain?

One bit per extension keeps the hot extensions at fixed offsets and the parse branch-free. A chain
(type, length, value items) lets a sender add header fields that a receiver may skip. But every
foreseen header addition changes how a datagram must be handled (security, segmentation, a hop
limit for multi-hop relaying), so it would be "must understand" anyway and need a capability. Four
free bits cover the foreseen ones (bit 4 for B7's `Authenticated`).

**Decision:** one bit per extension. If the bits run out, one becomes "an extension chain follows",
defined with a capability like any other flag. No release-1 code.

### 2.2 Session ids

u16 each, random per node, `0` = none. A node can hold 65,535 sessions; the largest foreseen hub
has a few hundred. To inject into a session an off-path attacker must guess the pair (sender id,
recipient id), 32 bits. A HelloAck is matched on the recipient id alone, 16 bits, which is why an
observation also requires the expected source endpoint, and why a refusal (which takes a working
link off JFP2 for 30 s) counts only from that endpoint and naming the right node. This is not
authentication and does not need to be.

### 2.3 Classes

u8 per partition. The internal numbers 2–8 were reserved for a mesh whose message set B3 will design
afresh (Login, LoginFail, AddNode and probably a challenge are missing from it); the reservation is
released and B3 appends. `255` stays reserved as `Extended`: a class beyond 255 would also need an
offer form other than the u8 in an offer entry (an offer TLV, behind a capability), so nothing in
release 1 needs to parse it. A sender only sends classes the receiver offered, so a release-1 node
never receives one.

## 3. Guaranteed delivery

**Segments (B6).** 255 segments of up to 1,100 payload bytes is 280 KB, more than any message JoinFS
sends. B6 enables segmentation behind a capability, so a release-1 node never receives count > 1
from a later build. If it does, it drops it *without acknowledging it*: before the change it
acknowledged first and then refused to reassemble, so the sender believed a message was delivered
that never was.

**The id rule.** Before the change, a translating relay deduplicated and reassembled per (origin, id)
and drew ids for its re-sends from its own counter. Three bugs followed:
- One origin's message with one id for two targets reached only the first (the second was suppressed
  as a duplicate). This runs on every translation, to legacy too, which is the common case.
- The relay acknowledged upstream with a plain ack, which the sender matched by (id, index, hop),
  clearing every entry that matched. With one id for two targets, the first ack cleared both
  entries, and a lost copy for the second target was never resent. A Forwarded ack lets the sender
  match (origin, target, id, index) exactly.
- A relay that re-sent under its own id made the final target deduplicate the relay's id under the
  origin's name, where it could collide with an id the origin used for a message of its own, which
  was then suppressed. Keeping the origin's id also lets the relay match the downstream ack to the
  upstream message without a table.

The pending table is keyed (origin, target, id, index) since a relay now holds ids it did not
choose. A node may keep one counter for everything or one per target; either way the rule holds as
long as one counter does not wrap within 30 s, which per-target counters guarantee at any hub size.

Downstream of a translation to legacy, delivery is hop by hop with legacy's own ids.

## 4. Names

### 4.1 Options

| Option | Verdict |
|---|---|
| Keep 7-byte legacy ids; add an 8-byte form behind a capability and a flag later | Rejected. Every relay between a new and a release-1 hop would have to rewrite the extension or drop the datagram, forever. |
| Names in release 1, legacy kind only | **Chosen.** Release 1 is wire-ready for keys, IPv6-only nodes and key-pair ids; later kinds need no flag, capability or rewrite. 2 bytes per relayed datagram. (Fan-out still needs work at the hub for 26.6 members, `design-future.md` §5.) |
| Longer names (16 bytes) for strong key-pair ids | Rejected. A name is a handle, not a credential: B7 authenticates by the public key and a signature in the handshake. |
| A self-describing name (kind + length) | Rejected. Fixed size keeps the relay extension at fixed offsets. |

### 4.2 The naming rule

A node is named by its kind-0 name while it has a legacy id, otherwise by its key. Every node can
apply it without knowing what others know, and a release-1 node can resolve every name it is sent,
because until B3 every member has a legacy id. A node without one (IPv6-only, JFP2-only) is named by
its key, which other nodes learn from B3 membership; a release-1 node cannot resolve it and drops
such traffic, as it could not have carried it over legacy either. When legacy retires and nodes stop
having legacy ids, the rule moves to keys by itself.

Release 1 therefore resolves kind 0 only, and nothing more is needed: no node can send a release-1
node a kind-1 name it could resolve, since a node is named by its key only when it has no legacy id,
and such a node is never a release-1 node's peer. An unresolvable datagram is not acknowledged, so
its sender retries until membership catches up, or gives up after 180 s.

Group names are only sent through a hop that agreed the fan-out capability (B4), so a release-1
relay never receives one.

## 5. Handshake and limits

### 5.1 Keepalives carry the full offer list

A keepalive is a full Hello every 5 s, and every Hello and HelloAck resolves the agreement again, so a
peer that restarted as another build is noticed within one keepalive and the router stops sending it
classes it no longer takes. A short keepalive would need a second mechanism for that. The cost is
about 100 bytes per neighbour every 5 s: 10 KB/s each way for a hub with 500 neighbours.

### 5.2 `Result` and the capabilities

Before the change any non-zero `Result` marked the peer legacy-only for 30 s. That would make a later
refusal reason, say "too many sessions, ask again later" from a large hub, look like "this node does
not speak JFP2". Hence "a non-zero `Result` means no session now".

The four reserved capability meanings (Coalescing, QuantizedPosition, Ipv6Peers, SelectiveAck) are
released: PositionV2 is chosen by schema version, names make `Ipv6Peers` unnecessary, and the other
two are assigned with their designs. No build has ever advertised a capability.

### 5.3 The 1,200-byte ceiling and the field limits

IPv6's minimum MTU is 1,280, less 48 bytes of IPv6 and UDP headers; 1,200 rather than the exact
1,232 leaves room for tunnels. Headers are at most 28 bytes, 52 with B7's 24, so every payload is at
most 1,100 bytes, which the per-field limits guarantee by construction (the largest, Status, is 1,058).
With them the u16 wrap in `WireText.WriteString` and an overflow of the 16 KB payload buffer cannot
happen. The receiver does not check limits, so a later schema version may raise them once B6
segmentation exists.

### 5.4 TLV rules

"Short ignored, long read to the known prefix" is how a later build extends a value. "First copy
counts" replaces the earlier "last copy counts"; a list goes inside one value. The same shape is used
by `WireEndPoint` (family fixes the size, a longer value is read up to it).

### 5.5 Why an observed endpoint, and why it replaced `PeerKey`

Every HelloAck says where the Hello came from, as STUN does. Release-1 builds report it, so later
builds can learn their public endpoint and NAT behaviour from release-1 peers, and the field data
(the NAT class, logged) informs the work on CGNAT. `PeerKey` was never used and mixed identity
(`Local`) with address: identity belongs to names, and an address is only an address.

The observed address never replaces the public address in release 1: the HTTP lookup stays the
source of the legacy id, because a join to a release-1 or legacy hub needs it first. Using an
observation to bootstrap a node whose lookup failed needs a probe answered by a non-member, which is
B3 (`design-future.md` §3).

## 6. Sessions are per hop

(The decision record, with its tests, is `../network-plugin-architecture.md` §2.12.)

A hub and an MSFS client shared one LAN and one public `IP:6112` (the router forwarded the port to
the hub); a friend joined from the internet. The friend reached both nodes at the same endpoint, so
the hub answered the JFP2 Hello meant for the client, and the ack was credited to the client. The
friend then sent the client's traffic to the hub, which consumed it. The client saw the friend as
legacy and never saw his aircraft. With the client on port 6113 everything worked. Decisions that
followed:
1. A session is with a neighbour and is bound to a node id stated in the handshake. A HelloAck names
   the node that answered, so a node learns whether the peer it asked for or another node replied.
2. Receiving a Hello is not proof that our datagrams arrive.
3. The protocol is a property of each hop. The sender never needs to know what the final target
   speaks.
4. The fixed header's ids are hop-scoped on Forwarded envelopes, which also lets the relay compare
   schema versions.
5. The route (`RouteVia`) lives in the core, set by the pathfinder, not derived from comparing
   endpoints.
6. Sessions are kept honest by keepalives: a router that steers the endpoint elsewhere, a peer that
   restarts and a dead path are invisible to a handshake alone.

## 7. Room for per-hop security (B7)

Nothing is built in release 1; this checks that it fits. Protection is per hop: a relay decodes to
translate, so end-to-end confidentiality does not fit JFP2's relaying. Each relay checks what it
receives and protects again what it sends, with the key of each hop's session.
- **Flag bit 4 `Authenticated`**, set only toward a neighbour that agreed its capability.
- **Header extension**, after the Forwarded extension: an 8-byte send counter per session and
  direction, never reused; the receiver keeps a replay window per session.
- **Trailer**: a 16-byte tag. With integrity only, an HMAC-SHA256 truncated to 16 bytes over the
  associated data and the payload; with encryption, the payload is encrypted with an AEAD (for
  example ChaCha20-Poly1305: nonce = 4 bytes of direction, 0 or 1, then the 8-byte counter) and the
  tag is the AEAD's.
- **Associated data:** every byte of the datagram from offset 0 up to the end of the last header
  extension, as sent on this hop: the fixed 8 bytes (with this hop's session ids and flag bit 4 set),
  the guaranteed extension, the Forwarded extension, and the counter. A relay rewrites the session
  ids, so it must check the incoming tag before and compute the outgoing one after the rewrite.
- 24 bytes per datagram; the field limits leave room for it.
- **Handshakes** cannot use the flag, so B7 protects them through a TLV: the public key, and a
  signature or MAC over the handshake payload that precedes it, bound to the responder's cookie or
  nonce.
