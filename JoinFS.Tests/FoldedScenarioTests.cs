using System.Runtime.CompilerServices;
using System.Text.Json;
using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// The scenarios in FoldedScenarios.json, shared with the proximity map's tests (test/matcher.test.mjs): both engines must pick the same installed model.
    /// </summary>
    public class FoldedScenarioTests
    {
        static string SourcePath([CallerFilePath] string path = "") => path;

        static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath())!, "FoldedScenarios.json")));

        public static IEnumerable<object[]> Scenarios()
        {
            using JsonDocument doc = Load();
            foreach (var s in doc.RootElement.GetProperty("scenarios").EnumerateArray()) yield return [s.GetProperty("name").GetString()!];
        }

        [Theory]
        [MemberData(nameof(Scenarios))]
        public void The_scenario_picks_the_expected_installed_model(string name)
        {
            using JsonDocument doc = Load();
            var scenario = doc.RootElement.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

            var models = scenario.GetProperty("installed").EnumerateArray().Select(m =>
                new Sub.Model(m.GetProperty("title").GetString()!, "", "", m.GetProperty("variation").GetString()!, 0, "Segler", "0", "", m.GetProperty("tag").GetString()!, "L", "", "", false)).ToArray();
            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            var matcher = new CombinedMatcher(new SubstitutionCatalog(substitution), MatchingData.Reference, null, MatchingData.Airlines);

            var remote = scenario.GetProperty("remote");
            var result = matcher.Match(new MatchRequest(remote.GetProperty("title").GetString()!, "", remote.GetProperty("tag").GetString()!, "", "", "", false, Sub.TypeRole_Glider, ""));

            if (scenario.TryGetProperty("expect", out var expect)) Assert.Equal(expect.GetString(), result.Model!.title);
            if (scenario.TryGetProperty("expectNot", out var not)) Assert.NotEqual(not.GetString(), result.Model!.title);
            if (scenario.TryGetProperty("expectVariation", out var variation)) Assert.Equal(variation.GetString(), result.Model!.variation);
        }

        [Fact]
        public void The_collapsed_designators_the_map_knows_are_the_ones_Doc8643_collapses()
        {
            // the map has no Doc8643, it carries this list (src/matcher.js COLLAPSED): keep both in step
            string[] inMap = ["BALL", "GLID", "GLIM", "GYRO", "PARA", "SHIP", "UHEL", "ULAC"];
            var fromDoc8643 = MatchingData.Doc8643.Rows.Select(r => r.IcaoType).Distinct().Where(MatchingData.Doc8643.IsCollapsed).Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(inMap, fromDoc8643);
        }
    }
}
