using JoinFS.Properties;

namespace JoinFS
{
    /// <summary>
    /// The user's own aircraft identity as the sim reports it - callsign, type, airline, registration - and what happens
    /// to the flight plan when it changes. Shared by the SimConnect and the X-Plane builds.
    /// </summary>
    public partial class Sim
    {
        readonly UserAircraftTracker userAircraftTracker = new();

        /// <summary>The ICAO type the sim reports for the user's aircraft, empty before it has reported one. Any thread (reads <see cref="View"/>).</summary>
        public string SimReportedIcaoType => view.UserIdentity?.IcaoType ?? "";

        /// <summary>
        /// The user's aircraft is seen for the first time. A plan already there for another type - a SimBrief OFP fetched at
        /// startup, before the sim reported the aircraft - is from another flight: it goes as on an aircraft change.
        /// </summary>
        /// <returns>true when the plan was reset</returns>
        bool CheckUserAircraftFirstSeen(Aircraft aircraft, ResolvedAircraftIdentity identity)
        {
            if (SimBriefPlanFitsAircraft(aircraft.flightPlan.icaoType, identity.IcaoType))
            {
                return false;
            }
            main.MonitorEvent("Flight plan is for '" + aircraft.flightPlan.icaoType + "', the sim reports '" + identity.IcaoType + "'");
            RefreshUserFlightPlanFromSim(aircraft, null, identity);
            return true;
        }

        /// <summary>
        /// Applies a changed identity to the user's aircraft and sends the flight plan at once. Another aircraft type is a
        /// new flight: the route is cleared and only a SimBrief plan for that type fills it again. A new callsign or
        /// registration on the same type keeps the route, and only a SimBrief plan filed under that callsign replaces it.
        /// </summary>
        /// <param name="before">The identity before the change, null if none was known</param>
        void RefreshUserFlightPlanFromSim(Aircraft aircraft, ResolvedAircraftIdentity before, ResolvedAircraftIdentity identity)
        {
            bool newFlight = ResolvedAircraftIdentity.IsNewFlight(before, identity);
            if (newFlight)
            {
                // nothing counts as loaded until a plan that fits this aircraft arrives
                userFlightPlanResets++;
            }
            ApplySimIdentity(aircraft, identity, clearRoute: newFlight);
#if !CONSOLE
            bool autoImport = Settings.Default.SimBriefAutoImport && string.IsNullOrWhiteSpace(Settings.Default.SimBriefUsername) == false;
#else
            bool autoImport = false;
#endif
            main.MonitorEvent((newFlight ? "Own aircraft changed" : "Own callsign changed") + " - refreshed callsign '" + identity.Callsign +
                "', airline '" + identity.IcaoAirline + "', registration '" + identity.Registration + "', type '" + identity.IcaoType + "' from the sim" +
                (newFlight ? ", flight plan cleared" : ", route kept") + (autoImport ? ", re-fetching SimBrief" : ""));
#if !CONSOLE
            if (autoImport)
            {
                if (newFlight)
                {
                    // only a plan made for this aircraft's type is applied
                    _ = RefreshUserFlightPlanFromSimBriefForAircraftAsync(identity.IcaoType);
                }
                else
                {
                    // only a plan filed under the new callsign is applied
                    _ = RefreshUserFlightPlanFromSimBriefForCallsignAsync(identity);
                }
            }
#endif
        }

        /// <summary>
        /// Gives the aircraft's flight plan the identity the sim reports (callsign, type, airline, registration),
        /// optionally clears the route of the previous flight, re-matches the livery and sends the plan to the others at once
        /// </summary>
        void ApplySimIdentity(Aircraft aircraft, ResolvedAircraftIdentity identity, bool clearRoute)
        {
            identity.ApplyTo(aircraft.flightPlan);
            if (clearRoute)
            {
                aircraft.flightPlan.ClearRoute();
                if (simBriefFetchState != SimBriefFetchState.Fetching)
                {
                    simBriefFetchState = SimBriefFetchState.NotTriggered;
                }
            }
            UpdateObject(aircraft, aircraft.ownerModel, aircraft.ownerLivery, identity.IcaoType, identity.IcaoAirline,
                aircraft.ownerClassCode, aircraft.ownerWtc, aircraft.ownerClassCodeConfirmed, aircraft.typerole);
            // tell the others now, not only with the next periodic flight plan broadcast (every 5 s)
            main.network.SimSender.BroadcastFlightPlanUpdate(aircraft.netId, aircraft.flightPlan);
            MarkViewDirty();
        }

        /// <summary>
        /// The Flight Plan tab's Clear: the user's plan becomes the aircraft's own again, as the sim reports it, without a
        /// route, and is sent at once (sim thread). False when the sim has not reported the aircraft yet.
        /// </summary>
        public bool ResetUserFlightPlanFromSim()
        {
            ResolvedAircraftIdentity identity = userAircraftTracker.Current;
            if (userAircraft == null || identity == null)
            {
                return false;
            }
            ApplySimIdentity(userAircraft, identity, clearRoute: true);
            main.MonitorEvent("Flight plan cleared - callsign '" + identity.Callsign + "', type '" + identity.IcaoType + "' from the sim");
            return true;
        }
    }
}
