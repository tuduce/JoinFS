using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JoinFS.Matching
{
    /// <summary>How an airline value was settled.</summary>
    public sealed class AirlineResolution
    {
        public string Icao = "";
        /// <summary>True when the given value was replaced (it was not a valid ICAO airline code as given)</summary>
        public bool Changed;
        /// <summary>True only for weak inferences (an airline name found in the title, a partial name word): their points are discounted.
        /// An exact code, unique IATA code or exact name/callsign is a lookup, not a guess.</summary>
        public bool Guessed;
        public string Note = "";
    }

    /// <summary>
    /// Resolves airline values that were put in the wrong field: atc_airline ("FDX") and icao_airline ("FEDEX", a callsign or name
    /// instead of the 3-letter code) are often mixed up, and the operator is often only in the title ("FedEx N234234").
    /// Source: data/ICAO_Airlines.dat (ICAO code, IATA code, name). Only unambiguous matches are accepted.
    /// </summary>
    public sealed class AirlineResolver
    {
        static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "air", "airline", "airlines", "airways", "airway", "aviation", "express", "international", "intl", "cargo", "lines",
            "line", "ltd", "limited", "inc", "co", "corp", "company", "the", "of", "and", "services", "service", "group", "transport"
        };

        static readonly char[] Separators = [' ', '-', '_', '.', ',', '/', '&', '\'', '(', ')', '|'];

        readonly HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, HashSet<string>> byIata = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>normalized name (with and without generic words) -> ICAO codes</summary>
        readonly Dictionary<string, HashSet<string>> byName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>every airline with its full name, for the full-text fallback</summary>
        readonly List<(string icao, string name)> names = [];

        readonly Dictionary<string, string> nameByCode = new(StringComparer.OrdinalIgnoreCase);

        public static AirlineResolver Empty { get; } = new();

        public static AirlineResolver FromFile(string path) => FromLines(File.ReadAllLines(path));

        public static AirlineResolver FromLines(IEnumerable<string> lines)
        {
            AirlineResolver resolver = new();
            foreach (var line in lines)
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 3 || parts[0].Trim().Length == 0) continue;
                string icao = parts[0].Trim();
                resolver.codes.Add(icao);
                resolver.names.Add((icao, parts[2].Trim()));
                resolver.nameByCode.TryAdd(icao, parts[2].Trim());
                AddTo(resolver.byIata, parts[1].Trim(), icao);
                AddTo(resolver.byName, Normalize(parts[2], dropNoise: false), icao);
                string core = Normalize(parts[2], dropNoise: true);
                if (core.Length >= 4) AddTo(resolver.byName, core, icao);
            }
            return resolver;
        }

        static void AddTo(Dictionary<string, HashSet<string>> map, string key, string icao)
        {
            if (key.Length == 0) return;
            if (!map.TryGetValue(key, out var set)) map[key] = set = new(StringComparer.OrdinalIgnoreCase);
            set.Add(icao);
        }

        /// <summary>The airline's name for an ICAO code ("SAA" -> "South African Airways"), or "" when unknown.</summary>
        public string NameOf(string icaoCode) => icaoCode != null && nameByCode.TryGetValue(icaoCode.Trim(), out var name) ? name : "";

        public bool IsKnownCode(string value) => value.Length == 3 && codes.Contains(value);

        /// <summary>Lower-case letters/digits of the name; optionally without generic words ("FedEx Express" -> "fedex").</summary>
        static string Normalize(string text, bool dropNoise)
        {
            var words = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (dropNoise)
            {
                var kept = words.Where(w => !NoiseWords.Contains(w)).ToArray();
                if (kept.Length > 0) words = kept;
            }
            return string.Concat(words.Select(TextTokens.AlnumOnly)).ToLowerInvariant();
        }

        static string Single(Dictionary<string, HashSet<string>> map, string key) =>
            key.Length > 0 && map.TryGetValue(key, out var set) && set.Count == 1 ? set.First() : "";

        /// <summary>
        /// Settle the airline: a valid ICAO code stays; otherwise try the given value and atc_airline as ICAO code, IATA code and
        /// (normalized) name/callsign, then the operator name inside title and livery. Empty Icao when nothing is certain.
        /// </summary>
        public AirlineResolution Resolve(string icaoAirline, string atcAirline, params string[] freeText)
        {
            icaoAirline = (icaoAirline ?? "").Trim();
            atcAirline = (atcAirline ?? "").Trim();
            if (IsKnownCode(icaoAirline)) return new AirlineResolution { Icao = icaoAirline.ToUpperInvariant() };

            foreach (var (value, label) in new[] { (icaoAirline, "ICAO airline"), (atcAirline, "atc_airline") })
            {
                if (value.Length == 0) continue;
                if (IsKnownCode(value)) return Looked(value.ToUpperInvariant(), $"{label} '{value}' is an ICAO airline code");
                string viaIata = Single(byIata, value);
                if (viaIata.Length > 0) return Looked(viaIata, $"{label} '{value}' is the IATA code of {viaIata}");
                string viaName = Single(byName, Normalize(value, true));
                if (viaName.Length == 0) viaName = Single(byName, Normalize(value, false));
                if (viaName.Length > 0) return Looked(viaName, $"{label} '{value}' is the name/callsign of {viaName}");
                string viaText = SingleInFullText(value);
                if (viaText.Length > 0) return Guessed(viaText, $"{label} '{value}' appears in the name of exactly one airline: {viaText}");
            }

            string fromText = FindNameInText(freeText, out string matchedName);
            if (fromText.Length > 0) return Guessed(fromText, $"airline name '{matchedName}' found in the title/livery -> {fromText}");
            return new AirlineResolution { Icao = icaoAirline };
        }

        /// <summary>Full-text search of the airline names for the value as a whole word; used only when exactly one airline contains it.</summary>
        string SingleInFullText(string value)
        {
            string needle = value.Trim();
            if (needle.Length < 4) return "";
            HashSet<string> hits = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (icao, name) in names)
            {
                if (TextTokens.ContainsToken(name, needle)) hits.Add(icao);
                if (hits.Count > 1) return "";
            }
            return hits.Count == 1 ? hits.First() : "";
        }

        static AirlineResolution Looked(string icao, string note) => new() { Icao = icao, Changed = true, Note = note };

        static AirlineResolution Guessed(string icao, string note) => new() { Icao = icao, Changed = true, Guessed = true, Note = note };

        /// <summary>The longest unambiguous airline name (min. 4 letters) found as whole words in the texts.</summary>
        string FindNameInText(string[] texts, out string matched)
        {
            matched = "";
            string best = "";
            int bestLength = 0;
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                string[] words = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                for (int start = 0; start < words.Length; start++)
                {
                    string joined = "";
                    for (int len = 1; len <= 3 && start + len <= words.Length; len++)
                    {
                        joined += TextTokens.AlnumOnly(words[start + len - 1]).ToLowerInvariant();
                        if (joined.Length < 4 || joined.Length <= bestLength || NoiseWords.Contains(joined)) continue;
                        string code = Single(byName, joined);
                        if (code.Length == 0) continue;
                        best = code;
                        bestLength = joined.Length;
                        matched = string.Join(" ", words.Skip(start).Take(len));
                    }
                }
            }
            return best;
        }
    }
}
