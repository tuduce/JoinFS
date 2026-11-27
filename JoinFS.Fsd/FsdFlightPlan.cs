namespace JoinFS.Fsd
{
    /// <summary>
    /// A flight plan, the <c>$FP</c> line, or an amendment of one, the <c>$AM</c> line. The two differ in one
    /// field: an amendment names the aircraft in a field of its own (<see cref="Callsign"/>), because its sender
    /// is the controller; in a <c>$FP</c> the sender is the aircraft. Times and the cruise level stay text, as
    /// clients write them (<c>0930</c>, <c>35000</c> or <c>FL350</c>), so nothing is lost on the way through.
    /// </summary>
    public sealed class FsdFlightPlan
    {
        /// <summary>True for <c>$AM</c>, false for <c>$FP</c>.</summary>
        public bool IsAmendment { get; set; }
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        /// <summary>The aircraft of an amendment. A <c>$FP</c> has no such field: its aircraft is <see cref="From"/>.</summary>
        public string Callsign { get; set; } = "";
        /// <summary><c>I</c> IFR, <c>V</c> VFR, <c>D</c> DVFR, <c>S</c> SVFR; NUL when the field is empty.</summary>
        public char Rules { get; set; } = 'I';
        public string Equipment { get; set; } = "";
        public int TrueAirspeedKnots { get; set; }
        public string Departure { get; set; } = "";
        public string EstimatedDepartureTime { get; set; } = "";
        public string ActualDepartureTime { get; set; } = "";
        public string CruiseAltitude { get; set; } = "";
        public string Destination { get; set; } = "";
        public int HoursEnroute { get; set; }
        public int MinutesEnroute { get; set; }
        public int HoursFuel { get; set; }
        public int MinutesFuel { get; set; }
        public string Alternate { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string Route { get; set; } = "";

        /// <summary>The aircraft the plan is about: the callsign of an amendment, the sender of a <c>$FP</c>.</summary>
        public string Aircraft => IsAmendment ? Callsign : From;

        public static bool TryParse(FsdLine line, out FsdFlightPlan plan)
        {
            plan = null;
            bool amendment = line.Verb == FsdVerb.FlightPlanAmendment;
            if (!amendment && line.Verb != FsdVerb.FlightPlan) return false;

            // an amendment has the aircraft as a third field; everything after it is the same as in a $FP
            int o = amendment ? 1 : 0;
            var p = new FsdFlightPlan
            {
                IsAmendment = amendment,
                From = line.Field(0),
                To = line.Field(1),
                Callsign = amendment ? line.Field(2) : "",
                Equipment = line.Field(3 + o),
                Departure = line.Field(5 + o),
                EstimatedDepartureTime = line.Field(6 + o),
                ActualDepartureTime = line.Field(7 + o),
                CruiseAltitude = line.Field(8 + o),
                Destination = line.Field(9 + o),
                Alternate = line.Field(14 + o),
                Remarks = line.Field(15 + o),
                Route = line.Field(16 + o),
            };
            if (!FsdText.TryChar(line.Field(2 + o), out char rules)) return false;
            p.Rules = rules;
            if (!FsdText.TryInt(line.Field(4 + o), out int speed)) return false;
            if (!FsdText.TryInt(line.Field(10 + o), out int hoursEnroute)) return false;
            if (!FsdText.TryInt(line.Field(11 + o), out int minutesEnroute)) return false;
            if (!FsdText.TryInt(line.Field(12 + o), out int hoursFuel)) return false;
            if (!FsdText.TryInt(line.Field(13 + o), out int minutesFuel)) return false;
            p.TrueAirspeedKnots = speed;
            p.HoursEnroute = hoursEnroute;
            p.MinutesEnroute = minutesEnroute;
            p.HoursFuel = hoursFuel;
            p.MinutesFuel = minutesFuel;
            plan = p;
            return true;
        }

        public FsdLine ToLine()
        {
            string[] common =
            {
                FsdText.Char(Rules), Equipment, FsdText.Int(TrueAirspeedKnots), Departure,
                EstimatedDepartureTime, ActualDepartureTime, CruiseAltitude, Destination,
                FsdText.Int(HoursEnroute), FsdText.Int(MinutesEnroute), FsdText.Int(HoursFuel), FsdText.Int(MinutesFuel),
                Alternate, Remarks, Route,
            };
            if (!IsAmendment)
                return new FsdLine(FsdVerb.FlightPlan, Prepend(common, From, To));
            return new FsdLine(FsdVerb.FlightPlanAmendment, Prepend(common, From, To, Callsign));
        }

        static string[] Prepend(string[] tail, params string[] head)
        {
            var all = new string[head.Length + tail.Length];
            head.CopyTo(all, 0);
            tail.CopyTo(all, head.Length);
            return all;
        }
    }
}
