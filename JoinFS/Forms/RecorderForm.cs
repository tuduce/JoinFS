using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class RecorderForm : Form
    {
        Main main;

        /// <summary>
        /// Gap at either side of the trackbar
        /// </summary>
        int trackBorder = 50;
        int defaultHeight = 100;

        public RecorderForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // calculate gap around trackbar
            trackBorder = Width - Track_Position.Width;
            // get default height
            defaultHeight = Height;
            MaximumSize = new Size(Screen.PrimaryScreen.Bounds.Width, Height);
            MinimumSize = new Size(350, Height);
        }

        public void RefreshWindow()
        {
            string timeText = "00:00:00";
            int max = 0;
            int val = 0;
            bool enabled = false;

            lock (main.conch)
            {
                // refresh time
                if (main.recorder.Active)
                {
                    // get recorder time
                    timeText = (new TimeSpan(0, 0, (int)main.recorder.Time)).ToString();

                    if (main.recorder.recording == false)
                    {
                        // update track
                        max = (int)main.recorder.EndTime;
                        val = ((int)main.recorder.Time > max) ? max : (int)main.recorder.Time;
                        enabled = true;
                    }
                }
                else if (main.recorder.Empty == false)
                {
                    // update track
                    max = (int)main.recorder.EndTime;
                }
            }

            if (Label_Time.Text.Equals(timeText) == false)
            {
                Label_Time.Text = timeText;
            }
            if (Track_Position.Maximum != max)
            {
                Track_Position.Maximum = max;
            }
            if (Track_Position.Value != val)
            {
                Track_Position.Value = val;
            }
            if (Track_Position.Enabled != enabled)
            {
                Track_Position.Enabled = enabled;
            }

            // show recorder end time
            string endText = new TimeSpan(0, 0, (int)main.recorder.EndTime).ToString();
            if (Label_EndTime.Text.Equals(endText) == false)
            {
                Label_EndTime.Text = endText;
            }

            // set recorder menu items
            bool recordEnabled = main.recorder.Active ? false : true;
            bool overdubEnabled = (main.recorder.recording || main.recorder.Empty) ? false : true;
            bool playEnabled = (main.recorder.recording || main.recorder.Empty) ? false : true;
            bool stopEnabled = main.recorder.Active ? true : false;

            if (Button_Record.Enabled != recordEnabled)
            {
                Button_Record.Enabled = recordEnabled;
            }
            if (Button_Overdub.Enabled != overdubEnabled)
            {
                Button_Overdub.Enabled = overdubEnabled;
            }
            if (Button_Play.Enabled != playEnabled)
            {
                Button_Play.Enabled = playEnabled;
            }
            if (Button_Stop.Enabled != stopEnabled)
            {
                Button_Stop.Enabled = stopEnabled;
            }

            Image playImage;

            lock (main.conch)
            {
                // check if recorder is active
                if (main.recorder.playing && main.recorder.paused == false)
                {
                    // use pause image
                    playImage = Properties.Resources.pause;
                }
                else
                {
                    // use play image
                    playImage = Properties.Resources.play;
                }
            }

            // check if play button needs updating
            if (Button_Play.Image != playImage)
            {
                // set pause button
                Button_Play.Image = playImage;
            }
        }

        private void RecorderForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.RecorderFormLocation;
            Size size = Settings.Default.RecorderFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.RecorderFormLocation = Location;
                Settings.Default.RecorderFormSize = Size;
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

            // check for tool tips
            if (Settings.Default.ToolTips)
            {
                ToolTip tip = new ToolTip
                {
                    ShowAlways = true,
                    IsBalloon = true,
                    AutomaticDelay = 1500
                };
                tip.SetToolTip(Button_Record, Resources.Strings.Tip_Record);
                tip.SetToolTip(Button_Play, Resources.Strings.Tip_Play);
                tip.SetToolTip(Button_Stop, Resources.Strings.Tip_Stop);
                tip.SetToolTip(Button_Overdub, Resources.Strings.Tip_Overdub);
                tip.SetToolTip(Label_Time, Resources.Strings.Tip_Time);
                tip.SetToolTip(Label_EndTime, Resources.Strings.Tip_EndTime);
                tip.SetToolTip(Track_Position, Resources.Strings.Tip_Position);
                tip.SetToolTip(Check_Loop, Resources.Strings.Tip_Loop);
            }

            // initialize loop button
            Check_Loop.Checked = main.settingsLoop;
        }

        private void RecorderForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void RecorderForm_Resize(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // stretch trackbar
                Track_Position.Width = Width - trackBorder;
            }
        }

        private void Track_Position_Scroll(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // update recorder time
                main.recorder.Jump(Track_Position.Value);
            }
        }

        private void Button_Record_Click(object sender, EventArgs e)
        {
            main.mainForm ?. CheckRecording();

            lock (main.conch)
            {
                // start new recording
                main.recorder.StartRecord(false);
                // check for main form
                if (main.mainForm != null)
                {
                    // unsaved
                    main.mainForm.unsaved = true;
                }
            }

            RefreshWindow();
        }

        private void Button_Play_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                // check if currently playing
                if (main.recorder.playing)
                {
                    main.recorder.Pause();
                }
                else
                {
                    main.recorder.StartPlay();
                }
            }

            RefreshWindow();
#if !SERVER
            main.aircraftForm ?. refresher.Schedule(2);
#endif
        }

        private void Button_Stop_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                main.recorder.Stop();
            }
            
            RefreshWindow();
#if !SERVER
            main.aircraftForm ?. refresher.Schedule();
#endif
        }

        private void Button_Overdub_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                main.recorder.StartPlay();
                main.recorder.StartRecord(true);
                if (main.mainForm != null) main.mainForm.unsaved = true;
            }

            RefreshWindow();
#if !SERVER
            main.aircraftForm ?. refresher.Schedule(2);
#endif
        }

        private void RecorderForm_Activated(object sender, EventArgs e)
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

        private void RecorderForm_Deactivate(object sender, EventArgs e)
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

        private void RecorderMenu_File_OpenRecording_Click(object sender, EventArgs e)
        {
            if (main.recorder.Active)
            {
                MessageBox.Show("Recorder is currently active. Choose 'Recorder|Stop' from the menu before opening a recording.", Main.name + ": " + Resources.Strings.RecorderStr);
            }
            else
            {
                // open dialog to choose jfs file
                OpenFileDialog dialog = new OpenFileDialog
                {
                    InitialDirectory = Settings.Default.RecordingFolder,
                    Filter = "JoinFS files (*.jfs)|*.jfs",
                    FilterIndex = 1,
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // open file
                        Stream stream = null;
                        if ((stream = dialog.OpenFile()) != null)
                        {
                            using (stream)
                            {
                                lock (main.conch)
                                {
                                    main.recorder.Read(new BinaryReader(stream));
                                    main.recorder.StartPlay();
                                }
                            }
                            // save folder
                            Settings.Default.RecordingFolder = Path.GetDirectoryName(dialog.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, Main.name + ": " + Resources.Strings.RecorderStr);
                        main.MonitorEvent(ex.Message);
                    }
                }
            }
        }

        private void RecorderMenu_File_SaveRecording_Click(object sender, EventArgs e)
        {
            // check if recorder is active
            if (main.recorder.Active)
            {
                MessageBox.Show("Recorder is currently active. Choose 'Recorder|Stop' from the menu before opening a recording.", Main.name + ": " + Resources.Strings.RecorderStr);
            }
            else if (main.recorder.Empty)
            {
                MessageBox.Show(Resources.Strings.RecorderEmpty, Main.name + ": " + Resources.Strings.RecorderStr);
            }
            else
            {
                main.mainForm ?. SaveRecording();
            }
        }

        private void RecorderMenu_File_AddRecording_Click(object sender, EventArgs e)
        {
            if (main.recorder.Active)
            {
                MessageBox.Show("Recorder is currently active. Choose 'Recorder|Stop' from the menu before opening a recording.", Main.name + ": " + Resources.Strings.RecorderStr);
            }
            else
            {
                // open dialog to choose jfs file
                OpenFileDialog dialog = new OpenFileDialog
                {
                    InitialDirectory = Settings.Default.RecordingFolder,
                    Filter = "JoinFS files (*.jfs)|*.jfs",
                    FilterIndex = 1,
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // open file
                        Stream stream = null;
                        if ((stream = dialog.OpenFile()) != null)
                        {
                            using (stream)
                            {
                                lock (main.conch)
                                {
                                    main.recorder.Append(new BinaryReader(stream));
                                    main.recorder.StartPlay();
                                }
                            }
                            // save folder
                            Settings.Default.RecordingFolder = Path.GetDirectoryName(dialog.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, Main.name + ": " + Resources.Strings.RecorderStr);
                        main.MonitorEvent(ex.Message);
                    }
                }
            }
        }

        private void RecorderMenu_Edit_TrimFromStart_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                if (main.recorder.Time <= 0.0)
                {
                    MessageBox.Show("Already at the start of the recording.", Main.name + ": " + Resources.Strings.RecorderStr);
                }
                else
                {
                    main.recorder.TrimFromStart();
                    RefreshWindow();
                }
            }
        }

        private void RecorderMenu_Edit_TrimToEnd_Click(object sender, EventArgs e)
        {
            lock (main.conch)
            {
                if (main.recorder.Time >= main.recorder.EndTime)
                {
                    MessageBox.Show("Already at the end of the recording.", Main.name + ": " + Resources.Strings.RecorderStr);
                }
                else
                {
                    main.recorder.TrimToEnd();
                    RefreshWindow();
                }
            }
        }

        private void RecorderForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.RecorderFormOpen = Visible;
        }

        private void RecorderForm_ResizeEnd(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // save form position
                Settings.Default.RecorderFormLocation = Location;
                Settings.Default.RecorderFormSize = Size;
            }
        }

        private void Check_Loop_Click(object sender, EventArgs e)
        {
            // update recorder
            main.settingsLoop = Check_Loop.Checked;
            Settings.Default.Loop = main.settingsLoop;
            Settings.Default.Save();
        }
    }
}
