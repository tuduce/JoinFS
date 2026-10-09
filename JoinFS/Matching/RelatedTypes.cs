using System;
using System.Collections.Generic;
using System.IO;

namespace JoinFS.Matching
{
    /// <summary>
    /// Groups of ICAO types that look alike (data/related.dat, from XPMP2): A318 A319 A320 A321 A19N A20N A21N are one family.
    /// A model of a related type is a near-exact stand-in - the same airframe family, so its livery usually matters more than
    /// the exact variant.
    /// </summary>
    public sealed class RelatedTypes
    {
        readonly Dictionary<string, int> groupOf = new(StringComparer.OrdinalIgnoreCase);

        public static RelatedTypes Empty { get; } = new();

        public static RelatedTypes FromFile(string path) => FromLines(File.ReadAllLines(path));

        public static RelatedTypes FromLines(IEnumerable<string> lines)
        {
            RelatedTypes related = new();
            int group = 0;
            foreach (var raw in lines)
            {
                string line = raw;
                int comment = line.IndexOf(';');
                if (comment >= 0) line = line[..comment];
                string[] codes = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (codes.Length < 2) continue;
                group++;
                foreach (var code in codes) related.groupOf.TryAdd(code, group);
            }
            return related;
        }

        /// <summary>True when both types are different members of the same group.</summary>
        public bool AreRelated(string a, string b) =>
            a.Length > 0 && b.Length > 0 && !a.Equals(b, StringComparison.OrdinalIgnoreCase) &&
            groupOf.TryGetValue(a, out int ga) && groupOf.TryGetValue(b, out int gb) && ga == gb;
    }
}
