using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// Pins what Substitution's airline helpers do today (ICAO validity, airline guessed from livery/title text), so that the airline lists
    /// of Substitution, AirlineDirectory and the new matcher can share one parser without changing behaviour.
    /// </summary>
    public class SubstitutionAirlineCharacterizationTests
    {
        [Theory]
        [InlineData("DLH", true)]
        [InlineData("dlh", true)]
        [InlineData("FDX", true)]
        [InlineData("1AB", false)]
        [InlineData("FSC739", false)]
        [InlineData("", false)]
        [InlineData("DL", false)]
        public void A_code_is_a_known_ICAO_airline_only_when_it_is_three_characters_in_the_list(string code, bool expected)
        {
            Assert.Equal(expected, Sub.IsKnownIcaoAirline(code));
        }

        [Theory]
        [InlineData("Condor 757", "CFG")]
        [InlineData("Lufthansa Airbus A320", "DLH")]
        [InlineData("Some Private Aircraft", "")]
        [InlineData("", "")]
        public void The_airline_is_guessed_from_the_longest_name_found_in_the_text(string text, string expected)
        {
            Assert.Equal(expected, Sub.GuessIcaoAirlineFromText(text));
        }

        [Fact]
        public void A_name_has_to_be_a_whole_word_to_count()
        {
            // "Lufthansas" must not be read as the airline "Lufthansa"
            Assert.Equal("", Sub.GuessIcaoAirlineFromText("Lufthansas"));
        }
    }
}
