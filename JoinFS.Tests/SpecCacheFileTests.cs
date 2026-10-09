using System.Globalization;
using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// "specs - &lt;sim&gt;.txt": the measured data of the installed models, kept next to models - &lt;sim&gt;.txt so a restart without a rescan
    /// does not lose it. A separate file on purpose: the models file format stays exactly as it is.
    /// </summary>
    public class SpecCacheFileTests
    {
        static Sub.Model Model(string title, string variation, AircraftSpecs? specs)
        {
            var model = new Sub.Model(title, "Maker", title, variation, 0, "Airliner", "0", "", "A20N", "M", "", "L2J", true);
            model.specs = specs;
            return model;
        }

        static AircraftSpecs Measured() => new()
        {
            Rotor = false, Wing = WingConfig.Low, SpanM = 35.8, LengthM = 37.57, MtowKg = 79016.5, EngineCount = 2, Engine = EngineKind.Jet,
            CruiseKt = 455, Gear = GearKind.Tricycle, Manufacturer = "AIRBUS",
            Sources = new()
            {
                ["Rotor"] = SpecSource.Config, ["Span"] = SpecSource.Config, ["Mtow"] = SpecSource.Config, ["Engines"] = SpecSource.Config,
                ["EngineType"] = SpecSource.Config, ["Cruise"] = SpecSource.Config, ["Gear"] = SpecSource.Config
            }
        };

        static Dictionary<(string, string), AircraftSpecs> RoundTrip(params Sub.Model[] models)
            => SpecCacheFile.Read(SpecCacheFile.Write(models));

        [Fact]
        public void Measured_values_survive_a_round_trip()
        {
            var read = RoundTrip(Model("Airbus A320", "Default", Measured()))[("Airbus A320", "Default")];

            Assert.Equal(35.8, read.SpanM);
            Assert.Equal(37.57, read.LengthM);
            Assert.Equal(79016.5, read.MtowKg);
            Assert.Equal(2, read.EngineCount);
            Assert.Equal(EngineKind.Jet, read.Engine);
            Assert.Equal(455, read.CruiseKt);
            Assert.Equal(GearKind.Tricycle, read.Gear);
            Assert.Equal(WingConfig.Low, read.Wing);
            Assert.False(read.Rotor);
            Assert.Equal("AIRBUS", read.Manufacturer);
            Assert.Equal(SpecSource.Config, read.SourceOf("Span"));
        }

        [Fact]
        public void Unknown_values_stay_unknown()
        {
            var sparse = new AircraftSpecs { SpanM = 12 };
            sparse.Sources["Span"] = SpecSource.Config;
            var read = RoundTrip(Model("Sparse", "Default", sparse))[("Sparse", "Default")];

            Assert.Equal(12, read.SpanM);
            Assert.Null(read.MtowKg);
            Assert.Null(read.EngineCount);
            Assert.Equal(EngineKind.Unknown, read.Engine);
            Assert.Equal(GearKind.Unknown, read.Gear);
        }

        [Fact]
        public void Models_sharing_one_aircraft_share_one_specs_object_after_reading()
        {
            var shared = Measured();
            var read = RoundTrip(Model("A320", "Red", shared), Model("A320", "Blue", shared));

            Assert.Same(read[("A320", "Red")], read[("A320", "Blue")]);
        }

        [Fact]
        public void The_file_has_one_spec_block_per_aircraft_not_per_livery()
        {
            var shared = Measured();
            var lines = SpecCacheFile.Write([Model("A320", "Red", shared), Model("A320", "Blue", shared), Model("A320", "Green", shared)]).ToList();

            Assert.Single(lines, l => l.StartsWith("S[+]"));
            Assert.Equal(3, lines.Count(l => l.StartsWith("M[+]")));
        }

        [Fact]
        public void Only_measured_data_is_written_reference_and_inferred_values_are_not()
        {
            var reference = new AircraftSpecs { SpanM = 34 };
            reference.Sources["Span"] = SpecSource.Reference;

            Assert.Empty(RoundTrip(Model("Ref only", "Default", reference)));
            Assert.Empty(RoundTrip(Model("No specs", "Default", null)));
        }

        [Fact]
        public void Numbers_are_written_with_a_dot_whatever_the_machine_language_is()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var lines = SpecCacheFile.Write([Model("A320", "Default", Measured())]).ToList();
                Assert.Contains(lines, l => l.Contains("35.8"));
                Assert.Equal(35.8, SpecCacheFile.Read(lines)[("A320", "Default")].SpanM);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Titles_containing_odd_characters_survive()
        {
            var read = RoundTrip(Model("Cessna 172 | \"Skyhawk\" ä", "Livery = 1", Measured()));
            Assert.True(read.ContainsKey(("Cessna 172 | \"Skyhawk\" ä", "Livery = 1")));
        }

        [Fact]
        public void Damaged_lines_are_skipped_and_the_rest_is_kept()
        {
            var good = SpecCacheFile.Write([Model("Good", "Default", Measured())]).ToList();
            var lines = new List<string> { "garbage", "S[+]only[+]three", "M[+]9[+]Orphan[+]Default", "M[+]x" };
            lines.AddRange(good);

            var read = SpecCacheFile.Read(lines);

            Assert.Single(read);
            Assert.True(read.ContainsKey(("Good", "Default")));
        }

        [Fact]
        public void An_empty_or_comment_only_file_reads_as_nothing()
        {
            Assert.Empty(SpecCacheFile.Read([]));
            Assert.Empty(SpecCacheFile.Read(["# only a comment", ""]));
        }
    }
}
