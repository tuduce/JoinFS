# Dial over IPv6 (tracer bullet)

Status: resolved
Blocked by: 01

Spec: [../spec.md](../spec.md), build-order slice 2. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Core `Join` takes an ordered address list (IPv6 first when the node has IPv6; next on dial timeout). Session layer turns typed text into that list (IPv6 literals with/without port, IPv4, hostnames resolved to all addresses, fixes D3); stop filtering to IPv4 in session parsing and `Program.cs`. The no-flipping rule (a datagram over the other family never replaces the current endpoint). Bans: source address OR legacy-id address, IPv6 entries ban the /64.

**Done when:** a dual-stack hub and node form a session over IPv6 with legacy and JFP2 both on it, and an IPv4-only node in the same session exchanges positions with both.

## Answer

Done on branch `ipv6-02-dial` (commit: the one titled "IPv6 slice 2").

- `MeshManager.Join(IReadOnlyList<IPEndPoint>, password)`: sends to the first address, moves to the next after `DialTimeout` (5 s) without a JoinReply or JoinFail, abandons the old address's guaranteed resends (`IProtocolPlugin.OnEndPointAbandoned`, legacy implements it). `Join(IPEndPoint, ...)` stays as a one-address call.
- No flipping: `RegisterNode` keeps a heard peer's endpoint when a direct datagram comes over the other family (still counted as heard). An unheard peer still takes the first datagram's source.
- Bans: `NetworkCore.BanIP` takes IPv6 (each entry bans its /64); `IsBanned(IPAddress)` for the source, `IsBanned(NodeId)` checked by the legacy plugin on the sender's id.
- `NetworkService` no longer drops IPv6-sourced datagrams.
- Session: `DialAddresses.Parse` (pure, fake resolver in tests) for literals, bracketed IPv6, hostnames resolved to all addresses (D3), IPv6 first when the socket is dual-mode. `NetBootstrap.ResolveHost` caches all addresses; `MakeEndPoint(string)` stays IPv4 (hub list, seeds, address book) but takes the first IPv4 address. `Main.Join(string)` and address-book entries dial the text; `Network` keeps the dial list for password retries and reconnects. A hub reached over IPv6 stays an IPv4 entry in the hub list; the password file skips IPv6 hubs.
- Tests: `DialOverIpv6Tests` (in-memory mesh), `DialAddressesTests`, one real-loopback `::1` test in `NetworkServiceTests`. No fixture touched.
