# Socket and test harness

Status: ready-for-agent
Blocked by: -

Spec: [../spec.md](../spec.md), build-order slice 1. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Dual-mode UDP socket on the existing port (IPv4-mapped sources converted to plain IPv4), the IPv6 setting and `--noipv6`, the IPv4 fallback with a one-time log line. Test harness: dual-stack test node on `TestMesh`/`InMemoryNetwork`, stateful IPv6 firewall filter, IPv6-only cut next to `Partition`.

**Done when:** every existing test passes unchanged and the real-socket tests (`NetworkServiceTests`: `::1` and `127.0.0.1` on one socket, IPv4 sender reported as IPv4, IPv6 off runs IPv4-only) pass.
