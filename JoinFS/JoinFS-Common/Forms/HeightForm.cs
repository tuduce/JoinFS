using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class HeightForm : Form
    {
        Main main;

        /// <summary>
        /// model to adjust
        /// </summary>
        Substitution.Model model = null;
        /// <summary>
        /// height adjustment
        /// </summary>
        public int adjustment;

        /// <summary>
        /// refresh window
        /// </summary>
        void RefreshWindow()
        {
            // check sign of adjustment
            if (adjustment > 0)
            {
                Text_Adjustment.Text = "+ " + adjustment + " cm";
            }
            else if (adjustment < 0)
            {
                Text_Adjustment.Text = "- " + -adjustment + " cm";
            }
            else
            {
                Text_Adjustment.Text = "0 cm";
            }
        }

        public HeightForm(Main main, Substitution.Model model, int adjustment)
        {
            this.main = main;

            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            // set model
            this.model = model;
            this.adjustment = adjustment;
            Text_Model.Text = model != null ? model.longType : "";

            // change font
            Text_Model.Font = main.dataFont;
            Button_Plus50.Font = main.dataFont;
            Button_Plus5.Font = main.dataFont;
            Button_Off.Font = main.dataFont;
            Button_Minus5.Font = main.dataFont;
            Button_Minus50.Font = main.dataFont;
            Text_Adjustment.Font = main.dataFont;
            // refresh
            RefreshWindow();
        }

        private void Button_Plus50_Click(object sender, EventArgs e)
        {
            // add 50cm
            adjustment += 50;
            lock (main.conch)
            {
                // update sim
                main.sim ?. UpdateHeightAdjustment(model, adjustment);
            }
            // refresh 
            RefreshWindow();
        }

        private void Button_Plus5_Click(object sender, EventArgs e)
        {
            // add 5cm
            adjustment += 5;
            lock (main.conch)
            {
                // update sim
                main.sim ?. UpdateHeightAdjustment(model, adjustment);
            }
            // refresh 
            RefreshWindow();
        }

        private void Button_Off_Click(object sender, EventArgs e)
        {
            // reset
            adjustment = 0;
            lock (main.conch)
            {
                // update sim
                main.sim ?. UpdateHeightAdjustment(model, adjustment);
            }
            // refresh 
            RefreshWindow();
        }

        private void Button_Minus5_Click(object sender, EventArgs e)
        {
            // minus 5cm
            adjustment -= 5;
            lock (main.conch)
            {
                // update sim
                main.sim ?. UpdateHeightAdjustment(model, adjustment);
            }
            // refresh 
            RefreshWindow();
        }

        private void Button_Minus50_Click(object sender, EventArgs e)
        {
            // minus 50cm
            adjustment -= 50;
            lock (main.conch)
            {
                // update sim
                main.sim ?. UpdateHeightAdjustment(model, adjustment);
            }
            // refresh 
            RefreshWindow();
        }
    }
}
