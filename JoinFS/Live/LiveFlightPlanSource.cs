using System;
using System.Threading;
using System.Threading.Tasks;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The user's flight plan and SimBrief for the new UI. The plan is the sim thread's own (<c>sim.userFlightPlan</c>): it is read from
    /// the <c>Sim.View</c> snapshot and changed by a command posted to the sim thread, as <c>FlightPlanForm</c> did. SimBrief is the
    /// same fetch the old SimBrief button made, and its state (<c>simBriefFetchState</c>) is what the strip's button shows.
    /// One class, since a plan imported to be shown carries a registration and an alternate that the tab has no field for, and they
    /// must reach the plan when it is saved.
    /// </summary>
    class LiveFlightPlanSource : IFlightPlanStore, ISimBriefClient
    {
        readonly Main main;

        // What the last import brought that the tab has no field for. Carried to the plan on Save, as the old dialog did.
        string pendingRegistration;
        string pendingAlternate;
        bool hasPending;

        // A filled plan has been saved from the tab since the last Reset. Only the UI thread touches it.
        bool planSaved;
        // Sim.userFlightPlanResets when the plan was saved: an aircraft change since then cleared it, so it is no longer loaded
        int planSavedAtReset;

        // the saved plan still counts: no aircraft change has cleared it since
        bool PlanSaved => planSaved && main.sim != null && main.sim.userFlightPlanResets == planSavedAtReset;

        // The last fetch was only shown on the tab and not filed: the button must not read "loaded" for it until Save.
        volatile bool fetchOnlyShown;

        public LiveFlightPlanSource(Main main)
        {
            this.main = main;
        }

        // ---- the plan

        // The plan just filed, shown until the sim thread has applied it and published a snapshot that has it. Without it the
        // tab would read the snapshot from before Save and show the old plan for a moment. Only the UI thread touches it.
        FlightPlanData filed;
        // The snapshot that was current when the sim thread applied the filed plan, and whether it has: any later one has it
        volatile SimSnapshot snapshotAtApply;
        volatile bool filedApplied;

        public FlightPlanData Load()
        {
            SimSnapshot view = main.sim?.View;
            if (filed != null)
            {
                if (!filedApplied || ReferenceEquals(view, snapshotAtApply))
                {
                    return filed;
                }
                filed = null;
            }
            Sim.FlightPlan plan = view?.UserFlightPlan;
            if (plan == null)
            {
                return new FlightPlanData("", "", "VFR", "", "", "", "", "");
            }
            return new FlightPlanData(
                plan.callsign, plan.icaoType, plan.rules == "IFR" ? "IFR" : "VFR",
                plan.departure.ToUpperInvariant(), plan.destination.ToUpperInvariant(), plan.altitude, plan.route, plan.remarks);
        }

        public void Save(FlightPlanData data)
        {
            if (main.sim == null)
            {
                return;
            }

            string registration = pendingRegistration;
            string alternate = pendingAlternate;
            bool imported = hasPending;
            hasPending = false;

            // blank route fields are a cleared plan: its alternate goes too
            bool cleared = data.IsBlank;
            if (cleared)
            {
                // nothing is loaded any more, whatever was fetched before
                Reset();
            }
            else
            {
                planSaved = true;
                planSavedAtReset = main.sim.userFlightPlanResets;
            }

            filed = data;
            filedApplied = false;
            main.SimCommand(sim =>
            {
                try
                {
                    Apply(sim, data, imported, registration, alternate, cleared);
                }
                finally
                {
                    // the snapshot published next has the plan
                    snapshotAtApply = sim.View;
                    filedApplied = true;
                    sim.MarkViewDirty();
                }
            });
        }

        /// <summary>Makes <paramref name="data"/> the user's flight plan and sends it at once (sim thread)</summary>
        void Apply(Sim sim, FlightPlanData data, bool imported, string registration, string alternate, bool cleared)
        {
            {
                // Clear: the aircraft's own callsign, type, airline and registration as the sim reports them, sent at once.
                // Only then - a callsign or type typed in without a route is the user's and must not be taken back.
                if (data.IsCleared && sim.ResetUserFlightPlanFromSim())
                {
                    return;
                }

                Sim.FlightPlan target = sim.userFlightPlan;

                // a blank callsign or type is the aircraft's own again, as the dialog's Clear gave it back
                bool callsignGiven = data.Callsign.Length > 0;
                target.callsign = callsignGiven ? data.Callsign : sim.userAircraft?.originalCallsign ?? "";
                // once typed or imported, what the sim reports must not overwrite it again
                target.callsignSetByUser = callsignGiven;
                // the airline follows the callsign, so a stale one from the sim's livery does not keep overriding it
                target.icaoAirline = CallsignRules.DeriveIcaoAirline(target.callsign, AirlineDirectory.Bundled);
                target.icaoType = data.Type.Length > 0 ? data.Type : sim.userAircraft?.originalIcaoType ?? "";
                target.departure = Clip(data.From, 4).ToUpperInvariant();
                target.destination = Clip(data.To, 4).ToUpperInvariant();
                target.rules = data.Rules == "IFR" ? "IFR" : "VFR";
                target.route = Clip(data.Route, Sim.FlightPlan.MAX_ROUTE);
                target.remarks = Clip(data.Remarks, Sim.FlightPlan.MAX_REMARKS);
                target.altitude = data.Altitude;
                if (imported)
                {
                    target.registration = registration;
                    target.alternate = alternate;
                }
                else if (cleared)
                {
                    target.alternate = "";
                }
                // filed: the others get it now, not with the next periodic flight plan broadcast (every 5 s)
                if (sim.userAircraft != null)
                {
                    main.network.SimSender.BroadcastFlightPlanUpdate(sim.userAircraft.netId, target);
                }
            }
        }

        static string Clip(string text, int length) => text.Length <= length ? text : text.Substring(0, length);

        // ---- SimBrief

        public bool ReportsState => true;

        public void Poll()
        {
        }

        public ConnectionState State
        {
            get
            {
                switch (main.sim?.simBriefFetchState)
                {
                    case Sim.SimBriefFetchState.Fetching:
                        return ConnectionState.Connecting;
                    case Sim.SimBriefFetchState.Success:
                        return fetchOnlyShown && !PlanSaved ? ConnectionState.Disconnected : ConnectionState.Connected;
                    default:
                        // a plan saved from the tab is loaded as much as one fetched; otherwise not asked yet, asked and
                        // nothing found, or cleared by an aircraft change: the button reads "not loaded"
                        return PlanSaved ? ConnectionState.Connected : ConnectionState.Disconnected;
                }
            }
        }

        public void Reset()
        {
            planSaved = false;
            fetchOnlyShown = false;
            if (main.sim != null && main.sim.simBriefFetchState != Sim.SimBriefFetchState.Fetching)
            {
                main.sim.simBriefFetchState = Sim.SimBriefFetchState.NotTriggered;
            }
        }

        public async Task<FlightPlanData> FetchAsync(string username, bool commit, CancellationToken cancellationToken)
        {
            Sim sim = main.sim;
            if (sim == null)
            {
                return null;
            }

            Sim.FlightPlan fetched = await sim.FetchSimBriefAsync(username, apply: commit);
            if (fetched == null)
            {
                return null;
            }

            fetchOnlyShown = !commit;
            if (commit)
            {
                Committed();
            }
            else
            {
                pendingRegistration = fetched.registration;
                pendingAlternate = fetched.alternate;
                hasPending = true;
            }

            return ToData(fetched);
        }

        public async Task<FlightPlanData> FetchAsync(string username, bool commit, Func<FlightPlanData, Task<bool>> confirmOtherType, CancellationToken cancellationToken)
        {
            Sim sim = main.sim;
            if (!commit || confirmOtherType == null || sim == null)
            {
                return await FetchAsync(username, commit, cancellationToken);
            }

            // fetched first, made the user's only when it is for this aircraft or the pilot says so
            Sim.SimBriefFetchState before = sim.simBriefFetchState;
            Sim.FlightPlan fetched = await sim.FetchSimBriefAsync(username, apply: false);
            if (fetched == null)
            {
                return null;
            }
            FlightPlanData data = ToData(fetched);
            if (!Sim.SimBriefPlanFitsAircraft(fetched.icaoType, sim.SimReportedIcaoType) && !await confirmOtherType(data))
            {
                // declined: the plan and the button stay as they were
                sim.simBriefFetchState = before;
                return null;
            }

            await sim.ApplySimBriefAsync(fetched);
            fetchOnlyShown = false;
            Committed();
            return data;
        }

        public string SimAircraftType => main.sim?.SimReportedIcaoType ?? "";

        // the strip's button commits and sends at once, there is no Save to wait for
        void Committed()
        {
            hasPending = false;
            main.SimCommand(s =>
            {
                if (s.userAircraft != null)
                {
                    main.network.SimSender.BroadcastFlightPlanUpdate(s.userAircraft.netId, s.userFlightPlan);
                }
            });
        }

        static FlightPlanData ToData(Sim.FlightPlan fetched) => new(
            fetched.callsign, fetched.icaoType, fetched.rules, fetched.departure, fetched.destination,
            fetched.altitude, fetched.route, fetched.remarks);
    }
}
