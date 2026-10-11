using System;
using System.Windows.Forms;
using System.IO;
using System.Drawing;
using JoinFS.Properties;
using JoinFS.Net;

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
            // clear events window
            Text_Events.Text = "";

            // check for monitor
            if (main.monitor != null)
            {
                // copy the last 50 lines
                string[] lines = main.monitor.CopyLines(50, out int total);

                // check for more than 50 lines
                if (total > 50)
                {
                    // add line to window
                    Text_Events.Text += "[Click 'View Logs' to see full log files]" + "\r\n";
                    Text_Events.Text += "..." + "\r\n";
                }

                // for each line
                foreach (var line in lines)
                {
                    // add line to window
                    Text_Events.Text += line + "\r\n";
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
                int totalFrames = main.sim.View.FrameCount - previousFrameCount;
                string fpsText = "FPS: " + (totalFrames / Math.Max(0.1, main.ElapsedTime - previousTime)).ToString("N0");

                if (Label_FPS.Text.Equals(fpsText) == false)
                {
                    Label_FPS.Text = fpsText;
                }

                // update frame count
                previousFrameCount = main.sim.View.FrameCount;
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
            main.monitor.WriteNodeStatistics();
            RefreshWindow();
        }

        private void Context_Monitor_Packet_Click(object sender, EventArgs e)
        {
            main.monitor.WritePacketStatistics();
            RefreshWindow();
        }

        private void Context_Monitor_Network_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                main.monitor.network = Context_Monitor_Network.CheckState != CheckState.Checked;
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
                main.monitor.variables = Context_Monitor_Variables.CheckState != CheckState.Checked;
            }
        }
    }
}
