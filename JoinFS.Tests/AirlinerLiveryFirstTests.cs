using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// Aircraft families (related.dat) no longer decide a match: how alike two aircraft are does. An airliner of the requested, verified airline that is at
    /// least 80 % alike ranks above the exact ICAO type of another airline; GA, helicopters, balloons and gliders keep "exact type first".
    /// The same scenarios run in the proximity chart (joeherwig/joinfs-aircraft-proximity-chart, test/near-identical.test.mjs).
    /// </summary>
    public class AirlinerLiveryFirstTests
    {
        static Sub.Model Model(string title, string icaoType, string classCode, string wtc, string typerole, string airline = "")
            => new(title, "Maker", title, airline, 0, typerole, "0", "", icaoType, wtc, airline, classCode, true);

        static Sub.Model Airliner(string title, string icaoType, string classCode, string wtc, string airline = "") => Model(title, icaoType, classCode, wtc, "Airliner", airline);

        static readonly Sub.Model B748Dlh = Airliner("B748 DLH", "B748", "L4J", "H", "DLH");
        static readonly Sub.Model A388Uae = Airliner("A388 UAE", "A388", "L4J", "J", "UAE");

        static string? Pick(string icao, string airline, string callsign, string title, params Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            MatchRequest request = new(title, "", icao, airline, "", "", false, Sub.TypeRole_Airliner, "") { LiveryAware = true, Callsign = callsign };
            return substitution.Resolve(request).model?.title;
        }

        static string? Pick(string icao, string airline, params Sub.Model[] models) => Pick(icao, airline, "", "Remote", models);

        // ---- no families any more ---------------------------------------------------------------------------------------

        [Fact]
        public void Related_types_are_gone_from_the_matching_data()
        {
            Assert.Null(typeof(MatchingData).GetProperty("Related"));
            Assert.Null(typeof(CombinedMatcher).GetField("RelatedTypePoints"));
        }

        // ---- the near-identical bonus -----------------------------------------------------------------------------------

        [Fact]
        public void The_near_identical_bonus_is_nothing_below_80_percent_rises_and_stops_at_the_maximum()
        {
            int Bonus(double score, double confidence) => CombinedMatcher.NearIdenticalBonus(new SimilarityResult { Score = score, Confidence = confidence });

            Assert.Equal(0.8, CombinedMatcher.NearIdenticalFrom);
            Assert.Equal(0, Bonus(0.79, 1));
            Assert.Equal(0, Bonus(0.80, 1));
            Assert.Equal(CombinedMatcher.NearIdenticalMax, Bonus(1.0, 1));
            int last = 0;
            for (double score = 0.80; score <= 1.0001; score += 0.02)
            {
                int bonus = Bonus(Math.Min(1, score), 1);
                Assert.True(bonus >= last, $"monotonic at {score}");
                last = bonus;
            }
            Assert.Equal((int)Math.Round(CombinedMatcher.NearIdenticalMax * 0.8), Bonus(1.0, 0.8));
            Assert.Equal(0, Bonus(1.0, 0.73));   // too little is known to call two aircraft identical
            Assert.Equal(0, Bonus(1.0, 0));
        }

        // ---- airliners: the verified airline and 80 % beat an exact type of another airline -------------------------------

        [Fact]
        public void A_UAE_A388_beats_the_exact_B748_of_another_airline()
            => Assert.Equal("A388 UAE", Pick("B748", "UAE", B748Dlh, A388Uae));

        [Fact]
        public void A_UAE_B744_still_beats_a_UAE_A388_and_the_B744_of_another_airline_loses_to_the_A388()
        {
            Assert.Equal("A388 UAE", Pick("B748", "UAE", Airliner("B744 BAW", "B744", "L4J", "H", "BAW"), A388Uae));
            Assert.Equal("B744 UAE", Pick("B748", "UAE", Airliner("B744 UAE", "B744", "L4J", "H", "UAE"), A388Uae));
        }

        [Fact]
        public void Without_an_airline_the_exact_type_still_wins()
            => Assert.Equal("B748 DLH", Pick("B748", "", B748Dlh, A388Uae));

        [Fact]
        public void An_airline_only_guessed_from_the_title_never_overrules_the_exact_type()
            => Assert.Equal("B748 DLH", Pick("B748", "", "", "Emirates 747-8", B748Dlh, A388Uae));

        [Fact]
        public void An_unknown_airline_code_gives_no_advantage()
            => Assert.Equal("B748 DLH", Pick("B748", "QQQ", B748Dlh, A388Uae));

        [Theory]
        [InlineData("UAE123", "A388 UAE")]       // a callsign with an airline prefix is a verified airline
        [InlineData("UAE4AB", "A388 UAE")]
        [InlineData("N748UA", "B748 DLH")]       // a registration is not
        [InlineData("D-EABC", "B748 DLH")]
        [InlineData("", "B748 DLH")]
        public void The_callsign_counts_only_when_it_starts_with_a_known_airline_code(string callsign, string expected)
            => Assert.Equal(expected, Pick("B748", "", callsign, "Remote", B748Dlh, A388Uae));

        [Fact]
        public void Below_80_percent_alike_the_exact_type_wins_even_with_the_airline()
            => Assert.Equal("B748 DLH", Pick("B748", "QFA", B748Dlh, Airliner("B738 QFA", "B738", "L2J", "M", "QFA")));

        [Fact]
        public void The_exact_type_with_the_airline_beats_everything()
            => Assert.Equal("B748 UAE", Pick("B748", "UAE", B748Dlh, A388Uae, Airliner("B748 UAE", "B748", "L4J", "H", "UAE")));

        [Fact]
        public void An_airliner_request_never_takes_a_GA_model_for_the_livery()
            => Assert.Equal("B748 DLH", Pick("B748", "UAE", B748Dlh, Model("C172 UAE", "C172", "L1P", "L", "SingleProp", "UAE")));

        // ---- everything that is not an airliner keeps "exact type first" -----------------------------------------------

        [Fact]
        public void A_GA_plane_with_a_stray_airline_callsign_is_never_re_typed()
        {
            var plain = Model("C172 plain", "C172", "L1P", "L", "SingleProp");
            var other = Model("P28A UAE", "P28A", "L1P", "L", "SingleProp", "UAE");
            Assert.Equal("C172 plain", Pick("C172", "UAE", plain, other));
            Assert.Equal("C172 plain", Pick("C172", "", "UAE123", "Remote", plain, other));
        }

        [Fact]
        public void Helicopters_keep_the_exact_type_before_the_airline()
        {
            var r44 = Model("R44 plain", "R44", "H1P", "L", "Rotorcraft");
            var r22 = Model("R22 UAE", "R22", "H1P", "L", "Rotorcraft", "UAE");
            Assert.Equal("R44 plain", Pick("R44", "UAE", r44, r22));
        }
    }
}
