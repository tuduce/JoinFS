using JoinFS.Fsd;
using JoinFS.Net;

namespace JoinFS.Tests.Atc
{
    /// <summary>docs/ATC/atc-fsd-plan.md stage 10: the ATIS connection (D5), with a fake mesh and fake connections.</summary>
    public class AtcGatewayAtisTests
    {
        static readonly NodeId Ben = NodeId.FromLegacy(0x0A000001, 6112, 4);

        readonly List<string> log = [];
        readonly FakeAtcMesh mesh = new();
        readonly AtcGateway gateway;
        readonly FakeFsdConnection main;

        public AtcGatewayAtisTests()
        {
            gateway = new AtcGateway(log.Add, null, mesh);
            main = LogIn("EDDM_TWR");
            gateway.OnLine(main, "%EDDM_TWR:18700:4:50:4:48.35377:11.78609:0");
            main.Clear();
            mesh.Local.Clear();
        }

        FakeFsdConnection LogIn(string callsign)
        {
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);
            gateway.OnLine(connection, "$ID" + callsign + ":SERVER:ffff:EuroScope:3:0:1234567:0:0");
            gateway.OnLine(connection, "#AA" + callsign + ":SERVER:Carla:1234567:token:4:100");
            return connection;
        }

        FakeFsdConnection LogInAtis()
        {
            FakeFsdConnection atis = LogIn("EDDM_ATIS");
            gateway.OnLine(atis, "%EDDM_ATIS:18010:4:50:2:48.35377:11.78609:0");
            return atis;
        }

        static AtcRemoteStation Approach() =>
            new(Ben, 0, "EDDM_APP", "Ben", 5, FsdFacility.Approach, 100, 119_150, 48.1, 11.5);

        // ---- login

        [Fact]
        public void ASecondConnection_WithAnAtisCallsign_IsAcceptedWhileTheMainStationIsOnline()
        {
            FakeFsdConnection atis = LogIn("EDDM_ATIS");

            Assert.False(atis.Closed);
            Assert.False(main.Closed);
            Assert.Equal("EDDM_ATIS", gateway.AtisStation!.Callsign);
            Assert.Equal("EDDM_TWR", gateway.Station!.Callsign);
            Assert.Equal(
            [
                "#TMserver:EDDM_ATIS:JoinFS ATC gateway",
                "$CQSERVER:EDDM_ATIS:CAPS",
                "$CRSERVER:EDDM_ATIS:ATC:Y:EDDM_ATIS",
                "$CRSERVER:EDDM_ATIS:CAPS:ATCINFO=1:SECPOS=1",
                "$CRSERVER:EDDM_ATIS:IP:127.0.0.1",
            ], atis.Sent);
            Assert.Empty(main.Sent);
        }

        [Fact]
        public void TheAtisIsAnnouncedAsSlot1_FromItsFirstPosition()
        {
            FakeFsdConnection atis = LogIn("EDDM_ATIS");
            Assert.Empty(mesh.Local);

            gateway.OnLine(atis, "%EDDM_ATIS:18010:4:50:2:48.35377:11.78609:0");

            (int slot, AtcStationInfo? station) = Assert.Single(mesh.Local);
            Assert.Equal(1, slot);
            Assert.Equal("EDDM_ATIS", station!.Callsign);
            Assert.Equal(118_010, station.FrequencyKhz);
            Assert.Equal(FsdFacility.Tower, gateway.Station!.Facility); // the main station is untouched
            Assert.Equal(118_700, gateway.Station.FrequencyKhz);
        }

        [Fact]
        public void AnAtisCallsignThatLogsInFirst_IsTheMainStation()
        {
            var g = new AtcGateway(_ => { }, null, new FakeAtcMesh());
            var connection = new FakeFsdConnection();
            g.OnConnected(connection);
            g.OnLine(connection, "#AAEDDM_ATIS:SERVER:Carla:1234567:token:4:100");

            Assert.False(connection.Closed);
            Assert.Equal("EDDM_ATIS", g.Station!.Callsign);
            Assert.Null(g.AtisStation);
        }

        [Fact]
        public void ASecondConnection_WithoutAnAtisCallsign_IsRefused()
        {
            FakeFsdConnection second = LogIn("EDDM_APP");

            Assert.True(second.Closed);
            Assert.Contains(":003:", second.Sent.Single());
            Assert.Null(gateway.AtisStation);
        }

        [Fact]
        public void AThirdConnection_IsRefused()
        {
            LogInAtis();
            FakeFsdConnection third = LogIn("EDDF_ATIS");

            Assert.True(third.Closed);
            Assert.Contains(":003:", third.Sent.Single());
            Assert.Equal("EDDM_ATIS", gateway.AtisStation!.Callsign);
        }

        [Fact]
        public void TheAtisCallsignOfAnotherNode_IsRefused()
        {
            mesh.Come(Approach() with { Callsign = "EDDM_ATIS", Slot = 1 });
            FakeFsdConnection atis = LogIn("EDDM_ATIS");

            Assert.True(atis.Closed);
            Assert.Contains(":001:", atis.Sent.Single());
            Assert.Null(gateway.AtisStation);
            Assert.False(main.Closed);
        }

        [Fact]
        public void TheMainCallsignAgain_IsRefusedAsInUse()
        {
            FakeFsdConnection second = LogIn("EDDM_TWR");
            Assert.True(second.Closed);
            Assert.Contains(":001:", second.Sent.Single());
        }

        // ---- the end of the two connections

        [Fact]
        public void TheAtisEnds_WithTheMainStation()
        {
            FakeFsdConnection atis = LogInAtis();
            mesh.Local.Clear();

            gateway.OnLine(main, "#DAEDDM_TWR:SERVER:1234567");

            Assert.True(main.Closed);
            Assert.True(atis.Closed);
            Assert.Null(gateway.Station);
            Assert.Null(gateway.AtisStation);
            Assert.Contains((1, (AtcStationInfo?)null), mesh.Local);
            Assert.Contains((0, (AtcStationInfo?)null), mesh.Local);
            Assert.Equal(0, gateway.ConnectionCount);
        }

        [Fact]
        public void TheAtisEnds_WhenTheMainConnectionDrops()
        {
            FakeFsdConnection atis = LogInAtis();

            gateway.OnClosed(main);

            Assert.True(atis.Closed);
            Assert.Null(gateway.AtisStation);
            Assert.Equal(0, gateway.ConnectionCount);
        }

        [Fact]
        public void TheAtisLoggingOff_LeavesTheMainStation()
        {
            FakeFsdConnection atis = LogInAtis();
            mesh.Local.Clear();

            gateway.OnLine(atis, "#DAEDDM_ATIS:SERVER:1234567");

            Assert.True(atis.Closed);
            Assert.False(main.Closed);
            Assert.Null(gateway.AtisStation);
            Assert.Equal("EDDM_TWR", gateway.Station!.Callsign);
            Assert.Equal([(1, (AtcStationInfo?)null)], mesh.Local);
        }

        [Fact]
        public void AfterTheAtisLeft_ANewAtisMayLogIn()
        {
            FakeFsdConnection atis = LogInAtis();
            gateway.OnClosed(atis);

            FakeFsdConnection again = LogIn("EDDM_ATIS");

            Assert.False(again.Closed);
            Assert.Equal("EDDM_ATIS", gateway.AtisStation!.Callsign);
        }

        [Fact]
        public void LosingTheAtisCallsignToAnotherNode_RefusesOnlyTheAtis()
        {
            FakeFsdConnection atis = LogInAtis();
            atis.Clear();

            mesh.Lose(1, "EDDM_ATIS");

            Assert.True(atis.Closed);
            Assert.Contains(":001:", atis.Sent.Single());
            Assert.False(main.Closed);
            Assert.Null(gateway.AtisStation);
        }

        [Fact]
        public void Shutdown_ClosesBothAndLogsBothOff()
        {
            FakeFsdConnection atis = LogInAtis();
            mesh.Local.Clear();

            gateway.Shutdown();

            Assert.True(main.Closed);
            Assert.True(atis.Closed);
            Assert.Contains((1, (AtcStationInfo?)null), mesh.Local);
            Assert.Contains((0, (AtcStationInfo?)null), mesh.Local);
        }

        // ---- lines

        [Fact]
        public void ALineFromAnotherNode_ToTheAtis_ReachesTheAtisConnection_AndTheAnswerGoesBack()
        {
            FakeFsdConnection atis = LogInAtis();
            atis.Clear();
            mesh.Come(Approach());
            main.Clear();

            mesh.Receive("$CQEDDM_APP:EDDM_ATIS:ATIS");
            Assert.Equal(["$CQEDDM_APP:EDDM_ATIS:ATIS"], atis.Sent);
            Assert.Empty(main.Sent);

            gateway.OnLine(atis, "$CREDDM_ATIS:EDDM_APP:ATIS:A:D");
            gateway.OnLine(atis, "$CREDDM_ATIS:EDDM_APP:ATIS:T:wind 270 at 8");
            Assert.Equal(
            [
                ("EDDM_APP", "$CREDDM_ATIS:EDDM_APP:ATIS:A:D"),
                ("EDDM_APP", "$CREDDM_ATIS:EDDM_APP:ATIS:T:wind 270 at 8"),
            ], mesh.Relayed);
        }

        [Fact]
        public void ALineToTheMainStation_StillReachesTheMainConnection()
        {
            FakeFsdConnection atis = LogInAtis();
            atis.Clear();

            mesh.Receive("$CQEDDM_APP:EDDM_TWR:C?");
            mesh.Receive("#TMEDDM_APP:@49999:chat");

            Assert.Equal(["$CQEDDM_APP:EDDM_TWR:C?", "#TMEDDM_APP:@49999:chat"], main.Sent);
            Assert.Empty(atis.Sent);
        }

        [Fact]
        public void ALineBetweenTheTwoConnections_IsDeliveredHere_AndNotRelayed()
        {
            FakeFsdConnection atis = LogInAtis();
            atis.Clear();
            mesh.Come(Approach());
            main.Clear();

            gateway.OnLine(main, "#TMEDDM_TWR:EDDM_ATIS:new letter");
            gateway.OnLine(atis, "#TMEDDM_ATIS:EDDM_TWR:ok");

            Assert.Equal(["#TMEDDM_TWR:EDDM_ATIS:new letter"], atis.Sent);
            Assert.Equal(["#TMEDDM_ATIS:EDDM_TWR:ok"], main.Sent);
            Assert.Empty(mesh.Relayed);
        }

        [Fact]
        public void TheAtisGroupLines_GoToTheMesh()
        {
            FakeFsdConnection atis = LogInAtis();

            gateway.OnLine(atis, "$CQEDDM_ATIS:@94835:NEWINFO:B");

            Assert.Equal([("*", "$CQEDDM_ATIS:@94835:NEWINFO:B")], mesh.Relayed);
        }

        [Fact]
        public void TheAtisConnection_IsAnsweredAsServerAndForAnUnknownCallsign()
        {
            FakeFsdConnection atis = LogInAtis();
            atis.Clear();

            gateway.OnLine(atis, "$PIEDDM_ATIS:SERVER:123");
            gateway.OnLine(atis, "$CQEDDM_ATIS:SERVER:ATC:EDDM_ATIS");
            gateway.OnLine(atis, "$CQEDDM_ATIS:NOBODY:ATIS");

            Assert.Equal(
            [
                "$POSERVER:EDDM_ATIS:123",
                "$CRSERVER:EDDM_ATIS:ATC:Y:EDDM_ATIS",
            ], atis.Sent.Take(2));
            Assert.Contains(":007:", atis.Sent[2]);
            Assert.Empty(main.Sent);
        }

        [Fact]
        public void TheMainConnection_IsToldTheAtisIsAController()
        {
            LogInAtis();
            main.Clear();

            gateway.OnLine(main, "$CQEDDM_TWR:SERVER:ATC:EDDM_ATIS");

            Assert.Equal(["$CRSERVER:EDDM_TWR:ATC:Y:EDDM_ATIS"], main.Sent);
        }

        [Fact]
        public void APositionOfTheMainCallsign_OnTheAtisConnection_IsIgnored()
        {
            FakeFsdConnection atis = LogInAtis();
            mesh.Local.Clear();

            gateway.OnLine(atis, "%EDDM_TWR:19000:4:50:4:48.0:11.0:0");

            Assert.Empty(mesh.Local);
            Assert.Equal(118_700, gateway.Station!.FrequencyKhz);
        }

        [Fact]
        public void TheKeepAlive_GoesToBothConnections()
        {
            FakeFsdConnection atis = LogInAtis();
            main.Clear();
            atis.Clear();

            gateway.Tick(1_000);
            gateway.Tick(1_000 + AtcGateway.HeartbeatIntervalMs);

            Assert.Equal(["#DLSERVER:*:0:0"], main.Sent);
            Assert.Equal(["#DLSERVER:*:0:0"], atis.Sent);
        }
    }
}
