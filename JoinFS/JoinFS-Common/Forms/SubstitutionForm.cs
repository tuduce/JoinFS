using System;
using System.Windows.Forms;
using System.Collections.Generic;

namespace JoinFS
{
    public partial class SubstitutionForm : Form
    {
        Main main;

#if FS2024
        string variation;
#endif
        string replace;
        int typerole;

        public string GetReplaceModel()
        {
#if FS2024
            string[] separator = { "[+]" };
            string[] parts = Text_Replace.Text.Split(separator, StringSplitOptions.None);
            return parts[0].TrimEnd(' ');
#else
            return Text_Replace.Text;
#endif
        }

        public string GetWithModel()
        {
#if FS2024
            string[] separator = { "[+]" };
            string[] parts = Text_Title.Text.Split(separator, StringSplitOptions.None);
            return parts[0].TrimEnd(' ');
#else
            return Text_Title.Text;
#endif
        }

#if FS2024
        public string GetWithVariation()
        {
            string[] separator = { "[+]" };
            string[] parts = Text_Title.Text.Split(separator, StringSplitOptions.None);
            if (parts.Length > 1)
            {
                return parts[1].TrimStart(' ');
            }
            else
            {
                return "";
            }
        }
#endif

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
            string variation = "";

            lock (main.conch)
            {
                // for each model
                foreach (var model in main.substitution.models)
                {
                    // check variation
                    if (model.type.Equals(Combo_Type.Text) && model.variation.Equals(Combo_Variation.Text))
                    {
                        title = model.title;
                        variation = model.variation;
                    }
                }
            }

            // add to list
#if FS2024
            Text_Title.Text = title + " [+] " + variation;
#else
            Text_Title.Text = title;
#endif
        }

#if FS2024
        public SubstitutionForm(Main main, string replace, string livery, int typerole)
#else
        public SubstitutionForm(Main main, string replace, int typerole)
#endif
        {
            InitializeComponent();

            this.main = main;
            this.replace = replace;
            this.typerole = typerole;

#if FS2024
            this.variation = livery;
#endif

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // change font
            Text_Replace.Font = main.dataFont;
            Text_Filter.Font = main.dataFont;
            Combo_Type.Font = main.dataFont;
            Combo_Variation.Font = main.dataFont;
            Text_Title.Font = main.dataFont;
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

        private void SubstitutionForm_Load(object sender, EventArgs e)
        {
            // populate type
            UpdateType("");

            // fill form
#if FS2024
            Text_Replace.Text = replace + " [+] " + variation;
#else
            Text_Replace.Text = replace;
#endif

            // get match
            Substitution.Model model;
            lock (main.conch)
            {
#if FS2024
                main.substitution.Match(replace, variation, typerole, out model, out Substitution.Type type);
#else
                main.substitution.Match(replace, typerole, out model, out Substitution.Type type);
#endif
            }

            // check if model exists
            if (model != null)
            {
                // set UI
                Combo_Type.Text = model.type;
                Combo_Variation.Text = model.variation;
#if FS2024
                Text_Title.Text = model.title + " [+] " + model.variation;
#else
                Text_Title.Text = model.title;
#endif
            }
            else
            {
                if (Combo_Type.Items.Count > 0)
                {
                    // select first in list
                    Combo_Type.SelectedIndex = 0;
                }
            }
        }
    }
}
