# JFP2: implementation

How `protocol.md` is realized in the code, what is built and what is not, how to extend and test it,
and the record of how it got here. How JFP2 fits into the application as one protocol plugin is
`../reference/joinfs-architecture.md` (§5 and §6); this document is about JFP2's own parts.

## 1. Code map

All under `JoinFS/Net/Protocols/Jfp2/`, tests under `JoinFS.Tests/Jfp2/` and `JoinFS.Tests/Net/`.

| Part | File | Spec |
|---|---|---|
| Envelope, flags, class constants, size limits | `Envelope.cs` | §3 |
| Handshake message, offers, TLVs, resolution, capabilities | `Negotiation.cs` | §5 |
| Node names (`NodeName`) | `NodeName.cs` | §4.3 |
| Address and port on the wire | `WireEndPoint.cs` | §4.4 |
| Text with limits | `Codecs/WireText.cs` | §4.2 |
| Guaranteed delivery: pending, acks, duplicates, reassembly | `Jfp2Reliability.cs` | §7 |
| One descriptor per class; the set a plugin speaks | `ClassDescriptor.cs`, `Jfp2Profile.cs` | §3.3, §5.3, §10.3 |
| Codecs, one per class and schema version | `Codecs/*Codec.cs` | §9 |
| The plugin | `Jfp2Plugin.cs` (state, routing), `.Handshake.cs` (handshake, keepalive, occupants), `.Relay.cs` (next hop, relay, translation), `.Codec.cs` (encoder, decode) | §5–§8 |
| Wireshark dissector | `JoinFS/util/wireshark/joinfs.lua` | all |

The plugin runs on the network thread, takes no lock, and reaches the core only through
`IProtocolHost`. `Jfp2Profile` is per instance, so a test can run an older and a newer build side by
side. `ClassDescriptor` holds every fact about a class in one place: the canonical `MessageKind`, its
number (a `MessageClasses` constant), its partition, whether it is guaranteed, one codec per schema
version, and how a decoded message reaches the core. The Hello offers, routing (`CanCarry`),
encoding and decoding all read the profile, and nothing else lists classes.

Names are `NodeName` on the wire and `NodeId` in the core; release 1 converts kind 0 both ways and
drops every other kind. The route of a peer (`Peer.RouteVia`, `RouteIsOwnEndPoint`) lives in the core.

## 2. What is implemented, and what is not

**Implemented (release 1):**
- the envelope with its guaranteed and forwarded extensions, hop-scoped ids on relayed datagrams;
- Hello/HelloAck with per-class negotiation, node names (kind 0), the build advertisement, the
  observed endpoint, and verified sessions with a keepalive;
- dropping of datagrams of another ProtoMajor or with flags the build cannot read;
- next-hop origination of relayed traffic, and relay or translation at the hop (`protocol.md` §8);
- single-datagram guaranteed delivery, with the id rule of §7.2;
- a byte limit for every string of the v1 codecs, which keeps every datagram within 1,200 bytes;
- the v1 codecs for all ten application classes, each class one descriptor of a per-plugin profile;
- the relay budget as a setting (`MeshManager.RelayBudget`, `--hubrelays <n>`, default 10), and a log
  line when a relay is refused ("relay capacity reached", at most once per 5 s).

**Specified or reserved, not implemented** (the reservations are in `protocol.md` §10.2; the sketches
in `design-future.md`): the Coalesced flag; the `Extended` class; name kinds other than 0;
multi-segment guaranteed delivery; capability bits (they are exchanged and agreed, none is assigned);
PositionV2; `WireEndPoint` lists; JFP2 mesh messages.

**Not implemented and not specified here:** everything in `roadmap.md`.

**Deliberate behaviours worth knowing:**
- A payload over 1,100 bytes can only come from a codec bug; it is sent as one datagram and logged.
- A node never hands a guaranteed message to legacy while JFP2 has no hop to the target that agreed
  its version; it waits.
- `Envelope.HandshakeProtoMajor` is a constant of its own that stays `2` even if `Envelope.ProtoMajor`
  moves.

## 3. The application side

### 3.1 Node names and the route

The plugin turns the name in a handshake into a `NodeId` only for kind 0, and looks the node up in
the mesh the legacy plugin built. A Hello whose names resolve to no known peer is ignored.

### 3.2 The observed endpoint

1. **Plugin** (network thread): counts an observation only from a HelloAck that answers its own
   Hello: addressed to the session's id, from the endpoint the Hello went to (`from ==
   ProbeEndPoint`), and naming the node asked for or the endpoint's occupant. It reports changes per
   neighbour through `IProtocolHost.EndPointObserved(reporter, endPoint)`.
2. **Core** raises `NetworkEventKind.EndPointObserved` (`NetworkEvent` already has `Node` and
   `EndPoint`).
3. **App** (`Network` drains the event): `NetBootstrap` hands it to `ObservedEndPoints`
   (`JoinFS/Session/ObservedEndPoints.cs`, plain C#), which keeps the latest observation of up to 8
   reporters, ignores LAN observations (our /24, private ranges), and classifies:

| Class | Evidence |
|---|---|
| `Public` | the observed address is ours and the port is the bound port |
| `PortPreserving` | the port is the bound port |
| `Translated` | another port, the same for every reporter |
| `EndpointDependent` | different reporters see different ports or addresses: the node depends on relays (typical of CGNAT) |
| `Unknown` | no public observation yet (two needed for `EndpointDependent`) |

The class and the observed endpoints are logged at event level when they change, next to the HTTP
address: field data for CGNAT work. Nothing else uses them in release 1 (decision E). The observed
address never replaces the public address (decision C).

### 3.3 Build advertisement

The `Build` extension is remembered per session and logged as `JFP2: <node> runs build <build>`: the
first build learned for a session at event level, later changes at network level. A hub's journal
therefore shows which builds speak JFP2, which is the data the legacy retirement criterion needs.

## 4. Offers in this build

Every application class 0–9 at version range [1, 1]; no internal classes. They come from the
plugin's profile, in its order: Status, StatusRequest, Identity, VariableSync, Position, Event,
FlightPlan, Notes, Weather, WeatherReply. The order is visible in Hello, so a new class is appended
last.

## 5. Adding a class or a schema version

**A new schema version of a class.** Write the codec (`ICodec<T>`, `SchemaVersion` = the next number)
and add it to the class's descriptor in `Jfp2Profile.Default`. Its offer becomes `[min, new]`; peers
agree it only when both sides speak it.

**A new plain class** (its canonical message is what goes on the wire, as Event or Weather). Append
its number to `MessageClasses` (never reuse one), write its codec, and append
`ClassDescriptor.Plain(MessageClasses.X, guaranteed, new XV1Codec())` to `Jfp2Profile.Default`. Append
it last. A plain class whose delivery must add something (FlightPlan: the owner is the sender) passes
a `Delivery<T>` to `Plain`. A class sent its own way (Identity ahead of Position, VariableSync in
chunks, Notes one note per message) is made with `ClassDescriptor.SentByPlugin` and needs a sender in
the plugin's `Encoder`; `Jfp2Plugin` refuses a profile with such a class for a kind it has no sender
for, rather than advertising it and dropping every message.

**Every string field** gets a limit constant in its codec next to the field
(`IdentityV1Codec.CallsignLimit`, ...), and `WireText.WriteString` takes it as a required argument. A
new class also needs: its entry in `protocol.md` §3.3, §9 and §9.7, a limits test
(`LongStrings_AreCutToTheirLimits`), a case in `EveryJfp2DatagramSent_IsAtMost1200Bytes`, and the
dissector.

**Testing version skew.** Each `Jfp2Plugin` takes a profile, so a `TestMesh` can run an older and a
newer build side by side: `Jfp2Profile.Default.Without(MessageClasses.X)` is a build that lacks a
class, `Default.With(descriptor.WithCodecs(v1, v2))` one that speaks another version range with a
test-only codec for a version that does not exist yet, and `Default.WithCapabilities(bits)` one that
advertises other capabilities. `Jfp2VersionSkewTests` does all three, including a relay translating
between versions; `Jfp2WireCharacterizationTests` pins what the default profile sends.

## 6. Tests

```
dotnet test JoinFS.Tests/JoinFS.Tests.csproj -c FS2024-Debug -p:Platform=x64
```

| Kind | Where | What it pins |
|---|---|---|
| Golden bytes | `Jfp2/HandshakeGoldenTests` | Hello, HelloAck, HelloAck with the observed endpoint (`protocol.md` Appendix A). Never regenerate to make a test pass. |
| Envelope, names, endpoints, text | `Jfp2/EnvelopeTests`, `WireEndPointTests`, `WireTextTests` | §3, §4 |
| Negotiation and TLV rules | `Jfp2/NegotiationTests` | §5 |
| Codecs | `Jfp2/*CodecTests` | §9; one `LongStrings_AreCutToTheirLimits` per codec |
| Plugin behaviour | `Net/Jfp2PluginTests` | handshake, refusals, datagram ceiling, unknown flags and names |
| Topologies | `Net/Jfp2RelayTests`, `Jfp2VersionSkewTests` | shared endpoint, relays, translation both ways, restart recovery, steering flips, silence fallback, NAT |
| Observed endpoint, app side | `Session/ObservedEndPointsTests` | the NAT classes |
| Legacy side of the mesh | `Net/LegacyMeshTests` | including the relay budget and CGNAT strangers |

`TestMesh` runs nodes on an in-memory network with a manual clock, `AddBehindNat` and a port-changing
`InMemoryNetwork.Nat` model NATs. `JoinFS/util/fake_xplane_plugin.py` stands in for the X-Plane
plugin. Wire captures from live sessions are in `../captures/`.

Interop with released builds was checked on the wire: an unmodified v26.5 client against the new hub
(`../captures/legacy-v26.5-client-vs-new-hub.pcapng`), and the new hub's Hello giving up after 5
attempts against it and falling back to legacy.

## 7. Record of the work

The full history is in `history/` and in git. This is the short version, in order.

**Before the redesign of the wire (2026-09, 2026-10-07).** JFP2 was built in phases (`history/`
implementation plan), field-tested against MSFS 2024, and audited field by field
(`history/protocol-v2-implementation-review.md`; its Findings 8 and 9 are bugs in released legacy
builds' guaranteed delivery that `LegacyPlugin` fixes, but a released peer's own sends through a
relay still hit them). Then the networking was rewritten as a plugin stack
(`../network-plugin-architecture.md`). Independent reviews led to the "before 26.6" fixes (commit
`0119d21`, branch `jfp2-pre-26.6`): A1 drop datagrams with another ProtoMajor; A2 drop datagrams with
unknown flags, with the sender rule; A3 freeze Hello/HelloAck with a golden test; A4 the `Build`
extension; A5 two misleading comments. And to the evolvability work (commit `6c850d8`, branch
`jfp2-evolvability`): C1 and C2, `ClassDescriptor` and `Jfp2Profile` replacing six places and a static
codec registry (Position encode/decode got faster, about 92 → 69 ns and 52 → 35 ns, still
allocation-free), and C4, a mesh message addressed to another node translated once the node knows its
own id. The first version-skew test found a real bug: a relay that forwarded Identity unchanged never
learned it, so it withheld the translated Positions (fixed in `Jfp2Plugin.Relay`).

**The first-release wire, in stages** (branch `jfp2-node-identity`; each reviewed, committed, all
tests green, six configurations built):

| Stage | What | Tests |
|---|---|---|
| 1 | Decision record and spec: the direction in `../network-plugin-architecture.md` §2.13; the spec's non-goals and last paragraph | docs only |
| 2 | `partial class Jfp2Plugin` across `.Handshake.cs`, `.Relay.cs`, `.Codec.cs`; members move, nothing else changes | all existing, unchanged |
| 3 | Names: `NodeName` with kind-0 conversion; `Names` TLV in place of `Node`; 16-byte Forwarded extension; `RelayNuid` deleted; kind-0-only resolution and the no-ack rule | `HandshakeGoldenTests` constants rewritten (a deliberate wire change); `EnvelopeTests.Forwarded_CarriesTwoEightByteNames`; `Jfp2PluginTests.ForwardedWithAnUnknownNameKind_IsDroppedAndNotAcknowledged`, `HelloWithANameOfAnUnknownKindBesideALegacyName_BindsByTheLegacyName` |
| 4 | Handshake, envelope and TLV rules: unknown `Result`, the partition byte, capabilities and internal classes 2–8 released, TLV length and repetition rules, `GuaranteedDone` always 3 bytes, `Jfp2Profile` gains the capabilities it advertises | `Jfp2PluginTests.HelloAckWithAnUnknownResult_MeansNoSessionNow_NotLegacyOnly`, `OfferOfAnUnknownPartition_IsSkipped`, `TwoByteGuaranteedDone_IsIgnored`; `NegotiationTests.RepeatedTag_TheFirstCopyCounts`, `ShortValue_IsIgnored`, `LongValue_IsReadUpToTheKnownPrefix`, `Names_WithATrailingRemainder_ReadsTheWholeNames` |
| 5 | Guaranteed-id rule: relay dedup and reassembly keyed (origin, final target, id); a re-sending relay keeps the upstream id; a Forwarded `GuaranteedDone` upstream; the pending table keyed (origin, target, id, index), the fallback branch of `Acknowledge` deleted; a dropped segment is never acknowledged | `Jfp2RelayTests`/`Jfp2VersionSkewTests`: `TranslatingRelay_SameIdToTwoTargets_DeliversBoth`, `_AckForOneTarget_DoesNotClearTheOther`, `_KeepsTheOriginsId`, `_DownstreamAck_ClearsTheEntryOfItsOriginAndTarget`; `Jfp2PluginTests.GuaranteedSegmentOfSeveral_IsDroppedWithoutAck` |
| 6 | Field limits and the 1,200-byte ceiling: a limit constant per string field; `WireText` cuts at a UTF-8 character boundary | `WireTextTests.LongText_IsCutAtACharacterBoundary`; `LongStrings_AreCutToTheirLimits` per codec; `Jfp2PluginTests.EveryJfp2DatagramSent_IsAtMost1200Bytes`, `_IsOneEveryJfp2BuildCanRead` |
| 7 | Observed endpoint: `WireEndPoint` replaces `PeerKey`; the `ObservedEndPoint` TLV; `IProtocolHost.EndPointObserved`; `NetworkEventKind.EndPointObserved`; `ObservedEndPoints`; the log line | `WireEndPointTests`; `HandshakeGoldenTests` (HelloAck with the TLV); `Jfp2PluginTests.HelloAck_TellsTheAskerItsSourceEndPoint`, `ObservationFromAnotherEndPoint_IsIgnored`; `Jfp2RelayTests.BehindNat_TheObservedEndPointIsTheMappedOne`; `ObservedEndPointsTests` |
| 8 | D2, false same-LAN detection (no wire change): `MeshManager.RegisterNode` keeps the address a peer was actually heard from (`RegisterSender`), where `MakeEndPoint` guessed `<our /24>.<their octet>` for any peer behind our public IP | `LegacyMeshTests.StrangersBehindOnePublicIp_AreReachedAtTheirSource` |
| 9 | D1, hub relay budget (no wire change): `MeshManager.MaxRoutingNodes` (10) became `RelayBudget`, set by `--hubrelays <n>`; refusals are logged | `LegacyMeshTests.RelayBudget_IsTheConfiguredOne` |

**Journal finding of stage 9** (2026-10-08, `joinfs-8` on the minion hub, 16,976 lines since
2026-07-19): no "relay" line at all. That proves nothing: the legacy path never logged a refusal
(only JFP2 did), and the hub is mostly joined directly. `TryAcquireRelay` now logs it, so the next
journals can say.

**Branch state:** the wire is complete for release 1 and 26.6 may ship. The remaining stages are in
`roadmap.md`.
