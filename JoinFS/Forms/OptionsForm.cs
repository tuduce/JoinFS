using System;
using System.Drawing;
using System.Windows.Forms;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class OptionsForm : Form
    {
        /// <summary>
        /// Offsets
        /// </summary>
        int listHeightOffset = 300;
        int listWidthOffset = 100;

        Main main;

        public OptionsForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // calculate offsets
            listHeightOffset = Height - DataGrid_Options.Height;
            listWidthOffset = Width - DataGrid_Options.Width;

            // change font
            DataGrid_Options.DefaultCellStyle.Font = main.dataFont;
        }

        private void OptionsForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.OptionsFormLocation;
            Size size = Settings.Default.OptionsFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.OptionsFormLocation = Location;
                Settings.Default.OptionsFormSize = Size;
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

#if !NO_CREATE
            DataGrid_Options.Rows.Add(@"--create", Resources.Strings.Options_Create);
#endif
            DataGrid_Options.Rows.Add(@"--join <address>", Resources.Strings.Options_Join);
            DataGrid_Options.Rows.Add(@"--rejoin", Resources.Strings.Options_Rejoin);
#if !NO_GLOBAL
            DataGrid_Options.Rows.Add(@"--global", Resources.Strings.Options_Global);
#endif
            DataGrid_Options.Rows.Add(@"--nickname ""<name>"" ", Resources.Strings.Options_Nickname);
            DataGrid_Options.Rows.Add(@"--port <port>", Resources.Strings.Options_Port);
#if !NO_HUBS && !NO_CREATE
            DataGrid_Options.Rows.Add(@"--hub", Resources.Strings.Tip_HubMode);
            DataGrid_Options.Rows.Add(@"--hubdomain ""<myserver.com>"" ", Resources.Strings.Tip_HubDomain);
            DataGrid_Options.Rows.Add(@"--hubname ""<name>"" ", Resources.Strings.Tip_HubName);
            DataGrid_Options.Rows.Add(@"--hubabout ""<text>"" ", Resources.Strings.Tip_HubAbout);
            DataGrid_Options.Rows.Add(@"--hubvoip ""<text>"" ", Resources.Strings.Tip_HubVoice);
            DataGrid_Options.Rows.Add(@"--hubevent ""<text>"" ", Resources.Strings.Tip_HubEvent);
#endif
            DataGrid_Options.Rows.Add(@"--password", Resources.Strings.Tip_Password);
            DataGrid_Options.Rows.Add(@"--play ""<file.jfs>"" ", Resources.Strings.Options_Play);
            DataGrid_Options.Rows.Add(@"--record", Resources.Strings.Options_Record);
            DataGrid_Options.Rows.Add(@"--loop", Resources.Strings.Tip_Loop);
            DataGrid_Options.Rows.Add(@"--activitycircle <distance>", Resources.Strings.Options_ActivityCircle);
            DataGrid_Options.Rows.Add(@"--follow <distance>", Resources.Strings.Options_Follow);
            DataGrid_Options.Rows.Add(@"--atc", Resources.Strings.Options_Atc);
            DataGrid_Options.Rows.Add(@"--airport <code>", Resources.Strings.Options_Airport);
            DataGrid_Options.Rows.Add(@"--lowbandwidth", Resources.Strings.Tip_LowBandwidth);
            DataGrid_Options.Rows.Add(@"--whazzup", Resources.Strings.Tip_Whazzup);
            DataGrid_Options.Rows.Add(@"--whazzup-public", Resources.Strings.Tip_WhazzupGlobal);
            DataGrid_Options.Rows.Add(@"--minimize", Resources.Strings.Options_Minimize);
            DataGrid_Options.Rows.Add(@"--nosim", Resources.Strings.Options_NoSim);
            DataGrid_Options.Rows.Add(@"--nogui", Resources.Strings.Options_NoGui);
            DataGrid_Options.Rows.Add(@"--multiobjects", Resources.Strings.Tip_MultiObjects);
            DataGrid_Options.Rows.Add(@"--simfolder", Resources.Strings.Options_SimFolder);
            DataGrid_Options.Rows.Add(@"--xplane", Resources.Strings.Tip_Xplane);
            DataGrid_Options.Rows.Add(@"--installplugin", Resources.Strings.Options_InstallPlugin);
            DataGrid_Options.Rows.Add(@"--tcas", Resources.Strings.Tip_TCAS);
            DataGrid_Options.Rows.Add(@"--quit", Resources.Strings.Options_Quit);
            DataGrid_Options.Rows.Add(@"--help", Resources.Strings.Options_Help);
        }

        private void OptionsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void OptionsForm_Resize(object sender, EventArgs e)
        {
            if (main != null)
            {
                // size list
                DataGrid_Options.Height = Height - listHeightOffset;
                DataGrid_Options.Width = Width - listWidthOffset;
            }
        }


        private void OptionsForm_Activated(object sender, EventArgs e)
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

        private void OptionsForm_Deactivate(object sender, EventArgs e)
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

        private void OptionsForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.OptionsFormOpen = Visible;
        }

        private void OptionsForm_ResizeEnd(object sender, EventArgs e)
        {
            if (main != null)
            {
                // save form position
                Settings.Default.OptionsFormLocation = Location;
                Settings.Default.OptionsFormSize = Size;
            }
        }
    }
}
