# ATC clients on the JoinFS mesh: design and plan

**Status:** plan agreed with the owner on 2026-11-20; no code written. Branch: `atc-fsd`.

An ATC client (EuroScope first) connects to the JoinFS running on the same PC, sees the pilots of the
session on its scope, and works with the other controllers of the session (tracks, handoffs, shared
data, ATC chat) over the mesh. Voice stays on TeamSpeak and is not part of this work.

How to read this: §1 the people, §2 what a controller needs, §3 the decisions and the alternatives
that were turned down, §4 the design, §5 the stages (one agent session each), §6 status and session
rules, §7 risks and open questions. The FSD wire is described in `FSD-protocol.md` next to this file.
Issue #125 is background, not a specification: where it and this plan differ, this plan holds.

---

## 1. The people in the stories

| Name | Who |
|---|---|
| **Carla** | Controller. Runs JoinFS and EuroScope on one PC, no simulator. Logs in as `EDDM_TWR`. |
| **Ben** | Controller on another PC, `EDDM_APP`. Sometimes also flies, with a simulator running. |
| **Pia** | Pilot on the current build, callsign `DLH4TK`. |
| **Olaf** | Pilot on a released build that speaks only the legacy protocol. |
| **Hank** | Runs the console hub the session meets on. |

## 2. What a controller needs from JoinFS

| # | The controller | FSD | Who serves it |
|---|---|---|---|
| 1 | logs in and out | `$ID`, `#AA`, `%`, `#DA` | the local JoinFS |
| 2 | sees traffic with flight plan and squawk | `@`, `$FP`, `#DP` | the local JoinFS, from what the mesh already carries |
| 3 | sees the other controllers | `#AA`, `%`, `#DA` | the mesh (new: `AtcStation`) |
| 4 | tracks, drops, hands over, accepts | `$CQ` IT/DR/HT/WH, `$HO`, `$HA`, `#PC` IH/HC | the mesh (new: `AtcRelay`) |
| 5 | shares scratchpad, temporary and final altitude, assigned squawk | `#PC`, `$CQ` | the mesh (`AtcRelay`) |
| 6 | chats with controllers | `#TM` to a station or `@49999` | the mesh (`AtcRelay`) |
| 7 | sees "squawk ident" and the transponder mode | mode letter in `@` | two new simulator variables |
| 8 | publishes an ATIS | second connection `<ICAO>_ATIS`, `$CQ`/`$CR` ATIS | the mesh (`AtcStation` slot 1, `AtcRelay`) |
| 9 | amends a flight plan | `$AM` | the mesh (`AtcRelay`), controllers only |

Instructions to a pilot go by voice. Nothing a controller does changes a pilot's simulator.

## 3. Decisions

Decided with the owner on 2026-11-20. Do not re-open them in an implementation session; a stage that
cannot be built as decided stops and reports (§6).

| # | Decision | Turned down, and why |
|---|---|---|
| D1 | **The FSD server is inside JoinFS**, one listener per instance, for the ATC client on the same PC. | *One FSD server on the hub for all controllers:* needs an open TCP port and makes the hub a single point; controllers would not be mesh members. *An external bridge* (today's `joinfs-euroscope-bridge`): a second program to install, and it cannot make a controller visible to the mesh. |
| D2 | **JoinFS is an FSD router, not a controller-state store.** The mesh carries a typed presence (`AtcStation`) and the controllers' own FSD packets (`AtcRelay`). Who tracks what, the scratchpads and the assigned data stay in the ATC clients, as on VATSIM, whose server does not store them either. | *Typed replicated state* (issue #125: Station, AtcTrack, AtcCoord, Atis, AtcText, NodeProfile, AtcDigest): JoinFS would have to copy EuroScope's semantics, with version counters, a conflict rule and a re-sync, and follow every client change. *Router plus a read-only track table:* nothing in this plan reads such a table; add it when a consumer exists. |
| D3 | **Off by default.** A setting (and a switch) turns the listener on; while off nothing is created: no socket, no thread, no timer. | — |
| D4 | **Loopback only**: `127.0.0.1` and `::1`, port 6809 unless set. No authentication, because nothing outside the PC reaches the listener. | *LAN as an option:* an unauthenticated login for anyone on the network. Ask again if a controller needs EuroScope on a second PC. |
| D5 | **One station per instance.** The first ATC login is the station; another is refused. The one exception is the same controller's ATIS connection (`*_ATIS`, stage 10). | *Several controllers on one JoinFS:* that is the hub-server model of D1. |
| D6 | **JFP2 only.** The two new classes are appended to JFP2; the legacy wire stays frozen. A legacy pilot is still seen and controlled (his aircraft data arrives as today) and sees the controller through the existing `PeerInfo` fields (D10). | *New legacy messages:* forbidden by the freeze. |
| D7 | **Station presence is soft state.** Sent on change, to a new member, and every 10 s; dropped after 35 s of silence or when the member leaves. Not guaranteed. | *Guaranteed on change plus a digest:* more machinery for the same result; a controller table is a handful of small messages. |
| D8 | **No range filtering.** A station receives every aircraft and every station of the session. | *Visibility ranges as on VATSIM:* a JoinFS session is tens of aircraft, not thousands. |
| D9 | **The pilot's flight plan is the pilot's.** A controller's amendment is shown to the controllers only and never written back to the pilot. | *Writing it back:* a second owner for the flight plan. |
| D10 | **The logged-in station is this node's ATC presence.** It fills the fields the old manual ATC mode filled (airport, level, frequency), so legacy peers, Whazzup, the hub's status and the activity circle keep working. | — |
| D11 | **The old ATC pieces go in the last stage:** the manual ATC mode (settings, `-atc`, `-airport`) and the `euroscope.txt` export (`Euroscope.cs`). From then on an FSD login is the only way to be a controller. | *Keeping them:* two ways to be ATC, one of them with no screen in the new UI. |
| D12 | **Code layout.** `JoinFS.Fsd/` is a standalone library (FSD text only). `JoinFS/Atc/` is the gateway and the listener. `JoinFS/Session/AtcSession.cs` is the mesh side. None of it depends on a simulator or on a build symbol, so every configuration has it, CONSOLE included. | *Everything in `JoinFS/`:* the parser could not be tested or reused alone. |

**Out of scope** (each needs its own plan): text between controllers and pilots; station events on
the hub's WebSocket and webhook, TeamSpeak channels, the web map; new flight plan fields (EOBT,
equipment, endurance); Aurora and clients that do not speak FSD; voice; supervisor functions
(`$!!`, wallop); landline (`#PC` IC/IK/...); weather and METAR for the ATC client.

## 4. Design

### 4.1 Parts

```
 EuroScope ──TCP, loopback──▶ FsdListener ──lines──▶ AtcGateway ◀──▶ AtcSession ◀──▶ mesh (JFP2)
 (FSD text)                   (I/O threads)          (app thread)     (app thread)     AtcStation, AtcRelay
                                                        ▲
                                                        └── IAtcTraffic: the aircraft of Sim.View
```

| Part | Where | What it owns | Thread |
|---|---|---|---|
| `JoinFS.Fsd` | `JoinFS.Fsd/` (new library; no reference to JoinFS, no build symbols; the Dockerfile copies its csproj before restore) | Splitting and building FSD lines, the packet verbs, the pitch/bank/heading packing, frequency and callsign rules. No sockets, no state. | any |
| `FsdListener` | `JoinFS/Atc/FsdListener.cs` | The TCP listener and its connections. Reads lines and posts them to the app thread (`Main.EnqueueCommand`); writes the lines the gateway queues. | its own (async I/O) |
| `AtcGateway` | `JoinFS/Atc/AtcGateway.cs` | The FSD server as the client sees it: login, the local station, answers given as `SERVER`, the traffic feed, where a client's line goes. Driven by `Tick(now)` from `Main.DoWork`. Reaches the rest only through interfaces (`IFsdConnection`, `IAtcTraffic`, `IAtcMesh`), so tests run it with fakes. | app |
| `AtcSession` | `JoinFS/Session/AtcSession.cs` | The station table of the session, sending and expiring `AtcStation`, sending and checking `AtcRelay`. A session part like `PeerTable`; implements `IAtcMesh`. | app |
| `LocalAtc` | `JoinFS/Atc/LocalAtc.cs` | An immutable record of this node's ATC presence (active, callsign, airport, level, frequency, latitude, longitude). Published by replacing the reference (`Main.LocalAtc`), so the sim thread and the UI read it without a lock. | written on app, read anywhere |

Rules that follow from the thread model (`docs/architecture.md` §2):
- The gateway and the session part run under `conch` on the app thread and never block.
- The listener never touches the gateway's state; it only posts.
- Aircraft are read from the `Sim.View` snapshot, never from `sim.objectList`.
- Messages go out through `INetworkOutbox`, like every other session part.

**`LocalAtc` (stage 3).** `LocalAtc.Resolve(station, manual settings)` in `JoinFS/Atc/LocalAtc.cs` is a pure function;
`Main.RefreshLocalAtc()` calls it when `AtcGateway.Station` changes (checked every `DoWork` tick by reference) and
after the manual settings change, publishes the result in `Main.LocalAtc`, and sends a `PeerInfo` to every node at once
when a peer would see a difference (`SamePresenceAs`: active, airport, level, frequency, station online; the
station's position alone is not one). Rules the stage added:
- An online station decides, even an observer: an observer is not active and does *not* fall back to manual mode.
- A station that has not sent its first `%` yet is online but not active (its facility and frequency are unknown).
- `StationOnline` (any main station logged in, observer included) drives `FeedFullRate`; `Active` drives `Atc`.
- `ILocalProfile.AtcCallsign` is the whole callsign. The local controller shows it in `GetLocalCallsign`, the live
  session list and the hub's own user list; another node still shows airport plus level until stage 7.
- `Main.TryGetLocalAtcPosition` gives the station's own position, else the airport's; `Sim` (activity circle),
  `Whazzup` and `Euroscope` use it. The hub's own user list still takes the airport's position (it needs an airport
  it lists), so it ignores the station's.

### 4.2 The client's connection

1. On accept, the gateway sends nothing: the client speaks first, `$ID` then `#AA`. (Stage 2: the working
   `joinfs-euroscope-bridge` sends no `$DI`, because EuroScope rejects a server that announces "VATSIM FSD". If the
   evening check shows EuroScope needs a greeting, add one in `AtcGateway.OnConnected`; see `euroscope-setup.md` §4.)
   After `#AA` the gateway sends a welcome `#TM` and the capability exchange (`$CQ ..:CAPS`, `$CR ..:ATC`, `$CR ..:CAPS`,
   `$CR ..:IP`), and every 30 s a keep-alive `#DLSERVER:*:0:0`; all three as the bridge does.
2. `#AA` is accepted when the callsign is valid, no station is logged in on this instance, and no
   station of the session has the callsign. Otherwise `$ER` and the connection is closed. CID,
   password and token are ignored; the rating is taken as sent.
3. `%` (every 15 s from the client) updates the station: frequency, facility, visibility, position.
4. `#DA`, or the connection closing, logs the station off.
5. A line longer than 2,048 bytes, or a verb before login other than `$ID`/`#AA`, closes the
   connection.

A station is: slot (0 main, 1 ATIS), callsign, name, rating, facility, visibility, frequency,
latitude, longitude. It is *online* from an accepted `#AA` until logoff.

### 4.3 Where a client's line goes

The gateway looks at the verb and the `To` field.

| `To` | What happens |
|---|---|
| `SERVER` | Answered here: `$PI`→`$PO`; `$CQ` ATC (is this callsign a controller: yes for an online station that is not an observer), IP, FP (the aircraft's `$FP`). Anything else is ignored. |
| the callsign of an online station on another node | `AtcRelay` to that node. |
| the other connection of this instance (main ↔ ATIS) | Delivered here. |
| `@94835` (all controllers), `@49999` (ATC chat), `*A`, `*`, `@<frequency>` | `AtcRelay` to every node that has an online station. Pilots receive nothing (out of scope). |
| an aircraft's callsign | `$CQ` RN and FP are answered here from the mesh data. Anything else is dropped. |
| anything else | `$ER` "no such callsign". |

Only these verbs are ever relayed: `$CQ`, `$CR`, `#PC`, `$HO`, `$HA`, `#TM`, `$PI`, `$PO`, and from
stage 11 `$AM`. `#AA`, `#DA` and `%` are never relayed: they become `AtcStation`. `$!!`, `$ZC`,
`$ZR` and `#PC` landline types are dropped.

### 4.4 A line arriving from the mesh

`AtcSession` delivers an `AtcRelay` to the gateway only when all of these hold; otherwise it drops
it and counts the drop:
- its verb is in the list of §4.3;
- it has no control character;
- its `From` is the callsign of a station that the *origin node* announced and that is online (a
  node can speak only for its own station);
- this instance has an online station.

The gateway writes it to the connection whose callsign is the line's `To`, or to the main connection
for a group address. It never interprets the payload.

### 4.5 The two JFP2 classes

Appended to the application partition at the next unused numbers (23 and 24 when this was written);
canonical messages in `JoinFS.Messages`; rules of `docs/network/jfp2/implementation.md` §5.

**`AtcStation`** (not guaranteed; the origin is the owner; one per slot)

| Field | Type | Notes |
|---|---|---|
| Slot | u8 | 0 main station, 1 ATIS |
| Flags | u8 | bit 0 Online. Clear means "logged off": the receiver drops the station at once. |
| Facility | u8 | FSD value: 0 OBS, 1 FSS, 2 DEL, 3 GND, 4 TWR, 5 APP, 6 CTR |
| Rating | u8 | as the client sent it |
| Visibility | u16 | nautical miles |
| Frequency | u32 | kHz (128550 = 128.550); 0 none |
| Latitude, Longitude | f64 × 2 | degrees |
| Callsign | string | limit 12 |
| Name | string | limit 32 |

Sent to every member: when the station logs in or changes, to a member that just joined, and every
10 s. A logoff is sent once with Online clear. A receiver drops a station it has not heard for 35 s,
and every station of a member that left.

**`AtcRelay`** (guaranteed, unicast)

| Field | Type | Notes |
|---|---|---|
| Line | string | One FSD line without its line ending, limit 1,000 bytes. A longer line is not sent; the gateway logs it. |

A class is agreed per hop, so a relay that lacks these classes (an older hub) does not pass them.
Controllers with a direct link are not affected; the release notes say hubs must be current.

### 4.6 Callsigns

- **Divided positions.** A station callsign is what the client sent, whole: `EDDM_TWR`,
  `EDDM_N_TWR`, `EDDM_S_TWR`, `EDMM_ALB_CTR`. It is valid when it is 3 to 12 characters of
  `A-Z 0-9 _`, with any number of `_`. JoinFS never builds or shortens it and never reads the kind
  of position from its suffix: that is the `Facility` field. Two stations are the same only when the
  whole callsign is the same, so north and south tower are two stations, each on its own node with
  its own frequency. Wherever this build shows a controller of this build (monitor, settings card,
  Whazzup, lists), it shows the whole callsign.
- **What an older build sees.** The frozen `PeerInfo` has only airport, level and frequency, so a
  legacy peer shows `EDDM_N_TWR` and `EDDM_S_TWR` both as tower at EDDM, told apart by frequency.
  Accepted: the airport field must stay a real airport code, because peers look its position up.
- **Two stations, one callsign.** A login is refused when the callsign is in the station table. If
  two logins cross, both nodes see the other's `AtcStation`: the node with the lower `NodeId` keeps
  the callsign, the other sends its client `$ER` and closes the connection.
- **Aircraft.** Controllers name an aircraft to each other by callsign (`$HO`, `#PC`), so every
  node must give an aircraft the same FSD callsign. The rule is a pure function in `JoinFS.Fsd`:
  1. the flight plan's callsign, upper case, only `A-Z 0-9 -`, cut to 10 characters;
  2. if that is empty: `JFS` and four hex digits of a hash of (owner `NodeId`, object id);
  3. if two aircraft have the same result: the one with the lowest (owner `NodeId`, object id)
     keeps it, each other one gets the result cut to 8 characters plus two hex digits of that hash.

- **The packing of pitch, bank and heading** (`FsdPitchBankHeading`): the document gives the 10-bit layout but not the signs. Pitch and bank are written negated, as FSD's own code does, so a nose-up pitch of 0.35 degrees is the step 1023; the document's example `4290776072` reads that way. Stage 4's check against EuroScope confirms it; if a tag shows the aircraft banked the wrong way, change the sign in that one class.
- **Aircraft callsign collisions** (§4.6 rule 3): a suffixed name that is already in use (another aircraft's name, or another suffixed one) gets the next hash value, in the order of (owner, object id), so every node still computes the same names.

### 4.7 What the client is told about traffic

For every `Sim.Aircraft` in `Sim.View.Objects` (the set `Euroscope.cs` uses today: the session's
aircraft, the user's own, the recorder's):
- `@` once a second: mode letter (`N` until stage 9), callsign, squawk (variable
  `transponder code:1`), position, altitude in feet, ground speed, packed pitch/bank/heading.
- `$FP` when first seen and when the flight plan changes.
- `#DP` when it has been gone from the view for 5 s.

While a station is online the node reports "simulator connected" to its peers
(`ILocalProfile.FeedFullRate`), so pilots send it positions at the full rate with no simulator.

**Traffic (stage 4).** `AtcTrafficFeed` (`JoinFS/Atc/`) owns what has been sent: the aircraft known to the client, with the
callsign and the `$FP` text sent for each. `AtcGateway.Tick` runs it once a second (`AtcTrafficFeed.IntervalMs`, R2's one
constant) for a logged-in client only; a login or logoff resets it, so a new client gets everything again. Rules the stage added:
- An aircraft is `IAtcTraffic`'s `AtcAircraft` (a value copy); `SimViewTraffic` builds the list from `Sim.View`. Its key is
  (owner, object id): the owner is the node's 8 wire bytes as a little-endian number (this node's own id for the user's aircraft,
  so every node computes the same name), `ulong.MaxValue` for the recorder's.
- Per tick, in this order: `#DP` for an aircraft whose assigned callsign changed, `#DP` for one gone 5 s, then for every aircraft
  the `@` and, when it is new or its `$FP` text changed, the `$FP`. The `@` comes first because the target exists from its
  first `@` (no `#AP` is sent). An unsolicited `$FP` is addressed to `*A`; the answer to a query to the asker.
- `$FP` mapping (`AtcFlightPlans`): rules `I`/`V`/`S`/`D` from the text (empty is `V`); equipment = the ICAO type; speed from
  `N0450`, `450` or `K0833` (Mach and text are 0); departure, destination and alternate in upper case; cruise level as typed;
  remarks and route as typed. A colon or control character is replaced by a space in every field except the route (the last
  field keeps its colons). Times, time en route and fuel are empty or 0.
- `@`: mode `N`, rating 1, squawk from `transponder code:1` read as BCD (0x1200 is 1200; a digit above 7 gives 0000), altitude
  and ground speed rounded, pressure correction 0. Ground speed is the horizontal part of `netVelocity` (as the WebSocket feed).
  Attitude: JoinFS pitch is nose down positive, so it is negated for `FsdPitchBankHeading`; bank is taken as SimConnect's
  `PLANE BANK DEGREES` (left wing down positive) and negated. Unverified: change it in `SimViewTraffic` only (§4.6).
- Queries (§4.3): `$CQ ..:SERVER:FP:<callsign>` and `$CQ <callsign>:FP` answer with the `$FP`; `$CQ <callsign>:RN` answers
  `$CR <callsign>:<asker>:RN:<owner's nickname>::1`. The callsign is the assigned FSD one. An unknown aircraft gets `$ER 008`
  from `SERVER` and nothing otherwise.

### 4.8 Settings and switches

| Name | Default | Meaning |
|---|---|---|
| setting `AtcFsd` | false | The listener runs. |
| setting `AtcFsdPort` | 6809 | Its port. |
| switch `-fsd [port]` | — | Turn it on for this run (all builds; the way to use it on CONSOLE). |

## 5. Stages

One stage is one agent session and one commit, named `Stage N of atc-fsd`. A stage is done when its
tests pass with the CI invocation (`dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug
-p:Platform=x64`), all six configurations build, and its "done when" holds. The story of a stage is
what the owner checks in the evening test before the next stage starts; "Check" needs no agent.

The stages are built in order. Each names what it must not touch.

---

### Stage 1. The FSD library

**Story.** *As the implementer of the later stages, I get FSD lines as typed values and build them
from typed values, so no later stage splits strings by hand.*

**Build**
- `JoinFS.Fsd/JoinFS.Fsd.csproj`, set up like `JoinFS.Messages` (same target frameworks and
  platforms); referenced by `JoinFS` and `JoinFS.Tests`; added to the solution and to the
  `Dockerfile` (csproj copied before restore).
- `FsdLine`: parse a line into verb, `From`, `To` and fields; build a line from them. The verbs of
  §4.2 to §4.7. `:` separates fields; the last field of `#TM` keeps its colons.
- `FsdPosition`: the `@` line, with the pitch/bank/heading packing.
- `FsdFlightPlan`: the `$FP` and `$AM` fields.
- `FsdAtc`: `#AA`, `%`, `#DA`, `$DI`, `$ER` (the error codes this plan uses).
- `FsdFrequency`: `28550` ↔ 128550 kHz; `FsdCallsign`: validity of a station callsign, and the
  aircraft rule of §4.6 as a pure function over a list of (owner, object id, callsign).
- `FsdRelayPolicy`: the verb list of §4.3 and the checks of §4.4 that need only the line.

**Tests** (`JoinFS.Tests/Atc/Fsd*Tests.cs`): every example line of `FSD-protocol.md` for these
verbs parses and is rebuilt byte for byte; the packing round-trips; the callsign rule gives the same
result for any order of its input, for empty and for duplicate callsigns; station callsigns with
none, one and two `_` are valid (`EDDM_TWR`, `EDDM_N_TWR`, `EDMM_ALB_CTR`), and one over 12
characters or with another character is not.

**Do not touch** anything in `JoinFS/`.

**Done when** the library builds in all configurations and in the Docker image, and the tests pass.
Also: add `JoinFS.Fsd/` and this plan to the code map and the staged-work lines of
`.claude/CLAUDE.md`.

---

### Stage 2. The listener and the login (tracer bullet)

**Story.** *As Carla, I start JoinFS with the listener on, point EuroScope at `127.0.0.1`, connect
as `EDDM_TWR`, and EuroScope stays connected. With the listener off, nothing listens.*

**Build**
- Settings `AtcFsd`, `AtcFsdPort` and the switch `-fsd [port]` (§4.8), with its help line.
- `FsdListener` and `IFsdConnection` (§4.1). Loopback only, IPv4 and IPv6.
- `AtcGateway`: §4.2 complete; of §4.3 only the `SERVER` row. Created by `Main` only when the
  listener is on; `Tick` called from `DoWork` next to `whazzup.DoWork()`.
- Monitor and log lines: listening, client connected, station logged in (callsign, frequency),
  logged off, login refused (why).
- `JoinFS/util/fake_fsd_client.py`: logs in as a station, sends `%` every 15 s, prints every line
  it receives, and sends lines typed on its standard input. It is the stand-in for EuroScope in
  every later check.
- `docs/ATC/euroscope-setup.md`: how to connect EuroScope (the steps found to work).

**Tests** (`JoinFS.Tests/Atc/AtcGatewayLoginTests.cs` with a fake connection; one
`FsdListenerTests` over real loopback TCP): login, a second login refused, an invalid callsign
refused, `%` updates the station, `#DA` and a dropped connection log off, an oversized line closes
the connection, `$PI` is answered.

**Known risk, to settle here.** What EuroScope needs before it shows "connected" (the `$DI` text,
answers to `$CQ ...:SERVER:ATC` and `IP`, a challenge `$ZC`) is not fully known from the document.
`joinfs-euroscope-bridge` (github.com/joeherwig) connects EuroScope today: read how it answers and
do the same; do not copy its code. Write what was found into `euroscope-setup.md`. If EuroScope
cannot be made to stay connected, stop and report (§6): every later stage depends on it.

**Do not touch** the session, the network stack, or the old ATC mode.

**Check.** EuroScope (or the fake client) stays connected for 10 minutes; the monitor shows the
station; a second client is refused; without `-fsd` and with the setting off, port 6809 is closed.

---

### Stage 3. The station is this node's ATC presence

**Story.** *As Olaf or Pia, when Carla logs in as `EDDM_TWR`, I see her as the Munich tower
controller exactly where a controller in manual ATC mode showed up before (hub list, Whazzup).*

**Build**
- `LocalAtc` (§4.1) and `Main.LocalAtc`. Its value: the online main station if there is one;
  otherwise the manual ATC settings as today (they go in stage 12); otherwise inactive.
  From a station: the whole callsign (§4.6); airport = the callsign up to its first `_`
  (`EDDM_N_TWR` gives `EDDM`); level from the facility, never from the suffix: DEL 0, GND 1, TWR 2,
  APP 3, CTR and FSS 4; an observer (facility 0) is not active; frequency in the form `PeerInfo`
  uses; position from `%`.
- `Whazzup` writes the station's whole callsign when there is one, instead of building one with
  `Atc.MakeAtcCallsign`.
- Every reader of the old values reads `Main.LocalAtc` instead: `MainSessionHost`
  (`ILocalProfile.Atc`, `AtcAirport`, `AtcLevel`, `AtcFrequency`), `Whazzup`, `Euroscope`, and
  `Sim` (the user location for the activity circle now takes the station's position; the behaviour
  for injected aircraft in ATC mode is unchanged).
- `ILocalProfile.FeedFullRate` is also true while a station is online (§4.7).
- A login or logoff triggers the `PeerInfo` the old mode sent on a change.

**Tests**: the mapping from a station (each facility, an observer, a callsign without `_`, a
divided position `EDDM_N_TWR`, a facility that disagrees with the suffix); Whazzup's line;
`PeerTableTests` for what `PeerInfo` carries with a station online; manual mode gives what it gave
before.

**Do not touch** the legacy codecs or any wire layout.

**Check.** Carla logs in; Olaf's released build lists her as ATC at EDDM with her frequency; she
logs off and the entry goes.

---

### Stage 4. Traffic on the scope

**Story.** *As Carla, without a simulator, I see Pia and Olaf move on my scope with callsign,
squawk, altitude, ground speed and flight plan; an aircraft that leaves the session disappears.*

**Build**
- `IAtcTraffic` and its adapter over `Sim.View` (`JoinFS/Atc/SimViewTraffic.cs`): per aircraft the
  key (owner, object id), flight plan, squawk, position, attitude, ground speed, the owner's
  nickname.
- In the gateway: §4.7, the callsign rule of §4.6, and the aircraft row of §4.3 (`$CQ` RN and FP).
- The mapping of a JoinFS flight plan to `$FP` (rules, type, speed, departure, altitude,
  destination, alternate, remarks, route; the fields JoinFS does not have are empty or zero).

**Tests** (`AtcGatewayTrafficTests`, fake traffic): an aircraft appears, moves, changes its flight
plan, leaves; duplicate and empty callsigns; a squawk; `$CQ` RN and FP answered; nothing is sent
before login.

**Do not touch** `Sim` beyond reading its view.

**Check.** Carla, with no simulator and JoinFS in a session with Pia: the target follows Pia within
a second or two, the flight plan opens in EuroScope, the squawk matches Pia's transponder. Ben, with
a simulator, also sees his own aircraft. Recorder playback appears as traffic.

*From here the build replaces `joinfs-euroscope-bridge` for a single controller.*

---

### Stage 5. The settings card

**Story.** *As Carla, I turn the ATC client connection on in Settings, see the port, and see whether
my EuroScope is connected and as what.*

**Build**
- In `JoinFS.UI`: an "ATC" card on the Settings tab with the switch (`AtcFsd`), the port, and a
  status line (off / listening on port N / connected as `EDDM_TWR` 118.700). Through the
  preferences and service interfaces of the UI (`JoinFS.UI/README.md`); `JoinFS/Live/` connects
  them. Turning the switch starts or stops the listener without a restart.
- Strings in every language the UI has.
- The row "ATC mode" of the README's open list is updated.

**Tests**: view-model tests in `JoinFS.UI.Tests` for the three states and for the switch.

**Do not touch** the gateway's behaviour.

**Check.** Switch on: EuroScope can connect. Switch off: the client is disconnected and the port is
closed. The state survives a restart. A fresh install has it off.

---

### Stage 6. The wire: `AtcStation` and `AtcRelay`

**Story.** *As the implementer of stages 7 and 8, I send and receive the two messages; as Olaf's or
an older build, I never see them.*

**Build** (§4.5, following `docs/network/jfp2/implementation.md` §5)
- `MessageKind.AtcStation`, `MessageKind.AtcRelay`, their structs and `IMessageHandler` overloads
  in `JoinFS.Messages`; the constants in `MessageClasses`; the codecs with a limit per string;
  both appended last to `Jfp2Profile.Default`.
- `protocol.md` §3.3, §9, §9.7, §10.2 and the change history; the dissector.
- `LegacyPlugin.CanCarry` stays false for both.

**Tests**: codec round trips; `LongStrings_AreCutToTheirLimits`; the 1,200-byte case; a
version-skew test in `Jfp2VersionSkewTests` (a node or a relay without the classes: nothing is
sent to it, nothing else breaks); `Jfp2WireCharacterizationTests` updated on purpose. The handshake
golden bytes change only as they did when class 22 was added: look at that commit first.

**Do not touch** the legacy fixtures. No behaviour: nothing sends these messages yet.

**Done when** the tests pass and a legacy-only and an older-JFP2 `TestMesh` node work unchanged.

---

### Stage 7. Stations across the mesh

**Story.** *As Carla (`EDDM_TWR`), when Ben connects as `EDDM_APP` on his PC, he appears in my
controller list with his frequency, and I in his. When he closes EuroScope or his JoinFS dies, he
disappears from my list.*

**Build**
- `AtcSession` (§4.1), created in `Network`'s constructor like the other parts: the station table
  keyed by (`NodeId`, slot); sending per D7 and §4.5; expiry; the crossing-login rule of §4.6;
  `IAtcMesh` (stations online, station changed/gone events, and the relay send of stage 8 left
  unimplemented).
- Gateway: a login is refused when the callsign is in the table; for each remote station `#AA`
  when it comes online, `%` every 15 s, `#DA` when it goes; a client that logs in later gets the
  stations already there.
- A remote station also fills what `PeerInfo`'s ATC fields fill for that node, so the UI lists
  agree for a node on this build.

**Tests**: `JoinFS.Tests/Session/AtcSessionTests.cs` with `SessionRig` (send on change, on member
joined, every 10 s; expiry at 35 s; member left; logoff notice; crossing logins; `EDDM_N_TWR` and
`EDDM_S_TWR` on two nodes are two stations); a `TestMesh`
scenario in `JoinFS.Tests/Net/` with two gateways and fake connections; a station on a node behind a
relay.

**Do not touch** `MeshManager` or the plugins.

**Check.** Carla and Ben each with EuroScope: both lists show the other within seconds; Ben's
frequency change shows at Carla's; killing Ben's JoinFS removes him at Carla's within 35 s; a login
as `EDDM_TWR` on a third PC is refused. Then the divided tower: Carla as `EDDM_N_TWR` and a third
controller as `EDDM_S_TWR` are both accepted and each sees the other under the whole callsign.

---

### Stage 8. Controller to controller

**Story.** *As Carla, I track `DLH4TK`, set a temporary altitude and a scratchpad, and hand it to
Ben. Ben sees my tag data, accepts, and now owns the track on both scopes. We chat in the ATC
channel.*

**Build**
- Gateway: the rest of §4.3 (stations, group addresses, the unknown row).
- `AtcSession`: sending `AtcRelay` (one guaranteed unicast per target node) and the checks of §4.4.
- Counters for relayed and dropped lines, with the reason, in the monitor.

**Tests**: gateway routing per row of §4.3; each check of §4.4 (a verb not in the list, a `From`
that is not the origin's station, a control character, no local station); a `TestMesh` scenario
with three stations that plays a handoff (`$CQ` IT, `#PC` TA and SC, `$HO`, `$HA`), an ATC chat
line and a private line, and asserts each scripted client received exactly the lines meant for it.

**Check, with two EuroScope instances** (the fake client cannot prove this one): track, drop,
temporary altitude, scratchpad, assigned squawk, handoff accepted, handoff refused, ATC chat,
private chat. Then the late joiner: Carla tracks two aircraft, Ben connects afterwards and must see
them as tracked by Carla. EuroScope is expected to do this itself, by asking who has a target
(`WH`) and being told (`IH`), as on VATSIM. If Ben does not see the tracks, stop and report (§7,
R3); do not add state to JoinFS in this stage.

*From here several controllers can work a session together.*

---

### Stage 9. Transponder mode and ident

**Story.** *As Carla, I ask Pia by voice to squawk ident; she presses IDENT in her simulator and
her tag flashes on my scope. An aircraft on standby is shown as standby.*

**Build**
- Two read-only variables in the variable tables (`Variables.cs`), next to `TRANSPONDER CODE:1`,
  not injected into remote aircraft: the transponder state and the ident. Verify each name against
  the simulator's documentation before adding it, per simulator and in the right `#if` branch;
  leave a simulator out when it does not have the variable. Candidates: SimConnect
  `TRANSPONDER STATE:1` and `TRANSPONDER IDENT` (MSFS); X-Plane
  `sim/cockpit/radios/transponder_mode` and `sim/cockpit/radios/transponder_id`.
- Gateway: the mode letter of `@`: ident `Y`, held for 5 s after it was last seen; standby or off
  `S`; otherwise, and when the variables are unknown, `N`.

**Tests**: the mapping and the 5 s hold in `AtcGatewayTrafficTests`; `CreateVuidTests` and the
variable-table tests still pass.

**Do not touch** the X-Plane plugin or `DATA_VERSION`: datarefs are named in the table. If a
change to the plugin turns out to be needed, stop and report.

**Check.** Pia in MSFS presses IDENT: the tag flashes at Carla's within two seconds. Standby shows
as standby. Olaf's aircraft (no such variables) shows as mode C. X-Plane is checked when a tester
has it; the stage does not wait for that.

---

### Stage 10. ATIS

**Story.** *As Ben, I start my ATIS in EuroScope as `EDDM_ATIS`; Carla sees the ATIS station and
reads its letter and text.*

**Build**
- Gateway: a second connection is accepted when its callsign ends in `_ATIS` and a main station is
  online; it becomes the station of slot 1 and ends with the main station. Lines addressed to it
  are delivered to it (§4.3, §4.4).
- `AtcSession` already carries slot 1; add what the tests show is missing.

**Tests**: second connection accepted and refused (no main station, not `_ATIS`, a third
connection); a `$CQ ...:EDDM_ATIS:ATIS` from another node reaches the ATIS connection and the
`$CR` lines come back.

**Check.** Two EuroScope instances: Ben's ATIS appears at Carla's with letter and text; it goes
when Ben disconnects.

---

### Stage 11. Flight plan amendments

**Story.** *As Carla, I correct `DLH4TK`'s cruise level in the flight plan; Ben sees the amended
plan. Pia's own flight plan in JoinFS does not change.*

**Build**
- Gateway: `$AM` from the client is added to the relayed verbs and goes to every station node; a
  received one is written to the client. Each gateway keeps the last amendment per aircraft and
  answers `$FP` requests of its own client from it; it is forgotten when the pilot's own flight
  plan changes or the aircraft leaves.

**Tests**: amendment relayed, kept, used for `$CQ ... FP`, forgotten on the two conditions.

**Limit, accepted (D9):** a controller who logs in later sees the pilot's plan, not the amendment.

**Check.** Two EuroScope instances: Carla amends, Ben sees it; Pia's Flight Plan tab is unchanged.

---

### Stage 12. The old ATC mode goes

**Story.** *As the maintainer, there is one way to be a controller: an FSD login.*

**Build**
- Remove `Euroscope.cs`, its setting, its UI control and its strings; `IAtcListener` and its
  adapter; the manual settings (`Atc`, `AtcAirport`, `AtcLevel`, `AtcFrequency`) and their fallback
  in `LocalAtc`. Old user settings files still load.
- `-atc` and `-airport` are accepted and ignored, with one log line that names `-fsd`.
- `PeerInfo` and `Status` keep their ATC fields (frozen wire); they are filled from `LocalAtc`.
- Docs: `docs/architecture.md` (ATC section, part tables, tests table), `JoinFS.UI/README.md`,
  `.claude/CLAUDE.md`, the release notes (how to connect EuroScope, hubs must be current, what was
  removed), and a note for the wiki that `joinfs-euroscope-bridge` is no longer needed.

**Tests**: settings from an older version load; the six configurations build; nothing refers to the
removed names.

**Check.** A hub started with `-atc EDDM` starts and logs the notice. Stages 2 to 11's checks still
hold.

## 6. Status and session rules

| Stage | State | Notes |
|---|---|---|
| 1 FSD library | done, 2026-11-20 | Two additions of §4.8; no deviation from the row. |
| 2 Listener and login | done, 2026-11-20 | Built and tested against the fake client and over real loopback TCP; **R1 is still open: EuroScope itself was not available, so "stays connected for 10 minutes" is the owner's evening check** (`euroscope-setup.md` §4 and §5). Deviations: (1) no `$DI` on connect (§4.2 rule 1 rewritten, evidence in `euroscope-setup.md`); (2) added the post-login welcome and capability exchange and the 30 s `#DL` keep-alive, copied in behaviour from the bridge; (3) `$CQ ..:SERVER:FP` answers `$ER 008` until stage 4 serves aircraft; (4) a second station gets `$ER 003`, the same callsign `$ER 001`; (5) the station is `AtcStationInfo` (`AtcGateway.Station`), which stage 3 turns into `LocalAtc`; no event yet. Nothing deferred to another row. |
| 3 ATC presence | done, 2026-11-20 | Stage 2's EuroScope evening check passed (owner: stays connected, tests ok), so R1 is closed. Built as the row says; additions in §4.1 ("`LocalAtc` (stage 3)"): `PeerTable.SendPeerInfoToAll`, `ILocalProfile.AtcCallsign`, `Main.TryGetLocalAtcPosition`, and the hub's own user list shows the whole callsign. Deviations: (1) the removal line of the `euroscope.txt` export now uses the callsign it exported, since `LocalAtc` is already inactive by then; (2) not tested: `Whazzup`'s line, `Euroscope`, `Sim` and `MainSessionHost` need `Main`, so they are covered by the evening check only; (3) `FS2024` (release) could not link while `JoinFS-FS2024` was running, its code compiled and FS2024-Debug builds in the tests. Nothing deferred to another row. R8 found. |
| 4 Traffic | done, 2026-11-20 | Built as the row says; details in §4.7 ("Stage 4"). **The row's Check is the owner's evening test** (EuroScope with no simulator, JoinFS in a session with Pia; also open: whether EuroScope accepts a target that starts with `@` and no `#AP`, and the bank sign, §4.6). Deviations: (1) the traffic is the session's aircraft, the user's own and the recorder's; the simulator's own AI aircraft that are not broadcast are left out (the row's set, read as §4.7's parenthesis says); (2) a changed callsign (the pilot edits it, or a collision is resolved differently) sends `#DP` for the old name before the new one appears; (3) not tested: `SimViewTraffic` beyond its squawk conversion (it needs a `Sim` snapshot), so the owner id, the nickname, the ground speed and the attitude signs are covered by the evening check only; (4) `FS2024` (release) could not link while `JoinFS-FS2024` was running (file locked), its code compiled and FS2024-Debug builds in the tests. Nothing deferred to another row. |
| 5 Settings card | open | |
| 6 Wire | open | |
| 7 Stations | open | |
| 8 Controller to controller | open | |
| 9 Transponder | open | |
| 10 ATIS | open | |
| 11 Amendments | open | |
| 12 Removal | open | |

Rules for a session (the same as for every staged work in this repository):
1. Read this plan, `FSD-protocol.md` for the verbs of your stage, and `docs/architecture.md` §2.
2. Take the lowest open stage. Write "claimed, <date>" in its row before coding.
3. Build exactly the stage: no part of a later stage, no stage split in two, no decision of §3
   re-opened. Stages 1 to 5 change no wire; only stage 6 does.
4. Something the stage needs but the plan does not say: choose the simplest thing that fits §4,
   and write it into §4 in the same commit. Something that contradicts §3 or §4, or one of the
   "stop and report" cases: commit nothing, write a resume note in the row, and report to the owner.
5. One commit per stage, `Stage N of atc-fsd`, with the row set to "done, <date>" and every
   deviation written in the Notes column and, if it moves work, in the row of the stage that takes
   it.
6. `XPlane.cs` and `JoinFS-XP/` are not touched by any stage.

## 7. Risks and open questions

| # | Risk or question | Handling |
|---|---|---|
| R1 | What EuroScope needs to accept the server (stage 2). | Stage 2 copies the bridge's observable behaviour (no `$DI`, post-login capability exchange); to be confirmed against the real client in the evening check, results into `euroscope-setup.md` §5. |
| R2 | The position rate. `@` once a second is far more often than VATSIM's five seconds. | Stage 4's check; the interval is one constant. |
| R3 | A late-joining controller learning existing tracks relies on the clients (`WH`/`IH`), as on VATSIM. | Stage 8's check. If it fails, the owner decides between a replay by the tracking node and living with it; no stage adds state on its own. |
| R4 | A hub of an older build does not pass the two classes. | Release notes; controllers with a direct link are unaffected. |
| R5 | No authentication in the mesh: any member may log in as any free callsign. | Accepted; the session's password is the gate. |
| R6 | A line over 1,000 bytes (a long `$AM` route) is not relayed. | Logged. Segments exist in JFP2 (§7.5) if it turns out to matter. |
| R7 | X-Plane's transponder datarefs are unverified and the variant is hard to test. | Stage 9 adds them only when verified; otherwise X-Plane pilots show as mode C. |
| R8 | `PeerInfo` carries the frequency as a signed 16 bit field (frozen wire), so a frequency above 132.767 MHz (field value above 32767) arrives negative at every peer, as it already did in manual mode. | Found in stage 3, not fixed: the legacy `PeerInfo` is frozen. Stage 7's `AtcStation` carries kHz as u32, so JoinFS peers get it right from then on; legacy peers keep the old limit. |
| Q1 | EuroScope on a second PC (D4)? | Ask again when a controller needs it. |
| Q2 | Station events for the TeamSpeak stack and the web map, and text between controllers and pilots. | Next plans; `AtcSession`'s station table is what they would read. |
