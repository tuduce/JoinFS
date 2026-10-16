# JFP2: sketches of later work

Current thinking for the roadmap items after release 1 (B2 to B7 and IPv6), recorded so that the
release-1 wire could be checked against it. **Nothing here is binding or built**, except §4.1, which
the owner approved. Each item gets its own design, approved by the owner, when its stage comes. The task list and order are in
`roadmap.md`; the designs that come first (core identity, the node key) are in
`design-node-identity.md`.

## 1. Where this fits

The review that started the work judged JFP2 against its goals (`goals.md`) and found that it
depended on legacy in four places: joining (Hello is ignored from nodes the legacy Join has not
registered), node identity (a 7-byte IPv4-shaped id), relay addressing (two 7-byte ids in the
Forwarded extension), and message kinds (14 kinds had no JFP2 codec). Release 1 removed the second
and third at the wire level. The items below remove the rest.

## 2. The mesh over JFP2 (B3)

Join, JoinReply, JoinFail, Login, LoginFail, AddNode, Leave, Pulse, PulseResponse, Pathfinder and
PathfinderResponse become internal classes appended after 9, offered with versions. Membership
entries carry all of a member's names and its endpoint candidates (a `WireEndPoint` list); long lists
are split into several messages (the handlers already accept partial lists). Mesh messages a relay
cannot forward byte for byte go through translation. The core's alias index
(`design-node-identity.md` §1) merges a node learned both ways. `Jfp2Profile` looks classes up by
application class number only today; it must take the partition when mesh classes become
descriptors.

**Endpoint candidates (also IPv6).** A `Peer` holds candidates (public IPv4, LAN, IPv6) with the time
each last answered; a JFP2 session probes them and keeps the one that answered. Legacy keeps one IPv4
endpoint. This is what lets a node behind CGNAT use an IPv6 path, and a dual-stack node avoid
appearing as two nodes.

## 3. Admission and bootstrap (B2)

Today a node cannot start a JFP2 conversation with a node it has not met through legacy. The sketch:
- A Hello from a non-member carries an `Admission` TLV, empty the first time. The answer is a
  HelloAck with `Result` 2, sender id 0, no offers, `ObservedEndPoint` and a 16-byte cookie:
  HMAC-SHA256 over the source endpoint and the asker's names with a per-process secret replaced every
  60 s (the previous still accepted).
- It creates no state, is never larger than the Hello, is rate-limited (one token bucket), carries no
  key, and is sent only by a node in a session or a hub. Every Hello with `Admission` is handled this
  way, member or not; the probe table uses ids disjoint from session ids and is checked first; a
  `Result` 2 without an observed endpoint is ignored.
- A Hello echoing a valid cookie creates a session that is not a member: never a next hop, `CanCarry`
  false, limited in number and lifetime, carrying Join/Login and kinds that need no session; the same
  session serves the node once `MeshManager` registers it.
- The probe also gives JFP2-first join and, gated on a failed lookup, the public address. Today a
  remembered address already makes a node `Ready`, the HTTP lookup blocks in the `Network`
  constructor, and only hubs retry it, so the trigger needs its own design.

## 4. IPv6 and dual-stack

IPv6 is possible with legacy frozen, because the freeze covers legacy's bytes, not the socket or JFP2.
It comes in two stages. §4.1, dual-stack nodes, is **approved by the owner (2026-10-16)** and comes
before B3. §4.2, IPv6-only nodes, needs B3 and stays a sketch. The work and its acceptance criteria
are in `agent-docs/features/ipv6/spec.md`; the terms (path, path upgrade, fallback, endpoint
candidate) are in `agent-docs/GLOSSARY.md`.

| Pair | IPv6 possible? | How |
|---|---|---|
| Dual-stack new build ↔ dual-stack new build | Yes, stage 1 | The whole path, legacy included, over IPv6 (§4.1) |
| Any new build ↔ released build | No | Released builds have an IPv4-only socket: legacy and JFP2 over IPv4 as today |
| IPv6-only new build ↔ new build | Stage 2 | JFP2 carries the mesh and directory kinds with `WireEndPoint` addresses (B3) |
| IPv6-only new build ↔ released build, via a dual-stack hub | Stage 2, only with an alias | The hub gives the IPv6 node a synthetic 7-byte id and translates (open question K) |

### 4.1 Stage 1: dual-stack nodes

**Why before B3.** Every node keeps its IPv4-shaped legacy id, so membership stays on legacy and
nothing in B2 or B3 is needed. The core already keeps each peer's endpoint apart from its id
(`MeshManager.RegisterNode` takes it from where a direct datagram came from), so legacy's frozen
bytes can travel an IPv6 path unchanged. What stage 1 builds (the dual-mode socket, endpoint
candidates per peer, IPv6 in `InMemoryNetwork`, the DNS fix D3) is what B3 needs anyway; the
candidate message of this stage is the one piece B3's membership entries may later replace.

**The socket.** One dual-mode socket, with IPv4-mapped sources converted back to IPv4 on receive. A
setting turns IPv6 off (on by default; `--noipv6` for the CONSOLE hub). If the OS cannot create a
dual-mode socket, the node uses an IPv4 socket and logs it once. The X-Plane plugin link and the
local Euroscope, websocket and web services stay IPv4.

**One path per peer.** All traffic to a peer, legacy and JFP2, goes over that peer's one path:
- **Dial.** A node dialling an address (a typed hostname or literal, an address-book entry) prefers
  IPv6: a hostname is resolved to all its addresses (D3) and the AAAA is used first if this node has
  IPv6. If the Join over IPv6 gets no answer in 5 s, the A record is tried. The core's Join takes the
  ordered address list and does this itself; the app only turns text into the list. A literal is
  `[2001:db8::1]:6112`, or a bare address with the default port. Entries of the global hub list are
  IPv4 and are dialled over IPv4; a path upgrade then moves them to IPv6.
- **No flipping.** A datagram from the peer over the other family never replaces the peer's
  endpoint. Today every direct datagram does (`RegisterNode`), which would make a peer that sends
  over both families flip between them.
- **Path upgrade.** A peer on an IPv4 path that has IPv6 endpoint candidates is probed: a Pulse to
  each candidate at the Pulse interval, for up to 30 s. The first PulseResponse that comes from one
  of its IPv6 candidates moves the peer's endpoint there. The IPv4 endpoint is kept for fallback.
  The two directions are independent: if only one side's probe gets through a firewall, only that
  side sends over IPv6.
- **Fallback.** A peer on an upgraded path that has not answered a Pulse over IPv6 for 10 s goes back
  to its IPv4 endpoint, and is not probed over IPv6 again for 5 minutes. A peer that was dialled
  over IPv6 has no IPv4 endpoint and expires after 30 s as today.

**Endpoint candidates.** Only IPv6 candidates are used in stage 1; IPv4 is still the legacy id.
- *Observed:* a node records the IPv6 source of a peer's direct datagrams. This is preferred.
- *Self-reported:* a node lists its own IPv6 candidate in its Hello and HelloAck (the `Candidates`
  tag, `protocol.md` §5.4): the address the OS would send from (on Windows a temporary address,
  which is what peers will see) with the socket's port. A hub reports its own the same way, so a
  node that joined it over IPv4 upgrades the hub path too. The tag is sent only when the node has
  an IPv6 candidate, so the handshakes of IPv4-only nodes, and the release-1 golden fixtures, stay
  byte for byte.
- *Spread by the hub only.* When a hub announces a member (at JoinReply and AddNode time, and when a
  member's candidates change), it sends `PeerCandidates` (`protocol.md` §9.8) with that member's
  candidates to the other members, and the others' candidates to the new member. Both sides of a
  pair then probe each other at about the same time, which is what gets the first datagram through
  stateful IPv6 firewalls; it is the same trick Pathfinder relies on for IPv4. A node accepts
  `PeerCandidates` only from the node it joined through, and only for names it resolves to a
  member; candidates for a member not yet registered are held for 10 s. A released or IPv4-only hub
  sends none, so its members never use IPv6 between each other.

**Bans.** `NetworkCore` drops a datagram whose source address **or** whose sender's legacy-id address
is banned, so a banned dual-stack user cannot return over IPv6. The ban list also takes IPv6
entries, each banning its /64.

**Unchanged.** Relaying, Pathfinder (its targets are legacy ids), `MaxNodesPerDevice`, the LAN
guess of `LocalIdentity.MakeEndPoint`. The NAT class of `ObservedEndPoints` is computed from IPv4
observations only. The legacy encoder is never handed an IPv6 node, because in stage 1 every node
has a legacy id.

**Visibility.** A peer on an IPv6 path is marked "IPv6" next to the routed/direct and protocol
labels (WinForms `SessionForm`, Avalonia `SessionView`); IPv4 is unmarked. Every dial choice, path
upgrade and fallback is a `NETWORK:` log line.

### 4.2 Stage 2: IPv6-only nodes (sketch)

Needs B3, and stages 10 and 11. To keep legacy frozen:
- the legacy encoder is never handed an IPv6-only node (it leaves them out of AddNode, JoinReply,
  Pathfinder, HubList and UserNuid);
- `LegacyPlugin.CanCarry` answers false for peers without a legacy id;
- membership entries carry endpoint candidates (§2), which may replace `PeerCandidates`.

## 5. Relaying as a first-class feature (B4)

One relay node serves at most 10 senders over one hop, shared with legacy (`MeshManager.RelayBudget`,
a hub setting since stage 9). With per-destination NAT mapping (common in CGNAT) the relay is the
only path. The sketch:
- hubs advertise their relay budget in `Status`;
- **fan-out:** a CGNAT pilot uploads once, to the group name "all members", and the hub copies to
  everyone, through a hub that agreed the fan-out capability;
- a hop limit only if relaying ever goes beyond one hop, with its own flag.

**Fan-out is the exception to "relays never rewrite".** A release-1 member cannot resolve the group
name and never agreed the capability, so the hub re-originates the message for each such member: a
Forwarded datagram with the member's own name as target (the origin's name and guaranteed id kept),
or the core's translation path. Fan-out to members that agreed the capability may stay one datagram
per hop. Fan-out needs relay budgets and amplification limits.

## 6. Security (B7)

Without legacy, JFP2's Join is the only access control. Today that is a 32-bit password hash in the
clear plus forgeable sender ids.
- **Challenge-response Join:** a nonce (the cookie) in HelloAck, a keyed hash of the password in Join.
  Legacy's DJB2-style hash would be replaced by a real KDF for the new path; JoinReply or a new
  negotiation message advertises which scheme the creator accepts.
- **Key-pair ids:** kind-2 names, the public key in a Hello TLV, a signature over the handshake
  payload and the responder's cookie, in a TLV.
- **Per-datagram protection per hop,** as `rationale.md` §7 specifies: flag bit 4, counter, 16-byte tag.
- Optionally a per-session symmetric-encryption mode with a key derived from the session password;
  a session without a password stays plaintext.

## 7. Coalescing, PositionV2, the Extended class

- **Coalesced payload** (flag bit 2, behind a capability): sub-messages of (flags u8 with an internal
  bit, class u8, length u16, body), letting small updates that become ready in the same tick share
  one datagram. Open: batch window and eligible classes.
- **PositionV2:** a new schema version of Position, chosen by negotiation alone (lat/lon as i32 at
  1e-7°, angles as i16). It is invisible to peers that never offer it.
- **Extended class (255):** the real class id is a u16 right after the fixed header, giving headroom
  past 255 classes. It would also need an offer form other than the u8 in an offer entry.
- **Selective acknowledgement:** assigned a capability with its design.

## 8. Remaining message kinds (B5) and large messages (B6)

The 14 kinds without a JFP2 codec fall into four groups:

| Kinds | Why no JFP2 codec | What to do |
|---|---|---|
| ObjectPosition, CommsRequest | Scoped out as low value | ObjectPosition as its own class with the recorder's `ObjectPositionV1Codec` (decided 2026-10-15, `docs/jfs2/recorder-v2-design.md` §11); redesign the bulk notes catch-up rather than port it |
| WeatherRequest, ShowOnRadar | Never used: no sender in the codebase | Do not port |
| HubList, UserListRequest, HubUser, UserPositions(Request), Online, UserNuid(Request) | Sent to hub endpoints outside the session; JFP2 only talks to verified mesh neighbours | After admission (B2), with `WireEndPoint` lists in place of legacy ids, over admitted sessions |
| RemoveObject, PeerInfo | In-session, never revisited | Add now |

**Large messages (B6).** Split at message level where handlers are idempotent; multi-segment delivery
(behind a capability, segments of up to 1,100 payload bytes, guaranteed ids per (origin, target)) only
for bulk notes and hub lists.
