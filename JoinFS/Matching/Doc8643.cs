using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JoinFS.Matching
{
    /// <summary>
    /// ICAO Doc8643 reference data (the dataset JoinFS bundles as XPMP2_Doc8643.dat): icaoType -> classCode/WTC.
    /// Row format (tab separated): manufacturer, model name, ICAO type, class code, WTC.
    /// First row for a designator wins, like JoinFS' LoadDoc8643Index.
    /// </summary>
    public sealed class Doc8643
    {
        public sealed record Row(string Manufacturer, string ModelName, string IcaoType, string ClassCode, string Wtc);

        readonly Dictionary<string, Row> byIcao = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Row>> allByIcao = new(StringComparer.OrdinalIgnoreCase);
        readonly List<Row> rows = [];

        public IReadOnlyList<Row> Rows => rows;

        public static Doc8643 FromLines(IEnumerable<string> lines)
        {
            Doc8643 doc = new();
            foreach (var line in lines)
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 5) continue;
                string icaoType = parts[2];
                if (icaoType.Length == 0 || icaoType == "ZZZZ") continue;

                Row row = new(parts[0], parts[1], icaoType, parts[3], parts[4]);
                doc.rows.Add(row);
                doc.byIcao.TryAdd(icaoType, row);
                if (!doc.allByIcao.TryGetValue(icaoType, out var list))
                {
                    list = [];
                    doc.allByIcao.Add(icaoType, list);
                }
                list.Add(row);
            }
            return doc;
        }

        /// <summary>
        /// The manufacturer that appears in most Doc8643 rows of the designator. The first row is often a licence builder
        /// (the C172's first row is AVIONES COLOMBIA), the original maker has the most variants. Ties resolve alphabetically.
        /// "" when the designator is unknown.
        /// </summary>
        public string MajorityManufacturer(string icaoType)
        {
            if (icaoType == null || !allByIcao.TryGetValue(icaoType, out var list)) return "";
            return list.GroupBy(r => r.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .First().Key;
        }

        public static Doc8643 FromFile(string path) => FromLines(File.ReadAllLines(path));

        /// <summary>True when icaoType is a real, recognized Doc8643 designator.</summary>
        public bool IsRecognized(string icaoType) => icaoType.Length > 0 && byIcao.ContainsKey(icaoType);

        public bool TryGet(string icaoType, out Row row)
        {
            if (icaoType == null || icaoType.Length == 0)
            {
                row = null;
                return false;
            }
            return byIcao.TryGetValue(icaoType, out row);
        }

        /// <summary>The lookup shape JoinFS' Model.RefreshIcaoDerived expects.</summary>
        public (string classCode, string wtc)? Lookup(string icaoType)
        {
            return TryGet(icaoType, out var row) ? (row.ClassCode, row.Wtc) : null;
        }
    }
}
