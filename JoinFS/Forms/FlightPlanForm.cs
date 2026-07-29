using System;
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

        // fields SimBrief can supply that this dialog doesn't have a visible control for -
        // carried through to plan on OK so they still reach the network/EuroScope
        string pendingRegistration;
        string pendingIcaoAirline;
        string pendingFlightNumber;
        string pendingAlternate;
        string pendingAltitude;

        public FlightPlanForm(Main main, Sim.FlightPlan plan)
        {
            InitializeComponent();

            this.main = main;
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
            Text_SimBriefUsername.Font = main.dataFont;

            if (Settings.Default.ToolTips)
            {
                ToolTip tip = new() { ShowAlways = true, IsBalloon = true, AutomaticDelay = 2000 };
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

                // carry through fields with no visible control, unchanged, unless an import replaces them
                pendingRegistration = plan.registration;
                pendingIcaoAirline = plan.icaoAirline;
                pendingFlightNumber = plan.flightNumber;
                pendingAlternate = plan.alternate;
                pendingAltitude = plan.altitude;
            }

            Text_SimBriefUsername.Text = Settings.Default.SimBriefUsername;

            if (FocusSimBriefUsername)
            {
                Text_SimBriefUsername.Focus();
            }
        }

        private async void Button_ImportSimBrief_Click(object sender, EventArgs e)
        {
            string username = Text_SimBriefUsername.Text.Trim();

            // remember the username regardless of fetch outcome
            Settings.Default.SimBriefUsername = username;
            Settings.Default.Save();

            Button_ImportSimBrief.Enabled = false;
            Label_SimBriefStatus.Text = "";
            try
            {
                Sim.FlightPlan imported = new();
                bool ok = await SimBrief.FetchAsync(username, imported, main);

                // the main-screen badge reflects the last fetch *attempt*, regardless of whether
                // the pilot goes on to commit it via OK - update it immediately, not just on OK
                main.sim.simBriefLastFetchSucceeded = ok;

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

                    pendingRegistration = imported.registration;
                    pendingAlternate = imported.alternate;
                    pendingAltitude = imported.altitude;

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

        private void Button_Clear_Click(object sender, EventArgs e)
        {
            // leaves callsign/type/rules alone (already sourced live from the sim) - only clears
            // the route-plan fields, same ones a SimBrief import would otherwise fill in
            Text_From.Text = "";
            Text_To.Text = "";
            Text_Route.Text = "";
            Text_Remarks.Text = "";
            pendingAlternate = "";
            pendingAltitude = "";
            Label_SimBriefStatus.Text = "";
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // return flight plan
                plan.callsign = Text_Callsign.Text;
                plan.icaoType = Text_Type.Text;
                plan.departure = Text_From.Text.Substring(0, Math.Min(4, Text_From.Text.Length)).ToUpperInvariant();
                plan.destination = Text_To.Text.Substring(0, Math.Min(4, Text_To.Text.Length)).ToUpperInvariant();
                plan.rules = Combo_Rules.Text;
                plan.route = Text_Route.Text.Substring(0, Math.Min(Sim.FlightPlan.MAX_ROUTE, Text_Route.Text.Length));
                plan.remarks = Text_Remarks.Text.Substring(0, Math.Min(Sim.FlightPlan.MAX_REMARKS, Text_Remarks.Text.Length));
                plan.registration = pendingRegistration;
                plan.icaoAirline = pendingIcaoAirline;
                plan.flightNumber = pendingFlightNumber;
                plan.alternate = pendingAlternate;
                plan.altitude = pendingAltitude;
            }
        }
    }
}
