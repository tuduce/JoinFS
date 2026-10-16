using System.Net;

namespace JoinFS.Tests
{
    public class AddressBookEntryTests
    {
        static AddressBook.AddressBookEntry Entry(string address, IPEndPoint? endPoint = null) =>
            new() { name = address, address = address, endPoint = endPoint ?? new IPEndPoint(0, 0) };

        [Fact]
        public void TwoIpv6Entries_WithNoEndPoint_AreDistinct()
        {
            var saved = Entry("[2001:db8::1]:6112");

            Assert.False(saved.IsSameDestination("[2001:db8::2]:6112", new IPEndPoint(0, 0)));
            Assert.True(saved.IsSameDestination("[2001:db8::1]:6112", new IPEndPoint(0, 0)));
        }

        [Fact]
        public void IPv4Entries_AreTheSame_WhenTheirEndPointsAre_WhateverTheText()
        {
            var saved = Entry("hub.example.org", new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112));

            Assert.True(saved.IsSameDestination("203.0.113.1", new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112)));
            Assert.False(saved.IsSameDestination("203.0.113.2", new IPEndPoint(IPAddress.Parse("203.0.113.2"), 6112)));
        }
    }
}
