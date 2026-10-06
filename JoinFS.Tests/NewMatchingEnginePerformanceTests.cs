using System.Diagnostics;
using JoinFS.Matching;
using Sub = JoinFS.Substitution;
using Xunit.Abstractions;

namespace JoinFS.Tests
{
    /// <summary>
    /// The matcher runs on the sim thread once per remote aircraft (and again on identity change / injection retry), so a join burst of
    /// dozens of aircraft must not stall the sim. These tests build a realistic 10,000-model list from the reference types and time Resolve.
    /// </summary>
    public class NewMatchingEnginePerformanceTests(ITestOutputHelper output)
    {
        /// <summary>~10k models: every reference type with a designator, each installed in many liveries, like an FSLTL/AIG-heavy MSFS install.</summary>
        static Sub.Model[] TenThousandModels()
        {
            var types = MatchingData.Reference.Entries.Where(e => !e.NonIcao).ToList();
            List<Sub.Model> models = [];
            int i = 0;
            while (models.Count < 10_000)
            {
                var type = types[i % types.Count];
                var doc = MatchingData.Doc8643.TryGet(type.Icao, out var row) ? row : null;
                string role = type.Specs.Rotor == true ? "Rotorcraft" : (type.Specs.EngineCount ?? 1) >= 2 && type.Specs.Engine == EngineKind.Jet ? "Airliner" : "SingleProp";
                models.Add(new Sub.Model($"{type.Name} #{i}", type.Manufacturer, type.Name, "Livery " + (i / types.Count), 0, role, "0", "",
                    type.Icao, doc?.Wtc ?? "", i % 7 == 0 ? "DLH" : "", doc?.ClassCode ?? "", true));
                i++;
            }
            return [.. models];
        }

        static Sub Create(MatchingEngine engine, Sub.Model[] models)
        {
            var substitution = new Sub(null!) { engine = engine };
            substitution.LoadDoc8643Index();
            substitution.models = [.. models];
            substitution.RebuildTitleIndex();
            substitution.MakeIcaoIndex();
            return substitution;
        }

        static readonly string[] Requests = ["B748", "A388", "C172", "500E", "R44", "A20N", "DH8D", "B77W", "PC12", "ZZZZ"];

        double AverageMilliseconds(Sub substitution, int rounds)
        {
            // warm up: loads the embedded data, builds caches, JITs
            foreach (var icao in Requests) substitution.Resolve(new MatchRequest("Warm " + icao, "", icao, "", "", "", false, Sub.TypeRole_Airliner, ""));

            var stopwatch = Stopwatch.StartNew();
            int count = 0;
            for (int round = 0; round < rounds; round++)
            {
                foreach (var icao in Requests)
                {
                    substitution.Resolve(new MatchRequest("Remote " + icao + round, "", icao, "DLH", "", "", false, Sub.TypeRole_Airliner, ""));
                    count++;
                }
            }
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds / count;
        }

        [Fact]
        public void Classic_and_new_engine_timing_on_ten_thousand_models()
        {
            var models = TenThousandModels();
            double classic = AverageMilliseconds(Create(MatchingEngine.Classic, models), 5);
            double combined = AverageMilliseconds(Create(MatchingEngine.New, models), 5);

            output.WriteLine($"models: {models.Length}; classic {classic:0.00} ms/request, new {combined:0.00} ms/request");

            // generous bound so the test is not flaky on a busy CI machine; the measured numbers above are what matters
            Assert.True(combined < 100, $"new engine too slow: {combined:0.0} ms per request");
        }
    }
}
