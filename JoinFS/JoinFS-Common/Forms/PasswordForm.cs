using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class PasswordForm : Form
    {
        /// <summary>
        /// return password
        /// </summary>
        public string password = "";

        public PasswordForm(Main main)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // change font
            Text_Password.Font = main.dataFont;
        }

        private void Button_Join_Click(object sender, EventArgs e)
        {
            // save password
            password = Text_Password.Text;
        }
    }
}
