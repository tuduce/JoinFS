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
    public partial class SessionForm : Form
    {
        Main main;

        /// <summary>
        /// Item
        /// </summary>
        class Item
        {
            public Guid guid;
            public LocalNode.Nuid nuid;
            public IPEndPoint endPoint;
            public string nickname;
            public string version;
            public string simulator;
            public string callsign;
            public int aircraftCount;
            public int objectCount;
            public string share;
            public bool save;
            public bool ignore;
            public int port;
            public string portText;
            public bool receiveEstablished;
            public bool sendEstablished;
            public string connected;
            public bool direct;
            public float latency;
            public string latencyText;
            public int versionValue;

            public Item(Guid guid, LocalNode.Nuid nuid, IPEndPoint endPoint, string nickname, string version, string simulator, string callsign, int aircraftCount, int objectCount, string share, bool save, bool ignore, int port, bool receiveEstablished, bool sendEstablished, bool direct, float latency)
            {
                this.guid = guid;
                this.nuid = nuid;
                this.endPoint = endPoint;
                this.nickname = nickname;
                this.version = version;
                this.simulator = simulator;
                this.callsign = callsign;
                this.aircraftCount = aircraftCount;
                this.objectCount = objectCount;
                this.share = share;
                this.save = save;
                this.ignore = ignore;
                this.port = port;
                portText = port.ToString();
                this.receiveEstablished = receiveEstablished;
                this.sendEstablished = sendEstablished;
                this.direct = direct;
                connected = "No";
                if (sendEstablished) connected = direct ? "Yes" : "Route";
                this.latency = latency;
                latencyText = (latency * 1000.0f).ToString("N0") + " ms";
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
        }

        /// <summary>
        /// List of items
        /// </summary>
        List<Item> itemList = new List<Item>();

        // send timer
        System.Windows.Forms.Timer sendTimer = new System.Windows.Forms.Timer();

        /// <summary>
        /// Offsets
        /// </summary>
        int listHeightOffset = 300;
        int listWidthOffset = 100;
        float listScale = 0.5f;
        int receiveWidthOffset;
        int receiveHeightOffset;
        int transmitWidthOffset;

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
        /// Line of received text
        /// </summary>
        class Line
        {
            public double time;
            public string text;
            public Color colour;

            /// <summary>
            /// constructor
            /// </summary>
            public Line(double time, Color colour, string nickname, string callsign, string text)
            {
                this.time = time;
                this.text = nickname + " : " + callsign + " : " + text;
                this.colour = colour;
            }

            /// <summary>
            /// constructor
            /// </summary>
            public Line(double time, Color colour, string text)
            {
                this.time = time;
                this.text = text;
                this.colour = colour;
            }
        }

        List<Line> commandLines = new List<Line>();

        List<Line> removeLines = new List<Line>();

        List<Line> receiveLines = new List<Line>();

        /// <summary>
        /// Get the currently selected item
        /// </summary>
        /// <returns></returns>
        Item GetSelectedItem()
        {
            // selected item
            Item selectedItem = null;

            // check for selection
            if (DataGrid_UserList.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_UserList.SelectedRows[0].Index;

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

        /// <summary>
        /// Get selected user
        /// </summary>
        /// <returns></returns>
        public LocalNode.Nuid GetSelectedNuid()
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for valid item
            return item != null ? item.nuid : new LocalNode.Nuid();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm"></param>
        public SessionForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // check heights
            if (DataGrid_UserList.Height < 1) DataGrid_UserList.Height = 1;
            if (Text_Receive.Height < 1) Text_Receive.Height = 1;
            // calculate offsets
            listHeightOffset = Height - DataGrid_UserList.Height - Text_Receive.Height;
            listWidthOffset = Width - DataGrid_UserList.Width;
            listScale = (float)DataGrid_UserList.Height / (DataGrid_UserList.Height + Text_Receive.Height);
            receiveWidthOffset = Width - Text_Receive.Width;
            receiveHeightOffset = Height - Text_Receive.Height - Text_Receive.Location.Y;
            transmitWidthOffset = Width - Text_Transmit.Width;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // change font
            Text_Receive.Font = main.dataFont;
            Text_Transmit.Font = main.dataFont;
            DataGrid_UserList.DefaultCellStyle.Font = main.dataFont;

            // send timer
            sendTimer.Tick += new EventHandler(AllowSend);
            sendTimer.Interval = 1000;

            Text_Receive.ForeColor = Settings.Default.CommsTextColour;
            Text_Receive.BackColor = Settings.Default.CommsBackColour;
            Text_Transmit.ForeColor = Settings.Default.CommsTextColour;
            Text_Transmit.BackColor = Settings.Default.CommsBackColour;

#if NO_COMMS
            Settings.WriteInt32("SessionChat", 0);
            Check_Chat.Visible = false;
            Text_Receive.Visible = false;
            Text_Transmit.Visible = false;
            Button_Send.Visible = false;
#endif

            // save window title
            title = Text;
        }

        private void AllowSend(object sender, System.EventArgs e)
        {
            Button_Send.Enabled = true;
            sendTimer.Stop();
        }

        /// <summary>
        /// Add this node
        /// </summary>
        void AddMe()
        {
            // callsign
            string callsign = main.network.GetLocalCallsign();
            int aircraftCount = 0;
            int objectCount = 0;
            // connected to node
            bool connected = false;
            LocalNode.Nuid nuid;

            // check for sim
            if (main.sim != null)
            {
                // get aircraft count
                aircraftCount = main.sim.objectList.FindAll(o => o is Sim.Aircraft && main.sim.IsBroadcast(o)).Count;
                // get object count
                objectCount = main.sim.objectList.FindAll(o => (o is Sim.Aircraft) == false && o.owner == Sim.Obj.Owner.Sim).Count;
            }

            // connected to node
            connected = main.network.localNode.Connected;
            nuid = main.network.localNode.GetLocalNuid();

            // create new item
            Item item = new Item(main.guid, nuid, new IPEndPoint(0, 0), main.settingsNickname, Main.version, main.sim != null ? main.sim.GetSimulatorName() : "", callsign, aircraftCount, objectCount, "", false, false, main.network.localNode.GetLocalNuid().port, connected, connected, true, 0.0f);
            // add to list
            itemList.Add(item);
        }

        /// <summary>
        /// Add node to list
        /// </summary>
        /// <param name="nuid">Nuid</param>
        /// <param name="address">IP Address</param>
        void AddNode(LocalNode.Nuid nuid, string nickname)
        {
            int aircraftCount = 0;
            int objectCount = 0;
            // share cockpit
            string share = "";
            bool receiveEstablished = false;
            bool sendEstablished = false;
            int port;
            float latency = 0.0f;

            // get guid
            Guid guid = main.network.GetNodeGuid(nuid);

            // get network data
            string version = main.network.GetNodeVersion(nuid);
            string simulator = main.network.GetNodeSimulator(nuid);
            string callsign = main.network.GetNodeCallsign(nuid);

            // check for sim
            if (main.sim != null)
            {
                // get aircraft count
                aircraftCount = main.sim.objectList.FindAll(o => o.ownerNuid == nuid && o is Sim.Aircraft).Count;
                // get object count
                objectCount = main.sim.objectList.FindAll(o => o.ownerNuid == nuid && (o is Sim.Aircraft) == false).Count;
            }

            // multiple objects permission
            if (main.log.MultipleObjects(nuid) || main.settingsMultiObjects)
            {
                share += "M";
            }

            // shared permissions
            if (main.log.ShareCockpit(nuid) || Settings.Default.ShareCockpitEveryone)
            {
                share += "C";

                if (main.network.shareFlightControls == nuid)
                {
                    share += Resources.strings.ShareFlightSuffix;
                }
                if (main.network.shareAncillaryControls == nuid)
                {
                    share += Resources.strings.ShareAncillarySuffix;
                }
                if (main.network.shareNavControls == nuid)
                {
                    share += Resources.strings.ShareNavSuffix;
                }
            }

            // get node endpoint
            main.network.localNode.GetNodeEndPoint(nuid, out IPEndPoint endPoint);

            // save
#if NO_HUBS
            bool save = main.addressBook.entries.Find(f => f.endPoint.Equals(endPoint)) != null;
#else
            bool save = main.addressBook.entries.Find(f => f.uuid == Network.MakeUuid(guid)) != null;
#endif
            // ignore
            bool ignore = main.log.IgnoreNode(nuid);

            // port
            port = endPoint.Port;
            // route
            bool direct = main.network.localNode.NodeDirect(nuid);
            // connected to node
            receiveEstablished = main.network.localNode.NodeReceiveEstablished(nuid);
            sendEstablished = main.network.localNode.NodeSendEstablished(nuid);

            // latency
            latency = main.network.localNode.GetNodeRTT(nuid);

            // add item
            Item item = new Item(guid, nuid, endPoint, nickname, version, simulator, callsign, aircraftCount, objectCount, share, save, ignore, port, receiveEstablished, sendEstablished, direct, latency);
            // add to list
            itemList.Add(item);
        }

        /// <summary>
        /// temporary user list
        /// </summary>
        List<Network.HubUser> tempHubUserList = new List<Network.HubUser>();

        /// <summary>
        /// Refresher
        /// </summary>
        public Refresher usersRefresher = new Refresher();

        /// <summary>
        /// Refresh form
        /// </summary>
        public void CheckUsersRefresher()
        {
            // check for scheduled refresh
            if (usersRefresher.Refresh())
            {
                // refresh
                RefreshUsers();
            }
        }

        /// <summary>
        /// Refresh users
        /// </summary>
        public void RefreshUsers()
        {
            // selected item
            Item selectedItem = GetSelectedItem();
            LocalNode.Nuid selectedNuid = (selectedItem != null) ? selectedItem.nuid : new LocalNode.Nuid();

            // clear list
            itemList.Clear();

            lock (main.conch)
            {
                // check if connected
                if (main.network.localNode.Connected)
                {
                    // add this node to the list
                    AddMe();
                }

                // for each node
                foreach (var node in main.network.nodeList)
                {
                    // add node to window list
                    AddNode(node.Key, node.Value.nickname);
                }
            }

            // check which column was selected
            switch (Settings.Default.SortUsersColumn)
            {
                case 0:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.nickname.CompareTo(i2.nickname); });
                    break;
                case 1:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.callsign.Equals(i2.callsign) ? i1.nickname.CompareTo(i2.nickname) : i1.callsign.CompareTo(i2.callsign); });
                    break;
                case 2:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.connected.Equals(i2.connected) ? i1.nickname.CompareTo(i2.nickname) : i1.connected.CompareTo(i2.connected); });
                    break;
                case 3:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.latency.Equals(i2.latency) ? i1.nickname.CompareTo(i2.nickname) : i1.latency.CompareTo(i2.latency); });
                    break;
                case 4:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.save.Equals(i2.save) ? i1.nickname.CompareTo(i2.nickname) : -i1.save.CompareTo(i2.save); });
                    break;
                case 5:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.ignore.Equals(i2.ignore) ? i1.nickname.CompareTo(i2.nickname) : -i1.ignore.CompareTo(i2.ignore); });
                    break;
                case 6:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.share.Equals(i2.share) ? i1.nickname.CompareTo(i2.nickname) : i1.share.CompareTo(i2.share); });
                    break;
                case 7:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.aircraftCount.Equals(i2.aircraftCount) ? i1.nickname.CompareTo(i2.nickname) : -i1.aircraftCount.CompareTo(i2.aircraftCount); });
                    break;
                case 8:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.objectCount.Equals(i2.objectCount) ? i1.nickname.CompareTo(i2.nickname) : -i1.objectCount.CompareTo(i2.objectCount); });
                    break;
                case 9:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.port.Equals(i2.port) ? i1.nickname.CompareTo(i2.nickname) : i1.port.CompareTo(i2.port); });
                    break;
                case 10:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.versionValue.Equals(i2.versionValue) ? i1.nickname.CompareTo(i2.nickname) : -i1.versionValue.CompareTo(i2.versionValue); });
                    break;
                case 11:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.simulator.Equals(i2.simulator) ? i1.nickname.CompareTo(i2.nickname) : i1.simulator.CompareTo(i2.simulator); });
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
                rows[index].CreateCells(DataGrid_UserList);
                // fill row
                rows[index].Cells[0].Value = itemList[index].nickname;
                rows[index].Cells[1].Value = itemList[index].callsign;
                rows[index].Cells[2].Value = itemList[index].connected;
                rows[index].Cells[3].Value = itemList[index].latencyText;
                rows[index].Cells[4].Value = itemList[index].save;
                rows[index].Cells[5].Value = itemList[index].ignore;
                rows[index].Cells[6].Value = itemList[index].share;
                rows[index].Cells[7].Value = itemList[index].aircraftCount.ToString();
                rows[index].Cells[8].Value = itemList[index].objectCount.ToString();
                rows[index].Cells[9].Value = itemList[index].port;
                rows[index].Cells[10].Value = itemList[index].version;
                rows[index].Cells[11].Value = itemList[index].simulator;
            }

            // clear existing cells
            DataGrid_UserList.Rows.Clear();
            DataGrid_UserList.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_UserList.Rows.Count; index++)
            {
                // check for valid string
                if (DataGrid_UserList.Rows[index].Cells[2].Value is string)
                {
                    // get value
                    string value = DataGrid_UserList.Rows[index].Cells[2].Value as string;

                    // check if connected
                    if (itemList[index].sendEstablished)
                    {
                        DataGrid_UserList.Rows[index].Cells[2].Style.BackColor = Settings.Default.ColourActiveBackground;
                        DataGrid_UserList.Rows[index].Cells[2].Style.ForeColor = Settings.Default.ColourActiveText;
                    }
                    else if (itemList[index].receiveEstablished)
                    {
                        DataGrid_UserList.Rows[index].Cells[2].Style.BackColor = Settings.Default.ColourWaitingBackground;
                        DataGrid_UserList.Rows[index].Cells[2].Style.ForeColor = Settings.Default.ColourWaitingText;
                    }
                    else
                    {
                        DataGrid_UserList.Rows[index].Cells[2].Style.BackColor = Settings.Default.ColourInactiveBackground;
                        DataGrid_UserList.Rows[index].Cells[2].Style.ForeColor = Settings.Default.ColourInactiveText;
                    }
                }

                // check for selected node
                if (itemList[index].nuid.Equals(selectedNuid))
                {
                    // select row
                    DataGrid_UserList.Rows[index].Selected = true;
                }
            }
        }

        /// <summary>
        /// Refresh comms
        /// </summary>
        public void RefreshComms()
        {
            // clear lines
            receiveLines.Clear();

            // add all command lines
            foreach (var line in commandLines)
            {
                // check for timeout
                if (main.ElapsedTime - line.time > 3600.0)
                {
                    // add to remove list
                    removeLines.Add(line);
                }
                else
                {
                    // add line
                    receiveLines.Add(line);
                }
            }

            // for all remove lines
            foreach (var line in removeLines)
            {
                // remove
                commandLines.Remove(line);
            }
            // clear remove list
            removeLines.Clear();

            // check if connected
            if (main.network.localNode.Connected)
            {
                lock (main.conch)
                {
                    // for each user
                    foreach (var userNotes in main.notes.userNotesList)
                    {
                        Guid guid = userNotes.Key;
                        // check for show ignored aircraft
                        if (main.log.IgnoreNode(ref guid) == false)
                        {
                            // for each comms note
                            foreach (var note in userNotes.Value.commsList)
                            {
                                // check for non-empty comms
                                if (note.Value.text.Length > 0)
                                {
                                    if (note.Value.channel == Notes.SESSION_CHANNEL)
                                    {
                                        // add line
                                        Color colour = Settings.Default.CommsTextColour;
                                        receiveLines.Add(new Line(note.Value.time, colour, userNotes.Value.uniqueNickname, userNotes.Value.callsign, note.Value.text));
                                    }
                                }
                            }
                        }
                    }

                    // sort lines by time
                    receiveLines.Sort(delegate (Line l1, Line l2) { return l1.time.CompareTo(l2.time); });
                }

                // enable comms
                Text_Receive.Enabled = true;
                Text_Transmit.Enabled = true;
                Button_Send.Enabled = true;
            }
            else
            {
                // disable comms
                Text_Receive.Enabled = false;
                Text_Transmit.Enabled = false;
                Button_Send.Enabled = false;
            }

            // get textHeight
            int textHeight = 0;
            using (Graphics g = Text_Receive.CreateGraphics())
            {
                textHeight = TextRenderer.MeasureText(g, "A", Text_Receive.Font).Height;
            }

            // line array
            string[] lines = new string[receiveLines.Count + 1];

            // for each line
            for (int index = 0; index < receiveLines.Count; index++)
            {
                lines[index] = receiveLines[index].text;
            }

            lines[receiveLines.Count] = "";

            // draw lines
            Text_Receive.Lines = lines;

            //// get char index
            //int startCharIndex = 0;
            //int endCharIndex = 0;

            //// for each line
            //for (int lineIndex = 0; lineIndex < receiveLines.Count; lineIndex++)
            //{
            //    // end of line
            //    endCharIndex += lines[lineIndex].Length + 1;
            //    // select
            //    Text_Receive.Select(startCharIndex, endCharIndex);
            //    // set colour
            //    Text_Receive.SelectionColor = receiveLines[lineIndex].colour;
            //    // next line
            //    startCharIndex = endCharIndex;
            //}

            // move to bottom
            Text_Receive.SelectionStart = Text_Receive.Text.Length;
            Text_Receive.ScrollToCaret();

            // check if chat is displayed
            if (Settings.Default.SessionChat)
            {
                Text_Receive.Visible = true;
                Text_Transmit.Visible = true;
                Button_Send.Visible = true;
            }
            else
            {
                Text_Receive.Visible = false;
                Text_Transmit.Visible = false;
                Button_Send.Visible = false;
            }
            // update positions
            SessionForm_Resize(null, null);

            // reset refresh button
            Button_Refresh.BackColor = System.Drawing.SystemColors.ControlLight;
        }

        /// <summary>
        /// Refresh window
        /// </summary>
        public void RefreshWindow()
        {
            RefreshUsers();
#if !NO_COMMS
            RefreshComms();
#endif
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

        private void SessionForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.SessionFormLocation;
            Size size = Settings.Default.SessionFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.SessionFormLocation = Location;
                Settings.Default.SessionFormSize = Size;
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

            // get chat state
            Check_Chat.CheckState = Settings.Default.SessionChat ? CheckState.Checked : CheckState.Unchecked;
        }

        private void SessionForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void SessionForm_Resize(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // apply offsets
                if (Settings.Default.SessionChat)
                {
                    DataGrid_UserList.Height = (int)((Height - listHeightOffset) * listScale);
                }
                else
                {
                    DataGrid_UserList.Height = Text_Transmit.Location.Y + Text_Transmit.Height - DataGrid_UserList.Location.X;
                }
                DataGrid_UserList.Width = Width - listWidthOffset;
                Text_Receive.Location = new Point(Text_Receive.Location.X, DataGrid_UserList.Location.Y + DataGrid_UserList.Height + 6);
                Text_Receive.Width = Width - receiveWidthOffset;
                Text_Receive.Height = Height - receiveHeightOffset - Text_Receive.Location.Y;
                Text_Transmit.Width = Width - transmitWidthOffset;
            }
        }

        private void DataGrid_UserList_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check for header click
            if (e.RowIndex == -1)
            {
                Settings.Default.SortUsersColumn = e.ColumnIndex;
                RefreshUsers();
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
                        case 4:
                            {
                                // check for me
                                if (item.guid.Equals(main.guid))
                                {
                                    MessageBox.Show("You can not save yourself.", Main.name + ": Session");
                                }
                                else
                                {
#if NO_HUBS
                                    // find entry
                                    AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.endPoint.Equals(item.endPoint));
#else
                                    // find entry
                                    AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.uuid == Network.MakeUuid(item.guid));
#endif

                                    // check for existing entry
                                    if (entry != null)
                                    {
                                        // confirm
                                        DialogResult result = MessageBox.Show("Remove '" + entry.name + "' from the address book?", Main.name + ": Address Book", MessageBoxButtons.YesNo);
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
                                                name = item.nickname.Length > 0 ? item.nickname : Network.UuidToString(Network.MakeUuid(item.guid))
                                            };

                                            // check for valid data
                                            if (entry.name.Length > 0)
                                            {
#if NO_HUBS
                                                // set address
                                                entry.address = Network.EncodeIP(item.endPoint.ToString());
                                                entry.endPoint = item.endPoint;
#else
                                                // set uuid
                                                entry.uuid = Network.MakeUuid(item.guid);
                                                // set address
                                                entry.address = Network.UuidToString(entry.uuid);
#endif
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
                            }
                            break;

                        case 5:
                            {
                                // check for me
                                if (item.guid.Equals(main.guid))
                                {
                                    MessageBox.Show("You can not ignore yourself.", Main.name + ": Session");
                                }
                                else
                                {
                                    lock (main.conch)
                                    {
                                        // check if currently ignored
                                        if (main.log.IgnoreNode(item.nuid))
                                        {
                                            main.log.RemoveIgnoreNode(item.nuid);
                                        }
                                        else
                                        {
                                            main.log.AddIgnoreNode(item.nuid);
                                        }
                                    }
                                    // update window
                                    RefreshWindow();
                                }
                            }
                            break;
                    }
                }
            }
        }

        private void SessionForm_Activated(object sender, EventArgs e)
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

        private void SessionForm_Deactivate(object sender, EventArgs e)
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

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Context_User_Permissions_Click(object sender, EventArgs e)
        {
#if !SERVER
            // get selected item
            Item item = GetSelectedItem();

            // check for this node
            if (item != null)
            {
                bool cockpit;
                bool flight;
                bool ancillary;
                bool nav;
                bool multiple;

                lock (main.conch)
                {
                    cockpit = main.log.ShareCockpit(item.nuid);
                    flight = main.network.shareFlightControls == item.nuid;
                    ancillary = main.network.shareAncillaryControls == item.nuid;
                    nav = main.network.shareNavControls == item.nuid;
                    multiple = main.log.MultipleObjects(item.nuid);
                }

                // create shared cockpit form
                PermissionsForm permissionsForm = new PermissionsForm(main, cockpit, flight, ancillary, nav, multiple);
                // show form
                DialogResult result = permissionsForm.ShowDialog();
                // check result
                if (result == DialogResult.OK)
                {
                    lock (main.conch)
                    {
                        // update share cockpit
                        if (permissionsForm.shareCockpit)
                        {
                            main.log.AddShareCockpit(item.nuid);
                        }
                        else
                        {
                            main.log.RemoveShareCockpit(item.nuid);
                        }

                        // update share flight controls
                        if (permissionsForm.shareFlight)
                        {
                            main.network.shareFlightControls = item.nuid;
                        }
                        else if (main.network.shareFlightControls == item.nuid)
                        {
                            main.network.shareFlightControls = new LocalNode.Nuid();
                        }

                        // update share engine controls
                        if (permissionsForm.shareEngine)
                        {
                            main.network.shareAncillaryControls = item.nuid;
                        }
                        else if (main.network.shareAncillaryControls == item.nuid)
                        {
                            main.network.shareAncillaryControls = new LocalNode.Nuid();
                        }

                        // update share other controls
                        if (permissionsForm.shareOther)
                        {
                            main.network.shareNavControls = item.nuid;
                        }
                        else if (main.network.shareNavControls == item.nuid)
                        {
                            main.network.shareNavControls = new LocalNode.Nuid();
                        }

                        // update multiple objects
                        if (permissionsForm.multiple)
                        {
                            main.log.AddMultipleObjects(item.nuid);
                        }
                        else
                        {
                            main.log.RemoveMultipleObjects(item.nuid);
                        }

                        // update shared data
                        main.network.ScheduleSharedDataMessage(item.nuid);
                    }

                    // update
                    RefreshUsers();
                }
            }
#endif
        }

        private void Context_User_Cockpit_Click(object sender, EventArgs e)
        {
            // update shared cockpit
            Settings.Default.ShareCockpitEveryone = Context_User_Cockpit.CheckState != CheckState.Checked;
            // update window
            RefreshUsers();
        }

        private void Context_User_Multiple_Click(object sender, EventArgs e)
        {
            // update shared cockpit
            Settings.Default.MultipleObjectsEveryone = Context_User_Multiple.CheckState != CheckState.Checked;
            main.settingsMultiObjects = Settings.Default.MultipleObjectsEveryone;
            // update window
            RefreshUsers();
        }

        private void Context_User_Opening(object sender, CancelEventArgs e)
        {
            // disable all options
            Context_User_Permissions.Enabled = false;
            Context_User_Cockpit.Checked = Settings.Default.ShareCockpitEveryone;
            Context_User_Multiple.Checked = main.settingsMultiObjects;

            // get selected item
            Item item = GetSelectedItem();

            // check for selection
            if (item != null && item.nuid != main.network.localNode.GetLocalNuid())
            {
                // allow permissions
                Context_User_Permissions.Enabled = true;
            }
        }

        private void SessionForm_ResizeEnd(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // save form position
                Settings.Default.SessionFormLocation = Location;
                Settings.Default.SessionFormSize = Size;

#if !NO_COMMS
                // refresh
                RefreshComms();
#endif
            }
        }

        private void Check_Chat_CheckedChanged(object sender, EventArgs e)
        {
#if !NO_COMMS
            Settings.Default.SessionChat = Check_Chat.Checked;
            // refresh
            RefreshComms();
#endif
        }

        private void Button_Send_Click(object sender, EventArgs e)
        {
            // check for message
            if (Text_Transmit.Text.Length > 0)
            {
                // check for command
                if (Text_Transmit.Text[0] == '.')
                {
                    // help
                    if (Text_Transmit.Text.Equals(".help", StringComparison.OrdinalIgnoreCase))
                    {
                        commandLines.Add(new Line(main.ElapsedTime + 0.000, Color.DimGray, "Command list:"));
                        commandLines.Add(new Line(main.ElapsedTime + 0.001, Color.DimGray, ".help - Show the command list"));
                    }
                    else
                    {
                        // unknown
                        commandLines.Add(new Line(main.ElapsedTime + 0.000, Color.DimGray, "Unknown command, '" + Text_Transmit.Text + "'"));
                    }
                }
                else
                {
                    lock (main.conch)
                    {
                        // check if connected to a session
                        if (main.network.localNode.Connected)
                        {
                            main.notes.PostCommsNote(Notes.SESSION_CHANNEL, Text_Transmit.Text);
                        }
                    }

                    // disable send button
                    Button_Send.Enabled = false;
                    // start timer
                    sendTimer.Start();
                }

                // clear text
                Text_Transmit.Clear();
            }
        }

        private void Context_Chat_TextColour_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Settings.Default.CommsTextColour
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update setting
                Settings.Default.CommsTextColour = dialog.Color;
                Text_Receive.ForeColor = dialog.Color;
                Text_Transmit.ForeColor = dialog.Color;
                // refresh window
                RefreshComms();
            }
        }

        private void Context_Chat_BackgroundColour_Click(object sender, EventArgs e)
        {
            // open colour picker
            ColorDialog dialog = new ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                FullOpen = true,
                Color = Settings.Default.CommsBackColour
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                // update setting
                Settings.Default.CommsBackColour = dialog.Color;
                Text_Receive.BackColor = dialog.Color;
                Text_Transmit.BackColor = dialog.Color;
                // refresh window
                RefreshComms();
            }
        }

        private void SessionForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.SessionFormOpen = Visible;
        }
    }
}
