using JoinFS.Net;

namespace JoinFS.Tests.Session;

/// <summary>
/// The send path only derives the airline into what it sends. It never writes back to the sim-owned plan.
/// </summary>
public class SimSenderFlightPlanTests
{
    [Fact]
    public void BroadcastDerivesTheAirlineWithoutMutatingTheSourcePlan()
    {
        var rig = new SessionRig();
        var sender = new SimSender(rig.Outbox, rig.Session, rig.Profile);
        var plan = new Sim.FlightPlan { callsign = "DLH1234", icaoAirline = "" };

        sender.BroadcastFlightPlanUpdate(7, plan);

        Assert.Equal("", plan.icaoAirline);
        Assert.Equal("DLH", Assert.Single(rig.Outbox.Messages<FlightPlanUpdate>()).IcaoAirline);
    }

    [Fact]
    public void BroadcastKeepsAnAirlineThatIsAlreadySet()
    {
        var rig = new SessionRig();
        var sender = new SimSender(rig.Outbox, rig.Session, rig.Profile);
        var plan = new Sim.FlightPlan { callsign = "DLH1234", icaoAirline = "BAW" };

        sender.BroadcastFlightPlanUpdate(7, plan);

        Assert.Equal("BAW", Assert.Single(rig.Outbox.Messages<FlightPlanUpdate>()).IcaoAirline);
    }
}
