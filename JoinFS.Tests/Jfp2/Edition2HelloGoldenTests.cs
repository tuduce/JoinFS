using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using Xunit;

namespace JoinFS.Tests.Jfp2
{
    /// <summary>
    /// The exact bytes of the whole Hello of edition 2 (docs/network/jfp2/protocol.md Appendix A.18): every offer of
    /// <see cref="Jfp2Profile.Default"/> in its order, the Segments capability, both names of a node that has a legacy id
    /// and a key, the build and the candidates. The Hello of 26.6 (<see cref="HandshakeGoldenTests"/>) is untouched, and
    /// the parts of this Hello are pinned one by one in <see cref="AdmissionGoldenTests"/> and <see cref="CandidatesGoldenTests"/>.
    /// What a release sends is a permanent part of the specification: a new class, tag or capability appends to this
    /// Hello in a new golden and never changes these bytes. NEVER change them to make a test pass.
    /// </summary>
    public class Edition2HelloGoldenTests
    {
        const string HelloHex =
            "FA 02 08 34 12 00 00 00" +
            "02 02" +
            "01 00 00 00 00 00 00 00" +                    // capabilities: Segments
            "34 12" +
            "00" +
            "1E 00" +                                      // thirty offers
            "00 07 01 01 00 08 01 01 00 01 01 01 00 02 01 01 00 00 01 01" +   // Status, StatusRequest, Identity, VariableSync, Position
            "00 03 01 01 00 04 01 01 00 05 01 01 00 06 01 01 00 09 01 01" +   // Event, FlightPlan, Notes, Weather, WeatherReply
            "00 0A 01 01 00 0B 01 01 00 0C 01 01 00 0D 01 01" +               // HubList, UserListRequest, HubUser, UserPositionsRequest
            "00 0E 01 01 00 0F 01 01 00 10 01 01 00 11 01 01" +               // UserPositions, Online, UserLookup, UserLookupAnswer
            "00 12 01 01 00 13 01 01 00 14 01 01 00 15 01 01 00 16 01 01" +   // ObjectPosition, RemoveObject, PeerInfo, CommsRequest, NotesHistory
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
        public void TheHelloOfEdition2_IsWrittenExactly() =>
            Assert.Equal(Convert.ToHexString(Bytes(HelloHex)), Convert.ToHexString(Datagram(Hello().Serialize())));

        [Fact]
        public void TheHelloOfEdition2_IsReadExactly()
        {
            byte[] datagram = Bytes(HelloHex);
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            HandshakeMessage hello = HandshakeMessage.Deserialize(datagram.AsSpan(header));
            Assert.Equal(Jfp2Capabilities.Segments, hello.Capabilities);
            Assert.Equal(Jfp2Profile.Default.Offers.Count, hello.Offers.Count);
            Assert.Equal(2, hello.Names.Count);
            Assert.Equal(Candidates, hello.Candidates);
            Assert.Empty(hello.Extensions);
        }

        [Fact]
        public void TheOffersOfEdition2_KeepEveryClassOf26_6AtTheSameVersion()
        {
            // a node of 26.6 offers Position .. WeatherReply (application 0 to 9) at [1, 1]; edition 2 adds, and never changes
            var offers = Jfp2Profile.Default.Offers.Where(o => !o.Internal && o.MessageClass <= MessageClasses.WeatherReply).ToList();
            Assert.Equal(10, offers.Count);
            Assert.All(offers, o => Assert.True(o.MinVersion == 1 && o.MaxVersion == 1));
        }
    }
}
