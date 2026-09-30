using System;
using System.Globalization;

namespace JoinFS
{
    /// <summary>
    /// ATC ground-station helpers (callsign, frequency formatting). Pure and stateless - not tied to
    /// a simulator connection, unlike everything on Sim - used by Euroscope, Whazzup, the session and
    /// the settings UI.
    /// </summary>
    public static class Atc
    {
        /// <summary>
        /// Convert frequency from an int to string
        /// </summary>
        /// <param name="frequency">Frequency</param>
        /// <returns>Frequency</returns>
        public static string FrequencyIntToString(int frequency)
        {
            // limit frequency
            frequency = Math.Min(60000, Math.Max(0, frequency));
            // convert to string
            return "1" + (frequency / 1000).ToString() + "." + (frequency % 1000).ToString("D3");
        }

        /// <summary>
        /// Convert frequency from a string to an int
        /// </summary>
        /// <param name="frequency">Frequency</param>
        /// <returns>Frequency</returns>
        public static int FrequencyStringToInt(string frequency)
        {
            // frequency result
            int result = 0;
            // split around dot
            string[] parts = frequency.Split('.');
            // check for first part
            if (parts.Length > 0)
            {
                // convert first part
                if (int.TryParse(parts[0][1..], NumberStyles.Number, CultureInfo.InvariantCulture, out int r0))
                {
                    // add to result
                    result += r0 * 1000;
                }
            }
            // check for second part
            if (parts.Length > 1)
            {
                // convert first part
                if (int.TryParse(parts[1].PadRight(3, '0'), NumberStyles.Number, CultureInfo.InvariantCulture, out int r1))
                {
                    // add to result
                    result += r1;
                }
            }
            // return
            return result;
        }

        /// <summary>
        /// Make callsign string from an airport and ATC level
        /// </summary>
        /// <param name="airport">Airport</param>
        /// <param name="level">Level</param>
        /// <returns>Callsign</returns>
        public static string MakeAtcCallsign(string airport, int level)
        {
            // callsign
            string callsign = airport;
            // check for airport
            if (callsign.Length > 0)
            {
                // add level
                switch (level)
                {
                    case 0: callsign += "_DEL"; break;
                    case 1: callsign += "_GND"; break;
                    case 2: callsign += "_TWR"; break;
                    case 3: callsign += "_APP"; break;
                    case 4: callsign += "_CTR"; break;
                }
            }
            // return result
            return callsign;
        }
    }
}
