using System;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Sends raw datagrams. Every protocol plugin shares one transport (one UDP socket and port),
    /// because the legacy wire identifies a node by IP+port and NAT traversal depends on it
    /// (docs/network-plugin-architecture.md §1, idea B).
    ///
    /// Receiving is not part of this interface: who calls the core with incoming datagrams, and on
    /// which thread, is decided by the owner (NetworkService for the real socket, InMemoryNetwork
    /// for deterministic tests).
    /// </summary>
    public interface IDatagramTransport
    {
        /// <summary>Sends <paramref name="datagram"/> to <paramref name="to"/>; never throws (failures are reported via <see cref="SendFailed"/>).</summary>
        void Send(IPEndPoint to, ReadOnlySpan<byte> datagram);

        /// <summary>The local port the transport is bound to (0 when closed).</summary>
        int LocalPort { get; }

        event Action<IPEndPoint, string> SendFailed;
    }
}
