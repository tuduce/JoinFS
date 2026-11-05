using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// For a designator ICAO folded several aircraft into (GLID ...), words shared by the remote title and an installed model's title
    /// order the otherwise equal candidates. A tie-breaker, never a way to beat the type signals.
    /// </summary>
    public class TitleTokenBonusTests
    {
        // ---- the token helper -----------------------------------------------------------------------------------------------

        static int Shared(string a, string b) => TitleTokens.SharedCount(TitleTokens.Of(a), TitleTokens.Of(b));

        [Fact]
        public void Words_are_compared_ignoring_case_and_punctuation()
        {
            Assert.Equal(Shared("DG-1001 Neo", "dg 1001 neo"), Shared("DG-1001 Neo", "DG-1001 Neo"));
            Assert.True(Shared("DG-1001 Neo", "dg 1001 neo") >= 2);
        }

        [Fact]
        public void Generic_words_do_not_count()
        {
            Assert.Equal(0, Shared("Glider", "Glider"));
            Assert.Equal(0, Shared("Sailplane Default", "Segelflugzeug default"));
        }

        [Fact]
        public void A_shared_series_name_counts_less_than_the_full_designation()
        {
            Assert.True(Shared("ASK 21", "Schleicher ASK 21") > Shared("ASK 21", "ASK 13"));
            Assert.True(Shared("ASK 21", "ASK 13") > 0);
        }

        [Fact]
        public void A_designation_written_with_or_without_a_space_is_the_same()
        {
            Assert.True(Shared("ASK 21", "ASK21 Club") > Shared("ASK 21", "ASK 13"));
        }

        [Fact]
        public void Nothing_in_common_counts_nothing()
        {
            Assert.Equal(0, Shared("Grunau Baby", "Discus"));
            Assert.Equal(0, Shared("", "Discus"));
        }

        [Fact]
        public void The_bonus_is_ten_points_per_shared_word_up_to_the_maximum()
        {
            Assert.Equal(0, CombinedMatcher.TitleTokenBonus(0));
            Assert.Equal(10, CombinedMatcher.TitleTokenBonus(1));
            Assert.Equal(CombinedMatcher.TitleTokenBonusMax, CombinedMatcher.TitleTokenBonus(50));
        }

        [Fact]
        public void The_bonus_can_never_lift_a_related_type_over_the_exact_type()
        {
            // exact ICAO type 200, related type 120 (RelatedTypePoints): the bonus must stay inside that gap
            Assert.True(CombinedMatcher.TitleTokenBonusMax < 200 - CombinedMatcher.RelatedTypePoints);
        }

        // ---- in the matcher ---------------------------------------------------------------------------------------------------

        static string Row(string icao, string name, string hints, double span, double mtow) => FormattableString.Invariant(
            $"{{\"icao\": \"{icao}\", \"name\": \"{name}\", \"manufacturer\": \"-\", \"titleHints\": [{hints}], \"wing\": \"mid\", \"spanM\": {span}, \"lengthM\": 7, \"mtowKg\": {mtow}, \"engines\": 0, \"engineType\": \"none\", \"cruiseKt\": 70, \"gear\": \"taildragger\"}}");

        static ReferenceSpecs Gliders(bool ambiguous) => ReferenceSpecs.FromJson("{\"aircraft\": [" + string.Join(",",
            ambiguous
                ? [Row("GLID", "Generic glider", "\"Glider\"", 15, 500), Row("GLID", "Glider, classic wooden", "\"Grunau Baby\"", 13, 260)]
                : [Row("GLID", "Generic glider", "\"Glider\"", 15, 500)]) + "]}");

        static Sub.Model Glider(string title, string variation = "Default")
            => new(title, "Maker", title, variation, 0, "Glider", "0", "", "GLID", "L", "", "", true);

        static CombinedResult Match(ReferenceSpecs reference, string remoteTitle, params Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), reference);
            return matcher.Match(new MatchRequest(remoteTitle, "", "GLID", "", "", "", false, Sub.TypeRole_Glider, ""));
        }

        [Fact]
        public void Among_equal_candidates_the_one_sharing_the_title_wins()
        {
            // neither title is in the data, so only the shared word "Quasar" can tell them apart (alphabetically "Alpha" would win)
            var result = Match(Gliders(true), "Quasar 7", Glider("Alpha Wood"), Glider("Zeta Quasar 7"));

            Assert.Equal("Zeta Quasar 7", result.Model!.title);
            Assert.Contains(result.Explanation.Ranking[0].BaselineContributions, c => c.Contains("title") && c.Contains("+"));
        }

        [Fact]
        public void A_designator_with_a_single_row_gets_no_bonus()
        {
            var result = Match(Gliders(false), "Quasar 7", Glider("Alpha Wood"), Glider("Zeta Quasar 7"));

            Assert.Equal("Alpha Wood", result.Model!.title);   // the alphabetical tie-break: nothing else distinguishes them
            Assert.DoesNotContain(result.Explanation.Ranking.SelectMany(r => r.BaselineContributions), c => c.Contains("shared title"));
        }
    }
}
