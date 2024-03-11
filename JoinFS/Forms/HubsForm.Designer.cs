namespace JoinFS
{
    partial class HubsForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(HubsForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle7 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle8 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_HubList = new System.Windows.Forms.DataGridView();
            this.ColHubName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColUsers = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColAircraft = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColAtc = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColSave = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColIgnore = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColVersion = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Context_Hub = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_Hub_Join = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Context_Hub_Offline = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Hub_Ignored = new System.Windows.Forms.ToolStripMenuItem();
            this.DataGrid_Hub = new System.Windows.Forms.DataGridView();
            this.ColAbout = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColVoIP = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColNextEvent = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_HubList)).BeginInit();
            this.Context_Hub.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Hub)).BeginInit();
            this.SuspendLayout();
            // 
            // DataGrid_HubList
            // 
            resources.ApplyResources(this.DataGrid_HubList, "DataGrid_HubList");
            this.DataGrid_HubList.AllowUserToAddRows = false;
            this.DataGrid_HubList.AllowUserToDeleteRows = false;
            this.DataGrid_HubList.AllowUserToResizeColumns = false;
            this.DataGrid_HubList.AllowUserToResizeRows = false;
            this.DataGrid_HubList.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_HubList.CausesValidation = false;
            this.DataGrid_HubList.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_HubList.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColHubName,
            this.ColStatus,
            this.ColUsers,
            this.ColAircraft,
            this.ColAtc,
            this.ColSave,
            this.ColIgnore,
            this.ColVersion});
            this.DataGrid_HubList.ContextMenuStrip = this.Context_Hub;
            this.DataGrid_HubList.MultiSelect = false;
            this.DataGrid_HubList.Name = "DataGrid_HubList";
            this.DataGrid_HubList.ReadOnly = true;
            this.DataGrid_HubList.RowHeadersVisible = false;
            this.DataGrid_HubList.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_HubList.ShowCellErrors = false;
            this.DataGrid_HubList.ShowCellToolTips = false;
            this.DataGrid_HubList.ShowEditingIcon = false;
            this.DataGrid_HubList.ShowRowErrors = false;
            this.DataGrid_HubList.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_HubList_CellClick);
            this.DataGrid_HubList.SelectionChanged += new System.EventHandler(this.DataGrid_HubList_SelectionChanged);
            // 
            // ColHubName
            // 
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.White;
            this.ColHubName.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColHubName, "ColHubName");
            this.ColHubName.MaxInputLength = 25;
            this.ColHubName.Name = "ColHubName";
            this.ColHubName.ReadOnly = true;
            this.ColHubName.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColHubName.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
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
            // ColUsers
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColUsers.DefaultCellStyle = dataGridViewCellStyle3;
            resources.ApplyResources(this.ColUsers, "ColUsers");
            this.ColUsers.MaxInputLength = 5;
            this.ColUsers.Name = "ColUsers";
            this.ColUsers.ReadOnly = true;
            this.ColUsers.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColUsers.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColAircraft
            // 
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColAircraft.DefaultCellStyle = dataGridViewCellStyle4;
            resources.ApplyResources(this.ColAircraft, "ColAircraft");
            this.ColAircraft.MaxInputLength = 5;
            this.ColAircraft.Name = "ColAircraft";
            this.ColAircraft.ReadOnly = true;
            this.ColAircraft.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAircraft.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColAtc
            // 
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColAtc.DefaultCellStyle = dataGridViewCellStyle5;
            resources.ApplyResources(this.ColAtc, "ColAtc");
            this.ColAtc.Name = "ColAtc";
            this.ColAtc.ReadOnly = true;
            this.ColAtc.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAtc.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColSave
            // 
            resources.ApplyResources(this.ColSave, "ColSave");
            this.ColSave.Name = "ColSave";
            this.ColSave.ReadOnly = true;
            this.ColSave.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColIgnore
            // 
            resources.ApplyResources(this.ColIgnore, "ColIgnore");
            this.ColIgnore.Name = "ColIgnore";
            this.ColIgnore.ReadOnly = true;
            this.ColIgnore.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // ColVersion
            // 
            dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColVersion.DefaultCellStyle = dataGridViewCellStyle6;
            resources.ApplyResources(this.ColVersion, "ColVersion");
            this.ColVersion.MaxInputLength = 30;
            this.ColVersion.Name = "ColVersion";
            this.ColVersion.ReadOnly = true;
            this.ColVersion.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColVersion.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Context_Hub
            // 
            resources.ApplyResources(this.Context_Hub, "Context_Hub");
            this.Context_Hub.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_Hub_Join,
            this.toolStripSeparator1,
            this.Context_Hub_Offline,
            this.Context_Hub_Ignored});
            this.Context_Hub.Name = "Context_Hub";
            this.Context_Hub.Opening += new System.ComponentModel.CancelEventHandler(this.Context_Hub_Opening);
            // 
            // Context_Hub_Join
            // 
            resources.ApplyResources(this.Context_Hub_Join, "Context_Hub_Join");
            this.Context_Hub_Join.Name = "Context_Hub_Join";
            this.Context_Hub_Join.Click += new System.EventHandler(this.Context_Hub_Join_Click);
            // 
            // toolStripSeparator1
            // 
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // Context_Hub_Offline
            // 
            resources.ApplyResources(this.Context_Hub_Offline, "Context_Hub_Offline");
            this.Context_Hub_Offline.Name = "Context_Hub_Offline";
            this.Context_Hub_Offline.Click += new System.EventHandler(this.Context_Hub_Offline_Click);
            // 
            // Context_Hub_Ignored
            // 
            resources.ApplyResources(this.Context_Hub_Ignored, "Context_Hub_Ignored");
            this.Context_Hub_Ignored.Name = "Context_Hub_Ignored";
            this.Context_Hub_Ignored.Click += new System.EventHandler(this.Context_Hub_Ignored_Click);
            // 
            // DataGrid_Hub
            // 
            resources.ApplyResources(this.DataGrid_Hub, "DataGrid_Hub");
            this.DataGrid_Hub.AllowUserToAddRows = false;
            this.DataGrid_Hub.AllowUserToDeleteRows = false;
            this.DataGrid_Hub.AllowUserToResizeColumns = false;
            this.DataGrid_Hub.AllowUserToResizeRows = false;
            this.DataGrid_Hub.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_Hub.CausesValidation = false;
            this.DataGrid_Hub.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_Hub.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColAbout,
            this.ColVoIP,
            this.ColNextEvent});
            this.DataGrid_Hub.MultiSelect = false;
            this.DataGrid_Hub.Name = "DataGrid_Hub";
            this.DataGrid_Hub.ReadOnly = true;
            this.DataGrid_Hub.RowHeadersVisible = false;
            this.DataGrid_Hub.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_Hub.ShowCellErrors = false;
            this.DataGrid_Hub.ShowCellToolTips = false;
            this.DataGrid_Hub.ShowEditingIcon = false;
            this.DataGrid_Hub.ShowRowErrors = false;
            this.DataGrid_Hub.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_Hub_CellClick);
            // 
            // ColAbout
            // 
            dataGridViewCellStyle7.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColAbout.DefaultCellStyle = dataGridViewCellStyle7;
            resources.ApplyResources(this.ColAbout, "ColAbout");
            this.ColAbout.MaxInputLength = 40;
            this.ColAbout.Name = "ColAbout";
            this.ColAbout.ReadOnly = true;
            this.ColAbout.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAbout.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColVoIP
            // 
            dataGridViewCellStyle8.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColVoIP.DefaultCellStyle = dataGridViewCellStyle8;
            resources.ApplyResources(this.ColVoIP, "ColVoIP");
            this.ColVoIP.MaxInputLength = 40;
            this.ColVoIP.Name = "ColVoIP";
            this.ColVoIP.ReadOnly = true;
            this.ColVoIP.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColVoIP.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColNextEvent
            // 
            resources.ApplyResources(this.ColNextEvent, "ColNextEvent");
            this.ColNextEvent.MaxInputLength = 128;
            this.ColNextEvent.Name = "ColNextEvent";
            this.ColNextEvent.ReadOnly = true;
            this.ColNextEvent.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColNextEvent.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Button_Refresh
            // 
            resources.ApplyResources(this.Button_Refresh, "Button_Refresh");
            this.Button_Refresh.BackColor = System.Drawing.SystemColors.ControlLight;
            this.Button_Refresh.Name = "Button_Refresh";
            this.Button_Refresh.UseVisualStyleBackColor = false;
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
            // HubsForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.DataGrid_Hub);
            this.Controls.Add(this.DataGrid_HubList);
            this.Name = "HubsForm";
            this.Activated += new System.EventHandler(this.HubsForm_Activated);
            this.Deactivate += new System.EventHandler(this.HubsForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.HubsForm_FormClosing);
            this.Load += new System.EventHandler(this.HubsForm_Load);
            this.ResizeEnd += new System.EventHandler(this.HubsForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.HubsForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.HubsForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_HubList)).EndInit();
            this.Context_Hub.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_Hub)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_HubList;
        private System.Windows.Forms.DataGridView DataGrid_Hub;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ContextMenuStrip Context_Hub;
        private System.Windows.Forms.ToolStripMenuItem Context_Hub_Join;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem Context_Hub_Offline;
        private System.Windows.Forms.ToolStripMenuItem Context_Hub_Ignored;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColHubName;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColUsers;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAircraft;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAtc;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColSave;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColIgnore;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColVersion;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAbout;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColVoIP;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColNextEvent;
    }
}