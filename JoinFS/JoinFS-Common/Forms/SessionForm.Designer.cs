namespace JoinFS
{
    partial class SessionForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SessionForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle7 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle8 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle9 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle10 = new System.Windows.Forms.DataGridViewCellStyle();
            this.DataGrid_UserList = new System.Windows.Forms.DataGridView();
            this.ColNickname = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColCallsign = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColConnected = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColLatency = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColSave = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColIgnore = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.ColPermissions = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColAircraft = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColObjects = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColPort = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColVersion = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColSimulator = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Context_User = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_User_Permissions = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Context_User_Cockpit = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_User_Multiple = new System.Windows.Forms.ToolStripMenuItem();
            this.Button_Refresh = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.Text_Receive = new System.Windows.Forms.RichTextBox();
            this.Context_Chat = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Context_Chat_TextColour = new System.Windows.Forms.ToolStripMenuItem();
            this.Context_Chat_BackgroundColour = new System.Windows.Forms.ToolStripMenuItem();
            this.Text_Transmit = new System.Windows.Forms.TextBox();
            this.Button_Send = new System.Windows.Forms.Button();
            this.Check_Chat = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_UserList)).BeginInit();
            this.Context_User.SuspendLayout();
            this.Context_Chat.SuspendLayout();
            this.SuspendLayout();
            // 
            // DataGrid_UserList
            // 
            resources.ApplyResources(this.DataGrid_UserList, "DataGrid_UserList");
            this.DataGrid_UserList.AllowUserToAddRows = false;
            this.DataGrid_UserList.AllowUserToDeleteRows = false;
            this.DataGrid_UserList.AllowUserToResizeColumns = false;
            this.DataGrid_UserList.AllowUserToResizeRows = false;
            this.DataGrid_UserList.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGrid_UserList.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGrid_UserList.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColNickname,
            this.ColCallsign,
            this.ColConnected,
            this.ColLatency,
            this.ColSave,
            this.ColIgnore,
            this.ColPermissions,
            this.ColAircraft,
            this.ColObjects,
            this.ColPort,
            this.ColVersion,
            this.ColSimulator});
            this.DataGrid_UserList.ContextMenuStrip = this.Context_User;
            this.DataGrid_UserList.MultiSelect = false;
            this.DataGrid_UserList.Name = "DataGrid_UserList";
            this.DataGrid_UserList.RowHeadersVisible = false;
            this.DataGrid_UserList.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.DataGrid_UserList.ShowCellToolTips = false;
            this.DataGrid_UserList.ShowEditingIcon = false;
            this.DataGrid_UserList.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGrid_UserList_CellClick);
            // 
            // ColNickname
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColNickname.DefaultCellStyle = dataGridViewCellStyle1;
            resources.ApplyResources(this.ColNickname, "ColNickname");
            this.ColNickname.MaxInputLength = 50;
            this.ColNickname.Name = "ColNickname";
            this.ColNickname.ReadOnly = true;
            this.ColNickname.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColNickname.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColCallsign
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColCallsign.DefaultCellStyle = dataGridViewCellStyle2;
            resources.ApplyResources(this.ColCallsign, "ColCallsign");
            this.ColCallsign.MaxInputLength = 10;
            this.ColCallsign.Name = "ColCallsign";
            this.ColCallsign.ReadOnly = true;
            this.ColCallsign.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColCallsign.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColConnected
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColConnected.DefaultCellStyle = dataGridViewCellStyle3;
            resources.ApplyResources(this.ColConnected, "ColConnected");
            this.ColConnected.MaxInputLength = 5;
            this.ColConnected.Name = "ColConnected";
            this.ColConnected.ReadOnly = true;
            this.ColConnected.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColConnected.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColLatency
            // 
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.ColLatency.DefaultCellStyle = dataGridViewCellStyle4;
            resources.ApplyResources(this.ColLatency, "ColLatency");
            this.ColLatency.MaxInputLength = 10;
            this.ColLatency.Name = "ColLatency";
            this.ColLatency.ReadOnly = true;
            this.ColLatency.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColLatency.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
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
            // ColPermissions
            // 
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColPermissions.DefaultCellStyle = dataGridViewCellStyle5;
            resources.ApplyResources(this.ColPermissions, "ColPermissions");
            this.ColPermissions.MaxInputLength = 10;
            this.ColPermissions.Name = "ColPermissions";
            this.ColPermissions.ReadOnly = true;
            this.ColPermissions.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColPermissions.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColAircraft
            // 
            dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColAircraft.DefaultCellStyle = dataGridViewCellStyle6;
            resources.ApplyResources(this.ColAircraft, "ColAircraft");
            this.ColAircraft.MaxInputLength = 10;
            this.ColAircraft.Name = "ColAircraft";
            this.ColAircraft.ReadOnly = true;
            this.ColAircraft.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColAircraft.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColObjects
            // 
            dataGridViewCellStyle7.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColObjects.DefaultCellStyle = dataGridViewCellStyle7;
            resources.ApplyResources(this.ColObjects, "ColObjects");
            this.ColObjects.MaxInputLength = 10;
            this.ColObjects.Name = "ColObjects";
            this.ColObjects.ReadOnly = true;
            this.ColObjects.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColObjects.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColPort
            // 
            dataGridViewCellStyle8.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColPort.DefaultCellStyle = dataGridViewCellStyle8;
            resources.ApplyResources(this.ColPort, "ColPort");
            this.ColPort.MaxInputLength = 5;
            this.ColPort.Name = "ColPort";
            this.ColPort.ReadOnly = true;
            this.ColPort.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColPort.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColVersion
            // 
            dataGridViewCellStyle9.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.ColVersion.DefaultCellStyle = dataGridViewCellStyle9;
            resources.ApplyResources(this.ColVersion, "ColVersion");
            this.ColVersion.MaxInputLength = 10;
            this.ColVersion.Name = "ColVersion";
            this.ColVersion.ReadOnly = true;
            this.ColVersion.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColVersion.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // ColSimulator
            // 
            dataGridViewCellStyle10.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.ColSimulator.DefaultCellStyle = dataGridViewCellStyle10;
            resources.ApplyResources(this.ColSimulator, "ColSimulator");
            this.ColSimulator.MaxInputLength = 64;
            this.ColSimulator.Name = "ColSimulator";
            this.ColSimulator.ReadOnly = true;
            this.ColSimulator.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ColSimulator.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Context_User
            // 
            resources.ApplyResources(this.Context_User, "Context_User");
            this.Context_User.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_User_Permissions,
            this.toolStripSeparator1,
            this.Context_User_Cockpit,
            this.Context_User_Multiple});
            this.Context_User.Name = "Context_User";
            this.Context_User.Opening += new System.ComponentModel.CancelEventHandler(this.Context_User_Opening);
            // 
            // Context_User_Permissions
            // 
            resources.ApplyResources(this.Context_User_Permissions, "Context_User_Permissions");
            this.Context_User_Permissions.Name = "Context_User_Permissions";
            this.Context_User_Permissions.Click += new System.EventHandler(this.Context_User_Permissions_Click);
            // 
            // toolStripSeparator1
            // 
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // Context_User_Cockpit
            // 
            resources.ApplyResources(this.Context_User_Cockpit, "Context_User_Cockpit");
            this.Context_User_Cockpit.Name = "Context_User_Cockpit";
            this.Context_User_Cockpit.Click += new System.EventHandler(this.Context_User_Cockpit_Click);
            // 
            // Context_User_Multiple
            // 
            resources.ApplyResources(this.Context_User_Multiple, "Context_User_Multiple");
            this.Context_User_Multiple.Name = "Context_User_Multiple";
            this.Context_User_Multiple.Click += new System.EventHandler(this.Context_User_Multiple_Click);
            // 
            // Button_Refresh
            // 
            resources.ApplyResources(this.Button_Refresh, "Button_Refresh");
            this.Button_Refresh.Name = "Button_Refresh";
            this.Button_Refresh.UseVisualStyleBackColor = true;
            this.Button_Refresh.Click += new System.EventHandler(this.Button_Refresh_Click);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // Text_Receive
            // 
            resources.ApplyResources(this.Text_Receive, "Text_Receive");
            this.Text_Receive.BackColor = System.Drawing.SystemColors.Window;
            this.Text_Receive.ContextMenuStrip = this.Context_Chat;
            this.Text_Receive.ForeColor = System.Drawing.SystemColors.WindowText;
            this.Text_Receive.Name = "Text_Receive";
            this.Text_Receive.ReadOnly = true;
            // 
            // Context_Chat
            // 
            resources.ApplyResources(this.Context_Chat, "Context_Chat");
            this.Context_Chat.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Context_Chat_TextColour,
            this.Context_Chat_BackgroundColour});
            this.Context_Chat.Name = "Context_Chat";
            // 
            // Context_Chat_TextColour
            // 
            resources.ApplyResources(this.Context_Chat_TextColour, "Context_Chat_TextColour");
            this.Context_Chat_TextColour.Name = "Context_Chat_TextColour";
            this.Context_Chat_TextColour.Click += new System.EventHandler(this.Context_Chat_TextColour_Click);
            // 
            // Context_Chat_BackgroundColour
            // 
            resources.ApplyResources(this.Context_Chat_BackgroundColour, "Context_Chat_BackgroundColour");
            this.Context_Chat_BackgroundColour.Name = "Context_Chat_BackgroundColour";
            this.Context_Chat_BackgroundColour.Click += new System.EventHandler(this.Context_Chat_BackgroundColour_Click);
            // 
            // Text_Transmit
            // 
            resources.ApplyResources(this.Text_Transmit, "Text_Transmit");
            this.Text_Transmit.BackColor = System.Drawing.SystemColors.Window;
            this.Text_Transmit.ForeColor = System.Drawing.SystemColors.WindowText;
            this.Text_Transmit.Name = "Text_Transmit";
            // 
            // Button_Send
            // 
            resources.ApplyResources(this.Button_Send, "Button_Send");
            this.Button_Send.Name = "Button_Send";
            this.Button_Send.UseVisualStyleBackColor = true;
            this.Button_Send.Click += new System.EventHandler(this.Button_Send_Click);
            // 
            // Check_Chat
            // 
            resources.ApplyResources(this.Check_Chat, "Check_Chat");
            this.Check_Chat.Name = "Check_Chat";
            this.Check_Chat.UseVisualStyleBackColor = true;
            this.Check_Chat.CheckedChanged += new System.EventHandler(this.Check_Chat_CheckedChanged);
            // 
            // SessionForm
            // 
            this.AcceptButton = this.Button_Send;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Check_Chat);
            this.Controls.Add(this.Button_Send);
            this.Controls.Add(this.Text_Transmit);
            this.Controls.Add(this.Text_Receive);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Button_Refresh);
            this.Controls.Add(this.DataGrid_UserList);
            this.KeyPreview = true;
            this.Name = "SessionForm";
            this.Activated += new System.EventHandler(this.SessionForm_Activated);
            this.Deactivate += new System.EventHandler(this.SessionForm_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SessionForm_FormClosing);
            this.Load += new System.EventHandler(this.SessionForm_Load);
            this.ResizeEnd += new System.EventHandler(this.SessionForm_ResizeEnd);
            this.VisibleChanged += new System.EventHandler(this.SessionForm_VisibleChanged);
            this.Resize += new System.EventHandler(this.SessionForm_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.DataGrid_UserList)).EndInit();
            this.Context_User.ResumeLayout(false);
            this.Context_Chat.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGrid_UserList;
        private System.Windows.Forms.Button Button_Refresh;
        private System.Windows.Forms.ContextMenuStrip Context_User;
        private System.Windows.Forms.ToolStripMenuItem Context_User_Permissions;
        private System.Windows.Forms.ToolStripMenuItem Context_User_Cockpit;
        private System.Windows.Forms.ToolStripMenuItem Context_User_Multiple;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.RichTextBox Text_Receive;
        private System.Windows.Forms.TextBox Text_Transmit;
        private System.Windows.Forms.Button Button_Send;
        private System.Windows.Forms.CheckBox Check_Chat;
        private System.Windows.Forms.ContextMenuStrip Context_Chat;
        private System.Windows.Forms.ToolStripMenuItem Context_Chat_TextColour;
        private System.Windows.Forms.ToolStripMenuItem Context_Chat_BackgroundColour;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColNickname;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColCallsign;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColConnected;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColLatency;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColSave;
        private System.Windows.Forms.DataGridViewCheckBoxColumn ColIgnore;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColPermissions;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColAircraft;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColObjects;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColPort;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColVersion;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColSimulator;
    }
}