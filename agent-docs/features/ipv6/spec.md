# IPv6 for dual-stack nodes

Status: ready-for-agent

Roadmap item **I1** (`docs/jfp2/roadmap.md` §2.2). This spec gives the requirements. The mechanism,
including every timing and limit, is in `docs/jfp2/design-future.md` §4.1. The wire additions are in
`docs/jfp2/protocol.md`: the `Candidates` tag (§5.4) and the internal class `PeerCandidates` (§9.8).
Terms are used as defined in `agent-docs/GLOSSARY.md`: node, hub, peer, dual-stack node, endpoint
candidate, path, direct and relayed, path upgrade, fallback, protocol upgrade. Decided with the owner
on 2026-10-16.

## Problem Statement

JoinFS works only over IPv4. Its socket is IPv4-only, and every address it parses, resolves or
remembers is filtered to IPv4. As a result:

- A pilot can't join a hub over IPv6. A hub hostname that resolves to IPv6 as well as IPv4 can even
  make the join fail, because only the first address returned is kept.
- Two pilots who both have working IPv6 still talk over IPv4, behind NAT, often through a relay,
  when a direct IPv6 path is available.
- Pilots whose IPv4 is behind carrier-grade NAT have no better path to try.

IPv6 is a stated goal of JFP2. The groundwork is in the wire already (`WireEndPoint` carries IPv6,
names are opaque, datagrams fit the IPv6 minimum MTU), but nothing uses it.

## Solution

A dual-stack node does over IPv6 everything it does over IPv4, preferring IPv6 wherever both ends
have it:

- it joins hubs over IPv6;
- it opens a direct IPv6 path to every other dual-stack node in the same session;
- it keeps IPv4 paths to peers that have only IPv4.

A dual-stack hub serves IPv4 and IPv6 members at once, and a dual-stack node has IPv4 paths to some
peers and IPv6 paths to others. Released builds and IPv4-only nodes see no change.

Each peer has one path, and all traffic to that peer uses it, legacy included. Legacy's bytes stay
frozen; only the address family they travel over changes. A path chosen as IPv4 moves to IPv6 once
the peer's IPv6 endpoint candidate answers (a **path upgrade**). It goes back to IPv4 if IPv6 stops
answering (a **fallback**). The hub tells members about each other's IPv6 candidates so that both
sides of a pair probe each other at once, which is what gets the first datagram through the
stateful firewall of a home router.

This is stage 1 of 2. Every node keeps its IPv4-shaped legacy id. IPv6-only nodes come after the
mesh moves to JFP2 (B3).

## User Stories

**Joining**

1. As a pilot on a dual-stack connection, I want to join a hub by hostname and get there over IPv6
   when the hub has an IPv6 address, so that I use the better path without doing anything.
2. As a pilot, I want a join to a hostname with both an IPv6 and an IPv4 address to still work when
   my IPv6 is broken, so that adding IPv6 to a hub never locks me out.
3. As a pilot, I want to type an IPv6 address with a port (`[2001:db8::1]:6112`) or without one
   (default port), so that I can join a hub that has no hostname.
4. As a pilot, I want an address-book entry saved from an IPv6 hostname or address to dial the same
   way next time, so that I don't retype it.
5. As a pilot who picks a hub from the global hub list, I want my path to that hub to move to IPv6
   when the hub supports it, so that hub-list joins benefit too even though the list carries IPv4
   only.
6. As a pilot, I want a hostname that resolves to several addresses to be tried properly rather than
   by its first address only, so that a hub with an IPv6 record can't fail my join (D3).

**Paths between pilots**

7. As a pilot on a dual-stack connection, I want a direct IPv6 path to every other dual-stack pilot
   in my session, so that we don't depend on IPv4 NAT or a relay.
8. As a pilot, I want an IPv4 path to a pilot who has only IPv4, in the same session where I use
   IPv6 with others, so that mixed sessions just work.
9. As a pilot behind a home router whose IPv6 firewall blocks unsolicited inbound traffic, I want
   the direct IPv6 path to open anyway, so that I don't have to open firewall ports.
10. As a pilot, I want my aircraft and everyone else's to stay visible when IPv6 to a peer breaks in
    the middle of a session, so that a flaky IPv6 route costs at most a short gap.
11. As a pilot with a flaky IPv6 route, I want my node to stop retrying IPv6 with a peer for a
    while after falling back, so that the path doesn't keep switching.
12. As a pilot, I want a peer's path never to flip between families because datagrams arrive over
    both, so that positions don't stutter.
13. As a pilot whose IPv6 works in only one direction (one side's firewall lets the probe through,
    the other's doesn't), I want each direction to use what works, so that one strict firewall
    doesn't cost both sides IPv6.
14. As a pilot, I want my node to accept other pilots' IPv6 addresses only from the hub I joined
    through, so that a stranger can't make my node send traffic to arbitrary addresses.

**Hubs**

15. As a hub operator running a dual-stack hub, I want IPv4 and IPv6 members in the same session,
    so that nobody is left out.
16. As a hub operator, I want my hub to tell its members about each other's IPv6 addresses, so that
    they can connect directly instead of through my relay budget.
17. As a hub operator on minion (a Raspberry Pi running the CONSOLE build under systemd), I want IPv6
    to work with no configuration, so that upgrading the build is all it takes.
18. As a hub operator, I want `--noipv6` to run my hub on IPv4 only, so that I can turn IPv6 off if
    it causes trouble.
19. As a hub operator, I want a banned user to stay banned when they come back over IPv6, so that
    IPv6 doesn't become a way around bans.
20. As a hub operator, I want to ban an IPv6 user by their /64, so that a ban survives their
    rotating addresses.
21. As a hub operator running a released build, I want new builds to join my hub over IPv4 exactly
    as before, so that nothing breaks while the community upgrades.

**Compatibility**

22. As a pilot on a released build (26.6 or older), I want to keep flying with pilots on new builds,
    so that the upgrade is not a flag day.
23. As a pilot on an IPv4-only connection, I want the new build to behave exactly as the old one
    did, so that IPv6 support costs me nothing.
24. As a pilot on a machine where the OS has IPv6 disabled, I want JoinFS to start on IPv4 and say
    so once in the log, so that it never fails to start because of IPv6.
25. As a pilot, I want an IPv6 on/off setting, on by default, so that I can turn IPv6 off if my
    provider's IPv6 is broken.
26. As a pilot using X-Plane, I want the link to my X-Plane plugin to keep working as before, so
    that IPv6 doesn't touch my local setup.

**Seeing what happens**

27. As a pilot, I want to see which peers I reach over IPv6, next to the existing routed/direct and
    protocol labels, so that I know IPv6 is working.
28. As the developer doing the evening field test, I want a `NETWORK:` log line for every dial
    choice, path upgrade and fallback, so that the logs show which family each pair used and why.
29. As the developer, I want a second instance on the same PC started with IPv6 off to act as an
    IPv4-only pilot, so that I can field-test a mixed session with the machines I have.

**For later work**

30. As a developer building B3 (the mesh over JFP2), I want the dual-mode socket, the endpoint
    candidates per peer and IPv6 in the in-memory network to exist already, so that IPv6-only
    nodes are a smaller step.

## Implementation Decisions

**Scope**
- Stage 1 covers dual-stack nodes only. Every node keeps its IPv4 legacy id, so membership stays
  on legacy. None of B2, B3 or roadmap stages 10 and 11 is needed. From stage 12, only "IPv6
  endpoints in the in-memory network" is done here.
- No released build ever sees IPv6: their sockets are IPv4-only. Every pair that includes a
  released build stays on IPv4, legacy and JFP2 alike.

**Transport**
- One dual-mode UDP socket on the existing port. IPv4-mapped source addresses are converted back to
  plain IPv4 before anything else sees them.
- With the IPv6 setting off (`--noipv6` on CONSOLE), or when the OS can't create a dual-mode socket,
  the transport opens an IPv4 socket as today and logs that once.
- The X-Plane plugin link and the local Euroscope, websocket and web services are unchanged (IPv4).

**One path per peer** (owned by the core's peer and mesh logic)
- A peer has one current endpoint, and every plugin sends to it. Legacy keeps its frozen bytes
  while its datagrams travel over IPv6.
- A direct datagram from the peer over the *other* family never replaces the current endpoint. It
  is still counted as heard from that peer.
- A peer on an upgraded path keeps its IPv4 endpoint for fallback.
- Path upgrade and fallback happen in the core, driven by Pulse and PulseResponse:
  - probes are Pulses sent to the peer's IPv6 candidates;
  - the upgrade happens on a PulseResponse from one of them;
  - fallback happens when the IPv6 path has answered no Pulse for a while, and starts a hold-off.
  - Timings are in `design-future.md` §4.1.
- The peer's family is part of what the core reports about each peer (the snapshot), next to the
  existing per-peer link description. Both UIs display it.

**Dialling** (interface change)
- The core's Join takes an **ordered list of addresses** instead of one. It sends to the first; if
  that gets no answer within the dial timeout, it moves to the next. The order is IPv6 addresses
  first when this node has IPv6. All of this is tested on the in-memory mesh.
- The session layer only turns what the user typed into that list:
  - an IPv6 literal with or without a port, an IPv4 literal, or a hostname resolved to *all* its
    addresses (fixes D3);
  - the dial-address parsing in the session layer and `Program.cs` stops filtering to IPv4.

**Endpoint candidates and the wire** (JFP2; legacy codecs untouched)
- **Handshake tag 4 `Candidates`, in Hello and HelloAck.** It carries the speaker's own IPv6
  endpoint candidate: the address the OS would send from, with the socket's port.
  - A node sends the tag **only when it has an IPv6 candidate**. Handshakes of IPv4-only nodes stay
    byte for byte as in 26.6, and the existing handshake goldens must not change.
  - A hub's HelloAck carrying the tag is what lets a hub-list join upgrade to IPv6.
- **Internal class 10 `PeerCandidates`** (guaranteed, offered in the internal partition): a list of
  (member name, endpoint candidates).
  - A hub sends it when it announces a member (JoinReply and AddNode time) and when a member's
    candidates change: the member's candidates to the others, and the others' candidates to the
    new member.
  - A receiver accepts it only from the node it joined through, ignores entries for itself, and
    holds entries for not-yet-registered members briefly.
- **Candidate sources.** A candidate is observed (the IPv6 source of a peer's direct datagrams,
  preferred) or self-reported through the tag.
- Only IPv6 candidates are used in stage 1. The NAT classification stays computed from IPv4
  observations only.

**Bans**
- A datagram is dropped if its source address **or** the address in its sender's legacy id is
  banned.
- The ban list also accepts IPv6 entries, and each one bans the whole /64 it belongs to.

**Unchanged**
- Relaying and the relay budget.
- Pathfinder (its targets are legacy ids).
- The per-device node limit.
- The LAN-address guess for peers behind the same NAT.
- The legacy encoder. It is never handed an IPv6-only node, because none exist in stage 1.

**Docs to keep current**
- `design-future.md` §4.1 is the design. A change of mechanism is made there first.
- `protocol.md` holds the wire. Mark edition 2 as released only when it ships.
- `roadmap.md` holds the task state.
- `docs/reference/joinfs-architecture.md` and `docs/network-plugin-architecture.md` §4 are updated
  when slice 5 lands. The README gains a note that IPv6 inside Docker needs daemon configuration.

## Testing Decisions

**What a good test is here.** It drives nodes through their public behaviour (join, tick, deliver,
the per-peer state the core reports) and asserts on what a pilot would observe:
- who is in the session;
- which family each peer is on;
- that positions arrive;
- that nothing expires.

It doesn't assert on private fields or the order of internal calls. Time is advanced with the
manual clock, never with real sleeps.

**Seams, highest first.**

1. **The in-memory mesh (`TestMesh`: the network core on `InMemoryNetwork` with `ManualClock`).**
   The main seam. Prior art: `LegacyMeshTests`, `Jfp2PluginTests`, `Jfp2RelayTests`,
   `MeshManagerReconnectTests`. The harness gains:
   - a **dual-stack test node**: one core whose in-memory transport owns an IPv4 and an IPv6
     endpoint, and sends from the one whose family matches the destination;
   - a **stateful IPv6 firewall** filter: drop unsolicited inbound IPv6 to a node until that node
     has sent to the source address;
   - a way to **cut IPv6 only** between two nodes, next to the existing `Partition`.

   Tested here:
   - a dial over IPv6, and its move to IPv4 when IPv6 doesn't answer;
   - the no-flipping rule;
   - hub path upgrade through the `Candidates` tag;
   - member-to-member upgrade through `PeerCandidates`, through both firewalls;
   - one-directional upgrade;
   - fallback without the peer expiring, and the hold-off;
   - `PeerCandidates` from a node other than the one joined through being ignored;
   - mixed sessions (hub H with dual-stack A and B and IPv4-only C: A–B on IPv6, A–C and B–C on
     IPv4, positions flowing everywhere);
   - bans by legacy-id address and by /64;
   - pairs where one side has only the release-1 JFP2 plugin behaviour, which stay on IPv4.
2. **The real socket (`NetworkServiceTests`, real loopback UDP).**
   - One dual-mode socket answers on `::1` and on `127.0.0.1`.
   - An IPv4 sender is reported as plain IPv4.
   - With IPv6 off, the node runs on IPv4 alone.
3. **Turning typed text into addresses (session layer, pure, with a fake resolver).** Prior art:
   `ObservedEndPointsTests`.
   - Bracketed and bare IPv6 literals, with and without a port.
   - IPv4 literals.
   - Hostnames with A only, AAAA only, and both, ordered IPv6 first when this node has IPv6.
4. **Wire codecs (`JoinFS.Tests/Jfp2`).** Prior art: `WireEndPointTests`, the version-skew and
   characterization tests.
   - Round trips of the `Candidates` tag and of `PeerCandidates`, including the limits, splitting a
     long list, an entry with no candidates, and unknown families.
   - A **new** golden Hello and HelloAck carrying `Candidates`.
   - **The existing golden fixtures are never regenerated.** If one changes, the implementation is
     wrong (`CLAUDE.md`, Tests).

**Not tested automatically.** The "IPv6" label in the WinForms and Avalonia session views is a thin
display of the family the core reports. The field test covers it.

**Field test** (the owner tests every evening; logs are in `%LOCALAPPDATA%\JoinFS-<build>\` and the
minion journal):
- this PC (dual-stack) and minion (dual-stack hub `joinfs-8`), plus a second instance on this PC
  started with IPv6 off;
- expected: this PC to minion on IPv6, the two local instances on IPv4 to each other, positions
  among all three.

## Out of Scope

- IPv6-only nodes, and open question K (synthetic legacy ids for them). Stage 2, after B3.
- IPv6 entries in the global hub list. They come with the directory kinds on JFP2 (B5); until then
  a path upgrade covers hub-list joins.
- Any change to legacy's codecs or bytes.
- The X-Plane plugin link, and the local Euroscope, websocket and web services.
- IPv6 inside Docker, beyond a README note.
- Choosing between several IPv6 addresses of one node beyond "the one the OS would send from".
- Relaying changes (B4).

## Further Notes

**Build order.** Each slice leaves the branch green, with IPv4 behaviour unchanged.

1. **Socket and test harness.**
   - Dual-mode socket, the setting and `--noipv6`, the IPv4 fallback.
   - The dual-stack test node, the firewall filter and the IPv6 cut.
   - *Done when:* every existing test passes unchanged and the real-socket tests pass.
2. **Dial over IPv6** (tracer bullet).
   - Join with an ordered address list; parsing and resolving (D3).
   - The no-flipping rule; bans.
   - *Done when:* dual-stack hub and node form a session over IPv6 with legacy and JFP2 both on it,
     and an IPv4-only node in the same session exchanges positions with both.
3. **Hub path upgrade and fallback.** The `Candidates` tag, probing, upgrade, fallback, hold-off.
4. **Paths between members.** `PeerCandidates`, simultaneous probing, the mixed H/A/B/C scenario.
5. **Visibility and docs.** The family in the peer snapshot and both UIs, the log lines, the docs
   listed under Implementation Decisions.
6. **Field test**, as under Testing Decisions.

**Check these while building** (pressure points in today's code; verify, don't assume):
- Does a JFP2 session survive its peer's endpoint changing mid-session? Sessions are bound to names
  (`protocol.md` §6.1), but refusals and the observed endpoint count only from the endpoint the
  Hello went to.
- Do guaranteed retransmits, legacy and JFP2, go to the peer's *current* endpoint, or to the one
  stored when the message was first sent?
- The core shares and mutates a peer's endpoint object, and its relay-loss logic compares the
  peer's endpoint and route endpoint by reference and by value. A path change must keep both
  meanings.
- AddNode advertises the port of the peer's current endpoint. That is the same socket port on both
  families; confirm it.

**Picking this up in a fresh session.**
1. Read, in order:
   - this spec;
   - `design-future.md` §4.1 (the mechanism);
   - `protocol.md` §5.4 and §9.8 (the wire);
   - `agent-docs/GLOSSARY.md`;
   - `CLAUDE.md`'s sections on threads and tests (the network thread owns the core; tests see
     FS2024-Debug only).
2. Find the next slice: the first of 1–6 whose "done when" doesn't hold. Check it against the
   **Progress** list below and the commits on the `ipv6` branch.
3. Work test-first at the seams above. Run tests with the command in `CLAUDE.md`.
4. When a slice is done, add a line to **Progress** (date, slice, commit), and update `roadmap.md`
   if I1's state changed. Record any mechanism change in `design-future.md` §4.1 first, then here
   if it changes a requirement.
5. The owner's working rules (in memory): check the 5-hour usage quota and stop at 80%; one fresh
   session per slice; short reports.

## Progress

- 2026-10-16: design approved, docs and this spec written. No code yet.
- 2026-10-16: slice 1 (socket and test harness) done, branch `ipv6-01-socket-harness`, commit 5e8d8bb.
- 2026-10-16: slice 2 (dial over IPv6) done, branch `ipv6-02-dial`.
- 2026-10-16: slice 3 (hub path upgrade and fallback) done, branch `ipv6-03-hub-upgrade`.
- 2026-10-16: slice 4 (paths between members) done, branch `ipv6-04-members`.
- 2026-10-16: slice 5 (visibility and docs) done, branch `ipv6-05-visibility`.
- 2026-10-16: review fixes, group A (behaviour) done, branch `ipv6-06-review-fixes`: PeerCandidates only direct (not forwarded), peer first heard over IPv6 keeps its id endpoint as fallback, JFP2 ban by legacy id, address-book entries without endpoint compared by text, sharper tests; ReplyEndPoint kept and documented, PeerSnapshot.Family left as first hop (documented). Stopped at the 80% quota. Resume: group B (single MaxCandidates, PathFamily helper for the DualStack label, localization of OptionsForm/SettingsForm strings, immutable ScheduleJoin pair, NetworkService cleanup), then JoinFS.UI.Tests and CONSOLE and FS2020 builds.
