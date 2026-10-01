using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using JoinFS.Properties;
using JoinFS.Net;




#if SIMCONNECT
#if P3D
//using LockheedMartin.Prepar3D.SimConnect;
using Microsoft.FlightSimulator.SimConnect;
#else
using Microsoft.FlightSimulator.SimConnect;
#endif
#endif

namespace JoinFS
{
    public partial class Sim
    {
        /// <summary>
        /// Resolve a real-world callsign from ICAO airline + flight number (e.g. "DLH" + "1234" -> "DLH1234").
        /// SimConnect's ATC ID is a tail number, not a callsign, and there is no single SimConnect variable that
        /// delivers a combined real-world callsign - so fall back to the tail number when either part is missing.
        /// </summary>
        internal static string ResolveCallsign(string icaoAirline, string flightNumber, string tailNumber)
        {
            if (!string.IsNullOrEmpty(icaoAirline) && !string.IsNullOrEmpty(flightNumber))
            {
                // ATC FLIGHT NUMBER is meant to be purely numeric, but it was write-only/unread by JoinFS
                // before this feature existed, so many add-ons/pilots instead stored an entire pre-existing
                // callsign there. If it already carries the airline prefix, or isn't numeric at all, trust
                // it as a complete callsign rather than gluing the airline code onto it again.
                if (flightNumber.StartsWith(icaoAirline, StringComparison.OrdinalIgnoreCase) || !flightNumber.All(char.IsDigit))
                {
                    return flightNumber;
                }
                return icaoAirline + flightNumber;
            }
            return tailNumber;
        }

        /// <summary>Matches a commercial-airline-shaped callsign: 3-letter ICAO airline designator, 1-4 digit
        /// flight number, optional trailing letters (e.g. "DLH1234", "BAW456A", "UAL2345"). Group 1 captures
        /// the designator. General Aviation tail-number callsigns ("N12345", "D-EJOE") don't match.</summary>
        [GeneratedRegex(@"^([A-Z]{3})\d{1,4}[A-Z]{0,3}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex AirlineCallsignRegex();

        /// <summary>
        /// Derive the ICAO airline designator from a callsign's shape, for use as a fallback when nothing
        /// more authoritative (SimBrief, live sim/config data) already supplied one. Returns "" when the
        /// callsign doesn't look like a commercial airline flight (General Aviation), so this is safe to try
        /// unconditionally.
        /// </summary>
        internal static string DeriveIcaoAirlineFromCallsign(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return "";
            Match m = AirlineCallsignRegex().Match(callsign.Trim().ToUpperInvariant());
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>
        /// Flight plan
        /// </summary>
        public class FlightPlan
        {
            public const int MAX_ROUTE = 512;
            public const int MAX_REMARKS = 512;

            /// <summary>Copy (every field is a value or a string)</summary>
            public FlightPlan Clone() => (FlightPlan)MemberwiseClone();

            public string callsign = "";
            /// <summary>
            /// True once callsign has been explicitly set via SimBrief import or manual FlightPlanForm
            /// entry - once true, SimConnect-derived defaults (the raw tail-number fallback, or the
            /// ATC-FLIGHT-NUMBER-based ResolveCallsign synthesis) must never overwrite it again.
            /// </summary>
            public bool callsignSetByUser = false;
            public string registration = "";
            public string icaoType = "";
            public string icaoAirline = "";
            public string flightNumber = "";
            public string departure = "";
            public string destination = "";
            public string rules = "";
            public string route = "";
            public string remarks = "";
            public string alternate = "";
            public string speed = "";
            public string altitude = "";
        }

        // user's main flight plan
        public FlightPlan userFlightPlan = new();

        /// <summary>
        /// Callsign/type last acted on by the own-aircraft-changed auto-refresh (see
        /// ResolveObjectInfoType's callers in Sim.SimConnect.cs) - compared against the freshly-
        /// resolved callsign/type on every "Me" info update to detect a real aircraft/callsign
        /// change. Deliberately lives here on Sim rather than on the Aircraft object itself: a
        /// genuine aircraft swap creates a brand-new Aircraft instance, so a per-instance field would
        /// always start empty and could never detect that case - this needs to survive across object
        /// recreation to compare the previous aircraft's identity against the new one. Empty means
        /// "not yet initialized" (first sighting this session - record only, no refresh, so this
        /// doesn't fight the ordinary reconnect/respawn "flight plan survives" behavior or duplicate
        /// the app-startup SimBrief auto-import trigger).
        /// </summary>
        string lastKnownUserCallsign = "";
        string lastKnownUserIcaoType = "";

        /// <summary>
        /// Re-fetch callsign/type for the user's own aircraft from the sim, and if SimBrief
        /// auto-import is enabled, re-run the SimBrief fetch too - the same thing that already
        /// happens once at JoinFS startup (see Program.cs), now also triggered whenever the sim
        /// reports a genuinely different aircraft/callsign for "Me" mid-session. A detected change is
        /// treated as "a new flight": the callsign goes back to auto-tracking even if it had been
        /// manually set for the previous leg.
        /// </summary>
        void RefreshUserFlightPlanFromSim(Aircraft aircraft, string resolvedCallsign, string resolvedType)
        {
            aircraft.flightPlan.callsignSetByUser = false;
            aircraft.flightPlan.callsign = resolvedCallsign;
            aircraft.flightPlan.icaoType = resolvedType;
#if !CONSOLE
            bool autoImport = Settings.Default.SimBriefAutoImport && string.IsNullOrWhiteSpace(Settings.Default.SimBriefUsername) == false;
#else
            bool autoImport = false;
#endif
            main.MonitorEvent("Own aircraft changed - refreshed callsign '" + resolvedCallsign + "'/type '" + resolvedType + "' from the sim" + (autoImport ? ", re-fetching SimBrief" : ""));
#if !CONSOLE
            if (autoImport)
            {
                _ = RefreshUserFlightPlanFromSimBriefAsync();
            }
#endif
        }

        /// <summary>
        /// State of the most recent SimBrief fetch attempt, for the main-screen SimBrief button's coloring -
        /// NotTriggered (neutral/default, like the flight plan button) until a fetch has actually happened,
        /// auto or manual, distinguishing "never asked" from "asked and failed".
        /// </summary>
        public enum SimBriefFetchState { NotTriggered, Fetching, Success, Failed }

        /// <summary>
        /// Result of the most recent SimBrief fetch attempt, for the main-screen SimBrief button's coloring
        /// </summary>
        public volatile SimBriefFetchState simBriefFetchState = SimBriefFetchState.NotTriggered;

#if !CONSOLE
        /// <summary>
        /// Fetch the pilot's latest SimBrief OFP and apply it to the user's flight plan if found.
        /// Any thread: the OFP is fetched into a copy and applied on the sim thread, which owns the
        /// flight plan; the task completes once it has been applied.
        /// </summary>
        public async Task<bool> RefreshUserFlightPlanFromSimBriefAsync()
        {
            simBriefFetchState = SimBriefFetchState.Fetching;
            FlightPlan fetched = new();
            bool ok = await JoinFS.SimBrief.FetchAsync(Settings.Default.SimBriefUsername, fetched, main);
            if (ok)
            {
                TaskCompletionSource applied = new(TaskCreationOptions.RunContinuationsAsynchronously);
                main.PostToSim(() =>
                {
                    try
                    {
                        ApplySimBrief(fetched);
                    }
                    finally
                    {
                        applied.SetResult();
                    }
                });
                await applied.Task;
            }
            simBriefFetchState = ok ? SimBriefFetchState.Success : SimBriefFetchState.Failed;
            return ok;
        }

        /// <summary>
        /// Copy what SimBrief provides (SimBrief.FetchAsync) into the user's flight plan (sim thread)
        /// </summary>
        void ApplySimBrief(FlightPlan fetched)
        {
            userFlightPlan.callsign = fetched.callsign;
            userFlightPlan.registration = fetched.registration;
            userFlightPlan.icaoType = fetched.icaoType;
            userFlightPlan.departure = fetched.departure;
            userFlightPlan.destination = fetched.destination;
            userFlightPlan.alternate = fetched.alternate;
            userFlightPlan.route = fetched.route;
            userFlightPlan.remarks = fetched.remarks;
            userFlightPlan.rules = fetched.rules;
            userFlightPlan.altitude = fetched.altitude;
            main.MonitorEvent("SimBrief flight plan imported: " + userFlightPlan.departure + " -> " + userFlightPlan.destination);
            // don't lock out the SimConnect fallback if SimBrief didn't actually provide a callsign
            if (userFlightPlan.callsign.Length > 0)
            {
                userFlightPlan.callsignSetByUser = true;
            }
            MarkViewDirty();
        }
#endif
    }
}
