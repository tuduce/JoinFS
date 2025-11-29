using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class CallsignForm : Form
    {
        Main main;

        /// <summary>
        /// return callsign
        /// </summary>
        public string callsign = "";

        public CallsignForm(Main main, string model, string original)
        {
            this.main = main;

            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");
            // set model
            Text_Model.Text = model;
            // set original
            Text_Original.Text = original;

            // change font
            Text_Model.Font = main.dataFont;
            Text_Original.Font = main.dataFont;
            Text_Callsign.Font = main.dataFont;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // return callsign
            callsign = Text_Callsign.Text.TrimStart(' ').TrimEnd(' ');
        }
    }
}
