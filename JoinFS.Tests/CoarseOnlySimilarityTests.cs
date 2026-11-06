using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// A model that has no reference data of its own (only a class code: "1 piston engine") agrees with every single piston plane on those two features.
    /// That must not outrank a model whose size, speed and wing were really compared.
    /// </summary>
    public class CoarseOnlySimilarityTests
    {
        static Sub.Model Model(string title, string icaoType) => new(title, "Maker", title, "", 0, "SingleProp", "0", "", icaoType, "L", "", "L1P", true);

        [Fact]
        public void An_AN2_takes_the_beaver_and_not_a_type_without_reference_data()
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [Model("Jetson One", "ONE"), Model("DHC-2 Beaver", "DHC2")];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            MatchRequest request = new("", "", "AN2", "", "", "", false, Sub.TypeRole_SingleProp, "");
            Assert.Equal("DHC-2 Beaver", substitution.Resolve(request).model?.title);
        }

        [Fact]
        public void A_type_without_spec_data_gets_no_similarity_credit_at_all()
        {
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [Model("Jetson One", "ONE")];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            MatchRequest request = new("", "", "AN2", "", "", "", false, Sub.TypeRole_SingleProp, "");
            var trace = substitution.Resolve(request).trace;
            Assert.Contains(trace.topCandidates[0].contributions, c => c.StartsWith("similarity") && c.EndsWith("(+0)"));
        }
    }
}
