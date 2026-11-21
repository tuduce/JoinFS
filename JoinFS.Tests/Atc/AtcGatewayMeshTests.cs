using JoinFS.Fsd;
using JoinFS.Net;

namespace JoinFS.Tests.Atc
{
    /// <summary>The mesh as the gateway sees it, scripted by a test.</summary>
    sealed class FakeAtcMesh : IAtcMesh
    {
        readonly List<AtcRemoteStation> stations = [];
        public readonly List<(int Slot, AtcStationInfo? Station)> Local = [];

        public IReadOnlyList<AtcRemoteStation> Stations => stations.ToList();
        public bool CallsignInUse(string callsign) => stations.Any(s => s.Callsign == callsign);
        public void SetLocalStation(int slot, AtcStationInfo? station) => Local.Add((slot, station));

        public event Action<AtcRemoteStation>? StationChanged;
        public event Action<AtcRemoteStation>? StationGone;
        public event Action<int, string>? LocalStationLost;

        public int Subscribers => (StationChanged?.GetInvocationList().Length ?? 0) + (StationGone?.GetInvocationList().Length ?? 0)
            + (LocalStationLost?.GetInvocationList().Length ?? 0);

        public void Come(AtcRemoteStation station)
        {
            stations.RemoveAll(s => s.Callsign == station.Callsign);
            stations.Add(station);
            StationChanged?.Invoke(station);
        }

        public void Go(AtcRemoteStation station)
        {
            stations.RemoveAll(s => s.Callsign == station.Callsign);
            StationGone?.Invoke(station);
        }

        public void Lose(int slot, string callsign) => LocalStationLost?.Invoke(slot, callsign);
    }

    /// <summary>docs/ATC/atc-fsd-plan.md stage 7: the stations of other nodes on the client's list.</summary>
    public class AtcGatewayMeshTests
    {
        static readonly NodeId Ben = NodeId.FromLegacy(0x0A000001, 6112, 4);

        readonly List<string> log = [];
        readonly FakeAtcMesh mesh = new();
        readonly AtcGateway gateway;

        public AtcGatewayMeshTests()
        {
            gateway = new AtcGateway(log.Add, null, mesh);
        }

        static AtcRemoteStation Approach(string callsign = "EDDM_APP", int khz = 119_150, string name = "Ben") =>
            new(Ben, 0, callsign, name, 5, FsdFacility.Approach, 100, khz, 48.1, 11.5);

        FakeFsdConnection LogIn(string callsign = "EDDM_TWR")
        {
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);
            gateway.OnLine(connection, "$ID" + callsign + ":SERVER:ffff:EuroScope:3:0:1234567:0:0");
            gateway.OnLine(connection, "#AA" + callsign + ":SERVER:Carla:1234567:token:4:100");
            return connection;
        }

        static void SendPosition(AtcGateway gateway, FakeFsdConnection connection, string callsign = "EDDM_TWR", string frequency = "18700") =>
            gateway.OnLine(connection, "%" + callsign + ":" + frequency + ":4:50:4:48.35377:11.78609:0");

        [Fact]
        public void ALoginWithACallsignOfAnotherNode_IsRefused()
        {
            mesh.Come(Approach("EDDM_TWR"));
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);

            gateway.OnLine(connection, "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100");

            Assert.True(connection.Closed);
            Assert.Contains("$ERSERVER:EDDM_TWR:001:", connection.Sent[0]);
            Assert.Null(gateway.Station);
            Assert.Contains(log, l => l.Contains("another node"));
        }

        [Fact]
        public void AClientThatLogsInLater_GetsTheStationsAlreadyThere_AfterTheCapabilityExchange()
        {
            mesh.Come(Approach());
            mesh.Come(Approach("EDDM_GND", 121_700, "Gero") with { Facility = FsdFacility.Ground });

            FakeFsdConnection connection = LogIn();

            Assert.Equal(
            [
                "#TMserver:EDDM_TWR:JoinFS ATC gateway",
                "$CQSERVER:EDDM_TWR:CAPS",
                "$CRSERVER:EDDM_TWR:ATC:Y:EDDM_TWR",
                "$CRSERVER:EDDM_TWR:CAPS:ATCINFO=1:SECPOS=1",
                "$CRSERVER:EDDM_TWR:IP:127.0.0.1",
                "#AAEDDM_APP:SERVER:Ben:0::5:100",
                "%EDDM_APP:19150:5:100:5:48.10000:11.50000:0",
                "#AAEDDM_GND:SERVER:Gero:0::5:100",
                "%EDDM_GND:21700:3:100:5:48.10000:11.50000:0",
            ], connection.Sent);
        }

        [Fact]
        public void AStationThatComesOnline_IsToldAs_AaThenPosition_AndChangeIsOnlyAPosition_AndAGoneIsDa()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            mesh.Come(Approach());
            Assert.Equal(["#AAEDDM_APP:SERVER:Ben:0::5:100", "%EDDM_APP:19150:5:100:5:48.10000:11.50000:0"], connection.Sent);

            connection.Clear();
            mesh.Come(Approach(khz: 119_300));
            Assert.Equal(["%EDDM_APP:19300:5:100:5:48.10000:11.50000:0"], connection.Sent);

            connection.Clear();
            mesh.Come(Approach(khz: 119_300)); // nothing changed
            Assert.Empty(connection.Sent);

            mesh.Go(Approach());
            Assert.Equal(["#DAEDDM_APP:"], connection.Sent);
        }

        [Fact]
        public void AStationAlreadyToldAtLogin_IsNotToldAgainByItsEvent()
        {
            mesh.Come(Approach());
            FakeFsdConnection connection = LogIn();
            int before = connection.Sent.Count;

            // the mesh raises the events of changes that were made before the login
            mesh.Come(Approach());

            Assert.Equal(before, connection.Sent.Count);
        }

        [Fact]
        public void TheRemotePositionsRepeatEvery15Seconds_AndNothingIsSentWithoutAClient()
        {
            gateway.Tick(0);
            mesh.Come(Approach()); // no client: nothing to tell
            FakeFsdConnection connection = LogIn();
            connection.Clear();
            gateway.Tick(1_000);

            gateway.Tick(1_000 + AtcGateway.RemotePositionIntervalMs - 1);
            Assert.DoesNotContain(connection.Sent, l => l.StartsWith('%'));
            gateway.Tick(1_000 + AtcGateway.RemotePositionIntervalMs);
            Assert.Single(connection.Sent, l => l.StartsWith("%EDDM_APP"));
        }

        [Fact]
        public void AStationThatGoesWhileNoClientIsThere_IsNotToldWhenOneComes()
        {
            mesh.Come(Approach());
            mesh.Go(Approach());

            FakeFsdConnection connection = LogIn();

            Assert.DoesNotContain(connection.Sent, l => l.Contains("EDDM_APP"));
        }

        [Fact]
        public void TheClientsStation_IsHandedToTheMesh_WithItsFirstPosition_AndWhenItLogsOff()
        {
            FakeFsdConnection connection = LogIn();
            Assert.Empty(mesh.Local); // not before the first %

            SendPosition(gateway, connection);
            var (slot, station) = Assert.Single(mesh.Local);
            Assert.Equal(0, slot);
            Assert.Equal("EDDM_TWR", station!.Callsign);
            Assert.True(station.PositionKnown);
            Assert.Equal(118_700, station.FrequencyKhz);

            gateway.OnLine(connection, "#DAEDDM_TWR:1234567");
            Assert.Equal((0, null), (mesh.Local[^1].Slot, mesh.Local[^1].Station));
        }

        [Fact]
        public void ADroppedConnection_LogsTheStationOffInTheMesh()
        {
            FakeFsdConnection connection = LogIn();
            SendPosition(gateway, connection);

            gateway.OnClosed(connection);

            Assert.Null(mesh.Local[^1].Station);
        }

        [Fact]
        public void ALostCallsign_SendsAnErrorAndClosesTheClient()
        {
            FakeFsdConnection connection = LogIn();
            SendPosition(gateway, connection);
            connection.Clear();

            mesh.Lose(0, "EDDM_TWR");

            Assert.True(connection.Closed);
            Assert.Contains("$ERSERVER:EDDM_TWR:001:", connection.Sent[0]);
            Assert.Null(gateway.Station);
            Assert.Contains(log, l => l.Contains("whose id is lower"));
        }

        [Fact]
        public void ALostCallsignOfAnotherStation_ChangesNothing()
        {
            FakeFsdConnection connection = LogIn();

            mesh.Lose(0, "EDDM_GND");
            mesh.Lose(1, "EDDM_TWR");

            Assert.False(connection.Closed);
            Assert.NotNull(gateway.Station);
        }

        [Fact]
        public void AfterShutdown_TheGatewayListensToTheMeshNoMore_AndTheStationIsLoggedOff()
        {
            FakeFsdConnection connection = LogIn();
            SendPosition(gateway, connection);
            Assert.True(mesh.Subscribers > 0);

            gateway.Shutdown();

            Assert.Equal(0, mesh.Subscribers);
            Assert.Null(mesh.Local[^1].Station);
        }
    }
}
