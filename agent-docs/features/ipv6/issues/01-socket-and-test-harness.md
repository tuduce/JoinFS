# Socket and test harness

Status: resolved
Blocked by: -

Spec: [../spec.md](../spec.md), build-order slice 1. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Dual-mode UDP socket on the existing port (IPv4-mapped sources converted to plain IPv4), the IPv6 setting and `--noipv6`, the IPv4 fallback with a one-time log line. Test harness: dual-stack test node on `TestMesh`/`InMemoryNetwork`, stateful IPv6 firewall filter, IPv6-only cut next to `Partition`.

**Done when:** every existing test passes unchanged and the real-socket tests (`NetworkServiceTests`: `::1` and `127.0.0.1` on one socket, IPv4 sender reported as IPv4, IPv6 off runs IPv4-only) pass.

## Answer

Done on branch `ipv6-01-socket-harness`, commit 5e8d8bb.

- `UdpTransport`: dual-mode socket (`Ipv6Enabled`, default on), IPv4-mapped sources converted to plain IPv4 on receive, IPv4 destinations mapped on send, IPv4 socket when IPv6 is off or the OS cannot make a dual-mode one (`FallbackReason`, `DualModeFactory` test seam).
- `NetworkService`: `Ipv6Enabled`, `Snapshot.DualStack`, one-time `NETWORK:` log line on fallback. IPv6-sourced datagrams are dropped before the core until slice 2 (the core's peers are still IPv4 legacy ids).
- Setting `Ipv6` (default true; `Settings.settings`, designer, `App.config`), `Main.settingsIpv6`, command line `-noipv6` (all builds, so a second local instance can act as an IPv4-only pilot). Not done here: the Options/Settings UI entry and the `--noipv6` help text (slice 5).
- Harness: `InMemoryNetwork.Attach(v4, v6, ...)` dual-stack transport, `FirewallIpv6`; `TestMesh.AddDualStack`, `Firewall`, `CutIpv6`; `TestNode.EndPoint6`/`Transport`.
- Tests: `UdpTransportTests`, `DualStackHarnessTests`, two in `NetworkServiceTests`. Existing tests and fixtures unchanged (FS2024-Debug x64 all pass). CONSOLE builds.
