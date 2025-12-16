using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class AtcForm : Form
    {
        Main main;

        /// <summary>
        /// Item in the list
        /// </summary>
        class Item
        {
            public Guid guid = Guid.Empty;

            public string callsign = "";
            public string nickname = "";
            public string frequency = "-";

            public Item(Guid guid, string callsign, string nickname, string frequency)
            {
                this.guid = guid;
                this.callsign = callsign;
                this.nickname = nickname;
                this.frequency = (frequency.Length < 4) ? "" : "1" + frequency.Substring(0, 2) + "." + frequency.Substring(2, 2);
            }

            public Item(Guid guid)
            {
                this.guid = guid;
            }
        };

        List<Item> itemList = [];

        /// <summary>
        /// Offsets
        /// </summary>
        int listHeightOffset = 300;
        int listWidthOffset = 100;

        /// <summary>
        /// default window title
        /// </summary>
        public string title = "";

        /// <summary>
        /// Time to reset refresh button
        /// </summary>
        double resetRefreshButtonTime = 0.0;
        const double RESET_REFRESH_BUTTON_DELAY = 30.0;

        /// <summary>
        /// Get the currently selected ATC
        /// </summary>
        /// <returns></returns>
        Item GetSelectedItem()
        {
            // selected item
            Item selectedItem = null;

            // check for selection
            if (DataGrid_AtcList.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_AtcList.SelectedRows[0].Index;

                // check index
                if (index >= 0 && index < itemList.Count)
                {
                    // get selected item
                    selectedItem = itemList[index];
                }
            }

            // check if no selected item
            if (selectedItem == null && itemList.Count > 0)
            {
                // select first item
                selectedItem = itemList[0];
            }

            // return selected item
            return selectedItem;
        }

        public AtcForm(Main main)
        {
            this.main = main;

            InitializeComponent();

            // calculate offsets
            listHeightOffset = Height - DataGrid_AtcList.Height;
            listWidthOffset = Width - DataGrid_AtcList.Width;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // save window title
            title = Text;

            // change font
            DataGrid_AtcList.DefaultCellStyle.Font = main.dataFont;
        }

        void AddAtc(Network.HubUser user)
        {
            // check for existing user
            if (itemList.Exists(u => u.guid.Equals(user.guid)) == false)
            {
                // add item
                itemList.Add(new Item(user.guid, user.flightPlan.callsign, user.nickname, user.frequency.ToString()));
            }
        }

        /// <summary>
        /// temporary user list
        /// </summary>
        List<Network.HubUser> tempHubUserList = [];

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
            // save guid
            Guid selectedGuid = (selectedItem != null) ? selectedItem.guid : Guid.Empty;

            // clear item list
            itemList.Clear();

            lock (main.conch)
            {
                // add user aircraft
                foreach (var user in main.network.localUserList)
                {
                    // check for ATC
                    if (user.atc)
                    {
                        // add to temporary list
                        tempHubUserList.Add(user);
                    }
                }

                // for each hub
                foreach (var hub in main.network.hubList)
                {
                    // check if hub is the one already connected to
                    if (hub.endPoint.Equals(main.network.joinEndPoint) == false)
                    {
                        // for each user in the hub
                        foreach (var user in hub.userList)
                        {
                            // check for ATC
                            if (user.atc)
                            {
                                // add to temporary list
                                tempHubUserList.Add(user);
                            }
                        }
                    }
                }

                // for each global user
                foreach (var user in tempHubUserList)
                {
                    // add atc
                    AddAtc(user);
                }

                // clear user list
                tempHubUserList.Clear();
            }

            // check which column was selected
            switch (Settings.Default.SortAtcColumn)
            {
                case 0:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.callsign.CompareTo(i2.callsign); });
                    break;
                case 1:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.nickname.Equals(i2.nickname) ? i1.callsign.CompareTo(i2.callsign) : i1.nickname.CompareTo(i2.nickname); });
                    break;
                case 2:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.frequency.Equals(i2.frequency) ? i1.callsign.CompareTo(i2.callsign) : i1.frequency.CompareTo(i2.frequency); });
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
                rows[index].CreateCells(DataGrid_AtcList);
                // fill row
                rows[index].Cells[0].Value = itemList[index].callsign;
                rows[index].Cells[1].Value = itemList[index].nickname;
                rows[index].Cells[2].Value = itemList[index].frequency;
            }

            // clear existing cells
            DataGrid_AtcList.Rows.Clear();
            DataGrid_AtcList.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_AtcList.Rows.Count; index++)
            {
                // check for selected hub
                if (itemList[index].guid.Equals(selectedGuid))
                {
                    // select row
                    DataGrid_AtcList.Rows[index].Selected = true;
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

        private void AtcForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.AtcFormLocation;
            Size size = Settings.Default.AtcFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.AtcFormLocation = Location;
                Settings.Default.AtcFormSize = Size;
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
        }

        private void AtcForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void DataGrid_AtcList_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check for header click
            if (e.RowIndex == -1)
            {
                Settings.Default.SortAtcColumn = e.ColumnIndex;
                RefreshWindow();
            }
        }

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void AtcForm_Activated(object sender, EventArgs e)
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

        private void AtcForm_Deactivate(object sender, EventArgs e)
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

        private void AtcForm_Resize(object sender, EventArgs e)
        {
            // size list
            DataGrid_AtcList.Height = Height - listHeightOffset;
            DataGrid_AtcList.Width = Width - listWidthOffset;
        }

        private void Context_ATC_Join_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for selection
            if (item != null)
            {
                // join address book entry
                main.Join(Network.UuidToString(Network.MakeUuid(item.guid)));
            }
        }

        private void AtcForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.AtcFormOpen = Visible;
        }

        private void AtcForm_ResizeEnd(object sender, EventArgs e)
        {
            // save form position
            Settings.Default.AtcFormLocation = Location;
            Settings.Default.AtcFormSize = Size;
        }
    }
}
