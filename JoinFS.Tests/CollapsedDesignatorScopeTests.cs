using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// The title-driven matching (same title hint, classes, last-resort generic model) is only for the designators ICAO collapsed (GLID, GLIM, UHEL ...),
    /// not for the dedicated type designators of real aircraft types.
    /// </summary>
    public class CollapsedDesignatorScopeTests
    {
        [Theory]
        [InlineData("GLID", true)]
        [InlineData("GLIM", true)]
        [InlineData("UHEL", true)]
        [InlineData("GYRO", true)]
        [InlineData("BALL", true)]
        [InlineData("ULAC", true)]
        [InlineData("C172", false)]
        [InlineData("B738", false)]
        [InlineData("LS8", false)]
        [InlineData("NOPE", false)]
        public void Only_designators_ICAO_collapsed_are_collapsed(string icao, bool expected)
        {
            Assert.Equal(expected, MatchingData.Doc8643.IsCollapsed(icao));
        }

        static string Row(string icao, string name, string hints, double span, double mtow, string engineType, int engines) => FormattableString.Invariant(
            $"{{\"icao\": \"{icao}\", \"name\": \"{name}\", \"manufacturer\": \"Maker\", \"titleHints\": [{hints}], \"wing\": \"low\", \"spanM\": {span}, \"lengthM\": 8, \"mtowKg\": {mtow}, \"engines\": {engines}, \"engineType\": \"{engineType}\", \"cruiseKt\": 100, \"gear\": \"tricycle\"}}");

        static CombinedResult Match(string json, string icao, string title, params Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), ReferenceSpecs.FromJson(json), null, MatchingData.Airlines);
            return matcher.Match(new MatchRequest(title, "", icao, "", "", "", false, Sub.TypeRole_Airliner, ""));
        }

        static Sub.Model Model(string title, string tag) => new(title, "", "", "", 0, "Einmot Prop", "0", "", tag, "L", "", "", false);

        [Fact]
        public void A_real_designator_with_several_rows_is_matched_by_the_normal_rules()
        {
            // two C172 rows (a user file might have them): the type is real, so none of the collapsed-designator ladder applies
            string json = "{\"aircraft\": [" + Row("C172", "Cessna 172", "\"Skyhawk\"", 11, 1100, "piston", 1) + "," + Row("C172", "Cessna 172 Floats", "\"Floatplane\"", 11, 1200, "piston", 1) + "]}";

            var result = Match(json, "C172", "Floatplane Remote", Model("Plain 172", "C172"), Model("Other", "P28A"));

            Assert.Equal(200, result.Explanation.Ranking.First(r => r.Model.title == "Plain 172").AttributeScores[Sub.MatchAttribute.IcaoType]);
            Assert.DoesNotContain(result.Explanation.Ranking.SelectMany(r => r.BaselineContributions), c => c.Contains("covers several aircraft"));
            Assert.False(result.Explanation.Ranking.Any(r => r.GenericFallback));
        }

        [Fact]
        public void For_a_real_designator_a_model_with_a_tag_is_never_typed_from_its_title()
        {
            // only a model without any tag is typed from its title for a real type (as before); a tagged model is judged by its tag and its physical data
            string json = "{\"aircraft\": [" + Row("C172", "Cessna 172", "\"Skyhawk\"", 11, 1100, "piston", 1) + "]}";
            var untagged = Model("Cessna 172 Skyhawk", "");
            var tagged = Model("Cessna 172 Skyhawk", "C17X");

            var result = Match(json, "C172", "Cessna 172", untagged, tagged);

            Assert.Contains(result.Explanation.Ranking.First(r => r.Model == untagged).BaselineContributions, c => c.Contains("title names ICAO type"));
            Assert.DoesNotContain(result.Explanation.Ranking.First(r => r.Model == tagged).BaselineContributions, c => c.Contains("title names ICAO type"));
        }

        [Fact]
        public void A_collapsed_designator_with_several_rows_gets_the_title_driven_matching()
        {
            // UHEL (microlight helicopter) with two rows: the title names the row, the model naming the same thing wins over the generic one
            string json = "{\"aircraft\": [" + Row("UHEL", "Generic ultralight helicopter", "\"Ultralight\"", 7, 400, "piston", 1)
                + "," + Row("UHEL", "Rotorway Exec", "\"Exec 162\"", 7.6, 680, "piston", 1) + "]}";
            json = json.Replace("\"wing\": \"low\"", "\"rotor\": true, \"wing\": \"rotor\"");

            var result = Match(json, "UHEL", "Rotorway Exec 162", Model("Generic Heli", "UHEL"), Model("Exec 162 Blue", "UHEL"));

            Assert.Equal("Exec 162 Blue", result.Model!.title);
            Assert.Contains(result.Explanation.Ranking.SelectMany(r => r.BaselineContributions), c => c.Contains("covers several aircraft"));
        }
    }
}
