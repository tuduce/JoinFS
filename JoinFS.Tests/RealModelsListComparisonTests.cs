using Xunit.Abstractions;
using Sub = JoinFS.Substitution;

namespace JoinFS.Tests
{
    /// <summary>
    /// Opt-in check against a real installation: point the environment variable JOINFS_MODELS_FILE at a "models - &lt;sim&gt;.txt" and the test
    /// resolves a set of typical remote aircraft with both engines and prints the choices side by side. Without the variable it does nothing,
    /// so it never depends on someone's private data. It asserts only that the new engine never picks something implausible.
    /// </summary>
    public class RealModelsListComparisonTests(ITestOutputHelper output)
    {
        static List<Sub.Model> Load(string path)
        {
            // the class code / WTC / typerole JoinFS derives from the ICAO type when it loads a models file
            Dictionary<string, (string classCode, string wtc)> doc8643 = new(StringComparer.OrdinalIgnoreCase);
            foreach (var row in JoinFS.Matching.MatchingData.Doc8643.Rows) doc8643.TryAdd(row.IcaoType, (row.ClassCode, row.Wtc));

            List<Sub.Model> models = [];
            string[] separator = ["[+]"];
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith('#') || line.Trim().Length == 0) continue;
                string[] p = line.Split(separator, StringSplitOptions.None);
                if (p.Length < 13) continue;
                var model = new Sub.Model(p[0], p[1], p[2], p[3], 0, p[5], p[6], p[7], p[8], p[9], p[10], p[11], bool.TryParse(p[12], out bool confirmed) && confirmed);
                model.RefreshIcaoDerived(doc8643);
                models.Add(model);
            }
            return models;
        }

        static readonly (string icao, string title, int role)[] Requests =
        [
            ("B748", "Boeing 747-8 Intercontinental", Sub.TypeRole_Airliner), ("A388", "Airbus A380-800", Sub.TypeRole_Airliner),
            ("500E", "MD 500E", Sub.TypeRole_Rotorcraft), ("R44", "Robinson R44", Sub.TypeRole_Rotorcraft), ("C206", "Cessna 206 Stationair", Sub.TypeRole_SingleProp),
            ("A20N", "Airbus A320neo", Sub.TypeRole_Airliner), ("DH8D", "Dash 8 Q400", Sub.TypeRole_TwinProp), ("PC12", "Pilatus PC-12", Sub.TypeRole_SingleProp),
            ("B06", "Bell 206 JetRanger", Sub.TypeRole_Rotorcraft), ("DC3", "Douglas DC-3", Sub.TypeRole_TwinProp)
        ];

        [Fact]
        public void Classic_and_new_engine_side_by_side_on_a_real_models_list()
        {
            string? path = Environment.GetEnvironmentVariable("JOINFS_MODELS_FILE");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var models = Load(path);
            Sub Create(MatchingEngine engine)
            {
                var s = new Sub(null!) { engine = engine };
                s.LoadDoc8643Index();
                s.models = [.. models];
                s.RebuildTitleIndex();
                s.MakeIcaoIndex();
                return s;
            }
            var classic = Create(MatchingEngine.Classic);
            var combined = Create(MatchingEngine.New);

            output.WriteLine($"{models.Count} models loaded from {path}");
            foreach (var (icao, title, role) in Requests)
            {
                var request = new MatchRequest(title, "", icao, "", "", "", false, role, "") { LiveryAware = true };
                var (oldModel, oldType, _) = classic.Resolve(request);
                var (newModel, newType, newTrace) = combined.Resolve(request);
                output.WriteLine($"{icao,-5} classic: {Describe(oldModel, oldType),-70} new: {Describe(newModel, newType)}");
                Assert.DoesNotContain(newTrace.steps, step => step.Contains("Exception"));
            }
        }

        static string Describe(Sub.Model? model, Sub.Type type) => model == null ? $"(none) [{type}]" : $"{model.title} / {model.variation} [{type}]";
    }
}
