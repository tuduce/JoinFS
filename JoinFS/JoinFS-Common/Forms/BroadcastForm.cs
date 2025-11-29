using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class BroadcastForm : Form
    {
        string model;
        bool group;

        public bool broadcastObject = false;
        public bool broadcastModel = false;
        public bool broadcastTacpack = false;
        public bool broadcastEverything = false;

        public BroadcastForm(Main main, string model, bool group, bool broadcastObj, bool broadcastModel, bool broadcastTacpack, bool broadcastEverything)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            this.model = model;
            this.group = group;
            this.broadcastObject = broadcastObj;
            this.broadcastModel = broadcastModel;
            this.broadcastTacpack = broadcastTacpack;
            this.broadcastEverything = broadcastEverything;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // update flags
            broadcastObject = Check_Object.Checked;
            broadcastModel = Check_Model.Checked;
            broadcastTacpack = Check_Tacpack.Checked;
            broadcastEverything = Check_Everything.Checked;
        }

        private void BroadcastForm_Load(object sender, EventArgs e)
        {
            // set mode name
            Check_Model.Text = "All '" + model + "'";

            // check if group selection
            if (group)
            {
                // disable object broadcast
                Check_Object.Enabled = false;
            }
            else
            {
                // initialize option
                Check_Object.Checked = broadcastObject;
            }

            // initialize options
            Check_Model.Checked = broadcastModel;
            Check_Tacpack.Checked = broadcastTacpack;
            Check_Everything.Checked = broadcastEverything;
        }
    }
}
