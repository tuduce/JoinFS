using JoinFS.Matching;

namespace JoinFS.Tests;

public class AirlineResolverTests
{
    static readonly AirlineResolver Airlines = MatchingData.Airlines;

    [Fact]
    public void A_valid_ICAO_code_is_left_alone()
    {
        var r = Airlines.Resolve("DLH", "");
        Assert.Equal("DLH", r.Icao);
        Assert.False(r.Guessed);
    }

    [Fact]
    public void FedEx_name_in_the_ICAO_field_resolves_to_FDX()
    {
        var r = Airlines.Resolve("FEDEX", "");
        Assert.Equal("FDX", r.Icao);
        Assert.True(r.Changed);
        Assert.False(r.Guessed);
    }

    [Fact]
    public void ICAO_code_in_the_atc_airline_field_is_used_when_the_icao_field_is_a_name()
    {
        var r = Airlines.Resolve("FEDEX", "FDX");
        Assert.Equal("FDX", r.Icao);
    }

    [Fact]
    public void Operator_name_in_the_title_is_found()
    {
        var r = Airlines.Resolve("", "", "FedEx N234234", "");
        Assert.Equal("FDX", r.Icao);
        Assert.True(r.Guessed);
    }

    [Fact]
    public void A_word_found_in_exactly_one_airline_name_is_used()
    {
        var r = Airlines.Resolve("Cebu", "");
        Assert.True(r.Guessed);
        Assert.True(r.Changed);
        Assert.Equal("CEB", r.Icao);
    }

    [Fact]
    public void A_word_found_in_several_airline_names_is_not_guessed()
    {
        var r = Airlines.Resolve("Pacific", "");
        Assert.False(r.Guessed);
    }

    [Fact]
    public void A_unique_two_letter_IATA_code_resolves_to_the_ICAO_code()
    {
        var r = Airlines.Resolve("FX", "");
        Assert.Equal("FDX", r.Icao);
        Assert.True(r.Changed);
        Assert.False(r.Guessed);
    }

    [Fact]
    public void Unknown_text_resolves_to_nothing()
    {
        var r = Airlines.Resolve("", "", "Cessna 172 Skyhawk N12345", "");
        Assert.False(r.Guessed);
        Assert.Equal("", r.Icao);
    }
}
