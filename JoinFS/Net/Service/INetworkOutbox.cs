using System;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Sending canonical messages from the app thread. <see cref="NetworkService"/> implements it by
    /// queuing each send for the network thread; the app's session parts depend only on this, so
    /// tests can record what they send.
    /// </summary>
    public interface INetworkOutbox
    {
        void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage;

        void SendTo<T>(NodeId recipient, in T message, bool guaranteed = false) where T : struct, IMessage;

        void SendToEndPoint<T>(IPEndPoint endPoint, in T message, bool guaranteed = false) where T : struct, IMessage;

        void Broadcast<T>(in T message, bool guaranteed = false) where T : struct, IMessage;

        /// <summary>An object's identity (cached for the protocols that need it) and then its position.</summary>
        void SendObjectState<TPosition>(in IdentityUpdate identity, in TPosition position, ReadOnlySpan<NodeId> recipients) where TPosition : struct, IMessage;
    }
}
