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

        /// <summary>
        /// This node's own IPv6 endpoint candidate: the global address the OS would send from, with the
        /// socket's port (docs/jfp2/design-future.md §4.1). Null when the node has no usable IPv6 (an
        /// IPv4-only socket, no IPv6 route), which also means it can send no IPv6.
        /// </summary>
        IPEndPoint LocalIpv6Candidate => null;

        event Action<IPEndPoint, string> SendFailed;
    }
}
