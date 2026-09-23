using System.Collections.Generic;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>Transport-level state of one peer, as of the snapshot.</summary>
    public sealed class PeerSnapshot
    {
        public NodeId Id { get; init; }
        public IPEndPoint EndPoint { get; init; }
        public IPEndPoint RouteEndPoint { get; init; }
        public bool Direct { get; init; }
        public bool RouteIsOwnEndPoint { get; init; }
        public bool ReceiveEstablished { get; init; }
        public bool SendEstablished { get; init; }
        public bool LowBandwidth { get; init; }
        public float Rtt { get; init; }
        /// <summary>Name of the plugin that carries positions to this peer (e.g. "Legacy", "JFP2").</summary>
        public string PositionProtocol { get; init; }
        /// <summary>Human-readable protocol link state for the session window (plugin-provided).</summary>
        public string LinkState { get; init; }
    }

    /// <summary>
    /// An immutable picture of the network thread's state, republished periodically. The app (UI,
    /// Sim, CONSOLE services) reads it without any lock; it is never more than one publish interval
    /// old. Replaces direct reads of LocalNode/Network fields from other threads.
    /// </summary>
    public sealed class NetworkSnapshot
    {
        public static readonly NetworkSnapshot Empty = new() { Peers = new Dictionary<NodeId, PeerSnapshot>(), PeerList = [] };

        public bool Open { get; init; }
        public int Port { get; init; }
        public NodeId LocalId { get; init; }
        public bool Ready => LocalId.Valid();
        public IPAddress LocalAddress { get; init; } = IPAddress.Loopback;
        public IPAddress InternetAddress { get; init; } = IPAddress.None;
        public SessionState State { get; init; }
        public bool Connected => State == SessionState.Connected;
        public uint Suid { get; init; }
        public bool GlobalSession { get; init; }
        public bool Creator { get; init; }
        public bool LoginRequired { get; init; }
        public bool PasswordProtected { get; init; }
        public JoinResult JoinResult { get; init; }
        public LoginResult LoginResult { get; init; }
        public bool LowBandwidth { get; init; }
        public int RelayCount { get; init; }
        public int GuaranteedInCount { get; init; }
        public int GuaranteedOutCount { get; init; }

        /// <summary>Peers keyed by id (read-only after publication).</summary>
        public IReadOnlyDictionary<NodeId, PeerSnapshot> Peers { get; init; }

        /// <summary>Peers in session order.</summary>
        public IReadOnlyList<PeerSnapshot> PeerList { get; init; }

        public int PeerCount => PeerList.Count;

        public PeerSnapshot Peer(NodeId id) => Peers.TryGetValue(id, out PeerSnapshot peer) ? peer : null;

        /// <summary>Round-trip time to a peer in seconds (9999 if unknown, like LocalNode.GetNodeRTT).</summary>
        public float Rtt(NodeId id) => Peers.TryGetValue(id, out PeerSnapshot peer) ? peer.Rtt : 9999.0f;

        /// <summary>The endpoint to reach <paramref name="node"/> on <paramref name="port"/> (LAN address when it shares our NAT).</summary>
        public IPEndPoint MakeEndPoint(NodeId node, ushort port)
        {
            IPEndPoint endPoint = node.ToEndPoint(port);
            if (endPoint.Address.Equals(InternetAddress))
            {
                byte[] bytes = LocalAddress.GetAddressBytes();
                bytes[3] = node.local;
                endPoint.Address = new IPAddress(bytes);
                endPoint.Port = node.port;
            }
            return endPoint;
        }
    }
}
