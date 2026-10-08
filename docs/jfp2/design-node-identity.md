# JFP2: node identity in the core, and the node key

The designs of stages 10 and 11 (`roadmap.md`). They are not wire (the wire already carries 8-byte
names, `protocol.md` §4.3); they decide how those names map onto the core's ids and where the
random key comes from. Both follow the release. They are approved designs, not yet built.

## 1. Identity in the core (stage 10, a pure refactor)

### 1.1 Today

`NodeId` (`Net/Core/NodeId.cs`) is public IPv4, **bound** port and LAN octet. It occurs about 415
times on 378 lines in 51 files:

| Use | Where |
|---|---|
| Key of core state (15 files in `Net/`) | `PeerDirectory`, `MessageMeta.Sender/Recipient`, `NetworkEvent.Node`, `ObjectStateCache`, `MeshManager`, both plugins, `NetworkSnapshot` |
| Opaque handle (about 30 app files) | `PeerTable.Nodes`, `Sim` owners (`ownerNuid`), shared cockpit, `SimIngest`, forms; `default` = "this node" (`PeerTable.cs`) or the recorder (`Recorder.cs` ff.) |
| An address (about 10 files) | `LocalIdentity.MakeEndPoint` and its copy `NetworkSnapshot.MakeEndPoint`; `MeshManager.RegisterNode`; `LegacyPlugin.cs` (endpoint of a target not in the directory); `HubDirectory` (`HubCount_IP` on `.ip`, `MakeEndPoint` of HubList entries, `hubs.dat`); `UserDirectory`; `LocalId.port` (`SessionForm`, `Program.cs`); `XPlaneLink.cs` (plugin framing); `NodeId.SameDevice` for `MaxNodesPerDevice` (`MeshManager.cs`, `PeerDirectory.cs`) |

Persistent app state never stores a `NodeId` as identity (`Log` keeps the peer's `Guid` and IPv4
address, `Log.cs`); `hubs.dat` stores one as an address.

It fails as an identity: it changes with the public address (`LocalIdentity.cs`; hubs refresh
it every 3 h, `NetBootstrap.cs`), it cannot exist before the public IPv4 is known
(`Network.cs`), two CGNAT users with port 6112 and the same LAN octet share one, and IPv6-only
nodes have none.

### 1.2 Options

| | Option | Verdict |
|---|---|---|
| a | Opaque key everywhere; re-key a peer when its Hello names a key | Rejected. A re-key must move core and app state (the sim thread's objects included) at one instant while messages under the old id are queued, and an unauthenticated Hello would trigger it. |
| b | `NodeId` becomes the 8-byte name; a peer's id is the name it was first learned by; `PeerDirectory` keeps an alias index from every name of a peer to its id | **Chosen.** Nothing is ever re-keyed; the app keeps treating ids as opaque; when legacy retires every id is a key, which is (a) without the re-keying. |
| c | Legacy `NodeId` stays; keys live only in the JFP2 plugin | Only as an interim. It cannot represent a node without a legacy id. |

### 1.3 The migration

1. `NodeId` holds the 8 bytes of a name. `NodeId.FromLegacy(ip, port, local)` and
   `TryGetLegacy(out LegacyNodeId)` convert kind 0 both ways without a table.
2. The old 7-byte struct becomes `LegacyNodeId`, used only by `LegacyPlugin`, `LocalIdentity`,
   `XPlaneLink` (pinned by `XPlaneLinkTests`) and `hubs.dat`.
3. `Valid()`: not all zero, and for kind 0 a non-zero ip (today's meaning). `ToString()`: kind 0 as
   today; kind 1 as `k` and 14 hex digits; other kinds as the kind number and hex.
4. The address uses read the legacy id: `MakeEndPoint(LegacyNodeId, port)`; `LegacyPlugin.cs` and
   `HubDirectory.cs` skip a node without one; `HubCount_IP` counts per `hub.endPoint` address;
   `SessionForm` and `Program.cs` read `Snapshot.Port`. `SameDevice` compares legacy ids (ip and
   octet); a node without a legacy id is its own device.
5. `PeerDirectory` gets the alias index (name → id), filled from handshakes; `LegacyPlugin` resolves
   every id it decodes through it and writes a peer's legacy id when encoding, leaving a peer without
   one out of every legacy message.
6. `NodeName` (the wire struct) merges into `NodeId`: the envelope's names and the plugin's lookups
   use `NodeId` directly.

Every existing test passes unchanged, because every member still has a legacy id and so a kind-0 id.
New tests: `NodeIdTests` (kind 0 round trip, `Valid`, text) and `PeerDirectoryAliasTests`.

### 1.4 What it does not fix

A peer learned through legacy keeps its kind-0 id, and legacy cannot tell that a node whose public IP
changed is the same node: in mixed sessions it reappears as a new peer, as today (decision D). Two
CGNAT users with one legacy id stay one confused peer to legacy; JFP2 can only detect it (§2.4).

## 2. The node key (stage 11)

The key is only `Names` content; it ships after the core migration, so that it is an identity from
the day it exists (decision A).

### 2.1 Generation and size

56 bits from `RandomNumberGenerator`, not all zero, as a kind-1 name. With 400,000 keys in existence
(100,000 installs, 4 instances each) the chance of any collision is about 10⁻⁶; collisions are
detected anyway (§2.4).

### 2.2 Store

`NodeKeyStore` (`JoinFS/Session/NodeKeyStore.cs`, app thread, created by `Main` after `storagePath`).
`storagePath` (`%LOCALAPPDATA%\<name>`, `Program.cs`) is per variant; instances of one variant
share it, and share the per-install `Guid` too (`Program.cs`).

- **Slots** `node-<slot>.key`, 0–3 (`MAX_INSTANCES` = 4, `Program.cs`). An instance opens a slot
  with `FileShare.None` and keeps it open while it runs.
- **Which slot:** the one whose file records this instance's bound UDP port, if it is free; otherwise
  the first free one, which then records the port. Taking slots in start order alone would swap keys
  between a hub on 6112 and a client on 6113 whenever they start in another order.
- **File:** version u8 (`1`), key 8 bytes, port u16 LE. An unreadable file gets a new key.
- **No slot free** (more instances than slots, read-only folder): a key for this run only, logged.
  `MAX_INSTANCES` is not enforced for `dotnet JoinFS-CONSOLE.dll`: the process name counted at
  `Program.cs` is `dotnet`.
- **No machine check.** A machine-name hash would change with every Docker container and would not
  tell cloned VMs apart. A copied folder keeps its key; the duplicate is detected (§2.4).
- **Locks:** .NET implements `FileShare.None` on Linux with advisory `flock`, which works between
  JoinFS processes on a local file system. It is unreliable on NFS and SMB shares and through Docker
  Desktop bind mounts, and `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` turns it off: two instances may then
  share a slot and a key, which §2.4 logs.
- **Docker:** the CONSOLE image has no volume for the storage folder today, so a recreated container
  would get a new key. Stage 11 adds a `VOLUME` line for the storage folder to the `Dockerfile`
  (decision F). The README's Docker section shows a **named** volume, since the `VOLUME` line alone
  creates an anonymous volume that `docker rm` and a new `docker run` do not reuse:
  `-v joinfs:/root/.local/share` (the storage folder is `LocalApplicationData`, which .NET maps to
  `$HOME/.local/share` on Linux; adjust if the image runs as another user). The minion hub's systemd
  service keeps its home folder.

`NetBootstrap` takes the key and posts it with the addresses; the network thread's `LocalIdentity`
gets `Key`. `TestNode` gives every node a deterministic key.

### 2.3 Privacy and spoofing

The key is stable on purpose. It links a node across sessions and networks no more than the
per-install `Guid` that every peer already receives (`PeerInfo.Guid`, `StatusUpdate.Guid`). Anyone
can claim any key; B7's kind 2 adds a key pair (`design-future.md` §6). Until then a key is bound
only by a HelloAck that answers our own Hello at the endpoint we asked, never by a Hello alone.

### 2.4 Binding in the JFP2 plugin before B3

A CGNAT legacy-id collision (two strangers with one legacy id) cannot be fixed before B3: legacy's
Pulse and PulseResponse keep moving the shared peer's port between the two nodes
(`MeshManager.cs`, `RegisterSender`), so the JFP2 session keeps being demoted as "route moved"
(`Jfp2Plugin.Handshake.cs`). Any rule that binds JFP2 sessions by key would flap with it. So until B3
the plugin only **detects and logs**:
- the key is recorded or replaced only from a HelloAck that answers our Hello (matched id, the node
  asked for, `from` equal to the probed endpoint);
- a different key on a session that is unverified or lapsed is a restart: replaced, logged at network
  level;
- a different key on a verified, fresh session is a collision: logged once per pair at event level,
  with both keys and endpoints, and nothing else changes;
- the same key on two legacy ids (a clone, an IP change, or a shared slot): logged.

A node never changes its own key because another node shows it: that is trivially spoofable (B7).

### 2.5 Tests (stage 11)

`Session/NodeKeyStoreTests` (temporary folder): `SameSlot_ReopensTheSameKey`,
`SlotRecordingThisPort_IsPreferred`, `AllSlotsHeld_GivesAKeyForThisRunOnly`, `CorruptFile_IsReplaced`.
`Jfp2PluginTests`: `Handshake_CarriesTheKeyBesideTheLegacyName`. `Jfp2RelayTests`:
`CgnatStrangersWithOneLegacyId_TheCollisionIsLoggedAndNothingFlaps` (`AddBehindNat("10.0.0.5",
"203.0.113.9")` and `AddBehindNat("10.0.1.5", "203.0.113.9")`: one legacy id; `Nat` maps them to
different public ports), `RestartWithANewKey_ReplacesTheKey`.
