using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class AddressForm : Form
    {
        /// <summary>
        /// Data entry
        /// </summary>
        public string name = "";
        public string address = "";

        public AddressForm(Main main, string name, string address)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // initialize
            Text_Name.Text = name;
            Text_Address.Text = address;
            // change font
            Text_Name.Font = main.dataFont;
            Text_Address.Font = main.dataFont;
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // return data
            name = Text_Name.Text.TrimStart(' ').TrimEnd(' ');
            address = Text_Address.Text.TrimStart(' ').TrimEnd(' ');
        }

        public bool CheckEntry()
        {
            // create new book mark
            AddressBook.AddressBookEntry entry = new();

            // check name
            if (name.Length != 0)
            {
                if (name.Contains("="))
                {
                    MessageBox.Show("Names cannot contain an '=' character", Main.Name + ": Address Book");
                    return false;
                }
            }
            else
            {
                MessageBox.Show("An empty name is invalid", Main.Name + ": Address Book");
                return false;
            }

            // check address
            if (address.Length == 0)
            {
                MessageBox.Show("An empty address is invalid.", Main.Name + ": Address Book");
                return false;
            }

            // success
            return true;
        }
    }
}
