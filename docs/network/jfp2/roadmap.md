# JFP2: roadmap

**The one place for work still to do and questions still open.** Status of finished work is in
`implementation.md` §7; why the work exists, `goals.md`; designs, `design-node-identity.md` and
`design-future.md`. Update this file when a task changes state, and nowhere else.

Item names (B1 to B7, C1 to C6, D1 to D3) come from the design review that started the work; they
are kept so older commits and notes still make sense.

## 1. Where we are

**Since 2026-10-21 the order and the stages are in `standalone-ipv6-plan.md` §6**, which takes over
items 10, 11, 12, B1 to B7, the dual-stack socket, D3 and the retirement of legacy. This file keeps
the open questions (§4) and the verification items (§2.4); §3 below is history.

- **The first-release wire is complete.** Stages 1 to 9 are done, reviewed and merged to `main`
  (#200). 26.6 may ship: nothing in JFP2 blocks it. From that tag the evolution rules
  of `protocol.md` §5.8 and §10 bind.
- **Nothing in release 1 lets a node join over JFP2 alone.** Legacy is still the membership layer.
  Everything below is about getting past that.

## 2. Open tasks

### 2.1 After the release, no wire change

| # | Task | Design | Notes |
|---|---|---|---|
| 10 | **Core identity migration.** Done in stage 1 of `jfp2-standalone`: `NodeId` is the 8-byte name (`JoinFS.Messages`), `LegacyNodeId` serves the legacy plugin, `PeerDirectory` has an alias index; `NodeName` is gone. | `design-node-identity.md` §1 | All existing tests pass unchanged (goldens untouched). |
| 11 | **Node key.** Done in stage 1 of `jfp2-standalone` (plan D3): `NodeKeyStore` (four slots, `node-N.key`, held open while the instance runs), `NodeKey` over `JoinFS.Crypto`'s `P256KeyPair`, `LocalIdentity.Key`/`KeyId`, the `Node key ... from slot` log line, the `VOLUME` line in the `Dockerfile` and the README's Docker section. The name is kind 2 (first 7 bytes of SHA-256 of the public key), not kind 1. No wire carries it before stage 4. | `design-node-identity.md` §2 | After 10. Deviations from the row: none. The evening test (two instances on 6112/6113 keep their key names across restarts) was not run by the implementing session. |
| 12 | **JFP2-only `TestMesh`** (review item C6). A test whose nodes register only `Jfp2Plugin`: it must create, join, exchange positions and leave. Committed skipped, with the reason; B3's stages un-skip it. IPv6 endpoints and the IPv6 firewall were added to `InMemoryNetwork` in stage 2 of `jfp2-standalone` (see the dual-stack socket row below); the per-datagram `Nat` hook it already had is the per-destination NAT. | | The tracer bullet for B3. |

### 2.2 Towards a stand-alone JFP2

Each needs its own design, approved by the owner, before it is built.

| Item | Task | Design | Depends on |
|---|---|---|---|
| B1 | **Opaque node id, addresses as attributes.** The wire is done (names). What remains is the core (stage 10, 11) and endpoint candidates per peer (B3). | `design-node-identity.md`, `design-future.md` §2 | 10, 11 |
| B2 | **Bootstrap without legacy.** Done in stage 3 of `jfp2-standalone` (plan row 3), wire in `protocol.md` §5.9 (Hello/HelloAck extension tag 4 `Admission`, `Result` 2, Appendix A.3 to A.5, dissector). A node in a session (`IProtocolHost.Connected`; a hub is one) answers a stranger's Hello that carries `Admission` with `Result` 2, no offers, the observed endpoint and a 16-byte cookie (`JoinFS.Crypto`'s `StatelessCookie`: HMAC-SHA256 over the source endpoint and the first four names the stranger stated, secret rotated every 60 s); a Hello echoing a valid cookie opens a **non-member session** (`PeerSession.NonMember`, kept apart from `sessions`, so never a next hop; `CanCarry` false; any other class on it is dropped). `Jfp2Plugin.Introduce(endpoint)` is the asking side. The values are defaults chosen under plan §7 rule 2: 32 non-member sessions, 60 s each and not extended by a keepalive, 10 challenges per second with a burst of 20, three Hellos by a stranger in all, none of them larger than the answer allows (a Hello smaller than the answer is ignored). `NetworkService` now passes JFP2 datagrams from IPv6 sources to the core (legacy ones are still dropped). Log lines: `JFP2: admitted stranger <name> at <endpoint>: non-member session` (event level, one per stranger), `... admitted this node: non-member session`, and at network level the Hellos, the challenges and the end of a session. Deviations from the row: (1) the cookie is bound to the asker's names, not to a 32-byte random, and `StatelessCookie` takes the time as an argument (the library has no clock) instead of a `TimeProvider`; (2) the stranger Hellos state the node's legacy id when it has one and its key name (plan 5.1), while the ordinary Hello still states the legacy id only: the key name joins it in stage 4; (3) nothing in the app calls `Introduce` yet (the JFP2-first join is stages 4 and 5), so the evening test cannot show the exchange between two real instances; the unit tests do, on `TestMesh`, on loopback and over IPv6; (4) the last clause of B2, the observed endpoint standing in for the my-IP lookup, is not done: the observed endpoint is only logged. Stage 7 takes it with the candidates (plan 5.4). | `design-future.md` §3 | B1 |
| B3 | **The mesh over JFP2.** Join, AddNode, Pathfinder and the rest as internal classes, with endpoint lists split across messages; `Jfp2Profile` looks classes up by partition; endpoint candidates per peer. | `design-future.md` §2 | B2, 12 |
| B6 | **Large messages.** Split at message level where handlers are idempotent; multi-segment guaranteed delivery only for bulk notes and hub lists. | `design-future.md` §8 | B3 |
| — | **Dual-stack socket and IPv6 endpoint candidates.** The socket half is done in stage 2 of `jfp2-standalone` (plan row 2): `UdpTransport` opens one dual-mode socket (IPv4-mapped sources turned back into IPv4; IPv4-only when the OS cannot or the IPv6 setting / `-noipv6` says so; IPv6-only with the `-noipv4` test switch, which is never stored), logs `NETWORK: socket on port N: <mode>`, and `LegacyPlugin` ignores any datagram from an IPv6 source. `NetworkService` dropped every non-IPv4 datagram before the core; stage 3 lifts that for JFP2 datagrams only (first byte `0xFA`). `InMemoryNetwork` has dual-stack nodes, `FirewallIpv6` and `TestMesh.CutIpv6`. Deviations from the row: none; there is no settings-form control for the IPv6 setting yet (stage 7 takes the UI). The endpoint candidates are stage 7. The evening test (second instance on 6113 with `-noipv6` or `-noipv4`) was not run by the implementing session. | `design-future.md` §4 | B3 |
| B4 | **Relaying as a first-class feature.** Hubs advertise their relay budget in `Status`; fan-out to "all members"; amplification limits. | `design-future.md` §5 | B3 |
| B5 | **Remaining kinds.** In-session: ObjectPosition (the recorder's `ObjectPositionV1Codec` moved into `JoinFS.Messages`, `docs/recording/jfs2/recorder-v2-design.md` §11), RemoveObject, PeerInfo. Directory kinds after B2. Do not port WeatherRequest and ShowOnRadar. Redesign the bulk notes catch-up. | `design-future.md` §8 | B2 |
| B7 | **Security.** Challenge-response Join, node names bound to a key pair (kind 2), per-hop protection of every datagram. | `design-future.md` §6, `rationale.md` §7 | B3 |
| — | **Retire legacy.** Delete the legacy plugin when the `Build` counts hubs log show enough users run the build with the JFP2 mesh. The X-Plane link keeps the framing constants it shares. | `goals.md` §5 | B3 and field data |

### 2.3 Smaller items

| Item | Task | Status |
|---|---|---|
| D3 | **DNS lookup** keeps only the first address returned, so a hostname that also has an IPv6 address can fail to join. | Open. |
| C3 | Split `Jfp2Plugin.cs` further, into separate classes (the partial-class split is done). | Optional; the partial split covers the need. |
| C5 | Separate `MeshManager`'s wire-visible rules from local policy. The relay budget is a setting (stage 9); the Pulse and Pathfinder timings are not. | Partly done. |
| — | **Position v2, coalescing, selective acknowledgement.** Each is designed when a measurement says it pays. The recorder will use Position v2 too (decided 2026-10-15): a quantised 52-byte candidate halves a `.jfs2` file (`docs/recording/jfs2/README.md`, Measurements, step 3). | `design-future.md` §7 |
| — | **Recording codecs.** Adopt the `ICodec` pattern in `Recorder.cs`, replacing the `#if FS2024` compile-time gate that causes the bug in `../../recording/jfs-format.md` §7.1. Separate piece of work; do not block JFP2 on it. | Open. |
| — | **Variable-name (vuid) table sync at scale.** | Open. |

### 2.4 Verification still owed

The audit of 2026-09-14/16 (`history/protocol-v2-implementation-review.md` §6) listed these as not
attempted. Field testing since may have covered some; check the logs before calling one untested.

- Guaranteed-delivery retry, dedup and give-up under real packet loss (a lossy link, or a temporary
  fault-injection hook), Event, Notes and WeatherReply between two JFP2 peers.
- The Position send path under realistic broadcast load, with a profiler, to confirm the GC pressure
  is gone.
- VariableSync traffic live with a simulator attached, with a value longer than 8 characters or
  non-ASCII, and a long value landing on a receiving SimConnect build's 8-byte local variable.
- The X-Plane plugin link (`../network-plugin-architecture.md` §4, "Still open after the rewrite").
- Done in the field, no longer owed: a v26.5 client coexisting with a JFP2 pair and a latest-build
  hub. The 50-simulated-peer load test is dropped: no real hub comes near that size.

## 3. Suggested order

1. Ship 26.6.
2. Stages 10, 11, 12.
3. B2, then B3 with B6, until the JFP2-only mesh test (12) passes. Dual-stack socket and IPv6
   candidates.
4. B4 and B5.
5. B7, designed with B3 but built when there is a key to sign with.
6. Retire legacy when the `Build` counts say it is time.

D3 and the verification items can go in at any point.

## 4. Open questions

| # | Question | Notes |
|---|---|---|
| K | IPv6-only nodes and legacy peers: hub-assigned synthetic legacy ids, or no visibility? | Decide in the IPv6 stage, from how many legacy users remain. |
| L | A "new identity" action that replaces key and `Guid` together? | Later, if users ask. |
| M | Relay fan-out: what budgets and amplification limits? | With B4. |
| N | Coalescing policy: batch window, eligible classes. | With a measurement. |
| O | A node whose replies are steered to another node cannot verify the path back, so that direction stays on legacy. Is that good enough? | Correct but not optimal; revisit with the CGNAT field data. |
| P | The NAT class: when does the log become a decision input (decision E)? | After field logs. |
