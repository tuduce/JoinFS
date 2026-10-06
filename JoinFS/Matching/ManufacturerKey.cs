using System;
using System.Collections.Generic;
using System.Linq;

namespace JoinFS.Matching
{
    /// <summary>
    /// Turns the many spellings of an aircraft manufacturer ("ROBINSON HELICOPTER", "Robinson", "Cessna Aircraft Company") into
    /// one comparable key, so "same manufacturer" can be judged from Doc8643 rows and cfg icao_manufacturer values alike.
    /// </summary>
    public static class ManufacturerKey
    {
        /// <summary>Words that describe the kind of company, not which one it is.</summary>
        static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "HELICOPTER", "HELICOPTERS", "AIRCRAFT", "AIRPLANE", "AIRPLANES", "AIRCRAFTS", "CORP", "CORPORATION", "COMPANY", "CO", "INC", "LTD", "LLC",
            "INDUSTRIES", "INDUSTRIE", "AVIATION", "AEROSPACE", "AEROSPATIALE", "THE", "GMBH", "SA", "SPA", "AG"
        };

        /// <summary>Names of the same design lineage or renamed companies</summary>
        static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["EUROCOPTER"] = "AIRBUS",
            ["BEECHCRAFT"] = "BEECH",
            ["HAWKER"] = "BEECH",
        };

        public static string From(string manufacturer)
        {
            if (string.IsNullOrWhiteSpace(manufacturer)) return "";

            var words = manufacturer.Split([' ', '-', '/', ',', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToUpperInvariant())
                .Where(w => !NoiseWords.Contains(w))
                .ToList();
            if (words.Count == 0) return "";

            string key = words[0] == "MCDONNELL" && words.Count > 1 ? words[0] + words[1] : words[0];
            return Synonyms.TryGetValue(key, out var mapped) ? mapped : key;
        }
    }
}
