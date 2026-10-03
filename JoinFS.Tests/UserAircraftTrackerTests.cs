namespace JoinFS.Tests;

/// <summary>
/// Own-aircraft change detection (UserAircraftTracker): resolution only runs when the raw SimConnect
/// data changes, and a change is reported only when the resolved identity really differs.
/// </summary>
public class UserAircraftTrackerTests
{
    static UserAircraftInfo Raw(string type = "A320", string callsign = "D-AIZZ", string flightNumber = "", string airline = "", string livery = "") =>
        new(type, "Model", callsign, flightNumber, airline, livery, "", true, "Airplane", 1, 2);

    static ResolvedAircraftIdentity Id(string callsign = "D-AIZZ", string icaoType = "A320", string airline = "") =>
        new(callsign, icaoType, airline, callsign, "");

    [Fact]
    public void IdenticalRawDataIsNotResolvedAgain()
    {
        var tracker = new UserAircraftTracker();
        int resolves = 0;
        ResolvedAircraftIdentity Resolve(UserAircraftInfo raw) { resolves++; return Id(); }

        tracker.Check(Raw(), Resolve, out _);
        tracker.Check(Raw(), Resolve, out _);
        tracker.Check(Raw(), Resolve, out _);

        Assert.Equal(1, resolves);
    }

    [Fact]
    public void FirstSightingRecordsWithoutReportingAChange()
    {
        var tracker = new UserAircraftTracker();

        bool changed = tracker.Check(Raw(), _ => Id(), out _);

        Assert.False(changed);
    }

    [Fact]
    public void ResolutionChangingOnTheSameRawDataIsNotAnAircraftChange()
    {
        // point 2: the index was not ready at first sighting, so the first resolve fell back to the raw
        // type. Later polls with the same raw data resolve from config. That is not an aircraft change.
        var tracker = new UserAircraftTracker();
        var raw = Raw(type: "A20N");
        tracker.Check(raw, _ => Id(icaoType: "A20N"), out _);

        bool changed = tracker.Check(raw, _ => Id(icaoType: "A320"), out _);

        Assert.False(changed);
    }

    [Fact]
    public void ChangedRawDataWithANewResolvedIdentityIsAChange()
    {
        var tracker = new UserAircraftTracker();
        tracker.Check(Raw(callsign: "D-AIZZ"), _ => Id(callsign: "D-AIZZ"), out _);

        bool changed = tracker.Check(Raw(callsign: "D-AIYY"), _ => Id(callsign: "D-AIYY"), out var now);

        Assert.True(changed);
        Assert.Equal("D-AIYY", now.Callsign);
    }

    [Fact]
    public void AirlineChangeAloneCountsAsAChange()
    {
        // point 3: DLH A320 -> BAW A320 keeps the same type and callsign shape, but the airline must refresh
        var tracker = new UserAircraftTracker();
        tracker.Check(Raw(airline: "DLH"), _ => Id(airline: "DLH"), out _);

        bool changed = tracker.Check(Raw(airline: "BAW"), _ => Id(airline: "BAW"), out var now);

        Assert.True(changed);
        Assert.Equal("BAW", now.IcaoAirline);
    }

    [Fact]
    public void ApplyToRefreshesEveryFieldThatMatchingAndBroadcastReadRegistrationAirlineAndCallsign()
    {
        var plan = new Sim.FlightPlan { callsign = "OLD1", callsignSetByUser = true, icaoType = "A319", icaoAirline = "DLH", registration = "D-AIXX", flightNumber = "1" };
        var id = new ResolvedAircraftIdentity("BAW123", "A320", "BAW", "G-EUXA", "123");

        id.ApplyTo(plan);

        Assert.Equal("BAW123", plan.callsign);
        Assert.False(plan.callsignSetByUser);
        Assert.Equal("A320", plan.icaoType);
        Assert.Equal("BAW", plan.icaoAirline);
        Assert.Equal("G-EUXA", plan.registration);
        Assert.Equal("123", plan.flightNumber);
    }
}
