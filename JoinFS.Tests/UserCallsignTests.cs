namespace JoinFS.Tests;

/// <summary>
/// The callsign of the user's own MSFS aircraft (CallsignRules.ResolveUser): the one entered in the sim comes
/// before the tail number. Values as MSFS 2024 reported them (ATC ID, ATC AIRLINE, ATC FLIGHT NUMBER).
/// </summary>
public class UserCallsignTests
{
    static AirlineDirectory Dir => AirlineDirectory.Bundled;

    [Theory]
    // FlyByWire A380X, BAW388 entered: ATC AIRLINE holds the whole callsign, no airline configured
    [InlineData("BAW388", "", "388", "D-A388", "BAW388")]
    // the same entered on a Eurowings livery: not EWG388
    [InlineData("BAW388", "EWG", "388", "D-A388", "BAW388")]
    [InlineData("VOI234", "", "D-EELK", "D-EELK", "VOI234")]
    // an airline name in ATC AIRLINE: the configured airline with the flight number
    [InlineData("EUROWINGS", "EWG", "320", "", "EWG320")]
    // nothing entered and no airline: the tail number
    [InlineData("FlyByWire", "", "380", "FBWA380X", "FBWA380X")]
    [InlineData("", "", "", "D-EELK", "D-EELK")]
    // a flight number that is a complete callsign by itself
    [InlineData("", "", "DLH123", "D-AIAB", "DLH123")]
    public void The_callsign_entered_comes_before_the_tail_number(string atcAirline, string icaoAirline, string flightNumber, string tailNumber, string expected)
    {
        Assert.Equal(expected, CallsignRules.ResolveUser(atcAirline, icaoAirline, flightNumber, tailNumber, Dir));
    }

    [Fact]
    public void Zero_width_spaces_are_no_tail_number()
    {
        // as logged: ATC ID='â€‹â€‹ ' (two zero-width spaces read in the wrong code page)
        Assert.Equal("", CallsignRules.CleanSimText("â€‹â€‹ "));
        Assert.Equal("", CallsignRules.CleanSimText("​​"));
        Assert.Equal("D-A388", CallsignRules.CleanSimText(" D-A388\t"));
        Assert.Equal("", CallsignRules.CleanSimText(null));
    }
}
