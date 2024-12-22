namespace JoinFS
{
    partial class SubstitutionForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SubstitutionForm));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.Button_OK = new System.Windows.Forms.Button();
            this.Button_Cancel = new System.Windows.Forms.Button();
            this.Combo_Type = new System.Windows.Forms.ComboBox();
            this.Combo_Variation = new System.Windows.Forms.ComboBox();
            this.Text_Title = new System.Windows.Forms.TextBox();
            this.Text_Replace = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.Text_Filter = new System.Windows.Forms.TextBox();
            this.Label_Filter = new System.Windows.Forms.Label();
            this.Button_Original = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // Button_OK
            // 
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
            // 
            // Button_Cancel
            // 
            this.Button_Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.Button_Cancel, "Button_Cancel");
            this.Button_Cancel.Name = "Button_Cancel";
            this.Button_Cancel.UseVisualStyleBackColor = true;
            // 
            // Combo_Type
            // 
            this.Combo_Type.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_Type.FormattingEnabled = true;
            resources.ApplyResources(this.Combo_Type, "Combo_Type");
            this.Combo_Type.Name = "Combo_Type";
            this.Combo_Type.Sorted = true;
            this.Combo_Type.SelectedValueChanged += new System.EventHandler(this.Combo_Type_SelectedValueChanged);
            // 
            // Combo_Variation
            // 
            this.Combo_Variation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_Variation.FormattingEnabled = true;
            resources.ApplyResources(this.Combo_Variation, "Combo_Variation");
            this.Combo_Variation.Name = "Combo_Variation";
            this.Combo_Variation.Sorted = true;
            this.Combo_Variation.SelectedValueChanged += new System.EventHandler(this.Combo_Variation_SelectedValueChanged);
            // 
            // Text_Title
            // 
            resources.ApplyResources(this.Text_Title, "Text_Title");
            this.Text_Title.Name = "Text_Title";
            this.Text_Title.ReadOnly = true;
            // 
            // Text_Replace
            // 
            resources.ApplyResources(this.Text_Replace, "Text_Replace");
            this.Text_Replace.Name = "Text_Replace";
            this.Text_Replace.ReadOnly = true;
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // Text_Filter
            // 
            resources.ApplyResources(this.Text_Filter, "Text_Filter");
            this.Text_Filter.Name = "Text_Filter";
            this.Text_Filter.TextChanged += new System.EventHandler(this.Text_Filter_TextChanged);
            // 
            // Label_Filter
            // 
            resources.ApplyResources(this.Label_Filter, "Label_Filter");
            this.Label_Filter.Name = "Label_Filter";
            // 
            // Button_Original
            // 
            this.Button_Original.DialogResult = System.Windows.Forms.DialogResult.No;
            resources.ApplyResources(this.Button_Original, "Button_Original");
            this.Button_Original.Name = "Button_Original";
            this.Button_Original.UseVisualStyleBackColor = true;
            // 
            // SubstitutionForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.Button_Cancel;
            this.Controls.Add(this.Button_Original);
            this.Controls.Add(this.Label_Filter);
            this.Controls.Add(this.Text_Filter);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.Text_Replace);
            this.Controls.Add(this.Text_Title);
            this.Controls.Add(this.Combo_Variation);
            this.Controls.Add(this.Combo_Type);
            this.Controls.Add(this.Button_Cancel);
            this.Controls.Add(this.Button_OK);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SubstitutionForm";
            this.ShowInTaskbar = false;
            this.Load += new System.EventHandler(this.SubstitutionForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.Button Button_Cancel;
        private System.Windows.Forms.ComboBox Combo_Type;
        private System.Windows.Forms.ComboBox Combo_Variation;
        private System.Windows.Forms.TextBox Text_Title;
        private System.Windows.Forms.TextBox Text_Replace;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox Text_Filter;
        private System.Windows.Forms.Label Label_Filter;
        private System.Windows.Forms.Button Button_Original;
    }
}