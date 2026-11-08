using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// A remote aircraft that reports a designator ICAO folded several aircraft into (GLID) is matched by what its title names: an installed
    /// model whose title names the same aircraft wins whatever its tag; the generic glider is a last resort.
    /// </summary>
    public class HintIdentityMatchingTests
    {
        static Sub.Model Model(string title, string tag, string variation = "")
            => new(title, "", "", variation, 0, "Segler", "0", "", tag, "L", "", "", false);

        static CombinedResult Match(string title, params Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), MatchingData.Reference, null, MatchingData.Airlines, MatchingData.Related);
            return matcher.Match(new MatchRequest(title, "", "GLID", "", "", "", false, Sub.TypeRole_Glider, ""));
        }

        static readonly Sub.Model Generic = Model("GLID blue", "GLID", "blue");
        static readonly Sub.Model Js315 = Model("JS3-15 white", "JS3-15", "white");
        static readonly Sub.Model Js318 = Model("JS3-18 Waldo", "JS3-18", "Waldo");
        static readonly Sub.Model Ls8 = Model("DG LS8", "LS8");
        static readonly Sub.Model Ls4Blue = Model("LS4 Blue", "LS4", "Blue");
        static readonly Sub.Model Ls4Green = Model("LS4 Green", "LS4", "Green");

        // ---- the same title hint is the same aircraft ------------------------------------------------------------------------

        [Fact]
        public void A_model_naming_the_same_aircraft_wins_whatever_its_tag()
        {
            Assert.Equal("LS4 Blue", Match("LS4 Red", Generic, Js315, Ls8, Ls4Blue).Model!.title);
        }

        [Fact]
        public void The_same_hint_beats_a_model_that_merely_carries_the_tag_GLID()
        {
            Assert.Equal("LS4 Blue", Match("LS4", Generic, Ls4Blue).Model!.title);
        }

        [Fact]
        public void Among_models_naming_the_same_aircraft_the_closest_title_wins()
        {
            Assert.Equal("LS4 Green", Match("LS4 Green Special", Ls4Blue, Ls4Green).Model!.title);
        }

        [Fact]
        public void A_JS3_18_remote_gets_the_18_m_model_not_the_15_m_one()
        {
            Assert.Equal("JS3-18 Waldo", Match("JS3-18 Rapture", Js315, Js318).Model!.title);
            Assert.Equal("JS3-15 white", Match("JS3-15 Rapture", Js315, Js318).Model!.title);
        }

        // ---- the generic glider is a last resort -------------------------------------------------------------------------------

        [Fact]
        public void Without_an_installed_match_a_close_identified_model_beats_the_generic_glider()
        {
            var result = Match("LS4 Red", Generic, Js315, Ls8);

            Assert.NotEqual("GLID blue", result.Model!.title);
        }

        [Fact]
        public void The_generic_glider_is_used_when_nothing_else_is_installed()
        {
            Assert.Equal("GLID blue", Match("LS4 Red", Generic).Model!.title);
        }

        [Fact]
        public void A_remote_that_names_nothing_still_gets_the_generic_glider()
        {
            Assert.Equal("GLID blue", Match("Mystery 77", Generic, Js315).Model!.title);
        }

        [Fact]
        public void The_report_says_why_a_model_was_ranked_that_way()
        {
            var result = Match("LS4 Red", Generic, Ls4Blue);

            Assert.Contains(result.Explanation.Ranking.First(r => r.Model == Ls4Blue).BaselineContributions, c => c.Contains("LS4") && c.Contains("same"));
            Assert.True(result.Explanation.Ranking.First(r => r.Model == Generic).GenericFallback);
        }

        // ---- the intended order, shown on the club class row ("Standard Cirrus", "Libelle", "Std Cirrus" ... are its members) -------------------

        static readonly Sub.Model Libelle = Model("Schempp-Hirth Libelle H301", "GLID", "Yellow");
        static readonly Sub.Model CirrusRed = Model("Standard Cirrus", "GLID", "Red");
        static readonly Sub.Model CirrusBlue = Model("Standard Cirrus", "GLID", "Blue");
        static readonly Sub.Model StdCirrusGreen = Model("Std Cirrus Green", "GLID", "Green");

        [Fact]
        public void A_title_word_found_in_an_installed_title_wins_over_other_members_of_the_class()
        {
            Assert.Equal("Standard Cirrus", Match("Schempp-Hirth Standard Cirrus", Libelle, CirrusRed, Generic, Ls8).Model!.title);
        }

        [Fact]
        public void Another_spelling_with_the_same_word_still_beats_a_class_member_without_it()
        {
            Assert.Equal("Std Cirrus Green", Match("Standard Cirrus", Libelle, StdCirrusGreen, Generic, Ls8).Model!.title);
        }

        [Fact]
        public void Among_several_models_with_the_word_the_best_fitting_livery_wins()
        {
            var result = Match("Standard Cirrus Blue", Libelle, CirrusRed, CirrusBlue, Generic);

            Assert.Equal("Blue", result.Model!.variation);
        }

        [Fact]
        public void Without_the_word_the_installed_aircraft_closest_to_the_row_parameters_wins()
        {
            // club class: mid wing, 15 m, 6.5 m, 400 kg, no engine: the Libelle is that row, the LS8 is heavier and the generic one is a last resort
            Assert.Equal("Schempp-Hirth Libelle H301", Match("Standard Cirrus", Libelle, Ls8, Generic).Model!.title);
        }

        [Fact]
        public void Without_the_word_and_without_a_member_the_closest_other_aircraft_is_used()
        {
            Assert.Equal("DG LS8", Match("Standard Cirrus", Ls8, Generic).Model!.title);
        }

        // ---- the data ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void No_title_hint_is_listed_on_two_rows_of_the_same_folded_designator()
        {
            var clashes = MatchingData.Reference.Entries
                .Where(e => MatchingData.Reference.RowCount(e.Icao) > 1)
                .SelectMany(e => e.TitleHints.Select(h => (hint: h.ToLowerInvariant(), e.Icao, e.Name)))
                .GroupBy(x => (x.Icao, x.hint)).Where(g => g.Select(x => x.Name).Distinct().Count() > 1)
                .Select(g => $"'{g.Key.hint}' on {string.Join(" and ", g.Select(x => x.Name))}").ToList();

            Assert.True(clashes.Count == 0, string.Join("; ", clashes));
        }

        [Fact]
        public void A_longer_hint_of_one_row_does_not_swallow_the_designation_of_another()
        {
            // "Rapture" must not pull "JS3-18 Rapture" onto the 15 m row
            Assert.Equal("Glider, 18 m class", MatchingData.Reference.FindForText("GLID", "JS3-18 Rapture")!.Name);
            Assert.Equal("Glider, 15 m class", MatchingData.Reference.FindForText("GLID", "JS3-15 Rapture")!.Name);
        }

        [Fact]
        public void A_single_row_designator_keeps_its_full_tag_points()
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [Model("Cessna 172 Skyhawk", "C172")];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), MatchingData.Reference, null, MatchingData.Airlines, MatchingData.Related);

            var result = matcher.Match(new MatchRequest("Cessna 172", "", "C172", "", "", "", false, 1, ""));

            Assert.Equal(200, result.Explanation.Ranking[0].AttributeScores[Sub.MatchAttribute.IcaoType]);
        }
    }
}
