using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Globalization;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class HubsForm : Form
    {
        /// <summary>
        /// Main form
        /// </summary>
        Main main;

        /// <summary>
        /// Item
        /// </summary>
        class Item
        {
            public string name;
            public Guid guid;
            public LocalNode.Nuid nuid;
            public string status;
            public bool online;
            public string addressText;
            public IPEndPoint endPoint;
            public int users;
            public int aircraft;
            public string atcAirport;
            public string nextEvent;
            public string voip;
            public string about;
            public bool save;
            public bool ignore;
            public string version;
            public int versionValue;

            public Item(string name, Guid guid, LocalNode.Nuid nuid, string status, bool online, string addressText, IPEndPoint endPoint, int users, int aircraft, string atcAirport, string nextEvent, string voip, string about, bool save, bool ignore, string version)
            {
                this.name = name;
                this.guid = guid;
                this.nuid = nuid;
                this.status = status;
                this.online = online;
                this.addressText = addressText;
                this.endPoint = endPoint;
                this.users = users;
                this.aircraft = aircraft;
                this.atcAirport = atcAirport;
                this.nextEvent = nextEvent;
                this.voip = voip;
                this.about = about;
                this.save = save;
                this.ignore = ignore;
                this.version = version;
                versionValue = 0;
                string[] parts = version.Split('.');
                if (parts.Length == 3)
                {
                    if (int.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out int n0) 
                        && int.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out int n1) 
                        && int.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out int n2))
                    {
                        versionValue = (n0 << 16) + (n1 << 8) + n2;
                    }
                }
            }
        };

        /// <summary>
        /// List of items
        /// </summary>
        List<Item> itemList = new List<Item>();

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
        public string title = "";

        /// <summary>
        /// Get the currently selected item
        /// </summary>
        /// <returns></returns>
        Item GetSelectedItem()
        {
            // selected item
            Item selectedItem = null;

            // check for selection
            if (DataGrid_HubList.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_HubList.SelectedRows[0].Index;

                // check index
                if (index >= 0 && index < itemList.Count)
                {
                    // get selected hub
                    selectedItem = itemList[index];
                }
            }

            // check if no selected hub
            if (selectedItem == null && itemList.Count > 0)
            {
                // select first hub
                selectedItem = itemList[0];
            }

            // return selected hub
            return selectedItem;
        }

        /// <summary>
        /// Refresh hub details
        /// </summary>
        /// <param name="hub"></param>
        void RefreshDetails(Item item)
        {
            // clear details
            DataGrid_Hub.Rows.Clear();

            // check for valid item
            if (item != null && item.endPoint != null)
            {
                // add row
                DataGrid_Hub.Rows.Add(item.about, item.voip, item.nextEvent);
            }
            else if (main.settingsHub)
            {
                // add row
                DataGrid_Hub.Rows.Add(main.settingsHubAbout, main.settingsHubVoip, main.settingsHubEvent);
            }

            // check for details
            if (DataGrid_Hub.Rows.Count > 0 && DataGrid_HubList.Rows.Count > 0)
            {
                // check for URL in about
                if (MainForm.CheckUrl(DataGrid_Hub.Rows[0].Cells[0].Value.ToString()))
                {
                    // change to link text
                    DataGrid_Hub.Rows[0].Cells[0].Style.Font = new Font(DataGrid_HubList.Rows[0].Cells[0].InheritedStyle.Font, FontStyle.Underline);
                    DataGrid_Hub.Rows[0].Cells[0].Style.ForeColor = System.Drawing.Color.DodgerBlue;
                }

                // check for URL in event
                if (MainForm.CheckUrl(DataGrid_Hub.Rows[0].Cells[1].Value.ToString()))
                {
                    // change to link text
                    DataGrid_Hub.Rows[0].Cells[1].Style.Font = new Font(DataGrid_HubList.Rows[0].Cells[0].InheritedStyle.Font, FontStyle.Underline);
                    DataGrid_Hub.Rows[0].Cells[1].Style.ForeColor = System.Drawing.Color.DodgerBlue;
                }

                // check for URL in event
                if (MainForm.CheckUrl(DataGrid_Hub.Rows[0].Cells[2].Value.ToString()))
                {
                    // change to link text
                    DataGrid_Hub.Rows[0].Cells[2].Style.Font = new Font(DataGrid_HubList.Rows[0].Cells[0].InheritedStyle.Font, FontStyle.Underline);
                    DataGrid_Hub.Rows[0].Cells[2].Style.ForeColor = System.Drawing.Color.DodgerBlue;
                }
            }

            // deselect
            DataGrid_Hub.ClearSelection();
        }

        /// <summary>
        /// Refresher
        /// </summary>
        public Refresher refresher = new Refresher();

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
            // save nuid
            LocalNode.Nuid selectedNuid = (selectedItem != null) ? selectedItem.nuid : new LocalNode.Nuid();

            // clear list
            itemList.Clear();

            lock (main.conch)
            {
                // check for this hub
                if (main.settingsHub)
                {
                    // count of aircraft
                    ushort aircraft = 0;

                    // check for sim
                    if (main.sim != null)
                    {
                        // for each object
                        foreach (var obj in main.sim.objectList)
                        {
                            // check for network object
                            if (main.sim.IsBroadcast(obj) || obj.owner == Sim.Obj.Owner.Network)
                            {
                                // accumulate counts
                                if (obj is Sim.Aircraft)
                                {
                                    aircraft++;
                                }
                            }
                        }
                    }

                    // get main ATC
                    int atcCount = main.network.GetMainAtc(out string atcAirport, out int atcLevel);

                    // status
                    string status = main.network.localNode.Password ? Resources.strings.Password : main.network.localNode.GlobalSession ? "Global" : "Online";

                    // add row
                    Item item = new Item(main.settingsHubName, main.guid, main.network.localNode.GetLocalNuid(), status, true, "", null, main.network.localUserList.Count, aircraft, atcAirport, main.settingsHubEvent, main.settingsHubVoip, main.settingsHubAbout, false, false, Main.version);
                    itemList.Add(item);
                }

                // for each hub
                foreach (var hub in main.network.hubList)
                {
                    // check for valid hub 
                    if (hub.nuid.Valid())
                    {
                        // check for show ignored hubs
                        if (Settings.Default.ListIgnoredHubs || main.log.IgnoreNode(hub.endPoint.Address) == false && main.log.IgnoreNode(ref hub.guid) == false)
                        {
                            // check for showing empty hubs
                            if (Settings.Default.ListOfflineHubs || hub.online)
                            {
                                // status
                                string status = "-";
                                if (hub.online)
                                {
                                    status = hub.password ? Resources.strings.Password : hub.globalSession ? "Global" : "Online";
                                }

                                // get address book state
                                bool entry = main.addressBook.entries.Find(f => f.endPoint.Address.Equals(hub.endPoint.Address)) != null;
                                // ignore hub
                                bool ignore = main.log.IgnoreNode(ref hub.guid) || main.log.IgnoreNode(hub.endPoint.Address);

                                // add row
                                Item item = new Item(hub.name, hub.guid, hub.nuid, status, hub.online, hub.addressText, hub.endPoint, hub.users, hub.planes + hub.helicopters, hub.atcAirport, hub.nextEvent, hub.voip, hub.about, entry, ignore, hub.appVersion);
                                itemList.Add(item);
                            }
                        }
                    }
                }
            }

            // check which column was selected
            switch (Settings.Default.SortHubsColumn)
            {
                case 0:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.name.CompareTo(i2.name); });
                    break;
                case 1:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.status.Equals(i2.status) ? i1.name.CompareTo(i2.name) : i1.status.CompareTo(i2.status); });
                    break;
                case 2:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.users.Equals(i2.users) ? i1.name.CompareTo(i2.name) : -i1.users.CompareTo(i2.users); });
                    break;
                case 3:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.aircraft.Equals(i2.aircraft) ? i1.name.CompareTo(i2.name) : -i1.aircraft.CompareTo(i2.aircraft); });
                    break;
                case 4:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.atcAirport.Length.Equals(i2.atcAirport.Length) ? i1.name.CompareTo(i2.name) : -i1.atcAirport.Length.CompareTo(i2.atcAirport.Length); });
                    break;
                case 5:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.save.Equals(i2.save) ? i1.name.CompareTo(i2.name) : -i1.save.CompareTo(i2.save); });
                    break;
                case 6:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.ignore.Equals(i2.ignore) ? i1.name.CompareTo(i2.name) : -i1.ignore.CompareTo(i2.ignore); });
                    break;
                case 7:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.versionValue.Equals(i2.versionValue) ? i1.name.CompareTo(i2.name) : -i1.versionValue.CompareTo(i2.versionValue); });
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
                rows[index].CreateCells(DataGrid_HubList);
                // fill row
                rows[index].Cells[0].Value = itemList[index].name;
                rows[index].Cells[1].Value = itemList[index].status;
                rows[index].Cells[2].Value = itemList[index].users;
                rows[index].Cells[3].Value = itemList[index].aircraft;
                rows[index].Cells[4].Value = itemList[index].atcAirport;
                rows[index].Cells[5].Value = itemList[index].save;
                rows[index].Cells[6].Value = itemList[index].ignore;
                rows[index].Cells[7].Value = itemList[index].version;
            }

            // update rows
            DataGrid_HubList.Rows.Clear();
            DataGrid_HubList.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_HubList.Rows.Count; index++)
            {
                // check if hub is online
                DataGrid_HubList.Rows[index].Cells[1].Style.BackColor = (itemList[index] == null || itemList[index].online) ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
                DataGrid_HubList.Rows[index].Cells[1].Style.ForeColor = (itemList[index] == null || itemList[index].online) ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;

                // check for selected hub
                if (itemList[index].nuid.Equals(selectedNuid))
                {
                    // select row
                    DataGrid_HubList.Rows[index].Selected = true;
                }
            }

            // refresh hub details
            RefreshDetails(GetSelectedItem());

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
                    RefreshWindow();
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

        private void HubsForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.HubsFormLocation;
            Size size = Settings.Default.HubsFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.HubsFormLocation = Location;
                Settings.Default.HubsFormSize = Size;
            }
            else
            {
                // window area
                Rectangle rectangle = new Rectangle(location, size);
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

            RefreshWindow();
        }

        private void HubsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void DataGrid_HubList_SelectionChanged(object sender, EventArgs e)
        {
            RefreshDetails(GetSelectedItem());
        }

        private void DataGrid_HubList_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check for header click
            if (e.RowIndex == -1)
            {
                Settings.Default.SortHubsColumn = e.ColumnIndex;
                RefreshWindow();
            }
            else
            {
                // get selected item
                Item item = GetSelectedItem();

                // check for selection
                if (item == null)
                {
                    // check which column was selected
                    switch (e.ColumnIndex)
                    {
                        case 4:
                            MessageBox.Show("You can only ignore other connected users.", Main.name + ": Users");
                            break;
                    }
                }
                else
                {
                    // check which column was selected
                    switch (e.ColumnIndex)
                    {
                        case 5:
                            {
                                // find address book entry
                                AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.uuid == Network.MakeUuid(item.guid));

                                // check for existing entry
                                if (entry != null)
                                {
                                    // confirm
                                    DialogResult result = MessageBox.Show("Remove '" + entry.name + "' from address book?", Main.name + ": Address Book", MessageBoxButtons.YesNo);
                                    if (result == DialogResult.Yes)
                                    {
                                        lock (main.conch)
                                        {
                                            // remove from list
                                            main.addressBook.entries.Remove(entry);
                                            // save list
                                            main.addressBook.Save();
                                        }
                                        // refresh details
                                        RefreshWindow();
                                        main.addressBookForm ?. refresher.Schedule();
                                        // update combo list
                                        main.mainForm ?. RefreshComboList();
                                    }
                                }
                                else
                                {
                                    lock (main.conch)
                                    {
                                        entry = new AddressBook.AddressBookEntry
                                        {
                                            name = item.name
                                        };

                                        // check for valid data
                                        if (entry.name.Length > 0)
                                        {
                                            // set uuid
                                            entry.uuid = Network.MakeUuid(item.guid);
                                            // set address
                                            entry.address = Network.UuidToString(entry.uuid);
                                            // check for end point
                                            if (item.endPoint != null)
                                            {
                                                // set end point
                                                entry.endPoint = item.endPoint;
                                            }
                                            // add entry
                                            main.addressBook.entries.Add(entry);
                                            // save list
                                            main.addressBook.Save();
                                        }
                                    }
                                    // refresh details
                                    RefreshWindow();
                                    main.addressBookForm ?. refresher.Schedule();
                                    // update combo list
                                    main.mainForm ?. RefreshComboList();
                                }
                            }
                            break;

                        case 6:
                            {
                                if (item.endPoint == null)
                                {
                                    MessageBox.Show("You can not ignore this hub", Main.name + ": Hubs");
                                }
                                else
                                {
                                    bool ignoreGuid;
                                    bool ignoreAddress;
                                    lock (main.conch)
                                    {
                                        ignoreGuid = main.log.IgnoreNode(ref item.guid);
                                        ignoreAddress = main.log.IgnoreNode(item.endPoint.Address);
                                    }
                                    // check if currently ignored
                                    if (ignoreGuid || ignoreAddress)
                                    {
                                        lock (main.conch)
                                        {
                                            // stop ignore
                                            main.log.RemoveIgnoreNode(ref item.guid);
                                            main.log.RemoveIgnoreNode(item.endPoint.Address);
                                        }
                                    }
                                    else
                                    {
                                        // confirm
                                        DialogResult result = MessageBox.Show("Ignore '" + item.name + "'?", Main.name + ": Hubs", MessageBoxButtons.YesNo);
                                        if (result == DialogResult.Yes)
                                        {
                                            lock (main.conch)
                                            {
                                                main.log.AddIgnoreNode(ref item.guid);
                                                main.log.AddIgnoreNode(item.endPoint.Address);
                                            }
                                        }
                                    }
                                    // refresh window
                                    RefreshWindow();
                                }
                            }
                            break;
                    }
                }
            }
        }

        private void DataGrid_Hub_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();

            // check for selection
            if (item != null)
            {
                // check which column was selected
                switch (e.ColumnIndex)
                {
                    case 0:
                        {
                            // open url
                            MainForm.OpenUrl(item.about);
                        }
                        break;

                    case 1:
                        {
                            // open url
                            MainForm.OpenUrl(item.voip);
                        }
                        break;

                    case 2:
                        {
                            // open url
                            MainForm.OpenUrl(item.nextEvent);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm"></param>
        public HubsForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // calculate offsets
            listHeightOffset = Height - DataGrid_HubList.Height;
            listWidthOffset = Width - DataGrid_HubList.Width;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // save window title
            title = Text;

            // change font
            DataGrid_HubList.DefaultCellStyle.Font = main.dataFont;
            DataGrid_Hub.DefaultCellStyle.Font = main.dataFont;
        }

        private void HubsForm_Resize(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // size detail
                DataGrid_Hub.Width = Width - listWidthOffset;

                // size list
                DataGrid_HubList.Height = Height - listHeightOffset;
                DataGrid_HubList.Width = Width - listWidthOffset;
            }
        }

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void HubsForm_Activated(object sender, EventArgs e)
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

        private void HubsForm_Deactivate(object sender, EventArgs e)
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

        private void Context_Hub_Opening(object sender, CancelEventArgs e)
        {
            // disable all options
            Context_Hub_Join.Enabled = false;

            // get selected item
            Item item = GetSelectedItem();

            lock (main.conch)
            {
                // check for selected item
                if (item != null)
                {
                    // check if online
                    if (item.online)
                    {
                        // allow model change
                        Context_Hub_Join.Enabled = true;
                    }
                }
            }

            // get list offline hubs
            Context_Hub_Offline.CheckState = Settings.Default.ListOfflineHubs ? CheckState.Checked : CheckState.Unchecked;
            // get list ignored hubs
            Context_Hub_Ignored.CheckState = Settings.Default.ListIgnoredHubs ? CheckState.Checked : CheckState.Unchecked;
        }

        private void Context_Hub_Join_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for hub
            if (item != null && item.endPoint != null)
            {
                // join hub
                main.Join(item.endPoint.ToString());
            }
        }

        private void Context_Hub_Offline_Click(object sender, EventArgs e)
        {
            // update settings
            Settings.Default.ListOfflineHubs = Context_Hub_Offline.CheckState != CheckState.Checked;
            RefreshWindow();
        }

        private void Context_Hub_Ignored_Click(object sender, EventArgs e)
        {
            // update settings
            Settings.Default.ListIgnoredHubs = Context_Hub_Ignored.CheckState != CheckState.Checked;
            RefreshWindow();
        }

        private void HubsForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.HubsFormOpen = Visible;
        }

        private void HubsForm_ResizeEnd(object sender, EventArgs e)
        {
            if (main != null)
            {
                // save form position
                Settings.Default.HubsFormLocation = Location;
                Settings.Default.HubsFormSize = Size;
            }
        }
    }
}
