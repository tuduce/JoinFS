# Paths between members

Status: ready-for-agent
Blocked by: 03

Spec: [../spec.md](../spec.md), build-order slice 4. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Internal class 10 `PeerCandidates` (codec, limits, splitting), hub sends on announce and on change, receiver accepts only from the node it joined through, simultaneous probing through both firewalls, one-directional upgrade, the mixed H/A/B/C scenario (A–B on IPv6, A–C and B–C on IPv4, positions everywhere), pairs with release-1 JFP2 stay on IPv4.
