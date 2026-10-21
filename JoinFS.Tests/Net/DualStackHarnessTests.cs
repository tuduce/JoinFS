using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The IPv6 half of the in-memory test harness: a node that owns an IPv4 and an IPv6 endpoint, the
    /// stateful IPv6 firewall of a home router, and a cut of IPv6 alone next to Partition.
    /// </summary>
    public class DualStackHarnessTests
    {
        static readonly IPEndPoint A4 = new(IPAddress.Parse("10.0.0.1"), 6112);
        static readonly IPEndPoint A6 = new(IPAddress.Parse("2001:db8::a"), 6112);
        static readonly IPEndPoint B4 = new(IPAddress.Parse("10.0.0.2"), 6112);
        static readonly IPEndPoint B6 = new(IPAddress.Parse("2001:db8::b"), 6112);

        sealed class Sink
        {
            public readonly List<IPEndPoint> From = [];
            public void Receive(IPEndPoint from, byte[] data) => From.Add(from);
        }

        [Fact]
        public void DualStackTransport_SendsFromTheEndpointMatchingTheDestination()
        {
            var network = new InMemoryNetwork();
            var a = network.Attach(A4, A6, (_, _) => { });
            var sink4 = new Sink();
            var sink6 = new Sink();
            network.Attach(B4, sink4.Receive);
            network.Attach(B6, sink6.Receive);

            a.Send(B4, [1]);
            a.Send(B6, [1]);
            network.Pump();

            Assert.Equal([A4], sink4.From);
            Assert.Equal([A6], sink6.From);
        }

        [Fact]
        public void DualStackNode_ReceivesOnBothEndpoints()
        {
            var network = new InMemoryNetwork();
            var received = new Sink();
            network.Attach(A4, A6, received.Receive);
            var b = network.Attach(B4, B6, (_, _) => { });

            b.Send(A4, [1]);
            b.Send(A6, [1]);
            network.Pump();

            Assert.Equal([B4, B6], received.From);
        }

        [Fact]
        public void IPv4OnlyTransport_CannotReachAnIPv6Destination()
        {
            var network = new InMemoryNetwork();
            var received = new Sink();
            network.Attach(B4, B6, received.Receive);
            var a = network.Attach(A4, (_, _) => { });

            a.Send(B6, [1]);

            Assert.Equal(0, network.Pump());
            Assert.Empty(received.From);
        }

        [Fact]
        public void Firewall_DropsUnsolicitedInboundIPv6_UntilTheNodeHasSentToTheSource()
        {
            var network = new InMemoryNetwork();
            var atA = new Sink();
            var a = network.Attach(A4, A6, atA.Receive);
            var atB = new Sink();
            var b = network.Attach(B4, B6, atB.Receive);
            network.FirewallIpv6(A6.Address);

            b.Send(A6, [1]);                  // unsolicited: dropped
            Assert.Equal(0, network.Pump());
            Assert.Empty(atA.From);

            a.Send(B6, [2]);                  // A opens the hole by sending to B
            b.Send(A6, [3]);
            network.Pump();
            Assert.Equal([A6], atB.From);
            Assert.Equal([B6], atA.From);
        }

        [Fact]
        public void Firewall_LeavesIPv4AndOtherAddressesAlone()
        {
            var network = new InMemoryNetwork();
            var atA = new Sink();
            network.Attach(A4, A6, atA.Receive);
            var b = network.Attach(B4, B6, (_, _) => { });
            var c = network.Attach(new IPEndPoint(IPAddress.Parse("2001:db8::c"), 6112), (_, _) => { });
            network.FirewallIpv6(A6.Address);

            b.Send(A4, [1]);                  // IPv4 is not filtered (NAT is a separate matter)
            network.Pump();
            Assert.Equal([B4], atA.From);

            var open = new Sink();
            var d = new IPEndPoint(IPAddress.Parse("2001:db8::d"), 6112);
            network.Attach(d, open.Receive);
            c.Send(d, [1]);                   // a node without a firewall receives freely
            network.Pump();
            Assert.Single(open.From);
        }

        [Fact]
        public void CutIpv6_DropsIPv6BetweenTwoNodes_AndLeavesIPv4()
        {
            var mesh = new TestMesh();
            TestNode a = mesh.AddDualStack("10.0.0.1", "2001:db8::a");
            TestNode b = mesh.AddDualStack("10.0.0.2", "2001:db8::b");
            TestNode c = mesh.AddDualStack("10.0.0.3", "2001:db8::c");
            mesh.CutIpv6(a, b);

            a.Transport.Send(b.EndPoint6!, [1]);
            Assert.Equal(0, mesh.Network.Pump());     // cut, both ways
            b.Transport.Send(a.EndPoint6!, [1]);
            Assert.Equal(0, mesh.Network.Pump());

            a.Transport.Send(b.EndPoint, [1]);        // IPv4 between the same two nodes still flows
            Assert.Equal(1, mesh.Network.Pump());
            a.Transport.Send(c.EndPoint6!, [1]);      // and IPv6 to a third node
            Assert.Equal(1, mesh.Network.Pump());
        }

        [Fact]
        public void LegacyJoinOverIPv6_IsIgnored_AndTheSameNodesJoinOverIPv4()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.AddDualStack("203.0.113.1", "2001:db8::1");
            TestNode a = mesh.AddDualStack("198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");

            a.Core.Mesh.Join(hub.EndPoint6!, 0);
            mesh.Run(5);
            Assert.False(hub.Knows(a));       // the hub's legacy plugin never saw the IPv6 datagram
            Assert.False(a.Knows(hub));
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint6) || d.From.Equals(hub.EndPoint));

            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(5);
            Assert.True(hub.Knows(a));
            Assert.True(a.Knows(hub));
        }
    }
}
