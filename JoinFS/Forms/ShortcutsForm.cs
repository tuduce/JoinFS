using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class ShortcutsForm : Form
    {
        /// <summary>
        /// Offsets
        /// </summary>
        int listHeightOffset = 300;
        int listWidthOffset = 100;

        Main main;

        public ShortcutsForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // calculate offsets
            listHeightOffset = Height - DataGrid_Shortcuts.Height;
            listWidthOffset = Width - DataGrid_Shortcuts.Width;

            // change font
            DataGrid_Shortcuts.DefaultCellStyle.Font = main.dataFont;
        }

        private void ShortcutsForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.ShortcutsFormLocation;
            Size size = Settings.Default.ShortcutsFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.ShortcutsFormLocation = Location;
                Settings.Default.ShortcutsFormSize = Size;
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

            RefreshWindow();
        }

        int GetSelectedRow()
        {
            // check for selection
            if (DataGrid_Shortcuts.SelectedRows.Count > 0)
            {
                // get index
                return DataGrid_Shortcuts.SelectedRows[0].Index;
            }

            return -1;
        }

        void RefreshWindow()
        {
            // rows
            DataGridViewRow[] rows = new DataGridViewRow[6];
            // for each row
            for (int index = 0; index < rows.Length; index++)
            {
                // create row
                rows[index] = new DataGridViewRow();
                rows[index].CreateCells(DataGrid_Shortcuts);
            }
            // fill rows
            rows[0].Cells[0].Style.BackColor = Settings.Default.ShortcutNetwork ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
            rows[0].Cells[0].Style.ForeColor = Settings.Default.ShortcutNetwork ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;
            rows[0].Cells[0].Value = main.mainForm ?. networkShortcut.combination;
            rows[0].Cells[1].Value = Resources.Strings.Shortcuts_Network;
            rows[1].Cells[0].Style.BackColor = Settings.Default.ShortcutSimulator ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
            rows[1].Cells[0].Style.ForeColor = Settings.Default.ShortcutSimulator ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;
            rows[1].Cells[0].Value = main.mainForm ?. simulatorShortcut.combination;
            rows[1].Cells[1].Value = Resources.Strings.Shortcuts_Simulator;
            rows[2].Cells[0].Style.BackColor = Settings.Default.ShortcutAllowShared ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
            rows[2].Cells[0].Style.ForeColor = Settings.Default.ShortcutAllowShared ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;
            rows[2].Cells[0].Value = main.mainForm ?. allowSharedShortcut.combination;
            rows[2].Cells[1].Value = Resources.Strings.Shortcuts_Allow;
            rows[3].Cells[0].Style.BackColor = Settings.Default.ShortcutHandOver ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
            rows[3].Cells[0].Style.ForeColor = Settings.Default.ShortcutHandOver ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;
            rows[3].Cells[0].Value = main.mainForm ?. handOverShortcut.combination;
            rows[3].Cells[1].Value = Resources.Strings.Shortcuts_Controls;
            rows[4].Cells[0].Style.BackColor = Settings.Default.ShortcutEnterCockpit ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
            rows[4].Cells[0].Style.ForeColor = Settings.Default.ShortcutEnterCockpit ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;
            rows[4].Cells[0].Value = main.mainForm ?. enterShortcut.combination;
            rows[4].Cells[1].Value = Resources.Strings.Shortcuts_Enter;
            rows[5].Cells[0].Style.BackColor = Settings.Default.ShortcutFollow ? Settings.Default.ColourActiveBackground : Settings.Default.ColourWaitingBackground;
            rows[5].Cells[0].Style.ForeColor = Settings.Default.ShortcutFollow ? Settings.Default.ColourActiveText : Settings.Default.ColourWaitingText;
            rows[5].Cells[0].Value = main.mainForm ?. followShortcut.combination;
            rows[5].Cells[1].Value = Resources.Strings.Shortcuts_Follow;

            // clear existing cells
            DataGrid_Shortcuts.Rows.Clear();
            DataGrid_Shortcuts.Rows.AddRange(rows);
        }

        private void ShortcutsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void ShortcutsForm_Resize(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // size list
                DataGrid_Shortcuts.Height = Height - listHeightOffset;
                DataGrid_Shortcuts.Width = Width - listWidthOffset;
            }
        }


        private void ShortcutsForm_Activated(object sender, EventArgs e)
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

        private void ShortcutsForm_Deactivate(object sender, EventArgs e)
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

        /// <summary>
        /// Hide enable or disable option depending on the shortcut
        /// </summary>
        /// <param name="shortcut"></param>
        void ShowOption(bool enabled)
        {
            // check setting
            if (enabled)
            {
                Context_Shortcut_Disable.Visible = true;
            }
            else
            {
                Context_Shortcut_Enable.Visible = true;
            }
        }

        private void Context_Shortcut_Opening(object sender, CancelEventArgs e)
        {
            // initially hide
            Context_Shortcut_Enable.Visible = false;
            Context_Shortcut_Disable.Visible = false;
            Context_Shortcut_Change.Visible = false;

            // get selected shortcut
            int index = GetSelectedRow();
            switch (index)
            {
                case 0: ShowOption(Settings.Default.ShortcutNetwork); break;
                case 1: ShowOption(Settings.Default.ShortcutSimulator); break;
                case 2: ShowOption(Settings.Default.ShortcutAllowShared); break;
                case 3: ShowOption(Settings.Default.ShortcutHandOver); break;
                case 4: ShowOption(Settings.Default.ShortcutEnterCockpit); break;
                case 5: ShowOption(Settings.Default.ShortcutFollow); break;
            }

            // check for valid selection
            if (index >= 0)
            {
                Context_Shortcut_Change.Visible = true;
            }
        }

        private void Context_Shortcut_Enable_Click(object sender, EventArgs e)
        {
            // get selected shortcut
            int index = GetSelectedRow();
            switch (index)
            {
                case 0: Settings.Default.ShortcutNetwork = true; break;
                case 1: Settings.Default.ShortcutSimulator = true; break;
                case 2: Settings.Default.ShortcutAllowShared = true; break;
                case 3: Settings.Default.ShortcutHandOver = true; break;
                case 4: Settings.Default.ShortcutEnterCockpit = true; break;
                case 5: Settings.Default.ShortcutFollow = true; break;
            }

            RefreshWindow();
        }

        private void Context_Shortcut_Disable_Click(object sender, EventArgs e)
        {
            // get selected shortcut
            int index = GetSelectedRow();
            switch (index)
            {
                case 0: Settings.Default.ShortcutNetwork = false; break;
                case 1: Settings.Default.ShortcutSimulator = false; break;
                case 2: Settings.Default.ShortcutAllowShared = false; break;
                case 3: Settings.Default.ShortcutHandOver = false; break;
                case 4: Settings.Default.ShortcutEnterCockpit = false; break;
                case 5: Settings.Default.ShortcutFollow = false; break;
            }

            RefreshWindow();
        }

        private void Context_Shortcut_Change_Click(object sender, EventArgs e)
        {
            // show shortcut form
            ShortcutForm shortcutForm = new(main);
            // check which shortcut is selected
            switch (GetSelectedRow())
            {
                case 0: shortcutForm.combination = main.mainForm ?. networkShortcut.combination; break;
                case 1: shortcutForm.combination = main.mainForm ?. simulatorShortcut.combination; break;
                case 2: shortcutForm.combination = main.mainForm ?. allowSharedShortcut.combination; break;
                case 3: shortcutForm.combination = main.mainForm ?. handOverShortcut.combination; break;
                case 4: shortcutForm.combination = main.mainForm ?. enterShortcut.combination; break;
                case 5: shortcutForm.combination = main.mainForm ?. followShortcut.combination; break;
            }

            // initialize key combination
            main.mainForm.shortcutScanning = true;
            // check result
            if (shortcutForm.ShowDialog() == DialogResult.OK)
            {
                // get selected shortcut
                switch (GetSelectedRow())
                {
                    case 0: Settings.Default.ShortcutNetworkKey = shortcutForm.combination; break;
                    case 1: Settings.Default.ShortcutSimulatorKey = shortcutForm.combination; break;
                    case 2: Settings.Default.ShortcutAllowSharedKey = shortcutForm.combination; break;
                    case 3: Settings.Default.ShortcutHandOverKey = shortcutForm.combination; break;
                    case 4: Settings.Default.ShortcutEnterKey = shortcutForm.combination; break;
                    case 5: Settings.Default.ShortcutFollowKey = shortcutForm.combination; break;
                }
                // reload shortcuts
                main.mainForm ?. LoadShortcuts();
                // refresh
                RefreshWindow();
            }
            // initialize key combination
            main.mainForm.shortcutScanning = false;
        }

        private void ShortcutsForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.ShortcutsFormOpen = Visible;
        }

        private void ShortcutsForm_ResizeEnd(object sender, EventArgs e)
        {
            // check if initialized
            if (main != null)
            {
                // save form position
                Settings.Default.ShortcutsFormLocation = Location;
                Settings.Default.ShortcutsFormSize = Size;
            }
        }
    }
}
