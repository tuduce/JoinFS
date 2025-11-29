using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class ShortcutForm : Form
    {
        // key combination
        public string combination;

        public ShortcutForm(Main main)
        {
            combination = "";

            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            Text_Shortcut.Font = main.dataFont;
        }

        private void ShortcutForm_KeyDown(object sender, KeyEventArgs e)
        {
            combination = "";
            if (e.Control) combination += "CTRL+";
            if (e.Shift) combination += "SHIFT+";
            if (e.Alt) combination += "ALT+";

            // check for letter
            if ((int)e.KeyCode >= 65 && (int)e.KeyCode <= 90)
            {
                combination += (char)e.KeyCode;

                // update window
                Text_Shortcut.Text = combination;
            }
        }

        private void ShortcutForm_Load(object sender, EventArgs e)
        {
            // set the shortcut
            Text_Shortcut.Text = combination;
        }
    }
}
