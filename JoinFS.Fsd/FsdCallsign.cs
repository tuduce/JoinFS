using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JoinFS.Fsd
{
    /// <summary>One aircraft as the callsign rule sees it: who owns it, which object it is, and the callsign of its flight plan.</summary>
    public readonly struct FsdAircraft
    {
        public FsdAircraft(ulong owner, ulong objectId, string callsign)
        {
            Owner = owner;
            ObjectId = objectId;
            Callsign = callsign;
        }

        /// <summary>The owner node's 8 wire bytes read as a little-endian number. Any total order will do, as long as every node uses the same.</summary>
        public ulong Owner { get; }
        public ulong ObjectId { get; }
        /// <summary>The flight plan's callsign as the pilot typed it; may be null or empty.</summary>
        public string Callsign { get; }
    }

    /// <summary>The callsign rules (docs/ATC/atc-fsd-plan.md §4.6).</summary>
    public static class FsdCallsign
    {
        public const int MinStationLength = 3;
        public const int MaxStationLength = 12;
        public const int MaxAircraftLength = 10;

        /// <summary>
        /// Is this a callsign a controller may log in with: 3 to 12 characters of <c>A-Z 0-9 _</c>, with any number of
        /// <c>_</c> (<c>EDDM_TWR</c>, <c>EDDM_N_TWR</c>, <c>EDMM_ALB_CTR</c>). Its suffix is not looked at.
        /// </summary>
        public static bool IsValidStation(string callsign)
        {
            if (callsign == null || callsign.Length < MinStationLength || callsign.Length > MaxStationLength) return false;
            foreach (char c in callsign)
                if (!(c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_')) return false;
            return true;
        }

        /// <summary>
        /// The callsign each aircraft shows to the controllers, in the order of <paramref name="aircraft"/>. Every
        /// node that sees the same aircraft gets the same names, whatever order it lists them in:
        /// 1. the flight plan's callsign in upper case, only <c>A-Z 0-9 -</c>, cut to 10 characters;
        /// 2. if that is empty, <c>JFS</c> and four hex digits of a hash of (owner, object id);
        /// 3. if several aircraft have the same name, the one with the lowest (owner, object id) keeps it and each
        ///    other gets the name cut to 8 characters and two hex digits of that hash.
        /// A name that step 3 gives and that is already in use gets the next hash (the rule is applied in the order
        /// of (owner, object id), so it stays the same for every node).
        /// </summary>
        public static string[] Aircraft(IReadOnlyList<FsdAircraft> aircraft)
        {
            int n = aircraft.Count;
            var result = new string[n];
            var order = new int[n];
            var names = new string[n];
            var taken = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                names[i] = Normalise(aircraft[i].Callsign);
                if (names[i].Length == 0) names[i] = "JFS" + Hash(aircraft[i], 0).ToString("X4", CultureInfo.InvariantCulture);
                taken.Add(names[i]);
            }
            Array.Sort(order, (a, b) =>
            {
                int c = aircraft[a].Owner.CompareTo(aircraft[b].Owner);
                if (c == 0) c = aircraft[a].ObjectId.CompareTo(aircraft[b].ObjectId);
                return c == 0 ? a.CompareTo(b) : c;
            });

            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (int i in order)
            {
                if (kept.Add(names[i]))
                {
                    result[i] = names[i];
                    continue;
                }
                string stem = names[i].Length > 8 ? names[i].Substring(0, 8) : names[i];
                int salt = 0;
                string candidate;
                do
                {
                    candidate = stem + (Hash(aircraft[i], salt) & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
                    salt++;
                }
                while (taken.Contains(candidate));
                taken.Add(candidate);
                result[i] = candidate;
            }
            return result;
        }

        static string Normalise(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return "";
            var sb = new StringBuilder(MaxAircraftLength);
            foreach (char c in callsign)
            {
                char u = c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
                if (u >= 'A' && u <= 'Z' || u >= '0' && u <= '9' || u == '-')
                {
                    sb.Append(u);
                    if (sb.Length == MaxAircraftLength) break;
                }
            }
            return sb.ToString();
        }

        /// <summary>FNV-1a over the owner, the object id and the salt: stable on every platform.</summary>
        static uint Hash(in FsdAircraft a, int salt)
        {
            uint h = 2166136261u;
            for (int i = 0; i < 8; i++) h = (h ^ (byte)(a.Owner >> 8 * i)) * 16777619u;
            for (int i = 0; i < 8; i++) h = (h ^ (byte)(a.ObjectId >> 8 * i)) * 16777619u;
            h = (h ^ (byte)salt) * 16777619u;
            return (h ^ h >> 16) & 0xFFFF;
        }
    }
}
