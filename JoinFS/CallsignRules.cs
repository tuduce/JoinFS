using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JoinFS
{
    public enum CallsignKind { Unknown, Airline, Registration, Named }

    /// <summary>The callsign after IATA normalization, and why (for the monitor log).</summary>
    public readonly record struct IataResolution(string Callsign, string Reason);

    /// <summary>
    /// The one shared rule set for callsign and airline designator handling. Pure: no SimConnect and no sim
    /// state, so every build compiles it and the sim, the send path and the UI all call the same code.
    /// </summary>
    public static partial class CallsignRules
    {
        const int MaxCallsignLength = 7;

        static readonly HashSet<string> stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "air", "airline", "airlines", "airways", "airway", "aviation", "international", "service", "services",
            "express", "flight", "the", "of", "and", "de", "la", "le", "del", "inc", "ltd", "ag", "gmbh", "sa", "co",
            "group", "company", "corporation", "holding", "operations", "charter",
        };

        static readonly HashSet<string> cargoWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "cargo", "carga", "freight", "fracht",
        };

        /// <summary>ICAO item 7: three-letter designator, one to four digits, at most one suffix letter.</summary>
        [GeneratedRegex(@"^([A-Z]{3})(\d{1,4})([A-Z]?)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex AirlineShape();

        /// <summary>IATA designators may contain a digit (8L, 6V), so the prefix is two alphanumerics.</summary>
        [GeneratedRegex(@"^([A-Z0-9]{2})(\d{1,4})([A-Z]?)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex IataShape();

        /// <summary>National registration forms: D-EJOE, G-ABCD, OE-XYZ, N12345, N1234AB.</summary>
        [GeneratedRegex(@"^(?:[A-Z]{1,2}-[A-Z0-9]{2,5}|N\d[0-9A-Z]{1,5})$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex RegistrationShape();

        /// <summary>A flight number that already is a complete airline callsign (no airline to prepend).</summary>
        [GeneratedRegex(@"^[A-Z]{3}\d{1,4}[A-Z]?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex CompleteCallsignShape();

        public static CallsignKind Classify(string callsign, AirlineDirectory directory)
        {
            string normalized = Normalize(callsign);
            if (normalized.Length == 0) return CallsignKind.Unknown;
            if (normalized.Contains(' ')) return CallsignKind.Named;
            if (RegistrationShape().IsMatch(normalized)) return CallsignKind.Registration;

            Match airline = AirlineShape().Match(normalized);
            if (airline.Success && normalized.Length <= MaxCallsignLength && directory.IsIcao(airline.Groups[1].Value))
            {
                return CallsignKind.Airline;
            }
            return CallsignKind.Unknown;
        }

        /// <summary>The ICAO airline designator a callsign stands for, or empty when it is not an airline callsign.</summary>
        public static string DeriveIcaoAirline(string callsign, AirlineDirectory directory)
        {
            string normalized = Normalize(callsign);
            if (Classify(normalized, directory) != CallsignKind.Airline) return "";
            return AirlineShape().Match(normalized).Groups[1].Value;
        }

        /// <summary>
        /// The callsign to broadcast. The flight number is combined with the ICAO airline unless it already is a
        /// complete callsign. Without an airline the tail number is used, and named or registration callsigns go out as they are.
        /// </summary>
        public static string Resolve(string icaoAirline, string flightNumber, string tailNumber)
        {
            if (!string.IsNullOrEmpty(icaoAirline) && !string.IsNullOrEmpty(flightNumber))
            {
                string number = flightNumber.Trim().ToUpperInvariant();
                if (flightNumber.StartsWith(icaoAirline, StringComparison.OrdinalIgnoreCase) || CompleteCallsignShape().IsMatch(number))
                {
                    return flightNumber;
                }
                return icaoAirline + flightNumber;
            }
            return tailNumber;
        }

        /// <summary>
        /// Maps an IATA-style flight number (LH235) to its ICAO form (DLH235). Only airliners are considered, and only
        /// when the designator is an IATA code in the list. Order: configured airline, then (ambiguous codes only) a
        /// livery word that only one candidate carries, then the non-cargo candidate, then the first in list order.
        /// </summary>
        public static IataResolution NormalizeIataPrefix(string callsign, string configuredAirline, bool airliner, string liveryName, AirlineDirectory directory)
        {
            string normalized = Normalize(callsign);
            if (!airliner || Classify(normalized, directory) != CallsignKind.Unknown || normalized.Length > MaxCallsignLength)
            {
                return new(callsign, "not an IATA-style airline flight");
            }

            Match iata = IataShape().Match(normalized);
            if (!iata.Success) return new(callsign, "not an IATA-style airline flight");

            IReadOnlyList<string> candidates = directory.IcaoForIata(iata.Groups[1].Value);
            if (candidates.Count == 0) return new(callsign, "unknown IATA code");

            string suffix = iata.Groups[2].Value + iata.Groups[3].Value;

            if (!string.IsNullOrEmpty(configuredAirline))
            {
                string configured = configuredAirline.Trim().ToUpperInvariant();
                if (candidates.Contains(configured)) return new(configured + suffix, "configured airline");
            }

            if (candidates.Count == 1) return new(callsign, "unambiguous IATA code without configured airline");

            string byLivery = WinnerByLivery(candidates, liveryName, directory);
            if (byLivery != null) return new(byLivery + suffix, "livery");

            List<string> nonCargo = candidates.Where(icao => !IsCargo(directory.NameOf(icao))).ToList();
            if (nonCargo.Count == 1) return new(nonCargo[0] + suffix, "non-cargo");
            return new((nonCargo.Count > 1 ? nonCargo[0] : candidates[0]) + suffix, "first in list");
        }

        /// <summary>
        /// A candidate wins on the livery only when a word of its name appears in no other candidate's name
        /// and that word appears in the livery. Exactly one such candidate must exist, otherwise the livery decides nothing.
        /// </summary>
        static string WinnerByLivery(IReadOnlyList<string> candidates, string liveryName, AirlineDirectory directory)
        {
            if (string.IsNullOrWhiteSpace(liveryName)) return null;

            HashSet<string> liveryWords = Words(liveryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wordsByCandidate = candidates.ToDictionary(icao => icao, icao => Words(directory.NameOf(icao)).ToHashSet(StringComparer.OrdinalIgnoreCase));

            var hits = candidates.Where(icao =>
                wordsByCandidate[icao].Any(word =>
                    liveryWords.Contains(word) &&
                    candidates.Where(other => other != icao).All(other => !wordsByCandidate[other].Contains(word))))
                .ToList();

            return hits.Count == 1 ? hits[0] : null;
        }

        static bool IsCargo(string airlineName) => Words(airlineName).Any(cargoWords.Contains);

        /// <summary>Words of 3+ letters, lowercased, without the generic airline words.</summary>
        static IEnumerable<string> Words(string text) =>
            Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+")
                .Where(word => word.Length >= 3 && !stopwords.Contains(word));

        static string Normalize(string callsign) => (callsign ?? "").Trim().ToUpperInvariant();
    }
}
