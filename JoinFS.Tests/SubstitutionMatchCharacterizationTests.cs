using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// Characterization tests: they pin what <see cref="Sub.Match"/> does TODAY, scenario by scenario, so the matching
    /// engine can be refactored and extended without changing the classic behaviour by accident. Nothing here is a wish - each
    /// expectation was observed on the unmodified code. Compiled against the FS2024 build (livery parameter).
    /// </summary>
    public class SubstitutionMatchCharacterizationTests
    {
        /// <summary>Models as a scan would hand them over, with class code and WTC given explicitly (so no Doc8643 dependence).</summary>
        static Sub.Model Model(string title, string icaoType, string classCode, string wtc, string typerole,
            string variation = "Default", string airline = "", string atcId = "")
            => new(title, "Maker", title, variation, 0, typerole, "0", "", icaoType, wtc, airline, classCode, true, atcId);

        static Sub Create(params Sub.Model[] models)
        {
            var substitution = new Sub(null!);
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            return substitution;
        }

        static async Task<(Sub.Model? model, Sub.Type type, Sub.MatchTrace trace)> Match(
            Sub substitution, string title = "Remote", string livery = "", string icao = "", string airline = "",
            string classCode = "", string wtc = "", int typerole = Sub.TypeRole_SingleProp, string registration = "")
            => await substitution.Match(title, livery, icao, airline, classCode, wtc, false, typerole, registration);

        // ---- tiers 1 and 2 ---------------------------------------------------------------------------------------------

        [Fact]
        public async Task Original_exact_title_and_livery_wins_before_any_scoring()
        {
            var s = Create(Model("Exact Title", "C172", "L1P", "L", "SingleProp", variation: "Red"), Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            var (model, type, _) = await Match(s, title: "Exact Title", livery: "Red", icao: "B744", typerole: Sub.TypeRole_Airliner);

            Assert.Equal(Sub.Type.Original, type);
            Assert.Equal("Exact Title", model!.title);
        }

        [Fact]
        public async Task Original_needs_the_livery_when_one_is_requested_and_lists_the_installed_liveries()
        {
            var s = Create(Model("Cessna 172", "C172", "L1P", "L", "SingleProp", variation: "Red"));
            var (_, type, trace) = await Match(s, title: "Cessna 172", livery: "Blue", icao: "C172");

            Assert.NotEqual(Sub.Type.Original, type);
            Assert.Contains(trace.steps, step => step.Contains("not with livery/variation 'Blue'") && step.Contains("'Red'"));
        }

        [Fact]
        public async Task A_user_substitution_wins_over_everything()
        {
            var favourite = Model("My Favourite", "C172", "L1P", "L", "SingleProp");
            var s = Create(favourite, Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            s.matches = new() { ["Remote"] = favourite };

            var (model, type, trace) = await Match(s, icao: "B744", typerole: Sub.TypeRole_Airliner);

            Assert.Equal(Sub.Type.Substitute, type);
            Assert.Same(favourite, model);
            Assert.StartsWith("Substitute: found a user-defined override", trace.steps[0]);
        }

        [Fact]
        public async Task A_substitution_whose_target_is_not_installed_falls_through()
        {
            var s = Create(Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            s.matches = new() { ["Remote"] = Model("Gone", "C172", "L1P", "L", "SingleProp") };

            var (model, type, trace) = await Match(s, icao: "B744", typerole: Sub.TypeRole_Airliner);

            Assert.Equal("Boeing 747-400", model!.title);
            Assert.NotEqual(Sub.Type.Substitute, type);
            Assert.Contains(trace.steps, step => step.Contains("not currently installed"));
        }

        // ---- tier 3: scoring --------------------------------------------------------------------------------------------

        [Fact]
        public async Task Exact_icao_type_scores_200_plus_class_wtc_and_typerole()
        {
            var s = Create(Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            var (_, type, trace) = await Match(s, icao: "B744", typerole: Sub.TypeRole_Airliner);

            // 200 type + 60 class + 20 engine count + 20 engine type + 40 WTC + 15 typerole
            Assert.Equal(355, trace.topCandidates[0].totalScore);
            Assert.Equal(Sub.Type.Icao, type);
        }

        [Fact]
        public async Task Airline_adds_exactly_100_points()
        {
            var s = Create(Model("A320 Lufthansa", "A320", "L2J", "M", "Airliner", airline: "DLH"));
            var with = (await Match(s, icao: "A320", airline: "DLH", typerole: Sub.TypeRole_Airliner)).trace.topCandidates[0].totalScore;
            var without = (await Match(s, icao: "A320", typerole: Sub.TypeRole_Airliner)).trace.topCandidates[0].totalScore;

            Assert.Equal(100, with - without);
        }

        [Fact]
        public async Task Same_class_and_wtc_beat_a_different_class_when_the_exact_type_is_missing()
        {
            var s = Create(Model("Boeing 737-800", "B738", "L2J", "M", "Airliner"), Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            var (model, type, _) = await Match(s, icao: "B748", typerole: Sub.TypeRole_Airliner);

            Assert.Equal("Boeing 747-400", model!.title);
            Assert.Equal(Sub.Type.Category, type);
        }

        [Fact]
        public async Task Typerole_mismatch_without_an_exact_type_costs_240_and_the_match_is_refused_into_the_last_resort()
        {
            var s = Create(Model("Cessna 172", "C172", "L1P", "L", "SingleProp"));
            var (model, type, trace) = await Match(s, icao: "A388", classCode: "L4J", wtc: "J", typerole: Sub.TypeRole_Airliner);

            Assert.Contains(trace.steps, step => step.Contains("below the minimum match threshold"));
            Assert.Equal("Cessna 172", model!.title);              // JoinFS today: the first installed model, whatever it is
            Assert.Equal(Sub.Type.Default, type);
        }

        [Fact]
        public async Task Class_code_and_wtc_are_looked_up_from_the_icao_type_when_the_sender_gave_none()
        {
            var s = Create(Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            var (_, _, trace) = await Match(s, icao: "B744", classCode: "", wtc: "", typerole: Sub.TypeRole_Airliner);

            var classCode = trace.attributes.Single(a => a.attribute == Sub.MatchAttribute.ClassCode);
            Assert.Equal("L4J", classCode.requested);
            Assert.Equal("H", trace.attributes.Single(a => a.attribute == Sub.MatchAttribute.Wtc).requested);
        }

        [Fact]
        public async Task A_guessed_icao_tag_counts_a_fifth()
        {
            var guessed = Model("Guessed 747", "B744", "L4J", "H", "Airliner");
            guessed.icaoGuessed = true;
            var s = Create(guessed);
            var (_, _, trace) = await Match(s, icao: "B744", typerole: Sub.TypeRole_Airliner);

            // 200 -> 40, 60 -> 12, 40 -> 8, plus engine count/type 20+20 and typerole 15 which are not discounted
            Assert.Equal(40 + 12 + 20 + 20 + 8 + 15, trace.topCandidates[0].totalScore);
        }

        [Fact]
        public async Task Registration_is_a_weak_tie_breaker()
        {
            var plain = Model("Cessna 172 A", "C172", "L1P", "L", "SingleProp");
            var tail = Model("Cessna 172 B", "C172", "L1P", "L", "SingleProp", atcId: "D-EABC");
            var s = Create(plain, tail);
            var (model, _, _) = await Match(s, icao: "C172", registration: "D-EABC");

            Assert.Equal("Cessna 172 B", model!.title);
        }

        [Fact]
        public async Task The_title_prefix_scores_one_point_per_shared_character_up_to_25()
        {
            var s = Create(Model("Boeing 747-400 Lufthansa", "ZZZZ", "L4J", "H", "Airliner"));
            var (_, _, trace) = await Match(s, title: "Boeing 747-8", icao: "ZZZZ", classCode: "L4J", wtc: "H", typerole: Sub.TypeRole_Airliner);

            Assert.Contains(trace.topCandidates[0].contributions, c => c.StartsWith("title prefix (11 chars)"));
        }

        // ---- tiers 4 and 5: defaults ------------------------------------------------------------------------------------

        [Fact]
        public async Task A_configured_typerole_default_is_used_when_nothing_scores()
        {
            var heli = Model("Default Heli", "EC45", "H2T", "L", "Rotorcraft");
            var s = Create(heli, Model("Cessna 172", "C172", "L1P", "L", "SingleProp"));
            string key = s.defaultModels[Sub.TypeRole_Rotorcraft];
            s.matches = new() { [key] = heli };

            var (model, type, trace) = await Match(s, icao: "ZZZZ", typerole: Sub.TypeRole_Rotorcraft);

            Assert.Equal(Sub.Type.Default, type);
            Assert.Equal("Default Heli", model!.title);
            Assert.Contains(trace.steps, step => step.StartsWith("Default: using the configured default model"));
        }

        [Fact]
        public async Task Without_a_default_the_first_installed_model_is_the_last_resort()
        {
            var s = Create(Model("First", "C172", "L1P", "L", "SingleProp"), Model("Second", "B744", "L4J", "H", "Airliner"));
            var (model, type, trace) = await Match(s, icao: "ZZZZ", typerole: Sub.TypeRole_Glider);

            Assert.Equal("First", model!.title);
            Assert.Equal(Sub.Type.Default, type);
            Assert.Contains(trace.steps, step => step.StartsWith("Last resort: falling back to the first installed model"));
        }

        [Fact]
        public async Task With_no_models_nothing_can_be_chosen()
        {
            var s = Create();
            var (model, type, trace) = await Match(s, icao: "B744");

            Assert.Null(model);
            Assert.Equal(Sub.Type.Default, type);
            Assert.Contains(trace.steps, step => step.StartsWith("Scoring: no installed models at all"));
        }

        // ---- the explanation --------------------------------------------------------------------------------------------

        [Fact]
        public async Task The_attribute_grid_has_one_row_per_attribute_in_enum_order()
        {
            var s = Create(Model("Boeing 747-400", "B744", "L4J", "H", "Airliner"));
            var (_, _, trace) = await Match(s, icao: "B744", typerole: Sub.TypeRole_Airliner);

            Assert.Equal(Enum.GetValues<Sub.MatchAttribute>(), trace.attributes.Select(a => a.attribute));
        }

        [Fact]
        public async Task At_most_five_candidates_are_listed_winner_first()
        {
            var models = Enumerable.Range(0, 8).Select(i => Model("Boeing 747-" + i, "B744", "L4J", "H", "Airliner")).ToArray();
            var s = Create(models);
            var (_, _, trace) = await Match(s, icao: "B744", typerole: Sub.TypeRole_Airliner);

            Assert.Equal(5, trace.topCandidates.Count);
            Assert.True(trace.topCandidates[0].totalScore >= trace.topCandidates[4].totalScore);
        }

        // ---- wire values that must never move ---------------------------------------------------------------------------

        [Fact]
        public void Typerole_ids_and_enum_values_are_pinned()
        {
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
                new[] { Sub.TypeRole_SingleProp, Sub.TypeRole_TwinProp, Sub.TypeRole_Airliner, Sub.TypeRole_Rotorcraft,
                        Sub.TypeRole_Glider, Sub.TypeRole_Fighter, Sub.TypeRole_Bomber, Sub.TypeRole_FourProp,
                        Sub.TypeRole_Airship, Sub.TypeRole_Balloon });
            Assert.Equal([0, 1, 2, 3, 4, 5, 6], Enum.GetValues<Sub.Type>().Select(v => (int)v));
            Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], Enum.GetValues<Sub.MatchAttribute>().Select(v => (int)v));
        }
    }
}
