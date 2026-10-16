# Hub path upgrade and fallback

Status: resolved
Blocked by: 02

Spec: [../spec.md](../spec.md), build-order slice 3. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

`Candidates` tag 4 in Hello/HelloAck (only when the node has an IPv6 candidate; existing goldens untouched, add NEW goldens), Pulse probing to IPv6 candidates, path upgrade on PulseResponse, fallback when IPv6 stops answering without the peer expiring, hold-off.

## Answer

Done on branch `ipv6-03-hub-upgrade` (commit: the one titled "IPv6 slice 3").

- Wire: `Candidates` tag 4 in `HandshakeMessage` (IPv6 only, at most 4, u8 length per `WireEndPoint`, written only when the node has an IPv6 candidate). NEW goldens in `HandshakeCandidatesTests` (Hello and HelloAck, `protocol.md` Appendix A.3/A.4); the existing goldens are untouched.
- Own candidate: `IDatagramTransport.LocalIpv6Candidate` (the address the OS would send from, found by a connected UDP socket, cached 30 s; `InMemoryTransport` returns its IPv6 endpoint). Hello/HelloAck carry it.
- Core (`MeshManager.Paths.cs`): candidates per peer (self-reported, or observed from a Pulse source in the /64 of a reported one), Pulse probing for 30 s, upgrade on a PulseResponse from a candidate, IPv4 endpoint kept (`Peer.Ipv4EndPoint`), fallback after 10 s without an IPv6 answer, 5 min hold-off. Candidates from a Hello/HelloAck count only from the endpoint the peer is known at.
- Path move (slice 2 findings): `EndPoint` and `RouteEndPoint` are replaced together (new object); peers routed through the moved peer follow it; `IProtocolPlugin.OnPathChanged` moves the verified JFP2 session (no "route moved" demotion) and the legacy guaranteed de-duplication; resends already follow the current route. A Pulse that arrives over the other family than the sender's route is answered to its source (`MessageMeta.Source`, new). `AddNode`/`JoinReply` advertise the IPv4 port (`Peer.AdvertisedPort`).
- `ObservedEndPoints` ignores IPv6 observations (NAT class from IPv4 only).
- Tests: `HubPathUpgradeTests` (in-memory mesh: upgrade, firewalls, cut, fallback, hold-off, one direction, session survival, guaranteed resends and de-duplication, AddNode port, relay follow and relay loss, forged candidates), `HandshakeCandidatesTests`, `UdpTransportTests`, `ObservedEndPointsTests`. One slice-2 test (`Join_AfterMovingOn_...`) now waits out the probe window before counting sends.
- Design docs changed: `design-future.md` §4.1 (details above) and `protocol.md` §5.4 / Appendix A.

For slice 4: a `Candidates` tag from any peer that passes the endpoint check already upgrades that pair (a Hello between two dual-stack members does it over IPv4); `PeerCandidates` is still needed for pairs with no direct IPv4 path (relayed peers are not probed here) and for simultaneous probing.
