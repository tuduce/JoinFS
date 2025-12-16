using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class AddressBookForm : Form
    {
        /// <summary>
        /// Reference to the main form
        /// </summary>
        Main main;

        /// <summary>
        /// Offsets
        /// </summary>
        int listHeightOffset = 300;
        int listWidthOffset = 100;

        /// <summary>
        /// Time to reset refresh button
        /// </summary>
        double resetRefreshButtonTime = 0.0;
        const double RESET_REFRESH_BUTTON_DELAY = 30.0;

        /// <summary>
        /// default window title
        /// </summary>
        string title = "";

        /// <summary>
        /// Constructor
        /// </summary>
        public AddressBookForm(Main main)
        {
            InitializeComponent();

            // set main form
            this.main = main;

            // calculate offsets
            listHeightOffset = Height - DataGrid_Entries.Height;
            listWidthOffset = Width - DataGrid_Entries.Width;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // save window title
            title = Text;

            // change font
            DataGrid_Entries.DefaultCellStyle.Font = main.dataFont;
        }

        /// <summary>
        /// Form items
        /// </summary>
        class Item
        {
            public string name = "";
            public string address = "";
            public string status = "";
            public bool online = false;

            public Item(string name, string address, string status, bool online)
            {
                this.name = name;
                this.address = address;
                this.status = status;
                this.online = online;
            }
        };

        /// <summary>
        /// Item list
        /// </summary>
        List<Item> itemList = [];

        /// <summary>
        /// Get the currently selected entry
        /// </summary>
        /// <returns></returns>
        Item GetSelectedItem()
        {
            // selected entry
            Item selectedItem = null;

            // check for selection
            if (DataGrid_Entries.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_Entries.SelectedRows[0].Index;

                // check index
                if (index >= 0 && index < itemList.Count)
                {
                    // get selected entry
                    selectedItem = itemList[index];
                }
            }

            // check if no selected entry
            if (selectedItem == null && itemList.Count > 0)
            {
                // select first entry
                selectedItem = itemList[0];
            }

            // return selected entry
            return selectedItem;
        }

        /// <summary>
        /// Refresher
        /// </summary>
        public Refresher refresher = new();

        /// <summary>
        /// Refresh form
        /// </summary>
        public void CheckRefresher()
        {
            // check for scheduled refresh
            if (refresher.Refresh())
            {
                // refresh
                RefreshWindow();
            }
        }

        /// <summary>
        /// Refresh window
        /// </summary>
        public void RefreshWindow()
        {
            // selected item
            Item selectedItem = GetSelectedItem();
            // get selected address
            string selectedAddress = (selectedItem != null) ? selectedItem.address : "";

            // clear item list
            itemList.Clear();

            lock (main.conch)
            {
                // for all entries
                foreach (var entry in main.addressBook.entries)
                {
                    // create item
                    Item item = new(entry.name, entry.address, entry.online ? "Online" : "-", entry.online);
                    // add to list
                    itemList.Add(item);
                }
            }

            // check which column was selected
            switch (Settings.Default.SortBookmarksColumn)
            {
                case 0:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.name.CompareTo(i2.name); });
                    break;
                case 2:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.online.Equals(i2.online) ? i1.name.CompareTo(i2.name) : -i1.online.CompareTo(i2.online); });
                    break;
            }

            // update window title
            Text = title + " (" + itemList.Count + ")";

            // rows
            DataGridViewRow[] rows = new DataGridViewRow[itemList.Count];
            // for each item
            for (int index = 0; index < itemList.Count; index++)
            {
                // create row
                rows[index] = new DataGridViewRow();
                rows[index].CreateCells(DataGrid_Entries);
                // fill row
                rows[index].Cells[0].Value = itemList[index].name;
                rows[index].Cells[1].Value = itemList[index].address;
                rows[index].Cells[2].Value = itemList[index].status;
            }

            // clear existing cells
            DataGrid_Entries.Rows.Clear();
            DataGrid_Entries.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_Entries.Rows.Count; index++)
            {
                // check if entry is online
                DataGrid_Entries.Rows[index].Cells[2].Style.BackColor = itemList[index].online ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
                DataGrid_Entries.Rows[index].Cells[2].Style.ForeColor = itemList[index].online ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;

                // check for selected entry
                if (itemList[index].address.Equals(selectedAddress))
                {
                    // select row
                    DataGrid_Entries.Rows[index].Selected = true;
                }
            }

            // reset refresh button
            Button_Refresh.BackColor = System.Drawing.SystemColors.ControlLight;
            // reset time
            resetRefreshButtonTime = main.ElapsedTime + RESET_REFRESH_BUTTON_DELAY;
        }

        /// <summary>
        /// process refresh button
        /// </summary>
        public void DoRefreshButton(bool force)
        {
            // check for reset
            if (force || main.ElapsedTime > resetRefreshButtonTime)
            {
                // check for auto refresh
                if (Settings.Default.AutoRefresh)
                {
                    main.addressBookForm.refresher.Schedule();
                }
                else
                {
                    // check if color requires changing
                    if (Button_Refresh.BackColor != System.Drawing.Color.Yellow)
                    {
                        // reset refresh button
                        Button_Refresh.BackColor = System.Drawing.Color.Yellow;
                    }
                }
            }
        }

        private void AddressBookForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.AddressBookFormLocation;
            Size size = Settings.Default.AddressBookFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.AddressBookFormLocation = Location;
                Settings.Default.AddressBookFormSize = Size;
            }
            else
            {
                // window area
                Rectangle rectangle = new(location, size);
                // is window hidden
                bool hidden = true;
                // for each screen
                foreach (Screen screen in Screen.AllScreens)
                {
                    // if screen does contain window
                    if (screen.WorkingArea.Contains(rectangle))
                    {
                        // not hidden
                        hidden = false;
                    }
                }

                // check if window is hidden
                if (hidden)
                {
                    // reload at default position
                    StartPosition = FormStartPosition.WindowsDefaultBounds;
                }
                else
                {
                    // restore position
                    StartPosition = FormStartPosition.Manual;
                    Location = location;
                    Size = size;
                }
            }

            main.addressBookForm.refresher.Schedule();
        }

        private void AddressBookForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void AddressBookForm_Resize(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // size list
                DataGrid_Entries.Height = Height - listHeightOffset;
                DataGrid_Entries.Width = Width - listWidthOffset;
            }
        }

        private void DataGrid_Entries_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check for header click
            if (e.RowIndex == -1)
            {
                Settings.Default.SortBookmarksColumn = e.ColumnIndex;
                main.addressBookForm ?. refresher.Schedule();
            }
        }

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            main.addressBookForm ?. refresher.Schedule();
        }

        private void AddressBookForm_Activated(object sender, EventArgs e)
        {
            // check always on top
            if (Settings.Default.AlwaysOnTop)
            {
                TopMost = true;
            }
            else
            {
                TopMost = false;
            }
        }

        private void AddressBookForm_Deactivate(object sender, EventArgs e)
        {
            // check always on top
            if (Settings.Default.AlwaysOnTop)
            {
                TopMost = true;
                Activate();
            }
            else
            {
                TopMost = false;
            }
        }

        private void Context_Entry_Opening(object sender, CancelEventArgs e)
        {
            lock (main.conch)
            {
                // reset buttons
                bool editEnabled = false;
                bool removeEnabled = false;
                bool joinEnabled = false;

                // get selected item
                Item item = GetSelectedItem();

                // check for selected item
                if (item != null)
                {
                    // allow buttons
                    editEnabled = true;
                    removeEnabled = true;
                    joinEnabled = true;
                }

                // update buttons
                if (Context_Entry_Edit.Enabled != editEnabled)
                {
                    Context_Entry_Edit.Enabled = editEnabled;
                }
                if (Context_Entry_Remove.Enabled != removeEnabled)
                {
                    Context_Entry_Remove.Enabled = removeEnabled;
                }
                if (Context_Entry_Join.Enabled != joinEnabled)
                {
                    Context_Entry_Join.Enabled = joinEnabled;
                }
            }
        }

        private void Context_Entry_Add_Click(object sender, EventArgs e)
        {
            // show entry form
            AddressForm addressForm = new(main, "", "");
            // check result
            if (addressForm.ShowDialog() == DialogResult.OK && addressForm.CheckEntry())
            {
                // failed to add
                bool failed = false;

                lock (main.conch)
                {
                    // create new entry
                    AddressBook.AddressBookEntry entry = new()
                    {
                        name = addressForm.name,
                        address = addressForm.address
                    };
                    // check for valid data
                    if (entry.name.Length > 0 && entry.address.Length > 0)
                    {
#if NO_HUBS
                        // attempt to make end point
                        main.network.MakeEndPoint(Network.DecodeIP(entry.address), Network.DEFAULT_PORT, out entry.endPoint);
                        // make sure that entry is not already present
                        if (main.addressBook.entries.Find(f => f.endPoint.Equals(entry.endPoint)) != null)
                        {
                            // already exists
                            failed = true;
                        }
                        else
                        {
                            // add entry
                            main.addressBook.entries.Add(entry);
                            // save list
                            main.addressBook.Save();
                        }
#else
                        // make sure that entry is not already present
                        if (main.addressBook.entries.Find(f => f.address == entry.address) != null)
                        {
                            // already exists
                            failed = true;
                        }
                        else
                        {
                            // make uuid
                            entry.uuid = Network.MakeUuid(entry.address);
                            // check if not uuid
                            if (entry.uuid == 0 && entry.address.Contains("."))
                            {
                                // attempt to make end point
                                main.network.MakeEndPoint(entry.address, Network.DEFAULT_PORT, out entry.endPoint);
                            }
                            // add entry
                            main.addressBook.entries.Add(entry);
                            // save list
                            main.addressBook.Save();
                        }
#endif
                    }
                }

                // check if failed to add
                if (failed)
                {
                    // error
                    MessageBox.Show("Already in the address book.", Main.Name + ": Add Address Book Entry");
                }

                // refresh
                main.addressBookForm ?. refresher.Schedule();
                main.sessionForm ?. usersRefresher.Schedule();
                main.hubsForm ?. refresher.Schedule();
                main.mainForm ?. RefreshComboList();
            }
        }

        private void Context_Entry_Edit_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for selection
            if (item != null)
            {
                // show entry form
                AddressForm addressForm = new(main, item.name, item.address);
                // check result
                if (addressForm.ShowDialog() == DialogResult.OK && addressForm.CheckEntry())
                {
                    // failed to add
                    bool failed = false;

                    lock (main.conch)
                    {
                        // find entry
                        AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.address.Equals(item.address));
                        // check if entry found
                        if (entry != null)
                        {
#if NO_HUBS
                            // attempt to make end point
                            main.network.MakeEndPoint(Network.DecodeIP(addressForm.address), Network.DEFAULT_PORT, out IPEndPoint endPoint);
                            // make sure that entry is not already present
                            if (main.addressBook.entries.Find(f => f.endPoint.Equals(endPoint)) != null)
                            {
                                // already exists
                                failed = true;
                            }
                            else
                            {
                                // update entry
                                entry.name = addressForm.name;
                                entry.address = addressForm.address;
                                entry.endPoint = endPoint;
                                // save list
                                main.addressBook.Save();
                            }
#else
                            // make sure that entry is not already present
                            if (main.addressBook.entries.Find(f => f.address == addressForm.address) != null)
                            {
                                // already exists
                                failed = true;
                            }
                            else
                            {
                                // update entry
                                entry.name = addressForm.name;
                                entry.address = addressForm.address;
                                // make uuid
                                entry.uuid = Network.MakeUuid(entry.address);
                                // check if not uuid
                                if (entry.uuid == 0 && entry.address.Contains("."))
                                {
                                    // attempt to make end point
                                    main.network.MakeEndPoint(entry.address, Network.DEFAULT_PORT, out entry.endPoint);
                                }
                            }
#endif

                            // save list
                            main.addressBook.Save();
                        }
                    }
                    // check if failed to add
                    if (failed)
                    {
                        // error
                        MessageBox.Show("Already in the address book.", Main.Name + ": Edit Address Book Entry");
                    }
                    // refresh
                    main.addressBookForm ?. refresher.Schedule();
                    main.sessionForm ?. usersRefresher.Schedule();
                    main.hubsForm ?. refresher.Schedule();
                    main.mainForm ?. RefreshComboList();
                }
            }
        }

        private void Context_Entry_Remove_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for selection
            if (item != null)
            {
                // confirm
                DialogResult result = MessageBox.Show("Remove '" + item.name + "' from the list?", Main.Name + ": Address Book", MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    lock (main.conch)
                    {
                        // find entry
                        AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.address.Equals(item.address));
                        // check if entry found
                        if (entry != null)
                        {
                            // remove from list
                            main.addressBook.entries.Remove(entry);
                            // save list
                            main.addressBook.Save();
                        }
                    }
                    // refresh
                    main.addressBookForm ?. refresher.Schedule();
                    main.sessionForm ?. usersRefresher.Schedule();
                    main.hubsForm ?. refresher.Schedule();
                   main.mainForm ?. RefreshComboList();
                }
            }
        }

        private void Context_Entry_Join_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for selection
            if (item != null)
            {
                // join address
#if NO_HUBS
                main.Join(Network.DecodeIP(item.address));
#else
                main.Join(item.address);
#endif
            }
        }

        private void AddressBookForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.BookmarksFormOpen = Visible;
        }

        private void AddressBookForm_ResizeEnd(object sender, EventArgs e)
        {
            // save form position
            Settings.Default.BookmarksFormLocation = Location;
            Settings.Default.BookmarksFormSize = Size;
        }
    }
}
