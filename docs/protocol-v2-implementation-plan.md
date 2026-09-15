# JFP2 Implementation Plan

Ordered, checkable task list for implementing the JFP2 protocol designed in
`docs/protocol-v2-design.md` and `docs/protocol-v2-architecture.md`. **Keep this file updated** —
check items off as they're done, add sub-tasks as needed, and note any deviation from the design
docs (with the reason) inline so the next session doesn't redo the same analysis.

File paths and line numbers below are anchored to commit `b2c914f` (`v26.5-10-gb2c914f`, 2026-09-11)
of `github.com/tuduce/JoinFS`. They will drift as the codebase changes — treat them as a starting
point for `grep`/search, not as guaranteed-current.

Status legend: `[ ]` not started, `[~]` in progress, `[x]` done.

**2026-09-14: see `docs/protocol-v2-implementation-review.md`** for an independent field-by-field
audit of everything Phases 0-5 actually built, plus a rollout-readiness verdict. Short version: the
field-level design holds up. Three of the review's findings were fixed the same day:

- **Finding 1** (guaranteed delivery entirely unimplemented, silently downgrading Event/Notes/
  WeatherReply's reliability versus their legacy equivalents): `Jfp2.Envelope`'s guaranteed-delivery
  extension is now read/written, and `LocalNode` retries a guaranteed send until acked via a new
  `GuaranteedDone` message.
- **Finding 2** (Position hot-path send allocating on every call): `SendJfp2Datagram` now sends off an
  `ArrayPool`-rented buffer instead of a fresh `byte[]` per call.
- **Finding 5, first half** (`VariableSyncV1Codec`'s `String8` entries were a fixed 8-byte ASCII field
  that silently truncated/mangled anything longer or non-ASCII - confirmed a real regression, not a
  safe assumption, once checked against `SimConnectInterface.cs`'s own string requests and the legacy
  `String8Variables` message's actual wire format, which was never 8-byte-capped): now length-prefixed
  UTF8 like every other string field, with `Network.cs`'s send-buffer sizing fixed to match (it
  previously assumed a fixed worst-case per entry, which the old encoding guaranteed but the new one
  doesn't).

All 6 configurations build clean; the test suite is at 127/127. None of these three fixes has been
verified live yet (real packet loss for Finding 1, a profiler for Finding 2, a genuinely-long or non-
ASCII synced variable value for Finding 5 - see the review's §6 and its Finding 5 entry). Finding 5's
second half (`WireText`'s unbounded `ushort` length cast) remains open, low-priority. Read the review
before starting Phase 6 or touching reliability/allocation/string-encoding-sensitive code in
`JoinFS/Jfp2/`.

## Phase 0 — Validate the reference implementation before porting anything

The reference code in `ProtocolV2Reference/` (repo root) was written without access to a .NET SDK and
has never been compiled. Do this first; everything else builds on it.

- [x] `cd ProtocolV2Reference && dotnet build` — **builds clean, 0 warnings/0 errors**, on .NET SDK
      10.0.401 (2026-09-13). The two design-level issues fixed by inspection during authoring (see
      `ProtocolV2Reference/README.md`) were the only mistakes — nothing else surfaced once compiled.
- [x] `dotnet run` — output matches every expectation in `Program.cs`'s comments exactly: negotiated
      table Position→v1, Identity→v1, VariableSync→v0 (as expected — B never declared it), Coalescing
      agreed / QuantizedPosition not agreed; Hello round-trip 46 bytes with offers/extension intact;
      PositionV1 round-trip 79 bytes; PositionV2 round-trip 61 bytes with **zero** quantization error
      for the demo's test coordinate (well within the claimed ~1.1cm worst case); Identity/VariableSync
      round-trips correct; legacy/JFP2 datagram detection correct both ways; final size table matches
      §6.1 exactly (legacy 116B, JFP2 v1 87B/25.0% smaller, JFP2 v2 69B/40.5% smaller).
- [x] Ported the demo's negotiation/envelope assertions into real xUnit tests — see
      `JoinFS.Tests/Jfp2/EnvelopeTests.cs` and `JoinFS.Tests/Jfp2/NegotiationTests.cs` (14 tests, all
      passing under `dotnet test -c FS2024-Debug`). **Deviation:** these test the code as ported into
      `JoinFS/Jfp2/` (Phase 1), not the standalone reference copy, since that's the code that matters
      going forward and it doubles as Phase 1 verification. `CodecRoundTripTests.cs` (Position/Identity/
      VariableSync codecs) is **deferred to Phase 2/3**, since Phase 1 only ports `Envelope.cs`/
      `Negotiation.cs` per this plan's own scope — no codec exists yet under `JoinFS/Jfp2/Codecs/` to
      test against. Add that file when Phase 2 lands the first real codec.
- [x] No discrepancy found between the Python-verified numbers and the compiled C# output — every
      number in `docs/protocol-v2-design.md` §6.1 reproduced exactly (see `dotnet run` output above).

## Phase 1 — Land JFP2 as dead code (design doc §9 step 1) — DONE 2026-09-13

Goal: JFP2 datagrams can be sent/received and negotiation completes, but nothing depends on it yet.
Legacy behavior must be provably unchanged throughout this phase.

- [x] Created `JoinFS/Jfp2/Envelope.cs` and `JoinFS/Jfp2/Negotiation.cs`, ported from
      `ProtocolV2Reference/Wire.cs`/`Negotiation.cs` with namespace `JoinFS.ProtocolV2.*` →
      `JoinFS.Jfp2.*`. **Deviation:** `Envelope.IsLegacyDatagram` no longer references a separate
      `Legacy.LegacyFrame.VersionConstant` type (the reference's `Legacy.cs` was not ported) — the
      constant (`0x520B`) is inlined directly as `Envelope.LegacyVersionConstant` with a comment
      explaining it deliberately duplicates (not reads) `LocalNode.VERSION`, since the production
      dispatch in `ReceiveMessages()` only ever needs the one-byte magic check
      (`messageData[0] == Envelope.Magic`), not a full legacy-header parse — `Legacy.cs` was reference-
      demo-only scaffolding. `PeerKey` was ported unused (reserved for a later phase's membership
      lists) per the design doc.
- [x] `LocalNode.ReceiveMessages()` (`JoinFS/Node.cs`): added the magic-byte check right after
      `udpClient.Receive` returns `messageData`, routing `0xFA` datagrams to `Jfp2ReceiveMsg(...)` and
      everything else to the unmodified `ReceiveMsg(endPoint)` — confirmed this is the only line
      touching the legacy receive path (`git diff` on the `ReceiveMessages` method shows only the added
      branch; `ReceiveMsg` itself has zero changes).
- [x] Added `readonly Dictionary<Nuid, Jfp2.PeerSession> jfp2Sessions` alongside `nodes`, cleaned up
      (`jfp2Sessions.Remove(nuid)`) in the same `DoWork()` pass that removes an expired node from
      `nodes`, so it can't leak.
- [x] Implemented Hello/HelloAck send and receive (`SendJfp2Hello`, `SendJfp2HelloAck`,
      `HandleJfp2Hello`, `HandleJfp2HelloAck`, `Jfp2ReceiveMsg` — all in `JoinFS/Node.cs`'s new
      `#region JFP2`). Only offered to peers already present in the legacy `nodes` dictionary (i.e.
      after legacy Join/JoinReply/AddNode has already registered them), matching the sequence diagram
      in `docs/protocol-v2-architecture.md` §3. The very first Hello to/from a peer has no established
      PeerId yet, so both send and reply address the raw `IPEndPoint`/`node.routeEndPoint` — matching
      to a known `Nuid` on receive is done by a linear scan over `nodes` matching `endPoint`
      (`FindNuidByEndPoint`), the same style of match the legacy guaranteed-delivery reassembly already
      uses. **Deviation:** `LocalJfp2Offers` is an empty list and `LocalJfp2Capabilities` is `0` for
      now — no application codec exists yet (Phase 2+), so there is nothing truthful to offer; the
      negotiation algorithm already handles an empty offer list gracefully (every class simply
      resolves to version 0, per `docs/protocol-v2-design.md` §5.3), confirmed by
      `NegotiationTests.Resolve_ClassOnlyOneSideKnows_FallsBackToVersionZero`.
- [x] Added the Hello-retry loop as `DoJfp2Handshake()`, called from `DoWork()` immediately after
      `DoPulse()`. **Deviation from the plan's literal wording:** rather than one shared
      `JoinFS.Timer` instance (that idiom is a singleton-style polled interval, not naturally suited to
      a *per-peer* collection), retry bookkeeping (`HelloAttempts`, `NextHelloAttempt`) lives as two
      extra fields directly on `Jfp2.PeerSession`, compared against `main.ElapsedTime` — same polling
      idiom (`Timer.Elapsed`'s `now > elapseTime` check), just inlined per-peer instead of via the
      shared `Timer` class. Retries every 2s (`JFP2_HELLO_RETRY_INTERVAL`, matching the guaranteed-
      message resend cadence), gives up after 5 attempts (`JFP2_HELLO_MAX_ATTEMPTS`) and sets
      `AssumedLegacy = true`.
- [x] Manual verification — **JFP2↔JFP2 case, confirmed on real hardware/real sockets:** ran two
      patched `CONSOLE-Debug` instances on loopback (`--create --port 6112` / `--join 127.0.0.1:6112
      --port 6113`, both `--nosim --nogui --background`). Observed in the logs (`monitor.network`
      temporarily flipped on for this run only, reverted immediately after — see git history/diff,
      no trace left in the tree):
      - Legacy Join → JoinReply → AddNode → Pulse/PulseResponse all worked exactly as before, for the
        entire session duration, with JFP2 running alongside on the same socket.
      - Both sides independently sent Hello once the other appeared in their legacy `nodes` dict; a
        genuine race surfaced organically (node B received node A's Hello ~1ms *before* B had
        finished registering A from A's own `JoinReply`) — `HandleJfp2Hello` handled it exactly as
        designed: logged `"Hello from unknown endpoint ... ignored (not in legacy mesh yet)"` and
        returned cleanly, no exception, no retry storm.
      - Both sides nonetheless reached `HandshakeComplete = true` within ~46ms — B's *own* Hello (sent
        moments later) reached A and A's HelloAck reply closed the loop on B's side, while A's session
        with B completed when A received B's Hello and replied with its own HelloAck. This is a nice
        emergent property, not a bug: either direction of a successful Hello/HelloAck round completes
        that side's local session, so the handshake self-heals around a single dropped/premature
        attempt without needing the *same* Hello to round-trip.
      - Neither side retried Hello again afterward (confirmed via log grep) — `DoJfp2Handshake`'s
        `!session.HandshakeComplete` guard correctly stopped further attempts once negotiation
        finished.
      - **Update, 2026-09-13 (during Phase 2's own manual verification):** the previously-unverified
        "unanswered Hello → `AssumedLegacy`, zero behavior change" path **has now been confirmed on
        real hardware**, incidentally, while verifying Phase 2 — see that section's writeup. A patched
        build joined the real, unpatched `joinfs.famtuduce.com:6112` hub; its Hello went unanswered for
        exactly 5 attempts over ~10s, then logged `"did not answer Hello after 5 attempts - assuming
        legacy-only peer"` and set `AssumedLegacy`, with legacy Join/JoinReply/Pulse traffic against
        the real hub completely unaffected throughout (and the node expiring normally ~20s later when
        the hub stopped responding, per the legacy `EXPIRE_TIME` logic, also untouched).

## Phase 2 — Migrate one low-risk message class (design doc §9 step 2) — DONE 2026-09-13

- [x] Picked `Status`/`StatusRequest` (network-protocol.md §8.6), per the design doc's own suggestion.
      **Deviation:** the design catalog (§4.3) only reserved one class slot ("Status") for this whole
      exchange, but the legacy protocol has two distinct messages with different shapes here. Rather
      than overload one class with a discriminator field, appended `StatusRequest = 8` to the
      Application partition in `JoinFS/Jfp2/Envelope.cs` (`Status` stays `7`) — this follows the
      catalog's own stated evolution rule ("always append a new class at the next unused number"),
      mirroring how `Hello`/`HelloAck` already split the internal partition.
- [x] Implemented `StatusRequestV1Codec`/`StatusV1Codec` under `JoinFS/Jfp2/Codecs/StatusCodecs.cs`,
      plus the shared `ICodec<T>`/`CodecRegistry` infrastructure (`JoinFS/Jfp2/Codecs/ICodec.cs`,
      ported from the reference) and a `WireText` helper for the length-prefixed UTF8 strings both
      codecs need (factored out so later phases' codecs — Event/FlightPlan/Notes/Weather — can reuse
      it instead of each duplicating `IdentityV1Codec`'s private copy, as the reference does).
      **Deviation:** every field is always present on the wire (no legacy-style "only if `AtcCount>0`"
      / "only if `HubEnabled`" conditional writes) — `Status` is low-frequency enough that a few empty
      strings cost nothing measurable, and this is exactly the EOF-sensing/conditional-shape pattern
      design doc §1.3/§7.5 wants JFP2 to retire. Field order and meaning otherwise match the legacy
      wire shape exactly. Verified with 11 xUnit tests
      (`JoinFS.Tests/Jfp2/StatusCodecTests.cs`): fixed-size `StatusRequest` round-trip, all 4
      HubEnabled/HubListRequested flag combinations, full-hub-block `Status` round-trip, non-hub
      round-trip, empty-string edge case, registry resolution, unregistered-version throwing, and a
      guard asserting `Status`/`StatusRequest` never collide.
- [x] Split the send call sites. **Deviation from the literal "group neighbors, parallel loop" pattern**
      (docs/protocol-v2-architecture.md §8.2's table, written with `Broadcast()`-style hot-path
      messages like Position in mind): `Status`/`StatusRequest` are unicast point-to-point sends to a
      single `IPEndPoint` (a hub, an address-book entry, a StatusRequest's direct replier), never
      broadcast to `nodes`, so the natural adaptation is a per-call-site `if
      (localNode.TryGetJfp2AppPeer(endPoint, class, out nuid, out version)) { <encode+SendJfp2Application> }
      else { <unchanged Write*Message()+Send()> }`, added as `Network.SendStatus`/`SendStatusRequest`.
      Migrated the 4 self-contained call sites (`DoAddressBook` ×2, `SubmitHub`, the StatusRequest-
      reply path). **Deliberately left `DoHubs()`'s hub-list ping loop and its `pendingHubsTimer` block
      unmigrated** — both share ONE legacy `sendBuffer` prepared once and fanned out to multiple
      endpoints across two independently-timed blocks with no re-prepare in between; splitting some of
      those sends to JFP2 while others keep depending on that shared buffer's exact contents was judged
      too fragile to be "low-risk" for this phase. This is a low-cost omission in practice: hub/
      address-book endpoints are essentially never also legacy-mesh (`nodes`) peers (Status is
      directory/discovery traffic, orthogonal to session membership — see the "manual verification"
      notes below for why this made live end-to-end testing of the send side genuinely hard to trigger
      through the CLI). Revisit `DoHubs()` in a later pass once the buffer-reuse contract there is
      either confirmed safe or restructured.
- [x] Added `LocalNode.TryGetJfp2AppPeer`/`SendJfp2Application`/`Jfp2ReceiveNotify` (the JFP2 analogue
      of the legacy `receiveNotify`) to bridge `LocalNode` (owns `jfp2Sessions`/the socket) and
      `Network` (owns the application logic) — `SendJfp2Datagram` (Phase 1, previously Hello/HelloAck-
      only with a hardcoded `Internal` flag) now takes an explicit `EnvelopeFlags` parameter and a
      `ReadOnlySpan<byte>` payload so it serves both partitions.
- [x] Refactored the legacy `case MESSAGE_ID.StatusRequest:`/`case MESSAGE_ID.Status:` bodies in
      `Network.ReceiveMsg` to read into the same `Jfp2.Codecs.StatusRequestUpdate`/`StatusUpdate`
      values the JFP2 side decodes into, then call new shared `HandleStatusRequest`/`HandleStatus`
      methods — the exact "codec decodes into a version-agnostic value, shared logic downstream"
      principle design doc §7.7 describes for the hub bridge, applied here even without a hub in the
      picture. **The actual wire reads (`reader.ReadXxx()` calls, their order, and every version gate)
      are byte-for-byte identical to before** — only what happens to the resulting values afterward was
      consolidated; this was checked by re-deriving each refactored block directly against the original
      and confirmed via the build/tests below. One write-only field (`Hub.dataVersion`, confirmed via
      repo-wide grep to have no reader anywhere) has no JFP2 equivalent — a JFP2-sourced `Status` update
      passes `0` for it, since JFP2 has no single scalar analogous to the legacy per-message
      `DataVersion`.
- [x] Build: all 6 configurations (FS2024/FS2020/FSX/P3D/XPLANE/CONSOLE) compile with 0 errors (the
      one XPlane.cs warning is pre-existing/unrelated). Full test suite: 80/80 passing.
- [x] Manual verification:
      - **Negotiation, confirmed live:** re-ran the Phase-1-style two-instance loopback test
        (`--create`/`--join` on `127.0.0.1`); both sides' `PeerSession.AgreedAppVersion` now resolves
        `Status=1, StatusRequest=1`, proving the new `LocalJfp2Offers` entries flow correctly through
        the exact same Hello/HelloAck/`Negotiator.Resolve` pipeline Phase 1 already validated end-to-
        end over real sockets.
      - **Legacy byte-identical fallback, confirmed against the real hub the user provided
        (`joinfs.famtuduce.com:6112`):** joined briefly (`--nosim --nogui --background`, ~30s,
        disconnected promptly). Legacy Join/JoinReply/Pulse worked exactly as an unpatched build would;
        JFP2 Hello (unanswered by the real, non-JFP2 hub) correctly gave up after 5 attempts and set
        `AssumedLegacy` — so every `SendStatus`/`SendStatusRequest` call this session would make against
        that hub is guaranteed to take the untouched legacy branch. See the Phase 1 section above for
        the log excerpt (this run is what closed that phase's previously-open verification gap).
      - **Not exercised end-to-end: an actual live JFP2-routed `Status`/`StatusRequest` send+receive
        over a real socket.** This turned out to be genuinely hard to trigger deterministically through
        the CLI in this sandbox: every send call site that could plausibly hit a `nodes`-dict/JFP2-
        negotiated peer is either GUI-gated (`DoAddressBook` requires the Address Book *form* to be open
        — `#if !CONSOLE` and `addressBookForm.Visible`), deliberately left on the legacy path
        (`DoHubs`), or requires `SubmitHub` to be called against an endpoint that *also* happens to be
        an already-joined session peer, which nothing in a scriptable CLI flow does today (Status is
        hub-discovery traffic, orthogonal to session `nodes` membership by design). Confidence instead
        comes from two independently-verified halves that compose deterministically: the codec's wire
        correctness (11 unit tests) and the peer-eligibility/negotiation machinery (live-tested above,
        and shared byte-for-byte with the already-proven Phase 1 Hello path). A maintainer with a GUI
        build and the Address Book form (or willing to add a temporary CLI hook to call `SubmitHub`
        against an already-`nodes`-joined peer) should do the one remaining live check: confirm a
        `Status`/`StatusRequest` datagram between two patched, already-`AgreedAppVersion`-negotiated
        peers is actually `0xFA`-prefixed on the wire, not just correctly routed in code.

## Phase 3 — Migrate Identity and VariableSync (design doc §9 step 3) — DONE (scoped) 2026-09-13

This closes the specific bugs documented in `docs/protocol-changes-v26.4-v26.5.md` §1.2 and
`docs/recording-protocol.md` §7.1/§7.2, for **direct JFP2↔JFP2 peer pairs**. `Jfp2Bridge` (hub-role
translation) was deliberately **not** implemented this phase — see the dedicated note below for why,
and what that does and doesn't limit.

- [x] Ported `IdentityV1Codec`/`VariableSyncV1Codec` for real — `JoinFS/Jfp2/Codecs/IdentityCodec.cs`,
      `JoinFS/Jfp2/Codecs/VariableSyncCodec.cs`. `VariableEntry.Vuid` is wired to the real value: the
      send side (`Network.SendJfp2VariableSync`) reads directly off `Obj.variableSet.integers/floats/
      string8s`, whose keys are already the real `VariableMgr.CreateVuid`-derived vuid (the same
      dictionaries the legacy `SendIntegerVariablesMessage`/etc. already send from) — there was no
      placeholder to replace, since Phase 3 never introduces a second source of vuids; see design doc
      §6.5 (already-settled) and `docs/protocol-v2-implementation-plan.md`'s own note not to re-derive
      it. **Deviation:** both codecs' `ObjectId` is `uint`, not the reference's `ushort` — `Obj.netId`
      (`JoinFS/Sim.cs`) is a real `uint` (a raw SimConnect object id / the sender's own assigned id),
      and narrowing it would have silently corrupted any id above 65535. Verified with 13 xUnit tests
      (`JoinFS.Tests/Jfp2/IdentityCodecTests.cs`, `VariableSyncCodecTests.cs`): full round-trips for
      Aircraft and non-Aircraft identity, the full `uint` `ObjectId` range, mixed-kind `VariableSync`
      entries, String8 truncation/padding, empty entries, the 255-entry-per-message boundary, and
      registry resolution.
- [x] Identity is sent on change and heartbeat (4s, inside design doc §6.2's "3-5 seconds"), from
      `Network.SendJfp2IdentityIfNeeded` — diffed per `(Obj.netId, peer Nuid)` against a new
      `jfp2IdentitySendState` cache, a no-op for any peer that hasn't negotiated Identity. **Deviation:**
      not on its own timer alongside `DoPulse()` as literally suggested — Identity's broadcast
      eligibility (`IsBroadcast(obj) && !obj.Injected`) is Sim-layer state `LocalNode` has no access to,
      so it rides `Sim.cs`'s existing `variablesTimer` (0.2s / 5Hz) instead: cheap to *check* every tick
      (a field-equality compare), genuinely *sent* only on change or heartbeat. This also naturally
      unifies with the VariableSync wiring below, which needed the same per-object/per-peer loop anyway.
- [x] `VariableSync` wired at the same call sites as the legacy Integer/Float/String8 messages
      (`JoinFS/Sim.cs`'s `variablesTimer` block, both the shared-cockpit and broadcast branches) via a
      new `Network.SendVariableUpdate(peerNuid, ...)`: JFP2 `VariableSync` if that peer negotiated it,
      otherwise the byte-unchanged legacy three-message send, unicast to that one peer. **Deviation:**
      the broadcast branch previously called `SendIntegerVariablesMessage(new Nuid(), ...)` once
      (→ `LocalNode.Broadcast()`, mesh-wide) — this had to become an explicit `foreach (var peerNuid in
      GetNodeList())` loop to let each peer resolve independently, same as Phase 2's Status split. This
      is wire-identical for any peer that ends up on the legacy branch: `Broadcast()` already just loops
      `Send()` per node internally (`JoinFS/Node.cs`), so restructuring the *caller's* loop changes
      nothing about what bytes reach any given peer. Chunks at 200 entries/message
      (`JFP2_VARIABLE_SYNC_CHUNK_SIZE`) to stay under the codec's 255-entry-per-message cap, since a
      single object's combined integer+float+string8 set can in principle approach
      `MAX_INTEGER_VARIABLES+MAX_FLOAT_VARIABLES+MAX_STRING8_VARIABLES` (100+100+80=280).
- [x] Receive side: `Network.HandleJfp2Identity`/`HandleJfp2VariableSync`, wired through the existing
      `jfp2ReceiveNotify`/`HandleJfp2Application` dispatch from Phase 2 (no `LocalNode`/dispatch changes
      needed — Identity/VariableSync are just two more application-partition classes on the same path).
      `HandleJfp2Identity` reuses `Sim.UpdateObject(obj, model, livery, icaoType, icaoAirline, classCode,
      wtc, classCodeConfirmed, typerole)` (the same public entry point the legacy path uses to apply
      identity + re-run substitution matching), plus `Sim.RemoveObjectFromSim` on a detected model
      change and `Sim.SetAtcId` on a detected callsign change — matching legacy's own side effects for
      those two cases, not just the field assignment. `HandleJfp2VariableSync` regroups the self-
      describing entry list back into the three typed dictionaries `Sim.UpdateAircraft`'s existing
      overloads expect (including the recorder integration), preserving the exact legacy semantics
      documented for that receive path — including its existing gap (non-Aircraft `Obj` variables are
      not applied on receive; matches `docs/network-protocol.md`/`docs/recording-protocol.md`'s own
      documented scope, not something this phase changes).
      **Known, deliberately out-of-scope edge case:** Identity can only ever describe an object that
      already exists locally (created by the still-legacy Position message that also creates it — see
      the next bullet); if it doesn't exist yet, `HandleJfp2Identity` is a safe no-op. This is fine
      today because Position hasn't moved to JFP2 yet (Phase 4) and legacy Position always creates the
      object; the "Identity before/atomic-with first Position" ordering design doc §7.7 calls out only
      becomes a real requirement once Position itself is JFP2-sourced, and needs re-examination then.
- [ ] `Jfp2Bridge` (hub-role translation) — **not implemented this phase.** Reasoning:
      1. A prerequisite gap surfaced during research and was never fully closed: the legacy receive-side
         `IntegerVariables`/`FloatVariables`/`String8Variables` cases in `Network.ReceiveMsg` route
         through `main.sim?.UpdateAircraft(...)` — the null-conditional means **received variables are
         dropped outright when `main.sim == null`** (a sim-less `CONSOLE` hub). The sim-less-hub
         *write*-side cache described in `docs/protocol-changes-v26.4-v26.5.md` §1.6 lives inside
         `VariableMgr.Set.Update*` (`JoinFS/VariableMgr.Set.cs` ~lines 504/632/730: `main.sim == null`
         is one of the conditions that stores a value directly instead of pushing it through
         SimConnect) — but the actual call path that reaches those methods on a sim-less hub was not
         located. `Jfp2Bridge`'s whole design depends on mirroring that cache shape correctly
         (`docs/protocol-v2-design.md` §7.7); building it against an unconfirmed assumption risked a
         hub that silently drops or corrupts relayed data for real multiplayer sessions — too high a
         blast radius to guess at.
      2. It's also the single highest-complexity, highest-risk piece of the whole JFP2 rollout (a
         3-way protocol translation, not a 2-party negotiation), and doing it hastily is worse than not
         doing it yet.
      3. Its absence costs nothing for the common case this phase already fully delivers: **any two
         directly-connected JFP2-negotiated peers** get full Identity/VariableSync benefit today,
         exactly as verified below. Only relay *through a hub that itself never negotiates JFP2 with
         one side* needs the bridge — and every such peer already has a complete, correct fallback: it
         simply never negotiates JFP2 with that hub (or that hub is a legacy build entirely) and uses
         the legacy path throughout, precisely as today.
      Follow-up for whoever picks this up: first resolve the sim-less-hub receive-path question above
      (grep for where a hub-mode `VariableMgr.Set` instance actually gets its `Update*` called from
      when `main.sim` is null), *then* design `Jfp2Bridge`'s per-object identity/vuid caches against a
      confirmed shape.
- [x] Build: all 6 configurations compile with 0 errors (the one pre-existing `XPlane.cs` warning is
      unrelated). Full test suite: 91/91 passing.
- [x] Manual verification:
      - **Negotiation, confirmed live:** re-ran the same two-instance loopback test used for Phases 1-2;
        both sides now resolve `Identity=1, VariableSync=1` in `AgreedAppVersion`, proving the new
        offers flow correctly through the already-proven Hello/HelloAck pipeline. Session ran cleanly
        for the test duration with regular Pulse traffic and zero exceptions/errors in either log,
        confirming the new `DoJfp2IdentityCleanup()` hook into `Network.DoWork()` doesn't destabilize
        anything.
      - **Not exercised end-to-end: a live Identity/VariableSync send+receive with an actual broadcast
        object.** Both test instances ran `--nosim`, so `Sim.objectList` was always empty and
        `IsBroadcast`/the shared-cockpit branch never had anything to send — the same practical
        limitation Phase 2 hit trying to trigger a live `Status` exchange, now for the same reason
        (this sandbox has no simulator to attach). Confidence instead comes from: the codec round-trip
        tests (13, covering the exact wire shapes), the live-verified negotiation table (proving
        eligibility checks resolve correctly), and direct code review of the receive-apply logic against
        the exact legacy methods it reuses (`Sim.UpdateObject`/`UpdateAircraft`/`RemoveObjectFromSim`/
        `SetAtcId`). A maintainer with a real simulator attached should fly two patched builds together
        and confirm in the Monitor log (`monitor.network = true`, or watch for the object actually
        appearing correctly) that livery/callsign/variable changes propagate and that the Sessions
        window's Protocol column (see the earlier "sessions window" work) shows `JFP2` for that peer.

## Phase 4 — Migrate Position (design doc §9 step 4) — PositionV1 DONE (scoped) 2026-09-13

- [x] Implemented `PositionV1Codec` for real — `JoinFS/Jfp2/Codecs/PositionCodec.cs`. **Deviation
      (scope):** covers **Aircraft position only** (the legacy `AircraftPosition` message); the generic
      non-Aircraft `ObjectPosition` message is **not** migrated this phase and stays entirely on the
      legacy path (`WriteObjectPositionVelocityMessage`'s one call site, `JoinFS/Sim.cs`, is untouched).
      Reasoning: `AircraftPosition` is the message the design doc's "highest-frequency, shave-the-last-
      millisecond" framing (§2, §6.1) is actually about; non-aircraft scenery/static objects are lower-
      frequency and not latency-critical, and scoping them out keeps this phase's blast radius
      contained to the path that actually matters. Revisit as a follow-on if generic-object position
      ever needs the same treatment.
      **Deviation (fields):** `PositionUpdate` is a real, extended version of the reference's demo
      struct, not a straight port — the reference lacked control-surface axes, `Elevation`,
      `StaticCgToGround`, and the paused/user-controlled distinction entirely. Added: `Rudder`/
      `Elevator`/`Aileron`/`BrakeLeft`/`BrakeRight` (semantic -1..1 floats in memory, wire-encoded as
      fixed-point `int16` exactly like `Sim.ConvertToAxis`/`ConvertFromAxis` — verified byte-exact
      against the legacy encoding), `Elevation`, `StaticCgToGround`, and a `PositionStateFlags` byte
      (`OnGround`/`ElevationCorrection`/`UserControlled`/`Paused`). `ObjectId` is `uint` (not the
      reference's `ushort`), consistent with Phase 3's own `Identity`/`VariableSync` widening. Every
      identity-ish field Phase 3 already split out (livery, ICAO type/airline, registration, class
      code/WTC, callsign, model, typerole) is deliberately **not** repeated here — that's the entire
      point of the Phase 3 split. Fixed size: 103 bytes (111 with the JFP2 envelope) vs. legacy's ~91-
      byte fixed portion *plus* ~50+ bytes of identity strings repeated on every single tick — the
      actual efficiency win is larger in practice than the raw fixed-field number suggests, precisely
      because Identity no longer rides along. Verified with 15 xUnit tests
      (`JoinFS.Tests/Jfp2/PositionCodecTests.cs`): full round-trip, all `PositionStateFlags`
      combinations, the shared-cockpit `uint.MaxValue` sentinel, control-axis quantization tolerance
      (±1/16384) across the full -1..1 range, and registry resolution.
- [x] **Closed a real gap Phase 3 had deferred: Identity-before-Position ordering** (design doc §7.7).
      Now that Position is JFP2-sourced for real, a brand-new object's first Position can no longer
      assume the legacy message already created it. Solved with two additions:
      - **Send side** (`Network.SendJfp2Position`): checks `jfp2IdentitySendState` (Phase 3's own per-
        peer identity-sent cache) before ever sending Position for an object to a peer; if Identity
        hasn't been sent to that peer even once yet, Position is silently withheld for that one tick
        (not a legacy fallback - this peer has already committed to JFP2 for Position) since
        `SendJfp2IdentityIfNeeded` always runs immediately before this in the same per-object/per-peer
        loop, guaranteeing Identity lands first.
      - **Receive side**: `HandleJfp2Identity` (Phase 3) now **caches** an Identity update into a new
        `jfp2PendingIdentity` dictionary instead of silently dropping it when the object doesn't exist
        yet (it previously just returned early - see Phase 3's own notes on this). `HandleJfp2Position`
        consumes that cache to create the `Sim.Aircraft` (via the same `Sim.UpdateAircraft` creation
        overload the legacy path uses) when it sees a `netId` it doesn't recognize yet. If Identity
        genuinely never arrived (packet loss - a real possibility since both messages are unreliable
        UDP), the Position update is dropped and a later tick (after the next ≤4s Identity heartbeat)
        picks it up instead - graceful degradation, not a crash.
      - **Extended `IdentityUpdate`/`IdentityV1Codec` with a `FlightNumber` field**, missing from the
        initial Phase 3 port (an oversight inherited from the reference implementation's own omission)
        but required for `Sim.UpdateAircraft`'s creation overload, which takes `flightNumber` alongside
        callsign/registration. Existing `IdentityCodecTests` extended to cover it; no other Phase 3
        behavior changed.
- [x] Wired all 3 real Aircraft-position send call sites in `JoinFS/Sim.cs`: the shared-cockpit unicast,
      the main per-tick broadcast loop (which already looped per-node for its own interval-mask
      throttling — unchanged, JFP2-vs-legacy is decided *after* the existing throttle check passes, so
      per-peer send cadence is untouched), and the recorder-playback rebroadcast path (`Injected &&
      IsBroadcast`, which — confirmed by re-deriving `Obj.Owner`/`IsBroadcast`'s definitions — can only
      ever be true for `Owner.Recorder`, i.e. this is playback rebroadcast, not a general "relay
      received network positions" path; no such path exists in the legacy codebase either). The last of
      these previously called `LocalNode.Broadcast()`; converted to an explicit per-node loop so each
      peer can resolve independently, wire-identical to the old broadcast for any peer that ends up on
      the legacy branch (same reasoning as Phase 2/3's identical conversions).
- [x] Build: all 6 configurations compile with 0 errors. Full test suite: 106/106 passing.
- [x] Manual verification: re-ran the two-instance loopback negotiation test; both sides resolve
      `Position=1` in `AgreedAppVersion`, zero errors/exceptions. **Not exercised (at the time): an
      actual live Position send+receive with a real broadcasting aircraft** - same `--nosim`
      limitation as every prior phase's manual verification in this sandbox. The ordering fix
      (pending-identity cache, withhold-until-identity-sent) is verified by code review and the
      codec's own unit tests, not by a live object-creation-via-JFP2 test.
      **Update, 2026-09-14 — this gap is now closed, with a real simulator, outside this sandbox:**
      the user built a `CONSOLE` v26.6 client and a v26.6 hub (the hub also upgraded), plus the
      uncommitted `Node.cs` change already sitting in the working tree that drops UDP packets
      addressed to the node's own IP (needed since both client instances and the hub ran on the same
      machine/IP, differentiated only by port). Two v26.6 CONSOLE instances (ports 6112 and 6113) each
      connected to the v26.6 hub; a simulator attached to the session showed all 3 aircraft (the local
      user aircraft plus one injected object per peer), and moving the user aircraft on one instance
      was correctly reflected in the corresponding injected aircraft on the other, confirmed in both
      directions. This is a real, live, end-to-end confirmation of JFP2 Position **and** Identity
      (object creation from the pending-identity cache — an injected aircraft can't appear at all
      without Identity having arrived and been consumed correctly) between two directly-negotiated
      JFP2 peers. **Second run, mixed versions:** the same v26.6 hub with one v26.6 client and one
      unpatched v26.5 (legacy-only) client — position routing between them worked as expected, i.e.
      the v26.6 peer's Hello to the v26.5 peer goes unanswered, `AssumedLegacy` kicks in, and Position
      falls back to the legacy wire format for that pair, exactly as designed (§7.2/§7.6 of the design
      doc) and now confirmed with live traffic rather than negotiation-table inspection alone.
      **Still not exercised live:** VariableSync/Event/Notes/Weather/FlightPlan with real triggering
      data (animation-state variables, a gear-toggle event, a chat message, a METAR reply) - this run
      only put Position and Identity through their paces. See
      `docs/protocol-v2-implementation-review.md` §3 for the updated verification-status summary.
- [ ] `PositionV2Codec` (quantized) — **not implemented.** Per the plan's own instruction ("roll out
      PositionV1 alone first; confirm stability across a real mesh before touching V2") — PositionV1
      has now been confirmed against a real simulator (see the update above), so this is no longer
      blocked on that; still deferred pending broader field experience with V1 first.

## Phase 5 — Remaining message classes (design doc §6.4) — DONE (scoped) 2026-09-13

- [x] `Event` — `JoinFS/Jfp2/Codecs/EventCodec.cs`, `EventV1Codec` (`MessageClasses.Event`, fixed size
      12 bytes: `ObjectId:uint, EventId:uint, Data:uint`). Straight mechanical port of the legacy
      `SimEvent` message — no version gating existed to collapse, `ObjectId` is `uint` per the
      established precedent (never the reference's `ushort`), `uint.MaxValue` remains the shared-
      cockpit sentinel. `Network.HandleSimEvent` was extracted verbatim from the legacy inline
      case-block logic (shared-cockpit branch, recorder-record branch) so both the legacy receive path
      and the new JFP2 receive path call the identical method. `Network.SendEventUpdate` is the usual
      split-send helper (JFP2 if negotiated, `false` otherwise so the caller falls back to legacy); all
      3 real send call sites in `JoinFS/Sim.cs` (shared-cockpit unicast, main broadcast, recorder-relay
      broadcast) converted to it, same per-peer-loop pattern as every prior phase's broadcast
      conversions.
- [x] `FlightPlan` — `JoinFS/Jfp2/Codecs/FlightPlanCodec.cs`, `FlightPlanV1Codec` (13 always-present
      string fields plus `ObjectId:uint`). **Deviation (fields):** collapses the legacy
      `dataVersion>=21003`/`>=21006` conditional-read gates (`Alternate`/`Speed`/`Altitude`/`Callsign`
      at 21003; `Registration`/`IcaoAirline`/`FlightNumber` at 21006) into unconditional fields, same
      simplification already applied to Status/Identity in Phases 2/3 — every current legacy sender
      already writes all 13 unconditionally. **Deviation (framing):** no separate `OwnerNuid` field on
      the wire — owner is implicit from the JFP2 sender, matching the precedent already set by
      Position/VariableSync/Identity; true third-party relay-of-another-peer's-flight-plan is out of
      scope until `Jfp2Bridge` exists (still deferred, see Phase 3). `Network.WriteFlightPlanMessage`
      was extracted from the old `SendFlightPlanMessage` body (prepare-only, no send) so
      `Network.BroadcastFlightPlanUpdate` can prepare the legacy buffer once and then loop per-peer
      (JFP2 `FlightPlanV1Codec` if negotiated, else the unchanged legacy message) without triggering the
      N-fold duplicate broadcast that would result from calling the old always-broadcasts
      `SendFlightPlanMessage` inside a loop; `SendFlightPlanMessage` itself is now a thin
      `WriteFlightPlanMessage`+`Broadcast()` wrapper, behaviorally unchanged for any caller that still
      wants a plain legacy broadcast. Both real call sites — `Sim.cs`'s `flightPlanTimer` periodic
      broadcast and `Forms/MainForm.cs`'s `BroadcastUserFlightPlanNow` — now call
      `BroadcastFlightPlanUpdate` instead. **Preserved-but-unexplained branch:** `Network.HandleFlightPlan`
      keeps the legacy receive handler's odd self-nuid branch (`ownerNuid == localNode.GetLocalNuid()`
      updates `main.sim.userFlightPlan` directly) verbatim even though neither real send call site can
      obviously trigger a node receiving its own broadcast back — kept as a faithful mechanical port
      per the "don't decide silently" rule rather than dropped on an assumption.
- [x] `Notes` — `JoinFS/Jfp2/Codecs/NotesCodec.cs`, `NotesV1Codec`. **Deviation (scope, deliberate):**
      covers only the live single-note push (`Network.SendCommsNoteMessage`'s exact parameter list:
      `Guid, Nickname, Callsign, NoteId, Age, Channel, Text`) — real-time chat traffic, the hot path
      worth optimizing. The bulk historical catch-up exchange (`SessionCommsRequest` +
      `SendSessionCommsMessage`'s full-dump reply) stays **entirely legacy**, not ported at all: it has
      5 different producer methods on a nested repeated-group wire shape with 2 mutually-inconsistent
      `Length`-field formulas (neither provably correct), 3 of those 5 producers have zero call sites
      anywhere in the repo, and it fires at most once per newly-established connection — not hot-path
      traffic JFP2 needs to shave bytes off, and porting it mechanically would mean faithfully
      reproducing dead code and an already-broken length formula. `Network.SendCommsNoteMessage`'s own
      internal `Broadcast()` call was converted to the same split-send-per-peer loop pattern (it, unlike
      FlightPlan, didn't need a separate prepare-only extraction since it has only the one call site).
- [x] `Weather` / `WeatherReply` — **deviation (catalog):** the design doc's message catalog (§4.3)
      reserved one slot ("Weather") for this whole exchange, but the legacy protocol has two distinct
      messages sharing one wire shape (`{ Metar: string }`) with different reliability/receive
      semantics: `WeatherUpdate` (peer-broadcast, applies to a specific peer's aircraft) and
      `WeatherReply` (unicast reply to a request, updates this node's own weather). Rather than overload
      one class with a discriminator, appended a new class 9 (`MessageClasses.WeatherReply`) following
      the same evolution rule already used for `StatusRequest` in Phase 2 — `Weather` (6) now serves
      `WeatherUpdate` only. Both share the `WeatherReport` struct and near-identical codec bodies
      (`JoinFS/Jfp2/Codecs/WeatherCodec.cs`: `WeatherUpdateV1Codec`, `WeatherReplyV1Codec`), kept as two
      classes rather than one because they're independently negotiated — a peer could in principle land
      on JFP2 for one and legacy for the other. **`WeatherRequest` itself is deliberately not ported**
      (send or receive, either partition): a repo-wide search confirms `WriteWeatherRequestMessage` has
      zero call sites anywhere in the current codebase, and the legacy receive handler
      (`case MESSAGE_ID.WeatherRequest`) never reads the request's `NetId` field before replying
      unconditionally from `main.sim.scheduleMetar` — there is no live behavior to mechanically port.
      Only the *reply* half was upgraded (`Network.SendWeatherReply`, called from the still-legacy
      `WeatherRequest` receive case, itself untouched), since a real legacy peer (or a JFP2 peer that
      fell back to legacy for this one unported message) can still trigger it. `Network.SendWeatherUpdate`
      is the ordinary split-send helper for the one real `WeatherUpdate` broadcast call site
      (`Sim.ProcessWeatherObservation`), converted to the same per-peer-loop pattern as every other
      broadcast conversion this phase.
- [x] `Network.HandleJfp2Application`'s switch extended with `Event`/`FlightPlan`/`Notes`/`Weather`/
      `WeatherReply` cases, each decoding via `CodecRegistry.Resolve<T>(...).Decode(...)` inside the
      same try/catch pattern as Phases 2–4's cases, dispatching into the shared handlers described
      above. `LocalNode.LocalJfp2Offers` extended with all 5 new classes at `(1, 1)`.
- [x] Fixed a latent test-infrastructure race exposed (not introduced) by adding 4 more test classes
      that each call `CodecRegistry.Register`: the registry's backing store was a plain `Dictionary`,
      not thread-safe under concurrent writes, and xUnit runs test classes in parallel by default — a
      concurrent `Register` from one class could occasionally corrupt lookups for an unrelated,
      already-registered class (observed as a spurious `KeyNotFoundException` for `Position`/`Identity`
      in an otherwise passing run). Switched `CodecRegistry`'s backing store to `ConcurrentDictionary`
      (`JoinFS/Jfp2/Codecs/ICodec.cs`) — this is also a real production concern, not just a test
      artifact, since more than one `Network` instance can exist in one process (e.g. the loopback test
      harness itself). Confirmed fixed by 3 consecutive clean test runs after the change (0 failures
      each), versus a reproducible intermittent failure before it.
- [x] Build: all 6 configurations compile with 0 errors, 0 warnings (the one `CS0219` warning present in
      `CONSOLE-Debug`/`XPLANE-Debug` builds is pre-existing and unrelated — an unused `minY` local in
      `XPlane.cs`). Full test suite: 121/121 passing (106 prior + 15 new: `EventCodecTests`,
      `FlightPlanCodecTests`, `NotesCodecTests`, `WeatherCodecTests`), confirmed stable across repeated
      runs after the `ConcurrentDictionary` fix above.
- [x] Manual verification: two-instance loopback negotiation test (temporary `nodeDebug` logging in
      `HandleJfp2Hello`/`HandleJfp2HelloAck`, temporary `monitor.network = true` in `Program.cs`, both
      reverted after — clean rebuild confirmed post-revert). Both sides resolved
      `Event=1 FlightPlan=1 Notes=1 Weather=1 WeatherReply=1` in `AgreedAppVersion`, zero errors. **Not
      exercised: an actual live Event/FlightPlan/Notes/Weather send+receive with real sim state** — same
      `--nosim` limitation noted in every prior phase's manual verification in this sandbox (no
      simulator attached means no broadcasting aircraft, no comms notes, no weather observation to
      actually trigger these call sites). Confidence beyond negotiation comes from the codec round-trip
      tests and direct code review of the extracted handlers against the exact legacy logic they
      preserve.

## Field test findings, 2026-09-15

Two field tests run by the maintainer against real MSFS clients and a real hub, after Phases 0-5 above
were complete. Both used a real simulator (unlike every `--nosim` verification logged in the phases
above), so these are the first tests to exercise VariableSync (light-state sync) end-to-end. Logged here
per this file's own "keep this updated" convention; the root-cause analysis is cross-referenced into
`docs/protocol-v2-implementation-review.md` (Finding 6) since it's a code-audit finding, not a design
question — nothing here changes `docs/protocol-v2-design.md`.

**Test 1 — mixed versions, direct mesh.** One v26.5 (legacy-only) and one v26.6 (this tree) instance,
both connected to a v26.6 hub. The v26.6 side correctly detected the hub as JFP2-capable and the v26.5
peer as legacy; the two client instances negotiated a direct JFP2 mesh connection (no hub routing), as
intended. Position data looked correct on both sides. The v26.6 side saw the v26.5 peer's landing/taxi
lights switch on and off, but with an intermittent ~1-2 second delay. The v26.5 (legacy) side reported
seeing the v26.6 peer's lights as always on, never observing them switch off.

**Test 2 — two v26.6 instances via a v26.6 hub, JFP2 mesh.** Both instances correctly negotiated a
direct JFP2 (`jfp2`) connection instead of routing through the hub. Light-state switching was correctly
delivered in both directions, again with an intermittent ~1-2 second delay. Position updates looked
good. The maintainer also noted the JFP2 negotiation itself felt slow to complete, during which the
aircraft list stayed empty (correctly, in the maintainer's assessment, but slower than expected).

**Analysis:**

- **The ~1-2s light-toggle delay has a precise, code-confirmed root cause, logged as Finding 6 in
  `docs/protocol-v2-implementation-review.md`.** Landing/taxi/nav/beacon lights share one underlying
  SimConnect variable (`LIGHT STATES`); `VariableMgr.Set`'s receive-side apply logic enforces a 3-second
  (`SLAVE_DELAY`) hold-off per vuid before a new value may overwrite the cached one, and updates landing
  inside that window are dropped rather than queued. Toggling landing and taxi lights together — what
  both tests did — is close to the worst case for this shared-vuid, drop-not-queue mechanism, producing
  exactly the observed intermittent 0-3s lag. This is pre-existing code, reached identically by the
  legacy and JFP2 receive paths (both terminate in `Sim.UpdateAircraft`'s `VariableMgr.Set.UpdateIntegers`
  call), so it affected Test 1's legacy side and Test 2's all-JFP2 mesh equally — consistent with what
  was observed. See Finding 6 for the full code trace and a possible follow-up (a shorter or absent
  hold-off specifically for boolean/mask-derived variables).
- **JFP2 negotiation's own cost is small; what's actually slow is pre-existing, unrelated mesh
  formation.** `DoJfp2Handshake` only attempts a Hello once a peer is already `Direct` in the legacy
  mesh (`Node.cs`) — JFP2 negotiation cannot start before that. Reaching `Direct` is gated by the
  legacy Pathfinder mechanism (`PATHFINDER_INTERVAL = 5s`, `Node.cs`), which predates JFP2 entirely and
  is unchanged by it; the Hello/HelloAck exchange itself completed in ~46ms in Phase 1's own loopback
  test once started. So "JFP2 negotiation feels slow" in Test 2 is very likely the ~5s-or-more legacy
  direct-path discovery, not JFP2's handshake cost — worth confirming with `GetNodeJfp2State` and
  `node.Direct` timestamps in the next test rather than assumed.
- **The "aircraft list is empty until negotiated" observation may not reflect an actual JFP2 gate.**
  Every per-message send call (`SendVariableUpdate`, the Position broadcast loop) falls back to legacy
  immediately, on every tick, whenever a peer's JFP2 session isn't yet negotiated — nothing in the code
  withholds Position pending negotiation completion (the one exception, a one-tick withhold for a
  brand-new object's first Position pending its own Identity, only applies to *already*-negotiated
  peers and is bounded to ~0.2s). The empty aircraft list is more likely coincident with the same mesh-
  formation/Join timing above than a hard dependency on JFP2 finishing. Recommend logging
  `GetNodeJfp2State` transitions alongside the first-aircraft-visible timestamp next time to separate
  the two.
- **Still open, needs a live re-test with logging:** why the v26.5 peer in Test 1 saw the light as
  "always on" rather than merely delayed. A 3-second hold-off predicts delay, not permanent staleness,
  so this isn't fully explained yet — possible causes include the true state spending much more time ON
  than OFF during the observation window, a specific OFF-carrying datagram being lost at an unlucky
  moment relative to the hold-off window, or a difference in v26.5's exact `VariableMgr.Set` snapshot
  that this review (run against the current tree, not the v26.5 tag) couldn't check. Next test should
  enable `monitor.variables`/`monitor.network` on **both** the sender and the v26.5 receiver
  simultaneously and watch the actual `LIGHT STATES` integer value the receiver decodes.

## Phase 6 — Follow-on, out of scope for this protocol but related

- [ ] Recording format synergy (design doc §8): consider adapting the `ICodec<T>`/`CodecRegistry`
      pattern to `Recorder.cs`'s `Obj.Write`/`Read1`, replacing the `#if FS2024` compile-time gate that
      causes the bug documented in `docs/recording-protocol.md` §7.1. Separate piece of work; do not
      block JFP2 network rollout on it.
- [ ] Variable-name/vuid table sync at scale, selective acknowledgement, coalescing policy tuning —
      see `docs/protocol-v2-design.md` §10 for what's still genuinely open.

## Notes for whoever picks this up next

- If you deviate from anything in `docs/protocol-v2-design.md` or `docs/protocol-v2-architecture.md`
  during implementation (a field doesn't fit, a message needs different framing, the threading
  assumption breaks somewhere), **update those docs**, don't just diverge silently — they're the
  source of truth other sessions (and the human maintainer) will read first.
- Keep legacy `Node.cs`/`Network.cs` read/write logic byte-for-byte untouched outside the one dispatch
  line called out in Phase 1. If a phase seems to require touching legacy serialization code, stop and
  reconsider — that almost certainly means the JFP2 codec should absorb the difference instead.
