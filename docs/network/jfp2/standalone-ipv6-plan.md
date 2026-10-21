# JFP2 stand-alone and IPv6: the plan

Written 2026-10-21. For the project owner, who takes the decisions of §4, and for the implementing
sessions, which build the stages of §6 one at a time under the rules of §6.2 and §7. The decisions
were taken on 2026-10-21 (D1 b, D2 a, D3 a, D4 a, D5 b, D6 a). What is built is in §6.1, nowhere
else. A session starts from the prompt in §9.

It restarts the planning from the owner's goals (§1) and from the released wire (26.6), not from the
two earlier attempts: branch `ipv6` (IPv6 for dual-stack nodes with membership left on legacy) and
branch `ipv6-2` (a third protocol, JFP3). §8 says what each branch contributes. Where this plan
contradicts a decision recorded on either branch, this plan wins.

## 0. How to read it

- **Owner:** §1 (check the goals are yours), §2 (the scenarios: this is the shared picture), §3
  (whether JFP2 can carry this), §4 (decide). Then skim §5 and §6.
- **Implementing session:** §6.1 first (which stage is yours and whether it is already under way),
  then §1, §2.1, §5, §6.2, §7, and the row of your stage in §6. Read only the specification
  sections your row cites. Do not read the branches' design documents unless the row sends you there
  for a cherry-pick.

## 1. Goals

In the owner's words, 2026-10-21. These bind every decision and stage below.

1. **Same functionality over IPv6 as over IPv4.** Everything a pilot can do over IPv4 today (create
   a session; join by address, address book, hub list or online user; fly; chat; flight plans;
   weather; see and be seen in the hub list; bans; recording) works the same over IPv6.
2. **IPv6 is preferred** whenever both ends have both families.
3. **IPv4-only and IPv6-only instances fly together.** Dual-stack instances relay between the
   families. A hub or a GUI instance, it does not matter which.
4. **JFP2 becomes stand-alone.** A node creates, joins, stays in and leaves a session, uses the hub
   directory and is relayed with no legacy datagram involved. JFP2-only nodes become possible, so
   legacy can be sunset when the owner chooses.
5. **No new protocol.** 26.6's JFP2 is the base and grows within the rules it published
   (`protocol.md` §5.8, §10, §12).

**Non-goals of this effort:** encryption and authentication (unless D1 says otherwise); anything
above the wire (simulator, model matching); the X-Plane plugin link (stays legacy framing on
loopback); new reliability semantics (JFP2 keeps unreliable and guaranteed).

**The three populations** every rule has to live with:

| Name | Builds | What it speaks | Addresses |
|---|---|---|---|
| **L** | up to 26.5 | legacy only; drops every datagram that does not start `0B 52` | IPv4 only |
| **R1** | 26.6 | legacy for membership, JFP2 for application classes 0–9; ignores a Hello from a node legacy did not register; resolves only kind-0 names | IPv4 only |
| **N** | this work | legacy to L and R1; full JFP2 to N | IPv4, IPv6 or both |

## 2. The shared picture

Every later decision and stage refers to the words of §2.1 and the scenarios of §2.3. When a
session and the owner disagree, the first thing to check is whether they mean the same scenario.

### 2.1 Words

- **Node**: one running JoinFS instance. **Member**: a node that is in the session. **Hub**: a node
  with the hub setting on; it also publishes the directory. Any member may relay, hub or not.
- **Name**: the 8-byte identifier of a node on the JFP2 wire (`protocol.md` §4.3). Kind 0 is the
  legacy id (IPv4, port, LAN octet); kind 2 is derived from a key. A node can have several names
  (**aliases**) and is one node whichever name it is called by. A **synthetic alias** is a kind-0
  name made up for a node that has no IPv4 of its own, so that L and R1 can address it.
- **Link**: what two neighbours have between them: a verified JFP2 session, or a legacy pairing.
  The protocol is a property of the link, never of the node (two nodes may have a JFP2 link while
  each has legacy links to others).
- **Path**: the endpoint, and so the family, a link currently uses. **Candidate**: an endpoint a
  node can be tried at (its public IPv4, its LAN IPv4, its global IPv6 addresses).
- **Direct**: a link to the node itself. **Relayed**: through a member that forwards.
- **Relay**: a member forwarding datagrams between two others, within its relay budget.
  **Bridge**: a relay whose two sides use different protocols or families, so it translates.
- **Admitting node**: the member that answered a node's Join. It is the node's first neighbour and,
  for a node with no IPv4, the one that makes its synthetic alias.
- **Dual-stack**: has a usable IPv4 and a global IPv6. **IPv4-only**, **IPv6-only**: has one
  family. **CGNAT**: has IPv4, but shared with other customers, so nobody can reach it inbound.

### 2.2 The cast

| Who | Build | Addresses | Stands for |
|---|---|---|---|
| **H** | N, hub | dual-stack, public IPv4 and global IPv6 | the test hub on minion once it runs this work; later, public hubs |
| **O** | R1 or L, hub | IPv4 only | every public hub today |
| **A** | N | dual-stack, home router: IPv4 behind NAT, IPv6 global with a firewall that blocks unsolicited inbound | the owner's PC |
| **B** | N | IPv4 only (ISP without IPv6, or the IPv6 setting off) | many pilots; also the owner's second instance for tests |
| **C** | N | IPv6 only, no IPv4 route at all | rare in homes; the case that forces JFP2 to stand alone; also an instance started with the test switch that disables IPv4 |
| **D** | N | IPv6 global plus CGNAT IPv4 (DS-Lite) | the common "bad IPv4" pilot: outbound IPv4 works, inbound never |
| **W** | L or R1 | IPv4 only | the Wilga pilot, and everyone who has not upgraded |

### 2.3 The scenarios

Each scenario says what happens today (26.6) and what this work must make happen. "Direct" and
"relayed" are what the session window shows.

**S1. A joins H by hostname.** The hostname has an A and an AAAA record.
Today: the DNS lookup keeps the first address only; if that is the AAAA record the join fails (JFP2
roadmap D3); otherwise A joins over IPv4 legacy and upgrades the application classes to JFP2.
Expected: A reaches H over IPv6 with JFP2 and nothing else; no legacy datagram between A and H. If
IPv6 to H is broken, the join still succeeds over IPv4 within a Happy Eyeballs delay.

**S2. A joins O by hostname.** O is a released hub.
Today: legacy IPv4 join, JFP2 for the application classes.
Expected: exactly the same, after whatever the JFP2-first attempt costs (D4). O never sees anything
it does not understand.

**S3. A and D are in H's session.** D is behind CGNAT.
Today: A cannot reach D's IPv4 and D cannot be reached by anyone; H relays everything between them
and spends one slot of its relay budget of 10 on it.
Expected: A and D have a direct IPv6 link. H relays nothing for them.

**S4. A and B are in H's session.** B has no IPv6.
Today: IPv4, legacy membership, JFP2 application classes, direct if the NATs allow, else relayed.
Expected: IPv4 as today, but the link is JFP2 throughout (membership included). Nothing visible
changes for B.

**S5. C and W are in H's session.** C has no IPv4; W is a released build.
Today: C cannot run JoinFS at all (the socket is IPv4).
Expected (goal 3): W sees C's aircraft and C sees W's. W's datagrams for C go to H over IPv4 legacy;
H carries them to C over IPv6 JFP2. C's go to H over IPv6 JFP2; H carries them to W over IPv4
legacy. W's session window shows C as a relayed peer; neither pilot does anything special. Branch
`ipv6-2` made C invisible to W (its decision 33); the owner rejected that on 2026-10-19.

**S6. C and A are in H's session.** Both have IPv6.
Expected: a direct IPv6 JFP2 link, whether A joined over IPv4 or IPv6. If A's router firewall blocks
C's first datagram, the fact that both try each other at once (as legacy's Pulse does today) opens
it.

**S7. C and B are in H's session.** No common family.
Expected: a dual-stack member relays. H if C or B joined through it; otherwise any dual-stack
member within its relay budget, for example A. The relay is found the way legacy's Pathfinder finds
one today, over JFP2.

**S8. A's IPv6 breaks mid-flight.** The ISP drops IPv6 for a minute.
Expected: A's links to H and to other dual-stack members go silent on IPv6 and move to IPv4 within
the JFP2 keepalive timeout (15 s); no aircraft disappears for longer than that. A tries IPv6 again
later, and a path never flips back and forth because datagrams happen to arrive on both families.

**S9. C opens the hub list.** The directory today is legacy messages to hub endpoints.
Expected: C sees every hub that has an IPv6 address, with its users and status, and can join it. A
hub that has only IPv4 is shown as unreachable for C, not hidden (so C knows why a friend's hub is
not there). H appears in O's hub list by its IPv4 as today.

**S10. C joins a password-protected session.**
Expected: the password is checked over JFP2 and C is refused or admitted exactly as a legacy joiner
would be.

**S11. The owner's evening test on one PC.** A on port 6112, a second instance on 6113 started as
IPv4-only (B) or as IPv6-only (C), the hub on minion.
Expected: both instances keep their own identity across restarts; the session window shows the
family of each link; the log says for each link which family was tried, which answered and why it
was chosen.

**S12. H runs as a JFP2-only hub.** The owner turns legacy off on a hub.
Expected: W cannot join it and does not see it in the hub list. A, B, C and D join over JFP2 alone.
This is how legacy is sunset: hub by hub, when the owner decides, not on a date.

**S13. W is on H's LAN.** W and H share one public IPv4.
Expected: as today for H itself. For C's synthetic alias (S5) W's released build will guess a LAN
address from the alias, get no answer, and fall back to routing through H via Pathfinder, which is
where the traffic has to go anyway. The stage that builds S5 must have this case in its test.

**S14. The admitting node leaves.** C joined through A (a GUI instance); A leaves; H and W remain.
Expected: C stays a member through its remaining links. W loses C unless another dual-stack member
reaches C and answers W's Pathfinder for C's alias; the alias itself still names A's IPv4, so W's
direct attempts go nowhere. This is accepted: W would have lost C through any relay that left. If
it turns out to matter in the field, the alias can be re-issued by a new admitting node later.

## 3. Can JFP2 be rescued? Yes

26.6 published JFP2 with rules for later editions (`protocol.md` §5.8, §10.1, §12.1). What
stand-alone JFP2 with IPv6 needs, against those rules:

| Need | Rule of 26.6 that applies | Fits? |
|---|---|---|
| Start a conversation with a node legacy did not register | R1 ignores a Hello from a stranger (§5.7) and never answers `Result` 2. A later edition may answer: `Result` 2 is reserved for exactly that ("not admitted: no session was created"), and R1 reads any non-zero result as "no session now" (§5.2). | Yes. N answers a stranger with `Result` 2 and a cookie in a new TLV; R1 just does not. Sketched in `design-future.md` §3. |
| Join, member lists, announce, leave, routes, over JFP2 | New internal classes are offered with versions (§5.3); R1 never offers them and so never receives them (§5.5). Internal classes 2–8 and 10–254 are free. | Yes. |
| Liveness | The keepalive Hello every 5 s with a 15 s timeout already exists (§6.2). | Yes; no Pulse needed on JFP2 links. |
| IPv6 endpoints | `WireEndPoint` already has family 6 (§4.4); names are opaque, so nothing in the envelope is IPv4-shaped. | Yes. |
| A node without an IPv4 identity | Name kinds 1 and 2 are reserved; R1 skips names it cannot resolve (§4.3). | Yes, with one rule: a node also states a kind-0 name whenever it has one, so R1 and L can address it. |
| Relaying between R1 and N | The Forwarded extension carries origin and target names (§3.5); a relay forwards byte for byte between two JFP2 sides (§8.3). | Yes. An R1 pilot and an IPv6-only pilot exchange JFP2 datagrams through H with no translation at all (S5's JFP2 side). |
| Large messages (member lists, notes history) | Segments exist but R1 never acknowledges one of several (§7.5); capabilities gate new behaviour (§5.6). | Yes, behind a capability bit between N nodes; lists that fit are split across messages instead. |
| Directory (hub list, status, online users) over JFP2 | New application classes 10–254 with offers; the 1,200-byte limit. | Yes, over short-lived sessions admitted with the cookie. |
| Envelope changes if ever needed | A later ProtoMajor is negotiated inside the ProtoMajor-2 Hello (§5.8). | Yes, but this effort needs none: the 8-byte header and 16-bit session ids are enough. |
| A handshake that is confidential from the first byte | Hello and HelloAck are plaintext and permanent (§5.8). | **No.** Per-datagram protection can be added later with a flag and a capability (§11.3), and a signature over the handshake can go in a TLV, but the Hello itself can never be hidden. |

So: JFP2 as released can be lifted to a stand-alone, IPv6-capable protocol within its own rules.
The one thing it can never offer is an encrypted handshake. Branch `ipv6-2` chose a new protocol
because it put "encrypted from the first packet" first (its `goals.md` §1 and decisions 1, 4); it
read the permanent Hello as a cage, where 26.6 meant it as the one fixed door through which
everything else is negotiated. If the owner wants encryption now, JFP3 is the honest answer and
`ipv6-2` is a head start; if the owner wants goals 1–5, JFP2 is enough and D1 settles it.

**Why the two attempts missed the goals**, in one paragraph each, so the same thing does not happen
again:

- `ipv6` read "IPv6" as "dual-stack nodes get IPv6 paths" and left membership on legacy. That
  cannot give C anything (no IPv4, no legacy id, no membership), so goals 3 and 4 were out of its
  scope by construction, and it carried legacy's bytes over IPv6 to get there, which is a detour
  from goal 4.
- `ipv6-2` read "stand-alone and extensible" as "a new, secure protocol" and, under that reading,
  took decisions the owner's goals contradict: a third protocol (goal 5), IPv6-only pilots invisible
  to legacy pilots (goal 3; decision 33, half-reversed in the field on 2026-10-19), security as the
  first goal (not in §1).

## 4. Decisions for the owner

Each decision: the question, the scenarios where it shows, the options with what a pilot would see,
a recommendation. Write the choice on the "Decision:" line; an implementing session treats a blank
line as "stop and ask".

### D1. Is security part of this effort?

**Where it shows.** S10: today's session password is a 32-bit hash sent in the clear in legacy's
Join; anyone who captures it can join. Every scenario: positions and chat are readable on the path.
Over legacy anyone can claim any node's identity, and legacy stays in every mixed session until it
is sunset, so whatever JFP2 adds protects only N-to-N links.

| Option | What it means | Consequence |
|---|---|---|
| **a. None** | JFP2 Join carries the same 32-bit hash legacy does. | Parity with today. Smallest work. Security is a separate later effort, which this design leaves room for: kind-2 names, a capability bit and a flag for per-datagram protection, as `protocol.md` §11.3 reserves. |
| **b. Only the join proof** | JFP2 Join sends an HMAC of the password hash over the cookie, never the hash itself. | A captured JFP2 join cannot be replayed. A few lines, platform HMAC-SHA256, no library. Legacy joins stay as they are. |
| **c. Encrypted and authenticated links** | What `ipv6-2` built: Noise handshake, AES-GCM per hop, key-pair identity. | Not possible within JFP2's permanent Hello (§3). This means JFP3: keep branch `ipv6-2`, fix its decisions that contradict §1 (33, and its reading of goal 3), and drop this plan's §5–§6. |

**Recommendation:** b. It costs nothing, and it keeps this effort about IPv6 and standing alone.

**Decision:** __b. Only the join proof______

### D2. How do L and R1 pilots see an IPv6-only pilot?

**Where it shows.** S5, S13, S14. W's build can only address a node by a 7-byte IPv4 id and can
only send to IPv4.

| Option | What it means | Consequence |
|---|---|---|
| **a. Synthetic alias** | C's admitting node (H in S5) gives C a kind-0 name built from the admitting node's own public IPv4, a port and a LAN-octet value it is not using. It announces C to L and R1 members under that name (legacy AddNode) and to N members under C's key name with the alias beside it. C uses the alias as its origin name toward L and R1 peers. W's datagrams for the alias land on H (the address is H's); H answers legacy's Pulse and Pathfinder in C's name and carries the rest to C over JFP2. | W sees C relayed through H, exactly as W sees a CGNAT pilot today. Costs one relay slot at H per L/R1 pilot talking to C. The address is H's own, so no stray UDP goes to third parties (a made-up address such as 240.0.0.0/4 would, and released builds cannot even send to it). Corner cases: S13 and S14. |
| **b. Invisible** | C has no kind-0 name; L and R1 never hear of it. | Goal 3 fails for the one population that most needs it. This was `ipv6-2`'s decision 33. |

**Recommendation:** a. It is the owner's requirement of 2026-10-19 made concrete.

**Decision:** __a. Synthetic alias______

### D3. What is a node's own name when it is not an IPv4 address?

**Where it shows.** S11 (identity across restarts and between two instances on one PC), S14 (C is
one node whichever relay it uses), a Docker hub that is recreated, and the later security effort.

| Option | What it means | Consequence |
|---|---|---|
| **a. Kind 2, from a P-256 key pair generated now** | Each instance slot stores a key pair (as `ipv6-2`'s key store does); the name is the first 7 bytes of the SHA-256 of the public key. The key signs nothing yet. | One identity for good: the later security effort uses the same key and nobody is renamed. `P256KeyPair` and the slot store exist on `ipv6-2` and are platform crypto only. |
| **b. Kind 1, a random 56-bit key** | What `design-node-identity.md` §2 planned. | Equally good for this effort; a later security effort renames every node once (to kind 2). |

**Recommendation:** a. Same work, one migration fewer.

**Decision:** __a______

### D4. When a node joins an address, does it try JFP2 first or both protocols at once?

**Where it shows.** S1 and S2: the joiner does not know whether the address is H or O. S10.

| Option | What it means | Consequence |
|---|---|---|
| **a. JFP2 first, legacy after 2 s of silence** | Hello (with the cookie exchange) to every candidate, IPv6 first; if no HelloAck in 2 s, the legacy Join goes to the IPv4 address. The result for that address is remembered (address book, hub list), so the next join to a legacy-only hub is immediate. | Joining H: fast, never a legacy datagram. Joining O for the first time: 2 s slower than today, then as today. One membership path at a time, so no duplicate members to reconcile. |
| **b. Both at once** | Legacy Join and JFP2 Hello leave together; whichever answers first wins. | No delay anywhere, but H would admit the joiner over legacy (one round trip) before JFP2 (two) finished, so the membership would be legacy's and IPv6 only a path upgrade, which is branch `ipv6`'s model, not goal 4. Making H hold the legacy Join while JFP2 runs is the same 2 s moved to the hub, plus reconciliation. |

**Recommendation:** a.

**Decision:** __a______

### D5. Login sessions (email and password) over JFP2?

**Where it shows.** A hub set to "login required" checks an email and password through
`CredentialStore`. Over JFP2 this needs the Login and LoginFail messages ported (`protocol.md` has
none). `ipv6-2` dropped the feature for JFP3 (its decision 17).

| Option | Consequence |
|---|---|
| **a. Port them** | Parity (goal 1). A small stage. |
| **b. Drop them for JFP2** | A "login required" hub accepts JFP2 joins without a login, or refuses them; either way C cannot use such a hub. |

**Recommendation:** a if any hub the owner cares about uses login; otherwise b, recorded as a known
gap in `roadmap.md`.

**Decision:** __b______

### D6. Which branch, and which build does the evening test run?

Today the daily field-test build comes from `ipv6-2` (JFP3 on). This plan's work starts from
`main` and takes the parts of both branches listed in §8. From the first stage on, the evening
build should come from the new branch, so that every stage is field-tested the day it lands; that
means JFP3 leaves the evening test.

| Option | Consequence |
|---|---|
| **a. New branch from `main`; evening builds from it** | Each stage field-tested; `ipv6-2` stays as a source of cherry-picks and as the JFP3 option if D1 ever becomes c. |
| **b. Keep testing `ipv6-2` until the new branch reaches IPv6 (stage 7)** | JFP3 keeps getting field data nobody will use if D1 is a or b; the new branch's first six stages go untested in the field. |

**Recommendation:** a, with the first commit on the new branch being the documentation move
(§8, the `aecbd6b` cherry-pick) so that the local `CLAUDE.md`, which already describes the moved
layout, is right again.

**Decision:** __a______

## 5. The design

One page, following the decisions of §4. Each stage writes the exact wire of its part into
`protocol.md` before coding (rule in §7); this section says what exists and why, not the bytes.

### 5.1 Identity and names

- `NodeId` becomes the 8-byte name (`design-node-identity.md` §1, built as `ipv6-2`'s stage 1).
  `PeerDirectory` keeps an alias index: every name of a peer maps to the one peer.
- Every node has a key name (kind 2, D3), stored per instance slot, stable across restarts.
- A node that has an IPv4 also has its kind-0 name, as today, and states both in every Hello,
  kind 0 first while legacy is spoken anywhere (so R1 resolves it). A node with no IPv4 states its
  key name, and the synthetic alias its admitting node gave it (D2).
- Legacy codecs are handed kind-0 names only. A peer with none is left out of every legacy message,
  and its traffic to L/R1 goes through the admitting node (5.5).

### 5.2 Admission

- An N node in a session, or a hub, answers a stranger's Hello with `Result` 2, no offers, the
  observed endpoint and a 16-byte stateless cookie (HMAC over the source endpoint and the asker's
  names with a secret rotated every 60 s; `design-future.md` §3). No state, rate-limited, never
  larger than the Hello.
- A Hello carrying a valid cookie creates a **non-member session**: `CanCarry` false, never a next
  hop, limited in number and lifetime (60 s unless a Join succeeds). It carries Join, the directory
  classes (5.6) and nothing else. On a successful Join it becomes the ordinary member session.
- R1 never sends `Result` 2 and never answers a stranger; the asker falls back to legacy (D4).

### 5.3 Join and membership over JFP2

- New internal classes, offered with versions: `Join` (session choice, password proof per D1,
  the joiner's names and candidates), `JoinResult` (accepted or why not; the joiner's synthetic
  alias if it has no IPv4), `MemberList` (names, aliases and candidates of every member, split
  across datagrams), `MemberAnnounce`, `MemberLeave`, `RouteQuery`/`RouteAnswer` (5.5).
- **Login sessions are not ported (D5 b).** A JFP2 Join to a hub that requires a login is refused
  with a `JoinResult` that says so; the joiner shows the same "login required" message as today and
  may still log in over legacy if it has IPv4. C cannot use such a hub. Stage 11 records the gap in
  `roadmap.md`.
- `MeshManager` stays the one membership (canonical). JFP2 becomes a second
  `IMembershipExpression` beside legacy's: a member learned over either protocol is one `Peer`;
  what is sent on each link is that link's protocol (legacy AddNode on legacy links, MemberAnnounce
  on JFP2 links). Liveness on JFP2 links is the keepalive Hello; no Pulse.
- **Join order (D4):** Hello to every candidate of the target, IPv6 first, IPv4 a Happy Eyeballs
  delay (250 ms) later; cookie exchange; Join on the first verified session; legacy Join after 2 s
  of silence, to the IPv4 address, and the outcome remembered per address.
- The password: proof per D1; the verifier is the admitting node, which holds the hash as today.

### 5.4 Candidates and family preference

- The dual-mode socket (`ipv6` slice 1): one socket for both families; IPv4-mapped sources turned
  into IPv4; IPv4 socket when the OS cannot do dual mode or the IPv6 setting is off; `-noipv6`,
  and a `-noipv4` test switch that makes an instance behave as C.
- A node's candidates: its observed public IPv4 endpoint (my-IP lookup as today, or the
  `ObservedEndPoint` of a HelloAck), its LAN IPv4, its global IPv6 addresses (from the interfaces,
  stable before temporary, at most 4, never link-local or ULA). Carried in a Hello TLV and in
  member entries; re-advertised when they change.
- **Preference (goal 2):** between two nodes that both have IPv6, the JFP2 session is verified on
  IPv6 whenever IPv6 answers; IPv4 is the fallback. A link on IPv4 probes IPv6 every 60 s and moves
  when it answers; a link whose IPv6 has been silent for 15 s moves to IPv4 and holds off IPv6 for
  60 s. A datagram arriving on the other family never moves the path by itself (S8).
- Legacy is never handed an IPv6 endpoint. L and R1 are reached over IPv4 legacy exactly as today.

### 5.5 Relaying and bridging

- JFP2 next hop and Forwarded datagrams as in `protocol.md` §6.3 and §8, byte for byte between
  two JFP2 sides whatever their families (S5, S7).
- `RouteQuery`/`RouteAnswer` (internal classes) replace Pathfinder for JFP2 links: a member asks its
  neighbours every 5 s for the members it has no direct link to; a neighbour answers with those it
  reaches directly and has budget for. One hop, as today.
- The relay budget stays one number per node, shared by the protocols (10; a hub setting).
- **Legacy proxy for members without a legacy link (D2):** a legacy datagram arriving at a relay
  for a recipient that has no legacy link is not re-sent as bytes (which is what the legacy plugin
  does today). Pulse and Pathfinder for it are answered by the relay in the recipient's name;
  application messages are decoded to canonical form and routed to the recipient over JFP2 (the
  reverse of `protocol.md` §8.4, which only ever needed the JFP2-to-legacy direction). Guaranteed
  messages are delivered hop by hop as §8.4 already describes.
- Hole punching between two N nodes: both sides send to each other's candidates at once when they
  learn of each other (what legacy's Pulse does today), which is enough for home firewalls (S6).

### 5.6 Directory over JFP2

- New application classes, over non-member sessions (5.2): `StatusRequest`/`Status` already exist;
  `HubList` with keys and candidates, `UserList`/`UserPositions`, `Online`, `UserLookup`. Entries
  carry `WireEndPoint` lists, so IPv6 hubs are listed.
- `hubs.dat` stores candidates; the legacy `HubList` still carries IPv4 entries only, so H is in O's
  list by its IPv4 and C's list shows IPv4-only hubs as unreachable (S9).
- Address text accepts `host`, `a.b.c.d:port`, `[v6]:port` and bare `v6`; DNS returns all addresses
  and the join tries them in family order (`ipv6` slice 2). The `NNNNN-NNNNN` code stays IPv4.
- The seed list gains IPv6 endpoints; the ban list takes IPv6 (a /64 per entry); the X-Plane link,
  Euroscope, Whazzup and the web services are unchanged.

### 5.7 Standing alone

- The JFP2-only `TestMesh` (JFP2 roadmap item 12) is the proof: nodes with only `Jfp2Plugin`
  create, join, exchange positions, relay, list hubs and leave.
- A hub setting "JFP2 only" (off by default) registers no legacy plugin (S12). Deleting the legacy
  plugin later is then a build change, decided from the `Build` counts hubs log.
- The remaining kinds without a JFP2 class (ObjectPosition, RemoveObject, PeerInfo, CommsRequest)
  get one; WeatherRequest and ShowOnRadar are not ported (nothing sends them).

## 6. Stages

One commit per stage, named after the row (`Stage N of jfp2-standalone`), whatever number of
sessions the stage takes (§6.2). Each stage ends with a scenario that can be shown in the evening
test. "Reuse" names a commit to cherry-pick or a file to port; the session reads that code, not the
branch's design documents. Sizes are estimates of sessions: S one, M two, L three. A stage is never
split into smaller stages, and its number never changes.

| # | Stage | Scenario it makes work | Proof (tests) | Field test | Reuse | Size |
|---|---|---|---|---|---|---|
| 0 | **Branch and docs.** New branch from `main`; cherry-pick the documentation move; this plan moves with it; `CLAUDE.md` checked against the tree. | — | all green | — | `ipv6-2` `aecbd6b` | S |
| 1 | **Identity in the core.** `NodeId` as the 8-byte name, alias index, key store with a P-256 key pair and kind-2 name (D3). Every existing test passes unchanged. | S11 (identity survives restarts; two instances, two keys) | `NodeIdTests`, `PeerDirectoryAliasTests`, `NodeKeyStoreTests` | log shows the key name at start | `ipv6-2` `ff4e7d6`, `efec326`, `JoinFS.Crypto/P256KeyPair.cs` | M |
| 2 | **Dual-mode socket.** One socket, mapped addresses normalised, IPv6 setting, `-noipv6`, `-noipv4`; IPv6 endpoints and a per-destination NAT in `InMemoryNetwork`; legacy plugin ignores IPv6 sources. | S11 (an instance starts as B or as C) | `UdpTransportTests` (loopback, both families), `DualStackHarnessTests` | log line for the socket mode | `ipv6` `5e8d8bb` | S |
| 3 | **Admission.** `Result` 2 with cookie, non-member sessions, rate limit. The first JFP2 conversation between strangers. | two N nodes handshake with no legacy between them | `Jfp2AdmissionTests`; `HandshakeGoldenTests` unchanged (§7) | log line per admitted stranger | design-future §3; `ipv6-2` `StatelessCookie.cs` | S |
| 4 | **Join over JFP2.** `Join`, `JoinResult`, `MemberList`; password proof (D1); JFP2 as a membership expression; the joiner becomes a member with no legacy datagram; a member linked over JFP2 lives by the keepalive (moved here from stage 5). IPv4 only. | S4 for the join itself | `Jfp2JoinTests`: create, join, wrong password, late joiner gets the list | A joins H with the `-jfp2join` test switch (until stage 5 makes it the default): Wireshark shows no `0B 52` between them | port of `ipv6-2` `Jfp3Plugin.Join.cs`, `Jfp3Membership.cs` (logic only; handshake differs) | L |
| 5 | **Membership and fallback.** `MemberAnnounce` (the members already in the session learn of a joiner), `MemberLeave` (and the members a joiner learned of through its admitting node, which live as long as that node's session until then), a rejoin of a node the admitting node still holds as a member, JFP2-first join with the 2 s legacy fallback and per-address memory (D4), which replaces the `-jfp2join` switch. The JFP2-only `TestMesh` passes for create, join, positions, leave. | S2 (joining O costs 2 s once), S12 without the setting yet | JFP2-only `TestMesh` un-skipped; `Jfp2JoinTests.ALegacyHub_GetsTheLegacyJoinAfterTwoSeconds` | A joins O and H alternately | same files | M |
| 6 | **Bridging on IPv4.** A JFP2 joiner announced to L/R1 by AddNode and a legacy joiner to N by MemberAnnounce; legacy-to-JFP2 translation at the relay; `RouteQuery`/`RouteAnswer`. | S4 with W present; S7's mechanism on IPv4 | `Jfp2MixedSessionTests` with `TestMesh.Partition` so that H is the only path | A, B (second instance), W-like 26.6 instance, H | `ipv6-2` `Jfp3Plugin.Relay.cs`, `Jfp3Routing.cs` (logic) | L |
| 7 | **IPv6 between N nodes.** Candidates TLV and member entries, address text and DNS for all families, Happy Eyeballs on join, preference and fallback (5.4), family in the snapshot and both UIs, dial log lines. | S1, S3, S6, S8 | `Jfp2IPv6Tests`: join over IPv6, prefer IPv6, fallback, no flipping; `DialAddressesTests` | A to H over IPv6; A and D-like instance direct over IPv6 | `ipv6` `a0d82f1` (dial list), `453e925` (UI family); `ipv6-2` `AddressText`, Happy Eyeballs | L |
| 8 | **IPv6-only nodes.** Synthetic alias from the admitting node (D2), legacy proxy (5.5), a node with no IPv4 is ready with its key name. | S5, S7, S13, S14 | `Jfp2IPv6OnlyTests`: C and W through H; W on H's LAN; the admitting node leaves | second instance started `-noipv4`, a 26.6 instance, H | `ipv6-2` `LocalIdentity.IPv6Only` | L |
| 9 | **Directory over JFP2.** Hub list with candidates, status, user lists, online users and lookups over non-member sessions; `hubs.dat`; seed list; ban list; the unreachable-hub note; the memory of an address that took no JFP2 join (kept in the mesh for the run of the app since stage 5) goes into the address book and `hubs.dat`. | S9 | `Jfp2DirectoryTests` | C's instance opens the hub list and joins H from it | `ipv6-2` `Jfp3Plugin.Directory.cs`, `Jfp3DirectoryCodecs.cs` (logic and bodies) | L |
| 10 | **Remaining kinds and large messages.** ObjectPosition, RemoveObject, PeerInfo, CommsRequest classes; segments behind a capability for notes history. | chat history for a late joiner; recorder playback into a JFP2-only session | round trips; `ALargeNotesHistory` | — | `ipv6-2` `Jfp2Reliability` segmentation | M |
| 11 | **JFP2-only hub.** The hub setting (5.7); the `JoinResult` refusal at a login-required hub and its message (D5 b), with the gap recorded in `roadmap.md`; the UI note where it applies. | S12; S10 at a login-required hub (refused) | `AJfp2OnlyHub_AnswersNoLegacyJoin`; JFP2-only `TestMesh` complete | H restarted JFP2-only; W fails, A joins | `ipv6-2` `--jfp3only` | S |
| 12 | **Specification and release.** `protocol.md` edition 2 with every class, TLV and capability this work added; `implementation.md`, `roadmap.md`, release notes; the Wireshark dissector complete. | — | goldens for the new Hello (§7) | — | `ipv6-2` `253652f` as a model | M |

Stages 1 and 2 are independent of each other; everything else is in order. A stage that does not
fit one session continues in the next session on the same row (§6.2); it is not split.

### 6.1 Status

The only record of what is built. A session updates its row when it starts (claim) and when it
ends (resume note or done). Nothing else in this document changes with progress.

| # | Status | Commit | Resume note (what is done, what is next, which test is red) |
|---|---|---|---|
| 0 | done 2026-10-21 | the first commit of `jfp2-standalone` | — |
| 1 | done 2026-10-21 | the commit `Stage 1 of jfp2-standalone` | — |
| 2 | done 2026-10-21 | the commit `Stage 2 of jfp2-standalone` | — |
| 3 | done 2026-10-21 | the commit `Stage 3 of jfp2-standalone` | — |
| 4 | done 2026-10-21 | the commit `Stage 4 of jfp2-standalone` | — |
| 5 | done 2026-10-21 | the commit `Stage 5 of jfp2-standalone` | — |
| 6 | done 2026-10-21 | the commit `Stage 6 of jfp2-standalone` | — |
| 7 | done 2026-10-21 | the commit `Stage 7 of jfp2-standalone` | — |
| 8 | done 2026-10-21 | the commit `Stage 8 of jfp2-standalone` | — |
| 9 | done 2026-10-21 | the commit `Stage 9 of jfp2-standalone` | — |
| 10 | not started | | |
| 11 | not started | | |
| 12 | not started | | |

### 6.2 One stage, one claim, one commit

These rules exist because earlier sessions committed partial stages under the next stage's number,
re-did stages that were already done, and split stages into pieces nobody could follow.

1. **Claim before anything else.** Read §6.1. If your stage says "done", stop and tell the owner:
   there is nothing to do. If it says "in progress", read the resume note and the working tree
   (`git status`, `git diff --stat`) and continue from there; do not start over and do not re-read
   the branches. If it says "not started", write "in progress since <date>" in the row before
   touching code.
2. **The row is the whole scope.** No sub-stages, no "7a", no commit of a part. If the stage needs
   a piece of a later row, move that piece into your row in §6 (and out of the later one) first, and
   say so in the resume note. Never pull later work forward because it is easy.
3. **A session that ends before the proof is green commits nothing.** It writes the resume note in
   §6.1 (what is done, what is next, which test is red) and leaves the working tree as it is. The
   next session continues the same row from that note. Uncommitted work is the normal state between
   two sessions of one stage.
4. **Done means all of:** the row's proof green; the full test suite green; the six configurations
   build; `protocol.md` and the dissector hold the stage's wire; the `NETWORK:` log lines exist; the
   roadmap entry records every deviation; the evening test showed the scenario, or the entry says
   why it could not yet. Then one commit, `Stage N of jfp2-standalone`, and the row becomes
   "done <date>" with the commit hash.
5. **A done stage is never re-done.** A defect found later is fixed inside the stage in progress and
   noted in its entry, or, when no stage is in progress, as a commit `Fix after stage N: <what>`.
   There is never a second commit called `Stage N`.
6. **Only one stage is in progress at a time.** Stages 1 and 2 are the one exception, and only if
   two sessions run on separate worktrees.

## 7. Rules for the implementing sessions

The rules of `CLAUDE.md` and `roadmap.md` apply. These are added for this work, each because a
session on one of the two branches got it wrong.

1. **Scope is the row.** Build what your row says, for the scenarios it names. If the row needs a
   decision that is not in §4 or §5, do not take it: write the scenario into `roadmap.md` §4 in the
   form of §2.3 (who, what happens today, what should happen), choose the option that changes the
   least for L and R1, log it in the stage's roadmap entry, and go on.
2. **Timings and limits are defaults, not decisions.** Use the values of §5; if one must differ,
   say so in the roadmap entry with the measurement or the scenario that forced it. Never ask the
   owner to choose a number.
3. **Words.** Use §2.1's words in code, logs, tests and documents. A "peer" is a `Peer`; a "link"
   has a protocol; a "path" has a family. Name tests after scenarios (`CAndW_ThroughH_SeeEachOther`).
4. **The wire.** Legacy bytes never change. `Legacy/Fixtures/*.hex` and `HandshakeGoldenTests` pin
   what 26.6 sends and are never regenerated: the new node's own Hello legitimately differs (more
   offers, new TLVs), so stage 3 adds a second golden for the new Hello and keeps the 26.6 one as
   "what an R1 peer sends us". New things go into TLVs, new classes, schema versions and
   capabilities, as `protocol.md` §10 says; the Hello envelope, fixed fields and offer layout do
   not move.
5. **Write the wire first.** Before coding a class, TLV or capability, add its section to
   `protocol.md` and its row to the registries (§10.2), and update `JoinFS/util/wireshark/joinfs.lua`
   in the same stage. A stage that leaves the dissector behind is not done.
6. **Legacy is never handed an IPv6 endpoint,** and no new message goes into `LegacyPlugin`'s
   codecs. The proxy of 5.5 produces existing legacy messages with existing codecs.
7. **One `Peer` per node.** A node learned by two names or over two protocols is one `Peer`;
   nothing is re-keyed. If you find yourself creating a second `Peer` for the same pilot, stop.
8. **Tests are scenarios on `TestMesh`,** with `ManualClock`, `InMemoryNetwork` families and
   `Partition`. They assert what a pilot sees (members, family of each link, positions arriving,
   nothing expiring), not private state. The JFP2-only `TestMesh` test is the tracer bullet: every
   stage from 5 on must leave it green or extend it.
9. **The evening test needs logs.** Every stage adds the `NETWORK:` lines that let `check-logs` say
   which family and protocol each link used and why it changed. No stage is "done" before an
   evening test exercised its scenario, or the roadmap entry says why it could not.
10. **Keep the copies in sync** (`JoinFS.Tests` copies of `Program.Code`, `Substitution`;
    `NetworkSnapshot.MakeEndPoint` beside `LocalIdentity.MakeEndPoint`).
11. **Report short.** A stage report is the roadmap entry plus at most 250 words: scenario shown,
    tests added, deviations, what the evening test should look at. It ends with the prompt for the
    next stage, copied from §9 word for word, so the owner only has to paste it.

## 8. The two branches

Neither is merged. Both stay as sources.

| Branch | Keep for | Do not take |
|---|---|---|
| `ipv6` (13 commits on `main`) | slice 1 (dual-mode socket, settings, dual-stack test harness), slice 2 (ordered dial list, address parsing, D3 fix), slice 5's UI family column, the user stories of `agent-docs/features/ipv6/spec.md` as a checklist for stage 7 | its model (membership on legacy, legacy bytes over IPv6, `PeerCandidates` as a legacy-membership bolt-on), its timings |
| `ipv6-2` (21 commits on `main`) | the docs move (`aecbd6b`); stages 1 and 2 (`NodeId` migration, key store: pure JFP2 items 10 and 11); `JoinFS.Crypto/P256KeyPair.cs` and `StatelessCookie.cs`; the logic of `Jfp3Plugin.Join/Relay/Directory` and the codec bodies, ported onto JFP2 classes; `parity.md` as the parity checklist for goal 1; its IPv6 tests as templates | the JFP3 protocol itself, Noise and `Transport.cs` (unless D1 = c), decisions 1, 2, 4, 13, 17, 33, 37, 38 and everything that follows from them; `seedhubs3.txt` with keys |

The local `.claude/CLAUDE.md` is gitignored; stage 0 rewrote its JFP3 paragraph to point here.

## 9. Starting a session

The owner pastes the prompt of the stage, and nothing else. Every prompt is the same text with the
stage number changed, so every session starts from the same picture. A session that continues a
stage after a resume note gets the same prompt again. Each stage report ends with the next prompt
(§7 rule 11); this list is the copy for when it does not.

**Stage 2: Dual-mode socket**

```
Implement stage 2 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 3 from §9.
```

**Stage 3: Admission**

```
Implement stage 3 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 4 from §9.
```

**Stage 4: Join over JFP2**

```
Implement stage 4 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 5 from §9.
```

**Stage 5: Membership and fallback**

```
Implement stage 5 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 6 from §9.
```

**Stage 6: Bridging on IPv4**

```
Implement stage 6 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 7 from §9.
```

**Stage 7: IPv6 between N nodes**

```
Implement stage 7 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 8 from §9.
```

**Stage 8: IPv6-only nodes**

```
Implement stage 8 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 9 from §9.
```

**Stage 9: Directory over JFP2**

```
Implement stage 9 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 10 from §9.
```

**Stage 10: Remaining kinds and large messages**

```
Implement stage 10 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 11 from §9.
```

**Stage 11: JFP2-only hub**

```
Implement stage 11 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; end with the prompt for stage 12 from §9.
```

**Stage 12: Specification and release**

```
Implement stage 12 of docs/network/jfp2/standalone-ipv6-plan.md.
Read §6.1 first and follow §6.2: claim the row or continue its resume note; one commit per stage,
named after the row, only when §6.2 rule 4 holds; otherwise write the resume note and commit nothing.
Then read §1, §2.1, §5, §7 and your row in §6, and only the protocol.md sections the row cites.
Decisions are in §4 and §5; if your row needs one that is not there, follow §7 rule 1.
Report in at most 250 words: scenario shown, tests added, deviations, what the evening test should
look at; this is the last stage, so no next prompt.
```
