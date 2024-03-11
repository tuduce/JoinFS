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
            DataGrid_Options.Rows.Add(@"--create", Resources.strings.Options_Create);
#endif
            DataGrid_Options.Rows.Add(@"--join <address>", Resources.strings.Options_Join);
            DataGrid_Options.Rows.Add(@"--rejoin", Resources.strings.Options_Rejoin);
#if !NO_GLOBAL
            DataGrid_Options.Rows.Add(@"--global", Resources.strings.Options_Global);
#endif
            DataGrid_Options.Rows.Add(@"--nickname ""<name>"" ", Resources.strings.Options_Nickname);
            DataGrid_Options.Rows.Add(@"--port <port>", Resources.strings.Options_Port);
#if !NO_HUBS && !NO_CREATE
            DataGrid_Options.Rows.Add(@"--hub", Resources.strings.Tip_HubMode);
            DataGrid_Options.Rows.Add(@"--hubdomain ""<myserver.com>"" ", Resources.strings.Tip_HubDomain);
            DataGrid_Options.Rows.Add(@"--hubname ""<name>"" ", Resources.strings.Tip_HubName);
            DataGrid_Options.Rows.Add(@"--hubabout ""<text>"" ", Resources.strings.Tip_HubAbout);
            DataGrid_Options.Rows.Add(@"--hubvoip ""<text>"" ", Resources.strings.Tip_HubVoice);
            DataGrid_Options.Rows.Add(@"--hubevent ""<text>"" ", Resources.strings.Tip_HubEvent);
#endif
            DataGrid_Options.Rows.Add(@"--password", Resources.strings.Tip_Password);
            DataGrid_Options.Rows.Add(@"--play ""<file.jfs>"" ", Resources.strings.Options_Play);
            DataGrid_Options.Rows.Add(@"--record", Resources.strings.Options_Record);
            DataGrid_Options.Rows.Add(@"--loop", Resources.strings.Tip_Loop);
            DataGrid_Options.Rows.Add(@"--activitycircle <distance>", Resources.strings.Options_ActivityCircle);
            DataGrid_Options.Rows.Add(@"--follow <distance>", Resources.strings.Options_Follow);
            DataGrid_Options.Rows.Add(@"--atc", Resources.strings.Options_Atc);
            DataGrid_Options.Rows.Add(@"--airport <code>", Resources.strings.Options_Airport);
            DataGrid_Options.Rows.Add(@"--lowbandwidth", Resources.strings.Tip_LowBandwidth);
            DataGrid_Options.Rows.Add(@"--whazzup", Resources.strings.Tip_Whazzup);
            DataGrid_Options.Rows.Add(@"--whazzup-public", Resources.strings.Tip_WhazzupGlobal);
            DataGrid_Options.Rows.Add(@"--minimize", Resources.strings.Options_Minimize);
            DataGrid_Options.Rows.Add(@"--nosim", Resources.strings.Options_NoSim);
            DataGrid_Options.Rows.Add(@"--nogui", Resources.strings.Options_NoGui);
            DataGrid_Options.Rows.Add(@"--multiobjects", Resources.strings.Tip_MultiObjects);
            DataGrid_Options.Rows.Add(@"--simfolder", Resources.strings.Options_SimFolder);
            DataGrid_Options.Rows.Add(@"--xplane", Resources.strings.Tip_Xplane);
            DataGrid_Options.Rows.Add(@"--installplugin", Resources.strings.Options_InstallPlugin);
            DataGrid_Options.Rows.Add(@"--tcas", Resources.strings.Tip_TCAS);
            DataGrid_Options.Rows.Add(@"--quit", Resources.strings.Options_Quit);
            DataGrid_Options.Rows.Add(@"--help", Resources.strings.Options_Help);
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
