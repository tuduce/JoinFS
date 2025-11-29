using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class FlightPlanForm : Form
    {
        public Sim.FlightPlan plan;

        Main main;

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
                Text_From.Text = plan.departure;
                Text_To.Text = plan.destination;
                Combo_Rules.Items.Add("VFR");
                Combo_Rules.Items.Add("IFR");
                Combo_Rules.SelectedIndex = plan.rules == "IFR" ? 1 : 0;
                Text_Route.Text = plan.route;
                Text_Remarks.Text = plan.remarks;
            }
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // return flight plan
                plan.icaoType = Text_Type.Text;
                plan.departure = Text_From.Text.Substring(0, Math.Min(4, Text_From.Text.Length));
                plan.destination = Text_To.Text.Substring(0, Math.Min(4, Text_To.Text.Length));
                plan.rules = Combo_Rules.Text;
                plan.route = Text_Route.Text.Substring(0, Math.Min(Sim.FlightPlan.MAX_ROUTE, Text_Route.Text.Length));
                plan.remarks = Text_Remarks.Text.Substring(0, Math.Min(Sim.FlightPlan.MAX_REMARKS, Text_Remarks.Text.Length));
            }
        }
    }
}
