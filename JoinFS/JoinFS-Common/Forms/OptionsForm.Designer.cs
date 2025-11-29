namespace JoinFS
{
    partial class OptionsForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OptionsForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_Options = new System.Windows.Forms.DataGridView();
            this.ColOption = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColDescription = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Options)).BeginInit();
            this.SuspendLayout();
            // 
            // DataGrid_Options
            // 
            resources.ApplyResources(this.DataGrid_Options, "DataGrid_Options");
            this.DataGrid_Options.AllowUserToAddRows = false;
            this.DataGrid_Options.AllowUserToDeleteRows = false;
            this.DataGrid_Options.AllowUserToResizeColumns = false;
            this.DataGrid_Options.AllowUserToResizeRows = false;
            this.DataGrid_Options.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_Options.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_Options.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColOption,
            this.ColDescription});
            this.DataGrid_Options.MultiSelect = false;
            this.DataGrid_Options.Name = "DataGrid_Options";
            this.DataGrid_Options.ReadOnly = true;
            this.DataGrid_Options.RowHeadersVisible = false;
            this.DataGrid_Options.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            // 
            // ColOption
            // 
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Source Code Pro", 8.999999F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ColOption.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColOption, "ColOption");
            this.ColOption.Name = "ColOption";
            this.ColOption.ReadOnly = true;
            // 
            // ColDescription
            // 
            resources.ApplyResources(this.ColDescription, "ColDescription");
            this.ColDescription.Name = "ColDescription";
            this.ColDescription.ReadOnly = true;
            // 
            // OptionsForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.DataGrid_Options);
            this.Name = "OptionsForm";
            this.Activated += new System.EventHandler(this.OptionsForm_Activated);
            this.Deactivate += new System.EventHandler(this.OptionsForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OptionsForm_FormClosing);
            this.Load += new System.EventHandler(this.OptionsForm_Load);
            this.ResizeEnd += new System.EventHandler(this.OptionsForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.OptionsForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.OptionsForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Options)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_Options;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColOption;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColDescription;
    }
}
