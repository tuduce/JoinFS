using System;
using System.Collections.Generic;
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

            // set initial folder
            Text_Folder.Text = initialFolder;
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
            // update options
            main.settingsScan = Check_Scan.CheckState == CheckState.Checked;
            Settings.Default.ModelScanOnConnection = main.settingsScan;
            main.settingsGenerateCsl = Check_Generate.CheckState == CheckState.Checked;
            Settings.Default.GenerateCsl = main.settingsGenerateCsl;
            main.settingsSkipCsl = Check_Skip.CheckState == CheckState.Checked;
            Settings.Default.SkipCsl = main.settingsSkipCsl;
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