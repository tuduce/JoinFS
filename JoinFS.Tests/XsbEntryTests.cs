using JoinFS.Matching;

namespace JoinFS.Tests
{
    /// <summary>
    /// X-Plane CSL packages (xsb_aircraft.txt) already say which aircraft type and airline a model is - ICAO / AIRLINE / LIVERY lines.
    /// JoinFS used to throw that away; <see cref="XsbEntry"/> reads it so X-Plane models carry an ICAO type and airline like MSFS models do.
    /// </summary>
    public class XsbEntryTests
    {
        static (string icaoType, string airline) Parse(string line)
        {
            string[] words = line.TrimStart(' ').Split(' ');
            return XsbEntry.Identity(words[0].ToUpper(), words);
        }

        [Fact]
        public void An_ICAO_line_gives_the_type_and_no_airline()
        {
            Assert.Equal(("B738", ""), Parse("ICAO B738"));
        }

        [Fact]
        public void An_AIRLINE_line_gives_type_and_airline()
        {
            Assert.Equal(("B738", "DLH"), Parse("AIRLINE B738 DLH"));
        }

        [Fact]
        public void A_LIVERY_line_gives_type_and_airline_not_the_livery_name()
        {
            Assert.Equal(("A320", "AFR"), Parse("LIVERY A320 AFR Special_Livery"));
        }

        [Fact]
        public void The_JoinFS_generated_marker_is_not_an_airline()
        {
            Assert.Equal(("C172", ""), Parse("LIVERY C172 JFS Cessna_Red"));
        }

        [Fact]
        public void Types_and_airlines_are_upper_cased()
        {
            Assert.Equal(("B738", "DLH"), Parse("airline b738 dlh"));
        }

        [Theory]
        [InlineData("ICAO")]
        [InlineData("ICAO X")]
        [InlineData("ICAO TOOLONG")]
        [InlineData("ICAO B7-8")]
        public void Anything_that_is_not_a_designator_is_ignored(string line)
        {
            Assert.Equal("", Parse(line).icaoType);
        }

        [Theory]
        [InlineData("AIRLINE B738 DELTA")]
        [InlineData("AIRLINE B738 D1")]
        [InlineData("AIRLINE B738 12")]
        public void An_airline_must_be_three_letters(string line)
        {
            Assert.Equal("", Parse(line).airline);
        }

        [Fact]
        public void The_type_survives_a_bad_airline()
        {
            Assert.Equal("B738", Parse("AIRLINE B738 DELTA").icaoType);
        }

        [Fact]
        public void MATCHES_lines_are_read_like_ICAO_lines()
        {
            Assert.Equal(("B738", ""), Parse("MATCHES B738"));
        }
    }
}
