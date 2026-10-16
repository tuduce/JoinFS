using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>The body of the internal class PeerCandidates (docs/jfp2/protocol.md §9.8): a list of (member name, endpoint candidates).</summary>
    public class PeerCandidatesCodecTests
    {
        static readonly PeerCandidatesV1Codec Codec = new();

        static IPEndPoint V6(int n, int port = 6112) => new(IPAddress.Parse("2001:db8::" + n.ToString("x")), port);

        static NodeId Node(int n) => new(IPAddress.Parse("203.0.113." + n), 6112, 1);

        static PeerCandidateEntry Entry(int n, params IPEndPoint[] candidates) => new() { Node = Node(n), Candidates = [.. candidates] };

        static PeerCandidates Message(params PeerCandidateEntry[] entries) => new() { Entries = [.. entries] };

        static byte[] Encode(in PeerCandidates message)
        {
            byte[] buffer = new byte[16384];
            int length = Codec.Encode(message, buffer);
            return buffer[..length];
        }

        static byte[] Name(int n)
        {
            byte[] name = new byte[NodeName.WireSize];
            NodeName.FromLegacy(Node(n)).WriteTo(name);
            return name;
        }

        [Fact]
        public void IsClass10OfTheInternalPartition_AtVersion1()
        {
            Assert.Equal(10, Codec.MessageClass);
            Assert.Equal(MessageClasses.PeerCandidates, Codec.MessageClass);
            Assert.Equal(1, Codec.SchemaVersion);
            Assert.Equal(MessageKind.PeerCandidates, PeerCandidates.Kind);
        }

        [Fact]
        public void RoundTrips_EntriesWithTheirCandidates_InOrder()
        {
            PeerCandidates message = Message(Entry(2, V6(2), V6(0x22, 40000)), Entry(3, V6(3)));

            PeerCandidates back = Codec.Decode(Encode(message));

            Assert.Equal(2, back.Entries.Count);
            Assert.Equal(Node(2), back.Entries[0].Node);
            Assert.Equal([V6(2), V6(0x22, 40000)], back.Entries[0].Candidates);
            Assert.Equal(Node(3), back.Entries[1].Node);
            Assert.Equal([V6(3)], back.Entries[1].Candidates);
        }

        [Fact]
        public void Layout_IsCountThenNameCandidateCountAndLengthPrefixedEndPoints()
        {
            byte[] bytes = Encode(Message(Entry(2, V6(2))));

            // Count 1; the name; one candidate; length 19 (family 6, 16 address bytes, port LE); 6112 = 0x17E0
            byte[] expected = [1, .. Name(2), 1, 19, 6, 0x20, 0x01, 0x0d, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0xE0, 0x17];
            Assert.Equal(expected, bytes);
        }

        [Fact]
        public void AnEntryWithNoCandidates_RoundTrips_AndMeansClear()
        {
            byte[] bytes = Encode(Message(Entry(2), Entry(3, V6(3))));

            PeerCandidates back = Codec.Decode(bytes);

            Assert.Equal(2, back.Entries.Count);
            Assert.Equal(Node(2), back.Entries[0].Node);
            Assert.Empty(back.Entries[0].Candidates);
            Assert.Equal([V6(3)], back.Entries[1].Candidates);
        }

        [Fact]
        public void AnEmptyMessage_RoundTripsToNoEntries()
        {
            Assert.Equal([0], Encode(new PeerCandidates { Entries = [] }));
            Assert.Empty(Codec.Decode([0]).Entries);
        }

        [Fact]
        public void AtMostFourCandidatesPerEntryAreWritten()
        {
            PeerCandidates back = Codec.Decode(Encode(Message(Entry(2, V6(1), V6(2), V6(3), V6(4), V6(5), V6(6)))));

            Assert.Equal([V6(1), V6(2), V6(3), V6(4)], back.Entries[0].Candidates);
        }

        [Fact]
        public void TheLargestMessage_IsTwelveEntriesOfFourIPv6Candidates_1069Bytes_WithinThePayloadCeiling()
        {
            PeerCandidateEntry[] entries = Enumerable.Range(1, PeerCandidatesV1Codec.MaxEntries)
                .Select(n => Entry(n, V6(1), V6(2), V6(3), V6(4))).ToArray();

            byte[] bytes = Encode(Message(entries));

            Assert.Equal(1069, bytes.Length);
            Assert.Equal(PeerCandidatesV1Codec.MaxSize, bytes.Length);
            Assert.True(bytes.Length <= Jfp2Limits.MaxPayloadSize);
            Assert.Equal(12, Codec.Decode(bytes).Entries.Count);
        }

        [Fact]
        public void AMessageOfMoreThanTwelveEntries_IsCutToTwelve_BySplittingItIsTheSendersJob()
        {
            PeerCandidates back = Codec.Decode(Encode(Message(Enumerable.Range(1, 20).Select(n => Entry(n, V6(n))).ToArray())));

            Assert.Equal(12, back.Entries.Count);
        }

        [Fact]
        public void Split_CutsALongListIntoMessagesOfAtMostTwelveEntries_KeepingOrderAndEntries()
        {
            List<PeerCandidateEntry> entries = Enumerable.Range(1, 30).Select(n => Entry(n, V6(n))).ToList();

            List<PeerCandidates> parts = PeerCandidatesV1Codec.Split(new PeerCandidates { Entries = entries });

            Assert.Equal([12, 12, 6], parts.Select(p => p.Entries.Count));
            Assert.Equal(entries.Select(e => e.Node), parts.SelectMany(p => p.Entries).Select(e => e.Node));
            Assert.All(parts, p => Assert.True(Encode(p).Length <= Jfp2Limits.MaxPayloadSize));
        }

        [Fact]
        public void Split_OfAShortList_IsTheListItself()
        {
            PeerCandidates message = Message(Entry(2, V6(2)));

            Assert.Same(message.Entries, Assert.Single(PeerCandidatesV1Codec.Split(message)).Entries);
        }

        [Fact]
        public void Split_OfNoEntries_IsNoMessage()
        {
            Assert.Empty(PeerCandidatesV1Codec.Split(new PeerCandidates { Entries = [] }));
        }

        [Fact]
        public void ACandidateOfAnUnassignedFamily_IsSkippedByItsLength_AndTheOthersAreRead()
        {
            // one entry, three candidates: IPv6, family 9 with 5 value bytes, IPv6
            byte[] ipv6 = WireEndPoint.ToBytes(V6(7));
            byte[] bytes = [1, .. Name(2), 3, (byte)ipv6.Length, .. ipv6, 6, 9, 1, 2, 3, 4, 5, (byte)ipv6.Length, .. WireEndPoint.ToBytes(V6(8))];

            PeerCandidates back = Codec.Decode(bytes);

            Assert.Equal([V6(7), V6(8)], Assert.Single(back.Entries).Candidates);
        }

        [Fact]
        public void ACandidateShorterThanItsFamilyNeeds_IsSkipped()
        {
            byte[] ipv6 = WireEndPoint.ToBytes(V6(7));
            byte[] bytes = [1, .. Name(2), 2, 5, 6, 1, 2, 3, 4, (byte)ipv6.Length, .. ipv6];

            Assert.Equal([V6(7)], Assert.Single(Codec.Decode(bytes).Entries).Candidates);
        }

        [Fact]
        public void ACandidateLongerThanItsFamily_IsReadUpToTheFamilysSize()
        {
            byte[] ipv6 = WireEndPoint.ToBytes(V6(7));
            byte[] bytes = [1, .. Name(2), 1, (byte)(ipv6.Length + 3), .. ipv6, 0xAA, 0xBB, 0xCC];

            Assert.Equal([V6(7)], Assert.Single(Codec.Decode(bytes).Entries).Candidates);
        }

        [Fact]
        public void AnIPv4Candidate_IsCarriedAsFamily4()
        {
            var v4 = new IPEndPoint(IPAddress.Parse("198.51.100.7"), 6112);

            PeerCandidates back = Codec.Decode(Encode(Message(Entry(2, v4))));

            Assert.Equal([v4], Assert.Single(back.Entries).Candidates);
        }

        [Fact]
        public void AnEntryWhoseNameIsOfAnUnknownKind_IsSkipped_NotTheOnesAfterIt()
        {
            byte[] stranger = [1, 9, 9, 9, 9, 9, 9, 9];     // kind 1: a name this build cannot resolve
            byte[] ipv6 = WireEndPoint.ToBytes(V6(7));
            byte[] bytes = [2, .. stranger, 1, (byte)ipv6.Length, .. ipv6, .. Name(3), 1, (byte)ipv6.Length, .. ipv6];

            PeerCandidates back = Codec.Decode(bytes);

            Assert.Equal(Node(3), Assert.Single(back.Entries).Node);
        }

        [Fact]
        public void AnEntryWithNoNode_IsSkipped()
        {
            byte[] none = new byte[8];
            byte[] ipv6 = WireEndPoint.ToBytes(V6(7));
            byte[] bytes = [1, .. none, 1, (byte)ipv6.Length, .. ipv6];

            Assert.Empty(Codec.Decode(bytes).Entries);
        }

        [Fact]
        public void APayloadThatEndsInsideAnEntry_KeepsTheEntriesBeforeIt_AndDoesNotThrow()
        {
            byte[] full = Encode(Message(Entry(2, V6(2)), Entry(3, V6(3), V6(4))));

            foreach (int cut in new[] { full.Length - 1, full.Length - 10, 1 + Name(2).Length + 1 + 20 + 4, 1 + 3 })
            {
                PeerCandidates back = Codec.Decode(full[..cut]);
                Assert.True(back.Entries.Count <= 1);
                if (back.Entries.Count == 1) Assert.Equal(Node(2), back.Entries[0].Node);
            }
            Assert.Empty(Codec.Decode([]).Entries);
        }

        [Fact]
        public void ACountLargerThanTwelve_ReadsOnlyTwelveEntries()
        {
            List<byte> bytes = [20];
            for (int n = 1; n <= 20; n++)
            {
                bytes.AddRange(Name(n));
                bytes.Add(0);
            }

            Assert.Equal(12, Codec.Decode(bytes.ToArray()).Entries.Count);
        }

        [Fact]
        public void DefaultProfile_OffersTheClassInTheInternalPartition_OnlyForNodesWithIPv6()
        {
            ClassDescriptor<PeerCandidates> descriptor = Jfp2Profile.Default.ForKind<PeerCandidates>(MessageKind.PeerCandidates);

            Assert.NotNull(descriptor);
            Assert.True(descriptor.Internal);
            Assert.True(descriptor.Guaranteed);
            Assert.True(descriptor.RequiresIpv6);
            Assert.Equal((true, MessageClasses.PeerCandidates, (byte)1, (byte)1),
                (descriptor.Offer.Internal, descriptor.Offer.MessageClass, descriptor.Offer.MinVersion, descriptor.Offer.MaxVersion));
            Assert.Same(descriptor, Jfp2Profile.Default.ForInternalClass(MessageClasses.PeerCandidates));
            Assert.Null(Jfp2Profile.Default.ForClass(MessageClasses.PeerCandidates));   // not an application class
        }
    }
}
