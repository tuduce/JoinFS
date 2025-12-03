using System;
using System.Windows.Forms;
using System.IO;
using System.Drawing;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class MonitorForm : Form
    {
        Main main;

        /// <summary>
        /// Offsets
        /// </summary>
        int eventsHeightOffset = 300;
        int eventsWidthOffset = 100;

#if SIMCONNECT
        /// <summary>
        /// Frame counter
        /// </summary>
        int previousFrameCount = 0;
        double previousTime = 0.0;
#endif

        /// <summary>
        /// Time to reset refresh button
        /// </summary>
        double resetRefreshButtonTime = 0.0;
        const double RESET_REFRESH_BUTTON_DELAY = 30.0;

        public MonitorForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // calculate offsets
            eventsHeightOffset = Height - Text_Events.Height;
            eventsWidthOffset = Width - Text_Events.Width;

            // change font
            Text_Events.Font = main.dataFont;
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
            // clear events window
            Text_Events.Text = "";

            // check for monitor
            if (main.monitor != null)
            {
                lock (main.conch)
                {
                    // check for more than 50 lines
                    if (main.monitor.lines.Count > 50)
                    {
                        // add line to window
                        Text_Events.Text += "[Click 'View Logs' to see full log files]" + "\r\n";
                        Text_Events.Text += "..." + "\r\n";

                        // for each line
                        for (int count = 0; count < 50; count++)
                        {
                            // add line to window
                            Text_Events.Text += main.monitor.lines[main.monitor.lines.Count - 50 + count] + "\r\n";
                        }
                    }
                    else
                    {
                        // for each line
                        foreach (var line in main.monitor.lines)
                        {
                            // add line to window
                            Text_Events.Text += line + "\r\n";
                        }
                    }
                }
            }

            // move to bottom
            Text_Events.SelectionStart = Text_Events.Text.Length;
            Text_Events.ScrollToCaret();

#if SIMCONNECT
            // check for sim
            if (main.sim != null)
            {
                // get total frames since last update
                int totalFrames = main.sim.frameCount - previousFrameCount;
                string fpsText = "FPS: " + (totalFrames / Math.Max(0.1, main.ElapsedTime - previousTime)).ToString("N0");

                if (Label_FPS.Text.Equals(fpsText) == false)
                {
                    Label_FPS.Text = fpsText;
                }

                // update frame count
                previousFrameCount = main.sim.frameCount;
                previousTime = main.ElapsedTime;
            }
#endif

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

        private void MonitorForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.MonitorFormLocation;
            Size size = Settings.Default.MonitorFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.MonitorFormLocation = Location;
                Settings.Default.MonitorFormSize = Size;
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

            // get auto log
            Check_Save.CheckState = Settings.Default.AutoLog ? CheckState.Checked : CheckState.Unchecked;
        }

        private void Monitor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void MonitorForm_Activated(object sender, EventArgs e)
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

        private void Check_Save_CheckedChanged(object sender, EventArgs e)
        {
            // update save
            Settings.Default.AutoLog = Check_Save.CheckState == CheckState.Checked;

            lock (main.conch)
            {
                // check for monitor
                if (main.monitor != null)
                {
                    // check for auto log
                    if (Settings.Default.AutoLog)
                    {
                        // open log
                        main.monitor.OpenLog();
                    }
                    else
                    {
                        // close log
                        main.monitor.CloseLog();
                    }
                }
            }
        }

        private void Button_Refresh_Click(object sender, EventArgs e)
        {
            RefreshWindow();
        }

        private void Button_ViewLogs_Click(object sender, EventArgs e)
        {
            // check if log files exist
            bool exist1 = File.Exists(main.monitor.logName);
            bool exist2 = File.Exists(main.monitor.previousName);

            // open logs
            if (exist1)
            {
                Main.Launch(main.monitor.logName);
            }

            if (exist2)
            {
                Main.Launch(main.monitor.previousName);
            }

            // check if no log exists
            if (exist1 == false && exist2 == false)
            {
                // warning
                main.ShowMessage(Resources.Strings.RecordLogs);
            }
        }

        private void MonitorForm_Deactivate(object sender, EventArgs e)
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

        private void MonitorForm_Resize(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // size list
                Text_Events.Height = Height - eventsHeightOffset;
                Text_Events.Width = Width - eventsWidthOffset;
            }
        }

        private void MonitorForm_ResizeEnd(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // save form position
                Settings.Default.MonitorFormLocation = Location;
                Settings.Default.MonitorFormSize = Size;

                // refresh
                RefreshWindow();
            }
        }

        private void Context_Monitor_Node_Click(object sender, EventArgs e)
        {
            main.MonitorEvent("== NODE STATS ==");
            main.MonitorEvent("Session ID : " + main.network.localNode.Suid);
            main.MonitorEvent("Node Count : " + main.network.localNode.NodeCount);
            main.MonitorEvent("Routing Nodes : " + main.network.localNode.RoutingNodeCount);
            main.MonitorEvent("Guaranteed Incoming : " + main.network.localNode.GuaranteedInCount);
            main.MonitorEvent("Guaranteed Outgoing : " + main.network.localNode.GuaranteedOutCount);
            // check for hub
            if (main.settingsHub)
            {
                main.MonitorEvent("Online Users : " + main.network.OnlineUserCount);
            }
            RefreshWindow();
        }

        private void Context_Monitor_Packet_Click(object sender, EventArgs e)
        {
            main.MonitorEvent("== RECEIVED PACKETS ==");
            main.MonitorEvent("ID : Minute : Hour : Day : Total (count b/s)");
            main.MonitorEvent("Join : " + Stats.Join);
            main.MonitorEvent("JoinReply : " + Stats.JoinReply);
            main.MonitorEvent("JoinFail : " + Stats.JoinFail);
            main.MonitorEvent("Login : " + Stats.Login);
            main.MonitorEvent("LoginFail : " + Stats.LoginFail);
            main.MonitorEvent("Leave : " + Stats.Leave);
            main.MonitorEvent("AddNode : " + Stats.AddNode);
            main.MonitorEvent("Pulse : " + Stats.Pulse);
            main.MonitorEvent("PulseResponse : " + Stats.PulseResponse);
            main.MonitorEvent("GuaranteedDone : " + Stats.GuaranteedDone);
            main.MonitorEvent("Pathfinder : " + Stats.Pathfinder);
            main.MonitorEvent("PathfinderResponse : " + Stats.PathfinderResponse);
            main.MonitorEvent("ObjectPosition : " + Stats.ObjectPosition);
            main.MonitorEvent("AircraftPosition : " + Stats.AircraftPosition);
            main.MonitorEvent("SimEvent : " + Stats.SimEvent);
            main.MonitorEvent("WeatherRequest : " + Stats.WeatherRequest);
            main.MonitorEvent("WeatherReply : " + Stats.WeatherReply);
            main.MonitorEvent("WeatherUpdate : " + Stats.WeatherUpdate);
            main.MonitorEvent("SharedData : " + Stats.SharedData);
            main.MonitorEvent("StatusRequest : " + Stats.StatusRequest);
            main.MonitorEvent("Status : " + Stats.Status);
            main.MonitorEvent("HubList : " + Stats.HubList);
            main.MonitorEvent("RemoveObject : " + Stats.RemoveObject);
            main.MonitorEvent("UserListRequest : " + Stats.UserListRequest);
            main.MonitorEvent("UserList : " + Stats.UserList);
            main.MonitorEvent("UserList2 : " + Stats.UserList2);
            main.MonitorEvent("UserPositionRequest : " + Stats.UserPositionRequest);
            main.MonitorEvent("UserPositions : " + Stats.UserPositions);
            main.MonitorEvent("SessionCommsRequest : " + Stats.SessionCommsRequest);
            main.MonitorEvent("Notes : " + Stats.Notes);
            main.MonitorEvent("UserNuidRequest : " + Stats.UserNuidRequest);
            main.MonitorEvent("UserNuid : " + Stats.UserNuid);
            main.MonitorEvent("Online : " + Stats.Online);
            main.MonitorEvent("FlightPlanRequest : " + Stats.FlightPlanRequest);
            main.MonitorEvent("FlightPlan : " + Stats.FlightPlan);
            main.MonitorEvent("WrongVersion : " + Stats.WrongVersion);
            main.MonitorEvent("Total : " + Stats.Total);
            RefreshWindow();
        }

        private void Context_Monitor_Network_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                main.monitor.network = Context_Monitor_Network.CheckState == CheckState.Checked ? false : true;
            }
        }

        private void Context_Monitor_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Context_Monitor_Network.CheckState = main.monitor.network ? CheckState.Checked : CheckState.Unchecked;
            Context_Monitor_Variables.CheckState = main.monitor.variables ? CheckState.Checked : CheckState.Unchecked;
        }

        private void MonitorForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.MonitorFormOpen = Visible;
        }

        private void Context_Monitor_Variables_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                main.monitor.variables = Context_Monitor_Variables.CheckState == CheckState.Checked ? false : true;
            }
        }
    }
}
