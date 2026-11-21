using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// The exact bytes of the Hello after stage 6 of atc-fsd (docs/network/jfp2/protocol.md Appendix A.19): the Hello of
    /// <see cref="Edition2HelloGoldenTests"/> with the offers of AtcStation (23) and AtcRelay (24) appended after
    /// NotesHistory, so two more offers and nothing else. The earlier golden stays as it is, as the rule of A.18 says.
    /// NEVER change these bytes to make a test pass.
    /// </summary>
    public class AtcHelloGoldenTests
    {
        const string HelloHex =
            "FA 02 08 34 12 00 00 00" +
            "02 02" +
            "01 00 00 00 00 00 00 00" +                    // capabilities: Segments
            "34 12" +
            "00" +
            "20 00" +                                      // thirty-two offers
            "00 07 01 01 00 08 01 01 00 01 01 01 00 02 01 01 00 00 01 01" +   // Status, StatusRequest, Identity, VariableSync, Position
            "00 03 01 01 00 04 01 01 00 05 01 01 00 06 01 01 00 09 01 01" +   // Event, FlightPlan, Notes, Weather, WeatherReply
            "00 0A 01 01 00 0B 01 01 00 0C 01 01 00 0D 01 01" +               // HubList, UserListRequest, HubUser, UserPositionsRequest
            "00 0E 01 01 00 0F 01 01 00 10 01 01 00 11 01 01" +               // UserPositions, Online, UserLookup, UserLookupAnswer
            "00 12 01 01 00 13 01 01 00 14 01 01 00 15 01 01 00 16 01 01" +   // ObjectPosition, RemoveObject, PeerInfo, CommsRequest, NotesHistory
            "00 17 01 01 00 18 01 01" +                                       // AtcStation, AtcRelay
            "01 02 01 01 01 03 01 02 01 04 01 02" +                           // internal: Join, JoinResult [1, 2], MemberList [1, 2]
            "01 05 01 02 01 06 01 01 01 07 01 01 01 08 01 01" +               // MemberAnnounce [1, 2], MemberLeave, RouteQuery, RouteAnswer
            "01 00 10 00 00 01 71 00 CB E0 17 14 02 11 22 33 44 55 66 77" +   // TLV Names: kind 0 first, then the key name
            "02 00 14 00 32 36 2E 36 2E 30 20 4A 6F 69 6E 46 53 2D 46 53 32 30 32 34" +
            "05 00 1C 00" +
            "13 06 20 01 0D B8 00 00 00 00 00 00 00 00 00 00 00 01 E0 17" +
            "07 04 CB 00 71 01 E0 17";

        static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

        static readonly IPEndPoint[] Candidates = [IPEndPoint.Parse("[2001:db8::1]:6112"), IPEndPoint.Parse("203.0.113.1:6112")];

        static byte[] Datagram(byte[] payload)
        {
            var envelope = new Envelope(EnvelopeFlags.Internal, 0x1234, 0, MessageClasses.Hello);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram, header);
            return datagram;
        }

        static HandshakeMessage Hello() => new()
        {
            ProtoMajorMin = 2, ProtoMajorMax = 2, SelfAssignedId = 0x1234,
            Capabilities = Jfp2Profile.Default.Capabilities,
            Offers = [.. Jfp2Profile.Default.Offers],
            Names = [NodeId.FromLegacy(0xCB007101, 6112, 20), NodeId.ReadFrom(Bytes("0211223344556677"))],
            Build = "26.6.0 JoinFS-FS2024",
            Candidates = [.. Candidates],
        };

        [Fact]
        public void TheHelloWithTheAtcClasses_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(HelloHex)), Convert.ToHexString(Datagram(Hello().Serialize())));

        [Fact]
        public void TheHelloWithTheAtcClasses_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloHex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            HandshakeMessage hello = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal(Jfp2Profile.Default.Offers.Count, hello.Offers.Count);
            Assert.Equal(32, hello.Offers.Count);
            Assert.Equal(Candidates, hello.Candidates);
            Assert.Empty(hello.Extensions);
        }

        [Fact]
        public void TheOffersOfTheAtcClasses_AreTheEarlierOffersAndTwoAppendedAfterThem()
        {
            // an older build reads the first 30 offers as before and skips the two it does not know (§5.3)
            IReadOnlyList<SchemaOffer> before = Edition2HelloGoldenTests.Edition2Profile.Offers;
            IReadOnlyList<SchemaOffer> now = Jfp2Profile.Default.Offers;
            Assert.Equal(before.Take(before.Count - 7), now.Take(before.Count - 7)); // up to NotesHistory, the last application offer
            Assert.Equal([MessageClasses.AtcStation, MessageClasses.AtcRelay], now.Skip(before.Count - 7).Take(2).Select(o => o.MessageClass));
            Assert.All(now.Skip(before.Count - 7).Take(2), o => Assert.True(!o.Internal && o.MinVersion == 1 && o.MaxVersion == 1));
            Assert.Equal(before.Skip(before.Count - 7), now.Skip(before.Count - 5)); // the internal offers follow, unchanged
        }
    }
}
