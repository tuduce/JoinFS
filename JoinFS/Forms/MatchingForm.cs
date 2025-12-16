using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using JoinFS.Properties;

namespace JoinFS
{
    public partial class MatchingForm : Form
    {
        readonly Main main;

        /// <summary>
        /// Offsets
        /// </summary>
        int listHeightOffset = 300;
        int listWidthOffset = 100;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm">Main form</param>
        public MatchingForm(Main main)
        {
            InitializeComponent();

            this.main = main;

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // calculate offsets
            listHeightOffset = Height - DataGrid_Substitutions.Height;
            listWidthOffset = Width - DataGrid_Substitutions.Width;

            // change font
            DataGrid_Substitutions.DefaultCellStyle.Font = main.dataFont;

            // show livery for MSFS2024 only
            // at this time, the sim object has no simname yet :(
#if FS2024
            DataGrid_Substitutions.Columns[2].Visible = true;
#else
            DataGrid_Substitutions.Columns[2].Visible = false;
#endif
        }

        /// <summary>
        /// Item
        /// </summary>
        class Item
        {
            public string model;
            public string substitute;
#if FS2024
            public string livery;
            public string substituteLivery;

            public Item(string model, string substitute, string livery, string substLivery)
#else
            public Item(string model, string substitute)
#endif
            {
                this.model = model;
                this.substitute = substitute;
#if FS2024
                this.livery = livery;
                this.substituteLivery = substLivery;
#endif
            }
        }

        /// <summary>
        /// list of items
        /// </summary>
        List<Item> itemList = [];

        /// <summary>
        /// Get the currently selected model
        /// </summary>
        /// <returns></returns>
        string GetSelectedModel()
        {
            // selected model
            string selectedModel = null;

            // check for selection
            if (DataGrid_Substitutions.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_Substitutions.SelectedRows[0].Index;

                // check index
                if (index >= 0 && index < itemList.Count)
                {
                    // get selected model
                    selectedModel = itemList[index].model;
                }
            }

            // check if no selected model
            if (selectedModel == null && itemList.Count > 0)
            {
                // select first model
                selectedModel = itemList[0].model;
            }

            // return selected model
            return selectedModel;
        }

#if FS2024
        Substitution.Model GetSelectedModelObject()
        {
            // selected model
            string selectedModel = null;
            string selectedLivery;
            Substitution.Model selectedModelObject = null;

            // check for selection
            if (DataGrid_Substitutions.SelectedRows.Count > 0)
            {
                // get index
                int index = DataGrid_Substitutions.SelectedRows[0].Index;

                // check index
                if (index >= 0 && index < itemList.Count)
                {
                    // get selected model
                    selectedModel = itemList[index].model;
                    selectedLivery = itemList[index].livery;
                    selectedModelObject = main.substitution.GetModel(selectedModel, selectedLivery);
                }
            }

            // check if no selected model
            if (selectedModel == null && itemList.Count > 0)
            {
                // select first model
                selectedModel = itemList[0].model;
                selectedLivery = itemList[0].livery;
                selectedModelObject = main.substitution.GetModel(selectedModel, selectedLivery);
            }

            // return selected model
            return selectedModelObject;
        }
#endif

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
        /// Update window
        /// </summary>
        public void RefreshWindow()
        {
            lock (main.conch)
            {
                // set simulator name and version
                Label_Simulator.Text = main.sim != null ? main.sim.GetSimulatorName() : "";
            }

            // selected model
            string selectedModel = GetSelectedModel();
#if FS2024
            Substitution.Model selectedModelObject = GetSelectedModelObject();
#endif

            // clear existing cells
            itemList.Clear();

            List<string> keys;

            lock (main.conch)
            {
                // get models
                keys = [.. main.substitution.matches.Keys];

                // sort keys
                keys.Sort();

                // for each default model
                foreach (var defaultModel in main.substitution.defaultModels)
                {
                    // check for match
                    if (main.substitution.matches.TryGetValue(defaultModel.Value, out Substitution.Model value))
                    {
                        // add row
#if FS2024
                        // TODO: something is not good at the livery substitution
                        itemList.Add(new Item(defaultModel.Value, value.title, value.variation, value.variation));
#else
                        itemList.Add(new Item(defaultModel.Value, main.substitution.matches[defaultModel.Value].title));
#endif
                    }
                }

                // for each substitution
                foreach (var key in keys)
                {
                    // check for default
                    if (main.substitution.defaultModels.ContainsValue(key) == false)
                    {
                        // add row
#if FS2024
                        // TODO: something is not good at the livery substitution
                        itemList.Add(new Item(key, main.substitution.matches[key].title, main.substitution.matches[key].variation, main.substitution.matches[key].variation));
#else
                        itemList.Add(new Item(key, main.substitution.matches[key].title));
#endif
                    }
                }
            }

            // rows
            DataGridViewRow[] rows = new DataGridViewRow[itemList.Count];
            // for each item
            for (int index = 0; index < itemList.Count; index++)
            {
                // create row
                rows[index] = new DataGridViewRow();
                rows[index].CreateCells(DataGrid_Substitutions);
                // fill row
                rows[index].Cells[0].Value = itemList[index].model;
                rows[index].Cells[1].Value = itemList[index].substitute;
#if FS2024
                rows[index].Cells[2].Value = itemList[index].substituteLivery;
#endif
            }

            DataGrid_Substitutions.Rows.Clear();
            DataGrid_Substitutions.Rows.AddRange(rows);

            // for each row
            for (int index = 0; index < itemList.Count && index < DataGrid_Substitutions.Rows.Count; index++)
            {
                // check for selected aircraft
                if (itemList[index].model.Equals(selectedModel))
                {
                    // select row
                    DataGrid_Substitutions.Rows[index].Selected = true;
                }
            }
        }

        private void MatchingForm_Load(object sender, EventArgs e)
        {
            // get saved position
            Point location = Settings.Default.MatchingFormLocation;
            Size size = Settings.Default.MatchingFormSize;

            // check for first time
            if (size.Width == 0 || size.Height == 0)
            {
                // save current position
                Settings.Default.MatchingFormLocation = Location;
                Settings.Default.MatchingFormSize = Size;
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

        private void MatchingForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void MatchingForm_Resize(object sender, EventArgs e)
        {
            // size list
            DataGrid_Substitutions.Height = Height - listHeightOffset;
            DataGrid_Substitutions.Width = Width - listWidthOffset;
        }


        private void MatchingForm_Activated(object sender, EventArgs e)
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

        private void MatchingForm_Deactivate(object sender, EventArgs e)
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

        private void Context_Matching_Substitute_Click(object sender, EventArgs e)
        {
            // get selected model
            string model = GetSelectedModel();

            // check for selection
            if (model != null)
            {
                // edit model match
#if FS2024
                // TODO: livery matching is not yet correct. solve it!
                string livery = main.substitution.matches[model].variation;

                if (main.substitution.EditMatch(model, livery, main.substitution.GetTypeRole(model)))
#else
                if (main.substitution.EditMatch(model, main.substitution.GetTypeRole(model)))
#endif
                {
                    RefreshWindow();
                }
            }
        }

        private void Context_Matching_Remove_Click(object sender, EventArgs e)
        {
            // get selected model
            string model = GetSelectedModel();
            // check for selection
            if (model != null)
            {
                // ask for confirmation
                DialogResult result = MessageBox.Show("Permanently remove substitution for '" + model + "'?", Main.Name + ": Model Matching", MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    lock (main.conch)
                    {
                        // remove this model match
                        main.substitution.matches.Remove(model);
                        main.ScheduleSubstitutionSave();
                        // remove aircraft using the selected model
                        main.sim ?. ScheduleRemoveModel(model);
                    }
                    RefreshWindow();
                }
            }
        }

        private void Context_Matching_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // reset buttons
            Context_EditMatching_Substitute.Enabled = false;
            Context_EditMatching_Remove.Enabled = false;

            // get selected model
            string model = GetSelectedModel();

            // check for selection
            if (model != null)
            {
                // enable buttons
                Context_EditMatching_Substitute.Enabled = true;

                lock (main.conch)
                {
                    // check for default model
                    if (main.substitution.defaultModels.ContainsValue(model) == false)
                    {
                        Context_EditMatching_Remove.Enabled = true;
                    }
                }
            }
        }

        private void MatchingForm_VisibleChanged(object sender, EventArgs e)
        {
            Settings.Default.MatchingFormOpen = Visible;
        }

        private void MatchingForm_ResizeEnd(object sender, EventArgs e)
        {
            // save form position
            Settings.Default.MatchingFormLocation = Location;
            Settings.Default.MatchingFormSize = Size;
        }
    }
}
