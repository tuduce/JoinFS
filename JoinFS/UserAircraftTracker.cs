using System;

namespace JoinFS
{
    /// <summary>
    /// The raw SimConnect data for the user's own aircraft, as plain values, so it compiles in every build
    /// and can be compared with Equals.
    /// </summary>
    public sealed record UserAircraftInfo(string Type, string Model, string Callsign, string FlightNumber, string Airline,
        string Livery, string LiveryFolder, bool IsUser, string Category, int EngineType, int NumEngines);

    /// <summary>The identity the user's aircraft is broadcast and matched under.</summary>
    public sealed record ResolvedAircraftIdentity(string Callsign, string IcaoType, string IcaoAirline, string Registration, string FlightNumber)
    {
        /// <summary>Writes every field the broadcast and the livery match read onto the flight plan, in one place.</summary>
        public void ApplyTo(Sim.FlightPlan plan)
        {
            plan.callsign = Callsign;
            plan.callsignSetByUser = false;
            plan.icaoType = IcaoType;
            plan.icaoAirline = IcaoAirline;
            plan.registration = Registration;
            plan.flightNumber = FlightNumber;
        }

        /// <summary>
        /// The identity of the user's X-Plane aircraft. <paramref name="flightId"/> is the Mode-S flight ID X-Plane
        /// transmits (sim/cockpit2/radios/actuators/flight_id): airline + flight number like KLM511, or the registration.
        /// <paramref name="tailNumber"/> is acf_tailnum, empty from a plugin that sends only one of the two.
        /// </summary>
        public static ResolvedAircraftIdentity FromXPlane(string icaoType, string flightId, string tailNumber)
        {
            string id = flightId?.Trim() ?? "";
            string tail = tailNumber?.Trim() ?? "";
            string callsign = id.Length > 0 ? id : tail;
            string airline = CallsignRules.DeriveIcaoAirline(callsign, AirlineDirectory.Bundled);
            // an older plugin sends the tail number as the callsign only
            if (tail.Length == 0 && airline.Length == 0)
            {
                tail = callsign;
            }
            return new ResolvedAircraftIdentity(callsign, icaoType?.Trim() ?? "", airline, tail, airline.Length > 0 ? callsign : "");
        }

        /// <summary>
        /// Whether going from <paramref name="before"/> to <paramref name="after"/> is a new flight - another aircraft
        /// type, so the route of the previous one is gone - rather than a new callsign or registration for the same one.
        /// </summary>
        public static bool IsNewFlight(ResolvedAircraftIdentity before, ResolvedAircraftIdentity after) =>
            before == null || !string.Equals(before.IcaoType?.Trim(), after.IcaoType?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decides whether the user's aircraft changed. Resolution runs only when the raw data differs from the last poll,
    /// so an unchanged aircraft costs one comparison per poll. A change is reported only when the resolved identity
    /// really differs, so a resolve that improves between polls (config becoming ready) is not reported as a change.
    /// </summary>
    public sealed class UserAircraftTracker
    {
        UserAircraftInfo lastRaw;
        ResolvedAircraftIdentity lastResolved;

        /// <summary>The identity last resolved from the sim, null before the first</summary>
        public ResolvedAircraftIdentity Current => lastResolved;

        /// <returns>true when the resolved identity changed since the previous different raw data.</returns>
        public bool Check(UserAircraftInfo raw, Func<UserAircraftInfo, ResolvedAircraftIdentity> resolve, out ResolvedAircraftIdentity identity)
        {
            if (lastRaw != null && lastRaw.Equals(raw))
            {
                identity = lastResolved;
                return false;
            }

            lastRaw = raw;
            identity = resolve(raw);
            bool changed = lastResolved != null && !lastResolved.Equals(identity);
            lastResolved = identity;
            return changed;
        }
    }
}
