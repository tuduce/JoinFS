using System.Diagnostics;
using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The threaded NetworkService over real UDP sockets on loopback: two instances form a session
    /// and exchange messages, with everything crossing the network thread / app thread boundary
    /// through the mailbox, the inbound queue and the snapshot.
    /// </summary>
    public class NetworkServiceTests
    {
        sealed class Collector : IMessageHandler, INetworkEventHandler
        {
            public readonly List<PositionUpdate> Positions = [];
            public readonly List<IdentityUpdate> Identities = [];
            public readonly List<NetworkEvent> Events = [];

            public void Handle(in MessageMeta meta, in PositionUpdate message) => Positions.Add(message);
            public void Handle(in MessageMeta meta, in IdentityUpdate message) => Identities.Add(message);
            public void OnNetworkEvent(in NetworkEvent e) { if (e.Kind != NetworkEventKind.Log) Events.Add(e); }
        }

        static NetworkService StartNode(out int port)
        {
            var service = new NetworkService();
            Assert.True(service.Open(0, out string error), error);
            service.Post(core =>
            {
                core.Identity.LocalAddress = IPAddress.Loopback;
                core.Identity.InternetAddress = IPAddress.Loopback;
            });
            service.Start();
            port = 0;
            var deadline = Stopwatch.StartNew();
            while (!service.Snapshot.Ready && deadline.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(5);
            port = service.Snapshot.Port;
            return service;
        }

        static bool WaitFor(Func<bool> condition, Action pump, double seconds = 5)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                pump();
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        [Fact]
        public void TwoServices_JoinAndExchangePositions()
        {
            using NetworkService hub = StartNode(out int hubPort);
            using NetworkService client = StartNode(out _);
            var hubApp = new Collector();
            var clientApp = new Collector();
            void Pump()
            {
                hub.Drain(hubApp, hubApp);
                client.Drain(clientApp, clientApp);
            }

            hub.Post(core => core.Mesh.Create(false, 0, false, ""));
            client.Post(core => core.Mesh.Join(new IPEndPoint(IPAddress.Loopback, hubPort), 0));

            Assert.True(WaitFor(() => client.Snapshot.Connected && client.Snapshot.PeerList.Count == 1 && client.Snapshot.PeerList[0].SendEstablished
                && hub.Snapshot.PeerList.Count == 1 && hub.Snapshot.PeerList[0].SendEstablished, Pump), "session did not establish");
            Assert.Contains(clientApp.Events, e => e.Kind == NetworkEventKind.SessionJoined);
            Assert.Contains(hubApp.Events, e => e.Kind == NetworkEventKind.PeerEstablished);

            NodeId hubId = client.Snapshot.PeerList[0].Id;
            var identity = new IdentityUpdate
            {
                ObjectId = 1, IsAircraft = true, IsPlane = true, Callsign = "TEST1", Model = "M", Livery = "", IcaoType = "C172",
                IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
            };
            client.SendObjectState(identity, new PositionUpdate { ObjectId = 1, NetTime = 5, Latitude = 47.5 }, [hubId]);

            Assert.True(WaitFor(() => hubApp.Positions.Count == 1, Pump), "position did not arrive");
            Assert.Equal(47.5, hubApp.Positions[0].Latitude);
            Assert.Equal("TEST1", hubApp.Identities.Single().Callsign);
            Assert.True(hub.Snapshot.PeerList[0].Rtt < 1.0f);
        }
    }
}
