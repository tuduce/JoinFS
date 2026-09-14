# JFP2 Manual Test Guide: Remaining Message Classes

## Purpose

`docs/protocol-v2-implementation-review.md` found that, as of 2026-09-14, only **Position** and
**Identity** have been confirmed working over JFP2 against a real simulator (two `CONSOLE` v26.6
instances through a v26.6 hub, plus a mixed v26.6/legacy-v26.5 run — see
`docs/protocol-v2-implementation-plan.md` Phase 4's "Update, 2026-09-14" note). This guide walks a
human tester through exercising the **other six implemented JFP2 message classes** —
**VariableSync, Event, FlightPlan, Notes/chat, Weather/WeatherReply, and Status/StatusRequest** —
using only the running application, no code changes and no packet capture required.

This is a functional/behavioral test pass. It confirms *"does the feature still work correctly when
the two peers are talking JFP2 instead of legacy"* — it does **not** attempt to prove the deeper
verification Findings 1 and 2 still need even after being fixed in code (§11 below): guaranteed-
delivery behavior under real, sustained packet loss, and allocation/GC behavior at scale. Keep that
distinction in mind when reporting results.

## 1. One-time setup

You need at minimum **two client instances** (matching versions, both built after
`docs/protocol-v2-implementation-plan.md` Phase 5) connected to each other, either directly or
through a hub that is itself upgraded (hub-mediated translation between generations isn't
implemented — `Jfp2Bridge` — so the hub only needs to be new enough to not get in the way; it does
not need to understand JFP2 itself for two JFP2 clients to negotiate with *each other* once
connected to the same session).

**Build matrix** — which configuration(s) you need depends on which features you want to cover:

| Feature | Minimum build needed | Notes |
|---|---|---|
| VariableSync, Event | Any (`CONSOLE` is fine) | Already proven reachable from `CONSOLE` — same rig as the Position/Identity test. |
| FlightPlan (automatic 5s resend) | Any (`CONSOLE` is fine) | Fires automatically for any broadcasting aircraft; no UI needed. |
| FlightPlan (manual "broadcast now" trigger) | GUI build (`FS2024`/`FS2020`/`FSX`/`P3D`/`XPLANE`) | Needs `MainForm`'s SimBrief button; `Forms\**` is excluded from `CONSOLE`. |
| Notes / chat | GUI build | Needs the Session window's chat box (`SessionForm`); not available in `CONSOLE` at all. |
| Weather / WeatherReply | GUI build **and** SimConnect (`FS2024`/`FS2020`/`FSX`/`P3D` — **not** `XPLANE`, **not** `CONSOLE`) | `Sim.RequestWeather`/`ProcessWeatherObservation` are compiled only under `#if SIMCONNECT`, which `XPLANE` and `CONSOLE` never define. |
| Status / StatusRequest | GUI build recommended (Address Book) | A CLI-only (`--hub`) path exists but is harder to point reliably at a specific already-negotiated peer — see §6.6. |

If you want to cover everything in one pass, make **at least one** of your two instances a GUI
SimConnect build (`FS2024` is the natural choice — it's what the existing test rig already
proved out for Position/Identity). The other can stay `CONSOLE` if that matches your existing setup.

**Known gotcha carried over from the Position/Identity test:** if you run multiple instances (and/or
the hub) on the same machine/IP, distinguished only by port, you need the `Node.cs` change already
sitting in the working tree that drops UDP sends addressed to the node's own IP (see
`docs/protocol-v2-implementation-plan.md` Phase 4's 2026-09-14 update for why). Confirm it's still
present (`git status` should show `JoinFS/Node.cs` modified, or check that the block around
`endPoint.Address.ToString() == "192.168.1.115"` — adjust the hardcoded address to match your actual
machine's IP if it differs — is *not* commented out) before building. If you're testing across
genuinely separate machines/IPs, you don't need this at all.

**Before testing anything else**, confirm both instances report the same app version and are
actually connected to each other in one session (same hub, or directly joined) — normal JoinFS
session setup, nothing JFP2-specific about this step.

## 2. Step 0 — confirm JFP2 negotiation (do this first, every time)

This is the one check that gates every test below. Both peers offer all 10 currently-implemented
application message classes (Position, Identity, VariableSync, Event, FlightPlan, Notes, Weather,
WeatherReply, Status, StatusRequest) in a **single** Hello/HelloAck exchange
(`LocalNode.LocalJfp2Offers`, `JoinFS/Node.cs`) — so once a peer shows as JFP2-negotiated, **every
message class in this guide is guaranteed to route to that peer over JFP2** for the rest of the
session (unless that peer later goes indirect via Pathfinder relay, which drops it back to legacy
automatically and safely — see `TryGetJfp2AppPeer`'s re-check-on-every-send behavior). You do not
need to re-check per feature.

**GUI build:** open **View → Session** to bring up the Sessions window. Find the `Protocol` column
for the peer you're testing against. It should read **`JFP2`**. (`Legacy` means that peer never
answered Hello — falls back safely, but nothing in this guide will exercise JFP2 for it; `Pending`
means negotiation hasn't completed yet, or the peer is only reachable indirectly.)

**`CONSOLE` build:** in the interactive terminal (don't launch with `--background`, which disables
the key-read loop entirely), press **`S`**. This prints a table ending in a `PROTOCOL` column, one
row per connected peer, with the same three values (`JFP2`/`Legacy`/`Pending`) — this is
`Main.MonitorSessionDetails` calling the identical `LocalNode.GetNodeJfp2State` the GUI uses.

If a pair that should negotiate shows `Legacy` or stays `Pending`: confirm both sides are actually
JFP2-capable builds (not an old binary), and that they're **directly** connected to each other, not
routed through an intermediate relay peer — `DoJfp2Handshake` deliberately never attempts Hello with
an indirect peer yet (no `Jfp2Bridge`).

## 3. How to observe results

**Primary method — watch the simulator.** For most of these features, the simplest and most
reliable confirmation is visual: does the effect show up correctly on the *other* peer's screen /
in their simulator. This is what the Position/Identity test already relied on, and it's sufficient
for a functional pass once Step 0 has confirmed the transport.

**Optional deeper trace — Monitor window (GUI only).** Open **View → Monitor**, right-click inside
it, and check **Network** and/or **Variables** (`main.monitor.network`/`main.monitor.variables`).
This turns on verbose per-message logging (`Main.MonitorNetwork`/`MonitorVariables`). There is
**no CLI flag or `CONSOLE` toggle for this** — those two checkboxes are the only switch in the whole
codebase, so if you need this level of detail from a headless peer, run that peer as a GUI build for
the duration of the test (or treat the GUI-side peer's log as your evidence, since a two-way test
only needs one side instrumented).

Either way, every `Main.MonitorEvent`/`MonitorNetwork`/`MonitorVariables` line is always also written
to a log file at `%LOCALAPPDATA%\JoinFS-<Configuration>\log-<port>.txt` (e.g.
`JoinFS-CONSOLE\log-6112.txt`), and in a `CONSOLE` build every line is additionally echoed to stdout
live. So even without the Network/Variables checkboxes, unconditional lines (like the
`BROADCAST FLOATS - <model> - vuid=value, ...` line `Sim.cs` emits once `Variables` logging is on) are
available for after-the-fact review.

**What logging can't tell you:** none of this distinguishes "sent via JFP2" from "sent via legacy" at
the individual datagram level — that would need a packet capture (Wireshark, filtering for the
`0xFA` magic byte vs. legacy `0x520B`) and is out of scope for this guide. Combining Step 0 (proves
the transport for that peer) with visible/logged evidence that the feature's data actually moved
(proves the feature) is the practical substitute, and matches how the Position/Identity test itself
was judged.

## 4. Reliability — what to expect from Event/Notes/WeatherReply, and what would now be a real bug

**Update, 2026-09-14: Finding 1 is fixed.** `docs/protocol-v2-implementation-review.md` originally
found JFP2 sent *everything* unreliable, silently downgrading **Event**, **Notes**, and
**WeatherReply** from the guaranteed (retried-until-acked) delivery they have under the legacy
protocol. That's now implemented: a guaranteed JFP2 send retries every 2 seconds for up to 5 attempts
until it's acked (`GuaranteedDone`), the same shape as the legacy protocol's own guaranteed-message
retry.

This has **not yet been tested live** (only via unit tests of the wire format) — this test pass is
actually the first opportunity to do that. So: a single dropped delivery for one of these three
classes should now self-heal within a few seconds without you doing anything (no need to manually
retry the in-sim action) — if you toggle gear once, send one chat message, or wait for one weather
reply and it never arrives even after several seconds, **that is now worth reporting as a real
finding** rather than something to shrug off and retry manually, since the whole point of this part of
the fix is that JoinFS itself does the retrying now. If you have Monitor → Network logging available
(GUI build), you can also directly confirm you're seeing this behavior: a guaranteed send that isn't
immediately acked should show up again ~2 seconds later as a retry.

## 5. Feature test: VariableSync and Event

These two are grouped because they're both driven by manipulating the aircraft in the simulator, not
by a JoinFS UI action, and both were already proven reachable from a `CONSOLE` build in the
Position/Identity test.

### 5.1 VariableSync

`VariableSync` carries whatever generic simulator variables `VariableMgr` defines for the current
aircraft type as ongoing key/value state (flaps/spoiler position, systems/lighting state, and other
"L:var"-style values) — distinct from Position's own fixed motion-state fields
(lat/lon/alt/pitch/bank/heading/velocities/control-axis positions), which are never carried here.

1. With both peers connected and Step 0 confirming `JFP2`, sit in the broadcasting aircraft (the
   same one whose movement mirrored correctly in the Position/Identity test).
2. Move a control that is **not** one of Position's own fields — flaps through a couple of detents,
   spoilers, or a lighting switch, are reasonable choices — and hold it for a second or two (the sync
   timer is 5Hz).
3. **Expected:** within well under a second, the peer's injected aircraft mirrors the new flap/
   spoiler/light state.
4. **Optional trace:** enable Monitor → Variables on the sending side; you should see a
   `BROADCAST FLOATS - <model> - <vuid>=<value>, ...` line each cycle listing the changed value(s).
5. **If you can identify a custom string-valued `L:var`** (the sync also carries `VariableKind.String8`
   entries, e.g. some ATC/callsign-style aircraft state — check `BROADCAST FLOATS`' neighboring log
   output or an aircraft's `variables.txt`/panel state file for a string-typed one), setting it to a
   value **longer than 8 characters or containing non-ASCII text** and confirming it arrives on the
   peer unmangled would directly confirm the Finding 5 fix (`docs/protocol-v2-implementation-
   review.md` §4/§6) — this specific case has not been exercised live yet, only via unit tests.
   Not essential to a basic pass; a nice-to-have if you happen to have an aircraft with one available.

### 5.2 Event

`Event` carries a small set of discrete simulator events JoinFS explicitly maps and forwards
(`Sim.cs`'s `EVENT_00011000`–`EVENT_0001100A` — eleven raw numeric SimConnect client events). **This
review could not conclusively identify, from source alone, which specific in-cockpit action fires
each of these eleven IDs** (they are mapped as raw numeric SimConnect events, `#0x00011000`.., not by
a named SimConnect event, and no comment in the codebase documents their real-world meaning).
Rather than guess and risk sending you after the wrong control, use discovery:

1. On the GUI/SimConnect side, enable Monitor → Network.
2. Try a range of common discrete cockpit actions one at a time — gear up/down, various light
   switches, parking brake, autopilot master, etc. — pausing a moment between each.
3. Watch the Monitor log for any line indicating a SimEvent/Event send (search the log for `Event`
   or `SimEvent`). Note which action(s) produced one.
4. Once you've identified a reliable local trigger, repeat it and confirm the corresponding state
   change appears correctly on the peer's injected aircraft (or, at minimum, that the log shows the
   send going out with no error each time).
5. If you can positively identify which cockpit control maps to which `EVENT_000110xx` id in the
   process, please note it in your results (§10) — that's a gap worth closing in the docs regardless
   of this test's outcome.

Per §4, Event now retries automatically for a few seconds if the first attempt is dropped, so you
shouldn't need to manually repeat the toggle — if the state change never propagates at all after a
several-second wait, that's worth reporting rather than dismissing as an expected one-off drop.

## 6. Feature test: FlightPlan

### 6.1 Automatic path (works on any build, including `CONSOLE`)

1. With both peers connected and Step 0 confirming `JFP2`, have the broadcasting side's aircraft
   carry *any* flight plan data (even a partially-filled one set through the sim's own flight-plan
   UI, or whatever the aircraft already has loaded).
2. Wait up to 5 seconds (`flightPlanTimer`) — `BroadcastFlightPlanUpdate` fires periodically for
   every broadcast aircraft without any JoinFS UI action.
3. **Expected:** the peer shows the updated flight-plan fields for that aircraft (departure/
   destination/route/etc., wherever your peer's UI or session details surface flight-plan data).

### 6.2 Manual trigger (GUI build only)

1. On the GUI side, configure a SimBrief username (Settings) if not already set.
2. Click the SimBrief button on the main window (`Button_SimBrief`) — this fetches the plan and
   calls `BroadcastUserFlightPlanNow` immediately, rather than waiting for the 5s timer.
3. **Expected:** same as above, but observable immediately instead of within 5 seconds.

## 7. Feature test: Notes / chat

**GUI build required on the sending side** (`SessionForm`'s chat box; `CONSOLE` has no equivalent).

1. With both peers connected and Step 0 confirming `JFP2`, open **View → Session** on the GUI
   instance to bring up the Session window.
2. Type a short message into the chat box and click **Send** (do **not** start it with `.` — text
   starting with `.` is treated as a local command and is never sent over the network).
3. **Expected:** the message appears in the peer's Session window chat pane (if the peer is also a
   GUI build) or, at minimum, is logged as received (check the peer's Monitor/log if it's headless).
4. Per §4, `Notes` now retries automatically (matching legacy's own guaranteed delivery) — you
   shouldn't need to resend manually. If a message never shows up even after a several-second wait,
   that's worth reporting rather than dismissing as an expected one-off drop.

## 8. Feature test: Weather / WeatherReply

**Requires a SimConnect GUI build on the broadcasting side** (`FS2024`/`FS2020`/`FSX`/`P3D` — not
`XPLANE`, not `CONSOLE`; see §1's build matrix for why).

1. With both peers connected and Step 0 confirming `JFP2`, on the SimConnect side, change the
   simulator's weather (in-sim weather menu, switch to/from live weather, or load a different weather
   preset/theme — whatever your simulator exposes).
2. Wait up to 60 seconds (`requestWeatherTimer`) for `Sim.RequestWeather` to poll and
   `ProcessWeatherObservation` to broadcast the new METAR automatically — this is not user-triggered
   beyond changing the weather itself.
3. **Expected:** the peer's weather updates to match (however your peer surfaces that — in-sim
   weather sync, a displayed METAR string, etc.).
4. **Note on `WeatherReply` specifically:** this review found no live code path in the current
   codebase that originates a `WeatherRequest` (it has zero call sites — see
   `docs/protocol-v2-implementation-review.md` §2.7), so there is currently no way to manually trigger
   a `WeatherReply` send from a modern JoinFS instance. This part of the message catalog is
   effectively untestable through normal use today; note this in your results rather than spending
   time hunting for a trigger that doesn't exist in the current UI.

## 9. Feature test: Status / StatusRequest

This is the least deterministic one to trigger through normal use (an existing implementation-plan
note already flagged this). Two approaches, in order of reliability:

### 9.1 Address Book (GUI build, most reliable)

1. On the GUI side, open the Address Book.
2. Add an entry pointing at the **same peer you already have a JFP2-negotiated session with**
   (i.e., the same address/port you confirmed as `JFP2` in Step 0) — this matters because Status/
   StatusRequest traffic normally targets hubs/directory endpoints, which are usually *not* the same
   endpoint as a session peer; reusing your already-negotiated session peer's address is what makes
   this resolve to the JFP2 path (`TryGetJfp2AppPeer` matches by endpoint).
3. With the Address Book window open and visible (it only polls while visible — `DoAddressBook` is
   gated on `addressBookForm.Visible`), it periodically sends a `StatusRequest` to that entry and
   should receive a `Status` reply, refreshing the entry's displayed info (user count, etc.).
4. **Expected:** the Address Book entry's status fields populate/update.

### 9.2 CLI hub join (either build, lower confidence)

1. Launch (or re-launch) an instance with `--hub --join <address>` pointed at a peer that is already
   a `nodes`-mesh member you've confirmed as `JFP2` in Step 0.
2. This calls `SubmitHub`, which is JFP2-migrated — but per
   `docs/protocol-v2-implementation-plan.md` Phase 2's own notes, hub/address-book endpoints are
   "essentially never also legacy-mesh peers" in normal use, so this path is not guaranteed to
   actually hit an already-negotiated endpoint. Treat a success here as a bonus confirmation, not
   the primary test — §9.1 is the reliable one if you have a GUI build available.

## 10. Recording your results

For each feature tested, note: date, build versions/configurations of both peers, whether Step 0
showed `JFP2` for the pair, pass/fail, and (if failed) whether it was a one-off that self-healed within
a few seconds (expected, and itself worth noting as confirmation the Finding 1 retry logic works) or a
message that never arrived at all (a real bug now, per §4 — Finding 1 is fixed, so unlike before this
guide was updated, a permanently-lost Event/Notes/WeatherReply is no longer expected behavior). Feed
confirmed results back into `docs/protocol-v2-implementation-plan.md` (in the relevant phase's "Manual
verification" section, matching the existing convention) and into
`docs/protocol-v2-implementation-review.md` §3, the same way the 2026-09-14 Position/Identity result
was recorded.

## 11. What this test pass does not prove

Even a full pass through every feature above does not close everything `docs/protocol-v2-
implementation-review.md` §6 still flags as needing a different methodology entirely:

- **Finding 1's retry/ack loop under real, sustained packet loss** — this guide's §4 update means a
  single dropped datagram should now self-heal within a few seconds, and normal use (including
  whatever ordinary loss your test network has) exercises the retry path to some extent. But actually
  *proving* the retry/ack/give-up logic behaves correctly under deliberately induced, sustained loss
  (not just whatever loss happens to occur) needs a lossy network link (`tc netem`-style tooling, or
  equivalent) or a temporary code-level fault injection — a different kind of test than this guide's
  "does the feature work" pass.
- **Finding 2's allocation fix under real broadcast-peer load** — this is a GC/performance
  characteristic, not a functional one; confirming the fix actually reduces GC pressure at scale needs
  a profiler (`dotnet-trace`/`dotnet-counters` attached to a broadcasting instance with many peers)
  rather than manual observation, regardless of how this guide's tests turn out.

Both are tracked in the review doc's §6 and should be treated as separate follow-up work, not blockers
on finishing this guide's functional pass.
