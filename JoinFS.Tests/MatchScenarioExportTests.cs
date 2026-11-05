using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using JoinFS.Matching;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// JoinFS' answer for a set of remote aircraft against real installed-model sets (MatchScenarioModels.json), written to MatchScenarios.json - the
    /// golden file the proximity map (joeherwig/charts, test/matcher.test.mjs) must reproduce winner and score for. The test fails when the committed
    /// file is out of date; set JOINFS_UPDATE_GOLDEN=1 to rewrite it after a deliberate change of the matcher, then copy both files to the map repo.
    /// </summary>
    public class MatchScenarioExportTests
    {
        sealed record WorldModel(string title, string manufacturer, string type, string variation, string role, string tag, string wtc, string airline, string classCode, bool confirmed);

        sealed record Scenario(string world, string tag, string title, string livery, string airline);

        static string Folder([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

        static Dictionary<string, List<WorldModel>> LoadWorlds()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "MatchScenarioModels.json")));
            Dictionary<string, List<WorldModel>> worlds = [];
            foreach (var world in doc.RootElement.GetProperty("worlds").EnumerateObject())
            {
                worlds[world.Name] = [.. world.Value.EnumerateArray().Select(m => new WorldModel(
                    m.GetProperty("title").GetString()!, m.GetProperty("manufacturer").GetString()!, m.GetProperty("type").GetString()!, m.GetProperty("variation").GetString()!,
                    m.GetProperty("role").GetString()!, m.GetProperty("tag").GetString()!, m.GetProperty("wtc").GetString()!, m.GetProperty("airline").GetString()!,
                    m.GetProperty("classCode").GetString()!, m.GetProperty("confirmed").GetBoolean()))];
            }
            return worlds;
        }

        /// <summary>The models as JoinFS loads a models list: constructed, then the class code, wake and typerole re-derived from the Doc8643 designator.</summary>
        static Sub BuildInstalled(List<WorldModel> world)
        {
            Dictionary<string, (string classCode, string wtc)> doc8643 = [];
            foreach (var row in MatchingData.Doc8643.Rows) doc8643.TryAdd(row.IcaoType, (row.ClassCode, row.Wtc));

            var substitution = new Sub(null!) { engine = MatchingEngine.New };
            substitution.LoadDoc8643Index();
            List<Sub.Model> models = [];
            foreach (var m in world)
            {
                Sub.Model model = new(m.title, m.manufacturer, m.type, m.variation, 0, m.role, "0", "", m.tag, m.wtc, m.airline, m.confirmed ? m.classCode : "", m.confirmed);
                model.RefreshIcaoDerived(doc8643);
                models.Add(model);
            }
            substitution.models = models;
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            return substitution;
        }

        static readonly string[] GridTypes = ["A20N", "A320", "A321", "A21N", "A319", "B738", "B739", "B38M", "B744", "B748", "B77W", "B788", "A388", "A343", "A359", "E190", "AT76", "CRJ7", "MD11", "B752"];
        static readonly string[] GridAirlines = ["", "RYR", "UAE", "AFR", "DLH", "NTL", "RUN", "ABB", "SAA", "AEE", "BAW", "KLM", "UAL"];

        static List<Scenario> Scenarios()
        {
            List<Scenario> list = [];
            // the cases of the issue first
            foreach (var (tag, airline) in new[] { ("B739", "RYR"), ("A20N", "RYR"), ("B744", "UAE"), ("B748", "UAE"), ("A20N", "AFR"), ("B38M", "BER"), ("A320", "NTL"), ("B738", "RYR") })
            {
                list.Add(new Scenario("msfs2024", tag, "", "", airline));
            }
            // every airliner type of the grid with every airline of the grid (and none)
            foreach (var tag in GridTypes)
            {
                foreach (var airline in GridAirlines) list.Add(new Scenario("msfs2024", tag, "", "", airline));
            }
            // light aircraft, helicopters and gliders: no airline, a title where one helps
            foreach (var tag in new[] { "C172", "DA40", "TBM9", "EC45", "AS50", "PC12", "DHC2", "P51" }) list.Add(new Scenario("msfs2024", tag, "", "", ""));
            foreach (var title in new[] { "LS4 Red", "K7 Cream/Red (imperial)", "JS3-18 Rapture", "Grunau Baby", "Mystery 77" }) list.Add(new Scenario("msfs2024", "GLID", title, "", ""));
            return list;
        }

        static string Run(Dictionary<string, List<WorldModel>> worlds, List<Scenario> scenarios)
        {
            Dictionary<string, Sub> installed = worlds.ToDictionary(w => w.Key, w => BuildInstalled(w.Value));
            Dictionary<string, CombinedMatcher> matchers = installed.ToDictionary(s => s.Key,
                s => new CombinedMatcher(new SubstitutionCatalog(s.Value), MatchingData.Reference, null, MatchingData.Airlines));

            StringBuilder text = new();
            text.Append("{\n  \"_comment\": \"JoinFS' answers (CombinedMatcher) for remote aircraft against the sets of MatchScenarioModels.json. Written by MatchScenarioExportTests (JOINFS_UPDATE_GOLDEN=1); the proximity map must reproduce winner and total. Keep the copies in both repos identical.\",\n  \"scenarios\": [\n");
            for (int i = 0; i < scenarios.Count; i++)
            {
                Scenario s = scenarios[i];
                MatchRequest request = new(s.title, s.livery, s.tag, s.airline, "", "", false, Sub.TypeRole_SingleProp, "") { LiveryAware = true };
                CombinedResult result = matchers[s.world].Match(request);
                RankedCandidate? ranked = result.Explanation.Ranking.FirstOrDefault(r => r.Model == result.Model);
                string winner = result.Model == null ? "null"
                    : $"{{\"title\":{Json(result.Model.title)},\"variation\":{Json(result.Model.variation)},\"tag\":{Json(result.Model.icaoType)},\"airline\":{Json(result.Model.icaoAirline)},\"total\":{(ranked == null ? "null" : ranked.CombinedScore.ToString(CultureInfo.InvariantCulture))},\"type\":{Json(result.Type.ToString())}}}";
                text.Append($"    {{\"world\":{Json(s.world)},\"remote\":{{\"tag\":{Json(s.tag)},\"title\":{Json(s.title)},\"livery\":{Json(s.livery)},\"airline\":{Json(s.airline)}}},\"expect\":{winner}}}");
                text.Append(i < scenarios.Count - 1 ? ",\n" : "\n");
            }
            text.Append("  ]\n}\n");
            return text.ToString();
        }

        static string Json(string value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        [Fact]
        public void The_golden_file_holds_JoinFS_current_answers()
        {
            string path = Path.Combine(Folder(), "MatchScenarios.json");
            string current = Run(LoadWorlds(), Scenarios());

            if (Environment.GetEnvironmentVariable("JOINFS_UPDATE_GOLDEN") == "1")
            {
                File.WriteAllText(path, current, new UTF8Encoding(false));
                return;
            }

            string committed = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
            Assert.True(committed == current, "MatchScenarios.json is out of date: run the tests with JOINFS_UPDATE_GOLDEN=1, check the diff, copy both json files to the proximity map repo (test/fixtures).");
        }
    }
}
