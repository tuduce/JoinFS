using System.Net;
using JoinFS.Net;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>A node of the new network stack on an in-memory network, recording what it delivers to the app.</summary>
    sealed class TestNode : INetworkSink
    {
        public readonly NetworkCore Core;
        public readonly IPEndPoint EndPoint;
        public readonly List<(MessageMeta Meta, object Message)> Received = [];
        public readonly List<NetworkEvent> Events = [];
        public readonly List<string> Logs = [];

        public TestNode(TestMesh mesh, string ip, ushort port, string publicIp, params IProtocolPlugin[] plugins)
        {
            EndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            InMemoryTransport transport = mesh.Network.Attach(EndPoint, (from, data) => Core!.OnDatagram(from, data));
            Core = new NetworkCore(transport, mesh.Clock, this) { IsOpen = true };
            Core.Identity.LocalAddress = EndPoint.Address;
            Core.Identity.InternetAddress = publicIp == null ? EndPoint.Address : IPAddress.Parse(publicIp);
            Core.Identity.Port = port;
            foreach (IProtocolPlugin plugin in plugins.Length > 0 ? plugins : [new LegacyPlugin()])
            {
                Core.AddPlugin(plugin);
            }
        }

        public NodeId Id => Core.Identity.Id;

        public void Deliver<T>(in MessageMeta meta, in T message) where T : struct, IMessage => Received.Add((meta, message));

        public void OnEvent(in NetworkEvent e)
        {
            if (e.Kind != NetworkEventKind.Log) Events.Add(e); else Logs.Add(e.Text);
        }

        public IEnumerable<T> Messages<T>() => Received.Where(r => r.Message is T).Select(r => (T)r.Message);

        public IEnumerable<(MessageMeta Meta, T Message)> MessagesWithMeta<T>() =>
            Received.Where(r => r.Message is T).Select(r => (r.Meta, (T)r.Message));

        public bool Knows(TestNode other) => Core.Peers.TryGet(other.Id, out Peer peer) && peer.SendEstablished;
    }

    /// <summary>Several TestNodes sharing one in-memory network and one manual clock.</summary>
    sealed class TestMesh
    {
        public readonly InMemoryNetwork Network = new();
        public readonly ManualClock Clock = new();
        public readonly List<TestNode> Nodes = [];

        public TestNode Add(string ip, ushort port = 6112, params IProtocolPlugin[] plugins)
        {
            var node = new TestNode(this, ip, port, null, plugins);
            Nodes.Add(node);
            return node;
        }

        /// <summary>A node on a LAN address whose public address (what its id says) is <paramref name="publicIp"/>.</summary>
        public TestNode AddBehindNat(string lanIp, string publicIp, ushort port = 6112, params IProtocolPlugin[] plugins)
        {
            var node = new TestNode(this, lanIp, port, publicIp, plugins);
            Nodes.Add(node);
            return node;
        }

        /// <summary>Advance time in 50 ms steps, ticking every node and delivering all traffic.</summary>
        public void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += 0.05)
            {
                Clock.Advance(0.05);
                foreach (TestNode node in Nodes) node.Core.Tick();
                Network.Pump();
            }
        }

        /// <summary>Drop all traffic between two nodes (both directions).</summary>
        public void Partition(TestNode a, TestNode b)
        {
            var previous = Network.Filter;
            Network.Filter = (from, to, data) =>
                !((from.Equals(a.EndPoint) && to.Equals(b.EndPoint)) || (from.Equals(b.EndPoint) && to.Equals(a.EndPoint)))
                && (previous == null || previous(from, to, data));
        }
    }
}
