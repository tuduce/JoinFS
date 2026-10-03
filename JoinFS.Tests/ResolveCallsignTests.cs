namespace JoinFS.Tests;

/// <summary>
/// Exercises CallsignRules.Resolve directly - regression
/// coverage for the callsign-duplication bug where ATC FLIGHT NUMBER already held a full pre-existing
/// callsign (from before JoinFS read that field at all) and got the airline code glued onto it again.
/// </summary>
public class ResolveCallsignTests
{
    [Fact]
    public void NumericFlightNumber_ConcatenatesWithAirline()
    {
        string result = CallsignRules.Resolve("DLH", "1234", "D-ALEX");

        Assert.Equal("DLH1234", result);
    }

    [Fact]
    public void FlightNumberAlreadyStartsWithAirline_IsNotDuplicated()
    {
        string result = CallsignRules.Resolve("DLH", "DLH1234", "D-ALEX");

        Assert.Equal("DLH1234", result);
    }

    [Fact]
    public void FlightNumberStartsWithAirline_CaseInsensitive_IsNotDuplicated()
    {
        string result = CallsignRules.Resolve("DLH", "dlh1234", "D-ALEX");

        Assert.Equal("dlh1234", result);
    }

    [Fact]
    public void NonNumericFlightNumber_IsUsedAsIs()
    {
        string result = CallsignRules.Resolve("DLH", "FSC739", "D-ALEX");

        Assert.Equal("FSC739", result);
    }

    [Fact]
    public void MissingAirlineOrFlightNumber_FallsBackToTailNumber()
    {
        Assert.Equal("D-ALEX", CallsignRules.Resolve("", "1234", "D-ALEX"));
        Assert.Equal("D-ALEX", CallsignRules.Resolve("DLH", "", "D-ALEX"));
    }
}
