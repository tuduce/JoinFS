using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// Types the MSFS 2024 and X-Plane model lists tag that have a row in aircraft-specs.json, with figures taken from the sources named in each row.
    /// </summary>
    public class ModelListTypesHaveRowsTests
    {
        [Theory]
        [InlineData("EVOT", 11.0, 2064)]
        [InlineData("RV10", 9.68, 1225)]
        [InlineData("A50", 50.6, 190000)]
        [InlineData("MI24", 17.2, 12000)]
        [InlineData("EC75", 14.8, 7800)]
        [InlineData("F22", 13.56, 38000)]
        [InlineData("MD81", 32.87, 63500)]
        [InlineData("MG29", 11.36, 18000)]
        [InlineData("MI26", 32.0, 56000)]
        [InlineData("SU27", 14.7, 30450)]
        [InlineData("AN26", 29.2, 24000)]
        [InlineData("TUCA", 11.14, 3175)]
        [InlineData("T134", 29.0, 47600)]
        [InlineData("X-CONC", 25.6, 185070)]
        [InlineData("X-ALIA", 15.24, 3175)]
        [InlineData("L5", 10.36, 929)]
        [InlineData("A4", 8.38, 11100)]
        [InlineData("AMX", 8.87, 13000)]
        [InlineData("B2", 52.4, 152600)]
        [InlineData("F117", 13.2, 23800)]
        [InlineData("F260", 8.35, 1100)]
        [InlineData("F5", 8.13, 11200)]
        [InlineData("T160", 55.7, 275000)]
        public void A_type_the_model_lists_use_has_a_row_with_its_size_and_weight(string tag, double spanM, double mtowKg)
        {
            var specs = MatchingData.Reference.Find(tag);

            Assert.NotNull(specs);
            Assert.Equal(spanM, specs!.SpanM);
            Assert.Equal(mtowKg, specs.MtowKg);
        }

        [Fact]
        public void The_file_has_no_row_without_an_ICAO_designator_for_the_gliders_of_the_model_lists()
        {
            // K 7 ("AS7"), LS4, SZD-30 and JS3 are not rows: the class lines list them as title hints and supply their parameters
            foreach (var tag in new[] { "AS7", "LS4", "SZD30", "JS3-15", "JS3-18" }) Assert.Null(MatchingData.Reference.Find(tag));
        }

        [Theory]
        [InlineData("K7 Cream/Red (imperial)", "Glider, classic wooden")]
        [InlineData("SZD-30 Pirat", "Glider, classic wooden")]
        [InlineData("LS4 Swiss", "Glider, standard class 15 m")]
        public void A_glider_of_the_model_lists_is_described_by_the_class_line_that_lists_it_as_a_hint(string title, string classLine)
        {
            Assert.Equal(classLine, MatchingData.Reference.FindForText("GLID", title)!.Name);
        }

        [Theory]
        [InlineData("JS3-15 D-KSBK (TN) Katrin Senna", "Glider, 15 m class", 15.0)]
        [InlineData("JS3-18 Rapture D-KSBK Katrin Senna", "Glider, 18 m class", 18.0)]
        public void A_JS3_has_no_row_of_its_own_its_parameters_come_from_the_class_line_that_lists_it_as_a_hint(string title, string classLine, double spanM)
        {
            // "JS3-15 D-KSBK (TN)[+]Jonker[+]JS3-15[+]Katrin Senna": no JS3 row, the 15 m / 18 m class line lists "JS3-15" / "JS3-18" and supplies the parameters
            Assert.DoesNotContain(MatchingData.Reference.Entries, e => e.Icao.StartsWith("JS3-"));
            var specs = MatchingData.Reference.FindForText("GLID", title)!;

            Assert.Equal(classLine, specs.Name);
            Assert.Equal(spanM, specs.SpanM);
        }

        [Fact]
        public void A_model_tagged_AS7_is_matched_for_a_remote_glider_titled_K7_and_named_by_the_same_hint()
        {
            var k7 = new Sub.Model("K7 Cream/Red (imperial)", "Schleicher", "K7", "Cream/Red (knots)", 0, "Segler", "0", "", "AS7", "L", "", "", true);
            var generic = new Sub.Model("GLID blue", "", "", "blue", 0, "Segler", "0", "", "GLID", "L", "", "", true);
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [generic, k7];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), MatchingData.Reference, null, MatchingData.Airlines);

            var result = matcher.Match(new MatchRequest("K7 Cream/Green (imperial)", "", "GLID", "", "", "", false, Sub.TypeRole_Glider, ""));

            Assert.Equal("K7 Cream/Red (imperial)", result.Model!.title);
            Assert.Contains(result.Explanation.Ranking.First(r => r.Model == k7).BaselineContributions, c => c.Contains("same title hint"));
        }
    }
}
