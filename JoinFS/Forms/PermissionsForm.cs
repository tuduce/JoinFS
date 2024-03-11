using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class PermissionsForm : Form
    {
        // flags
        public bool shareCockpit;
        public bool shareFlight;
        public bool shareEngine;
        public bool shareOther;
        public bool multiple;

        public PermissionsForm(Main main, bool cockpit, bool flight, bool engine, bool other, bool multiple)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            shareCockpit = cockpit;
            shareFlight = flight;
            shareEngine = engine;
            shareOther = other;
            this.multiple = multiple;
        }

        void RefreshWindow()
        {
            // check if cockpit is shared
            if (Check_Cockpit.Checked)
            {
                Check_Flight.Enabled = true;
            }
            else
            {
                Check_Flight.Enabled = false;
            }
        }

        private void Check_Cockpit_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // update flags
            shareCockpit = Check_Cockpit.Checked;
            shareFlight = Check_Flight.Checked;
            multiple = Check_Multiple.Checked;
        }

        private void PermissionsForm_Load(object sender, EventArgs e)
        {
            // set flags
            Check_Cockpit.Checked = shareCockpit;
            Check_Flight.Checked = shareFlight;
            Check_Multiple.Checked = multiple;

            RefreshWindow();
        }
    }
}
