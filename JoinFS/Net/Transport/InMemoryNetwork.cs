using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace JoinFS.Net
{
    /// <summary>
    /// A deterministic, single-threaded stand-in for the internet: every <see cref="InMemoryTransport"/>
    /// registered here gets an endpoint, sends are queued, and <see cref="Pump"/> delivers them in
    /// order to the receiver's callback. Lets tests run several network cores (peers, hubs, legacy
    /// and JFP2 nodes) in one process with no sockets, threads or timing flakiness, and inject
    /// loss to exercise retries.
    /// </summary>
    public sealed class InMemoryNetwork
    {
        readonly Dictionary<IPEndPoint, InMemoryTransport> transports = [];
        readonly Queue<(IPEndPoint From, IPEndPoint To, byte[] Data)> inFlight = new();

        /// <summary>Return false to drop a datagram (loss/partition injection).</summary>
        public Func<IPEndPoint, IPEndPoint, byte[], bool> Filter;

        /// <summary>
        /// Rewrites a datagram's endpoints on its way from sender to receiver (after <see cref="Filter"/>,
        /// which sees the sender's own addresses): a stand-in for NAT and port forwarding, including the
        /// awkward cases such as two nodes behind one public endpoint. Return the pair to deliver with.
        /// </summary>
        public Func<IPEndPoint, IPEndPoint, (IPEndPoint From, IPEndPoint To)> Nat;

        /// <summary>Every datagram ever sent, in order (for assertions and debugging).</summary>
        public readonly List<(IPEndPoint From, IPEndPoint To, byte[] Data)> Log = [];

        // IPv6 addresses behind a stateful firewall, and the openings their nodes made by sending out
        readonly HashSet<IPAddress> firewalled = [];
        readonly HashSet<(IPAddress Node, IPAddress Remote)> openings = [];

        public InMemoryTransport Attach(IPEndPoint endPoint, Action<IPEndPoint, byte[]> receive)
        {
            var transport = new InMemoryTransport(this, endPoint, null, receive);
            transports.Add(endPoint, transport);
            return transport;
        }

        /// <summary>
        /// A dual-stack node: one transport that owns an IPv4 and an IPv6 endpoint, receives on both and
        /// sends from the one whose family matches the destination (as a dual-mode socket does).
        /// </summary>
        public InMemoryTransport Attach(IPEndPoint endPoint, IPEndPoint endPointV6, Action<IPEndPoint, byte[]> receive)
        {
            var transport = new InMemoryTransport(this, endPoint, endPointV6, receive);
            transports.Add(endPoint, transport);
            transports.Add(endPointV6, transport);
            return transport;
        }

        public void Detach(IPEndPoint endPoint)
        {
            if (transports.Remove(endPoint, out InMemoryTransport transport) && transport.EndPointV6 != null)
            {
                transports.Remove(transport.EndPointV6);
                transports.Remove(transport.EndPoint);
            }
        }

        /// <summary>
        /// Puts the node with this IPv6 address behind the stateful firewall of a home router: an
        /// inbound IPv6 datagram is dropped unless the node has already sent to the sender's address.
        /// IPv4 is not affected.
        /// </summary>
        public void FirewallIpv6(IPAddress node) => firewalled.Add(node);

        internal void Enqueue(IPEndPoint from, IPEndPoint to, ReadOnlySpan<byte> datagram)
        {
            byte[] copy = datagram.ToArray();
            if (from.AddressFamily == AddressFamily.InterNetworkV6 && firewalled.Contains(from.Address))
            {
                openings.Add((from.Address, to.Address));
            }
            Log.Add((from, to, copy));
            inFlight.Enqueue((from, to, copy));
        }

        /// <summary>Delivers queued datagrams (including ones sent while delivering) until none remain
        /// or <paramref name="maxDatagrams"/> is reached. Returns how many were delivered.</summary>
        public int Pump(int maxDatagrams = 100_000)
        {
            int delivered = 0;
            while (inFlight.Count > 0 && delivered < maxDatagrams)
            {
                var (from, to, data) = inFlight.Dequeue();
                if (to.AddressFamily == AddressFamily.InterNetworkV6 && firewalled.Contains(to.Address)
                    && !openings.Contains((to.Address, from.Address)))
                {
                    continue;
                }
                if (Filter != null && !Filter(from, to, data))
                {
                    continue;
                }
                if (Nat != null)
                {
                    (from, to) = Nat(from, to);
                }
                if (transports.TryGetValue(to, out InMemoryTransport target))
                {
                    delivered++;
                    target.Receive(from, data);
                }
            }
            return delivered;
        }
    }

    public sealed class InMemoryTransport : IDatagramTransport
    {
        readonly InMemoryNetwork network;
        readonly Action<IPEndPoint, byte[]> receive;

        public IPEndPoint EndPoint { get; }

        /// <summary>The IPv6 endpoint of a dual-stack node (null for a node with one endpoint).</summary>
        public IPEndPoint EndPointV6 { get; }
        public int LocalPort => EndPoint.Port;

        public event Action<IPEndPoint, string> SendFailed { add { } remove { } }

        internal InMemoryTransport(InMemoryNetwork network, IPEndPoint endPoint, IPEndPoint endPointV6, Action<IPEndPoint, byte[]> receive)
        {
            this.network = network;
            this.receive = receive;
            EndPoint = endPoint;
            EndPointV6 = endPointV6;
        }

        public void Send(IPEndPoint to, ReadOnlySpan<byte> datagram)
        {
            IPEndPoint from = EndPointV6 != null && to.AddressFamily == AddressFamily.InterNetworkV6 ? EndPointV6 : EndPoint;
            if (EndPointV6 == null && from.AddressFamily != to.AddressFamily && from.AddressFamily == AddressFamily.InterNetwork)
            {
                return; // an IPv4-only node has no route to an IPv6 address
            }
            network.Enqueue(from, to, datagram);
        }

        internal void Receive(IPEndPoint from, byte[] data) => receive(from, data);
    }
}
