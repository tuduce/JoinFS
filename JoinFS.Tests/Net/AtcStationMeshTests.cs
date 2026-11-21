using System.Buffers.Binary;
using System.Net;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Tests.Atc;
using JoinFS.Tests.Session;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// docs/ATC/atc-fsd-plan.md stage 7 over a real mesh: each controller's node is a <see cref="TestNode"/> with the app's
    /// <see cref="AtcSession"/> and <see cref="AtcGateway"/> on top, and a fake EuroScope connection on the gateway.
    /// </summary>
    public class AtcStationMeshTests
    {
        /// <summary>The app's outbox, straight onto the core (the app queues for the network thread instead).</summary>
        sealed class CoreOutbox(NetworkCore core) : INetworkOutbox
        {
            public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
            {
                foreach (NodeId recipient in recipients) core.SendTo(recipient, message, meta.Guaranteed);
            }
            public void SendTo<T>(NodeId recipient, in T message, bool guaranteed = false) where T : struct, IMessage => core.SendTo(recipient, message, guaranteed);
            public void Broadcast<T>(in T message, bool guaranteed = false) where T : struct, IMessage => core.Broadcast(message, guaranteed);
            public void SendToEndPoint<T>(IPEndPoint endPoint, in T message, bool guaranteed = false) where T : struct, IMessage => throw new NotSupportedException();
            public void Ask<T>(IPEndPoint endPoint, in T message, bool guaranteed = false) where T : struct, IMessage => throw new NotSupportedException();
            public void SendObjectState<TPosition>(in IdentityUpdate identity, in TPosition position, ReadOnlySpan<NodeId> recipients) where TPosition : struct, IMessage => throw new NotSupportedException();
        }

        sealed class NodeSession(TestNode node) : ISessionState
        {
            public NetworkSnapshot Snapshot => NetworkSnapshot.Empty;
            public NodeId LocalId => node.Id;
            public bool Ready => LocalId.Valid();
            public bool Connected => true;
        }

        /// <summary>One controller's PC: the node, the session part on it, the gateway, and the client that connects to the gateway.</summary>
        sealed class Controller
        {
            public readonly TestNode Node;
            public readonly AtcSession Session;
            public readonly AtcGateway Gateway;
            public readonly List<string> Log = [];
            public readonly List<FakeFsdConnection> Clients = [];
            int events, received;

            public Controller(TestMesh mesh, TestNode node)
            {
                Node = node;
                Session = new AtcSession(new CoreOutbox(node.Core), new NodeSession(node), mesh.Clock, new FakeLog());
                Gateway = new AtcGateway(Log.Add, null, Session);
            }

            public FakeFsdConnection Client => Clients[^1];

            /// <summary>The client connects and logs in; without <paramref name="khz"/> it has not sent its first <c>%</c> yet.</summary>
            public FakeFsdConnection LogIn(string callsign, string name, int? khz = null, int facility = 4)
            {
                var connection = new FakeFsdConnection();
                Clients.Add(connection);
                Gateway.OnConnected(connection);
                Gateway.OnLine(connection, "#AA" + callsign + ":SERVER:" + name + ":1234567:token:4:100");
                if (khz != null) Position(connection, callsign, khz.Value, facility);
                return connection;
            }

            public void Position(FakeFsdConnection connection, string callsign, int khz, int facility = 4) =>
                Gateway.OnLine(connection, "%" + callsign + ":" + (khz - 100000) + ":" + facility + ":50:4:48.35377:11.78609:0");

            /// <summary>What the app does each tick: the network events and messages since the last, then the parts' work.</summary>
            public void Pump(double nowSeconds)
            {
                for (; events < Node.Events.Count; events++)
                {
                    NetworkEvent e = Node.Events[events];
                    switch (e.Kind)
                    {
                        case NetworkEventKind.PeerJoined: Session.OnPeerJoined(e.Node); break;
                        case NetworkEventKind.PeerEstablished: Session.OnPeerEstablished(e.Node); break;
                        case NetworkEventKind.PeerLeft: Session.OnPeerLeft(e.Node); break;
                    }
                }
                for (; received < Node.Received.Count; received++)
                {
                    var (meta, message) = Node.Received[received];
                    if (message is AtcStation station) Session.Handle(meta, station);
                    else if (message is AtcRelay relay) Session.Handle(meta, relay);
                }
                Session.DoWork();
                Gateway.Tick((long)(nowSeconds * 1000));
            }
        }

        sealed class Rig
        {
            public readonly TestMesh Mesh = new();
            public readonly TestNode Hub;
            public readonly List<Controller> Controllers = [];

            public Rig()
            {
                Hub = Mesh.Add("203.0.113.1", 6112, new Jfp2Plugin());
                Hub.Core.Mesh.Create(false, 0, false, "");
            }

            /// <param name="relayedFrom">A controller this one cannot reach directly: they meet through the hub.</param>
            public Controller Join(string ip, Controller? relayedFrom = null)
            {
                TestNode node = Mesh.Add(ip, 6112, new Jfp2Plugin());
                if (relayedFrom != null) Mesh.Partition(node, relayedFrom.Node);
                node.Core.Mesh.JoinOverJfp2(Hub.EndPoint, 0);
                var controller = new Controller(Mesh, node);
                Controllers.Add(controller);
                Run(2);
                return controller;
            }

            public void Run(double seconds)
            {
                for (double t = 0; t < seconds; t += 0.25)
                {
                    Mesh.Run(0.25);
                    foreach (Controller controller in Controllers) controller.Pump(Mesh.Clock.Now);
                }
            }
        }

        static ulong Bits(NodeId id)
        {
            Span<byte> bytes = stackalloc byte[NodeId.WireSize];
            id.WriteTo(bytes);
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }

        // ---- the story of stage 7

        [Fact]
        public void TwoControllers_BehindAHub_SeeEachOther_WithFrequency_AndTheListFollowsBensChanges()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3", relayedFrom: carla); // they only reach each other through the hub

            FakeFsdConnection carlasClient = carla.LogIn("EDDM_TWR", "Carla", 118_700);
            FakeFsdConnection bensClient = ben.LogIn("EDDM_APP", "Ben", 119_150, facility: 5);
            rig.Run(3);

            Assert.Contains("#AAEDDM_APP:SERVER:Ben:0::4:100", carlasClient.Sent);
            Assert.Contains(carlasClient.Sent, l => l.StartsWith("%EDDM_APP:19150:5:50:4:"));
            Assert.Contains("#AAEDDM_TWR:SERVER:Carla:0::4:100", bensClient.Sent);
            Assert.Contains(bensClient.Sent, l => l.StartsWith("%EDDM_TWR:18700:4:50:4:"));

            // Ben tunes another frequency
            carlasClient.Clear();
            ben.Position(bensClient, "EDDM_APP", 119_300, facility: 5);
            rig.Run(2);
            Assert.Contains(carlasClient.Sent, l => l.StartsWith("%EDDM_APP:19300:5:"));
            Assert.DoesNotContain(carlasClient.Sent, l => l.StartsWith("#AA"));

            // he closes EuroScope
            ben.Gateway.OnLine(bensClient, "#DAEDDM_APP:1234567");
            rig.Run(2);
            Assert.Contains("#DAEDDM_APP:", carlasClient.Sent);
            Assert.Empty(carla.Session.Stations);
        }

        [Fact]
        public void ALateJoiningController_GetsTheStationsAlreadyThere_AndAThirdPcCannotTakeATakenCallsign()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3");
            Controller third = rig.Join("192.0.2.4");
            carla.LogIn("EDDM_TWR", "Carla", 118_700);
            rig.Run(3);

            FakeFsdConnection bensClient = ben.LogIn("EDDM_APP", "Ben", 119_150, facility: 5);
            Assert.Contains("#AAEDDM_TWR:SERVER:Carla:0::4:100", bensClient.Sent); // already there when he logs in

            rig.Run(3);
            FakeFsdConnection refused = third.LogIn("EDDM_TWR", "Dave");

            Assert.True(refused.Closed);
            Assert.Contains("$ERSERVER:EDDM_TWR:001:", refused.Sent[0]);
            Assert.Null(third.Gateway.Station);
            Assert.False(carla.Client.Closed);
        }

        [Fact]
        public void TheNorthAndTheSouthTower_AreBothAccepted_AndEachSeesTheOtherByItsWholeCallsign()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller sam = rig.Join("192.0.2.3");

            FakeFsdConnection north = carla.LogIn("EDDM_N_TWR", "Carla", 118_700);
            FakeFsdConnection south = sam.LogIn("EDDM_S_TWR", "Sam", 120_500);
            rig.Run(3);

            Assert.False(north.Closed);
            Assert.False(south.Closed);
            Assert.Contains("#AAEDDM_S_TWR:SERVER:Sam:0::4:100", north.Sent);
            Assert.Contains(north.Sent, l => l.StartsWith("%EDDM_S_TWR:20500:4:"));
            Assert.Contains("#AAEDDM_N_TWR:SERVER:Carla:0::4:100", south.Sent);
        }

        [Fact]
        public void TwoLoginsWithOneCallsign_AtTheSameMoment_LeaveTheLowerNodeIdItsStation()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3");

            // both clients are accepted before either node has heard of the other's station
            FakeFsdConnection carlasClient = carla.LogIn("EDDM_TWR", "Carla");
            FakeFsdConnection bensClient = ben.LogIn("EDDM_TWR", "Ben");
            carla.Position(carlasClient, "EDDM_TWR", 118_700);
            ben.Position(bensClient, "EDDM_TWR", 118_700);
            rig.Run(4);

            (Controller keeps, Controller loses) = Bits(carla.Node.Id) < Bits(ben.Node.Id) ? (carla, ben) : (ben, carla);
            Assert.False(keeps.Client.Closed);
            Assert.NotNull(keeps.Gateway.Station);
            Assert.True(loses.Client.Closed);
            Assert.Null(loses.Gateway.Station);
            Assert.Contains("$ERSERVER:EDDM_TWR:001:", loses.Client.Sent[^1]);
            Assert.Empty(keeps.Session.Stations); // the other node's duplicate is not on its list
        }

        [Fact]
        public void WhenBensNodeDies_HeLeavesCarlasList_Within35Seconds()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3");
            FakeFsdConnection carlasClient = carla.LogIn("EDDM_TWR", "Carla", 118_700);
            ben.LogIn("EDDM_APP", "Ben", 119_150, facility: 5);
            rig.Run(3);
            Assert.Single(carla.Session.Stations);

            // his PC is gone: nothing he sends arrives, and nothing reaches him
            rig.Mesh.Partition(ben.Node, carla.Node);
            rig.Mesh.Partition(ben.Node, rig.Hub);
            rig.Run(40);

            Assert.Empty(carla.Session.Stations);
            Assert.Contains("#DAEDDM_APP:", carlasClient.Sent);
        }

        // ---- the story of stage 8

        /// <summary>What a client was sent besides the station list and the keep-alive.</summary>
        static List<string> Lines(FakeFsdConnection client) =>
            client.Sent.Where(l => !l.StartsWith("%") && !l.StartsWith("#AA") && !l.StartsWith("#DA") && !l.StartsWith("#DL")).ToList();

        [Fact]
        public void ThreeControllers_PlayAHandoff_AndEachClientGetsExactlyTheLinesMeantForIt()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3");
            Controller sam = rig.Join("192.0.2.4", relayedFrom: carla); // Sam and Carla only reach each other through the hub
            FakeFsdConnection carlas = carla.LogIn("EDDM_TWR", "Carla", 118_700);
            FakeFsdConnection bens = ben.LogIn("EDDM_APP", "Ben", 119_150, facility: 5);
            FakeFsdConnection sams = sam.LogIn("EDDM_GND", "Sam", 121_700, facility: 3);
            rig.Run(3);
            foreach (FakeFsdConnection client in new[] { carlas, bens, sams }) client.Clear();

            void Play(Controller controller, FakeFsdConnection client, string line)
            {
                controller.Gateway.OnLine(client, line);
                rig.Run(1);
            }

            // Carla tracks DLH4TK and sets her data: all controllers hear it, she does not
            Play(carla, carlas, "$CQEDDM_TWR:@94835:IT:DLH4TK");
            Play(carla, carlas, "#PCEDDM_TWR:@94835:CCP:TA:DLH4TK:5000");
            Play(carla, carlas, "#PCEDDM_TWR:@94835:CCP:SC:DLH4TK:ABC");
            string[] data = ["$CQEDDM_TWR:@94835:IT:DLH4TK", "#PCEDDM_TWR:@94835:CCP:TA:DLH4TK:5000", "#PCEDDM_TWR:@94835:CCP:SC:DLH4TK:ABC"];
            Assert.Equal(data, Lines(bens));
            Assert.Equal(data, Lines(sams));
            Assert.Empty(Lines(carlas));
            foreach (FakeFsdConnection client in new[] { carlas, bens, sams }) client.Clear();

            // she hands it to Ben: only Ben gets it
            Play(carla, carlas, "$HOEDDM_TWR:EDDM_APP:DLH4TK");
            Assert.Equal(["$HOEDDM_TWR:EDDM_APP:DLH4TK"], Lines(bens));
            Assert.Empty(Lines(sams));
            Assert.Empty(Lines(carlas));

            // he accepts: only Carla gets that, and the track is announced to the others
            Play(ben, bens, "$HAEDDM_APP:EDDM_TWR:DLH4TK");
            Play(ben, bens, "$CQEDDM_APP:@94835:IT:DLH4TK");
            Assert.Equal(["$HAEDDM_APP:EDDM_TWR:DLH4TK", "$CQEDDM_APP:@94835:IT:DLH4TK"], Lines(carlas));
            Assert.Equal(["$CQEDDM_APP:@94835:IT:DLH4TK"], Lines(sams));
            foreach (FakeFsdConnection client in new[] { carlas, bens, sams }) client.Clear();

            // the ATC chat reaches the others, a private line only its station
            Play(ben, bens, "#TMEDDM_APP:@49999:good evening: all");
            Assert.Equal(["#TMEDDM_APP:@49999:good evening: all"], Lines(carlas));
            Assert.Equal(["#TMEDDM_APP:@49999:good evening: all"], Lines(sams));
            Assert.Empty(Lines(bens));
            foreach (FakeFsdConnection client in new[] { carlas, bens, sams }) client.Clear();

            Play(carla, carlas, "#TMEDDM_TWR:EDDM_GND:psst");
            Assert.Equal(["#TMEDDM_TWR:EDDM_GND:psst"], Lines(sams));
            Assert.Empty(Lines(bens));
            Assert.Empty(Lines(carlas));

            // a station that is not there
            Play(carla, carlas, "#TMEDDM_TWR:EDDM_DEL:anyone");
            Assert.Equal(["$ERSERVER:EDDM_TWR:007:EDDM_DEL:No such callsign"], Lines(carlas));
            Assert.Empty(Lines(bens));
            Assert.Equal(8, carla.Session.RelayedOut); // three group lines to two nodes, the handoff and the private line
        }

        [Fact]
        public void ANodeCannotSpeakForAnotherNodesStation()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3");
            Controller sam = rig.Join("192.0.2.4");
            FakeFsdConnection carlas = carla.LogIn("EDDM_TWR", "Carla", 118_700);
            FakeFsdConnection bens = ben.LogIn("EDDM_APP", "Ben", 119_150, facility: 5);
            FakeFsdConnection sams = sam.LogIn("EDDM_GND", "Sam", 121_700, facility: 3);
            rig.Run(3);
            foreach (FakeFsdConnection client in new[] { carlas, bens, sams }) client.Clear();

            // Ben's client claims to be Carla
            ben.Gateway.OnLine(bens, "$HOEDDM_TWR:EDDM_GND:DLH4TK");
            ben.Gateway.OnLine(bens, "#TMEDDM_TWR:@49999:I am the tower");
            rig.Run(2);

            Assert.Empty(Lines(sams));
            Assert.Empty(Lines(carlas));
            Assert.Equal(2, sam.Session.Dropped(AtcRelayDrop.UnknownSender));
            Assert.Equal(1, carla.Session.Dropped(AtcRelayDrop.UnknownSender));
        }

        [Fact]
        public void ALineOverTheLimit_IsNotSent_OnTheMesh()
        {
            var rig = new Rig();
            Controller carla = rig.Join("198.51.100.2");
            Controller ben = rig.Join("192.0.2.3");
            FakeFsdConnection carlas = carla.LogIn("EDDM_TWR", "Carla", 118_700);
            FakeFsdConnection bens = ben.LogIn("EDDM_APP", "Ben", 119_150, facility: 5);
            rig.Run(3);
            bens.Clear();

            carla.Gateway.OnLine(carlas, "#TMEDDM_TWR:EDDM_APP:" + new string('R', 1100));
            carla.Gateway.OnLine(carlas, "#TMEDDM_TWR:EDDM_APP:short");
            rig.Run(2);

            Assert.Equal(["#TMEDDM_TWR:EDDM_APP:short"], Lines(bens));
            Assert.Equal(1, carla.Session.Dropped(AtcRelayDrop.TooLong));
        }
    }
}
