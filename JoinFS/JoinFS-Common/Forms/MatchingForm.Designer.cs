namespace JoinFS
{
    partial class MatchingForm
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MatchingForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            this.label2 = new System.Windows.Forms.Label();
            this.DataGrid_Substitutions = new System.Windows.Forms.DataGridView();
            this.Context_Matching = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_EditMatching_Substitute = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_EditMatching_Remove = new System.Windows.Forms.ToolStripMenuItem();
            this.label1 = new System.Windows.Forms.Label();
            this.Label_Simulator = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.ColOriginalModel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColSubstituteModel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColModelVariation = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Substitutions)).BeginInit();
            this.Context_Matching.SuspendLayout();
            this.SuspendLayout();
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // DataGrid_Substitutions
            // 
            this.DataGrid_Substitutions.AllowUserToAddRows = false;
            this.DataGrid_Substitutions.AllowUserToDeleteRows = false;
            this.DataGrid_Substitutions.AllowUserToResizeColumns = false;
            this.DataGrid_Substitutions.AllowUserToResizeRows = false;
            this.DataGrid_Substitutions.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_Substitutions.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_Substitutions.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColOriginalModel,
            this.ColSubstituteModel,
            this.ColModelVariation});
            this.DataGrid_Substitutions.ContextMenuStrip = this.Context_Matching;
            resources.ApplyResources(this.DataGrid_Substitutions, "DataGrid_Substitutions");
            this.DataGrid_Substitutions.MultiSelect = false;
            this.DataGrid_Substitutions.Name = "DataGrid_Substitutions";
            this.DataGrid_Substitutions.ReadOnly = true;
            this.DataGrid_Substitutions.RowHeadersVisible = false;
            this.DataGrid_Substitutions.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            // 
            // Context_Matching
            // 
            this.Context_Matching.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_EditMatching_Substitute,
            this.Context_EditMatching_Remove});
            this.Context_Matching.Name = "Context_EditMatching";
            resources.ApplyResources(this.Context_Matching, "Context_Matching");
            this.Context_Matching.Opening += new System.ComponentModel.CancelEventHandler(this.Context_Matching_Opening);
            // 
            // Context_EditMatching_Substitute
            // 
            this.Context_EditMatching_Substitute.Name = "Context_EditMatching_Substitute";
            resources.ApplyResources(this.Context_EditMatching_Substitute, "Context_EditMatching_Substitute");
            this.Context_EditMatching_Substitute.Click += new System.EventHandler(this.Context_Matching_Substitute_Click);
            // 
            // Context_EditMatching_Remove
            // 
            this.Context_EditMatching_Remove.Name = "Context_EditMatching_Remove";
            resources.ApplyResources(this.Context_EditMatching_Remove, "Context_EditMatching_Remove");
            this.Context_EditMatching_Remove.Click += new System.EventHandler(this.Context_Matching_Remove_Click);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Label_Simulator
            // 
            resources.ApplyResources(this.Label_Simulator, "Label_Simulator");
            this.Label_Simulator.Name = "Label_Simulator";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
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
            // ColOriginalModel
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColOriginalModel.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColOriginalModel, "ColOriginalModel");
            this.ColOriginalModel.MaxInputLength = 128;
            this.ColOriginalModel.Name = "ColOriginalModel";
            this.ColOriginalModel.ReadOnly = true;
            this.ColOriginalModel.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColOriginalModel.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColSubstituteModel
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColSubstituteModel.DefaultCellStyle = dataGridViewCellStyle2;
            resources.ApplyResources(this.ColSubstituteModel, "ColSubstituteModel");
            this.ColSubstituteModel.MaxInputLength = 128;
            this.ColSubstituteModel.Name = "ColSubstituteModel";
            this.ColSubstituteModel.ReadOnly = true;
            this.ColSubstituteModel.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColSubstituteModel.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColModelVariation
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColModelVariation.DefaultCellStyle = dataGridViewCellStyle3;
            resources.ApplyResources(this.ColModelVariation, "ColModelVariation");
            this.ColModelVariation.MaxInputLength = 128;
            this.ColModelVariation.Name = "ColModelVariation";
            this.ColModelVariation.ReadOnly = true;
            this.ColModelVariation.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColModelVariation.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // MatchingForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.Label_Simulator);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.DataGrid_Substitutions);
            this.Controls.Add(this.label2);
            this.Name = "MatchingForm";
            this.Activated += new System.EventHandler(this.MatchingForm_Activated);
            this.Deactivate += new System.EventHandler(this.MatchingForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MatchingForm_FormClosing);
            this.Load += new System.EventHandler(this.MatchingForm_Load);
            this.ResizeEnd += new System.EventHandler(this.MatchingForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.MatchingForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.MatchingForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Substitutions)).EndInit();
            this.Context_Matching.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.DataGridView DataGrid_Substitutions;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label Label_Simulator;
        private System.Windows.Forms.ContextMenuStrip Context_Matching;
        private System.Windows.Forms.ToolStripMenuItem Context_EditMatching_Substitute;
        private System.Windows.Forms.ToolStripMenuItem Context_EditMatching_Remove;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColOriginalModel;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColSubstituteModel;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColModelVariation;
    }
}