using System.Text;

namespace JoinFS.Fsd
{
    /// <summary>Why a line is not relayed.</summary>
    public enum FsdRelayRefusal
    {
        /// <summary>It is relayed.</summary>
        None = 0,
        /// <summary>Not an FSD line (empty, no verb) or longer than <see cref="FsdRelayPolicy.MaxLineBytes"/>.</summary>
        Malformed,
        /// <summary>It has a control character.</summary>
        ControlCharacter,
        /// <summary>Its verb is not one of the relayed verbs.</summary>
        VerbNotRelayed,
        /// <summary>A landline <c>#PC</c>: not carried.</summary>
        Landline,
    }

    /// <summary>Where a line's recipient field points (docs/ATC/atc-fsd-plan.md §4.3).</summary>
    public enum FsdAddressKind
    {
        /// <summary>A callsign: an aircraft or a station.</summary>
        Callsign,
        /// <summary><c>SERVER</c>.</summary>
        Server,
        /// <summary><c>@94835</c>, <c>@49999</c>, <c>*A</c>, <c>*</c> or <c>@&lt;frequency&gt;</c>: all the controllers.</summary>
        Group,
    }

    /// <summary>
    /// Which lines may cross the mesh between controllers, and the checks on a line that need nothing but the
    /// line (§4.3 and §4.4). Whether the sender is a known station of the node it came from, and whether this
    /// instance has a station, depend on state and are the session's to check.
    /// </summary>
    public static class FsdRelayPolicy
    {
        /// <summary>The longest line the mesh carries (the size of an <c>AtcRelay</c>), in UTF-8 bytes.</summary>
        public const int MaxLineBytes = 1000;

        public const string ServerAddress = "SERVER";
        /// <summary>All controllers.</summary>
        public const string AllControllers = "@94835";
        /// <summary>All pilots: nothing is relayed to them (docs/ATC/atc-fsd-plan.md, out of scope).</summary>
        public const string AllPilots = "@94836";
        /// <summary>The ATC chat channel.</summary>
        public const string AtcChat = "@49999";

        /// <summary>Is the verb one the mesh relays between controllers: <c>$CQ $CR #PC $HO $HA #TM $PI $PO $AM</c>.</summary>
        public static bool IsRelayedVerb(string verb)
        {
            switch (verb)
            {
                case FsdVerb.FlightPlanAmendment:
                case FsdVerb.ClientQuery:
                case FsdVerb.ClientResponse:
                case FsdVerb.ProController:
                case FsdVerb.Handoff:
                case FsdVerb.HandoffAccept:
                case FsdVerb.TextMessage:
                case FsdVerb.Ping:
                case FsdVerb.Pong:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The <c>#PC</c> types that are landline calls (intercom, override, monitor; request, approve, reject, end).
        /// They name a network address and are dropped.
        /// </summary>
        public static bool IsLandline(FsdLine line)
        {
            if (line.Verb != FsdVerb.ProController || line.Field(2) != FsdProController.Ccp) return false;
            string type = line.Field(3);
            return type.Length == 2 && "IOM".IndexOf(type[0]) >= 0 && "CKBO".IndexOf(type[1]) >= 0;
        }

        /// <summary>
        /// Is the line one the mesh relays? Checks that it is a line, is not longer than
        /// <see cref="MaxLineBytes"/>, has no control character, has a relayed verb and is no landline.
        /// </summary>
        public static FsdRelayRefusal Check(string text)
        {
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) > MaxLineBytes) return FsdRelayRefusal.Malformed;
            if (FsdLine.HasControlCharacter(text)) return FsdRelayRefusal.ControlCharacter;
            if (!FsdLine.TryParse(text, out FsdLine line)) return FsdRelayRefusal.Malformed;
            if (!IsRelayedVerb(line.Verb)) return FsdRelayRefusal.VerbNotRelayed;
            if (IsLandline(line)) return FsdRelayRefusal.Landline;
            return FsdRelayRefusal.None;
        }

        /// <summary>Classify a recipient field.</summary>
        public static FsdAddressKind Classify(string to)
        {
            if (to == ServerAddress) return FsdAddressKind.Server;
            if (to == "*" || to == "*A") return FsdAddressKind.Group;
            if (to == AllControllers || to == AtcChat) return FsdAddressKind.Group;
            // @94836 is all pilots: controllers do not send to pilots, so it is no group here
            // @<frequency>: five digits, as in @22800; several may be joined with &, as in @21950&@19600
            if (to != AllPilots && to.Length > 1 && to[0] == '@' && IsFrequencyList(to)) return FsdAddressKind.Group;
            return FsdAddressKind.Callsign;
        }

        static bool IsFrequencyList(string to)
        {
            foreach (string part in to.Split('&'))
            {
                if (part.Length != 6 || part[0] != '@') return false;
                for (int i = 1; i < part.Length; i++)
                    if (part[i] < '0' || part[i] > '9') return false;
            }
            return true;
        }
    }
}
