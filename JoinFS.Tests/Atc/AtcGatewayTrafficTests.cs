using JoinFS.Fsd;

namespace JoinFS.Tests.Atc
{
    /// <summary>The aircraft of the session, as a test sets them.</summary>
    sealed class FakeTraffic : IAtcTraffic
    {
        public List<AtcAircraft> List { get; set; } = [];
        public IReadOnlyList<AtcAircraft> Aircraft => List.ToArray();
    }

    /// <summary>docs/ATC/atc-fsd-plan.md stage 4: traffic on the scope, with fake traffic and a fake connection.</summary>
    public class AtcGatewayTrafficTests
    {
        const string LoginTwr = "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100";

        static readonly AtcFlightPlanData Plan = new("IFR", "A320", "N0450", "EDDM", "FL350", "EDDF", "EDDS", "RMK/TEST", "ANORA T104 TOBAK");

        readonly FakeTraffic traffic = new();
        readonly AtcGateway gateway;
        long now = 100_000;

        public AtcGatewayTrafficTests()
        {
            gateway = new AtcGateway(_ => { }, traffic);
        }

        static AtcAircraft Aircraft(ulong owner, ulong id, string callsign, double latitude = 48.1, double longitude = 11.5,
            int squawk = 1200, AtcFlightPlanData? plan = null, string nickname = "Pia") =>
            new(owner, id, callsign, nickname, squawk, latitude, longitude, 1500.4, 120.4, 0, 0, 90, plan ?? Plan);

        FakeFsdConnection LogIn()
        {
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);
            gateway.OnLine(connection, "$IDEDDM_TWR:SERVER:ffff:EuroScope:3:0:1234567:0:0");
            gateway.OnLine(connection, LoginTwr);
            connection.Clear();
            return connection;
        }

        void Tick(FakeFsdConnection connection, long advanceMs = 0)
        {
            now += advanceMs;
            gateway.Tick(now);
        }

        static List<string> Positions(FakeFsdConnection connection) => connection.Sent.Where(l => l.StartsWith("@")).ToList();
        static List<string> Plans(FakeFsdConnection connection) => connection.Sent.Where(l => l.StartsWith("$FP")).ToList();
        static List<string> Deletes(FakeFsdConnection connection) => connection.Sent.Where(l => l.StartsWith("#DP")).ToList();

        [Fact]
        public void NothingIsSentBeforeTheLogin()
        {
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);

            gateway.Tick(now);
            gateway.Tick(now + 5000);
            gateway.OnLine(connection, "$IDEDDM_TWR:SERVER:ffff:EuroScope:3:0:1234567:0:0");
            gateway.Tick(now + 10000);

            Assert.Empty(connection.Sent);
        }

        [Fact]
        public void AnAircraftAppears_WithItsPositionAndFlightPlan()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "dlh123"));

            Tick(connection);

            Assert.Equal("@N:DLH123:1200:1:48.10000:11.50000:1500:120:1024:0", connection.Sent[0]);
            Assert.Equal("$FPDLH123:*A:I:A320:450:EDDM:::FL350:EDDF:0:0:0:0:EDDS:RMK/TEST:ANORA T104 TOBAK", connection.Sent[1]);
            Assert.Equal(2, connection.Sent.Count);
        }

        [Fact]
        public void AnAircraftMoves_ThePositionIsSentOnceASecond_TheFlightPlanOnce()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            Tick(connection);
            connection.Clear();

            Tick(connection, 400);
            Assert.Empty(connection.Sent);

            traffic.List[0] = Aircraft(1, 1, "DLH123", latitude: 48.2, longitude: 11.6);
            Tick(connection, 600);

            Assert.Equal(["@N:DLH123:1200:1:48.20000:11.60000:1500:120:1024:0"], connection.Sent);
        }

        [Fact]
        public void AChangedFlightPlan_IsSentAgain()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            Tick(connection);
            connection.Clear();

            traffic.List[0] = Aircraft(1, 1, "DLH123", plan: Plan with { Destination = "EDDH" });
            Tick(connection, 1000);

            Assert.Single(Positions(connection));
            string plan = Assert.Single(Plans(connection));
            Assert.Contains(":EDDH:", plan);
        }

        [Fact]
        public void AnAircraftThatLeaves_IsDeletedAfterFiveSeconds()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            Tick(connection);
            traffic.List.Clear();
            connection.Clear();

            for (int i = 0; i < 4; i++) Tick(connection, 1000);
            Assert.Empty(connection.Sent);

            Tick(connection, 1000);
            Assert.Equal(["#DPDLH123:"], connection.Sent);

            // and only once
            connection.Clear();
            Tick(connection, 1000);
            Assert.Empty(connection.Sent);
        }

        [Fact]
        public void AnAircraftThatIsBackWithinFiveSeconds_IsNotDeletedOrAnnouncedAgain()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            Tick(connection);
            AtcAircraft aircraft = traffic.List[0];
            traffic.List.Clear();
            Tick(connection, 2000);
            traffic.List.Add(aircraft);
            connection.Clear();

            Tick(connection, 1000);

            Assert.Empty(Deletes(connection));
            Assert.Empty(Plans(connection));
            Assert.Single(Positions(connection));
        }

        [Fact]
        public void AChangedCallsign_DeletesTheOldOneFirst()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            Tick(connection);
            connection.Clear();

            traffic.List[0] = Aircraft(1, 1, "DLH456");
            Tick(connection, 1000);

            Assert.StartsWith("#DPDLH123", connection.Sent[0]);
            Assert.StartsWith("@N:DLH456:", connection.Sent[1]);
            Assert.StartsWith("$FPDLH456:", connection.Sent[2]);
        }

        [Fact]
        public void AnEmptyCallsign_GetsAnAssignedOne_ThatIsTheSameForTheSameAircraft()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 7, ""));
            Tick(connection);
            string first = FsdLine.TryParse(Positions(connection).Single(), out FsdLine line) ? line.From : "";
            connection.Clear();

            Tick(connection, 1000);
            string second = FsdLine.TryParse(Positions(connection).Single(), out line) ? line.From : "";

            Assert.Matches("^JFS[0-9A-F]{4}$", first);
            Assert.Equal(first, second);
            Assert.Empty(Deletes(connection));
        }

        [Fact]
        public void TwoAircraftWithTheSameCallsign_AreToldApart_AndTheLowestKeepsIt()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(2, 1, "DLH123", nickname: "Olaf"));
            traffic.List.Add(Aircraft(1, 1, "DLH123", nickname: "Pia"));

            Tick(connection);

            List<string> callsigns = Positions(connection).Select(p => FsdLine.TryParse(p, out FsdLine l) ? l.From : "").ToList();
            Assert.Equal(2, callsigns.Count);
            Assert.Equal(2, callsigns.Distinct().Count());
            // owner 1 comes second in the list and keeps the plain name
            Assert.Equal("DLH123", callsigns[1]);
            Assert.StartsWith("DLH123", callsigns[0]);
            Assert.Equal(2, Plans(connection).Count);
        }

        [Fact]
        public void ADuplicateCallsign_KeepsItsAssignedNameWhenTheOrderOfTheListChanges()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            traffic.List.Add(Aircraft(2, 1, "DLH123"));
            Tick(connection);
            connection.Clear();

            traffic.List.Reverse();
            Tick(connection, 1000);

            Assert.Empty(Deletes(connection));
            Assert.Empty(Plans(connection));
            Assert.Equal(2, Positions(connection).Count);
        }

        [Theory]
        [InlineData(1200, "1200")]
        [InlineData(7700, "7700")]
        [InlineData(0, "0000")]
        public void TheSquawkIsInTheLine(int squawk, string text)
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123", squawk: squawk));

            Tick(connection);

            Assert.StartsWith("@N:DLH123:" + text + ":", connection.Sent[0]);
        }

        [Theory]
        [InlineData(0x1200, 1200)]
        [InlineData(0x7700, 7700)]
        [InlineData(0x0000, 0)]
        [InlineData(0x1280, 0)]
        [InlineData(-1, 0)]
        [InlineData(0x10000, 0)]
        public void TheSimulatorsTransponderCodeIsBcd(int bcd, int code)
        {
            Assert.Equal(code, SimViewTraffic.Squawk(bcd));
        }

        [Fact]
        public void TheRealNameAndTheFlightPlanAreAnswered()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123", nickname: "Pia"));

            gateway.OnLine(connection, "$CQEDDM_TWR:DLH123:RN");
            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:FP:DLH123");
            gateway.OnLine(connection, "$CQEDDM_TWR:DLH123:FP");

            Assert.Equal(
            [
                "$CRDLH123:EDDM_TWR:RN:Pia::1",
                "$FPDLH123:EDDM_TWR:I:A320:450:EDDM:::FL350:EDDF:0:0:0:0:EDDS:RMK/TEST:ANORA T104 TOBAK",
                "$FPDLH123:EDDM_TWR:I:A320:450:EDDM:::FL350:EDDF:0:0:0:0:EDDS:RMK/TEST:ANORA T104 TOBAK",
            ], connection.Sent);
        }

        [Fact]
        public void TheAssignedCallsignIsWhatIsQueried()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            traffic.List.Add(Aircraft(2, 1, "DLH123"));
            Tick(connection);
            string other = Positions(connection).Select(p => FsdLine.TryParse(p, out FsdLine l) ? l.From : "").First(c => c != "DLH123");
            connection.Clear();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:FP:" + other);

            Assert.StartsWith("$FP" + other + ":EDDM_TWR:", connection.Sent.Single());
        }

        [Fact]
        public void AnUnknownAircraft_GetsNoFlightPlanError_OrNothing()
        {
            FakeFsdConnection connection = LogIn();

            gateway.OnLine(connection, "$CQEDDM_TWR:SERVER:FP:NOBODY");
            gateway.OnLine(connection, "$CQEDDM_TWR:NOBODY:RN");

            Assert.Equal(["$ERSERVER:EDDM_TWR:008:NOBODY:No flight plan"], connection.Sent);
        }

        [Fact]
        public void WithoutTraffic_NoAircraftAreServed()
        {
            var bare = new AtcGateway(_ => { });
            var connection = new FakeFsdConnection();
            bare.OnConnected(connection);
            bare.OnLine(connection, "$IDEDDM_TWR:SERVER:ffff:EuroScope:3:0:1234567:0:0");
            bare.OnLine(connection, LoginTwr);
            connection.Clear();

            bare.Tick(now);
            bare.OnLine(connection, "$CQEDDM_TWR:SERVER:FP:DLH123");

            Assert.Equal(["$ERSERVER:EDDM_TWR:008:DLH123:No flight plan"], connection.Sent);
        }

        [Fact]
        public void ANewLogin_GetsEverythingAgain()
        {
            FakeFsdConnection first = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            Tick(first);
            gateway.OnLine(first, "#DAEDDM_TWR:1234567");
            Assert.Null(gateway.Station);

            FakeFsdConnection second = LogIn();
            Tick(second, 100);

            Assert.Single(Positions(second));
            Assert.Single(Plans(second));
        }

        [Fact]
        public void Traffic_IsNotSentToAClientThatHasLoggedOff()
        {
            FakeFsdConnection connection = LogIn();
            traffic.List.Add(Aircraft(1, 1, "DLH123"));
            gateway.OnLine(connection, "#DAEDDM_TWR:1234567");
            connection.Clear();

            Tick(connection, 5000);

            Assert.Empty(connection.Sent);
        }
    }

    /// <summary>The mapping of a JoinFS flight plan to the fields of <c>$FP</c>.</summary>
    public class AtcFlightPlansTests
    {
        [Theory]
        [InlineData("IFR", 'I')]
        [InlineData("I", 'I')]
        [InlineData("ifr", 'I')]
        [InlineData("VFR", 'V')]
        [InlineData("SVFR", 'S')]
        [InlineData("DVFR", 'D')]
        [InlineData("", 'V')]
        [InlineData(null, 'V')]
        public void TheRules(string? text, char rules)
        {
            Assert.Equal(rules, AtcFlightPlans.Rules(text!));
        }

        [Theory]
        [InlineData("N0450", 450)]
        [InlineData("450", 450)]
        [InlineData("K0833", 450)]
        [InlineData("M078", 0)]
        [InlineData("", 0)]
        [InlineData("fast", 0)]
        [InlineData("N04500000", 0)]
        public void TheSpeedInKnots(string text, int knots)
        {
            Assert.Equal(knots, AtcFlightPlans.Knots(text));
        }

        [Fact]
        public void AnEmptyPlan_IsAValidLine_ThatParsesBack()
        {
            FsdLine line = AtcFlightPlans.ToFsd("DLH123", "*A", AtcFlightPlanData.Empty).ToLine();

            Assert.True(FsdLine.TryParse(line.ToString(), out FsdLine parsed));
            Assert.True(FsdFlightPlan.TryParse(parsed, out FsdFlightPlan plan));
            Assert.Equal("DLH123", plan.From);
            Assert.Equal('V', plan.Rules);
            Assert.Equal(17, parsed.Fields.Count);
        }

        [Fact]
        public void AColonOrAControlCharacterInAFreeFieldDoesNotBreakTheLine()
        {
            var plan = new AtcFlightPlanData("IFR", "A3:20", "N0450", "ED:DM", "FL350", "EDDF", "", "RMK/A:B\r\nC", "DCT:X\tY");

            string text = AtcFlightPlans.ToFsd("DLH123", "*A", plan).ToLine().ToString();

            Assert.True(FsdLine.TryParse(text, out FsdLine parsed));
            Assert.Equal(17, parsed.Fields.Count);
            Assert.Equal("RMK/A B  C", parsed.Field(15));
            Assert.Equal("DCT:X Y", parsed.Field(16));
        }

        [Fact]
        public void TheDepartureAndTheDestinationAreUpperCase()
        {
            var plan = new AtcFlightPlanData("IFR", "A320", "", "eddm", "", "eddf", "edds", "", "");

            FsdFlightPlan fp = AtcFlightPlans.ToFsd("DLH123", "*A", plan);

            Assert.Equal(["EDDM", "EDDF", "EDDS"], [fp.Departure, fp.Destination, fp.Alternate]);
        }
    }
}
