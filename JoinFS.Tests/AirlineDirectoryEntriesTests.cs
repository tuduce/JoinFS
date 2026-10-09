using JoinFS.Matching;

namespace JoinFS.Tests
{
    /// <summary>
    /// ICAO_Airlines.dat is parsed once, by <see cref="AirlineDirectory"/>. The airline resolver of the matcher and Substitution's
    /// validity / guess helpers are built on that parse - these tests pin what they get from it.
    /// </summary>
    public class AirlineDirectoryEntriesTests
    {
        static AirlineDirectory Sample() => AirlineDirectory.FromLines(
        [
            "DLH\tLH\tLufthansa",
            "CFG\tDE\tcondor",
            "ABC\t\tAlpha Airways",
            "ABC\t\tAlpha Airways Cargo",      // a second row for the same designator
            "bad line",
            "TOOLONG\tXX\tIgnored",
        ]);

        [Fact]
        public void Entries_keep_every_valid_row_in_file_order()
        {
            var entries = Sample().Entries;
            Assert.Equal(["DLH", "CFG", "ABC", "ABC"], entries.Select(e => e.Icao));
            Assert.Equal("LH", entries[0].Iata);
            Assert.Equal("", entries[2].Iata);
        }

        [Fact]
        public void Names_hold_the_first_name_of_each_designator_in_file_order()
        {
            var names = Sample().Names;
            Assert.Equal(["DLH", "CFG", "ABC"], names.Keys);
            Assert.Equal("Alpha Airways", names["ABC"]);
        }

        [Fact]
        public void The_bundled_list_is_what_the_matcher_resolves_against()
        {
            Assert.True(AirlineDirectory.Bundled.Entries.Count > 5000);
            var resolver = AirlineResolver.From(AirlineDirectory.Bundled);
            Assert.True(resolver.IsKnownCode("DLH"));
            Assert.True(resolver.IsKnownCode("dlh"));
            Assert.Equal("Lufthansa", resolver.NameOf("DLH"));
            Assert.Equal("Lufthansa", resolver.NameOf(" dlh "));
            Assert.False(resolver.IsKnownCode("1AB"));
        }

        [Fact]
        public void A_resolver_built_from_lines_agrees_with_one_built_from_the_directory()
        {
            var lines = new[] { "DLH\tLH\tLufthansa", "FDX\tFX\tFedEx Express" };
            var fromLines = AirlineResolver.FromLines(lines);
            var fromDirectory = AirlineResolver.From(AirlineDirectory.FromLines(lines));

            Assert.Equal(fromLines.Resolve("FEDEX", "").Icao, fromDirectory.Resolve("FEDEX", "").Icao);
            Assert.Equal("FDX", fromDirectory.Resolve("FX", "").Icao);
        }
    }
}
