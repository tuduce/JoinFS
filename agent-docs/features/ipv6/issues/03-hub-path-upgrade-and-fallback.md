# Hub path upgrade and fallback

Status: ready-for-agent
Blocked by: 02

Spec: [../spec.md](../spec.md), build-order slice 3. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

`Candidates` tag 4 in Hello/HelloAck (only when the node has an IPv6 candidate; existing goldens untouched, add NEW goldens), Pulse probing to IPv6 candidates, path upgrade on PulseResponse, fallback when IPv6 stops answering without the peer expiring, hold-off.
