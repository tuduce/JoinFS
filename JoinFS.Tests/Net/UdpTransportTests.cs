using System.Buffers;
using System.Net;
using System.Net.Sockets;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The real UDP socket on loopback: one dual-mode socket serves IPv6 and IPv4 peers on the one
    /// port, IPv4 sources are reported as plain IPv4, and with IPv6 off the node is IPv4-only.
    /// </summary>
    public class UdpTransportTests
    {
        sealed class Inbox
        {
            readonly object gate = new();
            readonly List<(IPEndPoint From, byte[] Data)> items = [];

            public void Add(IPEndPoint from, byte[] buffer, int length)
            {
                lock (gate) items.Add((from, buffer.AsSpan(0, length).ToArray()));
                ArrayPool<byte>.Shared.Return(buffer);
            }

            public (IPEndPoint From, byte[] Data)? WaitForOne(double seconds = 3)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    lock (gate) if (items.Count > 0) return items[0];
                    Thread.Sleep(5);
                }
                return null;
            }
        }

        static UdpTransport Start(bool ipv6, Inbox inbox)
        {
            var transport = new UdpTransport { Ipv6Enabled = ipv6 };
            transport.Received += inbox.Add;
            Assert.True(transport.Open(0, out string error), error);
            return transport;
        }

        static byte[] Receive(Socket client)
        {
            client.ReceiveTimeout = 3000;
            byte[] buffer = new byte[16];
            int n = client.Receive(buffer);
            return buffer.AsSpan(0, n).ToArray();
        }

        [Fact]
        public void DualMode_ReceivesFromIPv6AndIPv4_OnOnePort()
        {
            if (!Socket.OSSupportsIPv6) return; // the machine has no IPv6 stack: nothing to show
            var inbox6 = new Inbox();
            using UdpTransport transport = Start(true, inbox6);
            Assert.True(transport.IsDualStack);

            using var client6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            client6.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            client6.SendTo([6], new IPEndPoint(IPAddress.IPv6Loopback, transport.LocalPort));
            var got6 = inbox6.WaitForOne();
            Assert.NotNull(got6);
            Assert.Equal(AddressFamily.InterNetworkV6, got6!.Value.From.AddressFamily);
            Assert.Equal(IPAddress.IPv6Loopback, got6.Value.From.Address);
            Assert.Equal(((IPEndPoint)client6.LocalEndPoint!).Port, got6.Value.From.Port);

            // the reply goes back to the IPv6 sender
            transport.Send(got6.Value.From, [66]);
            Assert.Equal(new byte[] { 66 }, Receive(client6));
        }

        [Fact]
        public void DualMode_IPv4Sender_IsReportedAsPlainIPv4_AndAnswered()
        {
            var inbox = new Inbox();
            using UdpTransport transport = Start(true, inbox);

            using var client4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client4.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            client4.SendTo([4], new IPEndPoint(IPAddress.Loopback, transport.LocalPort));
            var got = inbox.WaitForOne();
            Assert.NotNull(got);
            Assert.Equal(AddressFamily.InterNetwork, got!.Value.From.AddressFamily);
            Assert.False(got.Value.From.Address.IsIPv4MappedToIPv6);
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)client4.LocalEndPoint!).Port), got.Value.From);

            // sending to a plain IPv4 endpoint works through the dual-mode socket
            transport.Send(got.Value.From, [44]);
            Assert.Equal(new byte[] { 44 }, Receive(client4));
        }

        [Fact]
        public void Ipv6Off_RunsIPv4Only()
        {
            var inbox = new Inbox();
            using UdpTransport transport = Start(false, inbox);
            Assert.False(transport.IsDualStack);
            Assert.Null(transport.FallbackReason); // switched off by choice, nothing to report

            using var client4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client4.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            client4.SendTo([4], new IPEndPoint(IPAddress.Loopback, transport.LocalPort));
            var got = inbox.WaitForOne();
            Assert.NotNull(got);
            Assert.Equal(AddressFamily.InterNetwork, got!.Value.From.AddressFamily);

            if (!Socket.OSSupportsIPv6) return;
            // an IPv6 datagram to the same port never arrives
            var inbox6 = new Inbox();
            using UdpTransport v4only = Start(false, inbox6);
            using var client6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            client6.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            client6.SendTo([6], new IPEndPoint(IPAddress.IPv6Loopback, v4only.LocalPort));
            Assert.Null(inbox6.WaitForOne(0.3));
        }

        [Fact]
        public void DualModeUnavailable_FallsBackToIPv4_AndSaysWhy()
        {
            var inbox = new Inbox();
            var transport = new UdpTransport { Ipv6Enabled = true, DualModeFactory = () => throw new SocketException((int)SocketError.AddressFamilyNotSupported) };
            using (transport)
            {
                transport.Received += inbox.Add;
                Assert.True(transport.Open(0, out string error), error);
                Assert.False(transport.IsDualStack);
                Assert.False(string.IsNullOrEmpty(transport.FallbackReason));

                using var client4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                client4.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                client4.SendTo([4], new IPEndPoint(IPAddress.Loopback, transport.LocalPort));
                Assert.NotNull(inbox.WaitForOne());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SendToAnUnsetEndPoint_IsDroppedSilently(bool ipv6)
        {
            using UdpTransport transport = Start(ipv6, new Inbox());
            var failures = new List<string>();
            transport.SendFailed += (to, why) => failures.Add(to + ": " + why);

            transport.Send(new IPEndPoint(0, 0), new byte[] { 1 });
            transport.Send(new IPEndPoint(IPAddress.Loopback, 0), new byte[] { 1 });
            transport.Send(new IPEndPoint(IPAddress.IPv6Any, 0), new byte[] { 1 });

            Assert.Empty(failures);
        }

        [Fact]
        public void LocalIpv6Candidate_IsTheFoundAddressWithTheSocketsPort_AndIsAskedForOnlyEvery30Seconds()
        {
            int lookups = 0;
            var transport = new UdpTransport { Ipv6Enabled = true, LocalIpv6Finder = () => { lookups++; return IPAddress.Parse("2001:db8::2"); } };
            using (transport)
            {
                Assert.True(transport.Open(0, out _));
                if (!transport.IsDualStack)
                {
                    return; // no IPv6 stack here
                }
                Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::2"), transport.LocalPort), transport.LocalIpv6Candidate);
                Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::2"), transport.LocalPort), transport.LocalIpv6Candidate);
                Assert.Equal(1, lookups);
            }
        }

        [Fact]
        public void LocalIpv6Candidate_IsNull_WithoutAGlobalIpv6Address_AndOnAnIPv4OnlySocket()
        {
            var noRoute = new UdpTransport { Ipv6Enabled = true, LocalIpv6Finder = () => null };
            using (noRoute)
            {
                Assert.True(noRoute.Open(0, out _));
                Assert.Null(noRoute.LocalIpv6Candidate);
            }
            var ipv4Only = new UdpTransport { Ipv6Enabled = false, LocalIpv6Finder = () => IPAddress.Parse("2001:db8::2") };
            using (ipv4Only)
            {
                Assert.True(ipv4Only.Open(0, out _));
                Assert.Null(ipv4Only.LocalIpv6Candidate);
            }
        }

        [Theory]
        [InlineData("2001:db8::1", true)]
        [InlineData("2a02:1234::5", true)]
        [InlineData("fd00::1", true)]
        [InlineData("fe80::1", false)]
        [InlineData("::1", false)]
        [InlineData("::", false)]
        [InlineData("ff02::1", false)]
        [InlineData("::ffff:203.0.113.9", false)]
        [InlineData("203.0.113.9", false)]
        public void Ipv6Address_IsGlobal_OnlyForARoutableIPv6Address(string address, bool expected) =>
            Assert.Equal(expected, Ipv6Address.IsGlobal(IPAddress.Parse(address)));
    }
}
