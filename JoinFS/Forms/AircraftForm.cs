using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class AircraftForm : Form
    {
        Main main;

        /// <summary>
        /// Item in the list
        /// </summary>
        class Item
        {
            public LocalNode.Nuid nuid;
            public uint netId = 0;
            public uint simId = 0;
            public Guid guid = Guid.Empty;

            public string callsign = "";
            public string distanceText = "-";
            public string nickname = "";
            public string model = "";
            public string original = "";
            public string simulator = "";
            public bool weather = false;
            public bool broadcast = false;
            public bool record = false;
            public bool created = false;
            public bool failed = false;
            public string squawk = "-";
            public string com1 = "-";
            public string com2 = "-";
            public string bearingText = "-";
            public string headingText = "-";
            public string altitudeText = "-";
            public string speedText = "";
            public string wind = "-";
            public string type = "-";
            public string from = "-";
            public string to = "-";
            public string rules = "-";
            public string route = "-";
            public string remarks = "-";
            public string alt = "-";
            public string cruise = "-";
            public string level = "-";
            public bool ignore = false;
            public double distance = 0.0;
            public double altitude = 0.0;
            public double speed = 0.0;
            public bool showOnRadar = true;

            public Item(Guid guid, LocalNode.Nuid nuid, uint netId, uint simId)
            {
                this.nuid = nuid;
                this.netId = netId;
                this.simId = simId;
                this.guid = guid;
            }

            public Item(Guid guid)
            {
                netId = 0;
                simId = 0;
                this.guid = guid;
            }
        };

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
        /// default window title
        /// </summary>
        string title = "";

        /// <summary>
        /// default option
        /// </summary>
        string defaultFlightPlanOption = "";

        /// <summary>
        /// Vuids
        /// </summary>
        uint vuidCom1;
        uint vuidCom2;
        uint vuidSquawk;
        uint vuidIfr;
        uint vuidFrom;
        uint vuidTo;

        /// <summary>
        /// Get the currently selected aircraft
        /// </summary>
        /// <returns></returns>
        Item GetSelectedItem()
        {
            // selected item
            Item selectedItem = null;

            // check for selection
            if (DataGrid_AircraftList.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_AircraftList.SelectedRows[0].Index;

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

        Sim.Aircraft GetAircraft(Item item)
        {
            // get aircraft
            return main.sim ?. objectList.Find(o => o.ownerNuid.Invalid() && o.simId == item.simId || o.ownerNuid.Valid() && o.ownerNuid == item.nuid && o.netId == item.netId && o is Sim.Aircraft) as Sim.Aircraft;
        }

        public AircraftForm(Main main)
        {
            this.main = main;
            
            InitializeComponent();

            // calculate offsets
            listHeightOffset = Height - DataGrid_AircraftList.Height;
            listWidthOffset = Width - DataGrid_AircraftList.Width;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");
            // save window title
            title = Text;

            // save default flight plan option
            defaultFlightPlanOption = Context_Aircraft_FlightPlan.Text;

            // change font
            DataGrid_AircraftList.DefaultCellStyle.Font = main.dataFont;
            Label_Details.Font = main.dataFont;
            Label_FlightPlan1.Font = main.dataFont;
            Label_FlightPlan2.Font = main.dataFont;

            // get vuids
            vuidCom1 = VariableMgr.CreateVuid("com active frequency:1");
            vuidCom2 = VariableMgr.CreateVuid("com active frequency:2");
            vuidSquawk = VariableMgr.CreateVuid("transponder code:1");
            vuidIfr = VariableMgr.CreateVuid("ai traffic isifr");
            vuidFrom = VariableMgr.CreateVuid("ai traffic fromairport");
            vuidTo = VariableMgr.CreateVuid("ai traffic toairport");
        }

        void AddAircraft(Sim.Aircraft aircraft)
        {
            // get guid
            Guid guid = main.network.GetNodeGuid(aircraft.ownerNuid);
            // check for existing aircraft
            if (aircraft.owner != Sim.Obj.Owner.Network || itemList.Exists(u => u.nuid.Equals(aircraft.ownerNuid) && u.netId == aircraft.netId) == false)
            {
                // create item
                Item item = new Item(guid, aircraft.ownerNuid, aircraft.netId, aircraft.simId);
                itemList.Add(item);

                // get user position
                Sim.Pos userPosition = main.sim ?. userAircraft ?. Position;
                // get aircraft position
                Sim.Pos aircraftPosition = aircraft.Position;
                // check for user aircraft
                if (userPosition != null && aircraftPosition != null)
                {
                    // get distance to aircraft
                    double d = Vector.GeodesicDistance(aircraftPosition.geo.x, aircraftPosition.geo.z, userPosition.geo.x, userPosition.geo.z);
                    // save distance
                    item.distance = d;
                    // convert to nautical miles
                    item.distanceText = (d * 0.00053995680346).ToString("N2");
                }

                // get nickname
                item.nickname = main.network.GetNodeName(aircraft.ownerNuid);
                // check for AI
                if (aircraft.user == false)
                {
                    // check for recorded aircraft
                    if (aircraft.owner == Sim.Obj.Owner.Recorder)
                    {
                        // mark name
                        item.nickname += " (R)";
                    }
                    else
                    {
                        // mark name
                        item.nickname += " (A)";
                    }
                }

                // check if aircraft created
                if (main.sim != null && main.sim.Connected)
                {
                    // get model
#if FS2024
                    if (aircraft.ModelLivery.Length > 0)
                        item.model = aircraft.ModelTitle + " (" + aircraft.ModelLivery + ")";
                    else
                        item.model = aircraft.ModelTitle;
#else
                    item.model = aircraft.ModelTitle;
#endif
                    // check for substitution
                    if (aircraft.subType == Substitution.Type.Substitute)
                    {
                        // mark model
                        item.model += " (S)";
                    }
                    else if (aircraft.subType == Substitution.Type.Auto)
                    {
                        // mark model
                        item.model += " (A)";
                    }
                    else if (aircraft.subType == Substitution.Type.Default)
                    {
                        // mark model
                        item.model += " (D)";
                    }
                    else if (aircraft.subType == Substitution.Type.AI)
                    {
                        // mark model
                        item.model += " (AI)";
                    }
                }
                else
                {
                    // get model
                    item.model = aircraft.flightPlan.icaoType;
                }

                // original model
                item.original = aircraft.ownerModel;
                // get simulator
                item.simulator = main.network.GetNodeSimulator(aircraft.ownerNuid);
                // weather
                item.weather = aircraft == main.sim ?. weatherAircraft;
                // broadcast
                item.broadcast = (main.sim != null) ? main.sim.IsBroadcast(aircraft) : false;
                // record
                item.record = aircraft.record;
                // created
                item.created = aircraft.Created;
                // failed
                item.failed = aircraft.failed;

                // check for valid aircraft position
                if (aircraftPosition != null)
                {
                    // check for user aircraft
                    if (userPosition != null)
                    {
                        // get bearing
                        double bearing = Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, aircraftPosition.geo.x, aircraftPosition.geo.z);
                        // convert bearing
                        bearing *= 180.0 / Math.PI;
                        // set text
                        item.bearingText = ((int)bearing).ToString("D3");
                    }

                    // set heading
                    item.headingText = ((int)(aircraftPosition.angles.y * 180.0 / Math.PI)).ToString("D3");
                    // set altitude
                    item.altitude = aircraftPosition.geo.y * Sim.FEET_PER_METRE;
                    item.altitudeText = item.altitude.ToString("N0");
                }

                // calculate speed
                double speed = Math.Sqrt(aircraft.netVelocity.linear.x * aircraft.netVelocity.linear.x + aircraft.netVelocity.linear.z * aircraft.netVelocity.linear.z) * 1.9438444925;
                // save speed
                item.speed = speed;
                // check for mach approach
                if (speed > 600.0)
                {
                    // mach speed
                    item.speedText = (speed / 667.0).ToString("N2") + " M";
                }
                else
                {
                    // speed in knots
                    item.speedText = speed.ToString("N0") + " kt";
                }

                // ignore aircraft
                if (aircraft.owner == Sim.Obj.Owner.Network)
                {
                    // check if ignored network
                    item.ignore = main.log.IgnoreNode(aircraft.ownerNuid);
                }
                else
                {
                    // check if ignored simulator
                    item.ignore = main.log.IgnoreName(aircraft.flightPlan.callsign);
                }

                // check for variables
                if (aircraft.variableSet != null)
                {
                    // COM frequencies
                    item.com1 = aircraft.variableSet.GetInteger(vuidCom1).ToString();
                    item.com2 = aircraft.variableSet.GetInteger(vuidCom2).ToString();
                    // squawk
                    item.squawk = aircraft.variableSet.GetInteger(vuidSquawk).ToString();
                    // rules
                    item.rules = aircraft.variableSet.GetInteger(vuidIfr) != 0 ? "IFR" : "VFR";
                    // from
                    item.from = aircraft.variableSet.GetString8(vuidFrom);
                    // to
                    item.to = aircraft.variableSet.GetString8(vuidTo);
                }

                // flight plan
                item.callsign = aircraft.flightPlan.callsign;
                item.type = aircraft.flightPlan.icaoType;
                // check for overriden from and to
                if (aircraft.flightPlan.departure.Length > 0) item.from = aircraft.flightPlan.departure;
                if (aircraft.flightPlan.destination.Length > 0) item.to = aircraft.flightPlan.destination;
                item.rules = aircraft.flightPlan.rules;
                item.route = aircraft.flightPlan.route;
                item.remarks = aircraft.flightPlan.remarks;
                item.alt = aircraft.flightPlan.alternate;
                item.cruise = aircraft.flightPlan.speed;
                item.level = aircraft.flightPlan.altitude;

                // convert frequencies
                item.com1 = (item.com1.Length < 3) ? "" : item.com1.Substring(0, 3) + "." + item.com1.Substring(3);
                item.com2 = (item.com2.Length < 3) ? "" : item.com2.Substring(0, 3) + "." + item.com2.Substring(3);

                // wind
                item.wind = aircraft.wind;
                // show on radar
                item.showOnRadar = aircraft.showOnRadar;
            }
        }

        /// <summary>
        /// Add Hub user
        /// </summary>
        /// <param name="user"></param>
        void AddAircraft(Network.HubUser user)
        {
            // check for existing user
            if (itemList.Exists(u => u.guid.Equals(user.guid)) == false)
            {
                // create item
                Item item = new Item(user.guid);

                // get user position
                Sim.Pos userPosition = main.sim ?. userAircraft ?. Position;
                // check for user aircraft
                if (userPosition != null)
                {
                    // get distance to aircraft
                    double d = Vector.GeodesicDistance(user.longitude * (Math.PI / 180.0), user.latitude * (Math.PI / 180.0), userPosition.geo.x, userPosition.geo.z);
                    // save distance
                    item.distance = d;
                    // convert to nautical miles
                    item.distanceText = (d * 0.00053995680346).ToString("N2");
                }

                // get nickname
                item.nickname = user.nickname;
                // get model
                item.model = user.flightPlan.icaoType;
                item.original = user.flightPlan.icaoType;

                // check for user aircraft
                if (userPosition != null)
                {
                    // get bearing
                    double bearing = Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, user.longitude * (Math.PI / 180.0), user.latitude * (Math.PI / 180.0));
                    // convert bearing
                    bearing *= 180.0 / Math.PI;
                    // set text
                    item.bearingText = ((int)bearing).ToString("D3");
                }

                // set heading
                item.headingText = user.heading.ToString("D3");
                // set altitude
                item.altitude = (double)user.altitude;
                item.altitudeText = user.altitude.ToString("N0");

                // save speed
                item.speed = (double)user.speed;
                // check for mach approach
                if (user.speed > 600.0)
                {
                    // mach speed
                    item.speedText = (user.speed / 667.0).ToString("N2") + " M";
                }
                else
                {
                    // speed in knots
                    item.speedText = user.speed.ToString("N0") + " kt";
                }

                // COM frequencies
                item.com1 = "2280";
                item.com2 = "2280";
                // squawk
                item.squawk = user.squawk.ToString("");
                // flight plan
                item.callsign = user.flightPlan.callsign;
                item.rules = user.ifr ? "IFR" : "VFR";
                item.from = user.flightPlan.departure;
                item.to = user.flightPlan.destination;
                item.rules = user.flightPlan.rules;
                item.route = user.flightPlan.route;
                item.remarks = user.flightPlan.remarks;
                item.alt = user.flightPlan.alternate;
                item.cruise = user.flightPlan.speed;
                item.level = user.flightPlan.altitude;

                // convert frequencies
                item.com1 = (item.com1.Length < 4) ? "" : "1" + item.com1.Substring(0, 2) + "." + item.com1.Substring(2, 2);
                item.com2 = (item.com2.Length < 4) ? "" : "1" + item.com2.Substring(0, 2) + "." + item.com2.Substring(2, 2);

                // add row
                itemList.Add(item);
            }
        }

        void RefreshDetails(Item item)
        {
            // check for valid item
            if (item != null)
            {
                // details
                Label_Details.Text = item.squawk + " | " + item.com1 + " | " + item.com2 + " | " + item.bearingText + " | " + item.original + " | " + item.simulator;
                // flight plan
                Label_FlightPlan1.Text = item.type + " | " + item.from + " | " + item.to + " | " + item.rules + " | " + item.alt + " | " + item.cruise + " | " + item.level;
                Label_FlightPlan2.Text = item.route + " | " + item.remarks;
            }
            else
            {
                // details
                Label_Details.Text = "";
                // flight plan
                Label_FlightPlan1.Text = "";
                Label_FlightPlan2.Text = "";
            }
        }

        /// <summary>
        /// temporary user list
        /// </summary>
        List<Network.HubUser> tempHubUserList = new List<Network.HubUser>();

        /// <summary>
        /// Refresh window
        /// </summary>
        public void RefreshWindow()
        {
            // selected item
            Item selectedItem = GetSelectedItem();
            Guid selectedGuid = (selectedItem != null) ? selectedItem.guid : Guid.Empty;
            uint selectedNetId = (selectedItem != null) ? selectedItem.netId : 0;
            uint selectedSimId = (selectedItem != null) ? selectedItem.simId : 0;

            // clear item list
            itemList.Clear();

            lock (main.conch)
            {
                // check for simulator
                if (main.sim != null)
                {
                    // add user aircraft
                    foreach (var obj in main.sim.objectList)
                    {
                        if (obj.owner == Sim.Obj.Owner.Me)
                        {
                            AddAircraft(obj as Sim.Aircraft);
                        }
                    }

                    // add network aircraft
                    foreach (var obj in main.sim.objectList)
                    {
                        if (obj is Sim.Aircraft && obj.owner == Sim.Obj.Owner.Network)
                        {
                            // check for show ignored aircraft
                            if (Settings.Default.IncludeIgnoredAircraft || main.log.IgnoreNode(obj.ownerNuid) == false)
                            {
                                AddAircraft(obj as Sim.Aircraft);
                            }
                        }
                    }

                    // add recorder aircraft
                    foreach (var obj in main.sim.objectList)
                    {
                        if (obj is Sim.Aircraft && obj.owner == Sim.Obj.Owner.Recorder)
                        {
                            AddAircraft(obj as Sim.Aircraft);
                        }
                    }

                    // check for simulator aircraft
                    if (Settings.Default.IncludeSimulatorAircraft)
                    {
                        // add any other aircraft
                        foreach (var obj in main.sim.objectList)
                        {
                            if (obj is Sim.Aircraft && obj.owner == Sim.Obj.Owner.Sim)
                            {
                                // check for show ignored aircraft
                                if (Settings.Default.IncludeIgnoredAircraft || main.log.IgnoreName((obj as Sim.Aircraft).flightPlan.callsign) == false)
                                {
                                    AddAircraft(obj as Sim.Aircraft);
                                }
                            }
                        }
                    }
                }

                // check for global aircraft
                if (Settings.Default.IncludeGlobalAircraft)
                {
                    // for each hub
                    foreach (var hub in main.network.hubList)
                    {
                        // check if hub is the one already connected to
                        if (hub.endPoint.Equals(main.network.joinEndPoint) == false)
                        {
                            // for each user in the hub
                            foreach (var user in hub.userList)
                            {
                                // add to temporary list
                                tempHubUserList.Add(user);
                            }
                        }
                    }

                    // for each global user
                    foreach (var user in tempHubUserList)
                    {
                        // add hub user aircraft
                        AddAircraft(user);
                    }

                    // clear user list
                    tempHubUserList.Clear();
                }
            }

            // sort list
            switch (Settings.Default.SortAircraftColumn)
            {
                case 0:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.callsign.CompareTo(i2.callsign); });
                    break;
                case 1:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.nickname.Equals(i2.nickname) ? i1.callsign.CompareTo(i2.callsign) : i1.nickname.CompareTo(i2.nickname); });
                    break;
                case 2:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.distance.Equals(i2.distance) ? i1.callsign.CompareTo(i2.callsign) : i1.distance.CompareTo(i2.distance); });
                    break;
                case 3:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.headingText.Equals(i2.headingText) ? i1.callsign.CompareTo(i2.callsign) : i1.headingText.CompareTo(i2.headingText); });
                    break;
                case 4:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.altitude.Equals(i2.altitude) ? i1.callsign.CompareTo(i2.callsign) : i1.altitude.CompareTo(i2.altitude); });
                    break;
                case 5:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.speed.Equals(i2.speed) ? i1.callsign.CompareTo(i2.callsign) : i1.speed.CompareTo(i2.speed); });
                    break;
                case 6:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.model.Equals(i2.model) ? i1.callsign.CompareTo(i2.callsign) : i1.model.CompareTo(i2.model); });
                    break;
                case 7:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.wind.Equals(i2.wind) ? i1.callsign.CompareTo(i2.callsign) : i1.wind.CompareTo(i2.wind); });
                    break;
                case 8:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.weather.Equals(i2.weather) ? i1.callsign.CompareTo(i2.callsign) : i1.weather.CompareTo(i2.weather); });
                    break;
                case 9:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.broadcast.Equals(i2.broadcast) ? i1.callsign.CompareTo(i2.callsign) : i1.broadcast.CompareTo(i2.broadcast); });
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
                rows[index].CreateCells(DataGrid_AircraftList);
                // fill row
                rows[index].Cells[0].Value = itemList[index].callsign;
                rows[index].Cells[1].Value = itemList[index].nickname;
                rows[index].Cells[2].Value = itemList[index].distanceText + " nm";
                rows[index].Cells[3].Value = itemList[index].headingText;
                rows[index].Cells[4].Value = itemList[index].altitudeText + " ft";
                rows[index].Cells[5].Value = itemList[index].speedText;
                rows[index].Cells[6].Value = itemList[index].model;
                rows[index].Cells[7].Value = itemList[index].wind;
                rows[index].Cells[8].Value = itemList[index].weather;
                rows[index].Cells[9].Value = itemList[index].broadcast;
                rows[index].Cells[10].Value = itemList[index].record;
                rows[index].Cells[11].Value = itemList[index].ignore;
            }

            // clear existing cells
            DataGrid_AircraftList.Rows.Clear();
            DataGrid_AircraftList.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_AircraftList.Rows.Count; index++)
            {
                // get item
                Item item = itemList[index];

                // get aircraft
                Sim.Aircraft aircraft = GetAircraft(item);
                // check for valid aircraft
                if (aircraft != null && main.sim != null)
                {
                    // check if aircraft is being tracked
                    if (main.sim.trackHeadingObject == aircraft || main.sim.trackBearingObject == aircraft)
                    {
                        // highlight aircraft
                        DataGrid_AircraftList.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 187);
                    }
                }

                // highlight distance column
                if (item.created)
                {
                    DataGrid_AircraftList.Rows[index].Cells[2].Style.BackColor = Settings.Default.ColourActiveBackground;
                    DataGrid_AircraftList.Rows[index].Cells[2].Style.ForeColor = Settings.Default.ColourActiveText;
                }
                else if (item.failed)
                {
                    DataGrid_AircraftList.Rows[index].Cells[2].Style.BackColor = Settings.Default.ColourInactiveBackground;
                    DataGrid_AircraftList.Rows[index].Cells[2].Style.ForeColor = Settings.Default.ColourInactiveText;
                }
                else 
                {
                    DataGrid_AircraftList.Rows[index].Cells[2].Style.BackColor = Settings.Default.ColourWaitingBackground;
                    DataGrid_AircraftList.Rows[index].Cells[2].Style.ForeColor = Settings.Default.ColourWaitingText;
                }

                // check for selected aircraft
                if (item.guid.Equals(selectedGuid) && item.netId == selectedNetId && item.simId == selectedSimId)
                {
                    // select row
                    DataGrid_AircraftList.Rows[index].Selected = true;
                }
            }

            // refresh details
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

        private void AircraftForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.AircraftFormLocation;
            Size size = Settings.Default.AircraftFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.AircraftFormLocation = Location;
                Settings.Default.AircraftFormSize = Size;
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
        }

        private void AircraftForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void DataGrid_AircraftList_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check for header click
            if (e.RowIndex == -1)
            {
                Settings.Default.SortAircraftColumn = e.ColumnIndex;
                RefreshWindow();
            }
            // check for valid index
            else if (e.RowIndex >= 0 && e.RowIndex < itemList.Count)
            {
                // message
                string message = "";
                bool refresh = false;
                bool doBroadcast = false;
                string model = "";
                bool broadcastObject = false;
                bool broadcastName = false;
                bool broadcastTacpack = false;
                bool broadcastEverything = false;

                // get item
                Item item = itemList[e.RowIndex];

                lock (main.conch)
                {
                    // get aircraft
                    Sim.Aircraft aircraft = GetAircraft(item);
                    // check which column was selected
                    switch (e.ColumnIndex)
                    {
                        case 8:
                            {
                                // check for non-network aircraft
                                if (aircraft == null || aircraft.owner != Sim.Obj.Owner.Network)
                                {
                                    message = Resources.Strings.WeatherObservation;
                                }
                                else if (main.sim != null)
                                {
                                    // update weather aircraft
                                    if (main.sim.weatherAircraft == aircraft)
                                    {
                                        // unset weather object
                                        main.sim.SetWeatherAircraft(null);
                                    }
                                    else
                                    {
                                        // set weather object
                                        main.sim.SetWeatherAircraft(aircraft);
                                    }
                                    // refresh
                                    refresh = true;
                                }
                            }
                            break;

                        case 9:
                            {
                                // check for network object
                                if (aircraft == null || aircraft.owner == Sim.Obj.Owner.Network)
                                {
                                    message = "You can not broadcast aircraft that you do not own.";
                                }
                                else
                                {
                                    // get broadcast
                                    broadcastObject = aircraft.broadcast;
                                    broadcastName = main.log.BroadcastName(aircraft.ownerModel);
                                    broadcastTacpack = Settings.Default.BroadcastTacpack;
                                    broadcastEverything = Settings.Default.AutoBroadcast;

                                    // broadcast dialog
                                    doBroadcast = true;
                                    // refresh
                                    refresh = true;
                                }
                            }
                            break;

                        case 10:
                            {
                                // check for global user
                                if (aircraft == null)
                                {
                                    message = "You can not record an aircraft that is from a different hub.";
                                }
                                else
                                {
                                    // check for recorded aircraft
                                    if (aircraft.owner == Sim.Obj.Owner.Recorder)
                                    {
                                        message = "You can not record an aircraft that is being played by the recorder.";
                                    }
                                    else
                                    {
                                        // check for aircraft
                                        if (aircraft != null)
                                        {
                                            // toggle record flag
                                            aircraft.record = aircraft.record ? false : true;
                                        }
                                        // refresh
                                        refresh = true;
                                    }
                                }
                            }
                            break;

                        case 11:
                            {
                                if (item.guid.Equals(main.guid))
                                {
                                    message = "You can not ignore yourself.";
                                }
                                // check owner
                                switch (aircraft.owner)
                                {
                                    case Sim.Obj.Owner.Me:
                                        message = "You can not ignore yourself.";
                                        break;

                                    case Sim.Obj.Owner.Recorder:
                                        message = "You can not ignore recorded aircraft.";
                                        break;

                                    case Sim.Obj.Owner.Network:
                                        if (aircraft.ownerNuid.Valid())
                                        {
                                            lock (main.conch)
                                            {
                                                // check if currently ignored
                                                if (main.log.IgnoreNode(aircraft.ownerNuid))
                                                {
                                                    main.log.RemoveIgnoreNode(aircraft.ownerNuid);
                                                }
                                                else
                                                {
                                                    main.log.AddIgnoreNode(aircraft.ownerNuid);
                                                }
                                            }
                                            // refresh
                                            refresh = true;
                                        }
                                        break;

                                    case Sim.Obj.Owner.Sim:
                                        {
                                            lock (main.conch)
                                            {
                                                // check if currently ignored
                                                if (main.log.IgnoreName(aircraft.flightPlan.callsign))
                                                {
                                                    main.log.RemoveIgnoreName(aircraft.flightPlan.callsign);
                                                }
                                                else
                                                {
                                                    main.log.AddIgnoreName(aircraft.flightPlan.callsign);
                                                }
                                            }
                                            // refresh
                                            refresh = true;
                                        }
                                        break;
                                }
                            }
                            break;
                    }
                }

                if (doBroadcast)
                {
                    // create broadcast form
                    BroadcastForm broadcastForm = new BroadcastForm(main, model, false, broadcastObject, broadcastName, broadcastTacpack, broadcastEverything);
                    // show form
                    DialogResult result = broadcastForm.ShowDialog();
                    // check result
                    if (result == DialogResult.OK)
                    {
                        lock (main.conch)
                        {
                            // get aircraft
                            Sim.Aircraft aircraft = GetAircraft(item);
                            // check for valid aircraft
                            if (aircraft != null)
                            {
                                // update object broadcast
                                aircraft.broadcast = broadcastForm.broadcastObject;
                                // update model broadcast
                                if (broadcastForm.broadcastModel)
                                {
                                    main.log.AddBroadcastName(model);
                                }
                                else
                                {
                                    main.log.RemoveBroadcastName(model);
                                }
                            }
                        }

                        // update tacpack broadcast
                        Settings.Default.BroadcastTacpack = broadcastForm.broadcastTacpack;
                        // update auto broadcast
                        Settings.Default.AutoBroadcast = broadcastForm.broadcastEverything;
                    }
                }

                // check for refresh
                if (refresh)
                {
                    RefreshWindow();
                }

                // check for message
                if (message.Length > 0)
                {
                    // show message
                    MessageBox.Show(message, Main.name + ": Aircraft");
                }
            }
        }

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void DataGrid_Aircraft_SelectionChanged(object sender, EventArgs e)
        {
            RefreshDetails(GetSelectedItem());
        }

        private void AircraftForm_Resize(object sender, EventArgs e)
        {
            // size detail
            Label_Details.Width = Width - listWidthOffset;
            Label_FlightPlan1.Width = Width - listWidthOffset;
            Label_FlightPlan2.Width = Width - listWidthOffset;

            // size list
            DataGrid_AircraftList.Height = Height - listHeightOffset;
            DataGrid_AircraftList.Width = Width - listWidthOffset;
        }

        private void Check_ListIgnoredAircraft_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void AircraftForm_Activated(object sender, EventArgs e)
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

        private void AircraftForm_Deactivate(object sender, EventArgs e)
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

        private void Check_GlobalUsers_CheckedChanged(object sender, EventArgs e)
        {
        }

        private void Context_Aircraft_RemoveFromRecording_Click(object sender, EventArgs e)
        {
            // get selected item
            Item selectedItem = GetSelectedItem();
            // check for valid selection
            if (selectedItem != null)
            {
                // show message
                DialogResult result = MessageBox.Show("Remove '" + selectedItem.callsign + "' from the recording?", Main.name + ": Recorder", MessageBoxButtons.YesNo);
                // check for confirmation
                if (result == DialogResult.Yes)
                {
                    // remove object from the recorder
                    main.recorder.Remove(selectedItem.netId);
                    // update window
                    RefreshWindow();
                }
            }
        }

        private void Context_Aircraft_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // disable all options
            Context_Aircraft_Substitute.Enabled = false;
            Context_Aircraft_Callsign.Enabled = false;
            Context_Aircraft_FlightPlan.Enabled = false;
            Context_Aircraft_Variables.Enabled = false;
            Context_Aircraft_AdjustHeight.Enabled = false;
            Context_Aircraft_Follow.Enabled = false;
            Context_Aircraft_EnterCockpit.Enabled = false;
            Context_Aircraft_TrackHeading.Enabled = false;
            Context_Aircraft_TrackBearing.Enabled = false;
            Context_Aircraft_CopyWeather.Enabled = false;
            Context_Aircraft_RemoveFromRecording.Enabled = false;
            string cockpitText = Resources.Strings.EnterCockpit;

            // get selected item
            Item item = GetSelectedItem();
            
            lock (main.conch)
            {
                // check if currently in another cockpit
                if (main.sim ?. enteredAircraft != null)
                {
                    // update button
                    cockpitText = "Leave Cockpit";
                    // can leave cockpit
                    Context_Aircraft_EnterCockpit.Enabled = true;
                }

                // check for selected item
                if (item != null)
                {
                    // update follow text
                    Context_Aircraft_Follow.Text = Resources.Strings.Follow + " '" + item.callsign + "'";

                    // get aircraft
                    Sim.Aircraft aircraft = GetAircraft(item);
                    // check for selection
                    if (aircraft != null)
                    {
                        // check if simulator connected
                        if (main.sim != null && main.sim.Connected)
                        {
                            // allow substitution
                            Context_Aircraft_Substitute.Enabled = true;
                            // enable flight plan
                            Context_Aircraft_FlightPlan.Enabled = true;
                            // enable variables
                            Context_Aircraft_Variables.Enabled = true;

                            // check if injected
                            if (aircraft.Injected)
                            {
                                // allow height adjustment
                                Context_Aircraft_AdjustHeight.Enabled = aircraft.subModel != null;
                            }
                            else
                            {
                                // allow callsign change
                                Context_Aircraft_Callsign.Enabled = true;
                            }

                            // check if aircraft is owned by someone else
                            if (aircraft.owner == Sim.Obj.Owner.Network)
                            {
                                // change to copy
                                Context_Aircraft_FlightPlan.Text = Resources.Strings.CopyFlightPlan;
                            }
                            else
                            {
                                // change to default
                                Context_Aircraft_FlightPlan.Text = defaultFlightPlanOption;
                            }

                            // check if not currently in another cockpit
                            if (main.sim.enteredAircraft == null)
                            {
                                // if aircraft is on the network
                                if (aircraft.owner == Sim.Obj.Owner.Network && aircraft.metar.Length > 0)
                                {
                                    // allow copy weather
                                    Context_Aircraft_CopyWeather.Enabled = true;
                                }

                                // check if aircraft is not user and data is valid
                                if (aircraft.owner != Sim.Obj.Owner.Me)
                                {
                                    // allow follow and enter
                                    Context_Aircraft_Follow.Enabled = true;
                                    // enable cockpit sharing
                                    Context_Aircraft_EnterCockpit.Enabled = true;
                                    // enable tracking
                                    Context_Aircraft_TrackHeading.Enabled = true;
                                    Context_Aircraft_TrackBearing.Enabled = true;
                                }
                            }
                        }

                        // get show on radar
                        Context_Aircraft_ShowOnRadar.CheckState = aircraft.showOnRadar ? CheckState.Checked : CheckState.Unchecked;
                    }

                    // check if selected aircraft is in the recorder
                    if (main.recorder.Exists(item.netId))
                    {
                        // enable menu item
                        Context_Aircraft_RemoveFromRecording.Enabled = true;
                    }

                    if (Context_Aircraft_EnterCockpit.Text.Equals(cockpitText) == false)
                    {
                        Context_Aircraft_EnterCockpit.Text = cockpitText;
                    }
                }
            }

            // get global aircraft
            Context_Aircraft_Hub.CheckState = Settings.Default.IncludeGlobalAircraft ? CheckState.Checked : CheckState.Unchecked;
            // get simlator aircraft
            Context_Aircraft_Simulator.CheckState = Settings.Default.IncludeSimulatorAircraft ? CheckState.Checked : CheckState.Unchecked;
            // get ignored aircraft
            Context_Aircraft_Ignored.CheckState = Settings.Default.IncludeIgnoredAircraft ? CheckState.Checked : CheckState.Unchecked;

            // check if tracking is on
            Context_Aircraft_StopTracking.Enabled = main.sim != null ? (main.sim.trackHeadingObject != null || main.sim.trackBearingObject != null) : false;

#if NO_HUBS
            Context_Aircraft_Hub.Visible = false;
#endif

            Context_Aircraft_EditFlightPlan.Visible = false;
            Context_Aircraft_ShowOnRadar.Visible = false;
        }

        private void Context_Aircraft_Substitute_Click(object sender, EventArgs e)
        {
            string model = "";
#if FS2024
            string livery = "";
#endif
            int typerole = 0;
            bool injected = false;

            lock (main.conch)
            {
                // get aircraft
                Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
                // check for selection
                if (aircraft != null)
                {
                    model = aircraft.ownerModel;
#if FS2024
                    livery = aircraft.ownerLivery;
#endif
                    typerole = aircraft.typerole;
                    injected = aircraft.Injected;
                }
            }

            // check for model
            if (model.Length > 0)
            {
                // check if aircraft is injected
                if (injected)
                {
                    // edit match
#if FS2024
                    main.substitution ?. EditMatch(model, livery, typerole);
#else
                    main.substitution ?. EditMatch(model, typerole);
#endif
                }
                else
                {
                    // edit masquerade
#if FS2024
                    main.substitution?.EditMasquerade(model, livery, typerole);
#else
                    main.substitution ?. EditMasquerade(model, typerole);
#endif
                }
            }
        }

        private void Context_Aircraft_Callsign_Click(object sender, EventArgs e)
        {
            string model = "";
            string originalCallsign = "";
            bool injected = false;

            lock (main.conch)
            {
                // get aircraft
                Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
                // check for selection
                if (aircraft != null)
                {
                    model = aircraft.ownerModel;
                    originalCallsign = aircraft.originalCallsign;
                    injected = aircraft.Injected;
                }
            }

            // check for model
            if (model.Length > 0 && injected == false)
            {
                // edit match
                main.substitution ?. EditCallsign(model, originalCallsign);
            }
        }

        public void Context_Aircraft_Follow_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // get aircraft
                Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
                // check for selection
                if (aircraft != null)
                {
                    // follow the selected aircraft
                    main.sim ?. ScheduleFollow(aircraft);
                }
            }
        }

        public void Context_Aircraft_EnterCockpit_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // check if currently in another cockpit
                if (main.sim != null && main.sim.enteredAircraft != null)
                {
                    // leave aircraft
                    main.sim.ScheduleLeave();
                }
                else
                {
                    // get aircraft
                    Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
                    // check for selection
                    if (aircraft != null)
                    {
                        // check if not network or cockpit is shared
                        if (aircraft.owner == Sim.Obj.Owner.Network && aircraft.CockpitShared == false)
                        {
                            // show message
                            main.ShowMessage(Resources.Strings.NoPermissionCockpit);
                        }
                        else if (aircraft.owner != Sim.Obj.Owner.Me)
                        {
                            // enter cockpit of other aircraft
                            main.sim ?. ScheduleEnterAircraft(aircraft);
                        }
                    }
                }
            }
        }

        private void Context_Aircraft_TrackHeading_Click(object sender, EventArgs e)
        {
            // get aircraft
            Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
            // check for valid aircraft
            if (main.sim != null && aircraft != null)
            {
                // start tracking aircarft
                main.sim.trackHeadingObject = aircraft;
                main.sim.trackBearingObject = null;
            }

            // refresh
            RefreshWindow();
        }

        private void Context_Aircraft_TrackBearing_Click(object sender, EventArgs e)
        {
            // get aircraft
            Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
            // check for valid aircraft
            if (main.sim != null && aircraft != null)
            {
                // start tracking aircarft
                main.sim.trackBearingObject = aircraft;
                main.sim.trackHeadingObject = null;
            }

            // refresh
            RefreshWindow();
        }

        private void Context_Aircraft_CopyWeather_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // get aircraft
                Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
                // check for selection
                if (aircraft != null)
                {
                    // set weather
                    main.sim ?. SetWeatherObservation(aircraft.metar);
                }
            }
        }

        private void Context_Aircraft_Public_Click(object sender, EventArgs e)
        {
            // update settings
            Settings.Default.IncludeGlobalAircraft = Context_Aircraft_Hub.CheckState != CheckState.Checked;
            // update window
            RefreshWindow();
        }

        private void Context_Aircraft_Ignored_Click(object sender, EventArgs e)
        {
            // update settings
            Settings.Default.IncludeIgnoredAircraft = Context_Aircraft_Ignored.CheckState != CheckState.Checked;
            // update window
            RefreshWindow();
        }

        private void Context_Aircraft_StopTracking_Click(object sender, EventArgs e)
        {
            // check for sim
            if (main.sim != null)
            {
                // stop tracking aircraft
                main.sim.trackHeadingObject = null;
                main.sim.trackBearingObject = null;
                // refresh
                RefreshWindow();
            }
        }

        private void Context_Aircraft_FlightPlan_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for valid aircraft
            if (item != null)
            {
                // target aircraft
                Sim.Aircraft targetAircraft;
                string callsign;
                string type;

                // get selected aircraft
                Sim.Aircraft aircraft = GetAircraft(item);
                // check if aircraft is on this system
                if (aircraft != null && aircraft.owner != Sim.Obj.Owner.Network)
                {
                    // target the selected aircraft
                    targetAircraft = aircraft;
                    callsign = item.callsign;
                    type = item.type;
                }
                else
                {
                    // target the user aircraft
                    targetAircraft = main.sim ?. userAircraft;
                    callsign = targetAircraft.flightPlan.callsign;
                    type = targetAircraft.flightPlan.icaoType;
                }

                // check for valid target aircraft
                if (targetAircraft != null)
                {
                    // create flight plan form
                    if (new FlightPlanForm(main, targetAircraft.flightPlan).ShowDialog() == DialogResult.OK)
                    {
                        lock (main.conch)
                        {
                            // update version
                            targetAircraft.flightPlanVersion++;
                            if (targetAircraft.flightPlanVersion == 0) targetAircraft.flightPlanVersion = 1;
                        }

                        // refresh
                        RefreshWindow();
                    }
                }
            }
        }

        private void Context_Aircraft_AdjustHeight_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();
            // check for valid aircraft
            if (item != null)
            {
                // get selected aircraft
                Sim.Aircraft aircraft = GetAircraft(item);
                // check for valid aircraft
                if (main.sim != null && aircraft != null && main.sim.Connected && aircraft.subModel != null)
                {
                    // height adjustment
                    int adjustment;

                    lock (main.conch)
                    {
                        // initialize height adjustment
                        adjustment = main.sim.GetHeightAdjustment(aircraft.subModel);
                    }

                    try
                    {
                        // create dialog for adjusting height
                        HeightForm heightForm = new HeightForm(main, aircraft.subModel, adjustment);
                        // show dialog
                        switch (heightForm.ShowDialog())
                        {
                            case System.Windows.Forms.DialogResult.Cancel:
                                lock (main.conch)
                                {
                                    // update sim
                                    main.sim.UpdateHeightAdjustment(aircraft.subModel, adjustment);
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        main.ShowMessage(ex.Message);
                    }

                    // save changes
                    main.ScheduleHeightAdjustmentSave();
                }
            }
        }

        private void Context_Aircraft_Simulator_Click(object sender, EventArgs e)
        {
            // update settings
            Settings.Default.IncludeSimulatorAircraft = Context_Aircraft_Simulator.CheckState != CheckState.Checked;
            // update window
            RefreshWindow();
        }

        private void AircraftForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.AircraftFormOpen = Visible;
        }

        private void AircraftForm_ResizeEnd(object sender, EventArgs e)
        {
            // save form position
            Settings.Default.AircraftFormLocation = Location;
            Settings.Default.AircraftFormSize = Size;
        }

        private void Context_Aircraft_EditFlightPlan_Click(object sender, EventArgs e)
        {
        }

        private void Context_Aircraft_ShowOnRadar_Click(object sender, EventArgs e)
        {
            // get aircraft
            Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
            // check for valid aircraft
            if (aircraft != null)
            {
                // toggle option
                aircraft.showOnRadar = aircraft.showOnRadar ? false : true;
            }

            // refresh
            RefreshWindow();
        }

        private void Context_Aircraft_Variables_Click(object sender, EventArgs e)
        {
            // check if no simulator connected
            if (main.sim != null && main.sim.Connected == false)
            {
                MessageBox.Show(Resources.Strings.AssignVariablesWarning, Main.name);
            }
            else
            {
                // get aircraft
                Sim.Aircraft aircraft = GetAircraft(GetSelectedItem());
                // check for valid aircraft
                if (aircraft != null)
                {
                    // show dialog for assigning variables
                    new VariablesForm(main, aircraft.ownerModel).ShowDialog();
                }
            }
        }
    }
}
