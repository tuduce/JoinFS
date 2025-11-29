namespace JoinFS
{
    partial class VariablesForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(VariablesForm));
            this.Button_OK = new System.Windows.Forms.Button();
            this.Label_Filter = new System.Windows.Forms.Label();
            this.Text_Filter = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.Text_Title = new System.Windows.Forms.TextBox();
            this.Combo_Variation = new System.Windows.Forms.ComboBox();
            this.Combo_Type = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.ListBox_Sets = new System.Windows.Forms.ListBox();
            this.Button_Add = new System.Windows.Forms.Button();
            this.Button_Remove = new System.Windows.Forms.Button();
            this.Button_Edit = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // Button_OK
            // 
            resources.ApplyResources(this.Button_OK, "Button_OK");
            this.Button_OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Button_OK.Name = "Button_OK";
            this.Button_OK.UseVisualStyleBackColor = true;
            this.Button_OK.Click += new System.EventHandler(this.Button_OK_Click);
            // 
            // Label_Filter
            // 
            resources.ApplyResources(this.Label_Filter, "Label_Filter");
            this.Label_Filter.Name = "Label_Filter";
            // 
            // Text_Filter
            // 
            resources.ApplyResources(this.Text_Filter, "Text_Filter");
            this.Text_Filter.Name = "Text_Filter";
            this.Text_Filter.TextChanged += new System.EventHandler(this.Text_Filter_TextChanged);
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // Text_Title
            // 
            resources.ApplyResources(this.Text_Title, "Text_Title");
            this.Text_Title.Name = "Text_Title";
            this.Text_Title.ReadOnly = true;
            this.Text_Title.TextChanged += new System.EventHandler(this.Text_Title_TextChanged);
            // 
            // Combo_Variation
            // 
            resources.ApplyResources(this.Combo_Variation, "Combo_Variation");
            this.Combo_Variation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_Variation.FormattingEnabled = true;
            this.Combo_Variation.Name = "Combo_Variation";
            this.Combo_Variation.Sorted = true;
            this.Combo_Variation.SelectedValueChanged += new System.EventHandler(this.Combo_Variation_SelectedValueChanged);
            // 
            // Combo_Type
            // 
            resources.ApplyResources(this.Combo_Type, "Combo_Type");
            this.Combo_Type.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_Type.FormattingEnabled = true;
            this.Combo_Type.Name = "Combo_Type";
            this.Combo_Type.Sorted = true;
            this.Combo_Type.SelectedValueChanged += new System.EventHandler(this.Combo_Type_SelectedValueChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // ListBox_Sets
            // 
            resources.ApplyResources(this.ListBox_Sets, "ListBox_Sets");
            this.ListBox_Sets.FormattingEnabled = true;
            this.ListBox_Sets.Name = "ListBox_Sets";
            this.ListBox_Sets.SelectedValueChanged += new System.EventHandler(this.ListBox_Sets_SelectedValueChanged);
            // 
            // Button_Add
            // 
            resources.ApplyResources(this.Button_Add, "Button_Add");
            this.Button_Add.Name = "Button_Add";
            this.Button_Add.UseVisualStyleBackColor = true;
            this.Button_Add.Click += new System.EventHandler(this.Button_Add_Click);
            // 
            // Button_Remove
            // 
            resources.ApplyResources(this.Button_Remove, "Button_Remove");
            this.Button_Remove.Name = "Button_Remove";
            this.Button_Remove.UseVisualStyleBackColor = true;
            this.Button_Remove.Click += new System.EventHandler(this.Button_Remove_Click);
            // 
            // Button_Edit
            // 
            resources.ApplyResources(this.Button_Edit, "Button_Edit");
            this.Button_Edit.Name = "Button_Edit";
            this.Button_Edit.UseVisualStyleBackColor = true;
            this.Button_Edit.Click += new System.EventHandler(this.Button_Edit_Click);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // VariablesForm
            // 
            this.AcceptButton = this.Button_OK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Edit);
            this.Controls.Add(this.Button_Remove);
            this.Controls.Add(this.Button_Add);
            this.Controls.Add(this.ListBox_Sets);
            this.Controls.Add(this.Label_Filter);
            this.Controls.Add(this.Text_Filter);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.Text_Title);
            this.Controls.Add(this.Combo_Variation);
            this.Controls.Add(this.Combo_Type);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Button_OK);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "VariablesForm";
            this.ShowInTaskbar = false;
            this.Load += new System.EventHandler(this.VariablesForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button Button_OK;
        private System.Windows.Forms.Label Label_Filter;
        private System.Windows.Forms.TextBox Text_Filter;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox Text_Title;
        private System.Windows.Forms.ComboBox Combo_Variation;
        private System.Windows.Forms.ComboBox Combo_Type;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ListBox ListBox_Sets;
        private System.Windows.Forms.Button Button_Add;
        private System.Windows.Forms.Button Button_Remove;
        private System.Windows.Forms.Button Button_Edit;
        private System.Windows.Forms.Label label1;
    }
}
