using System;
using System.Net;
using JoinFS.Net;

namespace JoinFS.Benchmarks
{
    /// <summary>
    /// A transport connecting exactly two NetworkCores with no queueing (unlike InMemoryNetwork,
    /// which is built for test assertions - its Log/inFlight lists would grow unboundedly across the
    /// millions of calls a microbenchmark makes). Send() forwards the span directly into the paired
    /// core's OnDatagram with zero copies, used only while negotiating in [GlobalSetup]; then
    /// <see cref="Silent"/> is set so the timed [Benchmark] methods measure only the plugin's own
    /// encode cost, not a second core's whole receive pipeline.
    /// </summary>
    sealed class SwitchableTransport : IDatagramTransport
    {
        readonly IPEndPoint localEndPoint;

        /// <summary>The other side, wired up after both cores exist.</summary>
        public NetworkCore? Peer;

        /// <summary>True once negotiation is done: Send() becomes a true no-op.</summary>
        public bool Silent;

        /// <summary>Set during setup to capture one real encoded datagram; cleared afterward.</summary>
        public Action<byte[]>? Capture;

        public SwitchableTransport(IPEndPoint localEndPoint) => this.localEndPoint = localEndPoint;

        public int LocalPort => localEndPoint.Port;

        public event Action<IPEndPoint, string>? SendFailed { add { } remove { } }

        public void Send(IPEndPoint to, ReadOnlySpan<byte> datagram)
        {
            Capture?.Invoke(datagram.ToArray());
            if (!Silent) Peer?.OnDatagram(localEndPoint, datagram);
        }
    }

    /// <summary>Drops everything - the app-facing side of a NetworkCore no [Benchmark] method should
    /// ever exercise (only the plugin/core layer below it is what's being measured here).</summary>
    sealed class NullSink : INetworkSink
    {
        public static readonly NullSink Instance = new();
        public void Deliver<T>(in MessageMeta meta, in T message) where T : struct, IMessage { }
        public void OnEvent(in NetworkEvent e) { }
    }
}
