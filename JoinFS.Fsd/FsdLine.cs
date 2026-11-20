using System;
using System.Collections.Generic;
using System.Globalization;

namespace JoinFS.Fsd
{
    /// <summary>The verbs (the packet prefix) of the FSD lines JoinFS reads and writes.</summary>
    public static class FsdVerb
    {
        public const string ServerId = "$DI";
        public const string ClientId = "$ID";
        public const string AddAtc = "#AA";
        public const string DeleteAtc = "#DA";
        public const string AddPilot = "#AP";
        public const string DeletePilot = "#DP";
        public const string PilotPosition = "@";
        public const string AtcPosition = "%";
        public const string Ping = "$PI";
        public const string Pong = "$PO";
        public const string ClientQuery = "$CQ";
        public const string ClientResponse = "$CR";
        public const string Handoff = "$HO";
        public const string HandoffAccept = "$HA";
        public const string FlightPlan = "$FP";
        public const string FlightPlanAmendment = "$AM";
        public const string ProController = "#PC";
        public const string TextMessage = "#TM";
        public const string Error = "$ER";
        public const string Kill = "$!!";
        public const string AuthChallenge = "$ZC";
        public const string AuthResponse = "$ZR";
    }

    /// <summary>
    /// One FSD line (without its line ending) as a verb and its fields. <c>:</c> separates the fields.
    /// The verb is the packet prefix: <c>@</c>, <c>%</c> and <c>^</c> alone, otherwise <c>$</c> or <c>#</c> and two
    /// characters. A line is read and written without loss: <see cref="ToString"/> of a parsed line is the
    /// text it was parsed from. The last field of <c>#TM</c>, <c>$FP</c>, <c>$AM</c> and <c>$ER</c> keeps its colons
    /// (the message, the route, the description).
    /// </summary>
    public sealed class FsdLine : IEquatable<FsdLine>
    {
        readonly string[] fields;

        public FsdLine(string verb, params string[] fields)
        {
            Verb = verb ?? throw new ArgumentNullException(nameof(verb));
            this.fields = fields ?? Array.Empty<string>();
        }

        public string Verb { get; }

        public IReadOnlyList<string> Fields => fields;

        /// <summary>The field at <paramref name="index"/>, or an empty string when the line has fewer.</summary>
        public string Field(int index) => (uint)index < (uint)fields.Length ? fields[index] : "";

        /// <summary>The sender's callsign. For <c>@</c> it is the second field, after the transponder mode.</summary>
        public string From => Field(Verb == FsdVerb.PilotPosition ? 1 : 0);

        /// <summary>The recipient, empty for the verbs that have none (positions, deletes).</summary>
        public string To => HasRecipient(Verb) ? Field(1) : "";

        /// <summary>Does this verb carry a recipient as its second field?</summary>
        public static bool HasRecipient(string verb)
        {
            switch (verb)
            {
                case "@":
                case "%":
                case "^":
                case "#SL":
                case "#ST":
                case FsdVerb.DeleteAtc:
                case FsdVerb.DeletePilot:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>Is <paramref name="c"/> a control character (never allowed inside a line)?</summary>
        public static bool IsControl(char c) => c < ' ' || c == '\u007F';

        /// <summary>Does the text contain a control character?</summary>
        public static bool HasControlCharacter(string text)
        {
            foreach (char c in text)
                if (IsControl(c)) return true;
            return false;
        }

        /// <summary>
        /// Parse a line. False for an empty line, a line with a control character (so also one that still has
        /// its line ending), or a first character that is no verb.
        /// </summary>
        public static bool TryParse(string text, out FsdLine line)
        {
            line = null;
            if (string.IsNullOrEmpty(text) || HasControlCharacter(text)) return false;

            int verbLength;
            switch (text[0])
            {
                case '@':
                case '%':
                case '^':
                    verbLength = 1;
                    break;
                case '$':
                case '#':
                    verbLength = 3;
                    break;
                default:
                    return false;
            }
            if (text.Length < verbLength) return false;

            string verb = text.Substring(0, verbLength);
            string rest = text.Substring(verbLength);
            string[] parts = rest.Split(new[] { ':' }, MaxFields(verb), StringSplitOptions.None);
            line = new FsdLine(verb, parts);
            return true;
        }

        /// <summary>How many fields a verb has at most; the last one then takes the rest of the line, colons included.</summary>
        static int MaxFields(string verb)
        {
            switch (verb)
            {
                case FsdVerb.TextMessage: return 3;
                case FsdVerb.Error: return 5;
                case FsdVerb.FlightPlan: return 17;
                case FsdVerb.FlightPlanAmendment: return 18;
                default: return int.MaxValue;
            }
        }

        /// <summary>The line's text, without a line ending.</summary>
        public override string ToString() => Verb + string.Join(":", fields);

        public bool Equals(FsdLine other) => other != null && ToString() == other.ToString();
        public override bool Equals(object obj) => Equals(obj as FsdLine);
        public override int GetHashCode() => ToString().GetHashCode();
    }

    /// <summary>Number and text helpers shared by the typed lines.</summary>
    static class FsdText
    {
        /// <summary>Latitude and longitude as FSD writes them: five decimals.</summary>
        public static string Degrees(double value) => value.ToString("F5", CultureInfo.InvariantCulture);

        public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static bool TryDegrees(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>An integer field; an empty one is 0.</summary>
        public static bool TryInt(string text, out int value)
        {
            if (text.Length == 0) { value = 0; return true; }
            return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>An unsigned 32-bit integer field (the packed pitch/bank/heading).</summary>
        public static bool TryUInt(string text, out uint value) =>
            uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        /// <summary>A single-character field; an empty one is NUL.</summary>
        public static bool TryChar(string text, out char value)
        {
            value = '\0';
            if (text.Length > 1) return false;
            if (text.Length == 1) value = text[0];
            return true;
        }

        public static string Char(char value) => value == '\0' ? "" : value.ToString();
    }
}
