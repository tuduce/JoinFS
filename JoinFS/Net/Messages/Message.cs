using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Every kind of message the network core understands, independent of wire protocol. A protocol
    /// plugin advertises which kinds it can carry to which peer; the router picks a plugin per
    /// (peer, kind). Values index flat per-peer routing arrays, so keep them dense and below
    /// <see cref="Count"/>.
    ///
    /// See docs/network-plugin-architecture.md §2.2 (canonical model) and §2.8 (how it evolves while
    /// the legacy protocol stays frozen): this model is in-process only, never a wire format.
    /// </summary>
    public enum MessageKind : byte
    {
        // ---- objects
        Position,
        ObjectPosition,
        Identity,
        VariableSync,
        Event,
        RemoveObject,
        FlightPlan,
        ShowOnRadar,

        // ---- peer / session information
        PeerInfo,
        StatusRequest,
        Status,
        WeatherRequest,
        WeatherReply,
        WeatherUpdate,

        // ---- hub directory
        HubList,
        UserListRequest,
        HubUser,
        UserPositionsRequest,
        UserPositions,
        Online,
        UserNuidRequest,
        UserNuid,

        // ---- comms
        CommsRequest,
        Notes,

        // ---- mesh (membership, liveness, routing) - handled by the core's MeshManager
        Join,
        JoinReply,
        JoinFail,
        Login,
        LoginFail,
        AddNode,
        Leave,
        Pulse,
        PulseResponse,
        Pathfinder,
        PathfinderResponse,

        Count,
    }

    /// <summary>
    /// A canonical message body. Implemented by structs only, so generic code constrained on
    /// <c>T : struct, IMessage</c> dispatches without boxing.
    /// </summary>
    public interface IMessage
    {
        static abstract MessageKind Kind { get; }

        /// <summary>Calls the <paramref name="handler"/> overload for this message type (double dispatch).</summary>
        void Dispatch(IMessageHandler handler, in MessageMeta meta);
    }

    /// <summary>
    /// Envelope information that accompanies every canonical message in either direction.
    /// </summary>
    public struct MessageMeta
    {
        /// <summary>
        /// The node the message is from. Inbound: the node that authored it (for a relayed or
        /// translated message this is the original author, not the relay). Outbound: normally
        /// the local node; a relay/translation sets it to the original author.
        /// </summary>
        public NodeId Sender;

        /// <summary>
        /// The node the message is addressed to; invalid for "not addressed to a particular node"
        /// (broadcast fan-out entries, or a raw-endpoint send to a not-yet-known node).
        /// </summary>
        public NodeId Recipient;

        /// <summary>
        /// Inbound: the endpoint the message is treated as coming from (the sender's route
        /// endpoint once it's established, else the datagram's source). Outbound: an explicit
        /// destination; null means "route to <see cref="Recipient"/>".
        /// </summary>
        public IPEndPoint EndPoint;

        /// <summary>Delivery must be acknowledged and retried.</summary>
        public bool Guaranteed;

        /// <summary>Inbound: the message reached us through a relay rather than directly from the sender.</summary>
        public bool Forwarded;

        /// <summary>
        /// The sender's application data-model version, when its protocol conveys one (the legacy
        /// protocol prefixes every application message with it; diagnostic only).
        /// </summary>
        public short DataVersion;

        public static MessageMeta To(NodeId recipient, bool guaranteed = false) =>
            new() { Recipient = recipient, Guaranteed = guaranteed };

        public static MessageMeta ToEndPoint(IPEndPoint endPoint, bool guaranteed = false, NodeId recipient = default) =>
            new() { EndPoint = endPoint, Recipient = recipient, Guaranteed = guaranteed };
    }
}
