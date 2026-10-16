using System.Net;
using JoinFS;

namespace JoinFS.Tests.Session
{
    /// <summary>
    /// Turning what the user typed into the ordered address list a Join dials (design-future.md §4.1,
    /// "Dial"): literals of either family with or without a port, and a hostname resolved to all its
    /// addresses, IPv6 first when this node has IPv6. The resolver is a fake.
    /// </summary>
    public class DialAddressesTests
    {
        const int Default = 6112;

        sealed class FakeDns
        {
            readonly Dictionary<string, string[]> records = [];
            public readonly List<string> Asked = [];

            public FakeDns With(string host, params string[] addresses)
            {
                records[host] = addresses;
                return this;
            }

            public IReadOnlyList<IPAddress> Resolve(string host)
            {
                Asked.Add(host);
                return records.TryGetValue(host, out string[]? list) ? list.Select(IPAddress.Parse).ToList() : [];
            }
        }

        static string[] Dial(string text, bool hasIpv6 = true, FakeDns? dns = null)
        {
            DialAddresses.Result result = DialAddresses.Parse(text, Default, hasIpv6, (dns ?? new FakeDns()).Resolve);
            Assert.Equal(DialAddresses.Failure.None, result.Failure);
            return result.EndPoints.Select(e => e.ToString()).ToArray();
        }

        [Theory]
        [InlineData("[2001:db8::1]:7000", "[2001:db8::1]:7000")]
        [InlineData("[2001:db8::1]", "[2001:db8::1]:6112")]
        [InlineData("2001:db8::1", "[2001:db8::1]:6112")]
        [InlineData("::1", "[::1]:6112")]
        [InlineData("203.0.113.9", "203.0.113.9:6112")]
        [InlineData("203.0.113.9:7000", "203.0.113.9:7000")]
        [InlineData("  203.0.113.9:7000 ", "203.0.113.9:7000")]
        [InlineData("::ffff:203.0.113.9", "203.0.113.9:6112")]
        public void Literals_AreTakenAsTheyAre_AndNeverResolved(string text, string expected)
        {
            var dns = new FakeDns();
            Assert.Equal([expected], Dial(text, dns: dns));
            Assert.Empty(dns.Asked);
        }

        [Fact]
        public void Hostname_WithOnlyARecords_DialsThemInOrder()
        {
            var dns = new FakeDns().With("hub.example", "203.0.113.1", "203.0.113.2");
            Assert.Equal(["203.0.113.1:6112", "203.0.113.2:6112"], Dial("hub.example", dns: dns));
        }

        [Fact]
        public void Hostname_WithOnlyAnAAAARecord_DialsIt()
        {
            var dns = new FakeDns().With("hub.example", "2001:db8::1");
            Assert.Equal(["[2001:db8::1]:7000"], Dial("hub.example:7000", dns: dns));
        }

        [Fact]
        public void Hostname_WithBothFamilies_DialsIPv6First_WhateverOrderTheResolverGave()
        {
            var dns = new FakeDns().With("hub.example", "203.0.113.1", "2001:db8::1", "203.0.113.2", "2001:db8::2");
            Assert.Equal(["[2001:db8::1]:6112", "[2001:db8::2]:6112", "203.0.113.1:6112", "203.0.113.2:6112"], Dial("hub.example", dns: dns));
        }

        [Fact]
        public void Hostname_WithBothFamilies_OnANodeWithoutIPv6_DialsIPv4Only()
        {
            var dns = new FakeDns().With("hub.example", "2001:db8::1", "203.0.113.1");
            Assert.Equal(["203.0.113.1:6112"], Dial("hub.example", hasIpv6: false, dns: dns));
        }

        [Fact]
        public void IPv6Only_OnANodeWithoutIPv6_HasNothingToDial()
        {
            var dns = new FakeDns().With("hub.example", "2001:db8::1");
            Assert.Equal(DialAddresses.Failure.NoAddress, DialAddresses.Parse("hub.example", Default, false, dns.Resolve).Failure);
            Assert.Equal(DialAddresses.Failure.NoAddress, DialAddresses.Parse("[2001:db8::1]:6112", Default, false, dns.Resolve).Failure);
        }

        [Fact]
        public void AddressesRepeatedInTheAnswer_AreDialledOnce()
        {
            var dns = new FakeDns().With("hub.example", "203.0.113.1", "::ffff:203.0.113.1", "203.0.113.1");
            Assert.Equal(["203.0.113.1:6112"], Dial("hub.example", dns: dns));
        }

        [Theory]
        [InlineData("unknown.example")]
        [InlineData("")]
        [InlineData("[2001:db8::1")]
        [InlineData("[2001:db8::1]junk")]
        [InlineData("[hub.example]:6112")]
        [InlineData("1:2:3:4:5:6:7:8:9")]
        public void NothingResolvable_IsNoAddress(string text)
        {
            DialAddresses.Result result = DialAddresses.Parse(text, Default, true, new FakeDns().Resolve);
            Assert.Equal(DialAddresses.Failure.NoAddress, result.Failure);
            Assert.Empty(result.EndPoints);
        }

        [Theory]
        [InlineData("hub.example:abc", "abc")]
        [InlineData("[2001:db8::1]:abc", "abc")]
        [InlineData("203.0.113.9:70000", "70000")]
        [InlineData("203.0.113.9:0", "0")]
        public void BadPort_IsReportedWithTheText(string text, string port)
        {
            DialAddresses.Result result = DialAddresses.Parse(text, Default, true, new FakeDns().Resolve);
            Assert.Equal(DialAddresses.Failure.InvalidPort, result.Failure);
            Assert.Equal(port, result.BadPort);
        }
    }
}
