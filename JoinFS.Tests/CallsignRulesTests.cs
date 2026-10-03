namespace JoinFS.Tests;

/// <summary>
/// The one shared rule set for callsign / airline designator resolution (CallsignRules), tested
/// against the real bundled ICAO_Airlines.dat so the designator data itself is part of the contract.
/// </summary>
public class CallsignRulesTests
{
    static AirlineDirectory Dir => AirlineDirectory.Bundled;

    [Theory]
    [InlineData("DLH1234", CallsignKind.Airline)]
    [InlineData("BAW456A", CallsignKind.Airline)]
    [InlineData("AMB42", CallsignKind.Airline)]
    [InlineData("BHL123", CallsignKind.Airline)]
    [InlineData("AIO123", CallsignKind.Airline)]
    [InlineData("DLH1234A", CallsignKind.Unknown)]
    [InlineData("DLH12345", CallsignKind.Unknown)]
    [InlineData("ABC123", CallsignKind.Unknown)]
    [InlineData("N12345", CallsignKind.Registration)]
    [InlineData("N1234AB", CallsignKind.Registration)]
    [InlineData("D-EJOE", CallsignKind.Registration)]
    [InlineData("G-ABCD", CallsignKind.Registration)]
    [InlineData("OE-XYZ", CallsignKind.Registration)]
    [InlineData("RESCUE 12", CallsignKind.Named)]
    [InlineData("Christoph 42", CallsignKind.Named)]
    [InlineData("", CallsignKind.Unknown)]
    public void Classify_SeparatesAirlineRegistrationNamedAndUnknown(string callsign, CallsignKind expected)
    {
        Assert.Equal(expected, CallsignRules.Classify(callsign, Dir));
    }

    [Theory]
    [InlineData("DLH1234", "DLH")]
    [InlineData("BAW456A", "BAW")]
    [InlineData("AMB42", "AMB")]
    [InlineData("N12345", "")]
    [InlineData("D-EJOE", "")]
    [InlineData("RESCUE 12", "")]
    [InlineData("ABC123", "")]
    public void DeriveIcaoAirline_OnlyForAirlineShapedAndListedDesignators(string callsign, string expected)
    {
        Assert.Equal(expected, CallsignRules.DeriveIcaoAirline(callsign, Dir));
    }

    [Theory]
    [InlineData("DLH", "1234", "D-ALEX", "DLH1234")]
    [InlineData("EWG", "34U", "D-ALEX", "EWG34U")]
    [InlineData("DLH", "DLH1234", "D-ALEX", "DLH1234")]
    [InlineData("DLH", "dlh1234", "D-ALEX", "dlh1234")]
    [InlineData("DLH", "FSC739", "D-ALEX", "FSC739")]
    [InlineData("", "1234", "D-ALEX", "D-ALEX")]
    [InlineData("DLH", "", "D-ALEX", "D-ALEX")]
    public void Resolve_BuildsOrKeepsTheCallsign(string icaoAirline, string flightNumber, string tail, string expected)
    {
        Assert.Equal(expected, CallsignRules.Resolve(icaoAirline, flightNumber, tail));
    }

    [Fact]
    public void Iata_ConfiguredAirlineDecidesAndNormalizesTheMistake()
    {
        var result = CallsignRules.NormalizeIataPrefix("LH235", "DLH", airliner: true, liveryName: "", Dir);

        Assert.Equal("DLH235", result.Callsign);
        Assert.Equal("configured airline", result.Reason);
    }

    [Fact]
    public void Iata_UnambiguousCodeNeedsTheConfiguredAirline()
    {
        var withoutEvidence = CallsignRules.NormalizeIataPrefix("BA18B", "", airliner: true, liveryName: "", Dir);
        Assert.Equal("BA18B", withoutEvidence.Callsign);

        var withEvidence = CallsignRules.NormalizeIataPrefix("BA18B", "BAW", airliner: true, liveryName: "", Dir);
        Assert.Equal("BAW18B", withEvidence.Callsign);
    }

    [Fact]
    public void Iata_OnlyForAirliners()
    {
        var result = CallsignRules.NormalizeIataPrefix("LH235", "DLH", airliner: false, liveryName: "", Dir);

        Assert.Equal("LH235", result.Callsign);
    }

    [Theory]
    [InlineData("D-EJOE")]
    [InlineData("N12345")]
    [InlineData("RESCUE 12")]
    public void Iata_NeverTouchesNonIataShapes(string callsign)
    {
        var result = CallsignRules.NormalizeIataPrefix(callsign, "DLH", airliner: true, liveryName: "", Dir);

        Assert.Equal(callsign, result.Callsign);
    }

    [Fact]
    public void Iata_AmbiguousCodeTakesTheLiveryWordThatOnlyOneCandidateHas()
    {
        // EZ: EIA (Evergreen International) and SUS (Sun Air of Scandinavia) share no distinctive word
        var result = CallsignRules.NormalizeIataPrefix("EZ123", "", airliner: true, liveryName: "Sun Air Scandinavia", Dir);

        Assert.Equal("SUS123", result.Callsign);
        Assert.Equal("livery", result.Reason);
    }

    [Fact]
    public void Iata_SharedParentNameInTheLiveryDoesNotDecide()
    {
        // LH: "Lufthansa" is shared by DLH and GEC, so the livery cannot decide between them.
        // Falls through to the non-cargo rule, which picks DLH.
        var result = CallsignRules.NormalizeIataPrefix("LH100", "", airliner: true, liveryName: "Lufthansa 100 years", Dir);

        Assert.Equal("DLH100", result.Callsign);
        Assert.Equal("non-cargo", result.Reason);
    }

    [Fact]
    public void Iata_CargoRuleDropsTheCargoCandidate()
    {
        // SQ: SIA (Singapore Airlines) and SQC (Singapore Airlines Cargo) share "singapore".
        // Neither livery nor configuration decides, so the non-cargo SIA wins.
        var result = CallsignRules.NormalizeIataPrefix("SQ100", "", airliner: true, liveryName: "", Dir);

        Assert.Equal("SIA100", result.Callsign);
        Assert.Equal("non-cargo", result.Reason);
    }

    [Fact]
    public void Iata_AllNonCargoCandidatesTakeTheFirstInListOrder()
    {
        // M3: NFA, SPJ, TUS are all non-cargo, and no livery is given, so the first in list order wins.
        string first = Dir.IcaoForIata("M3")[0];

        var result = CallsignRules.NormalizeIataPrefix("M3123", "", airliner: true, liveryName: "", Dir);

        Assert.Equal(first + "123", result.Callsign);
        Assert.Equal("first in list", result.Reason);
    }

    [Fact]
    public void Iata_CanBeLoggedWithItsEvidence()
    {
        var result = CallsignRules.NormalizeIataPrefix("SQ100", "", airliner: true, liveryName: "", Dir);

        Assert.False(string.IsNullOrEmpty(result.Reason));
    }
}
