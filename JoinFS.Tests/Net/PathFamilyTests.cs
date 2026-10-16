using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>The address family label the session views show for a peer's path.</summary>
    public class PathFamilyTests
    {
        static readonly IPEndPoint Ipv6 = new(IPAddress.Parse("2001:db8::1"), 6112);
        static readonly IPEndPoint Ipv4 = new(IPAddress.Parse("203.0.113.5"), 6112);

        [Fact]
        public void Ipv6Endpoint_IsLabelledIpv6()
        {
            Assert.Equal("IPv6", PathFamily.Label(Ipv6));
        }

        [Fact]
        public void Ipv4Endpoint_IsLabelledIpv4()
        {
            Assert.Equal("IPv4", PathFamily.Label(Ipv4));
        }

        [Fact]
        public void NoEndpointOrNoAddress_HasNoLabel()
        {
            Assert.Equal("", PathFamily.Label(null));
            Assert.Equal("", PathFamily.Label(new IPEndPoint(IPAddress.Any, 0)));
            Assert.Equal("", PathFamily.Label(new IPEndPoint(IPAddress.IPv6Any, 0)));
        }

        [Fact]
        public void Shown_IsTheFamilyOnlyOnADualStackNode()
        {
            PeerSnapshot v6 = new() { EndPoint = Ipv6, RouteEndPoint = Ipv6 };
            Assert.Equal("IPv6", PathFamily.Shown(true, v6));
            Assert.Equal("", PathFamily.Shown(false, v6));
            Assert.Equal("", PathFamily.Shown(true, null));
            Assert.Equal("", PathFamily.Shown(true, new PeerSnapshot()));
        }

        [Fact]
        public void PeerSnapshot_ReportsTheFamilyOfTheRoute()
        {
            Assert.Equal("IPv6", new PeerSnapshot { EndPoint = Ipv6, RouteEndPoint = Ipv6 }.Family);
            Assert.Equal("IPv4", new PeerSnapshot { EndPoint = Ipv4, RouteEndPoint = Ipv4 }.Family);
            // reached through an IPv4 relay: the first hop is IPv4 whatever the peer's own address
            Assert.Equal("IPv4", new PeerSnapshot { EndPoint = Ipv6, RouteEndPoint = Ipv4 }.Family);
            Assert.Equal("", new PeerSnapshot().Family);
        }
    }
}
