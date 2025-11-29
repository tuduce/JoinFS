using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class NicknameForm : Form
    {
        // return nickname
        public string nickname = "";

        public NicknameForm(Main main)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // set font
            Text_Nickname.Font = main.dataFont;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // return nickname
            nickname = Text_Nickname.Text;
        }
    }
}
