using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Transport-level state of one remote node in the session: where it is, how we reach it,
    /// whether traffic flows both ways, and its round-trip time. Owned by the network thread; the
    /// app sees it through <see cref="NetworkSnapshot"/>.
    /// </summary>
    public sealed class Peer
    {
        public readonly NodeId Id;

        /// <summary>Where the node actually is (its own address, as registered).</summary>
        public IPEndPoint EndPoint;

        /// <summary>Where we send to reach it: its own endpoint, or a relay node's endpoint.</summary>
        public IPEndPoint RouteEndPoint;

        /// <summary>
        /// Route goes to the node's own address (port may differ). The legacy protocol's notion of
        /// "direct", used for its relay and pathfinder decisions.
        /// </summary>
        public bool Direct => EndPoint.Address.Equals(RouteEndPoint.Address);

        /// <summary>
        /// The relay node this peer is reached through, or invalid when it is reached directly. Set
        /// when the pathfinder picks a relay; unlike <see cref="RouteEndPoint"/> it stays correct when
        /// the relay and the peer share an endpoint (two nodes behind one NAT port forward).
        /// </summary>
        public NodeId RouteVia;

        /// <summary>
        /// The other names the node is known by (its <see cref="Id"/> is the first it was learned by and
        /// never changes). Null until it has one; the index of them is <see cref="PeerDirectory"/>'s.
        /// </summary>
        internal List<NodeId> Aliases;

        /// <summary>The peer is reached through another node.</summary>
        public bool Relayed => RouteVia.Valid();

        /// <summary>
        /// Route is exactly the node's own endpoint (address and port) and no relay is involved.
        /// Stricter than <see cref="Direct"/>; what a relay needs before it forwards to a peer.
        /// </summary>
        public bool RouteIsOwnEndPoint => !Relayed && RouteEndPoint.Equals(EndPoint);

        /// <summary>We have heard from the node.</summary>
        public bool ReceiveEstablished;

        /// <summary>The node has answered us (our traffic reaches it).</summary>
        public bool SendEstablished;

        /// <summary>The node asked for reduced traffic.</summary>
        public bool LowBandwidth;

        /// <summary>Round-trip time in seconds (from pulses).</summary>
        public float Rtt;

        /// <summary><see cref="IClock.Now"/> after which the node is dropped unless it responds again.</summary>
        public double ExpireTime;

        /// <summary>Index into per-peer arrays (router cache); stable while the peer exists.</summary>
        internal int Slot;

        /// <summary>Per-kind index into NetworkCore's plugin list (-1 = not resolved yet).</summary>
        internal readonly sbyte[] Routes = new sbyte[(int)MessageKind.Count];

        public Peer(NodeId id, IPEndPoint endPoint, bool receiveEstablished)
        {
            Id = id;
            EndPoint = endPoint;
            RouteEndPoint = endPoint;
            ReceiveEstablished = receiveEstablished;
            InvalidateRoutes();
        }

        internal void InvalidateRoutes()
        {
            for (int i = 0; i < Routes.Length; i++) Routes[i] = -1;
        }
    }

    /// <summary>
    /// The one table of session peers, keyed by <see cref="NodeId"/>. Iteration order is insertion
    /// order (plain Dictionary, same add/remove pattern as LocalNode.nodes), which the legacy
    /// broadcast byte order depends on.
    ///
    /// A peer's id is the name it was first learned by and is never re-keyed (docs/network/jfp2/
    /// design-node-identity.md §1.2). Every other name of a peer (its legacy id learned through a
    /// handshake, its key learned through legacy) is an alias: the alias index maps a name to the id
    /// of its peer, so a plugin that decodes a name resolves it before it touches the core.
    /// </summary>
    public sealed class PeerDirectory
    {
        readonly Dictionary<NodeId, Peer> peers = [];
        readonly Dictionary<NodeId, NodeId> aliases = [];
        readonly Stack<int> freeSlots = new();
        int nextSlot;

        public int Count => peers.Count;

        /// <summary>
        /// Every peer, in insertion order. Typed as the concrete <c>Dictionary&lt;,&gt;.ValueCollection</c>
        /// rather than <see cref="IEnumerable{T}"/> on purpose: every caller only ever `foreach`s
        /// this (checked - none assign it to an IEnumerable&lt;Peer&gt; or pass it to a LINQ method),
        /// and foreach against the concrete type uses the dictionary's own struct enumerator directly
        /// instead of boxing it through the interface - one allocation removed from every decode
        /// (Jfp2Plugin.FindPeer runs this on every incoming datagram; ~40 B measured in
        /// JoinFS.Benchmarks' DecodeJfp2 before this fix, matching one boxed
        /// Dictionary&lt;NodeId,Peer&gt;.ValueCollection.Enumerator).
        /// </summary>
        public Dictionary<NodeId, Peer>.ValueCollection All => peers.Values;

        public bool TryGet(NodeId id, out Peer peer) => peers.TryGetValue(id, out peer);

        public bool Contains(NodeId id) => peers.ContainsKey(id);

        public Peer Add(NodeId id, IPEndPoint endPoint, bool receiveEstablished)
        {
            var peer = new Peer(id, endPoint, receiveEstablished)
            {
                Slot = freeSlots.Count > 0 ? freeSlots.Pop() : nextSlot++,
            };
            peers[id] = peer;
            return peer;
        }

        public bool Remove(NodeId id)
        {
            if (peers.Remove(id, out Peer peer))
            {
                freeSlots.Push(peer.Slot);
                if (peer.Aliases != null)
                {
                    foreach (NodeId alias in peer.Aliases) aliases.Remove(alias);
                }
                return true;
            }
            return false;
        }

        public void Clear()
        {
            peers.Clear();
            aliases.Clear();
            freeSlots.Clear();
            nextSlot = 0;
        }

        /// <summary>
        /// Records <paramref name="name"/> as another name of the peer <paramref name="id"/>. False, and
        /// nothing changes, when the peer is not in the directory, the name is not valid, or it already
        /// names a peer (this one: nothing to do; another: a name is never taken over).
        /// </summary>
        public bool AddAlias(NodeId name, NodeId id)
        {
            if (!name.Valid() || !peers.TryGetValue(id, out Peer peer) || peers.ContainsKey(name) || aliases.ContainsKey(name))
            {
                return false;
            }
            aliases[name] = id;
            (peer.Aliases ??= []).Add(name);
            return true;
        }

        /// <summary>
        /// The id of the peer <paramref name="name"/> belongs to: the name itself when it is a peer's id,
        /// the peer's id when it is an alias. False for a name no peer has.
        /// </summary>
        public bool TryResolve(NodeId name, out NodeId id)
        {
            if (peers.ContainsKey(name))
            {
                id = name;
                return true;
            }
            return aliases.TryGetValue(name, out id);
        }

        /// <summary>
        /// <paramref name="name"/> as the id of its peer when it has one, else unchanged: how a plugin
        /// reads a name off the wire (an unknown kind-0 name still stands for a node not yet added).
        /// </summary>
        public NodeId Resolve(NodeId name) => TryResolve(name, out NodeId id) ? id : name;

        /// <summary>
        /// The legacy id of the node <paramref name="id"/>: its own when it is a kind-0 id, else the kind-0
        /// alias of its peer. False for a node without one, which no legacy message can name.
        /// </summary>
        public bool TryGetLegacy(NodeId id, out LegacyNodeId legacy)
        {
            if (id.TryGetLegacy(out legacy))
            {
                return true;
            }
            if (peers.TryGetValue(id, out Peer peer) && peer.Aliases != null)
            {
                foreach (NodeId alias in peer.Aliases)
                {
                    if (alias.TryGetLegacy(out legacy))
                    {
                        return true;
                    }
                }
            }
            legacy = default;
            return false;
        }

        /// <summary>The peer whose own endpoint is exactly <paramref name="endPoint"/>, if any.</summary>
        public Peer FindByEndPoint(IPEndPoint endPoint)
        {
            foreach (Peer peer in peers.Values)
            {
                if (peer.EndPoint.Equals(endPoint))
                {
                    return peer;
                }
            }
            return null;
        }

        /// <summary>Number of peers on the same device (same public IP and LAN octet) as <paramref name="id"/>.</summary>
        public int CountSameDevice(NodeId id)
        {
            int count = 0;
            foreach (NodeId key in peers.Keys)
            {
                if (NodeId.SameDevice(key, id))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
