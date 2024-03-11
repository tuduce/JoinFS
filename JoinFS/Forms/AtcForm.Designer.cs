namespace JoinFS
{
    partial class AtcForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AtcForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_AtcList = new System.Windows.Forms.DataGridView();
            this.ColCallsign = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColNickname = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColFrequency = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Context_ATC = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_ATC_Join = new System.Windows.Forms.ToolStripMenuItem();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_AtcList)).BeginInit();
            this.Context_ATC.SuspendLayout();
            this.SuspendLayout();
            // 
            // DataGrid_AtcList
            // 
            resources.ApplyResources(this.DataGrid_AtcList, "DataGrid_AtcList");
            this.DataGrid_AtcList.AllowUserToAddRows = false;
            this.DataGrid_AtcList.AllowUserToDeleteRows = false;
            this.DataGrid_AtcList.AllowUserToResizeColumns = false;
            this.DataGrid_AtcList.AllowUserToResizeRows = false;
            this.DataGrid_AtcList.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_AtcList.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_AtcList.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColCallsign,
            this.ColNickname,
            this.ColFrequency});
            this.DataGrid_AtcList.ContextMenuStrip = this.Context_ATC;
            this.DataGrid_AtcList.MultiSelect = false;
            this.DataGrid_AtcList.Name = "DataGrid_AtcList";
            this.DataGrid_AtcList.ReadOnly = true;
            this.DataGrid_AtcList.RowHeadersVisible = false;
            this.DataGrid_AtcList.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_AtcList.ShowCellToolTips = false;
            this.DataGrid_AtcList.ShowEditingIcon = false;
            this.DataGrid_AtcList.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_AtcList_CellClick);
            // 
            // ColCallsign
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColCallsign.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColCallsign, "ColCallsign");
            this.ColCallsign.Name = "ColCallsign";
            this.ColCallsign.ReadOnly = true;
            this.ColCallsign.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColCallsign.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColNickname
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColNickname.DefaultCellStyle = dataGridViewCellStyle2;
            resources.ApplyResources(this.ColNickname, "ColNickname");
            this.ColNickname.Name = "ColNickname";
            this.ColNickname.ReadOnly = true;
            this.ColNickname.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColNickname.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColFrequency
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColFrequency.DefaultCellStyle = dataGridViewCellStyle3;
            resources.ApplyResources(this.ColFrequency, "ColFrequency");
            this.ColFrequency.Name = "ColFrequency";
            this.ColFrequency.ReadOnly = true;
            this.ColFrequency.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColFrequency.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Context_ATC
            // 
            resources.ApplyResources(this.Context_ATC, "Context_ATC");
            this.Context_ATC.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_ATC_Join});
            this.Context_ATC.Name = "Context_ATC";
            // 
            // Context_ATC_Join
            // 
            resources.ApplyResources(this.Context_ATC_Join, "Context_ATC_Join");
            this.Context_ATC_Join.Name = "Context_ATC_Join";
            this.Context_ATC_Join.Click += new System.EventHandler(this.Context_ATC_Join_Click);
            // 
            // Button_Refresh
            // 
            resources.ApplyResources(this.Button_Refresh, "Button_Refresh");
            this.Button_Refresh.Name = "Button_Refresh";
            this.Button_Refresh.UseVisualStyleBackColor = true;
            this.Button_Refresh.Click += new System.EventHandler(this.Button_Refresh_Click);
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
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // AtcForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.DataGrid_AtcList);
            this.KeyPreview = true;
            this.Name = "AtcForm";
            this.Activated += new System.EventHandler(this.AtcForm_Activated);
            this.Deactivate += new System.EventHandler(this.AtcForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.AtcForm_FormClosing);
            this.Load += new System.EventHandler(this.AtcForm_Load);
            this.ResizeEnd += new System.EventHandler(this.AtcForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.AtcForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.AtcForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_AtcList)).EndInit();
            this.Context_ATC.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_AtcList;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ContextMenuStrip Context_ATC;
        private System.Windows.Forms.ToolStripMenuItem Context_ATC_Join;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColCallsign;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColNickname;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColFrequency;
    }
}
