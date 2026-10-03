namespace JoinFS.Tests;

/// <summary>
/// Exercises Sim.ResolveCallsign (internal, exposed via InternalsVisibleTo) directly - regression
/// coverage for the callsign-duplication bug where ATC FLIGHT NUMBER already held a full pre-existing
/// callsign (from before JoinFS read that field at all) and got the airline code glued onto it again.
/// </summary>
public class ResolveCallsignTests
{
    [Fact]
    public void NumericFlightNumber_ConcatenatesWithAirline()
    {
        string result = Sim.ResolveCallsign("DLH", "1234", "D-ALEX");

        Assert.Equal("DLH1234", result);
    }

    [Fact]
    public void FlightNumberAlreadyStartsWithAirline_IsNotDuplicated()
    {
        string result = Sim.ResolveCallsign("DLH", "DLH1234", "D-ALEX");

        Assert.Equal("DLH1234", result);
    }

    [Fact]
    public void FlightNumberStartsWithAirline_CaseInsensitive_IsNotDuplicated()
    {
        string result = Sim.ResolveCallsign("DLH", "dlh1234", "D-ALEX");

        Assert.Equal("dlh1234", result);
    }

    [Fact]
    public void NonNumericFlightNumber_IsUsedAsIs()
    {
        string result = Sim.ResolveCallsign("DLH", "FSC739", "D-ALEX");

        Assert.Equal("FSC739", result);
    }

    [Fact]
    public void FlightNumberWithTrailingLetterSuffix_ConcatenatesWithAirline()
    {
        // Real-world flight numbers routinely carry a trailing letter suffix (e.g. a Eurowings
        // "34U") without being a complete pre-existing callsign - this must still combine with the
        // airline into "EWG34U", not broadcast bare as "34U".
        string result = Sim.ResolveCallsign("EWG", "34U", "D-ALEX");

        Assert.Equal("EWG34U", result);
    }

    [Fact]
    public void MissingAirlineOrFlightNumber_FallsBackToTailNumber()
    {
        Assert.Equal("D-ALEX", Sim.ResolveCallsign("", "1234", "D-ALEX"));
        Assert.Equal("D-ALEX", Sim.ResolveCallsign("DLH", "", "D-ALEX"));
    }
}
