# Paths between members

Status: resolved
Blocked by: 03

Spec: [../spec.md](../spec.md), build-order slice 4. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Internal class 10 `PeerCandidates` (codec, limits, splitting), hub sends on announce and on change, receiver accepts only from the node it joined through, simultaneous probing through both firewalls, one-directional upgrade, the mixed H/A/B/C scenario (A–B on IPv6, A–C and B–C on IPv4, positions everywhere), pairs with release-1 JFP2 stay on IPv4.

## Answer

Done on branch `ipv6-04-members`, commit 22363b4 (code, tests, docs).

- Wire: internal class 10 `PeerCandidates` (`PeerCandidatesV1Codec` in `JoinFS.Messages`, `NodeName` and `WireEndPoint` moved there, same namespace): at most 12 entries of at most 4 candidates, 1,069 bytes at most; `Split` cuts a long list; an empty entry clears; unresolvable names and unknown or short candidates are skipped; truncation keeps the entries before it. Guaranteed, JFP2 only (`LegacyPlugin.CanCarry` says no). The JFP2 profile now holds internal-partition descriptors (`ClassDescriptor.Internal`, `InInternalPartition`), the reliability layer and receive path know the partition. A node offers the class only when it has an IPv6 candidate (`RequiresIpv6`), so the Hello of an IPv4-only node is byte for byte unchanged (characterization test, goldens untouched).
- Hub (`MeshManager.Paths.cs`): the node that answered a member's Join is its hub (`Peer.JoinedThroughMe`). Once a second it sends each direct member, that has agreed the class, the candidates of every other peer it has not told it yet (revision per peer, node-wide counter), so announce-time and change-time are one rule. A candidate a Hello reported, or the hub observed, is announced the same way.
- Member: accepts only from the node it joined through (`JoinReply` sender), ignores its own entry and everything when it has no IPv6, applies an entry like a self-reported list (replace, empty clears; an observed source is then taken only in the /64 of one of them), holds entries for unregistered members 10 s (`HeldCandidatesTime`, at most 64).
- Relayed pairs: a relayed or never-reached peer is now probed with the announced candidates; its first PulseResponse from one makes it direct over IPv6, the relay is remembered and a fallback restores it (or, for a peer that never answered over IPv4, clears `SendEstablished` so Pathfinder finds the relay). The JFP2 session of a peer that falls back behind a relay is demoted, not moved.
- Two fixes found by the tests: the first datagram of an unheard peer over IPv6 no longer sets its endpoint when this node has IPv6 (its probes are often the first thing a member hears; the upgrade does it with the fallback bookkeeping), and a Pulse probe over IPv6 is answered to its source even when the route is a relay of the same family.
- Tests: `PeerCandidatesCodecTests`, `MemberPathsTests` (relayed pair through both firewalls, JFP2 and legacy over the new path, one direction only, fallback and return to the relay, mixed H/A/B/C, release-1 and IPv4-only pairs stay on IPv4, other sender ignored, own entry ignored, held and expired entries, empty entry, observed candidate announced, 15 members split into several messages, nothing re-sent when nothing changed). Two existing tests changed for the new class (`ProfileTests` counts 11 classes, `Jfp2PluginTests` loops the application classes).
- Docs changed first: `design-future.md` §4.1 (relayed peers are probed with announced candidates; hub rules; offer only with IPv6), `protocol.md` §3.3, §9.8, §10.2.
- For slice 5: `PeerJoined` carries the endpoint at first contact only, and the session layer uses it for one log line; the snapshot is republished from `Peer.EndPoint`, so it follows path moves (a relayed peer that upgrades now has `RouteVia` cleared, `Direct` true). The family label can read `PeerSnapshot.EndPoint`.

