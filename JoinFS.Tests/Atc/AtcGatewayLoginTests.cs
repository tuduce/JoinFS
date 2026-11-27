using JoinFS.Fsd;

namespace JoinFS.Tests.Atc
{
    /// <summary>A connection that records what the gateway sends and whether it closed it.</summary>
    sealed class FakeFsdConnection : IFsdConnection
    {
        public FakeFsdConnection(string address = "127.0.0.1") { RemoteAddress = address; }

        public string RemoteAddress { get; }
        public readonly List<string> Sent = [];
        public bool Closed { get; private set; }

        public void Send(string line) => Sent.Add(line);
        public void Close() => Closed = true;

        public void Clear() => Sent.Clear();
    }

    /// <summary>docs/ATC/atc-fsd-plan.md stage 2: the login and the SERVER row, with a fake connection.</summary>
    public class AtcGatewayLoginTests
    {
        const string LoginTwr = "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100";

        readonly List<string> log = [];
        readonly AtcGateway gateway;

        public AtcGatewayLoginTests()
        {
            gateway = new AtcGateway(log.Add);
        }

        FakeFsdConnection lastConnection = null!;

        FakeFsdConnection Connect()
        {
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);
            lastConnection = connection;
            return connection;
        }

        FakeFsdConnection LogIn(string login = LoginTwr)
        {
            FakeFsdConnection connection = Connect();
            gateway.OnLine(connection, "$IDEDDM_TWR:SERVER:ffff:EuroScope:3:0:1234567:0:0");
            gateway.OnLine(connection, login);
            return connection;
        }

        [Fact]
        public void NothingIsSentOnConnect()
        {
            // the bridge that works with EuroScope sends no $DI banner: docs/ATC/euroscope-setup.md
            FakeFsdConnection connection = Connect();

            Assert.Empty(connection.Sent);
            Assert.False(connection.Closed);
            Assert.Null(gateway.Station);
        }

        [Fact]
        public void Login_AcceptsTheStationAndAnswersTheCapabilityExchange()
        {
            FakeFsdConnection connection = LogIn();

            Assert.False(connection.Closed);
            AtcStationInfo station = gateway.Station!;
            Assert.Equal("EDDM_TWR", station.Callsign);
            Assert.Equal("Carla", station.Name);
            Assert.Equal(4, station.Rating);
            Assert.False(station.PositionKnown);
            Assert.Equal(
            [
                "#TMserver:EDDM_TWR:JoinFS ATC gateway",
                "$CQSERVER:EDDM_TWR:CAPS",
                "$CRSERVER:EDDM_TWR:ATC:Y:EDDM_TWR",
                "$CRSERVER:EDDM_TWR:CAPS:ATCINFO=1:SECPOS=1",
                "$CRSERVER:EDDM_TWR:IP:127.0.0.1",
            ], connection.Sent);
            Assert.Contains(log, l => l.Contains("EDDM_TWR") && l.Contains("logged in"));
        }

        [Fact]
        public void Login_WithAnObserverRating_IsNotAController()
        {
            FakeFsdConnection connection = LogIn("#AAEDDM_OBS:SERVER:Carla:1234567:token:1:100");

            Assert.Contains("$CRSERVER:EDDM_OBS:ATC:N:EDDM_OBS", connection.Sent);
        }

        [Theory]
        [InlineData("#AAEDDM_N_TWR:SERVER:Carla:1:t:4:100", "EDDM_N_TWR")]
        [InlineData("#AAEDMM_ALB_CTR:SERVER:Carla:1:t:5:100", "EDMM_ALB_CTR")]
        [InlineData("#AAEDDM:SERVER:Carla:1:t:4:100", "EDDM")]
        public void Login_AcceptsTheWholeCallsign(string login, string callsign)
        {
            LogIn(login);

            Assert.Equal(callsign, gateway.Station!.Callsign);
        }

        [Theory]
        [InlineData("#AAed:SERVER:Carla:1:t:4:100")]
        [InlineData("#AAEDDM_TOWER_NORTH:SERVER:Carla:1:t:4:100")]
        [InlineData("#AAEDDM-TWR:SERVER:Carla:1:t:4:100")]
        [InlineData("#AAeddm_twr:SERVER:Carla:1:t:4:100")]
        public void Login_RefusesAnInvalidCallsign(string login)
        {
            FakeFsdConnection connection = LogIn(login);

            Assert.True(connection.Closed);
            Assert.Null(gateway.Station);
            Assert.Single(connection.Sent);
            Assert.StartsWith("$ERSERVER:", connection.Sent[0]);
            Assert.Contains(":002:", connection.Sent[0]);
            Assert.Contains(log, l => l.Contains("refused") && l.Contains("invalid callsign"));
        }

        [Fact]
        public void Login_RefusesASecondStation()
        {
            FakeFsdConnection first = LogIn();
            FakeFsdConnection second = LogIn("#AAEDDM_APP:SERVER:Ben:2:t:4:100");

            Assert.True(second.Closed);
            Assert.False(first.Closed);
            Assert.Equal("EDDM_TWR", gateway.Station!.Callsign);
            Assert.Contains(":003:", second.Sent.Single());
            Assert.Contains(log, l => l.Contains("refused"));
        }

        [Fact]
        public void Login_RefusesTheSameCallsignAsInUse()
        {
            LogIn();
            FakeFsdConnection second = LogIn();

            Assert.True(second.Closed);
            Assert.Contains(":001:", second.Sent.Single());
        }

        [Fact]
        public void ARefusedConnection_DoesNotLogTheStationOff()
        {
            FakeFsdConnection first = LogIn();
            FakeFsdConnection second = LogIn("#AAEDDM_APP:SERVER:Ben:2:t:4:100");
            gateway.OnClosed(second);

            Assert.Equal("EDDM_TWR", gateway.Station!.Callsign);
            Assert.False(first.Closed);
        }

        [Fact]
        public void AnyOtherVerbBeforeLogin_ClosesTheConnection()
        {
            FakeFsdConnection connection = Connect();

            gateway.OnLine(connection, "$PIEDDM_TWR:SERVER:123");

            Assert.True(connection.Closed);
            Assert.Empty(connection.Sent);
            Assert.Equal(0, gateway.ConnectionCount);
        }

        [Fact]
        public void APilotLogin_IsRefused()
        {
            FakeFsdConnection connection = Connect();

            gateway.OnLine(connection, "#APDLH4TK:SERVER:1:t:1:101:2:Pia");

            Assert.True(connection.Closed);
            Assert.Null(gateway.Station);
        }

        [Fact]
        public void GarbageBeforeLogin_ClosesTheConnection()
        {
            FakeFsdConnection connection = Connect();

            gateway.OnLine(connection, "hello");

            Assert.True(connection.Closed);
        }

        [Fact]
        public void GarbageAfterLogin_IsIgnored()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "hello");

            Assert.False(connection.Closed);
            Assert.Empty(connection.Sent);
        }

        [Fact]
        public void APosition_UpdatesTheStation()
        {
            LogIn();

            gateway.OnLine(Last(), "%EDDM_TWR:28700:4:50:4:48.35377:11.78609:0");

            AtcStationInfo station = gateway.Station!;
            Assert.True(station.PositionKnown);
            Assert.Equal(FsdFacility.Tower, station.Facility);
            Assert.Equal(128700, station.FrequencyKhz);
            Assert.Equal(50, station.VisibilityRange);
            Assert.Equal(48.35377, station.Latitude, 5);
            Assert.Equal(11.78609, station.Longitude, 5);
            Assert.Contains(log, l => l.Contains("EDDM_TWR") && l.Contains("128.700"));
        }

        [Fact]
        public void ANewFrequency_ReplacesTheOldOne()
        {
            LogIn();
            gateway.OnLine(Last(), "%EDDM_TWR:28700:4:50:4:48.35377:11.78609:0");
            AtcStationInfo before = gateway.Station!;

            gateway.OnLine(Last(), "%EDDM_TWR:28550:4:50:4:48.35377:11.78609:0");

            Assert.Equal(128550, gateway.Station!.FrequencyKhz);
            Assert.Equal(128700, before.FrequencyKhz); // the old record is untouched
        }

        [Fact]
        public void APositionOfAnotherCallsign_IsIgnored()
        {
            LogIn();

            gateway.OnLine(Last(), "%EDDM_APP:28700:5:50:4:48.35377:11.78609:0");

            Assert.False(gateway.Station!.PositionKnown);
        }

        [Fact]
        public void APositionWithAnObserverFacility_MakesTheStationAnObserver()
        {
            FakeFsdConnection connection = LogIn();
            gateway.OnLine(connection, "%EDDM_TWR::0:50:4:48.35377:11.78609:0");
            connection.Clear();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:ATC:EDDM_TWR");

            Assert.Equal(["$CRSERVER:EDDM_TWR:ATC:N:EDDM_TWR"], connection.Sent);
        }

        [Fact]
        public void Logoff_LogsTheStationOffAndClosesTheConnection()
        {
            FakeFsdConnection connection = LogIn();

            gateway.OnLine(connection, "#DAEDDM_TWR:1234567");

            Assert.Null(gateway.Station);
            Assert.True(connection.Closed);
            Assert.Contains(log, l => l.Contains("logged off"));
        }

        [Fact]
        public void ADroppedConnection_LogsTheStationOff()
        {
            FakeFsdConnection connection = LogIn();

            gateway.OnClosed(connection);

            Assert.Null(gateway.Station);
            Assert.Contains(log, l => l.Contains("logged off") && l.Contains("connection closed"));
        }

        [Fact]
        public void AfterLogoff_ANewStationMayLogIn()
        {
            FakeFsdConnection first = LogIn();
            gateway.OnClosed(first);

            LogIn("#AAEDDM_APP:SERVER:Ben:2:t:4:100");

            Assert.Equal("EDDM_APP", gateway.Station!.Callsign);
        }

        [Fact]
        public void AnOversizedLine_ClosesTheConnection()
        {
            FakeFsdConnection connection = LogIn();

            gateway.OnLine(connection, "#TMEDDM_TWR:@49999:" + new string('x', AtcGateway.MaxLineBytes));

            Assert.True(connection.Closed);
            Assert.Null(gateway.Station);
        }

        [Fact]
        public void APing_IsAnswered()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "$PIEDDM_TWR:SERVER:1736029820");

            Assert.Equal(["$POSERVER:EDDM_TWR:1736029820"], connection.Sent);
        }

        [Fact]
        public void APingToAnotherCallsign_IsNotAnsweredAsServer_ButIsAnUnknownCallsign()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "$PIEDDM_TWR:EDDM_APP:1736029820");

            Assert.Equal(["$ERSERVER:EDDM_TWR:007:EDDM_APP:No such callsign"], connection.Sent);
        }

        [Fact]
        public void AControllerQuery_IsAnswered()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:ATC:EDDM_TWR");
            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:ATC:DLH4TK");

            Assert.Equal(["$CRSERVER:EDDM_TWR:ATC:Y:EDDM_TWR", "$CRSERVER:EDDM_TWR:ATC:N:DLH4TK"], connection.Sent);
        }

        [Fact]
        public void AnIpQuery_ReturnsTheClientAddress()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:IP");

            Assert.Equal(["$CRSERVER:EDDM_TWR:IP:127.0.0.1"], connection.Sent);
        }

        [Fact]
        public void AFlightPlanQuery_AnswersThereIsNone()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:FP:DLH4TK");

            Assert.Single(connection.Sent);
            Assert.StartsWith("$ERSERVER:EDDM_TWR:008:DLH4TK:", connection.Sent[0]);
        }

        [Fact]
        public void AnUnknownQueryToTheServer_IsIgnored()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:SV");

            Assert.Empty(connection.Sent);
            Assert.False(connection.Closed);
        }

        [Fact]
        public void Tick_SendsAKeepAliveEvery30SecondsWhileLoggedIn()
        {
            FakeFsdConnection connection = LogIn();
            connection.Clear();

            gateway.Tick(1000);
            gateway.Tick(1000 + AtcGateway.HeartbeatIntervalMs - 1);
            Assert.Empty(connection.Sent);

            gateway.Tick(1000 + AtcGateway.HeartbeatIntervalMs);
            Assert.Equal(["#DLSERVER:*:0:0"], connection.Sent);

            gateway.Tick(1000 + AtcGateway.HeartbeatIntervalMs + 1);
            Assert.Single(connection.Sent);
        }

        [Fact]
        public void Tick_WithoutAStation_SendsNothing()
        {
            FakeFsdConnection connection = Connect();

            gateway.Tick(0);
            gateway.Tick(10 * AtcGateway.HeartbeatIntervalMs);

            Assert.Empty(connection.Sent);
        }

        [Fact]
        public void Shutdown_ClosesEveryConnectionAndLogsTheStationOff()
        {
            FakeFsdConnection station = LogIn();
            FakeFsdConnection other = Connect();

            gateway.Shutdown();

            Assert.True(station.Closed);
            Assert.True(other.Closed);
            Assert.Null(gateway.Station);
            Assert.Equal(0, gateway.ConnectionCount);

            FakeFsdConnection late = Connect();
            Assert.True(late.Closed);
        }

        [Fact]
        public void ALineOfAnUnknownConnection_IsIgnored()
        {
            var stranger = new FakeFsdConnection();

            gateway.OnLine(stranger, LoginTwr);

            Assert.Null(gateway.Station);
            Assert.Empty(stranger.Sent);
        }

        /// <summary>The connection most recently made by <see cref="Connect"/>.</summary>
        FakeFsdConnection Last() => lastConnection;
    }
}
