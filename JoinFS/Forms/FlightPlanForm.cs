using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class FlightPlanForm : Form
    {
        public Sim.FlightPlan plan;

        /// <summary>
        /// When true, focuses the SimBrief username field on load instead of the callsign field -
        /// used when the main-screen SimBrief button is clicked with no username configured yet
        /// </summary>
        public bool FocusSimBriefUsername { get; set; }

        Main main;
        /// <summary>
        /// Aircraft this flight plan belongs to, used by Button_Clear_Click to re-fetch the live
        /// callsign (Sim.Aircraft.originalCallsign) - may be null (e.g. no user aircraft registered
        /// yet), in which case Clear leaves the callsign field alone.
        /// </summary>
        readonly Sim.Aircraft aircraft;

        // fields SimBrief can supply that this dialog doesn't have a visible control for -
        // carried through to plan on OK so they still reach the network/EuroScope
        string pendingRegistration;
        string pendingFlightNumber;
        string pendingAlternate;

        /// <param name="aircraft">Snapshot copy of the aircraft (Sim.View), or null for the user's own plan</param>
        /// <param name="plan">The plan to show - a snapshot copy; OK applies the edits to the live plan on the sim thread</param>
        public FlightPlanForm(Main main, Sim.Aircraft aircraft, Sim.FlightPlan plan)
        {
            InitializeComponent();

            this.main = main;
            this.aircraft = aircraft;
            this.plan = plan;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // change font
            Text_Callsign.Font = main.dataFont;
            Text_Type.Font = main.dataFont;
            Text_From.Font = main.dataFont;
            Text_To.Font = main.dataFont;
            Combo_Rules.Font = main.dataFont;
            Text_Route.Font = main.dataFont;
            Text_Remarks.Font = main.dataFont;
            Text_Altitude.Font = main.dataFont;
            Text_SimBriefUsername.Font = main.dataFont;

            if (Settings.Default.ToolTips)
            {
                ToolTip tip = new() { ShowAlways = true, IsBalloon = true, AutomaticDelay = 2000 };
                tip.SetToolTip(Text_Altitude, Resources.Strings.FlightPlan_AltitudeTooltip);
                tip.SetToolTip(Text_SimBriefUsername, Resources.Strings.FlightPlan_SimBriefUsernameTooltip);
                tip.SetToolTip(Button_ImportSimBrief, Resources.Strings.FlightPlan_ImportSimBriefTooltip);
            }
        }

        private void FlightPlanForm_Load(object sender, EventArgs e)
        {
            // initialize limits
            Text_From.MaxLength = 4;
            Text_To.MaxLength = 4;
            Text_Route.MaxLength = Sim.FlightPlan.MAX_ROUTE;
            Text_Remarks.MaxLength = Sim.FlightPlan.MAX_REMARKS;

            lock (main.conch)
            {
                // initialize form
                Text_Callsign.Text = plan.callsign;
                Text_Type.Text = plan.icaoType;
                Text_From.Text = plan.departure.ToUpperInvariant();
                Text_To.Text = plan.destination.ToUpperInvariant();
                Combo_Rules.Items.Add("VFR");
                Combo_Rules.Items.Add("IFR");
                Combo_Rules.SelectedIndex = plan.rules == "IFR" ? 1 : 0;
                Text_Route.Text = plan.route;
                Text_Remarks.Text = plan.remarks;
                Text_Altitude.Text = plan.altitude;

                // carry through fields with no visible control, unchanged, unless an import replaces them
                pendingRegistration = plan.registration;
                pendingFlightNumber = plan.flightNumber;
                pendingAlternate = plan.alternate;
            }

            Text_SimBriefUsername.Text = Settings.Default.SimBriefUsername;

            if (FocusSimBriefUsername)
            {
                Text_SimBriefUsername.Focus();
            }
        }

        private async void Button_ImportSimBrief_Click(object sender, EventArgs e)
        {
            await ImportSimBriefAsync();
        }

        /// <summary>
        /// Save the SimBrief username and fetch a plan from it, pre-filling the dialog on success.
        /// Shared by the Import button and pressing Enter in the username field.
        /// </summary>
        async Task ImportSimBriefAsync()
        {
            string username = Text_SimBriefUsername.Text.Trim();

            // remember the username regardless of fetch outcome
            Settings.Default.SimBriefUsername = username;
            Settings.Default.Save();

            Button_ImportSimBrief.Enabled = false;
            Label_SimBriefStatus.Text = "";
            // the main-screen badge reflects the fetch *attempt*, regardless of whether the pilot
            // goes on to commit it via OK - update it immediately, not just on OK
            main.sim.simBriefFetchState = Sim.SimBriefFetchState.Fetching;
            try
            {
                Sim.FlightPlan imported = new();
                bool ok = await SimBrief.FetchAsync(username, imported, main);

                main.sim.simBriefFetchState = ok ? Sim.SimBriefFetchState.Success : Sim.SimBriefFetchState.Failed;

                if (ok)
                {
                    // pre-fill the dialog only - nothing is committed to plan/broadcast until OK is clicked
                    Text_Callsign.Text = imported.callsign;
                    Text_Type.Text = imported.icaoType;
                    Text_From.Text = imported.departure.ToUpperInvariant();
                    Text_To.Text = imported.destination.ToUpperInvariant();
                    Combo_Rules.SelectedIndex = imported.rules == "IFR" ? 1 : 0;
                    Text_Route.Text = imported.route;
                    Text_Remarks.Text = imported.remarks;
                    Text_Altitude.Text = imported.altitude;

                    pendingRegistration = imported.registration;
                    pendingAlternate = imported.alternate;

                    Label_SimBriefStatus.Text = string.Format(Resources.Strings.FlightPlan_Imported, imported.departure, imported.destination);
                }
                else
                {
                    // never blank out already-shown data on failure
                    Label_SimBriefStatus.Text = Resources.Strings.FlightPlan_NoSimBriefPlan;
                }
            }
            finally
            {
                Button_ImportSimBrief.Enabled = true;
            }
        }

        /// <summary>
        /// Enter in a single-line TextBox never reaches KeyDown at all - a non-multiline TextBox's
        /// IsInputKey returns false for Enter, so WinForms treats it as a "dialog key" and routes it
        /// straight to the form's AcceptButton (OK) before the control's own KeyDown is ever raised.
        /// Intercepting here is the correct place: if the SimBrief username field has focus and isn't
        /// empty, save+import first, then commit and close - same end result as Import followed by OK.
        /// An empty field just falls through to the normal AcceptButton (OK) behavior.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter && ActiveControl == Text_SimBriefUsername && Text_SimBriefUsername.Text.Trim().Length > 0)
            {
                _ = AcceptSimBriefUsernameAsync();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        async Task AcceptSimBriefUsernameAsync()
        {
            await ImportSimBriefAsync();

            Button_OK_Click(this, EventArgs.Empty);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Button_Clear_Click(object sender, EventArgs e)
        {
            // re-fetch callsign/type from the sim instead of leaving whatever's currently shown - both
            // plan.callsign and plan.icaoType can be permanently stuck on a manually-typed or SimBrief-
            // imported value once committed via OK (nothing in the sim-update path ever overwrites them
            // again - see FlightPlan.callsignSetByUser and the "fill icaoType only when blank" pattern
            // in Sim.cs). originalCallsign/originalIcaoType are the aircraft's own values as first
            // reported by the sim for this object, kept separate from the editable plan fields for
            // exactly this purpose.
            if (aircraft != null)
            {
                Text_Callsign.Text = aircraft.originalCallsign;
                Text_Type.Text = aircraft.originalIcaoType;
            }
            // clears the rest of the route-plan fields, same ones a SimBrief import would otherwise fill in
            Text_From.Text = "";
            Text_To.Text = "";
            Text_Route.Text = "";
            Text_Remarks.Text = "";
            Text_Altitude.Text = "";
            pendingAlternate = "";
            Label_SimBriefStatus.Text = "";
            // the main-screen SimBrief button's color reflects the last fetch attempt - clearing the plan
            // here should revert it back to neutral/default, as if no SimBrief fetch had happened yet,
            // rather than continuing to show a stale success/failure from before the clear
            main.sim.simBriefFetchState = Sim.SimBriefFetchState.NotTriggered;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // edit a copy, then apply it to the live plan on the sim thread, which owns it
            Sim.FlightPlan plan = this.plan.Clone();
            {
                // return flight plan
                plan.callsign = Text_Callsign.Text;
                // explicitly set here (manual entry, or a SimBrief import pre-filled into this same
                // textbox and committed via OK either way) - once true, SimConnect-derived defaults must
                // never overwrite it again, see FlightPlan.callsignSetByUser
                plan.callsignSetByUser = true;
                // re-derive the ICAO airline from the (possibly just-changed) callsign - it's the only
                // airline-relevant signal actually editable in this dialog (SimBrief never supplies
                // icaoAirline), so a stale sim/livery-derived tag (e.g. from the sim's own aircraft-
                // customization dialog) must not keep overriding what the user is now flying as. Empty
                // when the new callsign doesn't look like a commercial flight (GA-style).
                plan.icaoAirline = Sim.DeriveIcaoAirlineFromCallsign(plan.callsign);
                plan.icaoType = Text_Type.Text;
                plan.departure = Text_From.Text.Substring(0, Math.Min(4, Text_From.Text.Length)).ToUpperInvariant();
                plan.destination = Text_To.Text.Substring(0, Math.Min(4, Text_To.Text.Length)).ToUpperInvariant();
                plan.rules = Combo_Rules.Text;
                plan.route = Text_Route.Text.Substring(0, Math.Min(Sim.FlightPlan.MAX_ROUTE, Text_Route.Text.Length));
                plan.remarks = Text_Remarks.Text.Substring(0, Math.Min(Sim.FlightPlan.MAX_REMARKS, Text_Remarks.Text.Length));
                plan.altitude = Text_Altitude.Text;
                plan.registration = pendingRegistration;
                plan.flightNumber = pendingFlightNumber;
                plan.alternate = pendingAlternate;
            }

            Sim.Obj live = aircraft?.Source;
            main.SimCommand(sim =>
            {
                Sim.FlightPlan target = (live is Sim.Aircraft liveAircraft && sim.IsLive(liveAircraft)) ? liveAircraft.flightPlan : sim.userFlightPlan;
                target.callsign = plan.callsign;
                target.callsignSetByUser = plan.callsignSetByUser;
                target.icaoAirline = plan.icaoAirline;
                target.icaoType = plan.icaoType;
                target.departure = plan.departure;
                target.destination = plan.destination;
                target.rules = plan.rules;
                target.route = plan.route;
                target.remarks = plan.remarks;
                target.altitude = plan.altitude;
                target.registration = plan.registration;
                target.flightNumber = plan.flightNumber;
                target.alternate = plan.alternate;
            });
            // whoever opened the dialog sees the edited plan straight away
            this.plan = plan;
        }
    }
}
