# Field test

Status: resolved
Blocked by: 05

Spec: [../spec.md](../spec.md), build-order slice 6. Mechanism: `docs/jfp2/design-future.md` §4.1.

Owner's evening field test as described in the spec's Testing Decisions. Not for agents.

## Answer

Field test of 2026-10-16: this PC with three FS2024 instances (6113 and 6114 dual-stack, 6115 with `-noipv6`)
and the dual-stack CONSOLE hub on minion (`joinfs-8`), all on one LAN. Logs read with `NETWORK:` lines on.

Confirmed:
- 6113 and 6114 dialled the hub over IPv6 and got the hub's candidate; 6115 joined over IPv4.
- The 6113-6114 path upgraded to IPv6 (JFP2 session moved with it); 6115 stayed on IPv4 with both.
- IPv6 cut for about a minute: both paths fell back to IPv4 after 10 s, nobody expired, the 300 s hold-off held,
  and about a second after it ended all paths upgraded again, onto the PC's new IPv6 address.
- The IPv6 label in the session view was correct, including after the switch back.
- Found and fixed: sends to an unset endpoint (`0.0.0.0:0`) failed on the IPv4-only socket (commit 3e7aad5).

Not covered (open):
- Hub-list join and its upgrade; hostname resolving to several addresses (D3 in the field); bans by legacy-id address
  and /64 in the field; a released 26.6 build as a peer; positions during a cut (MSFS froze when the adapter's IPv6 was disabled).
- Nothing was tested across a router firewall or NAT: everything was on one LAN, so the stateful-firewall case
  (user stories 9 and 13) is only covered by the in-memory tests.
- Source of the unset endpoint not found; `UdpTransport.Send` now drops such sends.
