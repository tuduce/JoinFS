using System;
using System.Collections.Generic;
using System.Net;

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

        public InMemoryTransport Attach(IPEndPoint endPoint, Action<IPEndPoint, byte[]> receive)
        {
            var transport = new InMemoryTransport(this, endPoint, receive);
            transports.Add(endPoint, transport);
            return transport;
        }

        public void Detach(IPEndPoint endPoint) => transports.Remove(endPoint);

        internal void Enqueue(IPEndPoint from, IPEndPoint to, ReadOnlySpan<byte> datagram)
        {
            byte[] copy = datagram.ToArray();
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
        public int LocalPort => EndPoint.Port;

        public event Action<IPEndPoint, string> SendFailed { add { } remove { } }

        internal InMemoryTransport(InMemoryNetwork network, IPEndPoint endPoint, Action<IPEndPoint, byte[]> receive)
        {
            this.network = network;
            this.receive = receive;
            EndPoint = endPoint;
        }

        public void Send(IPEndPoint to, ReadOnlySpan<byte> datagram) => network.Enqueue(EndPoint, to, datagram);

        internal void Receive(IPEndPoint from, byte[] data) => receive(from, data);
    }
}
