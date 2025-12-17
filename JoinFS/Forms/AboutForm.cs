using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class AboutForm : Form
    {
        public AboutForm(Main main)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title and add name
            Text = Text.Replace("JoinFS: ", "") + " " + Main.Name;
        }

        private void Label_Website_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            //string sc = Program.Code("https://joinfs.net", true, 1234);
            //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
            string url = "https://github.com/tuduce/JoinFS/releases";
            // send the users to the releases page of the github repo
            Main.Launch(url);
        }

        private void Box_Powered_Click(object sender, EventArgs e)
        {
            //string sc = Program.Code("https://joinfs.net", true, 1234);
            //string sc = Program.Code("https://github.com/tuduce/JoinFS/releases", true, 1234);
            string url = "https://github.com/tuduce/JoinFS/releases";
            // send the users to the releases page of the github repo
            Main.Launch(url);
        }
    }
}
