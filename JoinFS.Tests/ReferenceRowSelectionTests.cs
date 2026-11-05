using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// A designator that ICAO folded together (GLID, GLIM ...) may have several rows in aircraft-specs.json; the title picks the row.
    /// The first row of a designator is its default.
    /// </summary>
    public class ReferenceRowSelectionTests
    {
        static string Row(string icao, string name, double span, double mtow, string hints = "", string engineType = "none", int engines = 0) =>
            FormattableString.Invariant($"{{\"icao\": \"{icao}\", \"name\": \"{name}\", \"manufacturer\": \"-\", \"titleHints\": [{hints}], \"wing\": \"mid\", \"spanM\": {span}, \"lengthM\": 7, \"mtowKg\": {mtow}, \"engines\": {engines}, \"engineType\": \"{engineType}\", \"cruiseKt\": 70, \"gear\": \"taildragger\"}}");

        static ReferenceSpecs Gliders() => ReferenceSpecs.FromJson("{\"aircraft\": [" + string.Join(",",
            Row("GLID", "Generic glider", 15, 500, "\"Glider\", \"Sailplane\""),
            Row("GLID", "Glider, two-seat", 17.5, 620, "\"Twin Trainer\", \"DG-505\""),
            Row("GLID", "Glider, classic wooden", 13, 260, "\"Grunau Baby\", \"Ka 6\""),
            Row("AS21", "Schleicher ASK 21", 17, 600, "\"ASK 21\", \"ASK21\""),
            Row("C172", "Cessna 172", 11, 1100, "\"Skyhawk\"", "piston", 1)) + "]}");

        // ---- loading ------------------------------------------------------------------------------------------------------

        [Fact]
        public void Several_rows_of_one_designator_are_all_kept_and_the_first_is_the_default()
        {
            var reference = Gliders();

            Assert.Equal(3, reference.Entries.Count(e => e.Icao == "GLID"));
            Assert.Equal(3, reference.RowCount("GLID"));
            Assert.Equal(15, reference.Find("GLID")!.SpanM);
        }

        [Fact]
        public void A_designator_with_one_row_has_a_row_count_of_one_and_an_unknown_one_of_zero()
        {
            var reference = Gliders();

            Assert.Equal(1, reference.RowCount("C172"));
            Assert.Equal(0, reference.RowCount("ZZZZ"));
            Assert.Equal(0, reference.RowCount(""));
        }

        // ---- picking a row from the text ----------------------------------------------------------------------------------

        [Fact]
        public void The_row_whose_title_hint_appears_in_the_text_is_chosen()
        {
            var specs = Gliders().FindForText("GLID", "Grunau Baby IIb")!;

            Assert.Equal("Glider, classic wooden", specs.Name);
            Assert.Equal(13, specs.SpanM);
        }

        [Fact]
        public void The_longest_matching_hint_wins()
        {
            // "Glider" (generic row) and "Twin Trainer" both occur: the longer, more specific hint decides
            var specs = Gliders().FindForText("GLID", "Glider Twin Trainer")!;

            Assert.Equal("Glider, two-seat", specs.Name);
        }

        [Fact]
        public void A_text_without_a_hint_gives_the_first_row()
        {
            Assert.Equal("Generic glider", Gliders().FindForText("GLID", "Mystery 77")!.Name);
            Assert.Equal("Generic glider", Gliders().FindForText("GLID", "")!.Name);
        }

        [Fact]
        public void A_hint_must_be_a_standalone_token()
        {
            Assert.Equal("Generic glider", Gliders().FindForText("GLID", "Ka 66 Special")!.Name);
        }

        [Fact]
        public void Removing_the_generic_row_makes_the_next_row_the_default()
        {
            var reference = ReferenceSpecs.FromJson("{\"aircraft\": [" + string.Join(",",
                Row("GLID", "Glider, two-seat", 17.5, 620, "\"DG-505\""),
                Row("GLID", "Glider, classic wooden", 13, 260, "\"Grunau Baby\"")) + "]}");

            Assert.Equal("Glider, two-seat", reference.FindForText("GLID", "no hint")!.Name);
        }

        [Fact]
        public void A_specific_type_named_in_the_text_beats_the_class_rows_when_it_is_the_longer_match()
        {
            // "ASK 21" has a row of its own under another designator: for an ambiguous GLID it is the better description
            var specs = Gliders().FindForText("GLID", "Schleicher ASK 21 Club")!;

            Assert.Equal("Schleicher ASK 21", specs.Name);
            Assert.Equal(600, specs.MtowKg);
        }

        [Fact]
        public void A_specific_type_of_another_kind_is_not_taken_for_the_ambiguous_designator()
        {
            // a Cessna 172 in the title of a glider (a tow plane livery, say) must not turn the glider into a 172
            var specs = Gliders().FindForText("GLID", "Glider towed by Skyhawk")!;

            Assert.Equal("Generic glider", specs.Name);
        }

        [Fact]
        public void A_designator_with_a_single_row_ignores_the_text()
        {
            Assert.Equal("Cessna 172", Gliders().FindForText("C172", "Grunau Baby")!.Name);
        }

        [Fact]
        public void An_alias_resolves_to_its_designator_rows()
        {
            var reference = ReferenceSpecs.FromJson("{\"aircraft\": [" + string.Join(",",
                Row("GLID", "Generic glider", 15, 500, "\"Glider\"").Replace("\"titleHints\"", "\"aliases\": [\"GLDR\"], \"titleHints\""),
                Row("GLID", "Glider, classic wooden", 13, 260, "\"Grunau Baby\"")) + "]}");

            Assert.Equal("Glider, classic wooden", reference.FindForText("GLDR", "Grunau Baby")!.Name);
        }

        [Fact]
        public void Find_for_an_unknown_designator_is_null()
        {
            Assert.Null(Gliders().FindForText("ZZZZ", "Grunau Baby"));
        }

        // ---- the resolver uses it on both sides ---------------------------------------------------------------------------

        [Fact]
        public void The_remote_aircraft_is_described_by_the_row_its_title_names()
        {
            var resolver = new SpecResolver(Gliders(), MatchingData.Doc8643);

            var resolved = resolver.ForRemote(new MatchRequest("Grunau Baby", "", "GLID", "", "", "", false, 5, ""));

            Assert.Equal(13, resolved.Specs.SpanM);
            Assert.Contains(resolved.Notes, note => note.Contains("Glider, classic wooden"));
        }

        [Fact]
        public void An_installed_model_is_described_by_the_row_its_title_and_livery_name()
        {
            var resolver = new SpecResolver(Gliders(), MatchingData.Doc8643);
            var model = new Sub.Model("Wooden", "Maker", "Wooden", "Ka 6 red", 0, "Glider", "0", "", "GLID", "L", "", "", true);

            Assert.Equal(13, resolver.ForModel(model).Specs.SpanM);
        }

        [Fact]
        public void A_single_row_designator_keeps_its_notes_unchanged()
        {
            var resolver = new SpecResolver(Gliders(), MatchingData.Doc8643);

            var resolved = resolver.ForRemote(new MatchRequest("Skyhawk", "", "C172", "", "", "", false, 1, ""));

            Assert.DoesNotContain(resolved.Notes, note => note.Contains("chosen from the title"));
        }
    }
}
