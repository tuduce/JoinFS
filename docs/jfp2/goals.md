# JFP2: goals, principles and decisions

Why JFP2 exists, what it must achieve, the rules the work follows, and every decision taken so far.
It is the one place for those. The wire itself is `protocol.md`; the reasons for individual wire
choices are `rationale.md`; the work still to do is `roadmap.md`.

## 1. Why JFP2 exists

The legacy protocol works, but it has structural problems that more patches cannot fix, and some
users cannot join at all.

**Problems of the legacy wire** (first documented in the v26.4→v26.5 protocol audit, removed from
the tree; read it with `git show 73b203d^:docs/protocol-changes-v26.4-v26.5.md`):

1. **One global version number gates everything.** `DataVersion` is implicitly checked for every
   message. When one message's shape changes the version moves for the whole protocol, and every
   receiver must reason about "what does version N mean for message X" for every X. v26.4→v26.5
   needed an intermediate version purely as a migration step for two unrelated messages.
2. **Version gates that do not match compile-time gates.** The recorder gated some fields with
   `#if FS2024` while the network gated the same fields with a runtime version check, so an FS2020
   and an FS2024 build produced different shapes for "the same version".
3. **EOF-sensing as the extension mechanism.** Messages grow by appending fields and reading until
   the datagram ends. A receiver cannot skip a field it does not recognize without knowing its
   length, so nothing but the tail can be extended.
4. **No peer capability exchange.** Two peers cannot say which schema range each speaks; there are no
   capability bits and no extension area in the handshake.
5. **Header overhead on every packet.** The 21-byte header is paid on every datagram, including 4
   bytes of guaranteed-delivery bookkeeping on Position, which is never guaranteed.
6. **IPv4-only addressing.** The node id hard-codes a 4-byte IPv4 address into every datagram.
7. **No security.** The only access control is a 32-bit password hash in the clear, plus forgeable
   sender ids.

**Users who cannot join.** Some users are behind carrier-grade NAT (CGNAT) and cannot join the
legacy mesh. IPv6 and better relaying are how they get in, and the frozen legacy wire cannot gain
either.

## 2. Goals

1. **Replace legacy.** A node can join, stay in and leave a session over JFP2 alone. Legacy then
   retires (§5). Until the mesh runs over JFP2, JFP2 upgrades the links legacy discovers.
2. **Extensibility without a flag day.** New fields, classes and schema versions are added without
   breaking peers that have not been updated, and without every class changing in lockstep. This is
   a first-class goal, not a side effect: legacy grew by appending, and JFP2 exists to stop that.
3. **Explicit compatibility.** Every version of every message class has an explicit codec, never a
   reader that consumes bytes until the datagram runs out. Old and new builds coexist on the same
   mesh and the same UDP port.
4. **Capability and version exchange** once per connection.
5. **Speed.** The hot path (Position) is smaller on the wire than legacy and cheaper to encode and
   decode, with no heap allocation per message and no per-packet branching on version once a peer's
   capabilities are known.
6. **IPv6 and CGNAT.** Nodes can be reached by IPv6, by several endpoints, and through relays when a
   NAT leaves no direct path.
7. **Swappable protocols.** The plugin network stack (`Net/`) exists so protocols can be switched or
   evolved; JFP2 uses it that way.
8. **Security.** Without legacy, JFP2's Join is the only access control, so authentication is a
   goal, designed with the mesh over JFP2: a challenge-response Join, node names bound to a key
   pair, and protection of every datagram per hop (`design-future.md` §6). Release 1 builds none of
   it.

## 3. Non-goals

- **New reliability semantics.** JFP2 keeps legacy's two tiers: unreliable, or guaranteed
  (acknowledged and retransmitted). No ordered streams and no congestion control.
- **Changing anything above the wire**: the simulator abstraction, model matching.
- **A flag-day cutover.** JFP2 runs next to legacy until legacy retires (§5).
- **End-to-end confidentiality.** Relays decode to translate, so protection is per hop.

## 4. Principles

The rules every stage of the work follows.

- **Until 26.6 ships the JFP2 wire changes freely.** Daily dev builds are no constraint, since
  testers run the same daily build, and between two builds on either side of a wire change the
  legacy fallback carries the session. The golden tests (`HandshakeGoldenTests`) change only with a
  deliberate change of the handshake, never to make a failing test pass.
- **From 26.6 on the evolution rules of `protocol.md` §5.8 and §10 bind.** Hello and HelloAck keep
  their envelope, fixed fields and offer layout; new things go into TLVs, new classes, new schema
  versions and capability-gated flags.
- **Legacy stays frozen** (`Legacy/Fixtures/*.hex`). Released v26.5 builds speak only legacy and drop
  every `0xFA` datagram. New fields and messages go into JFP2 and the canonical model, never into
  `LegacyPlugin`'s codecs.
- **The receiver drops what it cannot place, and the sender never sends what the receiver did not
  agree.** A receiver skips what declares its own size.
- **No code for later stages in release 1,** except parsing rules that later builds depend on.
- **Each stage is independently testable:** reviewed, committed on its branch, every existing test
  green (golden JFP2 constants change only where a stage changes the wire on purpose, the legacy
  fixtures never), built in all six configurations.

## 5. Release plan

- **26.6 waits for the wire.** 26.6 is the first release that speaks JFP2 and the oldest one every
  later build must work with, so its wire must be right. It is not tagged until the stages marked
  "before the freeze" in `roadmap.md` are done. Its release notes announce JFP2.
- **26.6 still joins over legacy.** A 26.6 hub never accepts a JFP2-only joiner: it ignores a Hello
  from a node it does not know and has no JFP2 Join.
- **Retirement criterion for legacy.** Hubs log the `Build` extension of every JFP2 neighbour.
  Legacy retires, by deleting its plugin, when those counts show that enough users run a build with
  the mesh over JFP2. That is not 26.6. The X-Plane link keeps the legacy framing constants it shares
  (`XPlaneLink` uses `LegacyWire`).
- **Until the mesh runs over JFP2 (roadmap item B3), legacy is the membership layer.** Join, Pulse
  and Pathfinder go through `MeshManager` and only legacy carries them; JFP2 negotiates only with
  nodes the legacy mesh registered; every node speaks legacy.

## 6. Decisions

Decided by the project owner unless noted. Open questions are in `roadmap.md` §4.

### 6.1 Direction

| # | Decision |
|---|---|
| 1 | JFP2 is not frozen until its first release; 26.6 waits for the wire to be right. |
| 2 | JFP2 replaces legacy (§2, §5). It supersedes the earlier positions "JFP2 stays a link upgrader" and "JFP2 mesh: not now" of `../network-plugin-architecture.md`. |
| 3 | Security is a goal, designed with the mesh over JFP2 (B7). |
| 4 | Each reviewed stage is committed on branch `jfp2-node-identity`; the design was approved before implementation started (2026-10-08). |
| 5 | JFP2 is a per-hop protocol: a session is with a neighbour and is bound to a node id stated in the handshake, never to a source endpoint; the protocol is a property of each hop (2026-09-26, after the shared-NAT field test; `rationale.md` §6). |

### 6.2 Wire

| # | Decision | Where |
|---|---|---|
| 6 | Guaranteed ids are unique per (origin, final target); relays keep the origin's id. | `protocol.md` §7.2 |
| 7 | Every string field has a byte limit; no datagram exceeds 1,200 bytes (not the exact 1,232, to leave room for tunnels). | §3.6, §9.7 |
| 8 | TLV length rules: short ignored, long read to the known prefix, first copy counts. | §5.4 |
| 9 | Per-hop security, not end to end. | §11.3 |
| 10 | Keepalive Hellos carry the full offer list. | §6.2 |
| 11 | Names in release 1 (2 more bytes per relayed datagram) rather than a capability-gated layout later. | §4.3 |
| 12 | No extension-chain mechanism in release 1; a flag bit can define one later. | `rationale.md` §2.1 |
| 13 | Release 1 does not answer a Hello from a non-member, even statelessly: it would show every running node's names to scanners. Admission is designed with B3. | §5.7 |

### 6.3 Identity and addressing

| # | Question | Decision |
|---|---|---|
| A | Random keys (kind 1) first, or key-pair ids (kind 2) from the start? | Random keys in stage 11; kind 2 with B7, when there is something to sign. |
| B | When do D2 (false same-LAN detection) and D1 (hub relay budget) ship? | In 26.6, as stages 8 and 9 before the freeze. Done. |
| C | Does the HTTP my-IP lookup stay the primary source of the public address? | Yes, until B3's JFP2-first join; observations are logged only. |
| D | A legacy-learned peer whose public IP changes reappears as a new peer in mixed sessions. | Accepted: today's behaviour; only a JFP2-learned identity (B3) can fix it. |
| E | NAT class in the UI? | Log only, until field logs show the classification is right. |
| F | Docker persistence of the node key. | A `VOLUME` line in the `Dockerfile`, in stage 11. |
