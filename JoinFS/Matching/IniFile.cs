using System;
using System.Collections.Generic;

namespace JoinFS.Matching
{
    /// <summary>
    /// Minimal, tolerant reader for the INI-style .cfg files simulators use (aircraft.cfg, flight_model.cfg, engines.cfg):
    /// "[SECTION]" headers, "key = value" lines, ';' or '//' comments, optional double quotes. Case-insensitive lookups.
    /// A malformed line is skipped, never thrown on - add-on cfg files are often sloppy.
    /// </summary>
    public sealed class IniFile
    {
        sealed class Section
        {
            public string Name;
            public readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
        }

        readonly List<Section> sections = [];

        public static IniFile Parse(string text)
        {
            IniFile ini = new();
            Section current = null;
            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//") || line.StartsWith('#')) continue;

                if (line.StartsWith('['))
                {
                    int end = line.IndexOf(']');
                    if (end > 0)
                    {
                        current = new Section { Name = line[1..end].Trim() };
                        ini.sections.Add(current);
                    }
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0 || current == null) continue;

                string key = line[..equals].Trim();
                string value = CleanValue(line[(equals + 1)..]);
                current.Values.TryAdd(key, value);
            }
            return ini;
        }

        static string CleanValue(string raw)
        {
            string value = raw.Trim();
            if (value.StartsWith('"'))
            {
                int close = value.IndexOf('"', 1);
                return close > 0 ? value[1..close] : value.Trim('"');
            }
            int comment = IndexOfComment(value);
            if (comment >= 0) value = value[..comment];
            return value.Trim();
        }

        static int IndexOfComment(string value)
        {
            int semicolon = value.IndexOf(';');
            int slashes = value.IndexOf("//", StringComparison.Ordinal);
            if (semicolon < 0) return slashes;
            if (slashes < 0) return semicolon;
            return Math.Min(semicolon, slashes);
        }

        /// <summary>The first value of key in the first section called <paramref name="section"/>, or "" when absent.</summary>
        public string Get(string section, string key)
        {
            foreach (var s in sections)
            {
                if (s.Name.Equals(section, StringComparison.OrdinalIgnoreCase) && s.Values.TryGetValue(key, out var value))
                {
                    return value;
                }
            }
            return "";
        }

        /// <summary>All values in the section whose key starts with the prefix (e.g. "point." for contact points), in file order.</summary>
        public List<string> GetAll(string section, string keyPrefix)
        {
            List<string> values = [];
            foreach (var s in sections)
            {
                if (!s.Name.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var pair in s.Values)
                {
                    if (pair.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)) values.Add(pair.Value);
                }
            }
            return values;
        }

        /// <summary>Names of all sections starting with the prefix, in file order (e.g. "FLTSIM." for FLTSIM.0, FLTSIM.1...).</summary>
        public List<string> SectionNames(string prefix)
        {
            List<string> names = [];
            foreach (var s in sections)
            {
                if (s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(s.Name);
            }
            return names;
        }
    }
}
