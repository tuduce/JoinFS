# JFP2 documentation

JFP2 is the JoinFS Protocol, version 2: the wire that replaces JoinFS's original ("legacy")
protocol. These documents describe it. Each kind of information lives in exactly one of them.

| Question | Read | Kind |
|---|---|---|
| What must JFP2 achieve, and what has been decided? | [goals.md](goals.md) | Goals, principles, release plan, decision log |
| What exactly goes on the wire, and how must a node behave? | [protocol.md](protocol.md) | Specification (RFC-style, normative) |
| Why is the wire like that, and what was rejected? | [rationale.md](rationale.md) | Design rationale |
| How does the code do it, what is built, how do I add a class or test skew? | [implementation.md](implementation.md) | Implementation guide and record of the work |
| What is left to do, in what order, and what is still undecided? | [roadmap.md](roadmap.md) | **All** open tasks and open questions |
| How will node identity and the node key work? | [design-node-identity.md](design-node-identity.md) | Approved designs of the next stages |
| How might the mesh, admission, IPv6, relaying, security work? | [design-future.md](design-future.md) | Non-binding sketches of later work |
| How was it first built and audited? | [history/](history/) | Frozen records; do not update |

## Rules for keeping it that way

- **A fact has one home.** The wire is only in `protocol.md`; reasons only in `rationale.md`;
  decisions only in `goals.md` §6; open tasks and questions only in `roadmap.md`; what is built only
  in `implementation.md`. Other documents link; they do not restate.
- **The specification is normative and stands alone.** It uses MUST/SHOULD/MAY, contains no task
  lists, no status and no history, and reserved values appear only in its registries (§10.2). A node
  written from `protocol.md` alone must interoperate.
- **When a task finishes,** delete it from `roadmap.md`, add it to the record in `implementation.md`
  §7, and if it changed the wire, change `protocol.md`, its Appendix A and `HandshakeGoldenTests`
  together with a note in `protocol.md` Appendix C.
- **When a design is built,** move its rules into `protocol.md` and its code description into
  `implementation.md`, and shorten the design document to what is still open or delete it.

## Reading order

New to the project: `goals.md` §1–§5, then `protocol.md` §1–§3 and §5–§8, then
`implementation.md`. Implementing a change: `roadmap.md`, then the design it points to.

## Related documents elsewhere

- `../network-protocol.md`: the legacy wire, which JFP2 coexists with and eventually replaces.
- `../network-plugin-architecture.md`: the decision record of the plugin network stack JFP2 runs in
  (§2.12 and §2.13 concern JFP2).
- `../reference/joinfs-architecture.md`: how the whole application works today.
- `../captures/`: Wireshark captures of live sessions. `JoinFS/util/wireshark/joinfs.lua` dissects
  both protocols.
- `../../ProtocolV2Reference/`: the original stand-alone reference code, kept for history; it is not
  part of the build.
