using System;
using System.Collections.Generic;
using System.IO;

namespace JoinFS
{
    /// <summary>
    /// ICAO_Airlines.dat: column 1 ICAO designator, column 2 optional IATA designator, column 3 name.
    /// The one airline reference list; everything that needs airline validity or an IATA lookup goes through here.
    /// </summary>
    public sealed class AirlineDirectory
    {
        /// <summary>One valid row of the list.</summary>
        public sealed record Entry(string Icao, string Iata, string Name);

        readonly List<Entry> entries = [];
        readonly Dictionary<string, string> namesByIcao = new(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> icaoByIata = new(StringComparer.Ordinal);

        public static AirlineDirectory Bundled { get; } = FromLines(ReadBundledLines());

        public static AirlineDirectory FromLines(IEnumerable<string> lines)
        {
            var directory = new AirlineDirectory();
            foreach (string line in lines)
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;

                string icao = parts[0].Trim().ToUpperInvariant();
                string iata = parts[1].Trim().ToUpperInvariant();
                string name = parts[2].Trim();
                if (icao.Length != 3 || name.Length == 0) continue;

                directory.entries.Add(new Entry(icao, iata, name));
                directory.namesByIcao.TryAdd(icao, name);
                if (iata.Length > 0)
                {
                    if (!directory.icaoByIata.TryGetValue(iata, out var icaoCodes))
                    {
                        icaoCodes = [];
                        directory.icaoByIata[iata] = icaoCodes;
                    }
                    if (!icaoCodes.Contains(icao)) icaoCodes.Add(icao);
                }
            }
            return directory;
        }

        /// <summary>Every valid row, in file order (an airline can have several rows).</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>ICAO designator -> name, the first row of a designator wins; in file order.</summary>
        public IReadOnlyDictionary<string, string> Names => namesByIcao;

        public bool IsIcao(string designator) => namesByIcao.ContainsKey(designator);

        public string NameOf(string icao) => namesByIcao.TryGetValue(icao, out var name) ? name : "";

        /// <summary>ICAO designators that carry this IATA code, in list order. Empty when the code is unknown.</summary>
        public IReadOnlyList<string> IcaoForIata(string iata) =>
            icaoByIata.TryGetValue(iata, out var icaoCodes) ? icaoCodes : [];

        static List<string> ReadBundledLines()
        {
            var lines = new List<string>();
            using var stream = new MemoryStream(Properties.Resources_XPLANE.ICAO_Airlines);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = reader.ReadLine()) != null) lines.Add(line);
            return lines;
        }
    }
}
