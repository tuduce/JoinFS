using System.Globalization;
using System.Text;
using JoinFS.Fsd;

namespace JoinFS
{
    /// <summary>
    /// A JoinFS flight plan as a <c>$FP</c> (docs/ATC/atc-fsd-plan.md stage 4). A pure function: what JoinFS does not
    /// have (departure times, time en route, fuel) is empty or zero.
    /// </summary>
    public static class AtcFlightPlans
    {
        /// <summary>The <c>$FP</c> of <paramref name="aircraft"/> (its FSD callsign), addressed to <paramref name="to"/>.</summary>
        public static FsdFlightPlan ToFsd(string aircraft, string to, AtcFlightPlanData plan)
        {
            plan ??= AtcFlightPlanData.Empty;
            return new FsdFlightPlan
            {
                From = aircraft,
                To = to,
                Rules = Rules(plan.Rules),
                Equipment = Field(plan.IcaoType),
                TrueAirspeedKnots = Knots(plan.Speed),
                Departure = Field(plan.Departure).ToUpperInvariant(),
                CruiseAltitude = Field(plan.Altitude).ToUpperInvariant(),
                Destination = Field(plan.Destination).ToUpperInvariant(),
                Alternate = Field(plan.Alternate).ToUpperInvariant(),
                Remarks = Field(plan.Remarks),
                // the route is the last field of the line and keeps its colons
                Route = Clean(plan.Route),
            };
        }

        /// <summary><c>I</c>, <c>V</c>, <c>D</c> or <c>S</c> from "IFR", "VFR", "SVFR", "DVFR" or the letter; anything else is VFR.</summary>
        public static char Rules(string text)
        {
            text = (text ?? "").Trim().ToUpperInvariant();
            if (text.Length == 0) return 'V';
            if (text.StartsWith("SV")) return 'S';
            if (text.StartsWith("DV")) return 'D';
            return text[0] == 'I' ? 'I' : 'V';
        }

        /// <summary>
        /// The filed speed in knots: <c>N0450</c> or <c>450</c> is 450; <c>K0830</c> (km/h) is converted; a Mach number or
        /// text that is not a speed is 0.
        /// </summary>
        public static int Knots(string text)
        {
            text = (text ?? "").Trim().ToUpperInvariant();
            if (text.Length == 0) return 0;
            bool kmh = text[0] == 'K';
            if (text[0] == 'N' || kmh) text = text.Substring(1);
            if (text.Length == 0 || text.Length > 5) return 0;
            foreach (char c in text)
                if (c < '0' || c > '9') return 0;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) return 0;
            return kmh ? (int)System.Math.Round(value / 1.852) : value;
        }

        /// <summary>A text field: no colon (it would split the line) and no control character.</summary>
        static string Field(string text) => Clean(text).Replace(':', ' ').Trim();

        static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(FsdLine.IsControl(c) ? ' ' : c);
            return sb.ToString().Trim();
        }
    }
}
