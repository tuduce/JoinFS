using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>
    /// ObservedEndPoints: what neighbors' observations of this node's endpoint say about its NAT
    /// (docs/jfp2/implementation.md §3.2). This node: interface 192.168.1.20, bound to port 6112.
    /// </summary>
    public class ObservedEndPointsTests
    {
        static readonly IPAddress Local = IPAddress.Parse("192.168.1.20");
        const ushort Bound = 6112;

        static NodeId Reporter(uint n) => new(0xCB007100 + n, 6112, (byte)n);

        static IPEndPoint At(string address, int port) => new(IPAddress.Parse(address), port);

        static ObservedEndPoints Seen(IPAddress local, params IPEndPoint[] observations)
        {
            var observed = new ObservedEndPoints();
            for (int n = 0; n < observations.Length; n++)
            {
                observed.Observe(Reporter((uint)n + 1), observations[n], local, Bound);
            }
            return observed;
        }

        [Fact]
        public void NoObservation_IsUnknown()
        {
            var observed = new ObservedEndPoints();
            Assert.Equal(NatClass.Unknown, observed.Class);
            Assert.Contains("no public observation", observed.Describe(IPAddress.Parse("198.51.100.9"), Bound));
        }

        /// <summary>Peers see our own interface address at the bound port: no NAT.</summary>
        [Fact]
        public void OwnAddressAtTheBoundPort_IsPublic()
        {
            IPAddress publicInterface = IPAddress.Parse("198.51.100.9");
            Assert.Equal(NatClass.Public, Seen(publicInterface, At("198.51.100.9", 6112), At("198.51.100.9", 6112)).Class);
        }

        [Fact]
        public void AnotherAddressAtTheBoundPort_IsPortPreserving() =>
            Assert.Equal(NatClass.PortPreserving, Seen(Local, At("198.51.100.9", 6112), At("198.51.100.9", 6112)).Class);

        [Fact]
        public void AnotherPortTheSameForEveryone_IsTranslated() =>
            Assert.Equal(NatClass.Translated, Seen(Local, At("198.51.100.9", 40001), At("198.51.100.9", 40001)).Class);

        /// <summary>Two neighbors see two ports (or addresses): the NAT maps per destination, and this node depends on relays.</summary>
        [Fact]
        public void DifferentEndPointsForDifferentPeers_IsEndpointDependent()
        {
            Assert.Equal(NatClass.EndpointDependent, Seen(Local, At("198.51.100.9", 40001), At("198.51.100.9", 40002)).Class);
            Assert.Equal(NatClass.EndpointDependent, Seen(Local, At("198.51.100.9", 6112), At("198.51.100.10", 6112)).Class);
        }

        /// <summary>A neighbor on our own network sees a LAN address, which says nothing about the way out.</summary>
        [Theory]
        [InlineData("192.168.1.20")] // our own private address
        [InlineData("10.1.2.3")]
        [InlineData("172.20.0.4")]
        [InlineData("100.64.1.1")]   // shared address space (CGNAT)
        [InlineData("127.0.0.1")]
        [InlineData("169.254.3.4")]
        [InlineData("fd00::5")]
        [InlineData("fe80::1")]
        public void LanObservation_IsIgnored(string address)
        {
            var observed = new ObservedEndPoints();
            Assert.False(observed.Observe(Reporter(1), At(address, 6112), Local, Bound));
            Assert.Equal(NatClass.Unknown, observed.Class);
            Assert.Equal(0, observed.Reporters);
        }

        /// <summary>"Our /24" counts as LAN even when the LAN is publicly addressed; our own interface address does not.</summary>
        [Fact]
        public void AnotherAddressInOurSlash24_IsIgnored()
        {
            IPAddress publicInterface = IPAddress.Parse("198.51.100.9");
            Assert.True(ObservedEndPoints.IsLan(IPAddress.Parse("198.51.100.77"), publicInterface));
            Assert.False(ObservedEndPoints.IsLan(IPAddress.Parse("198.51.100.9"), publicInterface));
            Assert.False(ObservedEndPoints.IsLan(IPAddress.Parse("198.51.101.9"), publicInterface));
        }

        /// <summary>A neighbor's later observation replaces its earlier one, and only a change is worth a log line.</summary>
        [Fact]
        public void LatestObservationPerReporter_Counts_AndOnlyChangesReport()
        {
            var observed = new ObservedEndPoints();
            Assert.True(observed.Observe(Reporter(1), At("198.51.100.9", 40001), Local, Bound));   // Unknown -> Translated
            Assert.True(observed.Observe(Reporter(2), At("198.51.100.9", 40001), Local, Bound));   // same class and endpoint, one more reporter
            Assert.False(observed.Observe(Reporter(2), At("198.51.100.9", 40001), Local, Bound)); // nothing new
            Assert.True(observed.Observe(Reporter(1), At("198.51.100.9", 40002), Local, Bound));   // now two endpoints
            Assert.Equal(NatClass.EndpointDependent, observed.Class);
            Assert.True(observed.Forget(Reporter(2), Local, Bound));                              // reporter 2 left
            Assert.Equal(NatClass.Translated, observed.Class);
            Assert.Equal([At("198.51.100.9", 40002)], observed.EndPoints);
            Assert.False(observed.Forget(Reporter(9), Local, Bound));
        }

        [Fact]
        public void AtMostEightReporters_TheOldestGoes()
        {
            var observed = new ObservedEndPoints();
            observed.Observe(Reporter(1), At("198.51.100.9", 1), Local, Bound);
            for (uint n = 2; n <= 9; n++)
            {
                observed.Observe(Reporter(n), At("198.51.100.9", 40001), Local, Bound);
            }

            Assert.Equal(ObservedEndPoints.MaxReporters, observed.Reporters);
            Assert.Equal(NatClass.Translated, observed.Class); // reporter 1's odd port is gone
        }

        /// <summary>
        /// A reporter beyond the eight most recent is still kept: the plugin reports an observation only
        /// when it changes, so one dropped here would not come back when a recent reporter leaves.
        /// </summary>
        [Fact]
        public void ReporterBeyondTheEightMostRecent_ComesBackWhenARecentOneLeaves()
        {
            var observed = new ObservedEndPoints();
            observed.Observe(Reporter(1), At("198.51.100.9", 1), Local, Bound);
            for (uint n = 2; n <= 9; n++)
            {
                observed.Observe(Reporter(n), At("198.51.100.9", 40001), Local, Bound);
            }
            Assert.Equal(NatClass.Translated, observed.Class);

            Assert.True(observed.Forget(Reporter(9), Local, Bound)); // reporter 1 is among the eight again
            Assert.Equal(NatClass.EndpointDependent, observed.Class);

            for (uint n = 2; n <= 8; n++)
            {
                observed.Forget(Reporter(n), Local, Bound);
            }
            Assert.Equal(NatClass.Translated, observed.Class); // reporter 1 alone
            Assert.Equal([At("198.51.100.9", 1)], observed.EndPoints);
        }

        /// <summary>The log line's reporter count follows the count, not only the class.</summary>
        [Fact]
        public void ANewReporter_IsAChangeWorthALogLine()
        {
            var observed = new ObservedEndPoints();
            Assert.True(observed.Observe(Reporter(1), At("198.51.100.9", 40001), Local, Bound));
            Assert.True(observed.Observe(Reporter(2), At("198.51.100.9", 40001), Local, Bound));
            Assert.False(observed.Observe(Reporter(2), At("198.51.100.9", 40001), Local, Bound));
            Assert.Contains("(2 of up to 8 peers)", observed.Describe(Local, Bound));
        }

        /// <summary>An IPv4-mapped address (a dual-mode socket) is the IPv4 one.</summary>
        [Fact]
        public void MappedAddress_IsTheIPv4One()
        {
            ObservedEndPoints observed = Seen(Local, At("::ffff:198.51.100.9", 40001), At("198.51.100.9", 40001));
            Assert.Equal(NatClass.Translated, observed.Class);
            Assert.Equal([At("198.51.100.9", 40001)], observed.EndPoints);
        }

        [Fact]
        public void Describe_NamesTheClassTheEndPointsAndTheHttpAddress()
        {
            ObservedEndPoints observed = Seen(Local, At("198.51.100.9", 40001));
            string line = observed.Describe(IPAddress.Parse("198.51.100.9"), Bound);
            Assert.Contains("Translated", line);
            Assert.Contains("198.51.100.9:40001", line);
            Assert.Contains("HTTP address 198.51.100.9", line);
            Assert.Contains("bound port 6112", line);
        }
    }
}
