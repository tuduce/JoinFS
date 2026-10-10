using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class ScanForm_XPLANE : Form
    {
#if XPLANE
        /// <summary>
        /// Main instance
        /// </summary>
        Main main;

        /// <summary>
        /// List of folders to scan
        /// </summary>
        public List<string> scanFolders = new List<string>();

        /// <summary>
        /// List of folders found
        /// </summary>
        public List<string> folderList = new List<string>();

        /// <summary>
        /// Get simulator folder
        /// </summary>
        /// <returns></returns>
        public string GetFolder()
        {
            // return folder
            return Text_Folder.Text;
        }

        /// <summary>
        /// initial parameters
        /// </summary>
        string initialFolder;

        /// <summary>
        /// Get list of scan folders
        /// </summary>
        /// <returns>Folder list</returns>
        public string[] GetFolders()
        {
            // create list of folders
            List<string> result = new List<string>();
            // for each folder
            foreach (var folder in folderList)
            {
                // check if folder should be scanned
                if (scanFolders.Contains(folder))
                {
                    // add folder to list
                    result.Add(Path.Combine(GetFolder(), "Aircraft", folder));
                }
            }
            // return list of folders
            return result.ToArray();
        }

        /// <summary>
        /// True while the saved selection was empty (= every folder is scanned) and the user has not
        /// ticked or unticked a folder yet
        /// </summary>
        bool selectAllUntilChanged;

        /// <summary>
        /// True when every listed aircraft folder is ticked, which is what "scan everything" is
        /// saved as (so aircraft folders installed later are scanned too)
        /// </summary>
        public bool AllFoldersSelected()
        {
            return folderList.Count > 0 && folderList.All(folder => scanFolders.Contains(folder));
        }

        /// <summary>
        /// Refresh list of folders
        /// </summary>
        void RefreshFolders()
        {
            try
            {
                // CSL folder
                Text_CSL.Text = Path.Combine(Text_Folder.Text, "Resources", "plugins", "JoinFS", "Resources", "CSL");
                // clear grid
                DataGrid_Folders.Rows.Clear();
                // clear list
                folderList.Clear();
                // get simobject paths
                string[] paths = Directory.GetDirectories(Path.Combine(Text_Folder.Text, "Aircraft"));
                // for each folder
                foreach (var path in paths)
                {
                    // get folder
                    string folder = Path.GetFileName(path);
                    // an empty saved selection means "scan every aircraft folder": show it as such
                    // until the user picks folders themselves
                    if (selectAllUntilChanged && scanFolders.Contains(folder) == false)
                    {
                        scanFolders.Add(folder);
                    }
                    // add entry
                    DataGrid_Folders.Rows.Add(scanFolders.Contains(folder), folder);
                    // add to list
                    folderList.Add(folder);
                }
                DataGrid_Folders.ClearSelection();
            }
            catch
            {
            }

            // validate generate
            DataGrid_Folders.Visible = Check_Generate.CheckState == CheckState.Checked;
            Check_Skip.Enabled = Check_Generate.CheckState == CheckState.Checked;
            if (Check_Liveries != null)
            {
                Check_Liveries.Enabled = Check_Generate.CheckState == CheckState.Checked;
            }
        }

        public ScanForm_XPLANE(Main main, string simFolder, string initialScanFolders)
        {
            InitializeComponent();

            // set main
            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            this.initialFolder = simFolder;

            // change font
            Text_Folder.Font = main.dataFont;
            Text_CSL.Font = new Font(main.dataFont.Name, 7.0f);
            DataGrid_Folders.DefaultCellStyle.Font = main.dataFont;

            // get check boxes
            Check_Scan.CheckState = Settings.Default.ModelScanOnConnection ? CheckState.Checked : CheckState.Unchecked;
            Check_Generate.CheckState = Settings.Default.GenerateCsl ? CheckState.Checked : CheckState.Unchecked;
            Check_Skip.CheckState = Settings.Default.SkipCsl ? CheckState.Checked : CheckState.Unchecked;

            // nothing saved: every aircraft folder is scanned, so start with all of them ticked
            selectAllUntilChanged = initialScanFolders.Length == 0;

            // check for initial scan folders
            if (initialScanFolders.Length > 0)
            {
                // get folder list
                string[] folders = initialScanFolders.Split('|');
                // for each folder
                foreach (string folder in folders)
                {
                    // add to scan folders
                    scanFolders.Add(folder);
                }
            }

            // the liveries option, then the other detected installations (XP11/XP12/Steam), if any
            AddLiveriesCheckbox();
            AddCslSourcesList();
            AddInstallPicker(XPlaneInstallLocator.FindInstalls());

            // set initial folder
            Text_Folder.Text = initialFolder;
        }

        /// <summary>
        /// A detected installation as shown in the picker
        /// </summary>
        sealed class InstallChoice
        {
            public XPlaneInstall Install { get; }

            public InstallChoice(XPlaneInstall install)
            {
                Install = install;
            }

            public override string ToString()
            {
                return "X-Plane " + Install.Version + " - " + Install.Path;
            }
        }

        /// <summary>
        /// Add a drop-down below the folder box that lists every detected installation. Only
        /// shown when there is a real choice; the folder box stays the single source of the value.
        /// Built in code so the localized designer layouts need no change.
        /// </summary>
        void AddInstallPicker(IReadOnlyList<XPlaneInstall> installs)
        {
            if (installs.Count < 2)
            {
                return;
            }

            const int gap = 6;
            ComboBox picker = new()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = main.dataFont,
                Left = Text_Folder.Left,
                Width = Text_Folder.Width,
                Top = Text_Folder.Bottom + gap,
                Anchor = Text_Folder.Anchor,
            };
            SuspendLayout();
            MakeRoom(picker.Top, picker.Height + gap);

            foreach (XPlaneInstall install in installs)
            {
                picker.Items.Add(new InstallChoice(install));
            }
            picker.SelectedIndexChanged += (sender, args) =>
            {
                if (picker.SelectedItem is InstallChoice choice)
                {
                    Text_Folder.Text = choice.Install.Path;
                }
            };
            Controls.Add(picker);
            ResumeLayout();

            // pre-select the installation that is currently in use
            for (int index = 0; index < picker.Items.Count; index++)
            {
                if (string.Equals(((InstallChoice)picker.Items[index]).Install.Path, initialFolder, StringComparison.OrdinalIgnoreCase))
                {
                    picker.SelectedIndex = index;
                }
            }
        }

        /// <summary>
        /// Push every control at or below <paramref name="top"/> down by <paramref name="shift"/> and
        /// grow the form by the same amount, to fit a control added in code
        /// </summary>
        void MakeRoom(int top, int shift)
        {
            foreach (Control control in Controls)
            {
                bool anchoredTop = (control.Anchor & AnchorStyles.Top) != 0;
                bool anchoredBottom = (control.Anchor & AnchorStyles.Bottom) != 0;
                if (control.Top >= top && anchoredTop)
                {
                    control.Top += shift;
                    if (anchoredBottom)
                    {
                        // stretches with the form, so the resize below gives the height back
                        control.Height -= shift;
                    }
                }
            }
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift);
        }

        /// <summary>
        /// The "generate liveries" option, below "skip CSL objects already done". Built in code
        /// so the localized designer layouts need no change.
        /// </summary>
        CheckBox Check_Liveries;

        void AddLiveriesCheckbox()
        {
            const int gap = 6;
            Check_Liveries = new CheckBox
            {
                AutoSize = true,
                Text = Resources.Strings.GenerateLiveries,
                Checked = Settings.Default.GenerateLiveries,
                // under "generate CSL" on the left: the right-hand column has no room for the label
                Left = Check_Generate.Left,
                Top = Math.Max(Check_Generate.Bottom, Check_Skip.Bottom) + gap,
                Anchor = Check_Generate.Anchor,
                Enabled = Check_Generate.CheckState == CheckState.Checked,
            };
            Check_Liveries.MaximumSize = new Size(Math.Max(100, ClientSize.Width - Check_Liveries.Left - 12), 0);

            SuspendLayout();
            MakeRoom(Check_Liveries.Top, Check_Liveries.Height + gap);
            Controls.Add(Check_Liveries);
            ResumeLayout();
        }

        /// <summary>
        /// Installed CSL packs (X-CSL, Bluebell, IVAO_CSL ...) offered for linking; all ticked unless the
        /// user switched one off earlier. Built in code so the localized designer layouts stay as they are.
        /// </summary>
        CheckedListBox Check_CslPacks;
        readonly List<string> cslPackNames = new List<string>();

        void AddCslSourcesList()
        {
            IReadOnlyList<XPlaneCslSource> sources = XPlaneCslSources.Discover(initialFolder);
            if (sources.Count == 0)
            {
                return;
            }

            const int gap = 6;
            HashSet<string> unlinked = new HashSet<string>(
                (Settings.Default.UnlinkedCslSources ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            Label caption = new Label
            {
                AutoSize = true,
                Text = Resources.Strings.UseInstalledCsl,
                Left = Check_Generate.Left,
                Top = Check_Liveries.Bottom + gap,
                Anchor = Check_Generate.Anchor,
            };
            Check_CslPacks = new CheckedListBox
            {
                CheckOnClick = true,
                Font = main.dataFont,
                Left = Check_Generate.Left,
                Width = Text_Folder.Width,
                Top = caption.Bottom + 2,
                Anchor = Check_Generate.Anchor,
                IntegralHeight = false,
            };
            foreach (XPlaneCslSource source in sources)
            {
                cslPackNames.Add(source.Name);
                Check_CslPacks.Items.Add(source.Name + " (" + source.Packages + ")", unlinked.Contains(source.Name) == false);
            }
            Check_CslPacks.Height = Math.Min(sources.Count, 4) * Check_CslPacks.ItemHeight + 6;

            SuspendLayout();
            MakeRoom(caption.Top, caption.Height + 2 + Check_CslPacks.Height + gap);
            Controls.Add(caption);
            Controls.Add(Check_CslPacks);
            ResumeLayout();
        }

        /// <summary>
        /// Names of the packs the user switched off, '|' separated, for the settings
        /// </summary>
        string UnlinkedPackNames()
        {
            if (Check_CslPacks == null)
            {
                return Settings.Default.UnlinkedCslSources ?? "";
            }

            List<string> off = new List<string>();
            for (int index = 0; index < cslPackNames.Count; index++)
            {
                if (Check_CslPacks.GetItemChecked(index) == false)
                {
                    off.Add(cslPackNames[index]);
                }
            }
            return string.Join("|", off);
        }

        private void Button_Browse_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select the root X-Plane folder",
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // get simulator folder
                Text_Folder.Text = dialog.SelectedPath;
                RefreshFolders();
            }
        }

        private void Text_Folder_TextChanged(object sender, EventArgs e)
        {
            RefreshFolders();
        }

        private void DataGrid_Folders_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // check which column was selected
            switch (e.ColumnIndex)
            {
                case 0:
                    {
                        // check index
                        if (e.RowIndex >= 0 && e.RowIndex < folderList.Count)
                        {
                            // from now on the ticks are the user's own choice
                            selectAllUntilChanged = false;
                            // check if folder is scanned
                            if (scanFolders.Contains(folderList[e.RowIndex]))
                            {
                                // no longer scanned
                                scanFolders.Remove(folderList[e.RowIndex]);
                                // check for valid cell
                                if (e.RowIndex < DataGrid_Folders.Rows.Count && DataGrid_Folders.Rows[e.RowIndex].Cells.Count > 0)
                                {
                                    // update selected state
                                    DataGrid_Folders.Rows[e.RowIndex].Cells[0].Value = false;
                                }
                            }
                            else
                            {
                                // add folder
                                scanFolders.Add(folderList[e.RowIndex]);
                                // check for valid cell
                                if (e.RowIndex < DataGrid_Folders.Rows.Count && DataGrid_Folders.Rows[e.RowIndex].Cells.Count > 0)
                                {
                                    // update selected state
                                    DataGrid_Folders.Rows[e.RowIndex].Cells[0].Value = true;
                                }
                            }
                        }
                    }
                    break;
            }
        }

        private void Button_Scan_Click(object sender, EventArgs e)
        {
            // refuse folders that are not an X-Plane install and keep the dialog open
            if (XPlaneForm.ConfirmValidFolder(Text_Folder.Text) == false)
            {
                DialogResult = DialogResult.None;
                return;
            }

            // update options
            main.settingsScan = Check_Scan.CheckState == CheckState.Checked;
            Settings.Default.ModelScanOnConnection = main.settingsScan;
            main.settingsGenerateCsl = Check_Generate.CheckState == CheckState.Checked;
            Settings.Default.GenerateCsl = main.settingsGenerateCsl;
            main.settingsSkipCsl = Check_Skip.CheckState == CheckState.Checked;
            Settings.Default.SkipCsl = main.settingsSkipCsl;
            main.settingsGenerateLiveries = Check_Liveries.Checked;
            Settings.Default.GenerateLiveries = main.settingsGenerateLiveries;
            Settings.Default.UnlinkedCslSources = UnlinkedPackNames();
            Settings.Default.Save();
        }

        private void ScanForm_Load(object sender, EventArgs e)
        {
            // refresh
            RefreshFolders();
        }

        private void Check_Generate_CheckedChanged(object sender, EventArgs e)
        {
            // update option
            RefreshFolders();
        }
#endif
    }
}