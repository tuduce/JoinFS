using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace JoinFS.Matching
{
    /// <summary>
    /// Public reference data per ICAO type (data/aircraft-specs.json): the complement for models whose own cfg files
    /// carry no measurements (e.g. streamed default aircraft) and the source of the remote aircraft's expected size/speed.
    /// Also resolves wrong-but-common type tags ("500E" for the MD 500, alias list) and infers a type from a title.
    /// </summary>
    public sealed class ReferenceSpecs
    {
        public sealed class Entry
        {
            public string Icao = "";
            /// <summary>True for aircraft without an ICAO designator (Wright Flyer, Do X ...): the key is a pseudo code ("X-...") and the type is found by title only</summary>
            public bool NonIcao;
            public string Name = "";
            /// <summary>Curated manufacturer (display form, e.g. "Cessna"); compare through <see cref="ManufacturerKey"/></summary>
            public string Manufacturer = "";
            public List<string> Aliases = [];
            public List<string> TitleHints = [];
            public AircraftSpecs Specs = new();
        }

        readonly List<Entry> entries = [];
        readonly Dictionary<string, Entry> byIcao = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Entry> byAlias = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<Entry> Entries => entries;

        public static ReferenceSpecs FromFile(string path) => FromJson(File.ReadAllText(path));

        public static ReferenceSpecs FromJson(string json) => FromDocument(JsonDocument.Parse(json));

        /// <summary>From the UTF-8 bytes of the file (the embedded resource); a leading byte order mark is accepted.</summary>
        public static ReferenceSpecs FromBytes(byte[] utf8Json) => FromDocument(JsonDocument.Parse(utf8Json));

        static ReferenceSpecs FromDocument(JsonDocument doc)
        {
            using (doc)
            {
                ReferenceSpecs reference = new();
                foreach (var element in doc.RootElement.GetProperty("aircraft").EnumerateArray())
                {
                    reference.Add(ReadEntry(element));
                }
                return reference;
            }
        }

        void Add(Entry entry)
        {
            entries.Add(entry);
            byIcao.TryAdd(entry.Icao, entry);
            foreach (var alias in entry.Aliases) byAlias.TryAdd(alias, entry);
        }

        static Entry ReadEntry(JsonElement e)
        {
            Entry entry = new()
            {
                Icao = e.GetProperty("icao").GetString() ?? "",
                NonIcao = e.TryGetProperty("nonIcao", out var nonIcao) && nonIcao.ValueKind == JsonValueKind.True,
                Name = Str(e, "name"),
                Manufacturer = Str(e, "manufacturer"),
                Aliases = StrList(e, "aliases"),
                TitleHints = StrList(e, "titleHints"),
            };

            AircraftSpecs s = entry.Specs;
            s.Name = entry.Name;
            s.Rotor = e.TryGetProperty("rotor", out var rotor) && rotor.ValueKind == JsonValueKind.True;
            s.Set("Rotor", SpecSource.Reference);
            s.Wing = ParseWing(Str(e, "wing"));
            if (s.Wing != WingConfig.Unknown) s.Set("Wing", SpecSource.Reference);
            s.SpanM = Num(e, "spanM"); Mark(s, "Span", s.SpanM.HasValue);
            s.LengthM = Num(e, "lengthM"); Mark(s, "Length", s.LengthM.HasValue);
            s.MtowKg = Num(e, "mtowKg"); Mark(s, "Mtow", s.MtowKg.HasValue);
            double? engines = Num(e, "engines");
            s.EngineCount = engines.HasValue ? (int)engines.Value : null; Mark(s, "Engines", s.EngineCount.HasValue);
            s.Engine = ParseEngine(Str(e, "engineType")); Mark(s, "EngineType", s.Engine != EngineKind.Unknown);
            s.CruiseKt = Num(e, "cruiseKt"); Mark(s, "Cruise", s.CruiseKt.HasValue);
            s.Gear = ParseGear(Str(e, "gear")); Mark(s, "Gear", s.Gear != GearKind.Unknown);
            return entry;
        }

        static void Mark(AircraftSpecs specs, string feature, bool known)
        {
            if (known) specs.Set(feature, SpecSource.Reference);
        }

        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static double? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        static List<string> StrList(JsonElement e, string name)
        {
            List<string> list = [];
            if (e.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
                }
            }
            return list;
        }

        static WingConfig ParseWing(string text) => text.ToLowerInvariant() switch
        {
            "high" => WingConfig.High,
            "mid" => WingConfig.Mid,
            "low" => WingConfig.Low,
            "biplane" => WingConfig.Biplane,
            "rotor" => WingConfig.Rotor,
            _ => WingConfig.Unknown
        };

        static GearKind ParseGear(string text) => text.ToLowerInvariant() switch
        {
            "tricycle" => GearKind.Tricycle,
            "taildragger" => GearKind.Taildragger,
            "skids" => GearKind.Skids,
            "floats" => GearKind.Floats,
            "skis" => GearKind.Skis,
            _ => GearKind.Unknown
        };

        static EngineKind ParseEngine(string text) => text.ToLowerInvariant() switch
        {
            "piston" => EngineKind.Piston,
            "turboprop" => EngineKind.Turboprop,
            "jet" => EngineKind.Jet,
            "turboshaft" => EngineKind.Turboshaft,
            "electric" => EngineKind.Electric,
            "none" => EngineKind.None,
            _ => EngineKind.Unknown
        };

        /// <summary>The ICAO type for a code that may be an alias ("500E" -> "H500"); unknown codes are returned unchanged.</summary>
        public string ResolveAlias(string code)
        {
            if (string.IsNullOrEmpty(code)) return code ?? "";
            if (byAlias.TryGetValue(code, out var entry)) return entry.Icao;
            if (byIcao.ContainsKey(code)) return code;

            // punctuation/spacing variants of a known type or alias ("JU-52" -> "JU52", "B 738" -> "B738")
            string compact = TextTokens.AlnumOnly(code);
            if (compact.Length > 0 && compact != code)
            {
                if (byIcao.TryGetValue(compact, out var byType)) return byType.Icao;
                if (byAlias.TryGetValue(compact, out var byCompactAlias)) return byCompactAlias.Icao;
            }
            return code;
        }

        /// <summary>The curated manufacturer of an ICAO type (or alias), or "" when the type is not in the data.</summary>
        public string ManufacturerOf(string icaoOrAlias)
        {
            if (string.IsNullOrEmpty(icaoOrAlias)) return "";
            return byIcao.TryGetValue(ResolveAlias(icaoOrAlias), out var entry) ? entry.Manufacturer : "";
        }

        /// <summary>A copy of the reference specs for an ICAO type (or alias), or null when the type is not in the data.</summary>
        public AircraftSpecs Find(string icaoOrAlias)
        {
            if (string.IsNullOrEmpty(icaoOrAlias)) return null;
            string icao = ResolveAlias(icaoOrAlias);
            return byIcao.TryGetValue(icao, out var entry) ? Clone(entry.Specs) : null;
        }

        /// <summary>
        /// Guess the ICAO type from free text (a title): the longest title hint / aircraft name / ICAO code / alias that appears
        /// as a standalone token wins. Returns null when nothing matches.
        /// </summary>
        public string InferIcaoFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string best = null;
            int bestLength = 0;
            foreach (var entry in entries)
            {
                foreach (var hint in Hints(entry))
                {
                    if (hint.Length > bestLength && TextTokens.ContainsToken(text, hint))
                    {
                        best = entry.Icao;
                        bestLength = hint.Length;
                    }
                }
            }
            return best;
        }

        static IEnumerable<string> Hints(Entry entry)
        {
            if (entry.Icao.Length >= 3) yield return entry.Icao;
            if (entry.Name.Length > 0) yield return entry.Name;
            foreach (var hint in entry.TitleHints) yield return hint;
            foreach (var alias in entry.Aliases)
            {
                if (alias.Length >= 4) yield return alias;
            }
        }

        static AircraftSpecs Clone(AircraftSpecs s) => new()
        {
            Rotor = s.Rotor, Wing = s.Wing, SpanM = s.SpanM, LengthM = s.LengthM, MtowKg = s.MtowKg,
            EngineCount = s.EngineCount, Engine = s.Engine, CruiseKt = s.CruiseKt, Gear = s.Gear, Manufacturer = s.Manufacturer, Name = s.Name,
            Sources = new Dictionary<string, SpecSource>(s.Sources)
        };
    }
}
