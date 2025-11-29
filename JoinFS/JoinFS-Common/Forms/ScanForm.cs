using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class ScanForm : Form
    {
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
        /// List of addons
        /// </summary>
        public List<string> addOns = new List<string>();

        /// <summary>
        /// Addons selected
        /// </summary>
        public bool[] addOnsSelected;

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
        /// Get additionals
        /// </summary>
        /// <returns></returns>
        public string[] GetAdditionals()
        {
            // return additional
            return Text_Additional.Lines;
        }

        /// <summary>
        /// initial parameters
        /// </summary>
        string simulatorName;
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
                    result.Add(GetFolder() + Path.DirectorySeparatorChar + "SimObjects" + Path.DirectorySeparatorChar + folder);
                }
            }
            // for each additional folder
            foreach (var folder in Text_Additional.Lines)
            {
                // check for folder
                if (folder.Length > 0)
                {
                    // add folder to list
                    result.Add(folder);
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
                // clear grid
                DataGrid_Folders.Rows.Clear();
                // clear list
                folderList.Clear();
                // get simobject paths
                string[] paths = Directory.GetDirectories(Text_Folder.Text + Path.DirectorySeparatorChar + "SimObjects");
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
        }

        /// <summary>
        /// Refresh list of addons
        /// </summary>
        void RefreshAddOns()
        {
            try
            {
                // clear add-ons
                DataGrid_AddOns.Rows.Clear();
                // for each addon
                for (int index = 0; index < addOns.Count && index < addOnsSelected.Length; index++)
                {
                    // add to grid
                    DataGrid_AddOns.Rows.Add(addOnsSelected[index], addOns[index]);
                }
                // clear selection
                DataGrid_AddOns.ClearSelection();
            }
            catch
            {
            }
        }

        public ScanForm(Main main, string simFolder, string initialScanFolders, string initialAddOns, string initialAdditionals)
        {
            InitializeComponent();

            // set main
            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            this.simulatorName = main.sim.GetSimulatorName();
            this.initialFolder = simFolder;

            // change font
            Text_Folder.Font = main.dataFont;
            Label_Simulator.Font = main.dataFont;
            Text_Additional.Font = main.dataFont;
            DataGrid_Folders.DefaultCellStyle.Font = main.dataFont;
            DataGrid_AddOns.DefaultCellStyle.Font = main.dataFont;

            // get model scan
            Check_Scan.CheckState = Settings.Default.ModelScanOnConnection ? CheckState.Checked : CheckState.Unchecked;

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
            else
            {
                // add default scan folder
                scanFolders.Add("Airplanes");
                scanFolders.Add("Rotorcraft");
            }

            // set simulator name
            Label_Simulator.Text = simulatorName;
            // set initial folder
            Text_Folder.Text = initialFolder;

            // check for MSFS2020
            if (simulatorName == "Microsoft Flight Simulator 2020")
            {
                // change message
                Label_Specify.Text = "Please specify the 'Flight Simulator Packages' folder:";

                try
                {
                    lock (main.conch)
                    {
                        // check AddOns Were Loaded
                        if (Substitution.AddonsFileContents[0] != "")
                        {
                            // read all models from file
                            // for all lines

                            string Scancurraddon = "";
                            string Scanlastaddon = "";
                            int Scannaddons = 0;
                            foreach (string Scanline in Substitution.AddonsFileContents)
                            {
                                string[] separator = { "[+]" };
                                string[] Scanparts = Scanline.Split(separator, StringSplitOptions.None);
                                //count addons and split lines
                                Scanlastaddon = Scanparts[0];
                                if (Scancurraddon != Scanlastaddon)
                                {
                                    Scannaddons++;
                                    addOns.Add(Scanlastaddon);
                                }
                                // refresh current
                                Scancurraddon = Scanlastaddon;
                            }

                            // message
                            main.MonitorEvent("Loaded " + addOns.Count + " AddOns");
                        }
                        else
                        {
                            main.MonitorEvent("FS2020: No AddOns");
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
                // addons selected
                addOnsSelected = new bool[addOns.Count];
                // for each addon
                for (int index = 0; index < addOns.Count && index < addOnsSelected.Length; index++)
                {
                    // check if selected
                    addOnsSelected[index] = initialAddOns.Contains(addOns[index]);
                }
            }

            // check for MSFS2024
            if (simulatorName == "Microsoft Flight Simulator 2024")
            {
                // change message
                Label_Specify.Text = "Please specify the 'Flight Simulator Packages' folder:";

                // addons
                addOns.Add("My MSFS 2024");
                //addOns.Add("MSFS 2024 Standard");
                //addOns.Add("MSFS 2024 Deluxe");
                //addOns.Add("MSFS 2024 Premium Deluxe");
                //addOns.Add("MSFS 2024 Aviator");

                // addons selected
                addOnsSelected = new bool[addOns.Count];
                // for each addon
                for (int index = 0; index < addOns.Count && index < addOnsSelected.Length; index++)
                {
                    // check if selected
                    addOnsSelected[index] = initialAddOns.Contains(addOns[index]);
                }
            }

            // check for initial additionals
            if (initialAdditionals.Length > 0)
            {
                // get folder list
                string[] folders = initialAdditionals.Split('|');
                // for each folder
                foreach (string folder in folders)
                {
                    // check not first
                    if (Text_Additional.Text.Length > 0)
                    {
                        // add seperator
                        Text_Additional.Text += "\r\n";
                    }
                    // add to scan folders
                    Text_Additional.Text += folder;
                }
            }
        }

        private void Button_Browse_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select the main simulator folder",
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
            // update model scan
            main.settingsScan = Check_Scan.CheckState == CheckState.Checked;
            Settings.Default.ModelScanOnConnection = main.settingsScan;
        }

        private void ScanForm_Load(object sender, EventArgs e)
        {
            // refresh
            RefreshFolders();
            RefreshAddOns();
        }

        private void DataGrid_AddOns_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // check which column was selected
            switch (e.ColumnIndex)
            {
                case 0:
                    {
                        // check index
                        if (e.RowIndex >= 0 && e.RowIndex < addOnsSelected.Length)
                        {
                            // toggle addon
                            addOnsSelected[e.RowIndex] = !addOnsSelected[e.RowIndex];
                            // check for valid cell
                            if (e.RowIndex < DataGrid_AddOns.Rows.Count && DataGrid_AddOns.Rows[e.RowIndex].Cells.Count > 0)
                            {
                                // update selected state
                                DataGrid_AddOns.Rows[e.RowIndex].Cells[0].Value = addOnsSelected[e.RowIndex];
                            }
                        }
                    }
                    break;
            }
        }
    }
}
