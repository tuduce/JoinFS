using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class LoginForm : Form
    {
        // user credentials
        public string email = "";
        public string password = "";

        Main main;

        public LoginForm(Main main, string email, string password, bool verify)
        {
            InitializeComponent();

            this.main = main;

            // change icon
            Icon = main.icon;
            // change font
            Text_Email.Font = main.dataFont;
            Text_Password.Font = main.dataFont;

            // check for verify
            if (verify)
            {
                // change title
                Text = Resources.strings.VerifyPassword;
                // disable email
                Text_Email.Enabled = false;
                // verify label
                Label_Verify.Text = Resources.strings.VerifyPassword;
            }
            else
            {
                // hide verify label
                Label_Verify.Visible = false;
            }

            // set email and password
            Text_Email.Text = email;
            Text_Password.Text = password;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // return credentials
            email = Text_Email.Text.TrimStart(' ').TrimEnd(' ');
            password = Text_Password.Text.TrimStart(' ').TrimEnd(' ');
        }
    }
}
