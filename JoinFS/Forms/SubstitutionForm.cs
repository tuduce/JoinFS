using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

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

        readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 200 };

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

            SortedSet<string> typeSet = new(StringComparer.OrdinalIgnoreCase);

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
                        typeSet.Add(model.type);
                    }
                }
            }

            // clear current list and batch-populate the new one
            Combo_Type.BeginUpdate();
            Combo_Type.Items.Clear();
            Combo_Type.Items.AddRange(typeSet.ToArray());
            Combo_Type.EndUpdate();
        }

        public void UpdateVariation()
        {
            SortedSet<string> variationSet = new(StringComparer.OrdinalIgnoreCase);

            lock (main.conch)
            {
                // for each model
                foreach (var model in main.substitution.models)
                {
                    // check variation
                    if (model.type.Equals(Combo_Type.Text))
                    {
                        variationSet.Add(model.variation);
                    }
                }
            }

            // clear current list and batch-populate the new one
            Combo_Variation.BeginUpdate();
            Combo_Variation.Items.Clear();
            Combo_Variation.Items.AddRange(variationSet.ToArray());
            Combo_Variation.EndUpdate();
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

            // debounce the filter so typing doesn't trigger a full model-list scan on every keystroke
            filterTimer.Tick += FilterTimer_Tick;
        }

        void FilterTimer_Tick(object sender, EventArgs e)
        {
            filterTimer.Stop();

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

        private void Text_Filter_TextChanged(object sender, EventArgs e)
        {
            // reset the debounce timer on every keystroke - UpdateType() only runs once typing pauses
            filterTimer.Stop();
            filterTimer.Start();
        }

        private async void SubstitutionForm_Load(object sender, EventArgs e)
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
            Substitution.Type type;

            //lock (main.conch)
            //{
            // no live remote ICAO data available for this manual-override preview - falls through
            // to the same prefix/default tiers as before ICAO-based matching existed
#if FS2024
                (model, type) = await main.substitution.Match(replace, variation, "", "", typerole);
#else
                (model, type) = await main.substitution.Match(replace, "", "", typerole);
#endif
            //}

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
