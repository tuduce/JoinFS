using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// The built-in data with the user file aircraft-specs.user.json (same format) merged over it. A type designator found in both takes
        /// the user row only. Aliases stay unique and official designators still rule: a user alias that is an official designator, another
        /// row's designator or already used by an earlier user row is dropped; a user alias beats the same built-in alias on another row.
        /// Nothing here is fatal: problems are added to <paramref name="problems"/> and the rest of the file still applies.
        /// </summary>
        public static ReferenceSpecs WithUserOverrides(ReferenceSpecs builtIn, string userJson, Func<string, bool> isOfficialDesignator, ICollection<string> problems)
        {
            List<Entry> userEntries = ReadUserEntries(userJson, problems);
            if (userEntries.Count == 0) return builtIn;

            HashSet<string> userDesignators = new(userEntries.Select(e => e.Icao), StringComparer.OrdinalIgnoreCase);
            HashSet<string> designators = new(builtIn.entries.Select(e => e.Icao).Concat(userDesignators), StringComparer.OrdinalIgnoreCase);

            HashSet<string> userAliases = new(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in userEntries)
            {
                entry.Aliases = ValidAliases(entry, designators, userAliases, isOfficialDesignator, problems);
            }

            ReferenceSpecs merged = new();
            foreach (var entry in builtIn.entries)
            {
                if (userDesignators.Contains(entry.Icao)) continue;   // the user's row replaces it completely
                merged.Add(entry.Aliases.Any(userAliases.Contains) ? WithAliases(entry, entry.Aliases.Where(a => !userAliases.Contains(a)).ToList()) : entry);
            }
            foreach (var entry in userEntries) merged.Add(entry);
            return merged;
        }

        /// <summary>Like <see cref="WithUserOverrides"/> from a file; a missing file changes nothing, an unreadable one is reported.</summary>
        public static ReferenceSpecs WithUserOverridesFromFile(ReferenceSpecs builtIn, string path, Func<string, bool> isOfficialDesignator, ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return builtIn;
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                problems.Add($"cannot read {Path.GetFileName(path)}: {ex.Message}");
                return builtIn;
            }
            return WithUserOverrides(builtIn, json, isOfficialDesignator, problems);
        }

        static List<Entry> ReadUserEntries(string userJson, ICollection<string> problems)
        {
            List<Entry> entries = [];
            try
            {
                using JsonDocument doc = JsonDocument.Parse(userJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("aircraft", out var rows) || rows.ValueKind != JsonValueKind.Array)
                {
                    problems.Add("no aircraft list found");
                    return entries;
                }
                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
                int index = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    index++;
                    try
                    {
                        Entry entry = ReadEntry(row);
                        if (entry.Icao.Length == 0) throw new FormatException("empty icao");
                        if (!seen.Add(entry.Icao)) problems.Add($"{entry.Icao}: designator appears more than once, the first row is used");
                        else entries.Add(entry);
                    }
                    catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
                    {
                        problems.Add($"row {index} skipped: {ex.Message}");
                    }
                }
            }
            catch (JsonException ex)
            {
                problems.Add("not valid JSON: " + ex.Message);
            }
            return entries;
        }

        static List<string> ValidAliases(Entry entry, HashSet<string> designators, HashSet<string> alreadyUsed, Func<string, bool> isOfficialDesignator, ICollection<string> problems)
        {
            List<string> valid = [];
            foreach (var alias in entry.Aliases)
            {
                if (isOfficialDesignator(alias)) problems.Add($"{entry.Icao}: alias {alias} is an official ICAO designator and cannot be an alias");
                else if (designators.Contains(alias)) problems.Add($"{entry.Icao}: alias {alias} is the type designator of a row");
                else if (!alreadyUsed.Add(alias)) problems.Add($"{entry.Icao}: alias {alias} is already used by another row of the file");
                else valid.Add(alias);
            }
            return valid;
        }

        static Entry WithAliases(Entry entry, List<string> aliases) => new()
        {
            Icao = entry.Icao,
            NonIcao = entry.NonIcao,
            Name = entry.Name,
            Manufacturer = entry.Manufacturer,
            Aliases = aliases,
            TitleHints = entry.TitleHints,
            Specs = entry.Specs
        };

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
