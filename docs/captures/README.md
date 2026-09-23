# Packet captures: wire-format verification evidence

Raw Wireshark captures backing `docs/network-plugin-architecture.md` §4 step 1 ("record pcaps of a
live v26.5 session ... as a second reference") and the "Live interop" item in that document's
"Still open" list. **Not committed** — see `.gitignore` (`docs/captures/*.pcapng`); they're large
(the baseline is ~240 MB) and, per the privacy note below, contain other real people's IP addresses.
This README is the only tracked thing here.

Open with Wireshark using the dissector at `JoinFS/util/wireshark/joinfs.lua` (copy into Wireshark's
Personal Lua Plugins folder, or `-X lua_script:joinfs.lua`); it decodes both the legacy wire
(`docs/network-protocol.md`) and JFP2 (`docs/reference/jfp2-protocol.md`) on the same capture.
Captured 2026-09-23. Machine's LAN addresses across all three: `192.168.1.115` (the test client) and
`192.168.1.121` (the new hub, this branch, at `joinfs.famtuduce.com:6112`).

**Privacy note:** `legacy-v26.5-baseline.pcapng` was captured with the v26.5 client joined to the
live public hub network, so it contains real IP addresses of other people's hubs and, via relayed
positions, other real users — not just the tester's own traffic. Treat it (and, to a lesser extent,
the other two, which touch a handful of public hub IPs in passing) as containing third-party data:
keep it local, never share or publish it.

## Files, and what was verified in each

### `legacy-v26.5-baseline.pcapng`

An unmodified, released **v26.5** MSFS2024 client (port 6113) joined to the live public hub network
— 251,039 link-layer packets, 5,071 on UDP/6112, across dozens of distinct remote hub IPs. Neither
side runs any code from this branch (old pre-rewrite JFP2 or the current plugin-architecture
rewrite). This is the pure reference: genuine, real-world legacy wire traffic, uncontaminated by
anything under test.

- **Verified:** every one of the 5,071 datagrams starts with the legacy magic bytes `0x0B 0x52`
  (`0x520B` little-endian) — zero JFP2 traffic, as expected for a build with no JFP2 code at all.
- **Verified:** `AircraftPosition` (`MessageId=1`) messages decode with `DataVersion=21007` — v26.5's
  actual documented `DataVersion` (`docs/protocol-changes-v26.4-v26.5.md`) — and lengths ranging
  170–223 bytes (variable, from callsign/registration/livery string lengths).

Use it to sanity-check `JoinFS.Tests/Legacy/Fixtures/*.hex` (captured mechanically from the
pre-rewrite implementation, not a real session) against how a released binary actually behaves.
**Expected, documented difference, not a bug:** the fixtures (and this branch's `LegacyPlugin`) are a
byte *superset* of v26.5 — classCode, WTC and `staticCgToGround` are trailing fields v26.5 never
sends.

### `legacy-v26.5-client-vs-new-hub.pcapng`

The same v26.5 client (still port 6113), this time talking to the **new hub** (this branch's
rewrite). 372 packets on UDP/6112; the `192.168.1.115:6113 ↔ 192.168.1.121:6112` conversation itself
is 349 legacy + 5 JFP2 (the rest are the hub's unrelated traffic with other real peers, incidentally
on the same capture).

- **Verified — legacy side matches the baseline:** the 14 `AircraftPosition` messages in this
  conversation also decode as `DataVersion=21007`, `MessageId=1`, length 200 bytes (in-range for the
  baseline's 170–223), same header layout. Same real v26.5 binary, same message shape, talking to the
  new hub instead of the public network.
- **Verified — JFP2 negotiation degrades correctly against a genuinely legacy peer:** the 5 JFP2
  datagrams are all `192.168.1.121:6112 → 192.168.1.115:6113` (hub → client only — the v26.5 client
  never answers, as expected), byte-identical retries of a Hello (`0xFA 0x02 ...`), and there are
  exactly 5 of them — matching `Jfp2Plugin.HelloMaxAttempts = 5` exactly. After the 5th, the hub falls
  back to legacy for the rest of the session (all 349 remaining messages in the pair are legacy). This
  is the concrete, on-the-wire confirmation of the "AssumedLegacy" fallback described in
  `docs/reference/joinfs-architecture.md` §5.7 — a real released client that has never heard of JFP2
  neither crashes nor gets stuck waiting on it.

This is the "through a new hub" leg of the "Live interop" checklist in
`docs/network-plugin-architecture.md`, now with wire-level evidence rather than only the earlier
behavioral report (following another aircraft, seeing correct protocol labels).

### `jfp2-new-client-vs-new-hub.pcapng`

A new-build (this branch) MSFS2024 client (port 6112) talking to the new hub, both JFP2-capable. 188
packets on UDP/6112; the `192.168.1.115:6112 ↔ 192.168.1.121:6112` conversation is 83 legacy + 97
JFP2 (magic byte `0xFA`).

- **Consistent with the design, not a bug:** a JFP2-negotiated pair still carries legacy traffic,
  because mesh kinds (Join/Pulse/Pathfinder) are legacy-only today (`docs/reference/
  joinfs-architecture.md` §5.4/§5.7) — only Position/Identity/VariableSync/etc. move to JFP2.

This isn't part of the v26.5-interop checklist (neither side is v26.5) but corroborates the JFP2
plugin's negotiate-then-split-by-kind behavior on real traffic rather than only in unit tests.

## What's still not covered here

Per `docs/network-plugin-architecture.md`'s "Still open" list: **legacy direct** (no hub in between),
a **mixed hub** capture (one legacy and one JFP2-capable peer through the new hub at the same time,
to see the translation/relay path from `docs/reference/joinfs-architecture.md` §6 on the wire), and
the **X-Plane plugin link** leg.
