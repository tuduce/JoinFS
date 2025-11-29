using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class VariablesForm : Form
    {
        Main main;

        public void UpdateType(string filter)
        {
            // get filter words
            string[] words = filter.Split(' ');

            // clear current list
            Combo_Type.Items.Clear();

            List<string> typeList = new List<string>();

            lock (main.conch)
            {
                // for each model
                foreach (var model in main.substitution.models)
                {
                    // add type
                    bool add = true;

                    // for each filter word
                    foreach (var word in words)
                    {
                        // word found
                        bool found = false;

                        // check for filter word
                        if (model.manufacturer.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                        }

                        // check for filter word
                        if (model.type.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                        }

                        // check for filter word
                        if (model.variation.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                        }

                        // check if word not found
                        if (found == false)
                        {
                            add = false;
                        }
                    }

                    // check to add model
                    if (add)
                    {
                        typeList.Add(model.type);
                    }
                }
            }

            // for each model
            foreach (var type in typeList)
            {
                // check if not already listed
                if (Combo_Type.Items.Contains(type) == false)
                {
                    // add to list
                    Combo_Type.Items.Add(type);
                }
            }
        }

        public void UpdateVariation()
        {
            // clear current list
            Combo_Variation.Items.Clear();

            List<string> list = new List<string>();

            lock (main.conch)
            {
                // for each model
                foreach (var model in main.substitution.models)
                {
                    // check variation
                    if (model.type.Equals(Combo_Type.Text))
                    {
                        // check if not already listed
                        if (Combo_Variation.Items.Contains(model.variation) == false)
                        {
                            list.Add(model.variation);
                        }
                    }
                }
            }

            // for each model
            foreach (var variation in list)
            {
                // check if not already listed
                if (Combo_Variation.Items.Contains(variation) == false)
                {
                    // add to list
                    Combo_Variation.Items.Add(variation);
                }
            }
        }

        public void UpdateTitle()
        {
            // clear current list
            Text_Title.Text = "";

            string title = "";

            lock (main.conch)
            {
                // for each model
                foreach (var model in main.substitution.models)
                {
                    // check variation
                    if (model.type.Equals(Combo_Type.Text) && model.variation.Equals(Combo_Variation.Text))
                    {
                        title = model.title;
                    }
                }
            }

            // add to list
            Text_Title.Text = title;

            UpdateButtons();
        }

        public void UpdateVariables()
        {
            // clear current list
            ListBox_Sets.Items.Clear();

            // check for sim
            if (main.sim != null && main.sim.Connected)
            {
                lock (main.conch)
                {
                    // for each file
                    foreach (var filename in main.sim.GetModelVariables(Text_Title.Text))
                    {
                        // check if not already listed
                        if (ListBox_Sets.Items.Contains(filename) == false)
                        {
                            // add to list
                            ListBox_Sets.Items.Add(filename);
                        }
                    }
                }
            }

            UpdateButtons();
        }

        void UpdateButtons()
        {
            // check if anything selected
            if (ListBox_Sets.SelectedItem == null)
            {
                // disable buttons
                Button_Remove.Enabled = false;
                Button_Edit.Enabled = false;
            }
            else
            {
                // enable buttons
                Button_Remove.Enabled = true;

                // get file name
                string filename = ListBox_Sets.SelectedItem as string;
                // check file
                if (filename == "Plane.txt" ||
                    filename == "Rotorcraft.txt" ||
                    filename == "SingleProp.txt" ||
                    filename == "TwinProp.txt" ||
                    filename == "QuadProp.txt" ||
                    filename == "SingleTurbone" ||
                    filename == "TwinTurbine" ||
                    filename == "QuadTurbine")
                {
                    Button_Edit.Enabled = false;
                }
                else
                {
                    Button_Edit.Enabled = true;
                }
            }

            // check for model
            if (Text_Title.Text.Length == 0)
            {
                // disable buttons
                Button_Add.Enabled = false;
            }
            else
            {
                // enable buttons
                Button_Add.Enabled = true;
            }
        }

        public VariablesForm(Main main, string title)
        {
            this.main = main;

            InitializeComponent();

            // change icon
            Icon = main.icon;

            // change font
            Text_Filter.Font = main.dataFont;
            Combo_Type.Font = main.dataFont;
            Combo_Variation.Font = main.dataFont;
            Text_Title.Font = main.dataFont;
            ListBox_Sets.Font = main.dataFont;

            // disable buttons
            Button_Add.Enabled = false;
            Button_Remove.Enabled = false;
            Button_Edit.Enabled = false;

            // populate type
            UpdateType("");

#if XPLANE
            // disable model selection
            Text_Filter.Enabled = false;
            Combo_Type.Enabled = false;
            Combo_Variation.Enabled = false;
            if (title != null) Text_Title.Text = title;
#else 
            // get model
            Substitution.Model model = main.substitution.GetModel(title);
            // check for model
            if (model != null)
            {
                // update model
                Combo_Type.Text = model.type;
                Combo_Variation.Text = model.variation;
                Text_Title.Text = model.title;
            }
#endif

            UpdateVariables();
        }

        private void VariablesForm_Load(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void Text_Filter_TextChanged(object sender, EventArgs e)
        {
            // update type list
            UpdateType(Text_Filter.Text);
            if (Combo_Type.Items.Count > 0)
            {
                // select first in list
                Combo_Type.SelectedIndex = 0;
            }
        }

        private void Combo_Type_SelectedValueChanged(object sender, EventArgs e)
        {
            // populate list
            UpdateVariation();
            if (Combo_Variation.Items.Count > 0)
            {
                // select first variation
                Combo_Variation.SelectedIndex = 0;
            }
        }

        private void Combo_Variation_SelectedValueChanged(object sender, EventArgs e)
        {
            // populate list
            UpdateTitle();
        }

        private void Text_Title_TextChanged(object sender, EventArgs e)
        {
            // populate list
            UpdateVariables();
        }

        private void ListBox_Sets_SelectedValueChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void Button_Add_Click(object sender, EventArgs e)
        {
            // check for sim and model
            if (main.sim != null && main.sim.Connected && Text_Title.Text.Length > 0)
            {
                // open dialog to choose jfs file
                OpenFileDialog dialog = new OpenFileDialog
                {
                    InitialDirectory = Path.Combine(main.documentsPath, "Variables"),
                    Filter = "Text files (*.*)|*.*",
                    FilterIndex = 1,
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    // check for list not existing
                    if (main.sim.modelVariables.ContainsKey(Text_Title.Text) == false)
                    {
                        // add default files
                        main.sim.modelVariables[Text_Title.Text] = main.sim.GetModelDefaultVariables(Text_Title.Text);
                    }
                    // for each file
                    foreach (var path in dialog.FileNames)
                    {
                        // check for valid root folder
                        if (path.Substring(0, dialog.InitialDirectory.Length).ToLower() == dialog.InitialDirectory.ToLower())
                        {
                            // add variable file
                            main.sim.modelVariables[Text_Title.Text].Add(path.Substring(dialog.InitialDirectory.Length + 1));
                        }
                    }

                    // save changes
                    main.sim.SaveModelVaribles();
                    // update buttons
                    UpdateButtons();
                    // update variable list
                    UpdateVariables();
                }
            }
        }

        private void Button_Remove_Click(object sender, EventArgs e)
        {
            // get index
            int listIndex = ListBox_Sets.SelectedIndex;

            // check selected index
            if (listIndex >= 0 && listIndex < ListBox_Sets.Items.Count)
            {
                // remove selected
                ListBox_Sets.Items.RemoveAt(listIndex);

                // check for sim
                if (main.sim != null)
                {
                    // check for default model variables
                    if (main.sim.modelVariables.ContainsKey(Text_Title.Text) == false)
                    {
                        // add default files
                        main.sim.modelVariables[Text_Title.Text] = main.sim.GetModelDefaultVariables(Text_Title.Text);
                    }
                    // check index
                    if (listIndex < main.sim.modelVariables[Text_Title.Text].Count)
                    {
                        // remove file
                        main.sim.modelVariables[Text_Title.Text].RemoveAt(listIndex);
                    }

                    // save changes
                    main.sim.SaveModelVaribles();
                    // update buttons
                    UpdateButtons();
                    // update variable list
                    UpdateVariables();
                }
            }
        }

        private void Button_Edit_Click(object sender, EventArgs e)
        {
            // get index
            int listIndex = ListBox_Sets.SelectedIndex;

            // check selected index
            if (listIndex >= 0 && listIndex < ListBox_Sets.Items.Count)
            {
                try
                {
                    // edit file
                    Main.Launch(main.documentsPath + Path.DirectorySeparatorChar + "Variables" + Path.DirectorySeparatorChar + ListBox_Sets.SelectedItem as string);
                }
                catch (Exception ex)
                {
                    // monitor
                    main.MonitorEvent("Failed to edit variables file - " + ex.Message);
                }
            }
        }

        private void Button_OK_Click(object sender, EventArgs e)
        {
            // check for sim
            if (main.sim != null && main.sim.Connected)
            {
                lock (main.conch)
                {
                    // reconnect
                    main.sim.Close();
                    main.sim.Connect();
                }
            }
        }
    }
}
