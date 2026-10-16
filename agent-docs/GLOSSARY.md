# JoinFS

Pilots on different simulators fly together in shared sessions; JoinFS instances find and reach
each other over the internet and exchange aircraft state.

## Language

### Nodes

**Node**:
One running JoinFS.
_Avoid_: Instance, client, user (a user is the person flying)

**Hub**:
A node, usually always on, that other nodes join through and that lists sessions and users.
_Avoid_: Server

**Peer**:
Another node this node knows about in its session.
_Avoid_: Connection

### Reachability

**Dual-stack node**:
A node that can use both IPv4 and IPv6.

**IPv4-only node** / **IPv6-only node**:
A node that can use only one address family.

**Endpoint candidate**:
An address and port where a node may be reachable.
_Avoid_: Address (when more than one may exist)

**Path**:
A way for two nodes to reach each other over one address family: an IPv4 path or an IPv6 path.
_Avoid_: Connection, link

**Direct** / **Relayed**:
Whether a path goes straight between two nodes or through a third node.

**Path upgrade**:
A peer's path moving from IPv4 to IPv6.
_Avoid_: Upgrade (alone)

**Fallback**:
A peer's path moving back from IPv6 to IPv4 after the IPv6 path stopped answering.

### Protocols

**Legacy**:
The original JoinFS wire protocol, frozen and spoken by every node.

**JFP2**:
The newer wire protocol that will replace legacy.

**Protocol upgrade**:
Two neighbouring nodes switching the traffic between them from legacy to JFP2.
_Avoid_: Upgrade (alone)
