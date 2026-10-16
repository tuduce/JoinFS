using System.Net;
using System.Net.Sockets;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The path upgrade and fallback (docs/jfp2/design-future.md §4.1) for a node that joined a hub over IPv4:
    /// the hub's IPv6 candidate arrives in its HelloAck (tag Candidates), the node probes it with Pulses,
    /// and the path moves to IPv6 on the first PulseResponse from the candidate. It goes back to IPv4 when
    /// IPv6 stops answering without the peer expiring, and stays off IPv6 for the hold-off.
    /// </summary>
    public class HubPathUpgradeTests
    {
        static TestNode Dual(TestMesh mesh, string ip4, string ip6) => mesh.AddDualStack(ip4, ip6, 6112, new LegacyPlugin(), new Jfp2Plugin());
        static TestNode V4Only(TestMesh mesh, string ip4) => mesh.Add(ip4, 6112, new LegacyPlugin(), new Jfp2Plugin());

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static Peer PeerOf(TestNode node, TestNode peer) => node.Core.Peers.TryGet(peer.Id, out Peer p) ? p : null!;

        static bool OnIpv6(TestNode node, TestNode peer) => PeerOf(node, peer).EndPoint.AddressFamily == AddressFamily.InterNetworkV6;

        static IdentityUpdate Identity(uint id, string callsign) => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = callsign, Model = "M", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static void SendPosition(TestNode from, TestNode to, uint id, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id, "JF" + id));
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        /// <summary>A hub and a dual-stack node that joined it through its IPv4 address.</summary>
        static (TestMesh Mesh, TestNode Hub, TestNode A) HubAndNode()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            return (mesh, hub, a);
        }

        static void AssertPathIsConsistent(Peer peer)
        {
            // one path: the node's own endpoint is the one traffic goes to, the same object
            Assert.Same(peer.EndPoint, peer.RouteEndPoint);
            Assert.False(peer.Relayed);
            Assert.True(peer.Direct);
            Assert.True(peer.RouteIsOwnEndPoint);
        }

        [Fact]
        public void NodeThatJoinedOverIPv4_MovesToIPv6_WhenTheHubsCandidateAnswers()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Run(0.5);
            Assert.False(OnIpv6(a, hub));       // joined over IPv4 and nothing is known about IPv6 yet

            mesh.Run(12);

            Assert.True(a.Knows(hub) && hub.Knows(a));
            Assert.Equal(hub.EndPoint6, PeerOf(a, hub).EndPoint);
            Assert.Equal(a.EndPoint6, PeerOf(hub, a).EndPoint);   // the hub learned A's candidate from A's Hello
            AssertPathIsConsistent(PeerOf(a, hub));
            AssertPathIsConsistent(PeerOf(hub, a));
            Assert.Contains(a.Logs, l => l.StartsWith("NETWORK: Path upgrade") && l.Contains(hub.EndPoint6!.ToString()));
        }

        [Fact]
        public void AfterTheUpgrade_Jfp2AndLegacyKeepFlowingOverIPv6_AndTheSessionIsNeverDemoted()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Run(12);
            Assert.True(OnIpv6(a, hub));
            int before = mesh.Network.Log.Count;

            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            SendPosition(a, hub, 3, 12.5);
            SendPosition(hub, a, 4, 21.5);
            mesh.Run(30);

            Assert.Equal(12.5, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(21.5, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id) && Jfp2Of(hub).IsNegotiated(a.Id));
            Assert.DoesNotContain(a.EventLogs, l => l.Contains("no longer verified"));
            Assert.DoesNotContain(hub.EventLogs, l => l.Contains("no longer verified"));
            Assert.True(a.Knows(hub) && hub.Knows(a));
            // after the move, nothing more goes between the two over IPv4
            Assert.DoesNotContain(mesh.Network.Log.Skip(before), d => d.From.AddressFamily == AddressFamily.InterNetwork && a.OwnsAddress(d.From) && hub.OwnsAddress(d.To));
        }

        static NotesBundle Note(string text) => new()
        {
            Scope = CommsScope.Single,
            Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "n", Callsign = "c", Notes = [new CommsNote { NoteId = 5, Channel = 1, Text = text }] }],
        };

        [Fact]
        public void GuaranteedMessages_ThatWereSentBeforeTheMove_ArePendingToTheCurrentEndPoint()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Run(12);
            Assert.True(OnIpv6(a, hub));
            mesh.Partition(a, hub);     // IPv4 is gone for good

            a.Core.SendTo(hub.Id, Note("jfp2"), true);              // carried by JFP2
            a.Core.SendTo(hub.Id, new RemoveObject { ObjectId = 77 }, true);   // carried by legacy
            hub.Core.SendTo(a.Id, new RemoveObject { ObjectId = 78 }, true);
            mesh.Run(6);

            Assert.Equal("jfp2", Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            Assert.Equal(77u, Assert.Single(hub.Messages<RemoveObject>()).ObjectId);
            Assert.Equal(78u, Assert.Single(a.Messages<RemoveObject>()).ObjectId);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
            Assert.Equal(0, a.Core.Plugins.OfType<LegacyPlugin>().Single().GuaranteedOutCount);
            Assert.Equal(0, hub.Core.Plugins.OfType<LegacyPlugin>().Single().GuaranteedOutCount);
        }

        [Fact]
        public void AGuaranteedMessageReceivedBeforeTheMove_IsStillADuplicate_WhenItsResendComesOverTheNewPath()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Run(0.3);
            Assert.False(OnIpv6(a, hub));
            mesh.Network.Log.Clear();
            hub.Core.SendTo(a.Id, new RemoveObject { ObjectId = 5 }, true);
            mesh.Run(0.1);
            byte[] sent = mesh.Network.Log.First(d => hub.OwnsAddress(d.From) && a.OwnsAddress(d.To) && (d.Data[LegacyWire.FlagsOffset] & LegacyWire.FlagGuaranteed) != 0).Data;
            Assert.Equal(5u, Assert.Single(a.Messages<RemoveObject>()).ObjectId);

            mesh.Run(12);
            Assert.True(OnIpv6(a, hub));
            a.Core.OnDatagram(hub.EndPoint6!, sent);

            Assert.Single(a.Messages<RemoveObject>());
        }

        static byte[] Handshake(byte messageClass, ushort sender, ushort recipient, HandshakeMessage message)
        {
            byte[] payload = message.Serialize();
            var envelope = new Envelope(EnvelopeFlags.Internal, sender, recipient, messageClass);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram, header);
            return datagram;
        }

        static IEnumerable<HandshakeMessage> Handshakes(TestMesh mesh, TestNode from, byte messageClass) =>
            mesh.Network.Log.Where(d => from.OwnsAddress(d.From) && d.Data[0] == Envelope.Magic && Envelope.TryReadFrom(d.Data, out Envelope e, out _, out _)
                && e.RawMessageClass == messageClass && (e.Flags & EnvelopeFlags.Internal) != 0)
                .Select(d => { Envelope.TryReadFrom(d.Data, out _, out int header, out _); return HandshakeMessage.Deserialize(d.Data.AsSpan(header)); });

        [Fact]
        public void OnlyANodeWithAnIPv6Candidate_SendsTheCandidatesTag()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode c = V4Only(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(10);

            Assert.NotEmpty(Handshakes(mesh, c, MessageClasses.Hello));
            Assert.All(Handshakes(mesh, c, MessageClasses.Hello), h => Assert.Empty(h.Candidates));
            Assert.NotEmpty(Handshakes(mesh, hub, MessageClasses.HelloAck));
            Assert.All(Handshakes(mesh, hub, MessageClasses.HelloAck), h => Assert.Equal(hub.EndPoint6, Assert.Single(h.Candidates)));
            Assert.All(Handshakes(mesh, hub, MessageClasses.Hello), h => Assert.Equal(hub.EndPoint6, Assert.Single(h.Candidates)));
        }

        [Fact]
        public void ACandidatesTagFromAnotherAddress_CannotMakeTheNodeProbeAnAddress()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.CutIpv6(a, hub);
            mesh.Run(0.3);
            var forged = new HandshakeMessage
            {
                ProtoMajorMin = 2, ProtoMajorMax = 2, SelfAssignedId = 9,
                Names = [NodeName.FromLegacy(hub.Id)],
                Candidates = [new IPEndPoint(IPAddress.Parse("2001:db8::bad"), 6112)],
            };
            a.Core.OnDatagram(new IPEndPoint(IPAddress.Parse("198.18.0.9"), 6112), Handshake(MessageClasses.Hello, 9, 0, forged));

            mesh.Run(5);

            Assert.DoesNotContain(mesh.Network.Log, d => d.To.Address.Equals(IPAddress.Parse("2001:db8::bad")));
        }

        static byte[] FirstPulse(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.First(d => from.OwnsAddress(d.From) && to.OwnsAddress(d.To) && d.Data[0] == 0x0B && (d.Data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0
                && BitConverter.ToInt16(d.Data, LegacyWire.DataOffset) == (short)LegacyWire.InternalId.Pulse).Data;

        [Fact]
        public void APulseFromAnotherAddressInTheCandidates64_IsAnObservedCandidate_ButOneFromAnother64IsNot()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.CutIpv6(a, hub);
            mesh.Run(2);
            byte[] pulse = FirstPulse(mesh, hub, a);
            var sameNetwork = IPAddress.Parse("2001:db8::99");
            var otherNetwork = IPAddress.Parse("2001:db8:1::99");

            a.Core.OnDatagram(new IPEndPoint(otherNetwork, 6112), pulse);
            a.Core.OnDatagram(new IPEndPoint(sameNetwork, 6112), pulse);
            mesh.Run(3);

            Assert.Contains(mesh.Network.Log, d => d.To.Address.Equals(sameNetwork) && a.OwnsAddress(d.From));
            Assert.DoesNotContain(mesh.Network.Log, d => d.To.Address.Equals(otherNetwork) && a.OwnsAddress(d.From) && !(d.Data[0] == 0x0B && BitConverter.ToInt16(d.Data, LegacyWire.DataOffset) == (short)LegacyWire.InternalId.PulseResponse));
            Assert.Equal(hub.EndPoint, PeerOf(a, hub).EndPoint);    // a Pulse over the other family never moves the path by itself
        }

        [Fact]
        public void UpgradeWorks_WhenBothNodesAreBehindAStatefulIPv6Firewall()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Firewall(hub);
            mesh.Firewall(a);

            mesh.Run(15);

            Assert.True(OnIpv6(a, hub));
            Assert.True(OnIpv6(hub, a));
            Assert.True(a.Knows(hub) && hub.Knows(a));
        }

        [Fact]
        public void IPv4OnlyNode_StaysOnIPv4_AndNeverReceivesIPv6Traffic()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode c = V4Only(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            c.Core.Mesh.Join(hub.EndPoint, 0);

            mesh.Run(15);

            Assert.True(c.Knows(hub) && hub.Knows(c));
            Assert.Equal(hub.EndPoint, PeerOf(c, hub).EndPoint);
            Assert.Equal(c.EndPoint, PeerOf(hub, c).EndPoint);
            Assert.DoesNotContain(mesh.Network.Log, d => d.To.AddressFamily == AddressFamily.InterNetworkV6);
        }


        [Fact]
        public void WhenIPv6DoesNotAnswer_TheNodeStaysOnIPv4_AndNothingExpires()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.CutIpv6(a, hub);

            mesh.Run(70);

            Assert.False(OnIpv6(a, hub));
            Assert.False(OnIpv6(hub, a));
            Assert.True(a.Knows(hub) && hub.Knows(a));
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
        }

        [Fact]
        public void ProbingStopsAfterThirtySeconds()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.CutIpv6(a, hub);
            mesh.Run(45);
            int sent = mesh.Network.Log.Count(d => a.OwnsAddress(d.From) && d.To.Equals(hub.EndPoint6));
            Assert.InRange(sent, 10, 40);       // a Pulse a second for at most 30 s, never a flood

            mesh.Run(30);
            Assert.Equal(sent, mesh.Network.Log.Count(d => a.OwnsAddress(d.From) && d.To.Equals(hub.EndPoint6)));
        }

        [Fact]
        public void WhenIPv6StopsAnswering_TheNodeFallsBackToIPv4_WithoutTheHubExpiring_AndKeepsSendingPositions()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Run(12);
            Assert.True(OnIpv6(a, hub) && OnIpv6(hub, a));

            mesh.CutIpv6(a, hub);
            mesh.Run(5);
            Assert.True(OnIpv6(a, hub), "10 s without an answer are needed first");
            mesh.Run(10);

            Assert.False(OnIpv6(a, hub));
            Assert.False(OnIpv6(hub, a));
            Assert.Equal(hub.EndPoint, PeerOf(a, hub).EndPoint);
            Assert.Equal(a.EndPoint, PeerOf(hub, a).EndPoint);
            AssertPathIsConsistent(PeerOf(a, hub));
            AssertPathIsConsistent(PeerOf(hub, a));
            Assert.Contains(a.Logs, l => l.StartsWith("NETWORK: Path fallback"));
            Assert.True(a.Knows(hub) && hub.Knows(a));

            SendPosition(a, hub, 3, 7.5);
            SendPosition(hub, a, 4, 8.5);
            mesh.Run(40);   // longer than a peer's expiry
            Assert.True(a.Knows(hub) && hub.Knows(a));
            Assert.Equal(7.5, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(8.5, Assert.Single(a.Messages<PositionUpdate>()).Latitude);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
        }

        [Fact]
        public void AfterAFallback_IPv6IsNotProbedForFiveMinutes_ThenTheNodeMovesBackWhenItWorks()
        {
            var (mesh, hub, a) = HubAndNode();
            mesh.Run(12);
            Action restore = mesh.CutIpv6(a, hub);
            mesh.Run(15);
            Assert.False(OnIpv6(a, hub));
            restore();
            // the hub announces its candidates again every 5 s (keepalive Hellos) but the hold-off stands
            mesh.Run(240);
            Assert.False(OnIpv6(a, hub));
            Assert.False(OnIpv6(hub, a));

            mesh.Run(90);

            Assert.True(OnIpv6(a, hub));
            Assert.True(OnIpv6(hub, a));
            Assert.True(a.Knows(hub) && hub.Knows(a));
        }

        [Fact]
        public void UpgradeInOneDirectionOnly_WhenTheOtherSidesAnswersAreLost()
        {
            var (mesh, hub, a) = HubAndNode();
            // A's PulseResponses over IPv6 never arrive: the hub gets no answer to its probes, A gets the hub's
            var previous = mesh.Network.Filter;
            mesh.Network.Filter = (from, to, data) =>
                !(from.Equals(a.EndPoint6) && IsPulseResponse(data)) && (previous == null || previous(from, to, data));

            mesh.Run(15);

            Assert.True(OnIpv6(a, hub));
            Assert.False(OnIpv6(hub, a));
            Assert.True(a.Knows(hub) && hub.Knows(a));
        }

        static bool IsPulseResponse(byte[] data) =>
            data.Length > LegacyWire.DataOffset + 1 && data[0] == 0x0B && (data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0
            && BitConverter.ToInt16(data, LegacyWire.DataOffset) == (short)LegacyWire.InternalId.PulseResponse;

        [Fact]
        public void AddNode_AdvertisesTheIPv4Port_OfAPeerOnAnIPv6Path()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            var lan = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 6112);
            var mapped = new IPEndPoint(IPAddress.Parse("198.51.100.2"), 40000);
            // A is a dual-stack node behind an IPv4 NAT that maps its port; IPv6 is not translated
            var a = new TestNode(mesh, "10.0.0.2", "2001:db8::2", 6112, "198.51.100.2", new LegacyPlugin(), new Jfp2Plugin());
            mesh.Nodes.Add(a);
            mesh.Network.Nat = (from, to) =>
                (from.Equals(lan) ? mapped : from, to.Equals(mapped) ? lan : to);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(15);
            Assert.True(OnIpv6(hub, a));
            Assert.Equal(40000, PeerOf(hub, a).Ipv4EndPoint!.Port);

            TestNode c = V4Only(mesh, "192.0.2.9");
            c.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(5);

            Assert.True(c.Core.Peers.TryGet(a.Id, out Peer seenByC));
            Assert.Equal(40000, seenByC.EndPoint.Port);
        }

        [Fact]
        public void APeerReachedThroughTheHub_FollowsTheHubsPath_WhenItMovesToIPv6()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode p = V4Only(mesh, "192.0.2.3");
            mesh.Partition(a, p);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            p.Core.Mesh.Join(hub.EndPoint, 0);

            mesh.Run(15);

            Peer viaHub = PeerOf(a, p);
            Assert.True(OnIpv6(a, hub));
            Assert.Equal(hub.Id, viaHub.RouteVia);
            Assert.True(viaHub.Relayed);
            Assert.Equal(PeerOf(a, hub).EndPoint, viaHub.RouteEndPoint);   // follows the relay to its IPv6 endpoint
            Assert.Equal(p.EndPoint, viaHub.EndPoint);

            SendPosition(a, p, 9, 3.25);
            mesh.Run(1);
            Assert.Equal(3.25, Assert.Single(p.Messages<PositionUpdate>()).Latitude);
        }

        [Fact]
        public void WhenTheRelayLeaves_ThePeersRoutedThroughItFindANewRoute_AfterItsPathMoved()
        {
            var mesh = new TestMesh();
            TestNode hub = Dual(mesh, "203.0.113.1", "2001:db8::1");
            TestNode a = Dual(mesh, "198.51.100.2", "2001:db8::2");
            TestNode p = V4Only(mesh, "192.0.2.3");
            mesh.Partition(a, p);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            p.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(15);
            Peer viaHub = PeerOf(a, p);
            Assert.True(viaHub.Relayed);

            hub.Core.Mesh.Leave();
            mesh.Nodes.Remove(hub);
            mesh.Run(40);

            Assert.False(a.Core.Peers.Contains(hub.Id));
            Assert.False(viaHub.Relayed);
            Assert.Same(viaHub.EndPoint, viaHub.RouteEndPoint);
        }
    }
}
