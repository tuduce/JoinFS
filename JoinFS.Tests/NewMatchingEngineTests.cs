using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// The new matching engine on the real embedded reference data (aircraft-specs.json, Doc8643, related types, airlines).
    /// Scenario tests ported from the proof-of-concept (JoinFS-ModelMatcher-Test): each says what a pilot would expect to see.
    /// </summary>
    public class NewMatchingEngineTests
    {
        static Sub.Model Model(string title, string icaoType, string classCode, string wtc, string typerole, string variation = "Default", string airline = "")
            => new(title, "Maker", title, variation, 0, typerole, "0", "", icaoType, wtc, airline, classCode, true);

        static Sub Create(MatchingEngine engine, params Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = engine };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            return substitution;
        }

        static (Sub.Model? model, Sub.Type type, Sub.MatchTrace trace) Match(Sub s, string icao, string title = "Remote",
            int typerole = Sub.TypeRole_Airliner, string airline = "", string classCode = "", string wtc = "", string livery = "")
            => s.Resolve(new MatchRequest(title, livery, icao, airline, classCode, wtc, false, typerole, "") { LiveryAware = true });

        static (Sub.Model? model, Sub.Type type, Sub.MatchTrace trace) New(string icao, int typerole, params Sub.Model[] models)
            => Match(Create(MatchingEngine.New, models), icao, typerole: typerole);

        static Sub.Model Airliner(string title, string icao, string cls, string wtc, string variation = "Default", string airline = "") => Model(title, icao, cls, wtc, "Airliner", variation, airline);

        // ---- engine switch ----------------------------------------------------------------------------------------------

        [Fact]
        public void The_new_engine_is_the_default_engine()
        {
            Assert.Equal(MatchingEngine.New, new Sub(null!).engine);
            Assert.Equal(0, (int)MatchingEngine.Classic);
            Assert.Equal(1, (int)MatchingEngine.New);
        }

        [Fact]
        public void The_new_engine_runs_when_selected()
        {
            var (_, _, trace) = New("B748", Sub.TypeRole_Airliner, Airliner("Boeing 747-400", "B744", "L4J", "H"));
            Assert.Contains(trace.steps, step => step.StartsWith("Scoring (combined)"));
        }

        // ---- the scenarios the PoC was built for ------------------------------------------------------------------------

        [Fact]
        public void A_missing_B748_is_replaced_by_the_B744_not_the_B738_or_an_A320()
        {
            var (model, _, _) = New("B748", Sub.TypeRole_Airliner,
                Airliner("Boeing 737-800", "B738", "L2J", "M"), Airliner("Boeing 747-400", "B744", "L4J", "H"), Airliner("Airbus A320", "A320", "L2J", "M"));
            Assert.Equal("Boeing 747-400", model!.title);
        }

        [Fact]
        public void A_missing_A388_prefers_a_big_twin_over_a_Cessna_even_when_the_Cessna_is_listed_first()
        {
            var (model, _, _) = New("A388", Sub.TypeRole_Airliner,
                Model("Cessna 172", "C172", "L1P", "L", "SingleProp"), Airliner("Boeing 777-300ER", "B77W", "L2J", "H"));
            Assert.Equal("Boeing 777-300ER", model!.title);
        }

        [Fact]
        public void An_A388_is_never_rendered_as_a_Cessna_the_classic_engine_would_take_it_as_last_resort()
        {
            var cessna = Model("Cessna 172", "C172", "L1P", "L", "SingleProp");

            var (classic, _, _) = Match(Create(MatchingEngine.Classic, cessna), "A388", classCode: "L4J", wtc: "J");
            var (refused, _, trace) = Match(Create(MatchingEngine.New, cessna), "A388", classCode: "L4J", wtc: "J");

            Assert.Equal("Cessna 172", classic!.title);
            Assert.Null(refused);
            Assert.Contains(trace.steps, step => step.Contains("no physically plausible"));
        }

        [Fact]
        public void A_Cessna_172_takes_a_light_single_over_an_airliner()
        {
            var (model, _, _) = New("C172", Sub.TypeRole_SingleProp,
                Airliner("Boeing 737-800", "B738", "L2J", "M"), Model("Piper Cherokee", "P28A", "L1P", "L", "SingleProp"), Model("Diamond DA40", "DA40", "L1P", "L", "SingleProp"));
            Assert.Contains(model!.title, new[] { "Piper Cherokee", "Diamond DA40" });
        }

        [Fact]
        public void A_wrong_type_tag_is_corrected_through_the_alias_table()
        {
            // an MD 500E that reports "500E": not a designator, the real one is H500
            var (model, type, trace) = Match(Create(MatchingEngine.New,
                Model("Airbus H145", "EC45", "H2T", "L", "Rotorcraft"), Model("MD Helicopters MD 500", "H500", "H1T", "L", "Rotorcraft"), Airliner("Boeing 737-800", "B738", "L2J", "M")),
                "500E", title: "MD 500E", typerole: Sub.TypeRole_Rotorcraft);

            Assert.Equal("MD Helicopters MD 500", model!.title);
            Assert.Equal(Sub.Type.Icao, type);
            Assert.Contains(trace.steps, step => step.Contains("500E") && step.Contains("H500"));
        }

        [Fact]
        public void The_same_manufacturer_beats_a_closer_looking_other_make()
        {
            var (model, _, _) = New("R44", Sub.TypeRole_Rotorcraft,
                Model("Bell 206", "B06", "H1T", "L", "Rotorcraft"), Model("Robinson R22", "R22", "H1P", "L", "Rotorcraft"));
            Assert.Equal("Robinson R22", model!.title);
        }

        [Fact]
        public void A_related_type_with_the_right_airline_beats_the_exact_type_of_another_airline()
        {
            // an A20N of South African Airways is wanted: the A320 of SAA (same family) beats the A20N of Lufthansa
            var (model, _, _) = Match(Create(MatchingEngine.New,
                Airliner("Lufthansa A20N", "A20N", "L2J", "M", airline: "DLH"), Airliner("SAA A320", "A320", "L2J", "M", airline: "SAA")),
                "A20N", airline: "SAA");
            Assert.Equal("SAA A320", model!.title);
        }

        // ---- the tiers JoinFS has always had ----------------------------------------------------------------------------

        [Fact]
        public void An_exact_title_and_livery_still_wins_the_Original_tier()
        {
            var (model, type, _) = Match(Create(MatchingEngine.New,
                Model("Exact Title", "C172", "L1P", "L", "SingleProp", variation: "Red"), Airliner("Boeing 747-400", "B744", "L4J", "H")),
                "B744", title: "Exact Title", livery: "Red");
            Assert.Equal(Sub.Type.Original, type);
            Assert.Equal("Exact Title", model!.title);
        }

        [Fact]
        public void A_user_substitution_still_wins_and_sees_the_request_uncorrected()
        {
            var favourite = Model("My Favourite", "C172", "L1P", "L", "SingleProp");
            var s = Create(MatchingEngine.New, favourite, Airliner("Boeing 747-400", "B744", "L4J", "H"));
            s.matches = new() { ["Remote"] = favourite };

            var (model, type, _) = Match(s, "B744");
            Assert.Equal(Sub.Type.Substitute, type);
            Assert.Same(favourite, model);
        }

        [Fact]
        public void A_configured_typerole_default_is_used_when_nothing_plausible_scores()
        {
            var heli = Model("Default Heli", "R44", "H1P", "L", "Rotorcraft");
            var s = Create(MatchingEngine.New, heli, Airliner("Boeing 737-800", "B738", "L2J", "M"));
            s.matches = new() { [s.defaultModels[Sub.TypeRole_Rotorcraft]] = heli };

            var (model, type, _) = Match(s, "ZZZZ", typerole: Sub.TypeRole_Rotorcraft);
            Assert.Equal("Default Heli", model!.title);
            Assert.Equal(Sub.Type.Default, type);
        }

        [Fact]
        public void The_attribute_grid_keeps_one_row_per_attribute_in_enum_order()
        {
            var (_, _, trace) = New("B748", Sub.TypeRole_Airliner, Airliner("Boeing 747-400", "B744", "L4J", "H"));
            Assert.Equal(Enum.GetValues<Sub.MatchAttribute>(), trace.attributes.Select(a => a.attribute));
        }

        [Fact]
        public void Ties_are_broken_alphabetically_so_the_answer_is_the_same_every_time()
        {
            var a = New("B748", Sub.TypeRole_Airliner, Airliner("Zeta 747", "B744", "L4J", "H"), Airliner("Alpha 747", "B744", "L4J", "H"));
            var b = New("B748", Sub.TypeRole_Airliner, Airliner("Alpha 747", "B744", "L4J", "H"), Airliner("Zeta 747", "B744", "L4J", "H"));
            Assert.Equal("Alpha 747", a.model!.title);
            Assert.Equal("Alpha 747", b.model!.title);
        }

        [Fact]
        public void With_no_models_nothing_can_be_chosen()
        {
            var (model, _, _) = New("B744", Sub.TypeRole_Airliner);
            Assert.Null(model);
        }

        // ---- the data that ships with JoinFS ----------------------------------------------------------------------------

        [Fact]
        public void The_embedded_reference_data_loads_and_is_large_enough_to_matter()
        {
            Assert.True(MatchingData.Reference.Entries.Count >= 300);
            Assert.True(MatchingData.Doc8643.IsRecognized("B744"));
            Assert.True(MatchingData.Related.AreRelated("A320", "A20N"));
            Assert.True(MatchingData.Airlines.IsKnownCode("DLH"));
        }

        [Fact]
        public void Every_reference_type_with_a_designator_is_a_Doc8643_designator_with_a_manufacturer()
        {
            var problems = MatchingData.Reference.Entries
                .Where(e => !e.NonIcao)
                .Where(e => !MatchingData.Doc8643.IsRecognized(e.Icao) || e.Manufacturer.Length == 0)
                .Select(e => e.Icao).ToList();
            Assert.True(problems.Count == 0, "not in Doc8643 or without manufacturer: " + string.Join(", ", problems));
        }

        [Fact]
        public void Reference_keys_are_unique()
        {
            var duplicates = MatchingData.Reference.Entries.GroupBy(e => e.Icao, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, "duplicate: " + string.Join(", ", duplicates));
        }

        // ---- a model's physical data is attached ------------------------------------------------------------------------

        [Fact]
        public void A_model_with_measured_data_is_judged_on_it_not_on_the_reference_row()
        {
            // a 747-400 whose own flight model says it weighs as much as a Cessna: the plausibility gate must see the measured weight
            var impostor = Airliner("Weird 747", "B744", "L4J", "H");
            impostor.specs = new AircraftSpecs { MtowKg = 1200 };
            impostor.specs.Set("Mtow", SpecSource.Config);

            var (model, _, _) = New("B748", Sub.TypeRole_Airliner, impostor, Airliner("Boeing 747-400", "B744", "L4J", "H"));
            Assert.Equal("Boeing 747-400", model!.title);
        }
    }
}
