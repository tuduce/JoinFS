using System;
using System.Linq;

namespace JoinFS.Matching
{
    /// <summary>
    /// Reads the aircraft identity out of an X-Plane CSL line (xsb_aircraft.txt): <c>ICAO &lt;type&gt;</c>, <c>AIRLINE &lt;type&gt; &lt;airline&gt;</c>,
    /// <c>LIVERY &lt;type&gt; &lt;airline&gt; &lt;livery&gt;</c> (and <c>MATCHES &lt;type&gt;</c>). The ICAO type designator and the ICAO airline code are
    /// what the matcher compares; the livery part stays in the model's variation as before.
    /// </summary>
    public static class XsbEntry
    {
        /// <summary>The airline marker JoinFS writes into the CSL entries it generates itself: not an airline</summary>
        const string GeneratedMarker = "JFS";

        /// <param name="command">The upper-cased first word of the line</param>
        /// <param name="words">All words of the line (the command first)</param>
        /// <returns>The ICAO type designator and the ICAO airline code; "" for whatever the line does not say or says in an unusable form</returns>
        public static (string icaoType, string airline) Identity(string command, string[] words)
        {
            if (words.Length < 2) return ("", "");

            string icaoType = IsDesignator(words[1]) ? words[1].ToUpperInvariant() : "";
            string airline = "";
            if (command is "AIRLINE" or "LIVERY" && words.Length > 2 && IsAirlineCode(words[2])) airline = words[2].ToUpperInvariant();
            return (icaoType, airline);
        }

        /// <summary>An ICAO type designator: two to four letters or digits.</summary>
        static bool IsDesignator(string value) => value.Length is >= 2 and <= 4 && value.All(char.IsAsciiLetterOrDigit);

        /// <summary>An ICAO airline code: exactly three letters, and not JoinFS' own marker.</summary>
        static bool IsAirlineCode(string value) =>
            value.Length == 3 && value.All(char.IsAsciiLetter) && !value.Equals(GeneratedMarker, StringComparison.OrdinalIgnoreCase);
    }
}
