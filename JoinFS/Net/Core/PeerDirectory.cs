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

        /// <summary>The peer is reached through another node.</summary>
        public bool Relayed => RouteVia.Valid();

        /// <summary>
        /// Route is exactly the node's own endpoint (address and port) and no relay is involved.
        /// Stricter than <see cref="Direct"/>; what a relay needs before it forwards to a peer.
        /// </summary>
        public bool RouteIsOwnEndPoint => !Relayed && RouteEndPoint.Equals(EndPoint);

        /// <summary>The node's own path is IPv6 (a dial over IPv6 or a path upgrade).</summary>
        public bool OnIpv6 => EndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

        /// <summary>
        /// The IPv4 endpoint kept for a fallback while the node is on an upgraded IPv6 path
        /// (docs/jfp2/design-future.md §4.1); null on an IPv4 path and for a node dialled over IPv6.
        /// </summary>
        public IPEndPoint Ipv4EndPoint;

        /// <summary>
        /// The port the node is known by to other nodes (AddNode, JoinReply): its IPv4 endpoint's. For a node
        /// behind an IPv4 NAT that can differ from the port it is reached on over IPv6.
        /// </summary>
        public int AdvertisedPort => (Ipv4EndPoint ?? EndPoint).Port;

        /// <summary>The node's IPv6 endpoint candidates (self-reported or observed), probed for a path upgrade; at most <see cref="Jfp2.Codecs.PeerCandidatesV1Codec.MaxCandidates"/>.</summary>
        public IReadOnlyList<IPEndPoint> Candidates => candidates;

        internal List<IPEndPoint> candidates = [];

        /// <summary>
        /// Identifies the current <see cref="Candidates"/> among all changes this node has seen (a node-wide counter,
        /// so a peer that left and came back never repeats one): what a hub has told a member is remembered by it.
        /// </summary>
        internal int CandidatesRevision;

        /// <summary>This node answered the peer's Join, so it is the peer's hub: it tells the peer where the other members are (PeerCandidates).</summary>
        internal bool JoinedThroughMe;

        /// <summary>For a peer this node is the hub of: the <see cref="CandidatesRevision"/> of each peer whose candidates it has sent the peer.</summary>
        internal Dictionary<NodeId, int> Announced;

        /// <summary>
        /// The relay a peer was reached through when its path was upgraded to a direct IPv6 one: a fallback goes
        /// back to it. Invalid when the peer was direct on IPv4 before.
        /// </summary>
        internal NodeId RelayBeforeUpgrade;

        /// <summary>
        /// The peer had never answered over its IPv4 endpoint when its path was upgraded (the endpoint was only
        /// a guess from its id, and may be unreachable): a fallback leaves it to be searched for (Pathfinder).
        /// </summary>
        internal bool Ipv4Unproven;

        /// <summary><see cref="IClock.Now"/> until which the candidates are probed (30 s from when they became known).</summary>
        internal double ProbeUntil;

        /// <summary>No probing and no upgrade before this time: the hold-off after a fallback.</summary>
        internal double HoldOffUntil;

        /// <summary>When the node last answered a Pulse over the IPv6 path (see the fallback).</summary>
        internal double Ipv6AnsweredAt;

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
    /// </summary>
    public sealed class PeerDirectory
    {
        readonly Dictionary<NodeId, Peer> peers = [];
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
                return true;
            }
            return false;
        }

        public void Clear()
        {
            peers.Clear();
            freeSlots.Clear();
            nextSlot = 0;
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
