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
