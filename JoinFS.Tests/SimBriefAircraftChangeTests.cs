namespace JoinFS.Tests
{
    public class SimBriefAircraftChangeTests
    {
        [Theory]
        // the OFP was made for this aircraft
        [InlineData("A20N", "A20N", true)]
        [InlineData("a20n", "A20N ", true)]
        // the latest OFP is from the previous aircraft (seen: an H145 plan kept for an A320neo)
        [InlineData("EC45", "A20N", false)]
        [InlineData("A320", "A20N", false)]
        // nothing to compare: as before, the plan is applied
        [InlineData("", "A20N", true)]
        [InlineData("EC45", "", true)]
        [InlineData(null, "A20N", true)]
        public void APlanFits_OnlyAnAircraftOfItsType(string planType, string aircraftType, bool fits)
        {
            Assert.Equal(fits, Sim.SimBriefPlanFitsAircraft(planType, aircraftType));
        }

        [Theory]
        // the OFP was filed under the callsign now entered in the sim
        [InlineData("DLH123", "DLH123", "D-AINA", true)]
        [InlineData("dlh123 ", "DLH123", "D-AINA", true)]
        // a GA flight filed under its registration
        [InlineData("D-EJOE", "D-EJOE", "D-EJOE", true)]
        [InlineData("D-AINA", "DLH123", "D-AINA", true)]
        // an older OFP: the callsign just entered stays
        [InlineData("DLH122", "DLH123", "D-AINA", false)]
        // an OFP without a callsign matches nothing
        [InlineData("", "DLH123", "D-AINA", false)]
        [InlineData(null, "DLH123", "D-AINA", false)]
        public void APlanFitsACallsignChange_OnlyWhenFiledUnderIt(string planCallsign, string callsign, string registration, bool fits)
        {
            Assert.Equal(fits, Sim.SimBriefPlanFitsCallsign(planCallsign, callsign, registration));
        }

        [Theory]
        // airline + flight number from the FMC / ATC setup, the tail number from the aircraft
        [InlineData("A20N", "KLM511", "PH-EXA", "KLM511", "KLM", "PH-EXA", "KLM511")]
        // no airline: the flight ID is the registration
        [InlineData("C172", "D-EJOE", "D-EJOE", "D-EJOE", "", "D-EJOE", "")]
        // no flight ID (X-Plane 11): the tail number
        [InlineData("C172", "", "N172SP", "N172SP", "", "N172SP", "")]
        // an older plugin sends only the tail number, as the callsign
        [InlineData("C172", "N172SP", "", "N172SP", "", "N172SP", "")]
        // an airline flight from an older plugin, or without a tail number: no registration
        [InlineData("A20N", "KLM511", "", "KLM511", "KLM", "", "KLM511")]
        public void XPlaneIdentity_FromTheFlightIdAndTheTailNumber(string type, string flightId, string tail,
            string callsign, string airline, string registration, string flightNumber)
        {
            ResolvedAircraftIdentity identity = ResolvedAircraftIdentity.FromXPlane(type, flightId, tail);

            Assert.Equal(new ResolvedAircraftIdentity(callsign, type, airline, registration, flightNumber), identity);
        }

        [Fact]
        public void AnotherType_IsANewFlight_ANewCallsignIsNot()
        {
            var a320 = new ResolvedAircraftIdentity("DLH123", "A20N", "DLH", "D-AINA", "DLH123");

            Assert.True(ResolvedAircraftIdentity.IsNewFlight(null, a320));
            Assert.True(ResolvedAircraftIdentity.IsNewFlight(a320, a320 with { IcaoType = "EC45" }));
            Assert.False(ResolvedAircraftIdentity.IsNewFlight(a320, a320 with { Callsign = "DLH124", FlightNumber = "DLH124" }));
            Assert.False(ResolvedAircraftIdentity.IsNewFlight(a320, a320 with { Registration = "D-AINB" }));
            Assert.False(ResolvedAircraftIdentity.IsNewFlight(a320, a320 with { IcaoType = "a20n" }));
        }

        [Fact]
        public void ClearRoute_KeepsTheIdentity_AndDropsTheFlight()
        {
            var plan = new Sim.FlightPlan
            {
                callsign = "EWG320", icaoType = "A20N", icaoAirline = "EWG", registration = "D-AEWA",
                departure = "LOWI", destination = "EDDS", alternate = "EDDM", route = "DCT", remarks = "RMK",
                rules = "IFR", speed = "N0450", altitude = "FL360",
            };

            plan.ClearRoute();

            Assert.Equal("EWG320", plan.callsign);
            Assert.Equal("A20N", plan.icaoType);
            Assert.Equal("EWG", plan.icaoAirline);
            Assert.Equal("D-AEWA", plan.registration);
            Assert.All(new[] { plan.departure, plan.destination, plan.alternate, plan.route, plan.remarks, plan.rules, plan.speed, plan.altitude },
                field => Assert.Equal("", field));
        }
    }
}
