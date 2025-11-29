namespace JoinFS
{
    partial class AddressBookForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AddressBookForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_Entries = new System.Windows.Forms.DataGridView();
            this.ColName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColAddress = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Context_Entry = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_Entry_Join = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Context_Entry_Add = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Entry_Edit = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Entry_Remove = new System.Windows.Forms.ToolStripMenuItem();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Entries)).BeginInit();
            this.Context_Entry.SuspendLayout();
            this.SuspendLayout();
            // 
            // DataGrid_Entries
            // 
            resources.ApplyResources(this.DataGrid_Entries, "DataGrid_Entries");
            this.DataGrid_Entries.AllowUserToAddRows = false;
            this.DataGrid_Entries.AllowUserToDeleteRows = false;
            this.DataGrid_Entries.AllowUserToResizeColumns = false;
            this.DataGrid_Entries.AllowUserToResizeRows = false;
            this.DataGrid_Entries.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_Entries.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_Entries.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColName,
            this.ColAddress,
            this.ColStatus});
            this.DataGrid_Entries.ContextMenuStrip = this.Context_Entry;
            this.DataGrid_Entries.MultiSelect = false;
            this.DataGrid_Entries.Name = "DataGrid_Entries";
            this.DataGrid_Entries.ReadOnly = true;
            this.DataGrid_Entries.RowHeadersVisible = false;
            this.DataGrid_Entries.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_Entries.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_Entries_CellClick);
            // 
            // ColName
            // 
            resources.ApplyResources(this.ColName, "ColName");
            this.ColName.MaxInputLength = 40;
            this.ColName.Name = "ColName";
            this.ColName.ReadOnly = true;
            this.ColName.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColName.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColAddress
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColAddress.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColAddress, "ColAddress");
            this.ColAddress.MaxInputLength = 40;
            this.ColAddress.Name = "ColAddress";
            this.ColAddress.ReadOnly = true;
            this.ColAddress.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAddress.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColStatus
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColStatus.DefaultCellStyle = dataGridViewCellStyle2;
            resources.ApplyResources(this.ColStatus, "ColStatus");
            this.ColStatus.MaxInputLength = 10;
            this.ColStatus.Name = "ColStatus";
            this.ColStatus.ReadOnly = true;
            this.ColStatus.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColStatus.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Context_Entry
            // 
            resources.ApplyResources(this.Context_Entry, "Context_Entry");
            this.Context_Entry.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_Entry_Join,
            this.toolStripSeparator1,
            this.Context_Entry_Add,
            this.Context_Entry_Edit,
            this.Context_Entry_Remove});
            this.Context_Entry.Name = "Context_Entry";
            this.Context_Entry.Opening += new System.ComponentModel.CancelEventHandler(this.Context_Entry_Opening);
            // 
            // Context_Entry_Join
            // 
            resources.ApplyResources(this.Context_Entry_Join, "Context_Entry_Join");
            this.Context_Entry_Join.Name = "Context_Entry_Join";
            this.Context_Entry_Join.Click += new System.EventHandler(this.Context_Entry_Join_Click);
            // 
            // toolStripSeparator1
            // 
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // Context_Entry_Add
            // 
            resources.ApplyResources(this.Context_Entry_Add, "Context_Entry_Add");
            this.Context_Entry_Add.Name = "Context_Entry_Add";
            this.Context_Entry_Add.Click += new System.EventHandler(this.Context_Entry_Add_Click);
            // 
            // Context_Entry_Edit
            // 
            resources.ApplyResources(this.Context_Entry_Edit, "Context_Entry_Edit");
            this.Context_Entry_Edit.Name = "Context_Entry_Edit";
            this.Context_Entry_Edit.Click += new System.EventHandler(this.Context_Entry_Edit_Click);
            // 
            // Context_Entry_Remove
            // 
            resources.ApplyResources(this.Context_Entry_Remove, "Context_Entry_Remove");
            this.Context_Entry_Remove.Name = "Context_Entry_Remove";
            this.Context_Entry_Remove.Click += new System.EventHandler(this.Context_Entry_Remove_Click);
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
            // AddressBookForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.DataGrid_Entries);
            this.Cursor = System.Windows.Forms.Cursors.Default;
            this.Name = "AddressBookForm";
            this.Activated += new System.EventHandler(this.AddressBookForm_Activated);
            this.Deactivate += new System.EventHandler(this.AddressBookForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.AddressBookForm_FormClosing);
            this.Load += new System.EventHandler(this.AddressBookForm_Load);
            this.ResizeEnd += new System.EventHandler(this.AddressBookForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.AddressBookForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.AddressBookForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Entries)).EndInit();
            this.Context_Entry.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_Entries;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ContextMenuStrip Context_Entry;
        private System.Windows.Forms.ToolStripMenuItem Context_Entry_Add;
        private System.Windows.Forms.ToolStripMenuItem Context_Entry_Edit;
        private System.Windows.Forms.ToolStripMenuItem Context_Entry_Remove;
        private System.Windows.Forms.ToolStripMenuItem Context_Entry_Join;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColName;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAddress;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColStatus;
    }
}