using System.Net;

namespace JoinFS.Net
{
    public enum NetworkEventKind
    {
        /// <summary>This node joined a session (it now has a session id).</summary>
        SessionJoined,
        /// <summary>A node was heard from for the first time.</summary>
        PeerJoined,
        /// <summary>Two-way traffic with a node was confirmed (first pulse response).</summary>
        PeerEstablished,
        /// <summary>A node left or timed out; everything it owned is gone.</summary>
        PeerLeft,
        /// <summary>
        /// <see cref="NetworkEvent.Node"/> saw our datagrams come from <see cref="NetworkEvent.EndPoint"/>
        /// (raised when its observation changes); the app classifies our NAT from these and logs it.
        /// </summary>
        EndPointObserved,
        Log,
        /// <summary>
        /// The address <see cref="NetworkEvent.EndPoint"/> took no conversation over JFP2 (a join or a request got no answer): it is joined and asked
        /// over legacy for <see cref="NetworkEvent.Seconds"/>. The app keeps the memory, so the next run does not ask again at once.
        /// </summary>
        LegacyOnly,
    }

    public struct NetworkEvent
    {
        public NetworkEventKind Kind;
        public NodeId Node;
        public IPEndPoint EndPoint;
        public NetLogLevel Level;
        public string Text;
        /// <summary>LegacyOnly: how long the memory lasts.</summary>
        public double Seconds;
    }

    /// <summary>
    /// Where the network core sends what the application must act on: canonical messages addressed
    /// to this node (other than mesh messages, which the core consumes) and session events.
    /// NetworkService implements it with a queue drained on the app thread; tests collect directly.
    /// </summary>
    public interface INetworkSink
    {
        void Deliver<T>(in MessageMeta meta, in T message) where T : struct, IMessage;

        void OnEvent(in NetworkEvent e);
    }
}
