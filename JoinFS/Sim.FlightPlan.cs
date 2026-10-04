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
            /// True once callsign has been explicitly set via SimBrief import or manual FlightPlanForm
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
        /// Fetch the pilot's latest SimBrief OFP and apply it to the user's flight plan if found.
        /// Any thread: the OFP is fetched into a copy and applied on the sim thread, which owns the
        /// flight plan; the task completes once it has been applied.
        /// </summary>
        public async Task<bool> RefreshUserFlightPlanFromSimBriefAsync() =>
            await FetchSimBriefAsync(Settings.Default.SimBriefUsername, apply: true) != null;

        /// <summary>
        /// Fetch <paramref name="username"/>'s latest SimBrief OFP, and set <see cref="simBriefFetchState"/> for the SimBrief button.
        /// With <paramref name="apply"/> it is also applied to the user's flight plan (the task completes once it has been);
        /// without, the plan is only returned, for a dialog to show before the pilot commits it. Any thread.
        /// </summary>
        /// <returns>The fetched plan, or null when no usable OFP was found</returns>
        public async Task<FlightPlan> FetchSimBriefAsync(string username, bool apply)
        {
            simBriefFetchState = SimBriefFetchState.Fetching;
            FlightPlan fetched = new();
            bool ok = await JoinFS.SimBrief.FetchAsync(username, fetched, main);
            if (ok && apply)
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
            return ok ? fetched : null;
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
