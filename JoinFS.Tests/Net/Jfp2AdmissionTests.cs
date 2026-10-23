using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Admission (docs/network/jfp2/protocol.md §5.9): two nodes that have never met through legacy start a JFP2
    /// conversation. The nodes register no legacy plugin, so a legacy datagram between them is impossible, and the
    /// network log says so too.
    /// </summary>
    public class Jfp2AdmissionTests
    {
        static TestNode Node(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new Jfp2Plugin());

        static TestNode SessionHub(TestMesh mesh, string ip = "203.0.113.1")
        {
            TestNode hub = Node(mesh, ip);
            hub.Core.Mesh.Create(false, 0, false, "");
            return hub;
        }

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static IEnumerable<(IPEndPoint From, IPEndPoint To, byte[] Data)> Datagrams(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Where(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint));

        static bool IsClass(byte[] data, byte messageClass) => data[0] == Envelope.Magic && data[1] == 2 && (data[2] & 0x08) != 0 && data[7] == messageClass;

        /// <summary>A Hello a hand-made stranger sends: <paramref name="admission"/> null for none, empty to ask, 16 bytes to echo a cookie.</summary>
        static byte[] StrangerHello(NodeId name, ushort id, byte[]? admission, bool withOffers = true)
        {
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = 2, ProtoMajorMax = 2, SelfAssignedId = id, Names = [name], Admission = admission, Build = withOffers ? "26.6.0 JoinFS-FS2024" : null,
                Offers = withOffers ? [new SchemaOffer(false, MessageClasses.Position, 1, 1), new SchemaOffer(false, MessageClasses.Status, 1, 1)] : [],
            };
            byte[] payload = hello.Serialize();
            var envelope = new Envelope(EnvelopeFlags.Internal, id, 0, MessageClasses.Hello);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram, header);
            return datagram;
        }

        static HandshakeMessage ReadAck(byte[] datagram)
        {
            Assert.True(Envelope.TryReadFrom(datagram, out _, out int header, out _));
            return HandshakeMessage.Deserialize(datagram.AsSpan(header));
        }

        [Fact]
        public void TwoNodes_HandshakeWithNoLegacyBetweenThem()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            Assert.True(Jfp2Of(a).Introduce(hub.EndPoint));
            mesh.Run(1);

            Assert.True(Jfp2Of(a).HasNonMemberSession(hub.Id));
            Assert.True(Jfp2Of(hub).HasNonMemberSession(a.Id));
            Assert.Equal(0, Jfp2Of(a).IntroductionCount);
            Assert.Empty(hub.Core.Peers.All);   // a stranger is not a member
            Assert.Empty(a.Core.Peers.All);
            Assert.DoesNotContain(mesh.Network.Log, d => d.Data[0] != Envelope.Magic);
            Assert.Contains(hub.Logs, l => l.StartsWith("JFP2: admitted stranger") && l.Contains("198.51.100.2"));
            Assert.Contains(a.Logs, l => l.Contains("admitted this node: non-member session"));
        }

        [Fact]
        public void TheExchange_IsTwoHellosAndTwoAnswers_NoneAnswerLargerThanItsHello()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            Jfp2Of(a).Introduce(hub.EndPoint);
            mesh.Run(1);

            var hellos = Datagrams(mesh, a, hub).ToList();
            var answers = Datagrams(mesh, hub, a).ToList();
            Assert.Equal(2, hellos.Count);
            Assert.Equal(2, answers.Count);
            Assert.All(hellos, h => Assert.True(IsClass(h.Data, MessageClasses.Hello)));
            Assert.All(answers, h => Assert.True(IsClass(h.Data, MessageClasses.HelloAck)));
            Assert.True(answers[0].Data.Length <= hellos[0].Data.Length);

            HandshakeMessage challenge = ReadAck(answers[0].Data);
            Assert.Equal(HandshakeMessage.ResultNotAdmitted, challenge.Result);
            Assert.Empty(challenge.Offers);
            Assert.Equal(a.EndPoint, challenge.ObservedEndPoint);
            Assert.Equal(16, challenge.Admission!.Length);
            Assert.Equal(HandshakeMessage.ResultAccepted, ReadAck(answers[1].Data).Result);
        }

        [Fact]
        public void AStrangersHello_IsAnsweredWithoutKeepingState()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, []));
            mesh.Run(1);

            Assert.Equal(0, Jfp2Of(hub).NonMemberCount);
            Assert.Single(Datagrams(mesh, hub, a));
            Assert.Equal(HandshakeMessage.ResultNotAdmitted, ReadAck(Datagrams(mesh, hub, a).Single().Data).Result);
        }

        [Fact]
        public void ANodeInNoSession_DoesNotAnswerAStranger()
        {
            var mesh = new TestMesh();
            TestNode idle = Node(mesh, "203.0.113.1");
            TestNode a = Node(mesh, "198.51.100.2");

            Assert.True(Jfp2Of(a).Introduce(idle.EndPoint));
            mesh.Run(1);

            Assert.Empty(Datagrams(mesh, idle, a));
            Assert.False(Jfp2Of(a).HasNonMemberSession(idle.Id));
        }

        [Fact]
        public void AStrangersHelloSmallerThanTheAnswer_GetsNoAnswer()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, [], withOffers: false));
            mesh.Run(1);

            Assert.Empty(Datagrams(mesh, hub, a));
        }

        [Fact]
        public void AnUnansweredIntroduction_GivesUpWithoutStateAndAReleasedBuildSeesNothingItUnderstands()
        {
            var mesh = new TestMesh();
            TestNode old = mesh.Add("203.0.113.1", 6112, new LegacyPlugin());
            old.Core.Mesh.Create(false, 0, false, "");
            TestNode a = Node(mesh, "198.51.100.2");

            Jfp2Of(a).Introduce(old.EndPoint);
            mesh.Run(10);

            Assert.Equal(0, Jfp2Of(a).IntroductionCount);
            Assert.Equal(0, Jfp2Of(a).NonMemberCount);
            Assert.Equal(3, Datagrams(mesh, a, old).Count());   // asked three times, then gave up
            Assert.Empty(Datagrams(mesh, old, a));
            Assert.Contains(a.EventLogs, l => l.Contains("did not admit this node after 3 attempts"));
        }

        [Fact]
        public void ACookieForAnotherSource_IsNotAccepted_AndGetsANewCookie()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            TestNode b = Node(mesh, "192.0.2.3");

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, []));
            mesh.Run(0.2);
            byte[] cookie = ReadAck(Datagrams(mesh, hub, a).Single().Data).Admission!;

            // b replays a's cookie, saying it is a
            b.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x4321, cookie));
            mesh.Run(0.2);

            Assert.Equal(0, Jfp2Of(hub).NonMemberCount);
            Assert.Equal(HandshakeMessage.ResultNotAdmitted, ReadAck(Datagrams(mesh, hub, b).Single().Data).Result);
        }

        [Fact]
        public void AForgedCookie_IsNotAccepted()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, new byte[16]));
            mesh.Run(0.2);

            Assert.Equal(0, Jfp2Of(hub).NonMemberCount);
            Assert.Equal(HandshakeMessage.ResultNotAdmitted, ReadAck(Datagrams(mesh, hub, a).Single().Data).Result);
        }

        [Fact]
        public void ACookieAfterTheSecretRotatedTwice_GetsANewCookie_AndTheIntroductionStillSucceeds()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, []));
            mesh.Run(0.2);
            byte[] stale = ReadAck(Datagrams(mesh, hub, a).Single().Data).Admission!;
            mesh.Clock.Advance(130);

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, stale));
            mesh.Run(0.2);
            Assert.Equal(0, Jfp2Of(hub).NonMemberCount);

            Assert.True(Jfp2Of(a).Introduce(hub.EndPoint));
            mesh.Run(1);
            Assert.True(Jfp2Of(hub).HasNonMemberSession(a.Id));
        }

        [Fact]
        public void ChallengesAreRateLimited_AndRecover()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            for (int i = 0; i < 100; i++)
            {
                a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, []));
            }
            mesh.Network.Pump();
            int burst = Datagrams(mesh, hub, a).Count();
            Assert.InRange(burst, 1, 21);

            mesh.Run(2);
            mesh.Network.Log.Clear();
            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, []));
            mesh.Run(0.2);
            Assert.Single(Datagrams(mesh, hub, a));
        }

        [Fact]
        public void ANonMemberSession_EndsAfterSixtySeconds()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            Jfp2Of(a).Introduce(hub.EndPoint);
            mesh.Run(1);
            Assert.True(Jfp2Of(hub).HasNonMemberSession(a.Id));

            mesh.Run(55);
            Assert.True(Jfp2Of(hub).HasNonMemberSession(a.Id));
            Assert.True(Jfp2Of(a).HasNonMemberSession(hub.Id));

            mesh.Run(6);
            Assert.Equal(0, Jfp2Of(hub).NonMemberCount);
            Assert.Equal(0, Jfp2Of(a).NonMemberCount);
        }

        [Fact]
        public void ANonMemberSession_CarriesNoApplicationClass()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");
            Jfp2Of(a).Introduce(hub.EndPoint);
            mesh.Run(1);
            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort local, out ushort remote));

            var position = new Envelope(EnvelopeFlags.None, local, remote, MessageClasses.Position);
            var buffer = new byte[256];
            var message = new PositionUpdate { ObjectId = 3, NetTime = 1, Latitude = 12.5, StateFlags = PositionStateFlags.UserControlled };
            byte[] payload = buffer[..new PositionV1Codec().Encode(in message, buffer)];
            byte[] datagram = new byte[position.WireSize + payload.Length];
            payload.CopyTo(datagram, position.WriteTo(datagram));
            a.Transport.Send(hub.EndPoint, datagram);
            mesh.Run(0.2);

            Assert.Empty(hub.Messages<PositionUpdate>());
            Assert.Contains(hub.Logs, l => l.Contains("on a non-member session - dropped"));
            Assert.False(Jfp2Of(hub).CanCarry(a.Id, MessageKind.Position));
            Assert.False(Jfp2Of(hub).IsNegotiated(a.Id));
        }

        [Fact]
        public void NonMemberSessions_AreLimited()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            var strangers = Enumerable.Range(0, 300).Select(n => Node(mesh, "10.1." + (n / 250) + "." + (1 + n % 250))).ToList();

            // five strangers a second, inside the challenge rate: the table fills up (they last 60 s) and no more are admitted
            foreach (TestNode stranger in strangers)
            {
                Jfp2Of(stranger).Introduce(hub.EndPoint);
                mesh.Run(0.2);
            }
            mesh.Run(2);

            Assert.Equal(256, Jfp2Of(hub).NonMemberCount);
        }

        [Fact]
        public void AResultTwoWithoutAnObservedEndPoint_IsIgnored()
        {
            var mesh = new TestMesh();
            var seen = new List<byte[]>();
            var responder = new IPEndPoint(IPAddress.Parse("203.0.113.1"), 6112);
            InMemoryTransport? transport = null;
            transport = mesh.Network.Attach(responder, (from, data) =>
            {
                seen.Add(data);
                Assert.True(Envelope.TryReadFrom(data, out Envelope hello, out _, out _));
                var ack = new HandshakeMessage
                {
                    ProtoMajorMin = 2, ProtoMajorMax = 2, Result = HandshakeMessage.ResultNotAdmitted,
                    Names = [NodeId.FromLegacy(0xCB007101, 6112, 1)], Admission = new byte[16],
                };
                byte[] payload = ack.Serialize();
                var envelope = new Envelope(EnvelopeFlags.Internal, 0, hello.SenderPeerId, MessageClasses.HelloAck);
                byte[] datagram = new byte[envelope.WireSize + payload.Length];
                payload.CopyTo(datagram, envelope.WriteTo(datagram));
                transport!.Send(from, datagram);
            });
            TestNode a = Node(mesh, "198.51.100.2");

            Jfp2Of(a).Introduce(responder);
            mesh.Run(1);

            Assert.Single(seen);   // no second Hello with a cookie
            Assert.Contains(a.Logs, l => l.Contains("without a cookie and an observed endpoint"));
        }

        [Fact]
        public void TwoDualStackNodes_AdmitEachOtherOverIPv6()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.AddDualStack("203.0.113.1", "2001:db8::1", 6112, new Jfp2Plugin());
            hub.Core.Mesh.Create(false, 0, false, "");
            TestNode a = mesh.AddDualStack("198.51.100.2", "2001:db8::2", 6112, new Jfp2Plugin());

            Assert.True(Jfp2Of(a).Introduce(hub.EndPoint6!));
            mesh.Run(1);

            Assert.True(Jfp2Of(hub).HasNonMemberSession(a.Id));
            Assert.Equal(a.EndPoint6, Jfp2Of(hub).NonMemberEndPoint(a.Id));
            Assert.Equal(hub.EndPoint6, Jfp2Of(a).NonMemberEndPoint(hub.Id));
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6 || d.Data[0] != Envelope.Magic);
        }

        [Fact]
        public void AMemberHello_WithoutAdmission_IsStillIgnoredFromAnUnknownNode()
        {
            var mesh = new TestMesh();
            TestNode hub = SessionHub(mesh);
            TestNode a = Node(mesh, "198.51.100.2");

            a.Transport.Send(hub.EndPoint, StrangerHello(a.Id, 0x1234, null));
            mesh.Run(0.5);

            Assert.Empty(Datagrams(mesh, hub, a));
            Assert.Equal(0, Jfp2Of(hub).NonMemberCount);
        }
    }
}
