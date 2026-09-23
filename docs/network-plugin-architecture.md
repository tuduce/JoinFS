# Networking re-architecture: pluggable protocol stacks

> **What this document is:** the design and decision record of the 2026-09 networking rewrite — the problem, the options considered, the chosen design, how it evolved during implementation (§2.10), and the build log (§4).
> - **For how the system works today, read `docs/reference/joinfs-architecture.md`.** Where this record and the code disagree, the code (and that reference) wins.
> - The JFP2 wire protocol is specified in `docs/reference/jfp2-protocol.md`.

## Context

JFP2 works, but it is woven into the legacy code rather than encapsulated.
- **~440 JFP2 references** sit inside `Node.cs` (210) and `Network.cs` (227).
- **The per-peer "try JFP2 direct, then JFP2 relay, else legacy" block is copy-pasted ~10 times.** It appears at `Network.cs:2349, 2370, 2862, 3023, 3259, 3420, 3519, 3578, 3612, 4208`.
- **Callers depend on a hidden shared-buffer contract.** `Write*Message` prepares `LocalNode.sendBuffer` before a per-peer loop in `Sim.cs:2326/2420/2581/4380`, and the loop assumes nothing touches the buffer in between. `MainForm.BroadcastUserFlightPlanNow` already breaks that contract, because it runs off-lock on the UI thread.
- **Receive logic is duplicated.** `HandleJfp2Position` repeats legacy `AircraftPosition`, and `ApplyJfp2Variables` repeats the three variable cases.
- **Per-peer JFP2 state is split across three classes.** Sessions live in `LocalNode`; identity send and bridge caches live in `Network`; `Sim` has to know that Identity must be sent before Position.
- **Translation is pairwise, ad hoc, and has bugs.** The staged Tier 3 bridge sits in `Network.cs:3670+`:
  - Translated Position is credited to the **hub** instead of the origin. `PrepareMessage` writes `localNuid` as the header sender, so it doesn't match the translated variables, which are credited to the origin.
  - `TryGetJfp2RelayPeer` never checks whether the *final target* speaks JFP2. So Event, Notes, WeatherReply, FlightPlan, Weather and shared-cockpit messages sent to an indirect legacy peer are dropped at the hub. Before Phase 6, the legacy relay delivered them.
- **`Network` reaches into the whole app.** It calls about 100 `main.sim.*` members, the recorder, notes, euroscope, log, forms and settings directly. In the other direction, about 15 files read `network.*` / `localNode.*` fields.
- **`XPlane.cs` creates its own `LocalNode`** as the transport to the native plugin, so legacy framing is also the X-Plane IPC format.

**Goal:** move networking behind a plugin boundary.
- Legacy and JFP2 each become a protocol plugin.
- A router picks the plugin per peer and per message class.
- Translation between protocols happens through one protocol-neutral message model, not pairwise bridges.
- The app (Sim, Recorder, Forms) stops knowing that protocols exist.

**Decisions (agreed 2026-09-23):**
- **Big-bang rewrite:** the new stack is built alongside the old one, then switched over in one go.
- **Legacy stays wire-compatible with released v26.5; its internals are free to change.**
- **Threading is open to change** (see §1).
- **Recorder:** keep the `.jfs` format; see §6.

---

## 1. Ideas considered

| # | Idea | Verdict |
|---|---|---|
| A | **Strategy per message class inside `Network`**: extract `IMessageSender<T>` with legacy/JFP2 implementations and keep `LocalNode`/`Network` as they are. | Rejected. It removes the copy-paste but leaves mesh, transport, reliability and app coupling tangled. Translation stays pairwise, and the next protocol repeats today's problem. |
| B | **Each protocol is a full stack with its own socket.** | Rejected. `Nuid` = IP+port is the peer identity on the legacy wire, and NAT traversal depends on one port. Both stacks must share the socket (magic-byte demux already exists and works). |
| C | **Pairwise translators** (`LegacyToJfp2Bridge`, `Jfp2ToLegacyBridge`, ...). | Rejected. That is N² translators, each holding its own shadow state (today's `jfp2BridgeIdentityCache`). |
| D | **Layered core + protocol plugins over a shared transport, with a canonical message pivot and a router.** | **Chosen.** Details below. |

| Threading option | Verdict |
|---|---|
| Keep everything in `Main.DoWork` under `conch` | Rejected. Receive latency is set by the 5 ms poll. The shared-buffer races stay, and UI code keeps reading network internals under one global lock. |
| Actor per peer / async everywhere | Rejected. Too many moving parts, and it makes per-tick fan-out ordering hard to reason about. |
| **One dedicated network thread owning the socket and all protocol state; the app talks to it through queues and immutable snapshots** | **Chosen.** Only one boundary exists, and nothing below it needs a lock. The shared-buffer race is gone by construction. Receive wakes on arrival, not on the 5 ms tick. Sim, UI and CONSOLE services read a snapshot without locking. |

---

## 2. Target architecture (idea D)

*§2.1–§2.9 are the design as approved. §2.10 lists every place the implementation differs, and §2.11 the known structural debts.*

```
 ┌──────────────── App thread (Main.DoWork, conch) ────────────────┐   UI / WebSocket / Whazzup
 │ Sim ──publish LocalObjectState──►┐        ┌──► NetworkIngest ────┼─► Sim, Recorder, Notes,
 │ Notes/Forms ──commands──────────►│        │    (only code that    │   Euroscope, Log
 │                                  │        │    applies net data)  │
 └──────────────────────────────────┼────────┼──────────────────────┘   read NetworkSnapshot
                         outbound queue   inbound queue                  (immutable, volatile ref)
 ┌──────────────── Network thread (owns everything below) ─────────────────────────────┐
 │ NetworkService: drains outbound, fans out per peer (SendPolicy), publishes snapshot  │
 │ PeerDirectory (NodeId → PeerInfo, links, RTT, route)  ObjectStateCache  Directory     │
 │ MeshManager (join/login/membership/pulse/pathfinder state machine, protocol-neutral)  │
 │ Router: (peer, MessageKind) → plugin, a cached flat array                            │
 │        ▲ canonical messages ▼                                                         │
 │  ┌─────────────── LegacyPlugin ───────────┐   ┌─────────────── Jfp2Plugin ──────────┐ │
 │  │ framing 0x0B, guaranteed layer,        │   │ envelope 0xFA, Hello/HelloAck,      │ │
 │  │ FLAG_FORWARD relay, frozen codecs      │   │ per-class negotiation, guaranteed,  │ │
 │  │ (incl. mesh message codecs)            │   │ Tier-1 byte relay, versioned codecs │ │
 │  └────────────────────┬───────────────────┘   └───────────────┬─────────────────────┘ │
 │                    UdpTransport (one socket, magic-byte demux, pooled buffers)         │
 └────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Layers and responsibilities

1. **`UdpTransport`** (`Net/Transport/`)
   - Owns the one `Socket`, running a receive loop on the network thread. It receives into `ArrayPool` buffers.
   - Routes each datagram by `IProtocolPlugin.Accepts(span)` (magic byte).
   - Keeps the ban list.
   - Implements `IDatagramTransport`, so tests can use an in-memory hub of transports.
2. **Protocol plugins** (`Net/Protocols/Legacy`, `Net/Protocols/Jfp2`)
   - Each plugin owns its framing, handshake, reliability, same-protocol relay, and codecs.
   - Each plugin converts wire ⇄ canonical messages. It never sees `Sim`, `Main` or forms.
3. **Core** (`Net/Core`), all protocol-neutral:
   - `PeerDirectory`: the single peer table. It replaces `LocalNode.nodes`, `Network.nodeList` and `jfp2Sessions`-as-peer-list.
   - `Router`
   - `ObjectStateCache`: the latest Identity per `(origin, objectId)`, which translation uses.
   - `SendPolicy`: per-peer rate masks, low bandwidth, "peer sim disconnected ⇒ every 32nd tick". This logic moves out of `Sim.cs:2577-2610` and `5683`.
   - `NetworkService`: the facade and the thread.
4. **Directory services** (`Net/Directory`)
   - Hub list and `hubs.dat`, online users / uuid, address-book pings, DNS cache, seed/myip/banlist HTTP, credentials file watchers.
   - These are session semantics, not wire format. Today they are spread through `Network.cs`.
   - They send and receive canonical `Status` / `HubList` / `UserList` / ... messages through the router, like everything else.
5. **App side** (`JoinFS/NetworkIngest.cs`, changes in `Sim.cs`)
   - `NetworkIngest` drains the inbound queue at the start of `DoWork`, under `conch`. It is the **only** place that calls `sim.UpdateAircraft/UpdateObject/RemoveObject/...`.
   - It also calls `recorder.Record` (moved out of `Network`), and applies ignore / share-cockpit / multi-object filtering from `Log`.

### 2.2 Canonical message model (`Net/Messages`)

- **Reuse the JFP2 DTOs.** They are already protocol-neutral, value-type structs, and legacy decode already builds some of them (`Network.cs:4950, 4976, 5466`). Move `PositionUpdate`, `IdentityUpdate`, `VariableSyncUpdate`, `EventUpdate`, `FlightPlanUpdate`, `NoteUpdate`, `WeatherReport`, `StatusUpdate` and `StatusRequestUpdate` from `Jfp2/Codecs/*` into `Net/Messages`.
- **Add canonical types for legacy-only kinds:**
  - `ObjectPositionUpdate`, `RemoveObject`, `PeerInfo` (replaces SharedData), `HubList`, `UserList`, `UserPositions`, `OnlineUser`, `UserNuidRequest/Reply`, `WeatherRequest`, `CommsRequest`
  - JFP2 simply doesn't advertise these kinds yet, so the router uses legacy for them.
- **Every message carries a `MessageMeta`:**
  - `Origin NodeId`, the real owner, never the relay
  - `Target NodeId`, or broadcast
  - `Guaranteed` flag
  - `NetTime`
- `enum MessageKind : byte` indexes the router's flat arrays.
- **The canonical model follows the *newest* shape** (Identity separate from Position). Older protocols adapt to it, which puts the translation burden on the newer protocols, keeping legacy frozen:
  - The frozen legacy plugin gets a fixed adapter written once. It merges `ObjectStateCache` identity into its inline AircraftPosition on encode, and splits inline identity into Identity + Position on decode.
  - New protocols map 1:1.

### 2.3 Plugin contract (sketch)

```csharp
interface IProtocolPlugin {
    string Name { get; }  int Preference { get; }        // JFP2 > Legacy
    bool Accepts(ReadOnlySpan<byte> datagram);            // magic byte
    void Attach(IProtocolHost host);
    void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> data);
    void Tick(double now);                                // handshake retries, reliability, pulses
    LinkInfo GetLink(NodeId peer);                        // Direct/Relayed, per-kind agreed version (0 = can't)
    void Send(in MessageMeta meta, MessageKind kind, IMessageBody body);  // encode + transmit
    void OnPeerRemoved(NodeId peer);                      // one cleanup hook (fixes stale-state bugs)
}
interface IProtocolHost {                                 // the core, as the plugin sees it
    IDatagramTransport Transport { get; }  IPeerDirectory Peers { get; }  IClock Clock { get; }
    ObjectStateCache Objects { get; }      INetLog Log { get; }
    void Deliver(in MessageMeta meta, MessageKind kind, IMessageBody body);  // canonical, up
    void LinkChanged(NodeId peer);                        // invalidates the router cache
}
```

The hot path avoids allocation and interface dispatch per field. Codecs stay `ICodec<T>` over structs (as they are today). `Send` is generic over `T : struct` in the real signature. The `IMessageBody` above is shorthand.

### 2.4 Mesh ownership: mesh logic in the core, mesh messages as canonical kinds

**What "the mesh" is today** (`Node.cs`, about 1,500 lines of logic):
- session create, join and login, with password or credentials;
- membership spread (the node list in JoinReply, AddNode, Leave);
- liveness (Pulse/PulseResponse, which also gives RTT, 30 s expiry, the low-bandwidth flag and send/receive "established");
- route discovery (Pathfinder/Response, which sets a route endpoint via a relay);
- failure replies (JoinFail/LoginFail).

JFP2 has none of this. It negotiates only after the legacy Join, on peers the legacy mesh already knows.

**Decision:** don't put the mesh inside the legacy plugin. Instead:
- **`MeshManager` lives in the core and is protocol-neutral.** It owns the session state machine, membership, liveness/RTT, the route table (`peer → via peer`) and credentials checks. It writes directly into `PeerDirectory`, the single source of truth.
- **Mesh messages become canonical `MessageKind`s:** `Join`, `JoinReply`, `JoinFail`, `Login`, `LoginFail`, `AddNode`, `Leave`, `Pulse`, `PulseResponse`, `Pathfinder`, `PathfinderResponse`. They go through the router like any other kind.
- **The legacy plugin is reduced to framing, reliability, `FLAG_FORWARD` relay and codecs.** Its mesh codecs are pinned by golden-byte tests. `MeshManager` is a faithful port of legacy timings and rules, so v26.5 peers see identical behaviour.
- **Today, only legacy advertises the mesh kinds,** so all mesh traffic goes over legacy, exactly as now.
- **JFP2 stays a "link upgrader".** For directly reachable peers (`RouteIsOwnEndPoint`, Finding 7), it negotiates and reports per-kind capability through `GetLink`.

This costs about the same as porting the mesh into the legacy plugin, and the rewrite has to port it anyway. It avoids two problems: the mesh would otherwise be the one subsystem locked to a protocol, and the peer table would have two owners.

### 2.4.1 A JFP2-native mesh: what it would take, and whether it's worth it

With §2.4 in place, "JFP2 has its own mesh" no longer means a second mesh. It means **JFP2 advertises the mesh kinds too**, and the router picks JFP2 per peer. It would need:
1. **JFP2 codecs for the ~11 mesh kinds.** The internal-partition classes are already reserved in `Envelope.MessageClasses`. This is mechanical.
2. **Multi-segment guaranteed delivery in JFP2.** JFP2 guaranteed is single-datagram today, but JoinReply's node list and the bulk notes catch-up can exceed one datagram. This is the biggest item.
3. **A bootstrap before Join.** Today Hello comes after Join, and fallback to legacy takes 5 × 2 s. A JFP2-first join needs:
   - a quick probe: Hello first, falling back to legacy Join after about 500 ms;
   - or a hint that the target is JFP2-capable (hub list/Status carrying capabilities, or the address book remembering it).
4. **Session-less Hello.** It already is. Hello can be accepted before membership, gated by the ban list and rate limiting.
5. **Mixed sessions still need legacy for legacy peers.** The hub announces JFP2-joined peers to legacy peers over legacy AddNode. Routing mesh kinds per peer handles that automatically; there are no merge rules because there is only one `MeshManager`.

**Is it worth it?**
- **Not now.** While any legacy peer might be in the session (released v26.5 builds don't update themselves), the legacy mesh codecs must stay anyway.
- **The gain is small:**
  - smaller headers: 8 bytes instead of 21, on 1 Hz pulses;
  - Findings 8/9 no longer apply. They are fixed on our side in the rewrite anyway.
- **The cost is large:** the whole Join/Login/Pathfinder matrix has to be tested twice.
- **Worth doing when one of these becomes a real requirement**, because none can be added to a frozen legacy wire:
  - authenticated or encrypted sessions (legacy sends a password hash);
  - IPv6 (`Nuid` is IPv4; `Envelope.PeerKey` already exists for this);
  - NAT hole punching;
  - retiring legacy once its population is negligible.
- **Once §2.4 is in place, that later work is medium effort and touches no app or core code:** items 1 to 3 above, all inside the JFP2 plugin, plus a small bootstrap hook in `MeshManager`.

### 2.5 Router: which protocol talks to which peer

- For each `(peer, kind)`, the router picks the highest-`Preference` plugin whose `GetLink(peer)` has an agreed version above 0 for that kind.
- The result is cached in `byte[peerSlot][kind]` and rebuilt only on `LinkChanged`. On the hot path it is one array read. This replaces every `TryGetJfp2AppPeer`/`TryGetJfp2RelayPeer` branch site.
- **Indirect targets use the target's own capability, not the hub's.**
  - JFP2 only claims an indirect target when both the hub leg and the target are JFP2-capable for that kind.
  - The target's capability is learned from a `PeerInfo` capability field, which the hub propagates.
  - Unknown means legacy. This fixes the relay-drop regression.

### 2.6 Translation between protocols

This is generic: there is **no protocol-specific bridge class**.
1. The hub's plugin A receives a message addressed to someone else.
2. If plugin A can relay it natively, meaning the target is linked on A for that kind, A does a byte-for-byte forward. This covers JFP2 Tier 1 today and legacy `FLAG_FORWARD`. This fast path stays inside the plugin.
3. Otherwise A decodes to canonical and hands it to the core with `Origin` preserved. The core runs the `Router` for the target, and plugin B encodes and sends it. That covers Tier 2 (JFP2 vN→vM) and Tier 3 (JFP2↔legacy) in both directions, with no extra code per pair.
4. **Attribution fix:** when the legacy plugin sends a message with `Origin ≠ self`, it produces a datagram shaped like a legacy forward: header sender = origin, `FLAG_FORWARD`, recipient = target. A v26.5 receiver already handles that correctly, because it is exactly what its own relay emits.
5. **Guaranteed across a translation is hop-by-hop:**
   - The hub acks upstream on the incoming protocol and sends a new guaranteed message downstream.
   - The legacy plugin keeps a small "proxied guaranteed" table so it can catch the downstream `GuaranteedDone`, which is addressed to origin, instead of forwarding it on.
6. **Stateful adaptation lives in `ObjectStateCache` (core), keyed `(origin, objectId)`.** For example, legacy AircraftPosition needs identity inline. The cache is cleared through `OnPeerRemoved`, so there are no leaking caches (today's `jfp2BridgeIdentityCache`).

### 2.7 Threading and data flow

- **Outbound (app to network):**
  - `Sim` posts `PublishObject(LocalObjectState)` once per object per tick. It no longer loops peers.
  - The network thread fans out per peer using `SendPolicy` and the router.
  - Commands (Join, Leave, Create, SubmitHub, SendNote, BroadcastFlightPlan, ...) are queued too, so UI callers need no lock. This fixes the `MainForm` flight-plan race.
- **Inbound (network to app):**
  - Plugins `Deliver` canonical structs into a bounded queue.
  - `NetworkIngest` drains it at the top of `Main.DoWork`, under `conch`.
  - Latency to Sim is ≤ 1 app tick, the same as today. Receive itself no longer waits for the tick.
- **Snapshot:**
  - The network thread publishes an immutable `NetworkSnapshot` on change and at least every 250 ms, by swapping a volatile reference.
  - It carries: session state, peers (name, guid, callsign, RTT, direct, established, protocol per kind, version, simulator), hubs, users, counters.
  - Its readers:
    - `Sim` (RTT per frame, peer sim-connected)
    - Forms
    - Whazzup
    - WebSocket / Webhook
    - `MonitorSessionDetails`
  - None of them take `conch` for network data any more.
- **Queue implementation:** `System.Threading.Channels` bounded channels. Position traffic goes through pooled message wrappers or struct payloads, so there is no per-datagram allocation. Payload buffers come from `ArrayPool`.
- **Hot-path cleanups that fall out of this:**
  - Drop the `GetNodeList()` array allocation per tick.
  - Drop the `192.168.1.115` string compare on every send.
  - Gate diagnostic string building behind a monitor flag.

### 2.8 How protocols evolve next to a frozen legacy

**The canonical model is not a wire format.**
- It is the in-process data model the app consumes: what `NetworkIngest`/`Sim` understand.
- Every plugin is compiled into the same binary as the canonical model. So it has **no compatibility constraint of its own**: fields can be added, renamed or restructured freely from one release to the next.
- Wire compatibility is the job of each plugin's codecs, versioned per protocol and per class (JFP2 `PositionV1`, `PositionV2`, ...). It is never the canonical model's job.
- The canonical model is therefore the **superset** of what the app can use, not the lowest common denominator. Legacy is frozen at a subset of it.

**Evolution rules:**

| Change in a new protocol | What happens | Legacy plugin |
|---|---|---|
| **New field** (e.g. gear damage in `PositionV2`) | Add it to the canonical struct as an *optional* field: a presence bit, or a sentinel like the existing `staticCgToGround = NaN` for old peers. The V2 codec fills it; the V1 codec and the legacy codec leave it absent. Ingest applies a default when it's absent. | Unchanged. It ignores the field on encode and leaves it absent on decode. |
| **New message kind** (e.g. a richer chat) | New `MessageKind`. Plugins that don't advertise it make the router report "no link". The kind's owner can register a **downgrade rule** that expresses it through an existing kind: for instance, a new sim value rides legacy `VariableSync` as a vuid, or a chat becomes a `Note`. With no rule, legacy peers don't get the feature, which is how feature gaps work today. | Unchanged. Downgrade rules live with the new feature, in core or the new plugin. **This is where the translation burden sits.** |
| **Shape change** (split or merge, like Identity leaving Position) | The canonical model takes the new shape. The legacy codec adapts statefully using `ObjectStateCache`: it merges on encode and splits on decode. | Written once, when the canonical shape changes. Its wire bytes stay frozen, as enforced by the golden tests. |
| **Semantic change** (units, timebase, reference frame) | The canonical model keeps one internal meaning. Each codec converts at its boundary. | Its conversion stays frozen. |
| **Field dropped by the new protocol but required by legacy** | It stays in the canonical model while any plugin needs it. If the new protocol can't carry it, the value is filled from `ObjectStateCache` (last known value) or a default. | Unchanged. |
| **Legacy retired someday** | Delete the plugin. Canonical fields and adapters that only legacy needed can then be removed. | — |

**Guardrails that keep this honest:**
- **Golden-byte tests** make it impossible to change legacy's wire by accident when a canonical struct changes. A new field compiles, the legacy codec ignores it, and the tests still pass.
- **Each codec declares a fidelity mask:** which canonical fields it carries. The router can prefer the higher-fidelity link. Tests assert that translating JFP2 to legacy and back loses only the fields outside legacy's mask.
- **A canonical field is added only when the app actually consumes it.** A protocol cannot usefully carry data the app doesn't understand, and this stops the model from turning into a grab-bag.

### 2.9 Identity types

- **`NodeId`**: today's `LocalNode.Nuid` moved to `Net/Core`, with the same 7-byte wire form. It is the canonical peer id. JFP2's link-local PeerIds stay inside the JFP2 plugin.
- `uuid` / `Guid` helpers move to `Net/Directory`.

## 2.10 Refinements made during implementation

These refine §2.1–§2.7. The code is authoritative; each point says what changed and why.

1. **The session/directory layer runs on the app thread, not the network thread** (refines §2.1 item 4).
   - Hub lists, online users, user lists, status replies, notes replies and weather replies are all built from application state: settings, the sim's object list, `Notes`. Running them on the network thread would have meant copying that state across threads continuously.
   - So `Network.cs`'s session logic becomes a protocol-neutral `Session` layer on the app thread. It consumes canonical messages from the inbound queue and sends canonical messages through `NetworkService`.
   - The network thread keeps only transport, plugins, `MeshManager`, router, relay/translation and `ObjectStateCache`. Forms keep reading hub/user state under `conch` as they do today. The snapshot carries only transport-level peer state (RTT, direct, established, protocol per kind).
2. **Identity is state that travels with positions, not a message the app sends** (refines §2.6/§2.7).
   - The app hands the network thread each object's `IdentityUpdate` together with its position. The core keeps it in `ObjectStateCache`.
   - The legacy plugin inlines it into every AircraftPosition/ObjectPosition. The JFP2 plugin sends Identity on change, plus a heartbeat, before a peer's first Position.
   - The Identity-before-Position ordering therefore leaves `Sim.cs` and becomes plugin-internal.
3. **The legacy plugin always relays legacy datagrams unchanged.** Every JFP2-capable node also speaks legacy. So legacy→JFP2 translation is never *needed*; the generic translation path (§2.6) only ever runs for JFP2→legacy.
4. **`SendPolicy` stays in `Sim` for now** (defers part of §2.1 item 3). `Sim` still chooses recipients and rates per tick, but hands one canonical message plus a recipient list to the network, and the router picks the protocol per recipient. Moving rate policy out of `Sim` is independent of the protocol split and can follow later.
5. **Guaranteed messages go out immediately**, then retry every 2 s. `LocalNode` queued them until its next tick. The byte-for-byte fixtures are unaffected.
6. **"Only while in a session" filtering moved into the core** (`NetworkCore.RequiresSession`). This is the legacy receiver's `if (localNode.Connected)` checks, applied the same way to every protocol.
7. **Encoding reuses the double dispatch.** A plugin's encoder is an `IMessageHandler` whose overloads write each message type, so there is no codec registry lookup on the hot path.
8. **Legacy reliability is fixed on our side**:
   - acks are matched by (id, recipient) (Finding 9);
   - resends go to the current route (Finding 8);
   - a departed peer's pending sends are dropped;
   - `Leave` clears every plugin's state and the relay slots;
   - an unparsable login address gets a proper `LoginFail` instead of the stale-buffer send;
   - a forwarded datagram is always sent raw. `LocalNode` could accidentally queue it as guaranteed if the last message it had prepared was guaranteed.
9. **The plugin contract as built** (refines §2.3):
   - `CanCarry(peer, kind)` replaces `GetLink`/`LinkInfo`.
   - `Send<T>(meta, message, ReadOnlySpan<NodeId> recipients)` sends one encoding to many recipients (legacy broadcasts need one shared guaranteed id).
   - `Tick()` takes no arguments; time comes from the host's `IClock`.
   - `OnPeerRemoved(Peer)` and `OnSessionReset()` handle cleanup.
   - The optional `IDescribesLinks` interface gives the UI a link-state string.
10. **`MessageMeta` as built** (refines §2.2): `Sender` (the original author, never the relay), `Recipient`, `EndPoint` (inbound: effective source; outbound: explicit destination), `Guaranteed`, `Forwarded`, `DataVersion`. There is no separate `Origin`/`Target`: `Sender` + `Recipient` cover it. `NetTime` lives in the messages that have one.
11. **Queues and snapshot** (refines §2.7):
    - One `BlockingCollection` mailbox wakes the network thread for both received datagrams and app commands; `System.Threading.Channels` wasn't needed.
    - The snapshot is published every 100 ms and carries transport state only (item 1). There is no `PublishObject` API: `Sim` calls `Network.SendAircraftPosition` and similar helpers (item 4).
12. **Router scope** (refines §2.5):
    - JFP2 never claims an indirect peer, and the target-capability propagation through `PeerInfo` was not built.
    - Instead, current builds don't originate relayed JFP2 at all: indirect peers use the legacy relay, which is always correct because every node speaks legacy.
    - Relayed JFP2 from older builds is still forwarded or translated.
13. **Guardrails not built yet** (§2.8): codec fidelity masks, and downgrade rules for kinds a protocol can't carry. Neither is needed until a protocol carries a field or kind that legacy can't.
14. **Recorder** (refines §6): the version split and moving the `Record` calls to the ingest are done. `Sim.Write/Read` stayed in `Sim` rather than moving to a recorder-owned serializer; the network no longer uses them.

### 2.11 Known structural debts

The protocol boundary is intact — plugins never see the app, and the session layer has no wire code. Four things were weaker than they should be; all four are now resolved or substantially so:
1. **Resolved (2026-09-23): `Network.cs` was one large class** (about 3,500 lines) mixing six jobs (hub directory, online users, the per-peer info table, DNS, the ingest into `Sim`/`Recorder`/`Notes`, the send helpers). It depended on `Main`, forms and `Settings` directly, so it couldn't be tested.
   - It is now a facade of about 420 lines (session commands, message routing, the tick order) plus eight parts in `JoinFS/Session/`: `NetBootstrap`, `PeerTable`, `SimSender`, `SimIngest`, `HubDirectory`, `HubHost`, `UserDirectory`, `SessionComms`.
   - The parts depend on narrow interfaces (`SessionInterfaces.cs`, plus `INetworkOutbox` on `NetworkService`). `MainSessionHost` is the one adapter over `Main`, the forms and `Settings`.
   - They are covered by `JoinFS.Tests/Session`, including end-to-end runs from the in-memory mesh into `SimIngest`.
   - Behaviour and tick order are unchanged. One latent bug was fixed along the way: a failed `hubs.dat` save left its hubs in the shared temporary list, so the next hubs tick would have removed them from the hub list.
   - See `docs/reference/joinfs-architecture.md` §7.
2. **Resolved (2026-09-23): some facts had two owners.**
   - The local addresses and `LocalId` now have one decider, `NetBootstrap`. It keeps an app-side `LocalIdentity` (the same class, so there's one `NodeId` rule and one shared-NAT endpoint rule) and hands every change to the network thread's copy. The two copies are the unavoidable cross-thread handoff, not two owners.
   - `DetectLocalAddress` exists once, on `LocalIdentity`.
   - The `sessionActive` flag is deleted. `MeshManager.Leave()` was already a cheap no-op with nothing to leave (checked: it only broadcasts to an already-empty peer list and resets already-default state), so `Network.Leave()` now always posts it unconditionally instead of trying to locally track "are we in a session" — there's no longer any app-thread state that could disagree with the network thread while the snapshot catches up. The observable side effects (the `MonitorEvent`/UI refresh) are still gated, but on `Snapshot.State` alone.
3. **Mostly resolved: legacy vocabulary in the canonical model.**
   - **Deleted, confirmed dead:** `MessageMeta.DataVersion` (write-only in both readers, `HubDirectory.Hub.dataVersion`/`PeerTable.Node.dataVersion`, themselves also deleted — grepped the whole app and test tree, zero reads) and `FlightPlanUpdate.FormatVersion` (its receive-side gating in `ReadFlightPlan` all keys off the envelope's `dataVersion`, never off this field; its one consumer, `Sim.Aircraft.flightPlanVersion`, was itself write-only everywhere, including two UI increment sites, and is deleted too). Both wire bytes are still written/read by `LegacyPlugin` for byte fidelity (`FlightPlanFormatVersion` is now a named `LegacyWire` constant) - only the canonical-model propagation and the dead app-side fields are gone.
   - **Deleted, high confidence:** `VariableSyncUpdate.Owner`. Every other per-object canonical message (Position, ObjectPosition, Identity, Event, RemoveObject) identifies the owner purely via `MessageMeta.Sender`, which the core's translation path already preserves as the true origin through a relay (§2.6). Checked before removing: this codebase's only sender (`SimSender`) always set `Owner` to its own id (trivially equal to `Sender`); the one place `Owner` could plausibly have diverged - shared cockpit - already ignored the wire value and re-derived the owner separately; no golden fixture exercised `Owner != Sender`; and JFP2's own decode path already discarded the wire value in favor of `meta.Sender` before this change, so JFP2 never actually carried it. The one scenario asked about specifically, broadcasting a recorded aircraft's variables (`Sim.UpdateAircraft`'s `IsBroadcast` rebroadcast path), doesn't create a divergence either - `BroadcastVariables` never threads the aircraft's true `ownerNuid` through at all, so the rebroadcasting node's own id is what goes out regardless, same as `Sender` would be. `LegacyPlugin` still reads/writes the wire's Owner field (byte fidelity), and its decoder logs (`NetLogLevel.Event`, so it's visible without enabling network monitoring) if a received value ever disagrees with `meta.Sender`, as insurance against a sender this codebase hasn't seen.
   - **Not touched, on reflection not actually vocabulary contamination:** `CommsScope.All`. It genuinely models a real (if currently unserved) legacy request kind for `CommsRequest.Scope` - `Session`/`Global`/`All` map 1:1 to three distinct legacy message ids. Its reuse on `NotesBundle.Scope` to pick between two length-field formulas is confirmed dead (the field it selects is never read by any decoder, including this codebase's own - the source comment says so directly: "two historical formulas for a length field nobody reads"), and a golden fixture (`app_notes_all.hex`, via `BulkNotes`) pins the exact byte-for-byte output of the `All` case, so collapsing the two formulas would break a legitimate wire-fidelity guarantee for zero behavioral gain. Left as documented, deliberate dead weight rather than restructured.
4. **The UI maps plugin names.** `SessionForm` colours its protocol column from the strings `"JFP2"` and `"Legacy"`.


---

## 3. What happens to existing code

| Before | Planned | As built |
|---|---|---|
| `Node.cs` framing, `PrepareMessage`/`Send`, `GuaranteedMessageOut/In` | `LegacyFraming.cs`, `LegacyReliability.cs` | `Net/Protocols/Legacy/LegacyWire.cs` (constants), `LegacyPlugin.cs` (framing, relay), `LegacyReliability.cs`. One reusable send buffer per plugin, used only on the network thread. |
| `Node.cs` Join/Login/AddNode/Leave/Pulse/Pathfinder logic | `MeshManager` + legacy mesh codecs | `Net/Core/MeshManager.cs`; the mesh codecs are part of `LegacyPlugin`'s encoder and decoder. |
| `Network.cs` `Write*/Read*` + `ReceiveMsg` switch | one codec file per message | All in `LegacyPlugin.cs`: an `Encoder` (one `IMessageHandler` overload per message) and a `Receive*` method per message. Pinned by golden-byte tests. |
| `Node.cs` `#region JFP2`, `Jfp2/*`, JFP2 regions of `Network.cs` | `Protocols/Jfp2/` split into several classes | `Net/Protocols/Jfp2/Jfp2Plugin.cs` (sessions, handshake, reliability, relay, encoder, decoder) plus the moved `Envelope`, `Negotiation` and `Codecs`. `Jfp2Bridge.cs` deleted. |
| Hub/online-user/DNS/address-book/credentials code | `Net/Directory/*` | Credentials: `Net/Core/CredentialStore.cs`. The rest is on the app thread (§2.10 item 1), split into `JoinFS/Session/`: `HubDirectory`, `HubHost`, `UserDirectory`, `NetBootstrap` (DNS, downloads) (§2.11 item 1). |
| Duplicated legacy/JFP2 receive handlers + recorder hooks | `NetworkIngest` | `Session/SimIngest.cs` for object data; each other kind is handled by the session part that owns its state, with `Network` forwarding (§2.11 item 1). |
| `Sim.cs` per-peer send loops with protocol branches | `Sim` publishes state, `SendPolicy` fans out | `Sim` keeps its rate policy and passes one message plus the due recipients; no protocol branches (§2.10 item 4). |
| `XPlane.cs` private `LocalNode` | `XPlaneLink` | `JoinFS/XPlaneLink.cs`, byte-identical framing (`XPlaneLinkTests`). |
| `Network`/`LocalNode` fields read by about 15 files | `INetworkService` + `NetworkSnapshot` | `Network` session methods, `NetworkService` commands, and `Network.Snapshot`. |

Pre-existing bugs fixed along the way (most have a regression test in `JoinFS.Tests/Net`):
- Tier 3 owner attribution.
- The relay-drop regression.
- `Leave()` doesn't clear JFP2 or routing state.
- LoginFail is written into a stale buffer.
- `RequestNuid` sends one guaranteed id to five hubs (an ack race).
- Findings 8/9 on *our* side.
- `ShowOnRadar` has no receiver.
- String8 stats are miscounted.

---

## 4. Build order (big-bang on a branch; each step is test-gated, not shipped)

1. **Pin the legacy wire first, before touching anything.**
   - Add a test harness that drives the *current* `Network.Write*Message` / `LocalNode` framing with fixed inputs, and saves the bytes as golden fixtures in `JoinFS.Tests/Fixtures/legacy/`.
   - Record pcaps of a live v26.5 session with the Wireshark dissector (commit 5e5eba1) as a second reference.
   - **Status (2026-09-23): done, apart from the pcaps.** 51 fixtures under `JoinFS.Tests/Legacy/Fixtures/*.hex`.
     - **How they were captured:** a harness drove the pre-rewrite implementation. It called the application and request writers directly, and injected crafted datagrams into a real `LocalNode` to capture its replies: JoinReply/JoinFail/LoginFail, AddNode, GuaranteedDone, PulseResponse, PathfinderResponse, `FLAG_FORWARD` relay, and the UserNuid reply. Capture used a fixed loopback port (46112), with the Pulse timestamp zeroed.
     - **Now that the old code is deleted**, the capture tests are gone too. The fixtures are the frozen specification of the legacy wire, and `LegacyPluginGoldenTests` checks the new plugin against them.
     - **Not pinned on purpose:** the LoginFail sent for an unparsable email address. It writes into a stale send buffer (a known bug, fixed by the rewrite).
     - **Done (2026-09-23):** v26.5 pcaps from a live session. See `docs/captures/README.md` — an
       unmodified v26.5 client's own `AircraftPosition` traffic (`DataVersion=21007`) is confirmed
       identical in shape whether it's talking to the live public hub network or to this branch's new
       hub, and the new hub's JFP2 Hello correctly gives up after 5 attempts against it and falls back
       to legacy for the rest of the session.
     - **Baseline:** the fixtures pin the *current* branch's writers. Their wire is a byte superset of v26.5: class code, WTC and `staticCgToGround` were appended as trailing fields that older peers ignore.
2. `Net/Core` types and `Net/Messages` (moving the JFP2 DTOs), `IDatagramTransport` plus `InMemoryTransport` for tests.
   - **Status: done.** Canonical types live in `JoinFS/Net/Messages` (namespace `JoinFS.Net`). The JFP2 codecs and the old `Network.cs` now use them. `WeatherReport` became `WeatherReply` and `WeatherUpdate`. Transport: `IDatagramTransport`, `UdpTransport` (receive thread), `InMemoryNetwork`.
3. `UdpTransport`, `NetworkService` thread, queues, snapshot, `PeerDirectory`, `Router`, `SendPolicy`, `ObjectStateCache`.
   - **Status: done.**
     - `NetworkCore`: demux, a router with a per-peer route cache, generic translation, and session gating.
     - Supporting types: `PeerDirectory`, `LocalIdentity`, `ObjectStateCache`, `CredentialStore`, `NetHash`, `IProtocolPlugin`/`IProtocolHost`.
     - `NetworkService`: its own thread; one mailbox fed by the receive thread and the app; pooled work items and inbound message boxes; the snapshot published every 100 ms; `Open` runs on the network thread and leaves any session before moving port.
     - `NetworkServiceTests` runs two services over real loopback UDP.
     - `SendPolicy` is deferred (§2.10 item 4).
4. `MeshManager` in the core, plus `LegacyPlugin`: framing, reliability, and codecs, including the mesh codecs. It must pass the golden-byte tests plus in-memory multi-node tests: join, relay, guaranteed, leave, and login fail.
   - **Status: done.**
     - `LegacyPluginGoldenTests` puts byte-identical output on the wire for every fixture except UsageLog, a dead message the new stack doesn't carry.
     - `LegacyMeshTests` covers join, introductions, wrong password, relay between unreachable nodes, guaranteed delivery under loss, segmentation, leave, expiry and session gating.
     - `LegacyRoundTripTests` checks every message kind.
5. `Net/Directory`: hubs, online users, DNS, credentials.
   - **Status: done differently** (§2.10 item 1).
     - Credentials moved to the network thread as `Net/Core/CredentialStore`. Mesh login checks happen there.
     - Hubs, online users, DNS and the address book stay in the app-thread session layer (`Network.cs`), which is now protocol-free.
6. `Jfp2Plugin`, ported from the regions listed above. Then generic translation, with in-memory tests for these topologies:
   - JFP2↔JFP2 direct
   - JFP2 via JFP2 hub
   - JFP2→legacy via hub
   - legacy→JFP2 via hub
   - legacy-only pair via a JFP2 hub
   - **Status: done.** `JoinFS/Net/Protocols/Jfp2/`: `Jfp2Plugin` plus the moved `Envelope`, `Negotiation` and `Codecs`, all in namespace `JoinFS.Net.Jfp2`.
     - **Behaviour changes versus the branch before the rewrite:**
       - Relayed JFP2 is no longer *originated*. An indirect peer goes over the legacy relay, which fixes the relay-drop regression.
       - A relayed JFP2 message for a legacy-only target is translated by the core with the true sender preserved, which fixes the Tier 3 owner bug.
       - Guaranteed classes are delivered hop-by-hop across that translation, instead of dropped. The hub acks upstream; the legacy plugin consumes the downstream ack addressed to the original sender.
     - **Tests** (`Jfp2PluginTests`): negotiation; identity-once-then-heartbeat; fallback to legacy; a mixed broadcast; guaranteed delivery under loss; translation of relayed JFP2 (Position, Identity and a guaranteed Event) for a legacy-only target.
     - "legacy→JFP2 via hub" never needs translation (§2.10 item 3), so it is covered by the legacy relay tests.
7. **App switch-over:**
   - Add `NetworkIngest` and the `Sim` publish API.
   - Move Recorder hooks to the ingest side.
   - Move Forms, Whazzup, WebSocket, Webhook, Log, AddressBook and Notes to the snapshot and commands.
   - Replace `XPlaneLink`.
   - **Status: done.**
     - The session layer (`Network.cs`, 6.5k → 3.5k lines) implements `IMessageHandler`/`INetworkEventHandler` and is the ingest. It drains the service each `Main.DoWork` tick, under `conch`.
     - Aircraft positions go through the legacy full `Sim.UpdateAircraft` path, with identity from a per-object cache. Identity changes are applied only when something changed.
     - `Sim` sends one canonical message per tick with its due recipients. It has no protocol branches left, and identity-before-position lives in the JFP2 plugin.
     - Recorder hooks are in the ingest. `Sim.VERSION` became `Recorder.FileVersion` (same value, so `.jfs` files are unchanged); the legacy wire has `LegacyWire.DataVersion`. `Sim.Write/Read` stay in `Sim`, now used only by the recorder and the X-Plane link, never by the network.
     - Forms, `Log` and `Program` read `Network.Snapshot`. `SessionForm`'s protocol column shows the plugins' `LinkState`.
     - **`XPlaneLink`** replaces the private `LocalNode` that `XPlane.cs` used. Its framing is byte-identical (`XPlaneLinkTests`), so no `NODE_VERSION` bump.
     - All six configurations build: FS2024, FS2020, FSX, P3D, XPLANE and CONSOLE.
8. Delete `Node.cs`, `Network.cs` and `Jfp2/`.
   - **Status: done.**
     - `Node.cs` and `Jfp2Bridge.cs` are deleted, and `Jfp2/` was moved under `Net/Protocols/Jfp2/`.
     - `Network.cs` remains as the protocol-free session layer rather than being deleted (§2.10 item 1).
9. Update `docs/protocol-v2-architecture.md`, `.claude/CLAUDE.md` (the architecture section, the threading model, the "legacy files untouched" rule) and the implementation plan.
   - **Status: done** for this document and `CLAUDE.md`. `protocol-v2-architecture.md` has a banner pointing here.

**Still open after the rewrite:**
- **Live interop.** Test against an unmodified v26.5 build and a real simulator:
  - legacy direct
  - **through a new hub — done (2026-09-23).** Behavioral (following another aircraft, correct
    protocol labels) and wire-level (`docs/captures/legacy-v26.5-client-vs-new-hub.pcapng`, see
    `docs/captures/README.md`) confirmation.
  - JFP2 pair — a new-build↔new-build capture exists (`docs/captures/
    jfp2-new-client-vs-new-hub.pcapng`) but doesn't involve v26.5, so this specific item (v26.5
    coexisting with a JFP2 pair) is still open.
  - mixed hub
  - X-Plane plugin link

  This could not be run in the development sandbox: the built app fails to load its own assembly there, and yesterday's unmodified build fails the same way, so the cause is environmental.
- **v26.5 pcaps as a second wire reference — done.** See `docs/captures/README.md`.
- **`SendPolicy`** out of `Sim` (§2.10 item 4).
- **Performance measurements (§5) - the Position micro-benchmark is done, with real numbers.**
  `JoinFS.Benchmarks` (BenchmarkDotNet, `[MemoryDiagnoser]`, in-process toolchain) benchmarks
  encode/route/decode of Position for both plugins in isolation - two two-node pairs built directly
  against `NetworkCore` (bypassing `MeshManager`; JFP2 negotiation only needs
  `Peer.RouteIsOwnEndPoint`, per §2.4), with a transport that goes silent after capturing one real
  datagram so each `[Benchmark]` measures only the plugin's own allocation, not a paired node's
  receive pipeline too. Run with `dotnet run -c CONSOLE --project JoinFS.Benchmarks`.

  **Correction to an earlier version of this note:** it previously reported this couldn't be run at
  all, attributing a `FileNotFoundException` to the same environmental block as the live-interop
  item above (a plausible-looking match: it reproduced identically on real hardware too, not just
  the sandbox). That diagnosis was wrong. The real cause: `JoinFS.csproj`'s CONSOLE `PropertyGroup`
  hardcodes `<PlatformTarget>ARM64</PlatformTarget>` unconditionally - contradicting CLAUDE.md's
  documented "CONSOLE targets x64" and every other CONSOLE-building path's assumption. CI's
  `build-test.yml` masks it (passes `/p:PlatformTarget=${{ matrix.arch }}` externally, which wins
  over the in-file value), but **`JoinFS/util/buildAll.ps1` does not** - it runs `dotnet build
  .\JoinFS.csproj -c $config` with no arch override at all, so on any non-ARM64 machine it silently
  produces an ARM64 `JoinFS-CONSOLE.dll` today. The Dockerfile's `-r linux-musl-x64` publish wasn't
  checked and may or may not be immune - worth confirming separately. `JoinFS.Benchmarks.csproj`
  works around it by passing `PlatformTarget=x64` explicitly in its `ProjectReference`'s
  `AdditionalProperties`, but **`JoinFS.csproj` line ~133 itself is unfixed** - flagging rather than
  changing it, since shared build configuration is outside this benchmark task's scope and the
  ARM64 value might be someone's unfinished work rather than a plain mistake.

  Two bugs in the benchmark harness itself surfaced once the above was fixed and it could actually
  run: `SwitchableTransport.Capture` was wired to the receiving side's transport instead of the
  sending side's (capture only ever fires inside `Send()`), and calling a plugin's `Send()` directly
  (bypassing `NetworkCore.Send`, which normally auto-fills `MessageMeta.Sender` when unset) left
  `Sender` invalid, so the legacy encoder's identity lookup (keyed by `Sender`) silently found
  nothing and sent nothing. Both fixed; a third (JFP2 negotiation never completing) was `Peer`'s
  `ExpireTime` defaulting to 0, so `MeshManager.Tick()` expired the hand-added peer before the
  Hello/HelloAck loop finished - same thing `LegacyPluginGoldenTests`' `Stack.AddPeer` already works
  around by setting `ExpireTime = 1e9`.

  **Results** (`ShortRun`, this development machine - not a substitute for a real run on production-
  class hardware, but the shape is informative):

  | Method | Mean | Allocated |
  |---|---:|---:|
  | EncodeLegacy | 209 ns | 0 B |
  | EncodeJfp2 | 77 ns | 0 B |
  | DecodeLegacy | 560 ns | 352 B |
  | DecodeJfp2 | 64 ns | 40 B |
  | RouteLookup | 11 ns | 0 B |

  Encode matches the design's "zero allocations in steady state" target for both protocols, and
  JFP2 encodes about 2.7x faster than legacy (a simpler envelope + no guaranteed-delivery
  bookkeeping on a non-guaranteed send, versus legacy's multi-field writer). Route lookup is the
  expected single array read. **Decode is not allocation-free for either protocol** - legacy's 352 B
  is the likelier one to matter (it inlines identity, with several `BinaryReader.ReadString()`
  calls, into every position datagram by design); JFP2's smaller but still nonzero 40 B for a bare
  Position decode (no identity involved) is worth a closer look before trusting it's irreducible.
  This contradicts §5's "zero allocations per datagram" receive-path target as stated and is worth
  investigating, not just noting.

  The 50-simulated-peer system tests (§5's other half: CPU and wake latency under load) are not yet
  built.

---

## 5. Performance targets (verify, don't assume)

- **Send path:** Sim publish → fan-out → router lookup (array read) → struct codec into pooled buffer → `SendTo`. Zero allocations per datagram in steady state.
- **Receive path:** pooled receive → plugin decode to struct → queue → ingest. Zero allocations per position datagram.
- **How to check:** a BenchmarkDotNet micro-benchmark (or an allocation-counting test using `GC.GetAllocatedBytesForCurrentThread`) for encode, route and decode of Position. Compare CPU and wake latency under the CONSOLE hub with about 50 simulated peers on the in-memory transport, against the current build.

---

## 6. Recorder

*Status: done, except that `Sim.Write/Read` stayed in `Sim` (§2.10 item 14).*

- **Today, Recorder stores Sim state, not packets.** However:
  - it reuses the **legacy wire serializers** (`Sim.Write/Read`, `Sim.cs:3401-3690`);
  - it shares the **wire's version counter** (`Sim.VERSION` = 21008, which gates file reads, `Recorder.cs:1849`);
  - `Network`'s receive handlers call `recorder.Record` directly for network aircraft.
- **Recommendation: keep the `.jfs` format byte-identical and just cut the coupling.**
  - Move `Sim.Write/Read` into a Recorder-owned, frozen `JfsFrames` serializer.
  - Split the version into `LegacyWire.DataVersion` and `Recorder.FileVersion`. Both start at 21008.
  - Move the `Record` calls into `NetworkIngest`.
  - Old recordings keep working, and a protocol change can never again change the recording version.
- There is also an existing doc/code mismatch: `StaticCgToGround` is commented out in the frames (`Sim.cs:3521/3563`). Log it for a decision; it is out of scope here.

---

## 7. Verification

- **Unit tests:** `dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug -p:Platform=x64`, covering:
  - golden-byte legacy codecs
  - existing JFP2 codec, envelope and negotiation tests (moved)
  - router selection
  - `ObjectStateCache` cleanup
- **In-memory integration tests** with several `NetworkCore` nodes on `InMemoryNetwork` (`JoinFS.Tests/Net/TestMesh`), plus `NetworkServiceTests` over real loopback UDP:
  - all five topologies in step 6
  - every message kind
  - guaranteed delivery under drops
  - Findings 7/8/9 scenarios
  - leave/rejoin state
- **Interop:** live test against an unmodified **v26.5** binary (both legacy direct and via a new hub), following `docs/protocol-v2-manual-test-guide.md` and the mixed-version section of the implementation plan. Compare Wireshark captures against the golden pcaps.
- **Builds:** `dotnet build JoinFS\JoinFS.csproj -c <cfg>` for all configurations (`JoinFS/util/buildAll.ps1`), especially `CONSOLE` (no forms) and `XPLANE`. For X-Plane, run a live plugin-link smoke test, since `XPlaneLink` replaces the private `LocalNode`.
- **Recorder:** play back an existing `.jfs` recorded with v26.5 and the current branch, record a new one, and replay it.
