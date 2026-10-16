# Dial over IPv6 (tracer bullet)

Status: ready-for-agent
Blocked by: 01

Spec: [../spec.md](../spec.md), build-order slice 2. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Core `Join` takes an ordered address list (IPv6 first when the node has IPv6; next on dial timeout). Session layer turns typed text into that list (IPv6 literals with/without port, IPv4, hostnames resolved to all addresses, fixes D3); stop filtering to IPv4 in session parsing and `Program.cs`. The no-flipping rule (a datagram over the other family never replaces the current endpoint). Bans: source address OR legacy-id address, IPv6 entries ban the /64.

**Done when:** a dual-stack hub and node form a session over IPv6 with legacy and JFP2 both on it, and an IPv4-only node in the same session exchanges positions with both.
