using JoinFS.Fsd;
using JoinFS.Net;

namespace JoinFS.Tests.Atc
{
    /// <summary>docs/ATC/atc-fsd-plan.md stage 11: a controller's flight plan amendment is relayed, kept per aircraft and served, and forgotten.</summary>
    public class AtcGatewayAmendmentTests
    {
        static readonly NodeId Ben = NodeId.FromLegacy(0x0A000001, 6112, 4);
        static readonly AtcFlightPlanData Plan = new("IFR", "A320", "N0450", "EDDM", "FL350", "EDDF", "EDDS", "", "");
        static readonly AtcFlightPlanData PilotEdit = new("IFR", "A320", "N0450", "EDDM", "FL390", "EDDF", "EDDS", "", "");

        // Carla's amendment: cruise level FL360 and a route, sent as EuroScope does, to SERVER
        const string Amendment = "$AMEDDM_TWR:SERVER:DLH4TK:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT";
        const string AmendedPlanTo = "$FPDLH4TK:EDDM_TWR:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT";
        const string PilotsPlanTo = "$FPDLH4TK:EDDM_TWR:I:A320:450:EDDM:::FL350:EDDF:0:0:0:0:EDDS::";

        readonly FakeAtcMesh mesh = new();
        readonly FakeTraffic traffic = new();
        readonly AtcGateway gateway;
        readonly FakeFsdConnection client = new();

        public AtcGatewayAmendmentTests()
        {
            gateway = new AtcGateway(_ => { }, traffic, mesh);
            mesh.Come(new AtcRemoteStation(Ben, 0, "EDDM_APP", "Ben", 5, FsdFacility.Approach, 100, 119_150, 48.1, 11.5));
            traffic.List.Add(Pia(Plan));

            gateway.OnConnected(client);
            gateway.OnLine(client, "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100");
            gateway.OnLine(client, "%EDDM_TWR:18700:4:50:4:48.35377:11.78609:0");
            gateway.Tick(100_000); // the feed learns the aircraft
            client.Clear();
        }

        static AtcAircraft Pia(AtcFlightPlanData plan) => new(1, 1, "DLH4TK", "Pia", 1200, 48.1, 11.5, 1500, 120, 0, 0, 90, plan);

        void Say(string line) => gateway.OnLine(client, line);

        string[] ServedPlan()
        {
            client.Clear();
            Say("$CQEDDM_TWR:SERVER:FP:DLH4TK");
            return client.Sent.ToArray();
        }

        [Fact]
        public void AnAmendment_GoesToEveryStationNode_AddressedToAllControllers_FromTheClientsStation()
        {
            Say(Amendment);

            Assert.Equal([("*", "$AMEDDM_TWR:@94835:DLH4TK:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT")], mesh.Relayed);
            Assert.Empty(client.Sent); // no echo, no error
        }

        [Fact]
        public void AnAmendmentToAllControllers_IsRelayedOnce_AsWell()
        {
            Say("$AMEDDM_TWR:@94835:DLH4TK:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT");

            Assert.Single(mesh.Relayed);
            Assert.Equal(AmendedPlanTo, ServedPlan().Single());
        }

        [Fact]
        public void AnAmendmentWithAForeignFrom_IsRelayedFromTheClientsOwnCallsign()
        {
            Say("$AMEDDM_APP:SERVER:DLH4TK:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT");

            Assert.StartsWith("$AMEDDM_TWR:@94835:DLH4TK:", mesh.Relayed.Single().Line);
        }

        [Fact]
        public void AMalformedAmendment_IsIgnored()
        {
            Say("$AMEDDM_TWR:SERVER:DLH4TK:I:A320");
            Say("$AMEDDM_TWR:SERVER:DLH4TK:I:A320:fast:EDDM:::FL360:EDDF:1:0:2:0:EDDS::");

            Assert.Empty(mesh.Relayed);
            Assert.Empty(client.Sent);
            Assert.Equal(PilotsPlanTo, ServedPlan().Single());
        }

        [Fact]
        public void TheLastAmendment_AnswersTheFlightPlanQueries_OfItsOwnClient()
        {
            Say(Amendment);

            Assert.Equal(AmendedPlanTo, ServedPlan().Single());

            client.Clear();
            Say("$CQEDDM_TWR:DLH4TK:FP");
            Assert.Equal(["$FPDLH4TK:EDDM_TWR:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT"], client.Sent);

            Say("$AMEDDM_TWR:SERVER:DLH4TK:I:A320:450:EDDM:::FL370:EDDF:1:0:2:0:EDDS::");
            Assert.Contains(":FL370:", ServedPlan().Single()); // a second amendment replaces the first
        }

        [Fact]
        public void AnAmendmentIsPerAircraft()
        {
            traffic.List.Add(new AtcAircraft(2, 1, "BAW9", "Olaf", 1200, 48.2, 11.6, 1500, 120, 0, 0, 90, Plan));
            gateway.Tick(101_000);

            Say(Amendment);

            client.Clear();
            Say("$CQEDDM_TWR:SERVER:FP:BAW9");
            Assert.Equal(["$FPBAW9:EDDM_TWR:I:A320:450:EDDM:::FL350:EDDF:0:0:0:0:EDDS::"], client.Sent);
        }

        [Fact]
        public void AnAmendmentFromAnotherNode_IsWrittenToTheClient_KeptAndServed()
        {
            const string line = "$AMEDDM_APP:@94835:DLH4TK:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::DIRECT";
            mesh.Receive(line);

            Assert.Equal([line], client.Sent);
            Assert.Equal(AmendedPlanTo, ServedPlan().Single());
            Assert.Empty(mesh.Relayed); // a received amendment is not sent on
        }

        [Fact]
        public void AnAmendmentForAnAircraftThisNodeDoesNotKnow_IsStillWritten_AndNothingIsKept()
        {
            const string line = "$AMEDDM_APP:@94835:XYZ1:I:A320:450:EDDM:::FL360:EDDF:1:0:2:0:EDDS::";
            mesh.Receive(line);

            Assert.Equal([line], client.Sent);
            Assert.Equal(PilotsPlanTo, ServedPlan().Single());
        }

        [Fact]
        public void ThePilotsOwnPlan_IsNotChanged_ByAnAmendment()
        {
            Say(Amendment);
            client.Clear();

            gateway.Tick(110_000);

            // nothing is sent for the aircraft but its position: no $FP, the pilot's plan did not change
            Assert.DoesNotContain(client.Sent, l => l.StartsWith("$FP"));
            Assert.Equal(AmendedPlanTo, ServedPlan().Single());
        }

        [Fact]
        public void TheAmendment_IsForgotten_WhenThePilotsOwnPlanChanges()
        {
            Say(Amendment);
            traffic.List[0] = Pia(PilotEdit);
            client.Clear();

            gateway.Tick(101_000);

            // the new plan of the pilot goes to the controllers as an ordinary $FP, and is what a query gets
            Assert.Contains(client.Sent, l => l.StartsWith("$FPDLH4TK:*A:") && l.Contains(":FL390:"));
            Assert.Contains(":FL390:", ServedPlan().Single());
        }

        [Fact]
        public void TheAmendment_IsForgotten_WhenThePilotsPlanChangesBeforeTheNextTick()
        {
            Say(Amendment);
            traffic.List[0] = Pia(PilotEdit);

            Assert.Contains(":FL390:", ServedPlan().Single());
        }

        [Fact]
        public void TheAmendment_IsForgotten_WhenTheAircraftLeaves()
        {
            Say(Amendment);
            traffic.List.Clear();
            gateway.Tick(101_000);
            gateway.Tick(106_000); // gone for 5 s: #DP
            traffic.List.Add(Pia(Plan));
            gateway.Tick(107_000);

            Assert.Equal(PilotsPlanTo, ServedPlan().Single());
        }

        [Fact]
        public void TheAmendment_IsForgotten_WhenTheClientLogsOffAndANewOneLogsIn()
        {
            Say(Amendment);
            Say("#DAEDDM_TWR:1234567");

            var next = new FakeFsdConnection();
            gateway.OnConnected(next);
            gateway.OnLine(next, "#AAEDDM_TWR:SERVER:Carla:1234567:token:4:100");
            gateway.Tick(120_000);
            next.Clear();
            gateway.OnLine(next, "$CQEDDM_TWR:SERVER:FP:DLH4TK");

            Assert.Equal([PilotsPlanTo], next.Sent);
        }
    }
}
