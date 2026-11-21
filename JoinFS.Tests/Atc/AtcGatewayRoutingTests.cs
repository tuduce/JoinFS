using JoinFS.Fsd;
using JoinFS.Net;

namespace JoinFS.Tests.Atc
{
    /// <summary>docs/ATC/atc-fsd-plan.md stage 8, §4.3: where a client's line goes, one test per row; with a fake mesh and fake traffic.</summary>
    public class AtcGatewayRoutingTests
    {
        static readonly NodeId Ben = NodeId.FromLegacy(0x0A000001, 6112, 4);
        static readonly AtcFlightPlanData Plan = new("IFR", "A320", "N0450", "EDDM", "FL350", "EDDF", "EDDS", "", "");

        readonly FakeAtcMesh mesh = new();
        readonly FakeTraffic traffic = new();
        readonly AtcGateway gateway;
        readonly FakeFsdConnection client = new();

        public AtcGatewayRoutingTests()
        {
            gateway = new AtcGateway(_ => { }, traffic, mesh);
            mesh.Come(new AtcRemoteStation(Ben, 0, "EDDM_APP", "Ben", 5, FsdFacility.Approach, 100, 119_150, 48.1, 11.5));
            traffic.List.Add(new AtcAircraft(1, 1, "DLH4TK", "Pia", 1200, 48.1, 11.5, 1500, 120, 0, 0, 90, Plan));

            gateway.OnConnected(client);
            gateway.OnLine(client, "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100");
            gateway.OnLine(client, "%EDDM_TWR:18700:4:50:4:48.35377:11.78609:0");
            gateway.Tick(100_000); // the feed learns the aircraft
            client.Clear();
        }

        void Say(string line) => gateway.OnLine(client, line);

        // ---- a station of another node

        [Fact]
        public void ALineToAStationOfAnotherNode_IsRelayed_AsTheClientSentIt()
        {
            Say("$HOEDDM_TWR:EDDM_APP:DLH4TK");
            Say("#TMEDDM_TWR:EDDM_APP:see you: at 3:5");
            Say("$CQEDDM_TWR:EDDM_APP:C?");

            Assert.Equal(
            [
                ("EDDM_APP", "$HOEDDM_TWR:EDDM_APP:DLH4TK"),
                ("EDDM_APP", "#TMEDDM_TWR:EDDM_APP:see you: at 3:5"),
                ("EDDM_APP", "$CQEDDM_TWR:EDDM_APP:C?"),
            ], mesh.Relayed);
            Assert.Empty(client.Sent);
        }

        [Fact]
        public void EveryRelayedVerb_GoesToAStation()
        {
            string[] lines =
            [
                "$CQEDDM_TWR:EDDM_APP:ATC", "$CREDDM_TWR:EDDM_APP:ATC:Y:EDDM_TWR", "#PCEDDM_TWR:EDDM_APP:CCP:SC:DLH4TK:ABC",
                "$HOEDDM_TWR:EDDM_APP:DLH4TK", "$HAEDDM_TWR:EDDM_APP:DLH4TK", "#TMEDDM_TWR:EDDM_APP:hello",
                "$PIEDDM_TWR:EDDM_APP:123", "$POEDDM_TWR:EDDM_APP:123",
            ];
            foreach (string line in lines) Say(line);

            Assert.Equal(lines, mesh.Relayed.Select(r => r.Line));
        }

        // ---- group addresses

        [Theory]
        [InlineData("@94835")]
        [InlineData("@49999")]
        [InlineData("*A")]
        [InlineData("*")]
        [InlineData("@18700")]
        [InlineData("@21950&@19600")]
        public void ALineToAGroup_GoesToEveryNodeWithAStation(string to)
        {
            string line = "$CQEDDM_TWR:" + to + ":IT:DLH4TK";
            Say(line);

            Assert.Equal([("*", line)], mesh.Relayed);
            Assert.Empty(client.Sent);
        }

        [Fact]
        public void TheTrackingLines_AreGroupLines()
        {
            Say("$CQEDDM_TWR:@94835:WH:DLH4TK");
            Say("$CQEDDM_TWR:@94835:IT:DLH4TK");
            Say("#PCEDDM_TWR:@94835:CCP:IH:DLH4TK");
            Say("#PCEDDM_TWR:@94835:CCP:TA:DLH4TK:5000");
            Say("#TMEDDM_TWR:@49999:chat");

            Assert.Equal(5, mesh.Relayed.Count);
            Assert.All(mesh.Relayed, r => Assert.Equal("*", r.To));
        }

        [Fact]
        public void ALineToThePilots_IsDroppedWithoutAnError()
        {
            Say("#TMEDDM_TWR:@94836:wind 270");

            Assert.Empty(mesh.Relayed);
            Assert.Empty(client.Sent);
        }

        // ---- aircraft

        [Fact]
        public void AQueryToAnAircraft_IsAnsweredHere_AndNotRelayed()
        {
            Say("$CQEDDM_TWR:DLH4TK:RN");

            Assert.Equal(["$CRDLH4TK:EDDM_TWR:RN:Pia::1"], client.Sent);
            Assert.Empty(mesh.Relayed);
        }

        [Fact]
        public void AnythingElseToAnAircraft_IsDropped()
        {
            Say("#PCEDDM_TWR:DLH4TK:CCP:SC:DLH4TK:ABC");
            Say("#TMEDDM_TWR:DLH4TK:hello");

            Assert.Empty(client.Sent);
            Assert.Empty(mesh.Relayed);
        }

        // ---- the unknown row

        [Fact]
        public void ALineToAnUnknownCallsign_GetsNoSuchCallsign()
        {
            Say("#TMEDDM_TWR:EDDM_GND:anyone there");

            Assert.Equal(["$ERSERVER:EDDM_TWR:007:EDDM_GND:No such callsign"], client.Sent);
            Assert.Empty(mesh.Relayed);
        }

        [Fact]
        public void AStationThatLeft_IsNoLongerARelayTarget()
        {
            mesh.Go(new AtcRemoteStation(Ben, 0, "EDDM_APP", "Ben", 5, FsdFacility.Approach, 100, 119_150, 48.1, 11.5));
            client.Clear();

            Say("$HOEDDM_TWR:EDDM_APP:DLH4TK");

            Assert.Contains("007", client.Sent[0]);
            Assert.Empty(mesh.Relayed);
        }

        // ---- what never goes anywhere

        [Theory]
        [InlineData("$FPEDDM_TWR:*A:I:A320:450:EDDM:0:0:FL350:EDDF:1:0:2:0:EDDS::")]
        [InlineData("$AMEDDM_TWR:SERVER:DLH4TK:I:A320")]
        [InlineData("#SBEDDM_TWR:EDDM_APP:PIR:x")]
        [InlineData("$!!EDDM_TWR:EDDM_APP:hello")]
        [InlineData("$ZREDDM_TWR:SERVER:abc")]
        [InlineData("#AAEDDM_GND:SERVER:Carla:1234567:token:4:100")]
        public void AVerbThatIsNotRelayed_IsIgnored(string line)
        {
            Say(line);

            Assert.Empty(mesh.Relayed);
            Assert.Empty(client.Sent);
        }

        [Fact]
        public void ALineToItself_IsIgnored()
        {
            Say("#TMEDDM_TWR:EDDM_TWR:me");

            Assert.Empty(mesh.Relayed);
            Assert.Empty(client.Sent);
        }

        [Fact]
        public void LineToServer_AreStillAnsweredHere()
        {
            Say("$PIEDDM_TWR:SERVER:42");

            Assert.Equal(["$POSERVER:EDDM_TWR:42"], client.Sent);
            Assert.Empty(mesh.Relayed);
        }

        [Fact]
        public void WithoutAMesh_AnyOtherCallsignGetsNoSuchCallsign_AndAGroupIsDropped()
        {
            var alone = new AtcGateway(_ => { });
            var connection = new FakeFsdConnection();
            alone.OnConnected(connection);
            alone.OnLine(connection, "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100");
            connection.Clear();

            alone.OnLine(connection, "$CQEDDM_TWR:@94835:WH:DLH4TK");
            alone.OnLine(connection, "#TMEDDM_TWR:EDDM_APP:hi");

            Assert.Single(connection.Sent);
            Assert.Contains("$ERSERVER:EDDM_TWR:007:EDDM_APP", connection.Sent[0]);
        }

        // ---- a line from another node

        [Fact]
        public void ALineFromTheMesh_ToThisStationOrAGroup_IsWrittenToTheClient_Unchanged()
        {
            mesh.Receive("$HOEDDM_APP:EDDM_TWR:DLH4TK");
            mesh.Receive("$CQEDDM_APP:@94835:IT:DLH4TK");
            mesh.Receive("#TMEDDM_APP:@49999:chat: with colons");
            mesh.Receive("#PCEDDM_APP:*:CCP:SC:DLH4TK:ABC");

            Assert.Equal(
            [
                "$HOEDDM_APP:EDDM_TWR:DLH4TK",
                "$CQEDDM_APP:@94835:IT:DLH4TK",
                "#TMEDDM_APP:@49999:chat: with colons",
                "#PCEDDM_APP:*:CCP:SC:DLH4TK:ABC",
            ], client.Sent);
        }

        [Fact]
        public void ALineFromTheMesh_ForSomeoneElse_IsNotWritten()
        {
            mesh.Receive("$HOEDDM_APP:EDDM_GND:DLH4TK");
            mesh.Receive("#TMEDDM_APP:@94836:to the pilots");
            mesh.Receive("not a line");

            Assert.Empty(client.Sent);
        }

        [Fact]
        public void ALineFromTheMesh_WithNoClientLoggedIn_GoesNowhere()
        {
            var empty = new FakeAtcMesh();
            var idle = new AtcGateway(_ => { }, null, empty);

            empty.Receive("$HOEDDM_APP:EDDM_TWR:DLH4TK"); // no exception, nothing to write to

            Assert.Null(idle.Station);
        }
    }
}
