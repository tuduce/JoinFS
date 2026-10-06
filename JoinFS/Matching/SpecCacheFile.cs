using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model = JoinFS.Substitution.Model;

namespace JoinFS.Matching
{
    /// <summary>
    /// The measured data of the installed models in a small text file, <c>specs - &lt;sim&gt;.txt</c>, next to <c>models - &lt;sim&gt;.txt</c> (whose
    /// format stays untouched). One <c>S</c> line per distinct aircraft (all liveries share it) and one <c>M</c> line per model:
    /// <code>
    /// S[+]id[+]rotor[+]wing[+]span[+]length[+]mtow[+]engines[+]engineType[+]cruise[+]gear[+]manufacturer
    /// M[+]id[+]title[+]variation
    /// </code>
    /// Numbers use the invariant culture. Only measured values (read from the model's own files or the simulator) are stored - public reference
    /// values come from the program's own data and would only go stale in a file. A damaged line is skipped, never fatal; the file is a cache
    /// that the next model scan rebuilds.
    /// </summary>
    public static class SpecCacheFile
    {
        const string Separator = "[+]";
        const int SpecFields = 12;

        public static IEnumerable<string> Write(IEnumerable<Model> models)
        {
            yield return "# measured model data - a cache rebuilt by every model scan, safe to delete";

            Dictionary<AircraftSpecs, int> ids = new(ReferenceEqualityComparer.Instance);
            List<string> modelLines = [];
            List<string> specLines = [];
            foreach (var model in models)
            {
                AircraftSpecs specs = model.specs;
                if (specs == null || !IsMeasured(specs)) continue;

                if (!ids.TryGetValue(specs, out int id))
                {
                    id = ids.Count + 1;
                    ids[specs] = id;
                    specLines.Add(SpecLine(id, specs));
                }
                modelLines.Add(string.Join(Separator, "M", id.ToString(CultureInfo.InvariantCulture), model.title, model.variation));
            }

            foreach (var line in specLines) yield return line;
            foreach (var line in modelLines) yield return line;
        }

        /// <summary>The measured specs by (title, variation); models that share an aircraft share one <see cref="AircraftSpecs"/> object.</summary>
        public static Dictionary<(string title, string variation), AircraftSpecs> Read(IEnumerable<string> lines)
        {
            Dictionary<string, AircraftSpecs> specsById = [];
            Dictionary<(string, string), AircraftSpecs> result = [];

            foreach (var line in lines)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(Separator, StringSplitOptions.None);

                if (parts[0] == "S" && parts.Length == SpecFields)
                {
                    AircraftSpecs specs = ParseSpecs(parts);
                    if (specs != null) specsById[parts[1]] = specs;
                }
                else if (parts[0] == "M" && parts.Length == 4 && specsById.TryGetValue(parts[1], out AircraftSpecs shared))
                {
                    result[(parts[2], parts[3])] = shared;
                }
            }
            return result;
        }

        /// <summary>True when at least one value was measured (not just taken from the reference data or inferred).</summary>
        static bool IsMeasured(AircraftSpecs specs) => specs.Sources.Values.Any(s => s is SpecSource.Config or SpecSource.SimConnect);

        static string SpecLine(int id, AircraftSpecs s) => string.Join(Separator,
            "S", id.ToString(CultureInfo.InvariantCulture),
            s.Rotor.HasValue ? (s.Rotor.Value ? "1" : "0") : "",
            s.Wing == WingConfig.Unknown ? "" : s.Wing.ToString(),
            Number(s.SpanM), Number(s.LengthM), Number(s.MtowKg),
            s.EngineCount?.ToString(CultureInfo.InvariantCulture) ?? "",
            s.Engine == EngineKind.Unknown ? "" : s.Engine.ToString(),
            Number(s.CruiseKt),
            s.Gear == GearKind.Unknown ? "" : s.Gear.ToString(),
            s.Manufacturer);

        static string Number(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "";

        static AircraftSpecs ParseSpecs(string[] p)
        {
            AircraftSpecs specs = new();

            void Mark(string feature) => specs.Set(feature, SpecSource.Config);

            if (p[2] is "0" or "1") { specs.Rotor = p[2] == "1"; Mark("Rotor"); }
            if (Enum.TryParse(p[3], out WingConfig wing) && wing != WingConfig.Unknown) { specs.Wing = wing; Mark("Wing"); }
            if (TryNumber(p[4], out double span)) { specs.SpanM = span; Mark("Span"); }
            if (TryNumber(p[5], out double length)) { specs.LengthM = length; Mark("Length"); }
            if (TryNumber(p[6], out double mtow)) { specs.MtowKg = mtow; Mark("Mtow"); }
            if (int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int engines)) { specs.EngineCount = engines; Mark("Engines"); }
            if (Enum.TryParse(p[8], out EngineKind engine) && engine != EngineKind.Unknown) { specs.Engine = engine; Mark("EngineType"); }
            if (TryNumber(p[9], out double cruise)) { specs.CruiseKt = cruise; Mark("Cruise"); }
            if (Enum.TryParse(p[10], out GearKind gear) && gear != GearKind.Unknown) { specs.Gear = gear; Mark("Gear"); }
            if (p[11].Length > 0) { specs.Manufacturer = p[11]; Mark("Manufacturer"); }

            return specs.KnownFeatureCount > 0 ? specs : null;
        }

        static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
