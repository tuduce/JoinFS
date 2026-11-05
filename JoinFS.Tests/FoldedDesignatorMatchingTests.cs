using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// GLID/GLIM (and BALL ...) stand for many different aircraft; on the real reference data the title decides which model fits.
    /// The models are an excerpt of a real Microsoft Flight Simulator 2024 models list and of an X-Plane one.
    /// </summary>
    public class FoldedDesignatorMatchingTests
    {
        static Sub.Model Model(string title, string icaoType, string typerole, string variation = "")
            => new(title, "", "", variation, 0, typerole, "0", "", icaoType, "L", "", "", false);

        static readonly Sub.Model[] Msfs2024Gliders =
        [
            Model("DG-1001-E Neo", "", "Segler"),
            Model("DG LS8", "LS8", "Einmot Prop"),
            Model("Schleicher K7 GlideSimmer+B21", "AS7", "Segler"),
            Model("LS4", "LS4", "Segler"),
            Model("LS4 Red", "LS4", "Segler"),
            Model("LS4 Swiss", "LS4", "Segler"),
            Model("HotAirBalloon Passengers", "", "Ballon"),
        ];

        static CombinedResult Match(string title, string icao, int typerole, params Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), MatchingData.Reference, null, MatchingData.Airlines, MatchingData.Related);
            return matcher.Match(new MatchRequest(title, "", icao, "", "", "", false, typerole, ""));
        }

        // ---- the reference data ---------------------------------------------------------------------------------------------

        [Fact]
        public void GLID_and_GLIM_have_several_rows_and_the_generic_one_comes_first()
        {
            Assert.True(MatchingData.Reference.RowCount("GLID") >= 4);
            Assert.True(MatchingData.Reference.RowCount("GLIM") >= 2);
            Assert.StartsWith("Generic", MatchingData.Reference.Find("GLID")!.Name);
            Assert.StartsWith("Generic", MatchingData.Reference.Find("GLIM")!.Name);
        }

        [Fact]
        public void A_title_hint_of_a_folded_designator_is_not_also_a_hint_of_another_designator()
        {
            // the same hint on two designators would make the type inferred from an untagged title depend on file order
            var folded = MatchingData.Reference.Entries.Where(e => MatchingData.Reference.RowCount(e.Icao) > 1).ToList();
            var others = MatchingData.Reference.Entries.Where(e => !folded.Contains(e)).SelectMany(e => e.TitleHints.Select(h => (hint: h, e.Icao)))
                .ToLookup(h => h.hint.ToLowerInvariant());

            // a class row lists its members as hints, so a hint may be shared with a specific type that can stand in for the folded designator; with any other type it must not
            var clashes = folded.SelectMany(e => e.TitleHints.Where(h => others.Contains(h.ToLowerInvariant()))
                .SelectMany(h => others[h.ToLowerInvariant()].Where(o => !MatchingData.Reference.IsStandInFor(e.Icao, o.Icao)).Select(o => $"'{h}' on {e.Icao} and {o.Icao}"))).ToList();

            Assert.True(clashes.Count == 0, string.Join("; ", clashes));
        }

        [Theory]
        [InlineData("GLID", "Grunau Baby II", "Glider, classic wooden")]
        [InlineData("GLID", "ASK 13 Club", "Glider, two-seat trainer")]
        [InlineData("GLID", "Nimbus 4", "Glider, open class")]
        [InlineData("GLID", "LS4 Swiss", "Glider, standard class 15 m")]
        [InlineData("GLID", "Libelle H301", "Glider, club class")]
        [InlineData("GLID", "PW-5 Smyk", "Glider, 13.5 m class")]
        [InlineData("GLID", "Ventus 3", "Glider, 15 m class")]
        [InlineData("GLID", "LAK-17 Blue", "Glider, 18 m class")]
        [InlineData("GLID", "Arcus", "Glider, two-seat 20 m class")]
        [InlineData("GLID", "K7 Cream/Red (imperial)", "Glider, classic wooden")]
        [InlineData("GLID", "SZD-30 Pirat", "Glider, classic wooden")]
        [InlineData("GLID", "Something Unknown", "Generic glider")]
        [InlineData("GLIM", "Super Dimona", "Motor glider, two-seat touring")]
        [InlineData("GLIM", "Lange Antares 20E", "Motor glider, self-launching sailplane")]
        [InlineData("GLIM", "Something Unknown", "Generic motor glider")]
        public void The_title_picks_the_row(string icao, string title, string expectedRow)
        {
            Assert.Equal(expectedRow, MatchingData.Reference.FindForText(icao, title)!.Name);
        }

        [Theory]
        [InlineData("GLID", "ASK 21", "Schleicher ASK 21")]
        [InlineData("GLID", "Schleicher ASK-21 Mi", "Schleicher ASK 21")]
        [InlineData("GLID", "ASW 20 CL", "Schleicher ASW 20")]
        [InlineData("GLID", "ASW-22 BLE", "Schleicher ASW 22")]
        [InlineData("GLID", "ASW 24", "Schleicher ASW 24")]
        [InlineData("GLID", "ASW 27", "Schleicher ASG 29 / ASW 27")]
        [InlineData("GLID", "ASW 28-18", "Schleicher ASW 28")]
        [InlineData("GLID", "ASH 25", "Schleicher ASH 25")]
        [InlineData("GLID", "ASH-25", "Schleicher ASH 25")]
        [InlineData("GLID", "ASW 15", "Glider, standard class 15 m")]
        [InlineData("GLID", "ASW-19B", "Glider, standard class 15 m")]
        [InlineData("GLID", "ASW 17", "Glider, open class")]
        [InlineData("GLID", "ASK 18", "Glider, club class")]
        [InlineData("GLID", "ASK-23", "Glider, club class")]
        [InlineData("GLID", "ASK-13", "Glider, two-seat trainer")]
        [InlineData("GLIM", "ASK 14", "Motor glider, two-seat touring")]
        [InlineData("GLIM", "ASK 16", "Motor glider, two-seat touring")]
        [InlineData("GLIM", "ASH 26 E", "Motor glider, self-launching sailplane")]
        [InlineData("GLIM", "ASH-31 Mi", "Motor glider, self-launching sailplane")]
        public void Schleicher_gliders_reported_as_a_folded_designator_are_resolved_by_title(string icao, string title, string expectedRow)
        {
            Assert.Equal(expectedRow, MatchingData.Reference.FindForText(icao, title)!.Name);
        }

        [Fact]
        public void A_glider_with_a_row_of_its_own_is_described_by_it()
        {
            Assert.Equal(17.0, MatchingData.Reference.FindForText("GLID", "Schleicher ASK 21 Club")!.SpanM);
        }

        // ---- the matcher on a real MSFS list -------------------------------------------------------------------------------

        [Fact]
        public void A_remote_LS4_glider_gets_an_LS4_not_another_glider()
        {
            var result = Match("LS4 Swiss", "GLID", Sub.TypeRole_Glider, Msfs2024Gliders);

            Assert.StartsWith("LS4", result.Model!.title);
        }

        [Fact]
        public void A_remote_DG_1001_gets_the_installed_DG_1001()
        {
            var result = Match("DG-1001 Club", "GLID", Sub.TypeRole_Glider, Msfs2024Gliders);

            Assert.Equal("DG-1001-E Neo", result.Model!.title);
        }

        [Fact]
        public void A_remote_wooden_glider_gets_the_wooden_K7_not_a_modern_one()
        {
            var result = Match("Grunau Baby", "GLID", Sub.TypeRole_Glider, Msfs2024Gliders);

            Assert.Equal("Schleicher K7 GlideSimmer+B21", result.Model!.title);
        }

        [Fact]
        public void A_remote_glider_without_any_hint_is_matched_deterministically()
        {
            var first = Match("Mystery 77", "GLID", Sub.TypeRole_Glider, Msfs2024Gliders).Model!.title;
            var second = Match("Mystery 77", "GLID", Sub.TypeRole_Glider, [.. Msfs2024Gliders.Reverse()]).Model!.title;

            Assert.Equal(first, second);
        }

        [Fact]
        public void A_remote_glider_never_gets_the_balloon()
        {
            var result = Match("LS4 Swiss", "GLID", Sub.TypeRole_Glider, Model("HotAirBalloon Passengers", "", "Ballon"));

            Assert.True(result.Model == null || result.Model.title != "HotAirBalloon Passengers");
        }

        [Fact]
        public void Models_with_the_same_folded_tag_are_each_described_by_their_own_title_not_by_the_first_one_resolved()
        {
            // the generic model comes first: its specs must not be reused for the K7 that shares the tag GLID
            var generic = Model("GLID blue", "GLID", "Segler");
            var k7 = Model("K7 Red (metric)", "GLID", "Segler");

            var result = Match("Grunau Baby", "GLID", Sub.TypeRole_Glider, generic, k7);

            Assert.Equal("K7 Red (metric)", result.Model!.title);
            Assert.Equal(14.5, result.Explanation.Ranking.First(r => r.Model == k7).CandidateSpecs.Specs.SpanM);   // the classic wooden line lists K7 as a hint
            Assert.Equal(15.0, result.Explanation.Ranking.First(r => r.Model == generic).CandidateSpecs.Specs.SpanM);
        }

        static readonly Sub.Model[] GliderList =
        [
            Model("SZD-30 D-6758", "GLID", "Segler", "D-6758"),          // a wooden Pirat, tagged only GLID
            Model("GLID blue", "GLID", "Segler", "blue"),                 // a generic one
            Model("JS3-15 (TN)", "JS3-15", "Segler", "TN"),               // a tag that is no designator
            Model("DG LS8", "LS8", "Einmot Prop"),
        ];

        [Fact]
        public void A_remote_LS4_gets_the_installed_LS8_not_a_wooden_or_generic_glider_that_merely_carries_the_tag_GLID()
        {
            var result = Match("LS4", "GLID", Sub.TypeRole_Glider, GliderList);

            Assert.Equal("DG LS8", result.Model!.title);
        }

        [Fact]
        public void The_tag_still_counts_in_full_for_a_model_of_the_same_kind()
        {
            // a wooden remote and a wooden model, both reported as GLID: the tag and the kind agree
            var result = Match("Grunau Baby", "GLID", Sub.TypeRole_Glider, GliderList);

            Assert.Equal("SZD-30 D-6758", result.Model!.title);
        }

        [Fact]
        public void A_designator_with_one_row_keeps_the_full_tag_points()
        {
            var result = Match("Cessna 172", "C172", 1, Model("Cessna 172 Skyhawk", "C172", "Einmot Prop"), Model("Piper Cherokee", "P28A", "Einmot Prop"));

            Assert.Equal(200, result.Explanation.Ranking.First(r => r.Model.title == "Cessna 172 Skyhawk").AttributeScores[Sub.MatchAttribute.IcaoType]);
        }

        [Fact]
        public void The_placeholder_manufacturer_ANY_is_no_manufacturer()
        {
            Assert.Equal("", ManufacturerKey.From("(ANY)"));
        }

        // ---- the X-Plane list has one generic glider --------------------------------------------------------------------------

        [Fact]
        public void On_X_Plane_the_generic_GLID_model_is_still_the_answer_for_any_glider()
        {
            var result = Match("Grunau Baby", "GLID", Sub.TypeRole_Glider, Model("GLID GLID 000", "GLID", "Segler", "GLID 000"));

            Assert.Equal("GLID GLID 000", result.Model!.title);
        }
    }
}
