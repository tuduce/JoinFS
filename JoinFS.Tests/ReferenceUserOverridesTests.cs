using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// aircraft-specs.user.json (same format as the built-in aircraft-specs.json) is merged over the built-in reference data:
    /// a type designator found in both takes the user row only; invalid parts are skipped and reported, never fatal.
    /// </summary>
    public class ReferenceUserOverridesTests
    {
        static readonly HashSet<string> Official = new(StringComparer.OrdinalIgnoreCase) { "B744", "B738", "A320", "C172", "B737" };

        static string Row(string icao, string name, double mtow, string aliases = "", string hints = "", string manufacturer = "Maker") =>
            $"{{\"icao\": \"{icao}\", \"name\": \"{name}\", \"manufacturer\": \"{manufacturer}\", \"aliases\": [{aliases}], \"titleHints\": [{hints}], \"wing\": \"low\", \"spanM\": 30, \"lengthM\": 30, \"mtowKg\": {mtow}, \"engines\": 2, \"engineType\": \"jet\", \"cruiseKt\": 450, \"gear\": \"tricycle\"}}";

        static string File(params string[] rows) => "{\"aircraft\": [" + string.Join(",", rows) + "]}";

        static ReferenceSpecs BuiltIn() => ReferenceSpecs.FromJson(File(
            Row("B744", "Boeing 747-400", 396890, "\"B747\"", "\"747-400\"", "Boeing"),
            Row("B738", "Boeing 737-800", 79016, "\"B73X\"", "\"737-800\"", "Boeing"),
            Row("A320", "Airbus A320", 78000, "", "\"A320ceo\"", "Airbus")));

        static ReferenceSpecs Merge(ReferenceSpecs builtIn, string userJson, List<string>? problems = null)
            => ReferenceSpecs.WithUserOverrides(builtIn, userJson, Official.Contains, problems ?? []);

        // ---- the same designator: the user row only -------------------------------------------------------------------

        [Fact]
        public void A_designator_in_both_takes_the_user_row_only()
        {
            var merged = Merge(BuiltIn(), File(Row("B744", "My 747", 123456, "", "\"my jumbo\"", "Custom")));

            Assert.Equal(1, merged.Entries.Count(e => e.Icao == "B744"));
            var entry = merged.Entries.Single(e => e.Icao == "B744");
            Assert.Equal("My 747", entry.Name);
            Assert.Equal("Custom", entry.Manufacturer);
            Assert.Equal(123456, merged.Find("B744")!.MtowKg);
            Assert.Equal(["my jumbo"], entry.TitleHints);
            Assert.Empty(entry.Aliases);     // the built-in alias "B747" is gone with the built-in row
        }

        [Fact]
        public void A_new_designator_is_added_and_the_built_in_rows_stay()
        {
            var builtIn = BuiltIn();
            var merged = Merge(builtIn, File(Row("C172", "Skyhawk", 1157)));

            Assert.Equal(builtIn.Entries.Count + 1, merged.Entries.Count);
            Assert.NotNull(merged.Find("C172"));
            Assert.Equal(396890, merged.Find("B744")!.MtowKg);
        }

        [Fact]
        public void The_built_in_data_itself_is_not_changed()
        {
            var builtIn = BuiltIn();
            Merge(builtIn, File(Row("B744", "My 747", 1)));
            Assert.Equal("Boeing 747-400", builtIn.Entries.Single(e => e.Icao == "B744").Name);
        }

        // ---- aliases stay unique and official designators still rule --------------------------------------------------

        [Fact]
        public void A_user_alias_beats_the_same_built_in_alias_on_another_row()
        {
            var merged = Merge(BuiltIn(), File(Row("A320", "Airbus A320", 78000, "\"B73X\"")));

            Assert.Equal("A320", merged.ResolveAlias("B73X"));
            Assert.DoesNotContain("B73X", merged.Entries.Single(e => e.Icao == "B738").Aliases);
        }

        [Fact]
        public void An_alias_that_is_an_official_designator_is_rejected_and_reported()
        {
            var problems = new List<string>();
            var merged = Merge(BuiltIn(), File(Row("A320", "Airbus A320", 78000, "\"B737\"")), problems);

            Assert.DoesNotContain("B737", merged.Entries.Single(e => e.Icao == "A320").Aliases);
            Assert.Contains(problems, p => p.Contains("B737"));
        }

        [Fact]
        public void An_alias_that_is_another_rows_designator_is_rejected_and_reported()
        {
            var problems = new List<string>();
            var merged = Merge(BuiltIn(), File(Row("A320", "Airbus A320", 78000, "\"B744\"")), problems);

            Assert.Equal("B744", merged.ResolveAlias("B744"));
            Assert.DoesNotContain("B744", merged.Entries.Single(e => e.Icao == "A320").Aliases);
            Assert.Contains(problems, p => p.Contains("B744"));
        }

        [Fact]
        public void A_user_row_may_add_a_family_tag_alias_for_its_variant()
        {
            var merged = Merge(BuiltIn(), File(Row("B744", "Boeing 747-400", 396890, "\"B74X\"")));
            Assert.Equal("B744", merged.ResolveAlias("B74X"));
        }

        // ---- problems are reported, never fatal ----------------------------------------------------------------------

        [Fact]
        public void A_duplicate_designator_in_the_user_file_keeps_the_first_row()
        {
            var problems = new List<string>();
            var merged = Merge(BuiltIn(), File(Row("C172", "First", 1), Row("C172", "Second", 2)), problems);

            Assert.Equal("First", merged.Entries.Single(e => e.Icao == "C172").Name);
            Assert.Contains(problems, p => p.Contains("C172"));
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("{\"aircraft\": 5}")]
        [InlineData("{}")]
        public void An_unreadable_file_leaves_the_built_in_data_and_says_why(string userJson)
        {
            var builtIn = BuiltIn();
            var problems = new List<string>();
            var merged = Merge(builtIn, userJson, problems);

            Assert.Equal(builtIn.Entries.Count, merged.Entries.Count);
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void A_row_without_a_designator_is_skipped_and_the_other_rows_still_apply()
        {
            var problems = new List<string>();
            var merged = Merge(BuiltIn(), "{\"aircraft\": [{\"name\": \"no icao\"}, " + Row("C172", "Skyhawk", 1157) + "]}", problems);

            Assert.NotNull(merged.Find("C172"));
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void A_missing_file_changes_nothing()
        {
            var builtIn = BuiltIn();
            var problems = new List<string>();
            var merged = ReferenceSpecs.WithUserOverridesFromFile(builtIn, Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json"), Official.Contains, problems);

            Assert.Same(builtIn, merged);
            Assert.Empty(problems);
        }

        // ---- on the real data -----------------------------------------------------------------------------------------

        [Fact]
        public void Merged_real_data_keeps_designators_and_aliases_unique()
        {
            var merged = ReferenceSpecs.WithUserOverrides(MatchingData.Reference,
                File(Row("B744", "My 747", 1, "\"B747\", \"B74X\""), Row("B738", "My 737", 2, "\"B73X\"")),
                MatchingData.Doc8643.IsRecognized, []);

            Assert.Empty(merged.Entries.GroupBy(e => e.Icao, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key));
            var aliases = merged.Entries.SelectMany(e => e.Aliases).ToList();
            Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(aliases, alias => Assert.False(MatchingData.Doc8643.IsRecognized(alias), alias + " is an official designator"));
            Assert.Equal("My 747", merged.Entries.Single(e => e.Icao == "B744").Name);
        }

        // ---- end to end: no rebuild needed ----------------------------------------------------------------------------

        [Fact]
        public void A_user_file_changes_a_match_without_rebuilding()
        {
            string folder = Path.Combine(Path.GetTempPath(), "joinfs-user-specs-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "aircraft-specs.user.json");
            try
            {
                var boeing = new Sub.Model("Boeing 747-400", "Boeing", "747", "Default", 0, "Airliner", "0", "", "B744", "H", "", "L4J", true);
                MatchRequest request = new("Remote", "", "B74Y", "", "", "", false, Sub.TypeRole_Airliner, "") { LiveryAware = true };

                Sub Create()
                {
                    var substitution = new Sub(null!) { engine = MatchingEngine.New, userReferencePath = file };
                    substitution.LoadDoc8643Index();
                    substitution.models = [boeing];
                    substitution.RebuildTitleIndex();
                    substitution.MakeIcaoIndex();
                    return substitution;
                }

                var (_, _, without) = Create().Resolve(request);
                Assert.DoesNotContain(without.steps, step => step.Contains("B74Y") && step.Contains("B744"));

                System.IO.File.WriteAllText(file, ReferenceUserOverridesTests.File(Row("B744", "Boeing 747-400", 396890, "\"B74Y\"", "", "Boeing")));
                var (model, type, with) = Create().Resolve(request);

                Assert.Equal("Boeing 747-400", model!.title);
                Assert.Equal(Sub.Type.Icao, type);
                Assert.Contains(with.steps, step => step.Contains("B74Y") && step.Contains("B744"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
