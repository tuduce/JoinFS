# JFP2 design review: towards a stand-alone protocol

**Review, 2026-10-07.** It judges JFP2 (`docs/reference/jfp2-protocol.md`) against the goals stated
below and lists what to change, in order. JFP2's foundation is sound, but it depends on legacy in
four places and 26.6 is about to freeze its first version.

## Goals

JFP2 must be able to replace legacy: legacy is retired once enough users run a JFP2-capable build.

- **Escape legacy's ad-hoc evolution.** Extensibility and flexibility are first-class design goals,
  not side effects.
- **Stand-alone.** A node must be able to join, stay in and leave a session over JFP2 alone.
- **Swappable protocols.** The plugin network stack (`Net/`) exists so protocols can be switched or
  evolved; JFP2 should use it that way.
- **IPv6.** Some users cannot join the legacy mesh from behind CGNAT. IPv6 and better relaying are
  how they get in.

When this review was written, the docs still described JFP2 as a link upgrader that runs next to
legacy indefinitely; section E lists those passages. `docs/network-plugin-architecture.md` §2.13
now records the decision that JFP2 is the successor protocol.

## Verdict

The foundation is right; what blocks the goals is four dependencies on legacy, an expensive way to
add classes, and docs that encode the old goal.

**Already right:** per-class schema negotiation per neighbour, an extension area (TLV) in Hello that
old builds skip, dispatch on the first byte, sessions bound to a node id rather than an endpoint
(`docs/network-plugin-architecture.md` §2.12), and a router that picks a protocol per (peer, kind).

| Dependency on legacy | Where it shows |
|---|---|
| Joining | Hello is ignored from nodes the legacy Join has not registered (`Jfp2Plugin.HandleHello`); Join, Pulse and Pathfinder go over legacy only. |
| Node identity | `NodeId` is public IPv4 + port + last LAN octet, 7 bytes, shared with the legacy wire. |
| Relay addressing | The Forwarded extension carries two 7-byte `RelayNuid`s, IPv4-shaped. |
| Message kinds | 14 kinds have no JFP2 codec (see Earlier findings). |

**Timing:** 26.6 is not tagged yet and its release notes announce JFP2. Once it ships, 26.6 is the
oldest JFP2 every later build must work with.

## Earlier findings

The 14 missing kinds fall into four groups, and IPv6 is possible with legacy frozen because the
freeze covers legacy's bytes, not the socket or JFP2.

| Kinds | Why no JFP2 codec | What to do |
|---|---|---|
| ObjectPosition, CommsRequest | Scoped out as low value (`docs/protocol-v2-implementation-plan.md`, Phases 4 and 5) | ObjectPosition as Position + Identity with `IsAircraft=0`; redesign the bulk notes catch-up rather than port it |
| WeatherRequest, ShowOnRadar | Never used: no sender in the codebase | Don't port |
| HubList, UserListRequest, HubUser, UserPositions(Request), Online, UserNuid(Request) | Sent to hub endpoints outside the session; JFP2 only talks to verified mesh neighbours | After pre-membership Hello (B2), with `PeerKey` addresses |
| RemoveObject, PeerInfo | In-session, never revisited | Add now |

| Pair | IPv6 possible? | How |
|---|---|---|
| New build ↔ new build | Yes | JFP2 carries the mesh and directory kinds with `PeerKey` addresses |
| Dual-stack new build ↔ released build | Yes | Legacy over IPv4 as today; JFP2 over IPv6 to new peers |
| IPv6-only new build ↔ released build, direct | No | Released builds have an IPv4-only socket |
| IPv6-only new build ↔ released build, via a dual-stack hub | Only with an alias | The hub gives the IPv6 node a synthetic 7-byte id and translates |

To keep legacy frozen:
- the legacy encoder is never handed an IPv6 node (it leaves them out of AddNode, JoinReply,
  Pathfinder, HubList and UserNuid);
- `LegacyPlugin.CanCarry` answers false for IPv6 peers;
- one dual-mode socket, with IPv4-mapped sources converted back to IPv4 on receive.

## A. Before 26.6 ships

Five small changes that are cheap only before 26.6 freezes the first JFP2. Implementation started
2026-10-07; implemented and independently reviewed the same day, committed as `0119d21` on branch
`jfp2-pre-26.6`.

| Item | Change | Why | Status |
|---|---|---|---|
| A1 | Drop datagrams whose ProtoMajor byte is not 2; log at Network level, not as an error | `Envelope.ReadFrom` never checks byte 1, so a future major version would be misparsed | Reviewed |
| A2 | Receivers drop datagrams with any flag other than Guaranteed, Forwarded, Internal; senders set other bits only after the defining capability is agreed, never on Hello/HelloAck | The spec says reserved bits are ignored; a future flag that adds a header extension would shift the payload | Reviewed |
| A3 | Freeze Hello/HelloAck (envelope, fixed fields, offer layout); new things only in TLVs; a later major version is negotiated inside a ProtoMajor-2 Hello; golden byte test | Makes Hello the permanent entry point and closes the "Governance for ProtoMajor 3+" open question | Reviewed |
| A4 | TLV tag 2 "Build" with the app version, bounded and sanitized; logged once per peer; named in the Wireshark dissector | Hubs can count JFP2 builds, the data needed to decide when legacy retires | Reviewed |
| A5 | Fix two misleading comments: version 0 means "don't send", not "baseline"; recipient id 0 is not "broadcast" | They contradict the spec | Reviewed |

**Retirement criterion:** a 26.6 hub never accepts a JFP2-only joiner (it ignores Hello from unknown
nodes and has no JFP2 Join). Legacy retires when enough users run the build with the JFP2 mesh, not
26.6.

## B. Protocol changes for a stand-alone JFP2

The biggest change is node identity: an opaque id, with addresses kept separately, unblocks IPv6,
dual-stack and CGNAT at once.

**B1. Opaque node id, addresses as attributes.** The 7-byte id fails in four ways:
- CGNAT: strangers sharing a CGNAT public IP look as if on our LAN; `MakeEndPoint` sends to
  `192.168.x.<their octet>`. Sharing port and octet too gives identical ids.
- IPv6 privacy addresses rotate daily.
- A dual-stack node would appear as two nodes.
- The relay header changes shape per address family.

Proposal:
- a random 8-byte id, persisted per install and instance;
- each peer carries candidate endpoints (public IPv4, LAN, IPv6);
- the legacy plugin maps legacy ids to opaque ids, deterministically for legacy-only peers;
- Hello carries both ids;
- Forwarded carries 2 × 8 bytes behind a capability bit.

Sessions get a set of endpoints instead of one.

**B2. Bootstrap without legacy.**
- Accept Hello from unknown nodes, for a session that exists before membership, protected by a
  stateless cookie against spoofed sources.
- HelloAck reports the observed source endpoint in a TLV, as STUN does. This replaces the "what's my
  IP" lookup for both families and detects NAT/CGNAT.

**B3. The mesh over JFP2.** Join, AddNode and Pathfinder carry endpoint lists; long lists are split
across messages (the handlers already accept partial lists); mesh messages a relay can't forward
byte for byte go through translation.

**B4. Relaying as a first-class feature.** One relay node serves at most 10 senders
(`MeshManager.MaxRoutingNodes`), over one hop, shared with legacy. With per-destination mapping
(common in CGNAT) the relay is the only path. Hubs advertise a configurable relay capacity, and a
fan-out target ("all members") lets a CGNAT pilot upload once and the hub copy to everyone.

**B5. Remaining kinds.** In-session kinds now (ObjectPosition, RemoveObject, PeerInfo); directory
kinds after B2; skip WeatherRequest and ShowOnRadar; redesign bulk notes.

**B6. Large messages.** Split at message level where handlers are idempotent; multi-segment delivery
only for bulk notes and hub lists.

**B7. Security, designed for now.** Without legacy, JFP2's Join is the only access control. Today
that is a 32-bit password hash in the clear plus forgeable sender ids. Reserve a challenge-response
Join (nonce in HelloAck, keyed hash of the password) and optionally key-bound node ids.

## C. Code changes that make evolution cheap

Adding a JFP2 class today touches six places and version skew cannot be tested; fix both before
adding the mesh.

1. **One table per message class.** Today a class touches `MessageClasses`, `ClassFor`,
   `LocalOffers`, the static codec registration, an encoder overload and the `Decode` switch. One
   descriptor (kind, class, partition, codecs per version, guaranteed) makes it one place.
2. **Per-instance protocol profile.** `LocalOffers` and `CodecRegistry` are static, so tests cannot
   run an old-build node beside a new-build node; they inject hand-made Hellos. Every future version
   bump, including relays translating between JFP2 versions, ships untested without this.
3. **Split `Jfp2Plugin.cs`** (985 lines) into handshake and sessions, next hop and relay, encode and
   decode, before the mesh is added.
4. **Fix the ordering bug** in `NetworkCore.Deliver`: mesh kinds go to `MeshManager` before the check
   for messages addressed to another node.
5. **Separate MeshManager's rules from local policy.** Wire-visible rules stay fixed for released
   peers; relay budget and timing become configurable and JFP2 can improve on them per peer.
6. **A failing first test: a JFP2-only `TestMesh`.** Every JFP2 test node registers `LegacyPlugin`
   today. Making a mesh without it pass proves JFP2 stands alone. Add IPv6 endpoints and a
   per-destination NAT to `InMemoryNetwork` too.

**Status, 2026-10-07: C1, C2 and C4 implemented and independently reviewed**, committed as
`6c850d8` on branch `jfp2-evolvability`.
- C1 and C2: `ClassDescriptor` and `Jfp2Profile` replace the six places and the static
  `CodecRegistry`; how to add a class or a version is spec §6.6. Position encode/decode got faster
  (about 92 → 69 ns and 52 → 35 ns), still allocation-free.
- The first version-skew test (a relay re-encoding Position v2 for a v1 peer) found a real bug: a
  relay that forwarded Identity unchanged never learned it, so it withheld the translated Positions.
  Fixed in `Jfp2Plugin.Relay`.
- C4: a mesh message addressed to another node is translated once the node knows its own id.
- Left for B3: `Jfp2Profile` looks classes up by application class number only; it must take the
  partition when mesh classes become descriptors.

## D. Quick wins for CGNAT users

Three local fixes could help CGNAT users before any wire change; the first two are hypotheses until
field logs confirm them.

- **Configurable hub relay budget.** The 10-sender limit is a local constant. Check the minion
  journal for "relay capacity reached" to see if it is what hits people.
- **False same-LAN detection** when two peers share a public IP. `RegisterNode` updates only the port
  of a peer's endpoint, never the address.
- **DNS lookup** keeps only the first address returned, so a hostname that also has an IPv6 address
  can fail to join.

## E. Docs that contradict the goals

A new decision record, §2.13 "JFP2 as the successor protocol" in
`docs/network-plugin-architecture.md`, should supersede these passages:

| Document | Passage | Conflict |
|---|---|---|
| `docs/reference/jfp2-protocol.md` | §2 non-goals | "Runs alongside legacy indefinitely"; security is a non-goal |
| `docs/reference/jfp2-protocol.md` | §3, §7 | JFP2 described as a link upgrader |
| `docs/network-plugin-architecture.md` | §2.4.1 | JFP2 mesh: "not now" |
| `docs/network-plugin-architecture.md` | §2.10 item 3 | "Every node speaks legacy" |

**Status, 2026-10-08:** done in stage 1 of `docs/jfp2-wire-design.md`; decision record §2.13.

## Suggested order

Section A first, then the code changes that make every later step cheaper and testable.

1. Section A, before tagging 26.6.
2. C1, C2 and C4.
3. B1 and B2: opaque node id, observed endpoint in HelloAck, Hello from unknown nodes.
4. B3, B6 and C6, until the JFP2-only mesh test passes.
5. Dual-stack socket and IPv6 endpoint candidates.
6. B4 relay capacity and fan-out; B5 directory kinds.
7. Retire legacy when the Build counts from A4 say it is time.
