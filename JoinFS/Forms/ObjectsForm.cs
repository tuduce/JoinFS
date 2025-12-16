using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Globalization;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class ObjectsForm : Form
    {
        readonly Main main;

        /// <summary>
        /// Item
        /// </summary>
        class Item
        {
            public LocalNode.Nuid nuid;
            public uint simId;
            public string ownerName;
            public Sim.Obj.Owner owner;
            public string ownerModel;
#if FS2024
            public string ownerLivery;
#endif
            public string subModel;
            public int typerole;
            public int count;
            public string bearingText;
            public double distance;
            public string distanceText;
            public bool broadcast;
            public bool ignoreNode;
            public bool ignoreModel;

#if FS2024
            public Item(LocalNode.Nuid nuid, uint simId, string ownerName, Sim.Obj.Owner owner, string ownerModel, string ownerLivery, string subModel, int typerole, int count, string bearingText, double distance, bool broadcast, bool ignoreNode, bool ignoreModel)
#else
            public Item(LocalNode.Nuid nuid, uint simId, string ownerName, Sim.Obj.Owner owner, string ownerModel, string subModel, int typerole, int count, string bearingText, double distance, bool broadcast, bool ignoreNode, bool ignoreModel)
#endif
            {
                this.nuid = nuid;
                this.simId = simId;
                this.ownerName = ownerName;
                this.owner = owner;
                this.ownerModel = ownerModel;
#if FS2024
                this.ownerLivery = ownerLivery;
#endif
                this.subModel = subModel;
                this.typerole = typerole;
                this.count = count;
                this.bearingText = bearingText;
                this.distance = distance;
                this.distanceText = (distance * 0.00053995680346).ToString("N2");
                this.broadcast = broadcast;
                this.ignoreNode = ignoreNode;
                this.ignoreModel = ignoreModel;
            }
        }

        /// <summary>
        /// List of items
        /// </summary>
        List<Item> itemList = [];

        /// <summary>
        /// Item
        /// </summary>
        /// <remarks>
        /// Constructor
        /// </remarks>
        /// <param name="ownerNuid">Owner</param>
        /// <param name="model">Model</param>
        class Group(Sim.Obj obj, string ownerName)
        {
            /// <summary>
            /// Single instance
            /// </summary>
            public Sim.Obj obj = obj;
            /// <summary>
            /// Name of the owner
            /// </summary>
            public string ownerName = ownerName;
            /// <summary>
            /// Number of instances
            /// </summary>
            public int count = 1;
        }

        /// <summary>
        /// List of groups
        /// </summary>
        Dictionary<string, Group> groupList = [];

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
        /// Get the currently selected item
        /// </summary>
        /// <returns></returns>
        Item GetSelectedItem()
        {
            // selected item
            Item selectedItem = null;

            // check for selection
            if (DataGrid_ObjectList.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_ObjectList.SelectedRows[0].Index;

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

        public ObjectsForm(Main main)
        {
            this.main = main;
            
            InitializeComponent();

            // calculate offsets
            listHeightOffset = Height - DataGrid_ObjectList.Height;
            listWidthOffset = Width - DataGrid_ObjectList.Width;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // save window title
            title = Text;

            // change font
            DataGrid_ObjectList.DefaultCellStyle.Font = main.dataFont;
        }

        /// <summary>
        /// Get name of the owner
        /// </summary>
        /// <returns></returns>
        string GetOwnerName(Sim.Obj obj)
        {
            // check owner
            return obj.owner switch
            {
                Sim.Obj.Owner.Me or Sim.Obj.Owner.Sim => main.settingsNickname,
                Sim.Obj.Owner.Network => main.network.GetNodeName(obj.ownerNuid),
                Sim.Obj.Owner.Recorder => Resources.Strings.RecorderStr,
                _ => "",
            };
        }

        void AddObject(Sim.Obj obj, string ownerName, int count)
        {
            bool ignoreNode = false;
            bool ignoreModel = false;

            lock (main.conch)
            {
                // get ignore node state
                ignoreNode = obj.owner == Sim.Obj.Owner.Network && main.log.IgnoreNode(obj.ownerNuid);
                // get ignore model state
                ignoreModel = obj.owner == Sim.Obj.Owner.Network && main.log.IgnoreName(obj.ownerModel);
            }

            // check for show ignored objects
            if (Check_ListIgnoredObjects.CheckState == CheckState.Checked || ignoreNode == false && ignoreModel == false)
            {
                // model
                string subModel = obj.ModelTitle;
                // check for controlled object
                if (obj.ownerNuid.Valid())
                {
                    // check for substitution
                    if (obj.subType == Substitution.Type.Substitute)
                    {
                        // mark model
                        subModel += " (S)";
                    }
                    else if (obj.subType == Substitution.Type.Auto)
                    {
                        // mark model
                        subModel += " (A)";
                    }
                    else if (obj.subType == Substitution.Type.Default)
                    {
                        // mark model
                        subModel += " (D)";
                    }
                }

                // distance
                double distance = 0.0;
                // bearing
                string bearingText = "-";
                // broadcast
                bool broadcast = false;

                lock (main.conch)
                {
                    // get user position
                    Sim.Pos userPosition = main.sim ?. userAircraft ?. Position;
                    // get object position
                    Sim.Pos objPosition = obj.Position;
                    // check for single object
                    if (count == 1 && userPosition != null && objPosition != null)
                    {
                        // get distance to aircraft
                        distance = Vector.GeodesicDistance(objPosition.geo.x, objPosition.geo.z, userPosition.geo.x, userPosition.geo.z);
                        // get bearing
                        double bearing = Vector.GeodesicBearing(userPosition.geo.x, userPosition.geo.z, objPosition.geo.x, objPosition.geo.z);
                        // convert bearing
                        bearing *= 180.0 / Math.PI;
                        // set text
                        bearingText = ((int)bearing).ToString("D3");
                    }

                    // check for single object
                    if (count == 1)
                    {
                        // get broadcast state
                        broadcast = main.sim != null && main.sim.IsBroadcast(obj);
                    }
                    else
                    {
                        // get broadcast state
                        broadcast = main.log.BroadcastName(obj.ownerModel) || Settings.Default.BroadcastTacpack && Sim.IsTacpackModel(obj.ownerModel);
                    }
                }

                // create item
#if FS2024
                Item item = new(obj.ownerNuid, obj.simId, ownerName, obj.owner, obj.ownerModel, obj.ownerLivery, subModel, obj.typerole, count, bearingText, distance, broadcast, ignoreNode, ignoreModel);
#else
                Item item = new Item(obj.ownerNuid, obj.simId, ownerName, obj.owner, obj.ownerModel, subModel, obj.typerole, count, bearingText, distance, broadcast, ignoreNode, ignoreModel);
#endif
                // add item
                itemList.Add(item);
            }
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
            LocalNode.Nuid selectedNuid = (selectedItem != null) ? selectedItem.nuid : new LocalNode.Nuid();
            uint selectedId = (selectedItem != null) ? selectedItem.simId : 0;

            // clear object list
            itemList.Clear();

            // total objects
            int totalCount = 0;

            lock (main.conch)
            {
                // check for sim
                if (main.sim != null)
                {
                    // check for object groups
                    if (Settings.Default.GroupObjects)
                    {
                        // add objects
                        foreach (var obj in main.sim.objectList)
                        {
                            // check for base object
                            if ((obj is Sim.Aircraft) == false)
                            {
                                // create key
                                string key = obj.ownerNuid.ToString() + " " + obj.ownerModel;

                                // check for object
                                if (groupList.TryGetValue(key, out Group value))
                                {
                                    value.count++;
                                }
                                else
                                {
                                    // add new object
                                    groupList.Add(key, new Group(obj, GetOwnerName(obj)));
                                }
                            }
                        }

                        // for each group
                        foreach (var group in groupList)
                        {
                            // add object
                            AddObject(group.Value.obj, group.Value.ownerName, group.Value.count);
                            // add to count
                            totalCount += group.Value.count;
                        }

                        // clear group list
                        groupList.Clear();
                    }
                    else
                    {
                        // add objects
                        foreach (var obj in main.sim.objectList)
                        {
                            // check for base object
                            if ((obj is Sim.Aircraft) == false)
                            {
                                // add object
                                AddObject(obj, GetOwnerName(obj), 1);
                                // add to count
                                totalCount++;
                            }
                        }
                    }
                }
            }

            // check which column was selected
            switch (Settings.Default.SortObjectsColumn)
            {
                case 0:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.owner.CompareTo(i2.owner); });
                    break;
                case 1:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.subModel.CompareTo(i2.subModel); });
                    break;
                case 2:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.count.CompareTo(i2.count); });
                    break;
                case 3:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.bearingText.CompareTo(i2.bearingText); });
                    break;
                case 4:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.distance.CompareTo(i2.distance); });
                    break;
                case 5:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.broadcast.CompareTo(i2.broadcast); });
                    break;
                case 6:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.ignoreNode.CompareTo(i2.ignoreNode); });
                    break;
                case 7:
                    itemList.Sort(delegate (Item i1, Item i2) { return i1.ignoreModel.CompareTo(i2.ignoreModel); });
                    break;
            }

            // update window title
            Text = title + " (" + totalCount + ")";

            // rows
            DataGridViewRow[] rows = new DataGridViewRow[itemList.Count];
            // for each item
            for (int index = 0; index < itemList.Count; index++)
            {
                // create row
                rows[index] = new DataGridViewRow();
                rows[index].CreateCells(DataGrid_ObjectList);
                // fill row
                rows[index].Cells[0].Value = itemList[index].ownerName;
                rows[index].Cells[1].Value = itemList[index].subModel;
                rows[index].Cells[2].Value = itemList[index].count;
                rows[index].Cells[3].Value = itemList[index].bearingText;
                rows[index].Cells[4].Value = itemList[index].distanceText;
                rows[index].Cells[5].Value = itemList[index].broadcast;
                rows[index].Cells[6].Value = itemList[index].ignoreNode;
                rows[index].Cells[7].Value = itemList[index].ignoreModel;
            }

            // clear existing cells
            DataGrid_ObjectList.Rows.Clear();
            DataGrid_ObjectList.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_ObjectList.Rows.Count; index++)
            {
                // check for selected object
                if (itemList[index].nuid == selectedNuid && itemList[index].simId == selectedId)
                {
                    // select row
                    DataGrid_ObjectList.Rows[index].Selected = true;
                }
            }

            // check for selected object
            if (selectedItem != null)
            {
                RefreshButtons();
            }

            // reset refresh button
            Button_Refresh.BackColor = System.Drawing.SystemColors.ControlLight;
            // reset time
            resetRefreshButtonTime = main.ElapsedTime + RESET_REFRESH_BUTTON_DELAY;
        }

        void RefreshButtons()
        {
            // reset buttons
            bool subEnabled = false;

            // selected item
            Item selectedItem = GetSelectedItem();

            // check for selection
            if (selectedItem != null)
            {
                // check for key
                if (selectedItem.owner == Sim.Obj.Owner.Network)
                {
                    // allow model change
                    subEnabled = true;
                }

            }

            if (Button_Substitute.Enabled != subEnabled)
            {
                Button_Substitute.Enabled = subEnabled;
            }
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

        private void DataGrid_ObjectList_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check for header click
            if (e.RowIndex == -1)
            {
                Settings.Default.SortObjectsColumn = e.ColumnIndex;
                RefreshWindow();
            }
            // check for valid index
            else if (e.RowIndex >= 0 && e.RowIndex < itemList.Count)
            {
                // get item
                Item item = itemList[e.RowIndex];

                // check which column was selected
                switch (e.ColumnIndex)
                {
                    case 5:
                        {
                            // check for this node
                            if (item.owner == Sim.Obj.Owner.Network)
                            {
                                MessageBox.Show("You can not broadcast objects that are already present on the network.", Main.Name + ": Objects");
                            }
                            else
                            {
                                // create broadcast form
                                BroadcastForm broadcastForm = new(main, item.ownerModel, Settings.Default.GroupObjects, item.broadcast, main.log.BroadcastName(item.ownerModel), Settings.Default.BroadcastTacpack, Settings.Default.AutoBroadcast);
                                // show form
                                DialogResult result = broadcastForm.ShowDialog();
                                // check result
                                if (result == DialogResult.OK && main.sim != null)
                                {
                                    lock (main.conch)
                                    {
                                        // get object
                                        Sim.Obj obj = main.sim.objectList.Find(o => o.simId == item.simId);
                                        // check if object found
                                        if (obj != null)
                                        {
                                            // check for single selection
                                            if (Settings.Default.GroupObjects == false)
                                            {
                                                // update object broadcast
                                                obj.broadcast = broadcastForm.broadcastObject;
                                            }
                                            // update model broadcast
                                            if (broadcastForm.broadcastModel)
                                            {
                                                main.log.AddBroadcastName(obj.ownerModel);
                                            }
                                            else
                                            {
                                                main.log.RemoveBroadcastName(obj.ownerModel);
                                            }
                                        }
                                    }
                                    // update tacpack broadcast
                                    Settings.Default.BroadcastTacpack = broadcastForm.broadcastTacpack;
                                    // update tacpack broadcast
                                    Settings.Default.AutoBroadcast = broadcastForm.broadcastEverything;
                                }

                                RefreshWindow();
                            }
                        }
                        break;

                    case 6:
                        {
                            // check for this node
                            if (item.owner != Sim.Obj.Owner.Network)
                            {
                                MessageBox.Show("You can not ignore your own objects.", Main.Name + ": Objects");
                            }
                            else
                            {
                                lock (main.conch)
                                {
                                    // check if already in the log
                                    if (main.log.IgnoreNode(item.nuid))
                                    {
                                        // update log
                                        main.log.RemoveIgnoreNode(item.nuid);
                                    }
                                    else
                                    {
                                        // update log
                                        main.log.AddIgnoreNode(item.nuid);
                                    }
                                }
                                RefreshWindow();
                            }
                        }
                        break;

                    case 7:
                        {
                            // check for this node
                            if (item.owner != Sim.Obj.Owner.Network)
                            {
                                MessageBox.Show("You can not ignore your own objects.", Main.Name + ": Objects");
                            }
                            else
                            {
                                lock (main.conch)
                                {
                                    // check if already in the log
                                    if (main.log.IgnoreName(item.ownerModel))
                                    {
                                        // update log
                                        main.log.RemoveIgnoreName(item.ownerModel);
                                    }
                                    else
                                    {
                                        // update log
                                        main.log.AddIgnoreName(item.ownerModel);
                                    }
                                }
                                RefreshWindow();
                            }
                        }
                        break;
                }
            }
        }

        private void ObjectsForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.ObjectsFormLocation;
            Size size = Settings.Default.ObjectsFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.ObjectsFormLocation = Location;
                Settings.Default.ObjectsFormSize = Size;
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

            // initialize group check
            Check_Group.Checked = Settings.Default.GroupObjects;
        }

        private void ObjectsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Button_Substitute_Click(object sender, EventArgs e)
        {
            // get selected item
            Item item = GetSelectedItem();

            // check for selection
            if (item != null)
            {
                // edit model
#if FS2024
                if (main.substitution.EditMatch(item.ownerModel, item.ownerLivery, item.typerole))
#else
                if (main.substitution.EditMatch(item.ownerModel, item.typerole))
#endif
                {
                    RefreshWindow();
                }
            }
        }

        private void DataGrid_ObjectList_SelectionChanged(object sender, EventArgs e)
        {
            RefreshButtons();
        }

        private void ObjectForm_Resize(object sender, EventArgs e)
        {
            // size list
            DataGrid_ObjectList.Height = Height - listHeightOffset;
            DataGrid_ObjectList.Width = Width - listWidthOffset;
        }

        private void Check_ListIgnored_CheckedChanged(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Check_Group_CheckedChanged(object sender, EventArgs e)
        {
            Settings.Default.GroupObjects = Check_Group.Checked;
            RefreshWindow();
        }

        private void ObjectsForm_Activated(object sender, EventArgs e)
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

        private void ObjectsForm_Deactivate(object sender, EventArgs e)
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

        private void ObjectsForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.ObjectsFormOpen = Visible;
        }

        private void ObjectsForm_ResizeEnd(object sender, EventArgs e)
        {
            // save form position
            Settings.Default.ObjectsFormLocation = Location;
            Settings.Default.ObjectsFormSize = Size;
        }
    }
}
