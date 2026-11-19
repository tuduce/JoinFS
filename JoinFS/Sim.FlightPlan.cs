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
            /// True once callsign has been explicitly set via SimBrief import or the Flight Plan tab
            /// entry - once true, SimConnect-derived defaults (the raw tail-number fallback, or the
            /// ATC-FLIGHT-NUMBER-based CallsignRules.Resolve synthesis) must never overwrite it again.
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

            /// <summary>Clear what belongs to one flight - departure, destination, alternate, route, remarks, rules, speed, altitude - keeping the aircraft's identity</summary>
            public void ClearRoute()
            {
                departure = "";
                destination = "";
                alternate = "";
                route = "";
                remarks = "";
                rules = "";
                speed = "";
                altitude = "";
            }
        }

        /// <summary>
        /// Whether a SimBrief plan made for <paramref name="planIcaoType"/> fits the aircraft the sim reports as
        /// <paramref name="aircraftIcaoType"/>. Either one unknown: it fits, since nothing can be compared.
        /// </summary>
        public static bool SimBriefPlanFitsAircraft(string planIcaoType, string aircraftIcaoType)
        {
            string plan = planIcaoType?.Trim() ?? "";
            string aircraft = aircraftIcaoType?.Trim() ?? "";
            return plan.Length == 0 || aircraft.Length == 0 || string.Equals(plan, aircraft, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Counts the times the user's flight plan was reset for a changed aircraft, so the UI can tell that a plan it
        /// saved before no longer counts as loaded
        /// </summary>
        public volatile int userFlightPlanResets = 0;

        /// <summary>
        /// Whether a SimBrief plan filed under <paramref name="planCallsign"/> is the flight the sim now reports, by its
        /// callsign or its registration. An OFP without a callsign matches nothing.
        /// </summary>
        public static bool SimBriefPlanFitsCallsign(string planCallsign, string callsign, string registration)
        {
            string plan = planCallsign?.Trim() ?? "";
            return plan.Length > 0 &&
                (string.Equals(plan, callsign?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(plan, registration?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // user's main flight plan
        public FlightPlan userFlightPlan = new();


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
        /// The automatic fetch at startup: the pilot's latest SimBrief OFP, applied if the sim has not reported an aircraft
        /// yet or reports one of the OFP's type. Any thread: the OFP is fetched into a copy and applied on the sim thread,
        /// which owns the flight plan; the task completes once it has been applied. An OFP applied before the aircraft
        /// was known is checked again when it is (<see cref="CheckUserAircraftFirstSeen"/>).
        /// </summary>
        public async Task<bool> RefreshUserFlightPlanFromSimBriefAsync() =>
            await FetchSimBriefAsync(Settings.Default.SimBriefUsername, apply: true, fitCurrentAircraft: true) != null;

        /// <summary>
        /// Fetch the pilot's latest SimBrief OFP after the aircraft changed, and apply it only if it was made for that
        /// aircraft's type. Otherwise the plan stays as the aircraft change left it (identity from the sim, no route)
        /// and the SimBrief state reads "not loaded".
        /// </summary>
        /// <param name="aircraftIcaoType">The ICAO type the sim reports for the new aircraft</param>
        async Task RefreshUserFlightPlanFromSimBriefForAircraftAsync(string aircraftIcaoType) =>
            await FetchSimBriefAsync(Settings.Default.SimBriefUsername, apply: true, requiredIcaoType: aircraftIcaoType);

        /// <summary>
        /// Fetch the pilot's latest SimBrief OFP after only the callsign or registration changed, and apply all of it
        /// only if it was filed under that callsign (or registration) for this type. Otherwise the plan, its route and the
        /// SimBrief state stay as they were: an older OFP must not undo a callsign just entered in the sim.
        /// </summary>
        async Task RefreshUserFlightPlanFromSimBriefForCallsignAsync(ResolvedAircraftIdentity identity) =>
            await FetchSimBriefAsync(Settings.Default.SimBriefUsername, apply: true, requiredIcaoType: identity.IcaoType, requiredIdentity: identity);

        /// <summary>
        /// Fetch <paramref name="username"/>'s latest SimBrief OFP, and set <see cref="simBriefFetchState"/> for the SimBrief button.
        /// With <paramref name="apply"/> it is also applied to the user's flight plan (the task completes once it has been);
        /// without, the plan is only returned, for a dialog to show before the pilot commits it. Any thread.
        /// </summary>
        /// <param name="requiredIcaoType">Apply only an OFP made for this ICAO type (see <see cref="SimBriefPlanFitsAircraft"/>); null for any</param>
        /// <param name="requiredIdentity">Apply only an OFP filed under this callsign or registration (see <see cref="SimBriefPlanFitsCallsign"/>);
        /// otherwise the SimBrief state is put back as it was. Null for any.</param>
        /// <param name="fitCurrentAircraft">Apply only an OFP made for the type the sim reports when it arrives, if the sim reports one</param>
        /// <returns>The fetched plan, or null when no usable OFP was found</returns>
        public async Task<FlightPlan> FetchSimBriefAsync(string username, bool apply, string requiredIcaoType = null, ResolvedAircraftIdentity requiredIdentity = null,
            bool fitCurrentAircraft = false)
        {
            SimBriefFetchState previous = simBriefFetchState;
            simBriefFetchState = SimBriefFetchState.Fetching;
            FlightPlan fetched = new();
            bool ok = await JoinFS.SimBrief.FetchAsync(username, fetched, main);
            if (fitCurrentAircraft && requiredIcaoType == null)
            {
                // the aircraft the sim reports now, after the fetch: null while it has not reported one
                requiredIcaoType = userAircraftTracker.Current?.IcaoType;
            }
            if (ok && requiredIcaoType != null && SimBriefPlanFitsAircraft(fetched.icaoType, requiredIcaoType) == false)
            {
                // the latest OFP was made for another aircraft: a plan from a different flight is worse than none
                main.MonitorEvent("SimBrief plan is for '" + fetched.icaoType + "', the aircraft is '" + requiredIcaoType + "' - not applied");
                simBriefFetchState = requiredIdentity != null ? previous : SimBriefFetchState.NotTriggered;
                return null;
            }
            if (ok && requiredIdentity != null && SimBriefPlanFitsCallsign(fetched.callsign, requiredIdentity.Callsign, requiredIdentity.Registration) == false)
            {
                // the latest OFP is for another flight than the callsign now set in the sim: keep the sim's
                main.MonitorEvent("SimBrief plan is for callsign '" + fetched.callsign + "', the sim reports '" + requiredIdentity.Callsign + "' - not applied");
                simBriefFetchState = previous;
                return null;
            }
            if (!ok && requiredIdentity != null)
            {
                // nothing fetched: a kept plan is as loaded as before
                simBriefFetchState = previous;
                return null;
            }
            if (ok && apply)
            {
                await ApplySimBriefAsync(fetched);
            }
            simBriefFetchState = ok ? SimBriefFetchState.Success : SimBriefFetchState.Failed;
            return ok ? fetched : null;
        }

        /// <summary>
        /// Makes a fetched OFP the user's flight plan and sends it, whatever aircraft the sim reports - the pilot asked
        /// for it. Any thread; completes once it has been applied on the sim thread.
        /// </summary>
        public async Task ApplySimBriefAsync(FlightPlan fetched)
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
            simBriefFetchState = SimBriefFetchState.Success;
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
            // tell the others now, not only with the next periodic flight plan broadcast (every 5 s)
            if (userAircraft != null)
            {
                main.network.SimSender.BroadcastFlightPlanUpdate(userAircraft.netId, userFlightPlan);
            }
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
